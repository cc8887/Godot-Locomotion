#include "AlsAnimationGraphLibrary.h"

#include "Animation/AnimBlueprint.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimStateMachineTypes.h"
#include "Animation/BlendProfile.h"
#include "Animation/BlendSpace.h"
#include "Animation/Skeleton.h"
#include "AlphaBlend.h"
#include "Curves/CurveFloat.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "Dom/JsonObject.h"
#include "JsonObjectConverter.h"
#include "Serialization/JsonSerializer.h"

FString UAlsAnimationGraphLibrary::ReadBakedStateMachines(UAnimBlueprint* Blueprint)
{
    const IAnimClassInterface* Class = Blueprint && Blueprint->GeneratedClass
        ? IAnimClassInterface::GetFromClass(Blueprint->GeneratedClass) : nullptr;
    if (!Class) return TEXT("");
    const auto ExportObject = FJsonObjectConverter::CustomExportCallback::CreateLambda(
        [](FProperty* Property, const void* Value) -> TSharedPtr<FJsonValue>
        {
            if (const auto* ObjectProperty = CastField<FObjectPropertyBase>(Property))
            {
                const UObject* Object = ObjectProperty->GetObjectPropertyValue(Value);
                return MakeShared<FJsonValueString>(Object ? Object->GetPathName() : TEXT(""));
            }
            return nullptr;
        });
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), Blueprint->GetPathName());
    TArray<TSharedPtr<FJsonValue>> Machines, Curves;
    TSet<UCurveFloat*> CurveSet;
    TSet<UBlendProfile*> ProfileSet;
    int32 Index = 0;
    for (const auto& Machine : Class->GetBakedStateMachines())
    {
        const auto Data = MakeShared<FJsonObject>();
        if (!FJsonObjectConverter::UStructToJsonObject(FBakedAnimationStateMachine::StaticStruct(),
            &Machine, Data, 0, 0, &ExportObject)) return TEXT("");
        Data->SetNumberField(TEXT("machineIndex"), Index++);
        Machines.Add(MakeShared<FJsonValueObject>(Data));
        for (const auto& Edge : Machine.Transitions) if (Edge.CustomCurve) CurveSet.Add(Edge.CustomCurve);
        for (const auto& Edge : Machine.Transitions) if (Edge.BlendProfile) ProfileSet.Add(Edge.BlendProfile);
    }
    TArray<UCurveFloat*> Ordered = CurveSet.Array();
    Ordered.Sort([](const UCurveFloat& A, const UCurveFloat& B) { return A.GetPathName() < B.GetPathName(); });
    for (UCurveFloat* Curve : Ordered)
    {
        const auto Item = MakeShared<FJsonObject>(); const auto Data = MakeShared<FJsonObject>();
        Item->SetStringField(TEXT("path"), Curve->GetPathName());
        if (!FJsonObjectConverter::UStructToJsonObject(FRichCurve::StaticStruct(), &Curve->FloatCurve,
            Data, 0, 0, &ExportObject)) return TEXT("");
        Item->SetObjectField(TEXT("curve"), Data);
        TArray<TSharedPtr<FJsonValue>> Samples;
        for (int32 SampleIndex = 0; SampleIndex <= 200; ++SampleIndex)
        {
            const float Time = static_cast<float>(SampleIndex) / 200.f;
            const auto Sample = MakeShared<FJsonObject>();
            Sample->SetNumberField(TEXT("input"), Time);
            Sample->SetNumberField(TEXT("value"), Curve->GetFloatValue(Time));
            Samples.Add(MakeShared<FJsonValueObject>(Sample));
        }
        Item->SetArrayField(TEXT("verification"), Samples);
        Curves.Add(MakeShared<FJsonValueObject>(Item));
    }
    Root->SetArrayField(TEXT("bakedMachines"), Machines);
    Root->SetArrayField(TEXT("curves"), Curves);
    TArray<TSharedPtr<FJsonValue>> Profiles;
    TArray<UBlendProfile*> OrderedProfiles = ProfileSet.Array();
    OrderedProfiles.Sort([](const UBlendProfile& A, const UBlendProfile& B) { return A.GetPathName() < B.GetPathName(); });
    for (UBlendProfile* Profile : OrderedProfiles)
    {
        USkeleton* Skeleton = Profile->GetSkeleton();
        if (!Skeleton) return TEXT("");
        const auto Item = MakeShared<FJsonObject>();
        Item->SetStringField(TEXT("path"), Profile->GetPathName());
        Item->SetStringField(TEXT("skeleton"), Skeleton->GetPathName());
        Item->SetNumberField(TEXT("mode"), static_cast<int32>(Profile->GetMode()));
        TArray<TSharedPtr<FJsonValue>> Bones;
        const auto& Reference = Skeleton->GetReferenceSkeleton();
        for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone)
        {
            const auto Data = MakeShared<FJsonObject>(); const FName Name = Reference.GetBoneName(Bone);
            Data->SetStringField(TEXT("name"), Name.ToString());
            Data->SetNumberField(TEXT("parent"), Reference.GetParentIndex(Bone));
            Data->SetNumberField(TEXT("entry"), Profile->GetEntryIndex(Name));
            Data->SetNumberField(TEXT("scale"), Profile->GetBoneBlendScale(Name));
            Bones.Add(MakeShared<FJsonValueObject>(Data));
        }
        Item->SetArrayField(TEXT("bones"), Bones);
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
            const auto Case = MakeShared<FJsonObject>(); Case->SetNumberField(TEXT("alpha"), Blend.GetBlendedValue());
            TArray<TSharedPtr<FJsonValue>> Incoming, Outgoing;
            for (int32 Entry = 0; Entry < Profile->GetNumBlendEntries(); ++Entry)
            {
                Incoming.Add(MakeShared<FJsonValueNumber>(Data[0].PerBoneBlendData[Entry]));
                Outgoing.Add(MakeShared<FJsonValueNumber>(Data[1].PerBoneBlendData[Entry]));
            }
            Case->SetArrayField(TEXT("incoming"), Incoming); Case->SetArrayField(TEXT("outgoing"), Outgoing);
            Cases.Add(MakeShared<FJsonValueObject>(Case));
        }
        Item->SetArrayField(TEXT("nativeCases"), Cases);
        Profiles.Add(MakeShared<FJsonValueObject>(Item));
    }
    Root->SetArrayField(TEXT("blendProfiles"), Profiles);
    TArray<UEdGraph*> Graphs; Blueprint->GetAllGraphs(Graphs);
    TArray<TSharedPtr<FJsonValue>> EditorNodes;
    for (const UEdGraph* Graph : Graphs)
    for (const UEdGraphNode* Node : Graph->Nodes)
    {
        const FString Type = Node->GetClass()->GetName();
        if (Type != TEXT("AnimStateNode") && Type != TEXT("AnimStateTransitionNode") &&
            Type != TEXT("AnimGraphNode_StateMachine")) continue;
        const auto Item = MakeShared<FJsonObject>(); const auto Properties = MakeShared<FJsonObject>();
        Item->SetStringField(TEXT("path"), Node->GetPathName());
        Item->SetStringField(TEXT("class"), Type);
        for (const TCHAR* Name : { TEXT("Node"), TEXT("BoundGraph"), TEXT("EditorStateMachineGraph"),
            TEXT("PriorityOrder"), TEXT("CrossfadeDuration"), TEXT("BlendMode"), TEXT("LogicType"),
            TEXT("CustomBlendCurve"), TEXT("BlendProfileWrapper"), TEXT("CustomTransitionGraph"),
            TEXT("bAutomaticRuleBasedOnSequencePlayerInState"), TEXT("bDisabled"),
            TEXT("MinTimeBeforeReentry"), TEXT("bAlwaysResetOnEntry") })
        {
            if (FProperty* Property = Node->GetClass()->FindPropertyByName(Name))
            {
                const auto Value = FJsonObjectConverter::UPropertyToJsonValue(Property,
                    Property->ContainerPtrToValuePtr<void>(Node), 0, 0, &ExportObject);
                if (!Value.IsValid()) return TEXT("");
                Properties->SetField(Name, Value);
            }
        }
        Item->SetObjectField(TEXT("properties"), Properties); EditorNodes.Add(MakeShared<FJsonValueObject>(Item));
    }
    Root->SetArrayField(TEXT("editorStateNodes"), EditorNodes);
    FString Text;
    return FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) ? Text : FString();
}
