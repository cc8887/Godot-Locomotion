#include "AlsLyraGraphLibrary.h"

#include "Animation/AnimClassInterface.h"
#include "Animation/AnimNode_AssetPlayerBase.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "Animation/AnimNodeFunctionRef.h"
#include "Animation/AnimNodeData.h"
#include "Animation/BlendSpace.h"
#include "Animation/Skeleton.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "AnimNodes/AnimNode_BlendSpacePlayer.h"
#include "Dom/JsonObject.h"
#include "JsonObjectConverter.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UnrealType.h"

namespace
{
FString ObjectPath(const UObject* Object) { return Object ? Object->GetPathName() : TEXT(""); }
struct FSourceNodeAccess : FAnimNode_Base
{
    static FName FunctionName(const FAnimNode_Base& Node, FName Name)
    {
        // The native function accessors are private. Use their protected
        // folded-data path via a base member pointer, without casting a CDO
        // node to a derived object or modifying the engine.
        using FRead = const FAnimNodeFunctionRef& (FAnimNode_Base::*)(UE::Anim::FNodeDataId, const UObject*) const;
        const FRead Read = &FSourceNodeAccess::GetData<FAnimNodeFunctionRef>;
        return (Node.*Read)(
            UE::Anim::FNodeDataId(Name, &Node, FAnimNode_Base::StaticStruct()), nullptr).GetFunctionName();
    }
};
FName FunctionName(const FAnimNode_Base& Node, FName Name) { return FSourceNodeAccess::FunctionName(Node, Name); }
TSharedPtr<FJsonObject> Functions(const FAnimNode_Base& Node)
{
    const auto Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("initialUpdate"), FunctionName(Node, TEXT("InitialUpdateFunction")).ToString());
    Result->SetStringField(TEXT("becomeRelevant"), FunctionName(Node, TEXT("BecomeRelevantFunction")).ToString());
    Result->SetStringField(TEXT("update"), FunctionName(Node, TEXT("UpdateFunction")).ToString());
    return Result;
}
}

FString UAlsLyraGraphLibrary::ReadSourceNodes(UClass* AnimationClass)
{
    const IAnimClassInterface* Class = AnimationClass ? IAnimClassInterface::GetFromClass(AnimationClass) : nullptr;
    if (!Class) return {};
    UObject* CDO = AnimationClass->GetDefaultObject();
    const auto& Properties = Class->GetAnimNodeProperties();
    const auto ExportObject = FJsonObjectConverter::CustomExportCallback::CreateLambda(
        [](FProperty* Property, const void* Value) -> TSharedPtr<FJsonValue>
        {
            if (const auto* Object = CastField<FObjectPropertyBase>(Property))
                return MakeShared<FJsonValueString>(ObjectPath(Object->GetObjectPropertyValue(Value)));
            return nullptr;
        });
    TArray<TSharedPtr<FJsonValue>> Sources, Callbacks, Graphs, Layers;
    for (int32 PropertyIndex = 0; PropertyIndex < Properties.Num(); ++PropertyIndex)
    {
        const FStructProperty* Property = Properties[PropertyIndex];
        const auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_Base>(CDO);
        const int32 NodeIndex = Properties.Num() - 1 - PropertyIndex;
        if (!FunctionName(*Node, TEXT("InitialUpdateFunction")).IsNone() ||
            !FunctionName(*Node, TEXT("BecomeRelevantFunction")).IsNone() || !FunctionName(*Node, TEXT("UpdateFunction")).IsNone())
        {
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("nodeIndex"), NodeIndex);
            Row->SetStringField(TEXT("property"), Property->GetName());
            Row->SetStringField(TEXT("type"), Property->Struct->GetPathName());
            Row->SetObjectField(TEXT("functions"), Functions(*Node));
            Callbacks.Add(MakeShared<FJsonValueObject>(Row));
        }
        if (!Property->Struct->IsChildOf(FAnimNode_AssetPlayerBase::StaticStruct())) continue;
        const auto* Player = Property->ContainerPtrToValuePtr<FAnimNode_AssetPlayerBase>(CDO);
        const auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("nodeIndex"), NodeIndex);
        Row->SetNumberField(TEXT("propertyIndex"), PropertyIndex);
        Row->SetNumberField(TEXT("nativeNodeIndex"), Node->GetNodeIndex());
        Row->SetStringField(TEXT("property"), Property->GetName());
        Row->SetStringField(TEXT("type"), Property->Struct->GetPathName());
        Row->SetObjectField(TEXT("functions"), Functions(*Node));
        Row->SetStringField(TEXT("group"), Player->GetGroupName().ToString());
        Row->SetNumberField(TEXT("role"), Player->GetGroupRole());
        Row->SetNumberField(TEXT("method"), static_cast<int32>(Player->GetGroupMethod()));
        Row->SetBoolField(TEXT("looping"), Player->IsLooping());
        Row->SetBoolField(TEXT("ignoreRelevancy"), Player->GetIgnoreForRelevancyTest());
        Row->SetBoolField(TEXT("overridePositionWhenJoining"), Player->GetOverridePositionWhenJoiningSyncGroupAsLeader());
        const UAnimationAsset* Asset = Player->GetAnimAsset();
        Row->SetStringField(TEXT("asset"), ObjectPath(Asset));
        Row->SetStringField(TEXT("skeleton"), Asset ? ObjectPath(Asset->GetSkeleton()) : TEXT(""));
        if (Property->Struct == FAnimNode_SequencePlayer::StaticStruct())
        {
            const auto* Sequence = Property->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(CDO);
            Row->SetStringField(TEXT("kind"), TEXT("SequencePlayer"));
            Row->SetNumberField(TEXT("playRate"), Sequence->GetPlayRate());
            Row->SetNumberField(TEXT("playRateBasis"), Sequence->GetPlayRateBasis());
            Row->SetNumberField(TEXT("startPosition"), Sequence->GetStartPosition());
            Row->SetBoolField(TEXT("startFromMatchingPose"), Sequence->GetStartFromMatchingPose());
            const auto Clamp = MakeShared<FJsonObject>();
            if (!FJsonObjectConverter::UStructToJsonObject(FInputScaleBiasClampConstants::StaticStruct(),
                &Sequence->GetPlayRateScaleBiasClampConstants(), Clamp, 0, 0, &ExportObject)) return {};
            Row->SetObjectField(TEXT("playRateScaleBiasClamp"), Clamp);
        }
        else if (Property->Struct->IsChildOf(FAnimNode_SequenceEvaluatorBase::StaticStruct()))
        {
            const auto* Sequence = Property->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluatorBase>(CDO);
            Row->SetStringField(TEXT("kind"), TEXT("SequenceEvaluator"));
            Row->SetNumberField(TEXT("explicitTime"), Sequence->GetExplicitTime());
            Row->SetNumberField(TEXT("startPosition"), Sequence->GetStartPosition());
            Row->SetNumberField(TEXT("reinitialization"), Sequence->GetReinitializationBehavior());
            Row->SetBoolField(TEXT("teleport"), Sequence->GetTeleportToExplicitTime());
        }
        else if (Property->Struct->IsChildOf(FAnimNode_BlendSpacePlayerBase::StaticStruct()))
        {
            const auto* Blend = Property->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayerBase>(CDO);
            Row->SetStringField(TEXT("kind"), TEXT("BlendSpacePlayer"));
            Row->SetNumberField(TEXT("playRate"), Blend->GetPlayRate());
            Row->SetNumberField(TEXT("startPosition"), Blend->GetStartPosition());
            Row->SetBoolField(TEXT("resetOnAssetChange"), Blend->ShouldResetPlayTimeWhenBlendSpaceChanges());
            Row->SetBoolField(TEXT("evaluator"), Blend->IsEvaluator());
            Row->SetBoolField(TEXT("teleport"), Blend->ShouldTeleportToTime());
            const FVector Position = Blend->GetPosition();
            Row->SetArrayField(TEXT("position"), {MakeShared<FJsonValueNumber>(Position.X),
                MakeShared<FJsonValueNumber>(Position.Y), MakeShared<FJsonValueNumber>(Position.Z)});
            TArray<TSharedPtr<FJsonValue>> Samples;
            if (const UBlendSpace* Space = Blend->GetBlendSpace())
            for (const auto& Sample : Space->GetBlendSamples())
            {
                const auto Data = MakeShared<FJsonObject>();
                Data->SetStringField(TEXT("animation"), ObjectPath(Sample.Animation));
                Data->SetNumberField(TEXT("rateScale"), Sample.RateScale);
                Data->SetArrayField(TEXT("position"), {MakeShared<FJsonValueNumber>(Sample.SampleValue.X),
                    MakeShared<FJsonValueNumber>(Sample.SampleValue.Y), MakeShared<FJsonValueNumber>(Sample.SampleValue.Z)});
                Samples.Add(MakeShared<FJsonValueObject>(Data));
            }
            Row->SetArrayField(TEXT("samples"), Samples);
        }
        else
        {
            UE_LOG(LogTemp, Error, TEXT("Unsupported Lyra source node %s"), *Property->Struct->GetPathName());
            return {};
        }
        Sources.Add(MakeShared<FJsonValueObject>(Row));
    }
    for (const auto& Pair : Class->GetGraphAssetPlayerInformation())
    {
        const auto Graph = MakeShared<FJsonObject>();
        Graph->SetStringField(TEXT("name"), Pair.Key.ToString());
        TArray<TSharedPtr<FJsonValue>> Indices;
        for (int32 Index : Pair.Value.PlayerNodeIndices) Indices.Add(MakeShared<FJsonValueNumber>(Index));
        Graph->SetArrayField(TEXT("players"), Indices); Graphs.Add(MakeShared<FJsonValueObject>(Graph));
    }
    for (const auto& Function : Class->GetAnimBlueprintFunctions())
    {
        const auto Layer = MakeShared<FJsonObject>();
        Layer->SetStringField(TEXT("name"), Function.Name.ToString());
        Layer->SetStringField(TEXT("group"), Function.Group.ToString());
        Layer->SetBoolField(TEXT("implemented"), Function.bImplemented);
        Layer->SetNumberField(TEXT("rootIndex"), Function.OutputPoseNodeIndex);
        Layer->SetStringField(TEXT("rootProperty"), Function.OutputPoseNodeProperty ? Function.OutputPoseNodeProperty->GetName() : TEXT(""));
        Layers.Add(MakeShared<FJsonValueObject>(Layer));
    }
    const auto Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("class"), AnimationClass->GetPathName());
    Result->SetNumberField(TEXT("nodeCount"), Properties.Num());
    Result->SetArrayField(TEXT("sources"), Sources); Result->SetArrayField(TEXT("callbacks"), Callbacks);
    Result->SetArrayField(TEXT("graphs"), Graphs); Result->SetArrayField(TEXT("layers"), Layers);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}
