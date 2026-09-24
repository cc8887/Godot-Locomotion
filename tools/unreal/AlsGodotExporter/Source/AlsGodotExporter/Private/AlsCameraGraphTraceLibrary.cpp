#include "AlsAnimationGraphLibrary.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Editor.h"
#include "GameFramework/Actor.h"
#include "GameplayTagContainer.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Misc/EngineVersion.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FCameraProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& P, UAnimInstance* I, float D) { (P.*&FCameraProxyAccess::PreUpdate)(I, D); }
    static void UpdateRoot(FAnimInstanceProxy& P) { (P.*&FCameraProxyAccess::UpdateAnimation)(); }
    static void Post(FAnimInstanceProxy& P, UAnimInstance* I) { (P.*&FCameraProxyAccess::PostUpdate)(I); }
};
struct FCameraInstanceAccess : UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* I) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I); }
};
}

bool UAlsAnimationGraphLibrary::ExportCameraGraphTrace(const FString& RequestPath, const FString& OutputPath)
{
    if (FPaths::IsRelative(RequestPath) || FPaths::IsRelative(OutputPath) || RequestPath == OutputPath) return false;
    FString Text; TSharedPtr<FJsonObject> Request;
    if (!FFileHelper::LoadFileToString(Text, *RequestPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Request) ||
        Request->GetIntegerField(TEXT("schemaVersion")) != 1) return false;
    auto* Blueprint = LoadObject<UAnimBlueprint>(nullptr, TEXT("/ALS/ALSCamera/AB_Als_Camera.AB_Als_Camera"));
    auto* ComponentClass = LoadClass<USkeletalMeshComponent>(nullptr,
        TEXT("/ALS/ALSCamera/B_Als_CameraComponent.B_Als_CameraComponent_C"));
    auto* Defaults = ComponentClass ? Cast<USkeletalMeshComponent>(ComponentClass->GetDefaultObject()) : nullptr;
    auto* Mesh = Defaults ? Defaults->GetSkeletalMeshAsset() : nullptr;
    UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    if (!Blueprint || !Blueprint->GeneratedClass || !Mesh || !Mesh->GetSkeleton() || !World) return false;
    FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
    Spawn.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
    AActor* Owner = World->SpawnActor<AActor>(Spawn); if (!Owner) return false;
    Owner->SetActorTickEnabled(false);
    ON_SCOPE_EXIT { World->DestroyActor(Owner); };
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Mesh->GetSkeleton()->GetReferenceSkeleton().GetNum(); ++Bone)
        Required.Add(static_cast<FBoneIndexType>(Bone));
    TArray<TSharedPtr<FJsonValue>> Traces; int32 Total = 0;
    for (const auto& TraceValue : Request->GetArrayField(TEXT("traces")))
    {
        const auto Input = TraceValue->AsObject();
        TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner));
        Component->SetSkeletalMesh(Mesh);
        TStrongObjectPtr<UAnimInstance> Instance(NewObject<UAnimInstance>(Component.Get(), Blueprint->GeneratedClass));
        Instance->InitializeAnimation(true);
        auto& Proxy = FCameraInstanceAccess::Proxy(Instance.Get());
        Proxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Mesh->GetSkeleton());
        TArray<TSharedPtr<FJsonValue>> Frames; int32 Serial = 0;
        for (const auto& FrameValue : Input->GetArrayField(TEXT("frames")))
        {
            FMemMark Mark(FMemStack::Get());
            const TGuardValue<uint64> GlobalFrame(GFrameCounter, static_cast<uint64>(400000 + Serial + 1));
            const auto Frame = FrameValue->AsObject();
            const float Delta = static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            if (!FMath::IsFinite(Delta) || Delta < 0) return false;
            FCameraProxyAccess::Pre(Proxy, Instance.Get(), Delta);
            for (const TCHAR* Name : {TEXT("RotationMode"), TEXT("Stance"), TEXT("Gait"), TEXT("ViewMode"), TEXT("LocomotionAction")})
            {
                auto* Property = FindFProperty<FStructProperty>(Instance->GetClass(), Name);
                if (!Property || Property->Struct != FGameplayTag::StaticStruct()) return false;
                const FString Value = Frame->GetStringField(Name);
                const auto Tag = Value.IsEmpty() ? FGameplayTag() : FGameplayTag::RequestGameplayTag(FName(*Value), false);
                if (!Value.IsEmpty() && !Tag.IsValid()) return false;
                *Property->ContainerPtrToValuePtr<FGameplayTag>(Instance.Get()) = Tag;
            }
            auto* Shoulder = FindFProperty<FBoolProperty>(Instance->GetClass(), TEXT("bRightShoulder"));
            if (!Shoulder) return false;
            Shoulder->SetPropertyValue_InContainer(Instance.Get(), Frame->GetBoolField(TEXT("RightShoulder")));
            // Controlled graph inputs intentionally bypass NativeUpdateAnimation,
            // which would overwrite them from Character/CDO. Nodes/links remain original.
            FCameraProxyAccess::UpdateRoot(Proxy); Proxy.FlipBufferWriteIndex();
            FCameraProxyAccess::Post(Proxy, Instance.Get());
            FPoseContext Pose(&Proxy, true); FBlendedHeapCurve Curve;
            UE::Anim::FHeapAttributeContainer Attributes;
            FParallelEvaluationData Evaluation{Curve, Pose.Pose, Attributes};
            Instance->PreEvaluateAnimation();
            Instance->ParallelEvaluateAnimation(false, Mesh, Evaluation);
            const auto Curves = MakeShared<FJsonObject>();
            Curve.ForEachElement([&](const auto& C) { Curves->SetNumberField(C.Name.ToString(), C.Value); });
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("serial"), ++Serial);
            Row->SetObjectField(TEXT("input"), Frame);
            Row->SetObjectField(TEXT("curves"), Curves);
            Frames.Add(MakeShared<FJsonValueObject>(Row)); ++Total;
        }
        const auto Trace = MakeShared<FJsonObject>();
        Trace->SetStringField(TEXT("name"), Input->GetStringField(TEXT("name")));
        Trace->SetArrayField(TEXT("frames"), Frames); Traces.Add(MakeShared<FJsonValueObject>(Trace));
        Instance->UninitializeAnimation();
    }
    const auto Result = MakeShared<FJsonObject>();
    Result->SetNumberField(TEXT("schemaVersion"), 1);
    Result->SetStringField(TEXT("source"), Blueprint->GetPathName());
    Result->SetStringField(TEXT("mesh"), Mesh->GetPathName());
    Result->SetStringField(TEXT("engine"), FEngineVersion::Current().ToString());
    Result->SetStringField(TEXT("requestDigest"), Request->GetStringField(TEXT("requestDigest")));
    Result->SetStringField(TEXT("scope"), TEXT("Unmodified Refactored Camera AnimGraph; controlled tags/shoulder; excludes NativeUpdateAnimation, character motor and camera scene queries."));
    Result->SetArrayField(TEXT("traces"), Traces);
    FString Json;
    if (!FJsonSerializer::Serialize(Result, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)) ||
        !FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp, Display, TEXT("ALS_CAMERA_GRAPH_NATIVE_OK traces=%d frames=%d assets_saved=0"), Traces.Num(), Total);
    return true;
}
