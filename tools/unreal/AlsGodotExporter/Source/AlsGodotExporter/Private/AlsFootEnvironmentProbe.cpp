#include "AlsAnimationGraphLibrary.h"

#include "Components/BoxComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Nodes/AlsRigUnit_FootOffsetTrace.h"
#include "Serialization/JsonSerializer.h"
#include "Units/Simulation/RigUnit_SpringInterp.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsFootEnvironmentProbe
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& Value)
{
    return {MakeShared<FJsonValueNumber>(Value.X), MakeShared<FJsonValueNumber>(Value.Y), MakeShared<FJsonValueNumber>(Value.Z)};
}
TSharedPtr<FJsonValue> P(const FTransform& Value)
{
    auto Result = MakeShared<FJsonObject>();
    Result->SetArrayField(TEXT("p"), V(Value.GetLocation()));
    Result->SetArrayField(TEXT("s"), V(Value.GetScale3D()));
    const auto Q = Value.GetRotation();
    Result->SetArrayField(TEXT("q"), {MakeShared<FJsonValueNumber>(Q.X), MakeShared<FJsonValueNumber>(Q.Y),
        MakeShared<FJsonValueNumber>(Q.Z), MakeShared<FJsonValueNumber>(Q.W)});
    return MakeShared<FJsonValueObject>(Result);
}
}

bool UAlsAnimationGraphLibrary::ExportFootEnvironment(const FString& OutputPath)
{
    using namespace AlsFootEnvironmentProbe;
    if (FPaths::IsRelative(OutputPath) || IFileManager::Get().FileExists(*OutputPath)) return false;
    TArray<TSharedPtr<FJsonValue>> SpringCases;
    for (const int32 Hz : {30, 60, 120})
    for (const int32 Mode : {0, 1, 2, 3})
    {
        FRigUnit_SpringInterpV2 Unit;
        FRigVMExecuteContext Context;
        auto Case = MakeShared<FJsonObject>();
        Case->SetNumberField(TEXT("hz"), Hz);
        Case->SetNumberField(TEXT("mode"), Mode);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < 120; ++Frame)
        {
            const bool Reset = Frame == 70;
            if (Reset) Unit = FRigUnit_SpringInterpV2();
            const double Delta = Frame == 0 || Frame == 1 || Frame == 42 || Frame == 70 ? 0 : 1.0 / Hz;
            Context.SetDeltaTime(Delta);
            const float Left = Frame < 20 ? -10.f : Frame < 60 ? -50.f : 60.f;
            const float Right = Frame < 40 ? -5.f : Frame < 80 ? 20.f : 80.f;
            const float Amount = Frame < 30 ? 1.f : Frame < 55 ? .5f : Frame < 65 ? 0.f : 1.f;
            const double Clamped = FMath::Clamp(static_cast<double>(FMath::Min(Left, Right)), -30.0, 40.0);
            Unit.Target = static_cast<float>(Clamped * Amount);
            Unit.bUseCurrentInput = Mode >= 2;
            Unit.bInitializeFromTarget = Mode != 3;
            Unit.Current = Frame * .25f - 10;
            Unit.Strength = Mode == 1 && Frame >= 40 && Frame < 50 ? 0.f : 2.f;
            Unit.Force = Mode == 1 ? 7.f : 0.f;
            Unit.CriticalDamping = Mode == 2 ? .5f : Mode == 3 ? 2.f : 1.f;
            Unit.TargetVelocityAmount = Mode == 2 ? .75f : 0.f;
            Unit.Execute(Context);
            auto Row = MakeShared<FJsonObject>();
            Row->SetBoolField(TEXT("reset"), Reset);
            Row->SetNumberField(TEXT("dt"), Delta);
            Row->SetNumberField(TEXT("left"), Left);
            Row->SetNumberField(TEXT("right"), Right);
            Row->SetNumberField(TEXT("amount"), Amount);
            Row->SetNumberField(TEXT("target"), Unit.Target);
            Row->SetNumberField(TEXT("current"), Unit.Current);
            Row->SetNumberField(TEXT("strength"), Unit.Strength);
            Row->SetNumberField(TEXT("force"), Unit.Force);
            Row->SetNumberField(TEXT("damping"), Unit.CriticalDamping);
            Row->SetNumberField(TEXT("targetVelocity"), Unit.TargetVelocityAmount);
            Row->SetBoolField(TEXT("useCurrent"), Unit.bUseCurrentInput);
            Row->SetBoolField(TEXT("initializeFromTarget"), Unit.bInitializeFromTarget);
            Row->SetNumberField(TEXT("result"), Unit.Result);
            Row->SetNumberField(TEXT("velocity"), Unit.Velocity);
            Row->SetNumberField(TEXT("previousTarget"), Unit.SpringState.PrevTarget);
            Row->SetBoolField(TEXT("previousValid"), Unit.SpringState.bPrevTargetValid);
            Row->SetNumberField(TEXT("pelvisOffset"), static_cast<float>(static_cast<double>(Unit.Result) * Amount));
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Case->SetArrayField(TEXT("frames"), Frames);
        SpringCases.Add(MakeShared<FJsonValueObject>(Case));
    }
    const auto WorldInitialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false)
        .EnableTraceCollision(true).SetTransactional(false);
    // CreateWorld always creates the persistent level/WorldSettings, even with
    // bInSkipInitWorld=true. Supply initialization options to this single call.
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None,
        nullptr, false, ERHIFeatureLevel::Num, &WorldInitialization));
    if (!World.IsValid()) return false;
    auto* FloorActor = World->SpawnActor<AActor>();
    auto* Owner = World->SpawnActor<AActor>();
    if (!FloorActor || !Owner) { World->DestroyWorld(false); return false; }
    auto* Box = NewObject<UBoxComponent>(FloorActor);
    FloorActor->SetRootComponent(Box);
    FloorActor->AddInstanceComponent(Box);
    Box->SetBoxExtent(FVector(500, 500, 5));
    Box->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
    Box->SetCollisionResponseToAllChannels(ECR_Block);
    Box->RegisterComponent();
    TArray<TSharedPtr<FJsonValue>> TraceRows;
    for (const double Slope : {0.0, 25.0, 45.0, 65.0})
    for (const int32 Basis : {0, 1, 2})
    for (const int32 Situation : {0, 1, 2, 3})
    {
        Box->SetWorldLocationAndRotation(FVector(0, 0, Situation == 2 ? -1000 : -5), FRotator(Slope, 0, 0));
        FControlRigExecuteContext Context;
        Context.SetWorld(World.Get());
        Context.SetOwningActor(Owner);
        const FTransform ToWorld = Basis == 0 ? FTransform::Identity :
            FTransform(FRotator(Basis == 2 ? 10 : 0, 25, 0).Quaternion(), FVector(0, 0, 0), Basis == 2 ? FVector(1.2, .8, .9) : FVector::OneVector);
        Context.SetToWorldSpaceTransform(ToWorld);
        FAlsRigUnit_FootOffsetTrace Unit;
        Unit.FootTargetLocation = FVector(15, 10, 123); // Z is intentionally ignored by the node.
        Unit.bEnabled = Situation != 3;
        Unit.WalkableFloorAngle = Situation == 1 ? 30.f : 45.f;
        const FVector Start = Context.ToWorldSpace(FVector(15, 10, Unit.TraceDistanceUpward));
        const FVector End = Context.ToWorldSpace(FVector(15, 10, -Unit.TraceDistanceDownward));
        FHitResult Hit;
        World->LineTraceSingleByChannel(Hit, Start, End, Unit.TraceChannel, {TEXT("FootEnvironmentOracle"), true, Owner});
        Unit.Execute(Context);
        auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("slope"), Slope);
        Row->SetNumberField(TEXT("basis"), Basis);
        Row->SetNumberField(TEXT("situation"), Situation);
        Row->SetBoolField(TEXT("enabled"), Unit.bEnabled);
        Row->SetField(TEXT("toWorld"), P(ToWorld));
        Row->SetArrayField(TEXT("target"), V(Unit.FootTargetLocation));
        Row->SetArrayField(TEXT("start"), V(Start));
        Row->SetArrayField(TEXT("end"), V(End));
        Row->SetNumberField(TEXT("walkableAngle"), Unit.WalkableFloorAngle);
        Row->SetBoolField(TEXT("blocking"), Hit.bBlockingHit);
        Row->SetArrayField(TEXT("impact"), V(Hit.ImpactPoint));
        Row->SetArrayField(TEXT("normal"), V(Hit.ImpactNormal));
        Row->SetNumberField(TEXT("offsetZ"), Unit.OffsetLocationZ);
        Row->SetArrayField(TEXT("offsetNormal"), V(Unit.OffsetNormal));
        TraceRows.Add(MakeShared<FJsonValueObject>(Row));
    }
    World->DestroyWorld(false);
    auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetArrayField(TEXT("springs"), SpringCases);
    Root->SetArrayField(TEXT("traces"), TraceRows);
    FString Json;
    return FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json)) &&
        FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM);
}
