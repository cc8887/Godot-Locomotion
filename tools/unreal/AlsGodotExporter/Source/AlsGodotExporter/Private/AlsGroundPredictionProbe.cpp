#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "Components/BoxComponent.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "State/AlsInAirState.h"
#include "State/AlsLocomotionAnimationState.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/UnrealType.h"

bool UAlsAnimationGraphLibrary::ExportGroundPrediction(const FString& OutputPath)
{
    if (FPaths::IsRelative(OutputPath) || IFileManager::Get().FileExists(*OutputPath)) return false;
    auto* AnimationClass = LoadClass<UAlsAnimationInstance>(nullptr, TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C"));
    if (!AnimationClass) return false;
    const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None,
        nullptr, false, ERHIFeatureLevel::Num, &Initialization));
    if (!World.IsValid() || !World->IsGameWorld()) return false;
    auto* Floor = World->SpawnActor<AActor>(); auto* Owner = World->SpawnActor<AActor>();
    if (!Floor || !Owner) { World->DestroyWorld(false); return false; }
    auto* Box = NewObject<UBoxComponent>(Floor); Floor->SetRootComponent(Box); Floor->AddInstanceComponent(Box);
    Box->SetBoxExtent(FVector(10000, 10000, 50)); Box->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
    Box->SetCollisionObjectType(ECC_WorldStatic); Box->SetCollisionResponseToAllChannels(ECR_Block);
    Box->RegisterComponent(); Box->SetWorldLocation(FVector(0, 0, -50));
    auto* Mesh = NewObject<USkeletalMeshComponent>(Owner); Owner->AddInstanceComponent(Mesh); Mesh->RegisterComponent();
    TStrongObjectPtr<UAlsAnimationInstance> Instance(NewObject<UAlsAnimationInstance>(Mesh, AnimationClass));
    auto* LocomotionProperty = FindFProperty<FStructProperty>(UAlsAnimationInstance::StaticClass(), TEXT("LocomotionState"));
    auto* AirProperty = FindFProperty<FStructProperty>(UAlsAnimationInstance::StaticClass(), TEXT("InAirState"));
    auto* SettingsProperty = FindFProperty<FObjectPropertyBase>(UAlsAnimationInstance::StaticClass(), TEXT("Settings"));
    auto* SettingsAsset = SettingsProperty ? Cast<UAlsAnimationInstanceSettings>(SettingsProperty->GetObjectPropertyValue_InContainer(Instance.Get())) : nullptr;
    auto* Refresh = Instance->FindFunction(TEXT("RefreshInAir"));
    if (!LocomotionProperty || !AirProperty || !Refresh || Instance->GetWorld() != World.Get() || !SettingsAsset)
    { World->DestroyWorld(false); return false; }
    auto& Locomotion = *LocomotionProperty->ContainerPtrToValuePtr<FAlsLocomotionAnimationState>(Instance.Get());
    auto& Air = *AirProperty->ContainerPtrToValuePtr<FAlsInAirState>(Instance.Get());
    const auto& Settings = SettingsAsset->InAir;
    Locomotion.CapsuleRadius = 35; Locomotion.CapsuleHalfHeight = 90; Locomotion.WalkableFloorAngleCos = .7f;
    TArray<TSharedPtr<FJsonValue>> Rows;
    bool Valid = true;
    for (const float Height : {89.f, 90.f, 100.f, 200.f, 600.f})
    for (const float Vertical : {-199.99f, -200.f, -201.f, -500.f, -1500.f, -4000.f, -5000.f})
    for (const float Horizontal : {0.f, 350.f})
    for (const float Scale : {.5f, 1.f, 1.5f})
    for (const float Block : {-.5f, 0.f, .3f, .99995f, 1.f, 1.5f})
    {
        Locomotion.LocationWorldSpace = FVector(0, 0, Height);
        Locomotion.VelocityWorldSpace = FVector(Horizontal, 0, Vertical); Locomotion.ScaleWorldSpace = Scale;
        Instance->OverrideCurveValue(TEXT("GroundPredictionBlock"), Block);
        Air.VerticalVelocityWorldSpace = 123456.f;
        Instance->ProcessEvent(Refresh, nullptr);
        Valid &= Air.VerticalVelocityWorldSpace == Vertical;
        const float Allowance = 1.f - Instance->GetCurveValueClamped01(TEXT("GroundPredictionBlock"));
        const bool Enabled = Vertical <= -200.f && Allowance > UE_KINDA_SMALL_NUMBER;
        FVector Sweep = FVector::ZeroVector;
        FHitResult Hit;
        if (Enabled)
        {
            FVector Direction = Locomotion.VelocityWorldSpace; Direction.Z = FMath::Clamp(Direction.Z, -4000.0, -200.0); Direction.Normalize();
            Sweep = Direction * FMath::GetMappedRangeValueClamped(FVector2f(-200, -4000), FVector2f(150, 2000), Vertical) * Scale;
            World->SweepSingleByChannel(Hit, Locomotion.LocationWorldSpace, Locomotion.LocationWorldSpace + Sweep,
                FQuat::Identity, Settings.GroundPredictionSweepChannel, FCollisionShape::MakeCapsule(35, 90),
                {TEXT("GroundPredictionOracle"), false, Owner}, Settings.GroundPredictionSweepResponses);
        }
        auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("height"), Height); Row->SetNumberField(TEXT("vertical"), Vertical);
        Row->SetNumberField(TEXT("horizontal"), Horizontal); Row->SetNumberField(TEXT("scale"), Scale);
        Row->SetNumberField(TEXT("block"), Block); Row->SetNumberField(TEXT("allowance"), Allowance);
        Row->SetBoolField(TEXT("enabled"), Enabled); Row->SetBoolField(TEXT("blocking"), Hit.bBlockingHit);
        Row->SetBoolField(TEXT("penetrating"), Hit.bStartPenetrating); Row->SetNumberField(TEXT("time"), Hit.Time);
        Row->SetArrayField(TEXT("sweep"), {MakeShared<FJsonValueNumber>(Sweep.X), MakeShared<FJsonValueNumber>(Sweep.Y), MakeShared<FJsonValueNumber>(Sweep.Z)});
        Row->SetArrayField(TEXT("normal"), {MakeShared<FJsonValueNumber>(Hit.ImpactNormal.X), MakeShared<FJsonValueNumber>(Hit.ImpactNormal.Y), MakeShared<FJsonValueNumber>(Hit.ImpactNormal.Z)});
        Row->SetNumberField(TEXT("result"), Air.GroundPredictionAmount);
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Instance.Reset(); World->DestroyWorld(false);
    if (!Valid) return false;
    auto Root = MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("Actual AB_Als_C RefreshInAir in transient game world; native comparison sweep; all inputs on transient instance"));
    Root->SetArrayField(TEXT("rows"), Rows);
    FString Json;
    return FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json)) &&
        FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}
