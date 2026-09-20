#include "AlsStopGraphCommandlet.h"
#include "AlphaBlend.h"
#include "Animation/BlendProfile.h"
#include "Animation/Skeleton.h"
#include "Curves/CurveFloat.h"

#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/AnimInertializationSyncScope.h"
#include "Components/SkeletalMeshComponent.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "Animation/AnimSequenceBase.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "Animation/BlendSpace.h"
#include "AnimNodes/AnimNode_BlendSpacePlayer.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "Animation/AnimStateMachineTypes.h"
#include "Dom/JsonObject.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "Engine/Blueprint.h"
#include "JsonObjectConverter.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UObject/UnrealType.h"
#include "UObject/StructOnScope.h"

namespace AlsPoseCacheProbe
{
struct FSource : FAnimNode_Base
{
    int32 Updates = 0;
    float Weight = 0.f;
    float Delta = 0.f;
    float RootMotion = 0.f;
    bool InertialSync = false;
    virtual void Update_AnyThread(const FAnimationUpdateContext& Context) override
    {
        ++Updates;
        Weight = Context.GetFinalBlendWeight();
        Delta = Context.GetDeltaTime();
        RootMotion = Context.GetRootMotionWeightModifier();
        InertialSync = Context.GetSharedContext() && Context.GetMessage<UE::Anim::FAnimInertializationSyncScope>() != nullptr;
        if (const auto Requester = Context.GetSharedContext() ? Context.GetMessage<UE::Anim::IInertializationRequester>() : nullptr)
        {
            FInertializationRequest Request;
            Request.Duration = .2f;
            Requester->RequestInertialization(Request);
        }
    }
};

struct FInertial : FAnimNode_Inertialization
{
    int32 Requests = 0;
    virtual void RequestInertialization(const FInertializationRequest& Request) override
    {
        ++Requests;
        FAnimNode_Inertialization::RequestInertialization(Request);
    }
};

TArray<TSharedPtr<FJsonValue>> Observe()
{
    TArray<TSharedPtr<FJsonValue>> Cases;
    const float Weights[][3] = {{.2f, .8f, .4f}, {.7f, .7f, .1f}, {0, 0, 0}, {.3f, .1f, .9f},
        {.000001f, .000002f, 0}, {.5f, .2f, .5f}, {1, 1, 1}, {.4f, .4f, .4f}};
    for (const int32 Hz : {30, 60, 120})
    for (int32 Scenario = 0; Scenario < UE_ARRAY_COUNT(Weights); ++Scenario)
    {
        auto Component = NewObject<USkeletalMeshComponent>();
        auto Instance = NewObject<UAnimInstance>(Component);
        FAnimInstanceProxy Proxy(Instance);
        FSource Source;
        FAnimNode_SaveCachedPose Cache;
        Cache.Pose.SetLinkNode(&Source);
        FInertial Inertial[3];
        TArray<TSharedPtr<FJsonValue>> Calls;
        for (int32 Call = 0; Call < 3; ++Call)
        {
            const bool SharedEnabled = !(Scenario == 3 && Call == 2) && !(Scenario == 7 && Call == 1);
            const bool SyncEnabled = Call != 1;
            const bool HandlerEnabled = !(Scenario == 5 && Call == 0);
            const float Delta = Scenario == 6 ? 0.f : (Call + 1.f) / Hz;
            const float RootMotion = .25f * (Call + 1);
            FAnimationUpdateSharedContext Shared;
            auto Context = FAnimationUpdateContext(&Proxy, Delta, SharedEnabled ? &Shared : nullptr)
                .FractionalWeightAndRootMotion(Weights[Scenario][Call], RootMotion);
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("weight"), Weights[Scenario][Call]);
            Row->SetNumberField(TEXT("delta"), Delta);
            Row->SetNumberField(TEXT("rootMotion"), RootMotion);
            Row->SetBoolField(TEXT("shared"), SharedEnabled);
            Row->SetBoolField(TEXT("sync"), SharedEnabled && SyncEnabled);
            Row->SetBoolField(TEXT("handler"), SharedEnabled && HandlerEnabled);
            Calls.Add(MakeShared<FJsonValueObject>(Row));
            if (SharedEnabled)
            {
                UE::Anim::TOptionalScopedGraphMessage<UE::Anim::FAnimInertializationSyncScope> Sync(SyncEnabled, Context);
                Inertial[Call].Source.SetLinkNode(&Cache);
                const auto Forward = FindFProperty<FBoolProperty>(FAnimNode_Inertialization::StaticStruct(),
                    TEXT("bForwardRequestsThroughSkippedCachedPoseNodes"));
                check(Forward);
                Forward->SetPropertyValue_InContainer(&Inertial[Call], HandlerEnabled);
                Inertial[Call].Update_AnyThread(Context);
            }
            else Cache.Update_AnyThread(Context);
        }
        Cache.PostGraphUpdate();
        Cache.PostGraphUpdate();
        const auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("hz"), Hz);
        Row->SetNumberField(TEXT("scenario"), Scenario);
        Row->SetArrayField(TEXT("calls"), Calls);
        Row->SetNumberField(TEXT("updates"), Source.Updates);
        Row->SetNumberField(TEXT("weight"), Source.Weight);
        Row->SetNumberField(TEXT("delta"), Source.Delta);
        Row->SetNumberField(TEXT("rootMotion"), Source.RootMotion);
        Row->SetBoolField(TEXT("sync"), Source.InertialSync);
        TArray<TSharedPtr<FJsonValue>> Requests;
        for (const auto& Node : Inertial) Requests.Add(MakeShared<FJsonValueNumber>(Node.Requests));
        Row->SetArrayField(TEXT("inertialRequests"), Requests);
        Cases.Add(MakeShared<FJsonValueObject>(Row));
    }
    return Cases;
}
}

UAlsStopGraphCommandlet::UAlsStopGraphCommandlet()
{
    IsClient = false;
    IsServer = false;
    IsEditor = true;
    LogToConsole = true;
}

int32 UAlsStopGraphCommandlet::Main(const FString& Params)
{
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    const bool IncludeCaches = FParse::Param(*Params, TEXT("IncludeCaches"));
    const bool IncludeInventory = IncludeCaches || FParse::Param(*Params, TEXT("IncludeInventory"));
    const bool IncludeRuntimeInputs = FParse::Param(*Params, TEXT("IncludeRuntimeInputs"));
    const bool IncludeInputs = IncludeRuntimeInputs || FParse::Param(*Params, TEXT("IncludeInputs"));
    const bool IncludeMovement = FParse::Param(*Params, TEXT("IncludeMovement"));
    const bool IncludeGrounded = IncludeMovement || FParse::Param(*Params, TEXT("IncludeGrounded"));
    const bool IncludeMain = IncludeGrounded || FParse::Param(*Params, TEXT("IncludeMain"));
    const bool IncludeCycle = IncludeMain || IncludeInputs || IncludeInventory || FParse::Param(*Params, TEXT("IncludeCycle"));
    const bool IncludeDetail = IncludeCycle || FParse::Param(*Params, TEXT("IncludeDetail"));
    UBlueprint* Blueprint = LoadObject<UBlueprint>(nullptr,
        TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"));
    if (!Blueprint) return 2;

    // Native reflection includes private folded animation properties and default values
    // that Python editor properties and differential T3D exports can omit.
    const FJsonObjectConverter::CustomExportCallback ExportObject =
        FJsonObjectConverter::CustomExportCallback::CreateLambda([](FProperty* Property, const void* Value)
        -> TSharedPtr<FJsonValue>
        {
            if (const FObjectPropertyBase* ObjectProperty = CastField<FObjectPropertyBase>(Property))
            {
                const UObject* Object = ObjectProperty->GetObjectPropertyValue(Value);
                return MakeShared<FJsonValueString>(Object ? Object->GetPathName() : TEXT(""));
            }
            return nullptr;
        });
    const TCHAR* PropertyNames[] = {
        TEXT("Node"), TEXT("BlendNode"), TEXT("BoundGraph"), TEXT("EditorStateMachineGraph"), TEXT("CustomTransitionGraph"),
        TEXT("PriorityOrder"), TEXT("CrossfadeDuration"), TEXT("BlendMode"), TEXT("LogicType"),
        TEXT("CustomBlendCurve"), TEXT("BlendProfileWrapper"), TEXT("bSharedRules"), TEXT("SharedRulesName"),
        TEXT("TransitionStart"), TEXT("TransitionEnd"), TEXT("TransitionInterrupt"),
        TEXT("StateEntered"), TEXT("StateLeft"), TEXT("StateFullyBlended"), TEXT("bAlwaysResetOnEntry"),
        TEXT("BoundEnum"), TEXT("VisibleEnumEntries"), TEXT("NameOfCache"), TEXT("FunctionReference"),
        TEXT("VariableReference"), TEXT("bAutomaticRuleBasedOnSequencePlayerInState"),
        TEXT("bDisabled"), TEXT("MinTimeBeforeReentry")};
    TArray<UEdGraph*> AllGraphs;
    Blueprint->GetAllGraphs(AllGraphs);
    // Close the rate/stride curve macro dependency using reflected graph references.
    // GetGraph resolves renamed/cached references without mutating or saving the asset.
    TSet<const UEdGraph*> InputMacros;
    if (IncludeRuntimeInputs)
    {
        TArray<UEdGraph*> Pending;
        for (UEdGraph* Graph : AllGraphs)
            if (Graph->GetFName() == TEXT("GetAnimCurve_Clamped") && Graph->GetOuter() == Blueprint) Pending.Add(Graph);
        if (Pending.Num() != 1) return 46;
        for (const UEdGraph* Graph : AllGraphs)
            if (Graph->GetFName() == TEXT("UpdateGraph") && Graph->GetOuter() == Blueprint)
                for (const UEdGraphNode* Node : Graph->Nodes)
                    if (const auto* Property = FindFProperty<FStructProperty>(Node->GetClass(), TEXT("MacroGraphReference")))
                    {
                        if (Property->Struct != FGraphReference::StaticStruct()) return 47;
                        UEdGraph* Dependency = Property->ContainerPtrToValuePtr<FGraphReference>(Node)->GetGraph();
                        if (!Dependency) return 48;
                        Pending.AddUnique(Dependency);
                    }
        for (int32 Index = 0; Index < Pending.Num(); ++Index)
        {
            UEdGraph* Graph = Pending[Index];
            if (InputMacros.Contains(Graph)) continue;
            InputMacros.Add(Graph); AllGraphs.AddUnique(Graph);
            for (const UEdGraphNode* Node : Graph->Nodes)
                if (const auto* Property = FindFProperty<FStructProperty>(Node->GetClass(), TEXT("MacroGraphReference")))
                {
                    if (Property->Struct != FGraphReference::StaticStruct()) return 47;
                    const auto* Reference = Property->ContainerPtrToValuePtr<FGraphReference>(Node);
                    UEdGraph* Dependency = Reference->GetGraph();
                    if (!Dependency) return 48;
                    Pending.AddUnique(Dependency);
                }
        }
    }
    UBlueprint* CharacterBlueprint = nullptr;
    if (IncludeInputs)
    {
        CharacterBlueprint = LoadObject<UBlueprint>(nullptr,
            TEXT("/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP"));
        if (!CharacterBlueprint || !CharacterBlueprint->GeneratedClass) return 22;
        TArray<UEdGraph*> CharacterGraphs;
        CharacterBlueprint->GetAllGraphs(CharacterGraphs);
        for (UEdGraph* Graph : CharacterGraphs)
            if (Graph->GetFName() == TEXT("SetEssentialValues") || Graph->GetFName() == TEXT("BPI_Get_EssentialValues"))
                AllGraphs.Add(Graph);
        if (AllGraphs.Num() == 0) return 22;
    }
    AllGraphs.Sort([](const UEdGraph& A, const UEdGraph& B) { return A.GetPathName() < B.GetPathName(); });
    TArray<TSharedPtr<FJsonValue>> Graphs;
    int32 PlantEvaluators = 0;
    int32 DetailPlayers = 0;
    int32 CyclePlayers = 0;
    int32 CycleSequences = 0;
    int32 CharacterInputGraphs = 0;
    int32 RuntimeInputGraphs = 0;
    const TSet<FName> RuntimeInputNames = {
        TEXT("UpdateMovementValues"), TEXT("UpdateInAirValues"), TEXT("CalculateVelocityBlend"),
        TEXT("CalculateDiagonalScaleAmount"), TEXT("CalculateRelativeAccelerationAmount"),
        TEXT("CalculateWalkRunBlend"), TEXT("CalculateStrideBlend"), TEXT("CalculateStandingPlayRate"),
        TEXT("CalculateCrouchingPlayRate"), TEXT("CalculateLandPrediction"), TEXT("CalculateInAirLeanAmount"),
        TEXT("InterpVelocityBlend"), TEXT("InterpLeanAmount"), TEXT("EventGraph"), TEXT("UpdateGraph")};
    int32 MainPlayers = 0;
    int32 MainEvaluators = 0;
    int32 CrouchingPlayers = 0, CrouchingEvaluators = 0, CrouchingSpaces = 0;
    int32 MovementGraphs = 0;
    TMap<FString, const UAnimSequence*> SyncSequences;
    for (const UEdGraph* Graph : AllGraphs)
    {
        const bool IsDetail = Graph->GetPathName().Contains(TEXT(".(N) Locomotion Detail"));
        const bool IsCycle = Graph->GetPathName().Contains(TEXT(".(N) Locomotion Cycles")) ||
            Graph->GetPathName().Contains(TEXT(":(N) CycleBlending"));
        const bool IsStandingSource = Graph->GetFName() == TEXT("(N) Rotate Left 90") ||
            Graph->GetFName() == TEXT("(N) Rotate Right 90");
        const bool IsDetailDependency = IsDetail || Graph->GetFName() == TEXT("BaseLayer") ||
            Graph->GetFName() == TEXT("UpdateCharacterInfo") || Graph->GetFName() == TEXT("UpdateMovementValues") ||
            Graph->GetFName() == TEXT("EventGraph");
        const bool IsCharacterInput = CharacterBlueprint && Graph->IsIn(CharacterBlueprint);
        const bool IsMainSource = Graph->GetPathName().Contains(TEXT(".Main Grounded States"));
        const bool IsMovement = Graph->GetPathName().Contains(TEXT(".Main Movement States"));
        const bool IsCrouching = Graph->GetPathName().Contains(TEXT(".(CLF) Locomotion")) ||
            Graph->GetPathName().Contains(TEXT(":(CLF) CycleBlending"));
        const bool IsGroundedInput = (IncludeInputs || IncludeMain) && IsMainSource;
        const bool IsRuntimeInput = IncludeRuntimeInputs && RuntimeInputNames.Contains(Graph->GetFName()) && Graph->GetOuter() == Blueprint;
        if (!Graph->GetPathName().Contains(TEXT(".(N) Locomotion States")) &&
            Graph->GetFName() != TEXT("ShouldMoveCheck") && !(IncludeDetail && IsDetailDependency) &&
            !(IncludeCycle && IsCycle) && !IsCharacterInput && !IsGroundedInput && !(IncludeGrounded && IsCrouching) &&
            !(IncludeMovement && IsMovement) && !IsRuntimeInput && !InputMacros.Contains(Graph)) continue;
        if (IsRuntimeInput) ++RuntimeInputGraphs;
        if (IsCharacterInput) ++CharacterInputGraphs;
        if (IsMovement) ++MovementGraphs;
        const auto GraphJson = MakeShared<FJsonObject>();
        GraphJson->SetStringField(TEXT("path"), Graph->GetPathName());
        GraphJson->SetStringField(TEXT("name"), Graph->GetName());
        TArray<TSharedPtr<FJsonValue>> Nodes;
        for (const UEdGraphNode* Node : Graph->Nodes)
        {
            const auto NodeJson = MakeShared<FJsonObject>();
            NodeJson->SetStringField(TEXT("name"), Node->GetName());
            NodeJson->SetStringField(TEXT("class"), Node->GetClass()->GetName());
            if (((IncludeCycle && IsCycle) || (IncludeGrounded && IsCrouching) || (IncludeMovement && IsMovement)) &&
                Node->GetClass()->GetFName() == TEXT("AnimGraphNode_BlendSpacePlayer"))
            {
                const auto Generated = Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass);
                const int32* Index = Generated ? Generated->GetNodePropertyIndexFromGuid(Node->NodeGuid) : nullptr;
                const auto Player = Generated ? Generated->GetPropertyInstance<FAnimNode_BlendSpacePlayer>(
                    Generated->GetDefaultObject(), Node->NodeGuid) : nullptr;
                UBlendSpace* Space = Player ? Player->GetBlendSpace() : nullptr;
                if (!Index || !Space) return 12;
                NodeJson->SetNumberField(TEXT("compiledNodeIndex"), *Index);
                const auto Runtime = MakeShared<FJsonObject>();
                Runtime->SetStringField(TEXT("assetObjectPath"), Space->GetPathName());
                Runtime->SetStringField(TEXT("groupName"), Player->GetGroupName().ToString());
                Runtime->SetNumberField(TEXT("groupRole"), Player->GetGroupRole());
                Runtime->SetNumberField(TEXT("groupMethod"), static_cast<int32>(Player->GetGroupMethod()));
                Runtime->SetNumberField(TEXT("startPosition"), Player->GetStartPosition());
                Runtime->SetNumberField(TEXT("playRate"), Player->GetPlayRate());
                Runtime->SetBoolField(TEXT("loop"), Player->IsLooping());
                Runtime->SetBoolField(TEXT("resetOnAssetChange"), Player->ShouldResetPlayTimeWhenBlendSpaceChanges());
                Runtime->SetBoolField(TEXT("evaluator"), Player->IsEvaluator());
                Runtime->SetBoolField(TEXT("bUseLegacySamplePointAnimationLengthCalculations"), Space->bUseLegacySamplePointAnimationLengthCalculations);
                Runtime->SetBoolField(TEXT("bShouldMatchSyncPhases"), Space->bShouldMatchSyncPhases);
                TArray<TSharedPtr<FJsonValue>> SyncMarkerNames;
                if (const auto Names = Space->GetUniqueMarkerNames())
                    for (const auto Name : *Names) SyncMarkerNames.Add(MakeShared<FJsonValueString>(Name.ToString()));
                Runtime->SetArrayField(TEXT("syncMarkerNames"), SyncMarkerNames);
                const TCHAR* SpaceProperties[] = {TEXT("NotifyTriggerMode"), TEXT("bAllowMarkerBasedSync"),
                    TEXT("TargetWeightInterpolationSpeedPerSec"), TEXT("bInterpolateUsingGrid"), TEXT("AxisToScaleAnimation")};
                for (const TCHAR* Name : SpaceProperties)
                {
                    FProperty* Property = Space->GetClass()->FindPropertyByName(Name);
                    if (!Property)
                    {
                        UE_LOG(LogTemp, Error, TEXT("Missing BlendSpace property %s on %s"), Name, *Space->GetPathName());
                        return 13;
                    }
                    const auto Value = FJsonObjectConverter::UPropertyToJsonValue(Property,
                        Property->ContainerPtrToValuePtr<void>(Space), 0, 0, &ExportObject);
                    if (!Value.IsValid()) return 13;
                    Runtime->SetField(Name, Value);
                }
                TArray<TSharedPtr<FJsonValue>> Samples;
                for (const FBlendSample& Sample : Space->GetBlendSamples())
                {
                    const auto SampleJson = MakeShared<FJsonObject>();
                    if (!Sample.Animation || !FJsonObjectConverter::UStructToJsonObject(FBlendSample::StaticStruct(),
                        &Sample, SampleJson, 0, 0, &ExportObject)) return 14;
                    SampleJson->SetNumberField(TEXT("sourceIndex"), Samples.Num());
                    SampleJson->SetNumberField(TEXT("assetRateScale"), Sample.Animation->RateScale);
                    SampleJson->SetNumberField(TEXT("playLength"), Sample.Animation->GetPlayLength());
                    SyncSequences.Add(Sample.Animation->GetPathName(), Sample.Animation);
                    Samples.Add(MakeShared<FJsonValueObject>(SampleJson));
                }
                Runtime->SetArrayField(TEXT("samples"), Samples);
                NodeJson->SetObjectField(TEXT("runtimePlayer"), Runtime);
                if (IsCycle) ++CyclePlayers;
                if (IsCrouching) ++CrouchingSpaces;
            }
            if (((IncludeDetail && IsDetail) || (IncludeCycle && (IsCycle || IsStandingSource)) || (IncludeMain && IsMainSource) ||
                (IncludeGrounded && IsCrouching) || (IncludeMovement && IsMovement)) &&
                Node->GetClass()->GetFName() == TEXT("AnimGraphNode_SequencePlayer"))
            {
                const auto Generated = Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass);
                const int32* Index = Generated ? Generated->GetNodePropertyIndexFromGuid(Node->NodeGuid) : nullptr;
                if (!Index) return 10;
                NodeJson->SetNumberField(TEXT("compiledNodeIndex"), *Index);
                const auto Player = Generated->GetPropertyInstance<FAnimNode_SequencePlayer>(Generated->GetDefaultObject(), Node->NodeGuid);
                const auto Sequence = Player ? Player->GetSequence() : nullptr;
                if (!Sequence) return 11;
                const auto Markers = Sequence->GetUniqueMarkerNames();
                NodeJson->SetStringField(TEXT("assetObjectPath"), Sequence->GetPathName());
                NodeJson->SetNumberField(TEXT("assetRateScale"), Sequence->RateScale);
                NodeJson->SetNumberField(TEXT("assetMarkerCount"), Markers ? Markers->Num() : 0);
                if (IncludeCycle) SyncSequences.Add(Sequence->GetPathName(), CastChecked<UAnimSequence>(Sequence));
                if (IncludeCycle && IsCycle) ++CycleSequences;
                if (IncludeMain && IsMainSource) ++MainPlayers;
                if (IncludeGrounded && IsCrouching) ++CrouchingPlayers;
            }
            if (IncludeCycle && Node->GetClass()->GetFName() == TEXT("AnimGraphNode_SequenceEvaluator"))
            {
                const auto Generated = Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass);
                const int32* Index = Generated ? Generated->GetNodePropertyIndexFromGuid(Node->NodeGuid) : nullptr;
                if (!Index) return 16;
                NodeJson->SetNumberField(TEXT("compiledNodeIndex"), *Index);
                if (IncludeMain && IsMainSource) ++MainEvaluators;
                if (IncludeGrounded && IsCrouching) ++CrouchingEvaluators;
            }
            const auto Properties = MakeShared<FJsonObject>();
            if (IncludeRuntimeInputs)
                if (auto* Property = FindFProperty<FStructProperty>(Node->GetClass(), TEXT("MacroGraphReference")))
                {
                    if (Property->Struct != FGraphReference::StaticStruct()) return 47;
                    const auto* Reference = Property->ContainerPtrToValuePtr<FGraphReference>(Node);
                    if (!Reference->GetGraph()) return 48;
                    const auto Value = FJsonObjectConverter::UPropertyToJsonValue(Property,
                        Reference, 0, 0, &ExportObject);
                    if (!Value.IsValid()) return 49;
                    Properties->SetField(TEXT("MacroGraphReference"), Value);
                }
            for (const TCHAR* Name : PropertyNames)
            {
                if (FProperty* Property = Node->GetClass()->FindPropertyByName(Name))
                {
                    const auto Value = FJsonObjectConverter::UPropertyToJsonValue(Property,
                        Property->ContainerPtrToValuePtr<void>(Node), 0, 0, &ExportObject);
                    if (!Value.IsValid()) return 3;
                    Properties->SetField(Name, Value);
                }
            }
            if (IncludeDetail)
            {
                const TCHAR* GetterProperties[] = {TEXT("GetterType"), TEXT("AssociatedAnimAssetPlayerNode"),
                    TEXT("AssociatedStateNode"), TEXT("SourceNode"), TEXT("SourceStateNode"), TEXT("CustomFunctionName"),
                    TEXT("EventReference"), TEXT("bOverrideFunction")};
                for (const TCHAR* Name : GetterProperties)
                    if (FProperty* Property = Node->GetClass()->FindPropertyByName(Name))
                    {
                        const auto Value = FJsonObjectConverter::UPropertyToJsonValue(Property,
                            Property->ContainerPtrToValuePtr<void>(Node), 0, 0, &ExportObject);
                        if (!Value.IsValid()) return 3;
                        Properties->SetField(Name, Value);
                    }
            }
            if (FObjectPropertyBase* EnumProperty = FindFProperty<FObjectPropertyBase>(Node->GetClass(), TEXT("BoundEnum")))
            {
                if (const UEnum* Enum = Cast<UEnum>(EnumProperty->GetObjectPropertyValue_InContainer(Node)))
                {
                    TArray<TSharedPtr<FJsonValue>> Entries;
                    for (int32 Index = 0; Index < Enum->NumEnums(); ++Index)
                    {
                        const auto Entry = MakeShared<FJsonObject>();
                        Entry->SetStringField(TEXT("name"), Enum->GetNameStringByIndex(Index));
                        Entry->SetStringField(TEXT("label"), Enum->GetDisplayNameTextByIndex(Index).ToString());
                        Entry->SetNumberField(TEXT("value"), Enum->GetValueByIndex(Index));
                        Entries.Add(MakeShared<FJsonValueObject>(Entry));
                    }
                    Properties->SetArrayField(TEXT("EnumEntries"), Entries);
                }
            }
            NodeJson->SetObjectField(TEXT("properties"), Properties);
            if (IncludeCycle && Node->GetClass()->GetFName() == TEXT("AnimGraphNode_SequenceEvaluator"))
            {
                const FString Path = Properties->GetObjectField(TEXT("Node"))->GetStringField(TEXT("sequence"));
                const auto Sequence = LoadObject<UAnimSequence>(nullptr, *Path);
                if (!Sequence) return 17;
                SyncSequences.Add(Sequence->GetPathName(), Sequence);
            }
            TArray<TSharedPtr<FJsonValue>> Pins;
            for (const UEdGraphPin* Pin : Node->Pins)
            {
                const auto PinJson = MakeShared<FJsonObject>();
                PinJson->SetStringField(TEXT("name"), Pin->PinName.ToString());
                PinJson->SetStringField(TEXT("direction"), Pin->Direction == EGPD_Input ? TEXT("input") : TEXT("output"));
                PinJson->SetStringField(TEXT("value"), Pin->DefaultValue);
                if (IncludeDetail)
                    if (const UEnum* Enum = Cast<UEnum>(Pin->PinType.PinSubCategoryObject.Get()))
                    {
                        PinJson->SetStringField(TEXT("enumType"), Enum->GetPathName());
                        const int64 EnumValue = Enum->GetValueByNameString(Pin->DefaultValue);
                        if (EnumValue != INDEX_NONE)
                        {
                            PinJson->SetNumberField(TEXT("enumValue"), EnumValue);
                            PinJson->SetStringField(TEXT("enumLabel"), Enum->GetDisplayNameTextByValue(EnumValue).ToString());
                        }
                    }
                TArray<TSharedPtr<FJsonValue>> Links;
                for (const UEdGraphPin* Linked : Pin->LinkedTo)
                {
                    const auto Link = MakeShared<FJsonObject>();
                    Link->SetStringField(TEXT("node"), Linked->GetOwningNode()->GetName());
                    Link->SetStringField(TEXT("pin"), Linked->PinName.ToString());
                    Links.Add(MakeShared<FJsonValueObject>(Link));
                }
                PinJson->SetArrayField(TEXT("links"), Links);
                Pins.Add(MakeShared<FJsonValueObject>(PinJson));
            }
            NodeJson->SetArrayField(TEXT("pins"), Pins);
            Nodes.Add(MakeShared<FJsonValueObject>(NodeJson));
            if ((Graph->GetName() == TEXT("Plant Left Foot") || Graph->GetName() == TEXT("Plant Right Foot")) &&
                Node->GetClass()->GetFName() == TEXT("AnimGraphNode_SequenceEvaluator")) ++PlantEvaluators;
            if (IsDetail && Node->GetClass()->GetFName() == TEXT("AnimGraphNode_SequencePlayer")) ++DetailPlayers;
        }
        GraphJson->SetArrayField(TEXT("nodes"), Nodes);
        Graphs.Add(MakeShared<FJsonValueObject>(GraphJson));
    }
    if (PlantEvaluators != 12) return 4;
    if (IncludeDetail && DetailPlayers != 16) return 6;
    if (IncludeCycle && (CyclePlayers == 0 || CycleSequences == 0)) return 15;
    if (IncludeMain && (MainPlayers != 2 || MainEvaluators != 1)) return 30;
    if (IncludeGrounded && (CrouchingPlayers != 8 || CrouchingEvaluators != 4 || CrouchingSpaces != 1)) return 31;
    const auto Root = MakeShared<FJsonObject>();
    if (IncludeMovement)
    {
        if (MovementGraphs == 0) return 34;
        Root->SetNumberField(TEXT("movementSourceSchemaVersion"), 1);
    }
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), Blueprint->GetPathName());
    if (IncludeDetail) Root->SetStringField(TEXT("scope"), TEXT("Stop and Locomotion Detail with update/event dependencies"));
    if (IncludeCycle)
    {
        Root->SetNumberField(TEXT("standingSourceSchemaVersion"), 1);
        Root->SetStringField(TEXT("scope"), TEXT("Standing Cycle, Stop and Detail source graph"));
        if (IncludeMain)
        {
            Root->SetNumberField(TEXT("mainSourceSchemaVersion"), 1);
            Root->SetStringField(TEXT("scope"), TEXT("Main Grounded, Standing, Cycle, Stop and Detail source graph"));
        }
        Root->SetNumberField(TEXT("syncSchemaVersion"), 1);
        Root->SetNumberField(TEXT("notifySchemaVersion"), 1);
        TArray<FString> Paths;
        SyncSequences.GetKeys(Paths); Paths.Sort();
        TArray<TSharedPtr<FJsonValue>> Assets;
        for (const FString& Path : Paths)
        {
            const auto Sequence = SyncSequences[Path];
            const auto Asset = MakeShared<FJsonObject>();
            Asset->SetStringField(TEXT("path"), Path);
            Asset->SetNumberField(TEXT("length"), Sequence->GetPlayLength());
            Asset->SetNumberField(TEXT("rateScale"), Sequence->RateScale);
            TArray<TSharedPtr<FJsonValue>> Markers;
            for (const auto& Marker : Sequence->AuthoredSyncMarkers)
            {
                const auto Item = MakeShared<FJsonObject>();
                Item->SetNumberField(TEXT("index"), Markers.Num());
                Item->SetStringField(TEXT("name"), Marker.MarkerName.ToString());
                Item->SetNumberField(TEXT("time"), Marker.Time);
                Item->SetNumberField(TEXT("track"), Marker.TrackIndex);
                Markers.Add(MakeShared<FJsonValueObject>(Item));
            }
            Asset->SetArrayField(TEXT("markers"), Markers);
            TArray<TSharedPtr<FJsonValue>> Notifies;
            for (int32 Index = 0; Index < Sequence->Notifies.Num(); ++Index)
            {
                const auto& Notify = Sequence->Notifies[Index];
                const auto Item = MakeShared<FJsonObject>();
                Item->SetNumberField(TEXT("index"), Index);
                Item->SetNumberField(TEXT("track"), Notify.TrackIndex);
                Item->SetStringField(TEXT("name"), Notify.NotifyName.ToString());
                Item->SetStringField(TEXT("notifyObject"), Notify.Notify ? Notify.Notify->GetPathName() : FString());
                Item->SetStringField(TEXT("stateObject"), Notify.NotifyStateClass ? Notify.NotifyStateClass->GetPathName() : FString());
                Item->SetNumberField(TEXT("stateBehaviorFlags"), Notify.NotifyStateClass ? Notify.NotifyStateClass->NotifyStateBehaviorFlags : 0);
                const UObject* Object = Notify.Notify ? static_cast<UObject*>(Notify.Notify) : static_cast<UObject*>(Notify.NotifyStateClass);
                Item->SetStringField(TEXT("class"), Object ? Object->GetClass()->GetPathName() : FString());
                Item->SetNumberField(TEXT("time"), Notify.GetTime());
                Item->SetNumberField(TEXT("duration"), Notify.GetDuration());
                Item->SetNumberField(TEXT("triggerOffset"), Notify.TriggerTimeOffset);
                Item->SetNumberField(TEXT("endTriggerOffset"), Notify.EndTriggerTimeOffset);
                Item->SetNumberField(TEXT("triggerTime"), Notify.GetTriggerTime());
                Item->SetNumberField(TEXT("endTriggerTime"), Notify.GetEndTriggerTime());
                Item->SetNumberField(TEXT("weightThreshold"), Notify.TriggerWeightThreshold);
                Item->SetNumberField(TEXT("chance"), Notify.NotifyTriggerChance);
                Item->SetNumberField(TEXT("filterType"), Notify.NotifyFilterType);
                Item->SetNumberField(TEXT("filterLod"), Notify.NotifyFilterLOD);
                Item->SetNumberField(TEXT("tickMode"), Notify.MontageTickType);
                Item->SetBoolField(TEXT("filterViaRequest"), Notify.bCanBeFilteredViaRequest);
                Item->SetBoolField(TEXT("onDedicatedServer"), Notify.bTriggerOnDedicatedServer);
                Item->SetBoolField(TEXT("onFollower"), Notify.bTriggerOnFollower);
                Notifies.Add(MakeShared<FJsonValueObject>(Item));
            }
            Asset->SetArrayField(TEXT("notifies"), Notifies);
            Assets.Add(MakeShared<FJsonValueObject>(Asset));
        }
        Root->SetArrayField(TEXT("syncAssets"), Assets);
    }
    Root->SetArrayField(TEXT("graphs"), Graphs);
    if (IncludeRuntimeInputs)
    {
        if (RuntimeInputGraphs != RuntimeInputNames.Num() || !Blueprint->GeneratedClass) return 41;
        const UObject* Defaults = Blueprint->GeneratedClass->GetDefaultObject();
        const auto Values = MakeShared<FJsonObject>();
        for (const TCHAR* Name : { TEXT("VelocityBlendInterpSpeed"), TEXT("GroundedLeanInterpSpeed"),
            TEXT("InAirLeanInterpSpeed"), TEXT("AnimatedWalkSpeed"), TEXT("AnimatedRunSpeed"),
            TEXT("AnimatedSprintSpeed"), TEXT("AnimatedCrouchSpeed"), TEXT("JumpPlayRate"), TEXT("FallSpeed"),
            TEXT("LandPrediction"), TEXT("WalkRunBlend"), TEXT("StrideBlend"), TEXT("CrouchingPlayRate") })
        {
            const auto* Property = FindFProperty<FNumericProperty>(Defaults->GetClass(), Name);
            if (!Property || !Property->IsFloatingPoint()) return 42;
            const double Value = Property->GetFloatingPointPropertyValue(Property->ContainerPtrToValuePtr<void>(Defaults));
            if (!FMath::IsFinite(Value)) return 43;
            Values->SetNumberField(Name, Value);
        }
        const auto Bindings = MakeShared<FJsonObject>();
        TArray<TSharedPtr<FJsonValue>> Curves;
        TSet<FString> Paths;
        for (const TCHAR* Name : { TEXT("StrideBlend_N_Walk"), TEXT("StrideBlend_N_Run"), TEXT("StrideBlend_C_Walk"),
            TEXT("DiagonalScaleAmountCurve"), TEXT("LeanInAirCurve"), TEXT("LandPredictionCurve") })
        {
            const auto* Property = FindFProperty<FObjectPropertyBase>(Defaults->GetClass(), Name);
            auto* Curve = Property ? Cast<UCurveFloat>(Property->GetObjectPropertyValue_InContainer(Defaults)) : nullptr;
            if (!Curve) return 44;
            const FString Path = Curve->GetPathName();
            Bindings->SetStringField(Name, Path);
            if (Paths.Contains(Path)) continue;
            Paths.Add(Path);
            const auto Item = MakeShared<FJsonObject>();
            const auto Data = MakeShared<FJsonObject>();
            Item->SetStringField(TEXT("name"), Curve->GetName()); Item->SetStringField(TEXT("path"), Path);
            if (!FJsonObjectConverter::UStructToJsonObject(FRichCurve::StaticStruct(), &Curve->FloatCurve, Data, 0, 0, &ExportObject)) return 45;
            Item->SetObjectField(TEXT("curve"), Data);
            TArray<TSharedPtr<FJsonValue>> Samples;
            float Low = 0.f, High = 0.f; Curve->FloatCurve.GetTimeRange(Low, High);
            for (int32 Step = 0; Step <= 200; ++Step)
            {
                const float Time = FMath::Lerp(Low, High, Step / 200.f);
                const auto Sample = MakeShared<FJsonObject>();
                Sample->SetNumberField(TEXT("input"), Time); Sample->SetNumberField(TEXT("value"), Curve->GetFloatValue(Time));
                Samples.Add(MakeShared<FJsonValueObject>(Sample));
            }
            Item->SetArrayField(TEXT("verification"), Samples); Curves.Add(MakeShared<FJsonValueObject>(Item));
        }
        Root->SetNumberField(TEXT("movementInputSchemaVersion"), 1);
        const auto StateDefaults = MakeShared<FJsonObject>();
        for (const TCHAR* Name : { TEXT("VelocityBlend"), TEXT("LeanAmount"), TEXT("RelativeAccelerationAmount"),
            TEXT("DiagonalScaleAmount"), TEXT("StandingPlayRate"), TEXT("Speed"), TEXT("ShouldMove") })
        {
            auto* Property = Defaults->GetClass()->FindPropertyByName(Name);
            if (!Property) return 50;
            const auto Value = FJsonObjectConverter::UPropertyToJsonValue(Property,
                Property->ContainerPtrToValuePtr<void>(Defaults), 0, 0, &ExportObject);
            if (!Value.IsValid()) return 51;
            const auto Item = MakeShared<FJsonObject>();
            Item->SetStringField(TEXT("propertyClass"), Property->GetClass()->GetName());
            if (const auto* StructProperty = CastField<FStructProperty>(Property))
                Item->SetStringField(TEXT("struct"), StructProperty->Struct->GetPathName());
            Item->SetField(TEXT("value"), Value); StateDefaults->SetObjectField(Name, Item);
        }
        Root->SetNumberField(TEXT("groundedInputSchemaVersion"), 1);
        Root->SetObjectField(TEXT("movementInputStateDefaults"), StateDefaults);
        // Evaluate the actual generated Blueprint functions with controlled prior curves.
        // This is an input-function oracle, not a full animation/physics pose trace.
        auto* RateComponent = NewObject<USkeletalMeshComponent>(GetTransientPackage());
        auto* RateInstance = NewObject<UAnimInstance>(RateComponent, Blueprint->GeneratedClass);
        auto* RateSpeed = FindFProperty<FNumericProperty>(RateInstance->GetClass(), TEXT("Speed"));
        auto* RateStride = FindFProperty<FNumericProperty>(RateInstance->GetClass(), TEXT("StrideBlend"));
        if (!RateSpeed || !RateStride || !RateSpeed->IsFloatingPoint() || !RateStride->IsFloatingPoint()) return 52;
        const auto EvaluateRate = [RateInstance](const TCHAR* Name, double& Result)
        {
            UFunction* Function = RateInstance->FindFunction(Name);
            if (!Function) return false;
            FNumericProperty* Return = nullptr;
            for (TFieldIterator<FProperty> It(Function); It; ++It)
                if (It->HasAnyPropertyFlags(CPF_OutParm))
                {
                    if (Return || !CastField<FNumericProperty>(*It)) return false;
                    Return = CastField<FNumericProperty>(*It);
                }
            if (!Return || !Return->IsFloatingPoint()) return false;
            FStructOnScope Scope(Function);
            RateInstance->ProcessEvent(Function, Scope.GetStructMemory());
            Result = Return->GetFloatingPointPropertyValue(Return->ContainerPtrToValuePtr<void>(Scope.GetStructMemory()));
            return FMath::IsFinite(Result);
        };
        TArray<TSharedPtr<FJsonValue>> RateCases;
        for (const double Speed : {0., 75., 150., 350., 600., 900.})
        for (const float Gait : {-1.f, 0.f, 1.f, 1.5f, 2.f, 2.5f, 3.f, 4.f})
        for (const float Crouch : {-.5f, 0.f, .5f, 1.f, 1.5f})
        for (const double Scale : {.5, 1., 2.})
        {
            RateSpeed->SetFloatingPointPropertyValue(RateSpeed->ContainerPtrToValuePtr<void>(RateInstance), Speed);
            RateComponent->SetWorldScale3D(FVector(2., 3., Scale));
            RateInstance->OverrideCurveValue(TEXT("Weight_Gait"), Gait);
            RateInstance->OverrideCurveValue(TEXT("BasePose_CLF"), Crouch);
            double Stride, StandingRate, CrouchingRate;
            if (!EvaluateRate(TEXT("CalculateStrideBlend"), Stride)) return 53;
            RateStride->SetFloatingPointPropertyValue(RateStride->ContainerPtrToValuePtr<void>(RateInstance), Stride);
            if (!EvaluateRate(TEXT("CalculateStandingPlayRate"), StandingRate) ||
                !EvaluateRate(TEXT("CalculateCrouchingPlayRate"), CrouchingRate)) return 53;
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("speedCm"), Speed); Row->SetNumberField(TEXT("weightGait"), Gait);
            Row->SetNumberField(TEXT("basePoseCrouch"), Crouch); Row->SetNumberField(TEXT("meshScaleZ"), Scale);
            Row->SetNumberField(TEXT("stride"), Stride); Row->SetNumberField(TEXT("standingPlayRate"), StandingRate);
            Row->SetNumberField(TEXT("crouchingPlayRate"), CrouchingRate);
            RateCases.Add(MakeShared<FJsonValueObject>(Row));
        }
        Root->SetArrayField(TEXT("groundedRateNativeCases"), RateCases);
        Root->SetObjectField(TEXT("movementInputDefaults"), Values);
        Root->SetObjectField(TEXT("movementInputCurveBindings"), Bindings);
        Root->SetArrayField(TEXT("movementInputCurves"), Curves);
        UE_LOG(LogTemp, Display, TEXT("ALS_MOVEMENT_INPUTS_OK graphs=%d curves=%d defaults=13 assets_saved=0"), RuntimeInputGraphs, Curves.Num());
        UE_LOG(LogTemp, Display, TEXT("ALS_GROUNDED_INPUT_DEPENDENCIES_OK macros=%d state_defaults=7 assets_saved=0"), InputMacros.Num());
        UE_LOG(LogTemp, Display, TEXT("ALS_GROUNDED_RATE_NATIVE_OK cases=%d functions=3 assets_saved=0"), RateCases.Num());
    }
    if (IncludeGrounded)
    {
        Root->SetNumberField(TEXT("groundedSourceSchemaVersion"), 1);
        Root->SetStringField(TEXT("scope"), TEXT("Main Grounded with Standing and Crouching source dependencies"));
        auto* Skeleton = LoadObject<USkeleton>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton"));
        auto* Curve = LoadObject<UCurveFloat>(nullptr, TEXT("/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/ChangeStance.ChangeStance"));
        auto* Profile = Skeleton ? Skeleton->GetBlendProfile(TEXT("QuickFeet")) : nullptr;
        if (!Profile || !Curve || Profile->GetSkeleton() != Skeleton) return 32;
        const auto ProfileJson = MakeShared<FJsonObject>();
        ProfileJson->SetStringField(TEXT("path"), Profile->GetPathName());
        ProfileJson->SetStringField(TEXT("skeleton"), Skeleton->GetPathName());
        ProfileJson->SetNumberField(TEXT("mode"), static_cast<int32>(Profile->GetMode()));
        TArray<TSharedPtr<FJsonValue>> Bones;
        const auto& Reference = Skeleton->GetReferenceSkeleton();
        for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone)
        {
            const auto Item = MakeShared<FJsonObject>();
            const FName Name = Reference.GetBoneName(Bone);
            Item->SetNumberField(TEXT("index"), Bone);
            Item->SetStringField(TEXT("name"), Name.ToString());
            Item->SetNumberField(TEXT("parent"), Reference.GetParentIndex(Bone));
            Item->SetNumberField(TEXT("entry"), Profile->GetEntryIndex(Name));
            Item->SetNumberField(TEXT("scale"), Profile->GetBoneBlendScale(Name));
            Bones.Add(MakeShared<FJsonValueObject>(Item));
        }
        ProfileJson->SetArrayField(TEXT("bones"), Bones);
        TArray<TSharedPtr<FJsonValue>> Entries;
        for (int32 Index = 0; Index < Profile->GetNumBlendEntries(); ++Index)
        {
            const auto& Entry = Profile->GetEntry(Index);
            const auto Item = MakeShared<FJsonObject>();
            Item->SetNumberField(TEXT("index"), Index);
            Item->SetStringField(TEXT("bone"), Entry.BoneReference.BoneName.ToString());
            Item->SetNumberField(TEXT("scale"), Entry.BlendScale);
            Entries.Add(MakeShared<FJsonValueObject>(Item));
        }
        ProfileJson->SetArrayField(TEXT("entries"), Entries);
        // The actual state-machine per-bone update/normalization, not a full AnimBP trace.
        TArray<TSharedPtr<FJsonValue>> Cases;
        for (int32 Step = 0; Step <= 32; ++Step)
        {
            FAlphaBlend Blend(.8f); Blend.SetBlendOption(EAlphaBlendOption::Cubic);
            Blend.SetValueRange(0.f, 1.f); Blend.SetAlpha(Step / 32.f);
            TArray<FBlendSampleData> Data; Data.SetNum(2);
            for (int32 Side = 0; Side < 2; ++Side)
            {
                Data[Side].TotalWeight = Side == 0 ? Blend.GetBlendedValue() : 1.f - Blend.GetBlendedValue();
                Data[Side].PerBoneBlendData.SetNum(Profile->GetNumBlendEntries());
                Profile->UpdateBoneWeights(Data[Side], Blend, 0.f, Data[Side].TotalWeight, Side != 0);
            }
            FBlendSampleData::NormalizeDataWeight(Data);
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("linearAlpha"), Step / 32.f);
            Row->SetNumberField(TEXT("alpha"), Blend.GetBlendedValue());
            TArray<TSharedPtr<FJsonValue>> Incoming, Outgoing;
            for (int32 Index = 0; Index < Profile->GetNumBlendEntries(); ++Index)
            {
                Incoming.Add(MakeShared<FJsonValueNumber>(Data[0].PerBoneBlendData[Index]));
                Outgoing.Add(MakeShared<FJsonValueNumber>(Data[1].PerBoneBlendData[Index]));
            }
            Row->SetArrayField(TEXT("incoming"), Incoming); Row->SetArrayField(TEXT("outgoing"), Outgoing);
            Cases.Add(MakeShared<FJsonValueObject>(Row));
        }
        ProfileJson->SetArrayField(TEXT("nativeCases"), Cases);
        Root->SetObjectField(TEXT("quickFeet"), ProfileJson);
        const auto CurveJson = MakeShared<FJsonObject>();
        CurveJson->SetStringField(TEXT("path"), Curve->GetPathName());
        const auto RichCurve = MakeShared<FJsonObject>();
        if (!FJsonObjectConverter::UStructToJsonObject(FRichCurve::StaticStruct(), &Curve->FloatCurve,
            RichCurve, 0, 0, &ExportObject)) return 33;
        CurveJson->SetObjectField(TEXT("curve"), RichCurve);
        Root->SetObjectField(TEXT("changeStanceCurve"), CurveJson);
    }
    if (IncludeInputs)
    {
        if (CharacterInputGraphs != 2) return 23;
        Root->SetNumberField(TEXT("inputSchemaVersion"), 1);
        Root->SetStringField(TEXT("characterSource"), CharacterBlueprint->GetPathName());
        auto* Component = NewObject<USkeletalMeshComponent>(GetTransientPackage());
        auto* Instance = NewObject<UAnimInstance>(Component, Blueprint->GeneratedClass);
        auto* Speed = FindFProperty<FNumericProperty>(Instance->GetClass(), TEXT("Speed"));
        auto* Moving = FindFProperty<FBoolProperty>(Instance->GetClass(), TEXT("IsMoving"));
        auto* HasInput = FindFProperty<FBoolProperty>(Instance->GetClass(), TEXT("HasMovementInput"));
        auto* PivotLimit = FindFProperty<FNumericProperty>(Instance->GetClass(), TEXT("TriggerPivotSpeedLimit"));
        UFunction* Function = Instance->FindFunction(TEXT("ShouldMoveCheck"));
        if (!Speed || !Speed->IsFloatingPoint() || !Moving || !HasInput || !Function || !PivotLimit) return 24;
        Root->SetNumberField(TEXT("triggerPivotSpeedLimit"), PivotLimit->GetFloatingPointPropertyValue(
            PivotLimit->ContainerPtrToValuePtr<void>(Instance)));
        FBoolProperty* Return = nullptr;
        for (TFieldIterator<FProperty> It(Function); It; ++It)
            if (It->HasAnyPropertyFlags(CPF_OutParm))
            {
                if (Return || !CastField<FBoolProperty>(*It)) return 25;
                Return = CastField<FBoolProperty>(*It);
            }
        if (!Return) return 25;
        TArray<TSharedPtr<FJsonValue>> Cases;
        const double Speeds[] = {0, 0.999, 1, 1.001, 149.999, 150, 150.001, 175, 375, 650};
        for (int32 MovingValue = 0; MovingValue < 2; ++MovingValue)
        for (int32 InputValue = 0; InputValue < 2; ++InputValue)
        for (double SpeedValue : Speeds)
        {
            Moving->SetPropertyValue_InContainer(Instance, MovingValue != 0);
            HasInput->SetPropertyValue_InContainer(Instance, InputValue != 0);
            Speed->SetFloatingPointPropertyValue(Speed->ContainerPtrToValuePtr<void>(Instance), SpeedValue);
            FStructOnScope Scope(Function);
            Instance->ProcessEvent(Function, Scope.GetStructMemory());
            const auto Row = MakeShared<FJsonObject>();
            Row->SetBoolField(TEXT("isMoving"), MovingValue != 0);
            Row->SetBoolField(TEXT("hasMovementInput"), InputValue != 0);
            Row->SetNumberField(TEXT("speed"), SpeedValue);
            Row->SetBoolField(TEXT("shouldMove"), Return->GetPropertyValue_InContainer(Scope.GetStructMemory()));
            Cases.Add(MakeShared<FJsonValueObject>(Row));
        }
        Root->SetArrayField(TEXT("shouldMoveNativeCases"), Cases);
        UE_LOG(LogTemp, Display, TEXT("ALS_LOCOMOTION_INPUTS_OK character_graphs=%d should_move_cases=%d assets_saved=0"),
            CharacterInputGraphs, Cases.Num());
    }
    if (IncludeInventory)
    {
        const auto Generated = Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass);
        if (!Generated) return 18;
        TArray<TSharedPtr<FJsonValue>> Inventory;
        for (const UEdGraph* Graph : AllGraphs)
        {
            TArray<UEdGraphNode*> SortedNodes = Graph->Nodes;
            SortedNodes.Sort([](const UEdGraphNode& A, const UEdGraphNode& B) { return A.GetPathName() < B.GetPathName(); });
            for (const UEdGraphNode* Node : SortedNodes)
            {
                if (!Node->GetClass()->GetName().StartsWith(TEXT("AnimGraphNode_"))) continue;
                const auto Item = MakeShared<FJsonObject>();
                Item->SetStringField(TEXT("path"), Node->GetPathName());
                Item->SetStringField(TEXT("class"), Node->GetClass()->GetName());
                const int32* Index = Generated->GetNodePropertyIndexFromGuid(Node->NodeGuid);
                Item->SetNumberField(TEXT("compiledNodeIndex"), Index ? *Index : INDEX_NONE);
                const int32 PropertyIndex = Generated->GetNodeIndexFromGuid(Node->NodeGuid);
                Item->SetNumberField(TEXT("propertyIndex"), PropertyIndex);
                int32 CacheSourcePropertyIndex = INDEX_NONE;
                if (Node->GetClass()->GetFName() == TEXT("AnimGraphNode_UseCachedPose") && Index)
                {
                    const auto Property = Generated->GetAnimNodeProperties()[PropertyIndex];
                    const auto Runtime = MakeShared<FJsonObject>();
                    if (!FJsonObjectConverter::UStructToJsonObject(Property->Struct,
                        Property->ContainerPtrToValuePtr<void>(Generated->GetDefaultObject()), Runtime, 0, 0, &ExportObject)) return 21;
                    CacheSourcePropertyIndex = Runtime->GetObjectField(TEXT("linkToCachingNode"))->GetIntegerField(TEXT("linkId"));
                }
                Item->SetNumberField(TEXT("cacheSourcePropertyIndex"), CacheSourcePropertyIndex);
                // Keep non-asset producers, cache references and uncompiled editor nodes explicit.
                // A pose cache reference is not another instance of its underlying asset player.
                const auto Player = Generated->GetPropertyInstance<FAnimNode_AssetPlayerBase>(
                    Generated->GetDefaultObject(), Node->NodeGuid);
                Item->SetBoolField(TEXT("assetPlayer"), Player != nullptr);
                if (Player)
                {
                    const auto Asset = Player->GetAnimAsset();
                    Item->SetStringField(TEXT("asset"), Asset ? Asset->GetPathName() : TEXT(""));
                    Item->SetStringField(TEXT("groupName"), Player->GetGroupName().ToString());
                    Item->SetNumberField(TEXT("groupRole"), Player->GetGroupRole());
                    Item->SetNumberField(TEXT("groupMethod"), static_cast<int32>(Player->GetGroupMethod()));
                    Item->SetBoolField(TEXT("loop"), Player->IsLooping());
                    TArray<TSharedPtr<FJsonValue>> Samples;
                    if (const auto Space = Cast<UBlendSpace>(Asset))
                        for (const FBlendSample& Sample : Space->GetBlendSamples())
                        {
                            if (!Sample.Animation) return 19;
                            Samples.Add(MakeShared<FJsonValueString>(Sample.Animation->GetPathName()));
                        }
                    else if (const auto Sequence = Cast<UAnimSequence>(Asset))
                        Samples.Add(MakeShared<FJsonValueString>(Sequence->GetPathName()));
                    Item->SetArrayField(TEXT("samples"), Samples);
                    if (const auto SpacePlayer = Generated->GetPropertyInstance<FAnimNode_BlendSpacePlayerBase>(
                        Generated->GetDefaultObject(), Node->NodeGuid))
                    {
                        Item->SetBoolField(TEXT("evaluator"), SpacePlayer->IsEvaluator());
                        Item->SetBoolField(TEXT("teleport"), SpacePlayer->ShouldTeleportToTime());
                    }
                    else if (const auto Evaluator = Generated->GetPropertyInstance<FAnimNode_SequenceEvaluatorBase>(
                        Generated->GetDefaultObject(), Node->NodeGuid))
                    {
                        Item->SetBoolField(TEXT("evaluator"), true);
                        Item->SetBoolField(TEXT("teleport"), Evaluator->GetTeleportToExplicitTime());
                    }
                    else
                    {
                        Item->SetBoolField(TEXT("evaluator"), false);
                        Item->SetBoolField(TEXT("teleport"), false);
                    }
                }
                const auto Properties = MakeShared<FJsonObject>();
                for (const TCHAR* Name : PropertyNames)
                    if (FProperty* Property = Node->GetClass()->FindPropertyByName(Name))
                    {
                        const auto Value = FJsonObjectConverter::UPropertyToJsonValue(Property,
                            Property->ContainerPtrToValuePtr<void>(Node), 0, 0, &ExportObject);
                        if (!Value.IsValid()) return 20;
                        Properties->SetField(Name, Value);
                    }
                Item->SetObjectField(TEXT("properties"), Properties);
                Inventory.Add(MakeShared<FJsonValueObject>(Item));
            }
        }
        Root->SetNumberField(TEXT("inventorySchemaVersion"), 1);
        Root->SetNumberField(TEXT("compiledPropertyCount"), Generated->GetAnimNodeProperties().Num());
        Root->SetArrayField(TEXT("compiledNodeInventory"), Inventory);
        if (IncludeCaches)
        {
            TArray<TSharedPtr<FJsonValue>> Orders;
            const auto& NativeOrders = Generated->GetOrderedSavedPoseNodeIndicesMap();
            TArray<FName> Names;
            NativeOrders.GetKeys(Names);
            Names.Sort(FNameLexicalLess());
            for (const FName Name : Names)
            {
                const auto Order = MakeShared<FJsonObject>();
                Order->SetStringField(TEXT("root"), Name.ToString());
                TArray<TSharedPtr<FJsonValue>> Indices;
                for (const int32 Index : NativeOrders[Name].OrderedSavedPoseNodeIndices)
                    Indices.Add(MakeShared<FJsonValueNumber>(Index));
                Order->SetArrayField(TEXT("compiledNodeIndices"), Indices);
                Orders.Add(MakeShared<FJsonValueObject>(Order));
            }
            Root->SetNumberField(TEXT("cacheSchemaVersion"), 1);
            Root->SetArrayField(TEXT("orderedSavedPoseNodes"), Orders);
            Root->SetArrayField(TEXT("cacheUpdateNativeCases"), AlsPoseCacheProbe::Observe());
            UE_LOG(LogTemp, Display, TEXT("ALS_POSE_CACHE_OK roots=%d cases=24 assets_saved=0"), Orders.Num());
        }
        UE_LOG(LogTemp, Display, TEXT("ALS_GRAPH_INVENTORY_OK nodes=%d assets_saved=0"), Inventory.Num());
    }
    if (IncludeDetail)
    {
        const IAnimClassInterface* AnimClass = IAnimClassInterface::GetFromClass(Blueprint->GeneratedClass);
        if (!AnimClass) return 7;
        TArray<TSharedPtr<FJsonValue>> Machines;
        for (const auto& Machine : AnimClass->GetBakedStateMachines())
            if (Machine.MachineName == TEXT("(N) Locomotion Detail") || Machine.MachineName == TEXT("(N) Locomotion States") ||
                Machine.MachineName == TEXT("(N) Stop States") || Machine.MachineName == TEXT("Main Grounded States") ||
                (IncludeCycle && (Machine.MachineName == TEXT("(N) Locomotion Cycles") || Machine.MachineName == TEXT("(N) Directional States"))) ||
                (IncludeGrounded && (Machine.MachineName == TEXT("(CLF) Locomotion States") ||
                    Machine.MachineName == TEXT("(CLF) Locomotion Cycles") || Machine.MachineName == TEXT("CLF_Directional States"))) ||
                (IncludeMovement && (Machine.MachineName == TEXT("Main Movement States") || Machine.MachineName == TEXT("Jump States"))))
            {
                const auto MachineJson = MakeShared<FJsonObject>();
                if (!FJsonObjectConverter::UStructToJsonObject(FBakedAnimationStateMachine::StaticStruct(), &Machine,
                    MachineJson, 0, 0, &ExportObject)) return 8;
                Machines.Add(MakeShared<FJsonValueObject>(MachineJson));
            }
        if (Machines.Num() != (IncludeMovement ? 11 : IncludeGrounded ? 9 : IncludeCycle ? 6 : 4)) return 9;
        Root->SetArrayField(TEXT("bakedMachines"), Machines);
    }
    FString Text;
    const auto Writer = TJsonWriterFactory<>::Create(&Text);
    const auto Encoding = IncludeDetail ? FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM : FFileHelper::EEncodingOptions::AutoDetect;
    if (!FJsonSerializer::Serialize(Root, Writer) || !FFileHelper::SaveStringToFile(Text, *OutputPath, Encoding)) return 5;
    UE_LOG(LogTemp, Display, TEXT("ALS_STOP_GRAPH_OK graphs=%d plant_evaluators=%d assets_saved=0"), Graphs.Num(), PlantEvaluators);
    if (IncludeDetail) UE_LOG(LogTemp, Display, TEXT("ALS_DETAIL_GRAPH_OK players=%d assets_saved=0"), DetailPlayers);
    if (IncludeCycle) UE_LOG(LogTemp, Display, TEXT("ALS_CYCLE_GRAPH_OK blendspaces=%d sequences=%d assets_saved=0"), CyclePlayers, CycleSequences);
    if (IncludeMain) UE_LOG(LogTemp, Display, TEXT("ALS_MAIN_SOURCES_OK sequences=%d evaluators=%d assets_saved=0"), MainPlayers, MainEvaluators);
    if (IncludeGrounded) UE_LOG(LogTemp, Display, TEXT("ALS_GROUNDED_SOURCES_OK crouch_sequences=%d evaluators=%d blendspaces=%d assets_saved=0"), CrouchingPlayers, CrouchingEvaluators, CrouchingSpaces);
    if (IncludeMovement) UE_LOG(LogTemp, Display, TEXT("ALS_MAIN_MOVEMENT_GRAPH_OK graphs=%d assets_saved=0"), MovementGraphs);
    return 0;
}
