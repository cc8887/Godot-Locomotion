#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsLinkedAnimationInstance.h"
#include "AlsCharacter.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimNode_StateMachine.h"
#include "AnimNodes/AnimNode_TwoWayBlend.h"
#include "Nodes/AlsAnimNode_GameplayTagsBlend.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Editor.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FOverlayProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P,UAnimInstance* I,float D){(P.*&FOverlayProxyAccess::PreUpdate)(I,D);}
    static void UpdateRoot(FAnimInstanceProxy& P){(P.*&FOverlayProxyAccess::UpdateAnimation)();}
    static void Post(FAnimInstanceProxy& P,UAnimInstance* I){(P.*&FOverlayProxyAccess::PostUpdate)(I);}
};
struct FOverlayInstanceAccess : UAlsLinkedAnimationInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* I){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I);}
    static void SetParent(UAlsLinkedAnimationInstance* I,UAlsAnimationInstance* P){I->*&FOverlayInstanceAccess::Parent=P;}
};
struct FOverlayBlendAccess : FAnimNode_TwoWayBlend
{
    static float Alpha(const FAnimNode_TwoWayBlend& Node){return Node.*&FOverlayBlendAccess::InternalBlendAlpha;}
};
struct FWeaponMachineTraceAccess : FAnimNode_StateMachine
{
    static const TArray<FAnimationActiveTransitionEntry>& Edges(const FAnimNode_StateMachine& N){return N.*&FWeaponMachineTraceAccess::ActiveTransitionArray;}
    static const TArray<int32>& Updates(const FAnimNode_StateMachine& N){return N.*&FWeaponMachineTraceAccess::StatesUpdated;}
};
bool WeaponTraceBool(UObject* Object,const TCHAR* StructName,const TCHAR* Name,bool Value)
{
    auto* Struct=FindFProperty<FStructProperty>(Object->GetClass(),StructName);if(!Struct)return false;
    auto* Field=FindFProperty<FBoolProperty>(Struct->Struct,Name);if(!Field)return false;
    Field->SetPropertyValue_InContainer(Struct->ContainerPtrToValuePtr<void>(Object),Value);return true;
}
struct FOverlayActionBlendAccess : FAlsAnimNode_GameplayTagsBlend
{
    static TArray<TSharedPtr<FJsonValue>> Weights(const FAlsAnimNode_GameplayTagsBlend& Node)
    {
        TArray<TSharedPtr<FJsonValue>> Result;
        for(const auto& Child:Node.*&FOverlayActionBlendAccess::PerBlendData)Result.Add(MakeShared<FJsonValueNumber>(Child.Weight));
        return Result;
    }
};
bool SetState(UObject* Object,const TCHAR* StructName,const TSharedPtr<FJsonObject>& Values)
{
    auto* Struct=FindFProperty<FStructProperty>(Object->GetClass(),StructName);if(!Struct)return false;
    void* Memory=Struct->ContainerPtrToValuePtr<void>(Object);
    for(const auto& Pair:Values->Values)
    {
        auto* Field=FindFProperty<FFloatProperty>(Struct->Struct,FName(*Pair.Key));if(!Field)return false;
        const float Value=static_cast<float>(Pair.Value->AsNumber());if(!FMath::IsFinite(Value))return false;
        Field->SetPropertyValue_InContainer(Memory,Value);
    }
    return true;
}
TSharedPtr<FJsonObject> DefaultOverlayTransformJson(const FTransform& T)
{
    const auto Result=MakeShared<FJsonObject>();
    auto Values=[](std::initializer_list<double> V){TArray<TSharedPtr<FJsonValue>> A;for(double X:V)A.Add(MakeShared<FJsonValueNumber>(X));return A;};
    const auto P=T.GetTranslation();const auto Q=T.GetRotation();const auto S=T.GetScale3D();
    Result->SetArrayField(TEXT("position"),Values({P.X,P.Y,P.Z}));Result->SetArrayField(TEXT("rotation"),Values({Q.X,Q.Y,Q.Z,Q.W}));
    Result->SetArrayField(TEXT("scale"),Values({S.X,S.Y,S.Z}));return Result;
}
}

bool UAlsAnimationGraphLibrary::ExportRefactoredDefaultOverlayTrace(const FString& RequestPath,const FString& OutputPath)
{
    if(FPaths::IsRelative(RequestPath)||FPaths::IsRelative(OutputPath)||RequestPath==OutputPath)return false;
    FString Text;TSharedPtr<FJsonObject> Request;
    if(!FFileHelper::LoadFileToString(Text,*RequestPath)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Request))return false;
    FString Kind=TEXT("Default");Request->TryGetStringField(TEXT("overlay"),Kind);
    const bool Weapon=Kind==TEXT("Bow")||Kind==TEXT("PistolOneHanded")||Kind==TEXT("PistolTwoHanded")||Kind==TEXT("Rifle");
    if(!Weapon&&Kind!=TEXT("Default")&&Kind!=TEXT("Feminine")&&Kind!=TEXT("Masculine")&&Kind!=TEXT("Box")&&Kind!=TEXT("HandsTied")&&Kind!=TEXT("Injured")&&Kind!=TEXT("Barrel")&&Kind!=TEXT("Binoculars")&&Kind!=TEXT("Torch"))return false;
    const bool Box=Kind==TEXT("Box");
    const bool Hands=Kind==TEXT("HandsTied"),Injured=Kind==TEXT("Injured"),Barrel=Kind==TEXT("Barrel");
    const bool Binoculars=Kind==TEXT("Binoculars"),Torch=Kind==TEXT("Torch"),Prop=Binoculars||Torch;
    const bool Cached=Hands||Injured||Barrel,HasActions=Box||Cached||Prop;
    const FString BlueprintPath=FString::Printf(TEXT("/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_%s.AB_Als_%s"),*Kind,*Kind);
    auto* Blueprint=LoadObject<UAnimBlueprint>(nullptr,*BlueprintPath);
    auto* Generated=Blueprint?Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass):nullptr;
    auto* CharacterClass=LoadClass<AAlsCharacter>(nullptr,TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
    auto* Defaults=CharacterClass?Cast<AAlsCharacter>(CharacterClass->GetDefaultObject()):nullptr;
    auto* Mesh=Defaults?Defaults->GetMesh()->GetSkeletalMeshAsset():nullptr;
    auto* World=GEditor?GEditor->GetEditorWorldContext().World():nullptr;
    if(!Generated||!Mesh||!Mesh->GetSkeleton()||!World)return false;
    FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;Spawn.SpawnCollisionHandlingOverride=ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    auto* Owner=World->SpawnActor<AActor>(Spawn);if(!Owner)return false;Owner->SetActorTickEnabled(false);
    ON_SCOPE_EXIT{World->DestroyActor(Owner);};
    const auto& Reference=Mesh->GetSkeleton()->GetReferenceSkeleton();TArray<FBoneIndexType> Required;
    TArray<TSharedPtr<FJsonValue>> Names;
    for(int32 Bone=0;Bone<Reference.GetNum();++Bone){Required.Add(static_cast<FBoneIndexType>(Bone));Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Bone).ToString()));}
    TArray<TSharedPtr<FJsonValue>> Traces;int32 Total=0;
    for(const auto& TraceValue:Request->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner));Component->SetSkeletalMesh(Mesh);
        TStrongObjectPtr<UAlsAnimationInstance> Parent(NewObject<UAlsAnimationInstance>(Component.Get()));
        TStrongObjectPtr<UAlsLinkedAnimationInstance> Instance(NewObject<UAlsLinkedAnimationInstance>(Component.Get(),Generated));
        auto Initialize=[&]()
        {
            Instance->InitializeAnimation(true);FOverlayInstanceAccess::SetParent(Instance.Get(),Parent.Get());
            auto& P=FOverlayInstanceAccess::Proxy(Instance.Get());
            P.GetRequiredBones().InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Mesh->GetSkeleton());
            P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetUseSourceData(false);
        };
        Initialize();
        auto& Proxy=FOverlayInstanceAccess::Proxy(Instance.Get());
        const auto& Properties=Generated->GetAnimNodeProperties();
        const int32 IdleIndex=Binoculars?5:Torch?6:Hands?19:Injured?13:Box||Barrel?9:11;
        const int32 ActionIndex=Binoculars?24:Torch?23:Hands?2:Injured?14:Barrel?10:13;
        const int32 AimIndex=Binoculars?6:7;
        const int32 PredictionIndex=Weapon?INDEX_NONE:Hands?11:Injured||!HasActions?4:INDEX_NONE;
        const int32 CacheIndex=Hands?17:Injured?10:5;
        if(!Weapon&&(Properties.Num()!=(Binoculars?28:Torch?26:Hands||Injured?22:Barrel?18:Box?17:14)||Properties[IdleIndex]->Struct!=FAnimNode_SequencePlayer::StaticStruct()))return false;
        if(Prop&&Properties[AimIndex]->Struct!=FAlsAnimNode_GameplayTagsBlend::StaticStruct())return false;
        if(HasActions&&Properties[ActionIndex]->Struct!=FAlsAnimNode_GameplayTagsBlend::StaticStruct())return false;
        if(PredictionIndex!=INDEX_NONE&&Properties[PredictionIndex]->Struct!=FAnimNode_TwoWayBlend::StaticStruct())return false;
        if(Cached&&Properties[CacheIndex]->Struct!=FAnimNode_SaveCachedPose::StaticStruct())return false;
        auto* Prediction=PredictionIndex==INDEX_NONE?nullptr:Properties[PredictionIndex]->ContainerPtrToValuePtr<FAnimNode_TwoWayBlend>(Instance.Get());
        auto* Actions=HasActions?Properties[ActionIndex]->ContainerPtrToValuePtr<FAlsAnimNode_GameplayTagsBlend>(Instance.Get()):nullptr;
        auto* Aim=Prop?Properties[AimIndex]->ContainerPtrToValuePtr<FAlsAnimNode_GameplayTagsBlend>(Instance.Get()):nullptr;
        auto* Cache=Cached?Properties[CacheIndex]->ContainerPtrToValuePtr<FAnimNode_SaveCachedPose>(Instance.Get()):nullptr;
        auto* Idle=Weapon?nullptr:Properties[IdleIndex]->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(Instance.Get());
        FAnimNode_StateMachine* Machine=nullptr;
        if(Weapon)
        {
            for(auto* Property:Properties)if(Property->Struct==FAnimNode_StateMachine::StaticStruct())
            {if(Machine)return false;Machine=Property->ContainerPtrToValuePtr<FAnimNode_StateMachine>(Instance.Get());}
            if(!Machine||Generated->GetBakedStateMachines().Num()!=1||Generated->GetAnimNotifies().Num()!=2)return false;
        }
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FrameValue:TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());const TGuardValue<uint64> GlobalFrame(GFrameCounter,static_cast<uint64>(900000+Total));
            const auto Frame=FrameValue->AsObject();const float Delta=static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            if(!FMath::IsFinite(Delta)||Delta<0)return false;
            if(Frame->GetBoolField(TEXT("reset")))Initialize();
            if(!SetState(Parent.Get(),TEXT("PoseState"),Frame->GetObjectField(TEXT("poseState")))||
                !SetState(Parent.Get(),TEXT("InAirState"),Frame->GetObjectField(TEXT("inAirState"))))return false;
            if(HasActions||Weapon)
            {
                auto* Property=FindFProperty<FStructProperty>(Parent->GetClass(),TEXT("LocomotionAction"));
                if(!Property||Property->Struct!=FGameplayTag::StaticStruct())return false;
                const auto Tag=Frame->GetStringField(TEXT("action"));
                *Property->ContainerPtrToValuePtr<FGameplayTag>(Parent.Get())=Tag.IsEmpty()?FGameplayTag():FGameplayTag::RequestGameplayTag(FName(*Tag),false);
            }
            if(Prop||Weapon)
            {
                if(!SetState(Parent.Get(),TEXT("ViewState"),Frame->GetObjectField(TEXT("viewState"))))return false;
                auto* Property=FindFProperty<FStructProperty>(Parent->GetClass(),TEXT("RotationMode"));
                if(!Property||Property->Struct!=FGameplayTag::StaticStruct())return false;
                const auto Tag=Frame->GetStringField(TEXT("rotationMode"));
                *Property->ContainerPtrToValuePtr<FGameplayTag>(Parent.Get())=Tag.IsEmpty()?FGameplayTag():FGameplayTag::RequestGameplayTag(FName(*Tag),false);
            }
            if(Weapon)
            {
                for(const auto& Pair:{TPair<const TCHAR*,const TCHAR*>(TEXT("Gait"),TEXT("gait")),TPair<const TCHAR*,const TCHAR*>(TEXT("LocomotionMode"),TEXT("locomotionMode"))})
                {
                    auto* Property=FindFProperty<FStructProperty>(Parent->GetClass(),Pair.Key);
                    if(!Property||Property->Struct!=FGameplayTag::StaticStruct())return false;
                    const auto Tag=Frame->GetStringField(Pair.Value);
                    *Property->ContainerPtrToValuePtr<FGameplayTag>(Parent.Get())=Tag.IsEmpty()?FGameplayTag():FGameplayTag::RequestGameplayTag(FName(*Tag),false);
                }
                if(!WeaponTraceBool(Parent.Get(),TEXT("LocomotionState"),TEXT("bMoving"),Frame->GetBoolField(TEXT("moving")))||
                    !WeaponTraceBool(Parent.Get(),TEXT("TransitionsState"),TEXT("bTransitionsAllowed"),Frame->GetBoolField(TEXT("allowed"))))return false;
            }
            FOverlayProxyAccess::Pre(Proxy,Instance.Get(),Delta);
            FOverlayProxyAccess::UpdateRoot(Proxy);Proxy.FlipBufferWriteIndex();
            if(Weapon){Instance->NotifyQueue.AnimNotifies.Reset();Instance->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();}
            FOverlayProxyAccess::Post(Proxy,Instance.Get());
            FPoseContext Pose(&Proxy,true);FBlendedHeapCurve Curve;UE::Anim::FHeapAttributeContainer Attributes;
            FParallelEvaluationData Evaluation{Curve,Pose.Pose,Attributes};Instance->PreEvaluateAnimation();Instance->ParallelEvaluateAnimation(false,Mesh,Evaluation);
            TArray<TSharedPtr<FJsonValue>> Atoms;for(auto Bone:Pose.Pose.ForEachBoneIndex())Atoms.Add(MakeShared<FJsonValueObject>(DefaultOverlayTransformJson(Pose.Pose[Bone])));
            const auto Curves=MakeShared<FJsonObject>();Curve.ForEachElement([&](const auto& C){Curves->SetNumberField(C.Name.ToString(),C.Value);});
            const auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("input"),Frame);Row->SetArrayField(TEXT("pose"),Atoms);Row->SetObjectField(TEXT("curves"),Curves);
            if(Prediction)Row->SetNumberField(TEXT("predictionAlpha"),FOverlayBlendAccess::Alpha(*Prediction));
            if(Actions)Row->SetArrayField(TEXT("actionWeights"),FOverlayActionBlendAccess::Weights(*Actions));
            if(Aim)Row->SetArrayField(TEXT("aimWeights"),FOverlayActionBlendAccess::Weights(*Aim));
            if(Cache)Row->SetNumberField(TEXT("cacheWeight"),Cache->GlobalWeight);
            if(Idle){Row->SetNumberField(TEXT("idleTime"),Idle->GetAccumulatedTime());Row->SetNumberField(TEXT("idleWeight"),Idle->GetCachedBlendWeight());}
            if(Machine)
            {
                Row->SetNumberField(TEXT("current"),Machine->GetCurrentState());Row->SetNumberField(TEXT("elapsed"),Machine->GetCurrentStateElapsedTime());
                TArray<TSharedPtr<FJsonValue>> Weights,Edges,Updates,Notifies,Players;
                for(int32 State=0;State<3;++State)Weights.Add(MakeShared<FJsonValueNumber>(Machine->GetStateWeight(State)));
                for(int32 State:FWeaponMachineTraceAccess::Updates(*Machine))Updates.Add(MakeShared<FJsonValueNumber>(State));
                for(const auto& Edge:FWeaponMachineTraceAccess::Edges(*Machine))
                {
                    const auto Data=MakeShared<FJsonObject>();Data->SetNumberField(TEXT("from"),Edge.PreviousState);Data->SetNumberField(TEXT("to"),Edge.NextState);
                    Data->SetNumberField(TEXT("duration"),Edge.CrossfadeDuration);Data->SetNumberField(TEXT("elapsed"),Edge.ElapsedTime);Data->SetNumberField(TEXT("alpha"),Edge.Alpha);
                    TArray<TSharedPtr<FJsonValue>> Indices;for(int32 Index:Edge.SourceTransitionIndices)Indices.Add(MakeShared<FJsonValueNumber>(Index));
                    Data->SetArrayField(TEXT("edges"),Indices);Edges.Add(MakeShared<FJsonValueObject>(Data));
                }
                for(const auto& Event:Instance->NotifyQueue.AnimNotifies)if(const auto* Notify=Event.GetNotify())
                    for(int32 Index=0;Index<Generated->GetAnimNotifies().Num();++Index)if(Notify==&Generated->GetAnimNotifies()[Index])Notifies.Add(MakeShared<FJsonValueNumber>(Index));
                for(int32 Index=0;Index<Properties.Num();++Index)if(Properties[Index]->Struct==FAnimNode_SequencePlayer::StaticStruct())
                {
                    const auto* Player=Properties[Index]->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(Instance.Get());const auto Data=MakeShared<FJsonObject>();
                    Data->SetNumberField(TEXT("propertyIndex"),Index);Data->SetNumberField(TEXT("time"),Player->GetAccumulatedTime());Data->SetNumberField(TEXT("weight"),Player->GetCachedBlendWeight());Players.Add(MakeShared<FJsonValueObject>(Data));
                }
                Row->SetArrayField(TEXT("weights"),Weights);Row->SetArrayField(TEXT("transitions"),Edges);Row->SetArrayField(TEXT("updates"),Updates);
                Row->SetArrayField(TEXT("notifies"),Notifies);Row->SetArrayField(TEXT("players"),Players);
            }
            Frames.Add(MakeShared<FJsonValueObject>(Row));++Total;
        }
        Instance->UninitializeAnimation();const auto Trace=MakeShared<FJsonObject>();Trace->SetStringField(TEXT("name"),TraceValue->AsObject()->GetStringField(TEXT("name")));
        Trace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    const auto Result=MakeShared<FJsonObject>();Result->SetNumberField(TEXT("schemaVersion"),1);Result->SetStringField(TEXT("source"),Blueprint->GetPathName());
    if(Weapon)
    {
        TArray<TSharedPtr<FJsonValue>> Notifies;int32 Index=0;
        for(const auto& Notify:Generated->GetAnimNotifies())
        {const auto Data=MakeShared<FJsonObject>();Data->SetNumberField(TEXT("index"),Index++);Data->SetStringField(TEXT("name"),Notify.NotifyName.ToString());Notifies.Add(MakeShared<FJsonValueObject>(Data));}
        Result->SetArrayField(TEXT("notifyDefinitions"),Notifies);
    }
    Result->SetStringField(TEXT("requestDigest"),Request->GetStringField(TEXT("requestDigest")));Result->SetArrayField(TEXT("names"),Names);Result->SetArrayField(TEXT("traces"),Traces);
    FString Json;if(!FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_DEFAULT_OVERLAY_NATIVE_OK traces=%d frames=%d assets_saved=0"),Traces.Num(),Total);return true;
}
