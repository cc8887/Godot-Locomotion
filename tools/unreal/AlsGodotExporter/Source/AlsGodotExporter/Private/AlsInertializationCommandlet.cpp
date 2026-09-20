#include "AlsInertializationCommandlet.h"

#include "Animation/AnimInstance.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimBlueprintGeneratedClass.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "EdGraph/EdGraphNode.h"
#include "Misc/FileHelper.h"
#include "Misc/MemStack.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace AlsInertialProbe
{
TArray<TSharedPtr<FJsonValue>> Numbers(std::initializer_list<double> Values)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
    return Result;
}
TSharedRef<FJsonObject> TransformJson(const FTransform& Transform)
{
    const auto Row = MakeShared<FJsonObject>();
    const FVector P = Transform.GetTranslation();
    const FQuat R = Transform.GetRotation();
    const FVector S = Transform.GetScale3D();
    Row->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
    Row->SetArrayField(TEXT("rotation"), Numbers({R.X, R.Y, R.Z, R.W}));
    Row->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z}));
    return Row;
}
TArray<TSharedPtr<FJsonValue>> PoseJson(const FCompactPose& Pose)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (auto Bone : Pose.ForEachBoneIndex()) Result.Add(MakeShared<FJsonValueObject>(TransformJson(Pose[Bone])));
    return Result;
}
struct FProbeSource : FAnimNode_Base
{
    FCompactPose Pose;
    FBlendedCurve Curves;
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    {
        Output.Pose.CopyBonesFrom(Pose);
        Output.Curve.CopyFrom(Curves);
    }
};
struct FProbeProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    using FAnimInstanceProxy::PreUpdate;
};
}

UAlsInertializationCommandlet::UAlsInertializationCommandlet()
{
    IsClient = false; IsServer = false; IsEditor = true; LogToConsole = true;
}

int32 UAlsInertializationCommandlet::Main(const FString& Params)
{
    using namespace AlsInertialProbe;
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    const auto Sequence = LoadObject<UAnimSequence>(nullptr,
        TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_F"));
    if (!Sequence || !Sequence->GetSkeleton()) return 2;
    const auto Blueprint = LoadObject<UAnimBlueprint>(nullptr,
        TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP"));
    if (!Blueprint || !Blueprint->GeneratedClass) return 2;
    const auto SourceNode = FindObject<UEdGraphNode>(nullptr, *(Blueprint->GetPathName() + TEXT(":BaseLayer.AnimGraphNode_Inertialization_0")));
    const auto Generated = Cast<UAnimBlueprintGeneratedClass>(Blueprint->GeneratedClass);
    auto Template = SourceNode && Generated ? Generated->GetPropertyInstance<FAnimNode_Inertialization>(Generated->GetDefaultObject(), SourceNode->NodeGuid) : nullptr;
    if (!Template) { UE_LOG(LogTemp, Error, TEXT("Cannot resolve BaseLayer inertialization node by source GUID (source=%s)"), SourceNode ? *SourceNode->GetPathName() : TEXT("missing")); return 4; }
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE FAnimNode_Inertialization Update/Evaluate; synthetic four-bone poses, default filters/profiles, no root-motion attributes"));
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const int32 Hz : {30, 60, 120})
    for (int32 Scenario = 0; Scenario < 5; ++Scenario)
    {
        const FMemMark Mark(FMemStack::Get());
        auto Component = NewObject<USkeletalMeshComponent>();
        Component->SetTeleportDistanceThreshold(500.f);
        auto Instance = NewObject<UAnimInstance>(Component);
        FProbeProxy Proxy(Instance);
        Proxy.Initialize(Instance);
        TArray<FBoneIndexType> Required = {0, 1, 2, 3};
        Proxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Sequence->GetSkeleton());
        FProbeSource Source;
        Source.Pose.SetBoneContainer(&Proxy.GetRequiredBones());
        Source.Pose.ResetToRefPose();
        FAnimNode_Inertialization Node = *Template;
        Node.Source.SetLinkNode(&Source);
        Node.Initialize_AnyThread(FAnimationInitializeContext(&Proxy));
        Node.CacheBones_AnyThread(FAnimationCacheBonesContext(&Proxy));
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < 60; ++Frame)
        {
            const float Delta = Scenario == 3 && (Frame == 8 || Frame == 9) ? 0.f : 1.f / Hz;
            const int32 Updates = Scenario == 3 && Frame == 12 ? 3 : 1;
            const bool Teleport = Scenario == 4 && Frame >= 20;
            const FTransform ComponentTransform(FRotator(0, Scenario == 2 && Frame >= 4 ? 77.f : 0.f, 0).Quaternion(),
                FVector(Teleport ? 1000 : 0, 0, 0), FVector::OneVector);
            Component->SetWorldTransform(ComponentTransform);
            Proxy.PreUpdate(Instance, Delta);
            Source.Curves.Empty();
            Source.Curves.Set(TEXT("Both"), .2f + Frame * .013f);
            if (Frame < 4) Source.Curves.Set(TEXT("Outgoing"), .7f);
            else Source.Curves.Set(TEXT("Incoming"), -.3f);
            for (auto Bone : Source.Pose.ForEachBoneIndex())
            {
                const int32 Index = Bone.GetInt();
                const float Switch = Frame >= 4 ? 1.f : 0.f;
                FQuat Rotation = FRotator(Index * 17 + Frame * .3, Switch * 72 - Index * 11, Frame * -.4).Quaternion();
                if (Frame % 2) Rotation *= -1.0;
                Source.Pose[Bone] = FTransform(Rotation, FVector(Index * .1 + Frame * .01 - Switch * .4, Index * -.2, Switch * .3),
                    FVector(1 + Index * .03, 1 + Frame * .001, 1 - Switch * .1));
            }
            TArray<TSharedPtr<FJsonValue>> Requests;
            auto Request = [&](float Duration) { Node.RequestInertialization(Duration, nullptr); Requests.Add(MakeShared<FJsonValueNumber>(Duration)); };
            if (Frame == 4 || (Scenario == 0 && Frame == 1)) Request(.2f);
            if (Scenario == 1 && (Frame == 6 || Frame == 7 || Frame == 10)) { Request(.3f); Request(.1f); }
            if (Scenario == 4 && Frame == 20) Request(.2f);
            FAnimationUpdateSharedContext Shared;
            for (int32 Update = 0; Update < Updates; ++Update) Node.Update_AnyThread(FAnimationUpdateContext(&Proxy, Delta, &Shared));
            FPoseContext Output(&Proxy);
            Node.Evaluate_AnyThread(Output);
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("delta"), Delta);
            Row->SetNumberField(TEXT("updates"), Updates);
            Row->SetArrayField(TEXT("requests"), Requests);
            Row->SetObjectField(TEXT("component"), TransformJson(ComponentTransform));
            Row->SetArrayField(TEXT("input"), PoseJson(Source.Pose));
            Row->SetArrayField(TEXT("output"), PoseJson(Output.Pose));
            const auto Inputs = MakeShared<FJsonObject>();
            const auto Outputs = MakeShared<FJsonObject>();
            Source.Curves.ForEachElement([&](const auto& Curve) { Inputs->SetNumberField(Curve.Name.ToString(), Curve.Value); });
            Output.Curve.ForEachElement([&](const auto& Curve) { Outputs->SetNumberField(Curve.Name.ToString(), Curve.Value); });
            Row->SetObjectField(TEXT("inputCurves"), Inputs);
            Row->SetObjectField(TEXT("outputCurves"), Outputs);
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Trace = MakeShared<FJsonObject>();
        Trace->SetNumberField(TEXT("hz"), Hz);
        Trace->SetNumberField(TEXT("scenario"), Scenario);
        Trace->SetNumberField(TEXT("teleportThreshold"), Component->GetTeleportDistanceThreshold());
        Trace->SetArrayField(TEXT("frames"), Frames);
        Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    Root->SetArrayField(TEXT("traces"), Traces);
    FString Text;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return 3;
    UE_LOG(LogTemp, Display, TEXT("ALS_INERTIALIZATION_OK traces=%d frames=%d bones=4 assets_saved=0"), Traces.Num(), Traces.Num() * 60);
    return 0;
}
