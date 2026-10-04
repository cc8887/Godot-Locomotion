#include "AlsLyraGraphLibrary.h"
#include "AlsLyraMainObservationProbe.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSubsystem_PropertyAccess.h"
#include "Animation/AnimSequence.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/PlayerController.h"
#include "Kismet/KismetMathLibrary.h"
#include "UObject/StrongObjectPtr.h"
#include "UObject/StructOnScope.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace LyraMainObservationProbe
{
struct FInstanceAccess : UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* Instance) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(Instance); }
    static void TeardownStoppedMontages(UAnimInstance* Instance)
    {
        (Instance->*&FInstanceAccess::Montage_Advance)(0.f);
        // Advance terminates the instance; native event dispatch then deletes
        // invalid entries, which is what IsAnyMontagePlaying observes.
        (Instance->*&FInstanceAccess::DispatchQueuedAnimEvents)();
    }
};
struct FProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& Proxy, UAnimInstance* Instance, float Delta)
    { (Proxy.*&FProxyAccess::UpdateCounter).Increment(); (Proxy.*&FProxyAccess::PreUpdate)(Instance, Delta); }
};
void Double(const TSharedPtr<FJsonObject>& Row, const TCHAR* Name, double Value)
{
    uint64 Bits; FMemory::Memcpy(&Bits, &Value, sizeof(Bits));
    Row->SetNumberField(Name, Value); Row->SetStringField(FString(Name) + TEXT("Bits"), FString::Printf(TEXT("%016llx"), static_cast<unsigned long long>(Bits)));
}
TSharedPtr<FJsonObject> Vector(FVector V)
{
    auto Row = MakeShared<FJsonObject>(); Double(Row, TEXT("x"), V.X); Double(Row, TEXT("y"), V.Y); Double(Row, TEXT("z"), V.Z); return Row;
}
FVector ReadVector(const TSharedPtr<FJsonObject>& Row, const TCHAR* Name)
{
    const auto& A = Row->GetArrayField(Name); check(A.Num() == 3); return FVector(A[0]->AsNumber(), A[1]->AsNumber(), A[2]->AsNumber());
}
const TCHAR* Fields[] = {
    TEXT("WorldLocation"), TEXT("DisplacementSinceLastUpdate"), TEXT("DisplacementSpeed"),
    TEXT("WorldRotation"), TEXT("YawDeltaSinceLastUpdate"), TEXT("YawDeltaSpeed"), TEXT("AdditiveLeanAngle"),
    TEXT("WorldVelocity"), TEXT("LocalVelocity2D"), TEXT("LocalVelocityDirectionAngle"), TEXT("LocalVelocityDirectionAngleWithOffset"),
    TEXT("LocalVelocityDirection"), TEXT("LocalVelocityDirectionNoOffset"), TEXT("HasVelocity"),
    TEXT("LocalAcceleration2D"), TEXT("HasAcceleration"), TEXT("PivotDirection2D"), TEXT("CardinalDirectionFromAcceleration"),
    TEXT("IsRunningIntoWall"), TEXT("IsOnGround"), TEXT("IsCrouching"), TEXT("CrouchStateChange"),
    TEXT("ADSStateChanged"), TEXT("WasADSLastUpdate"), TEXT("TimeSinceFiredWeapon"), TEXT("IsJumping"), TEXT("IsFalling"),
    TEXT("IsFirstUpdate"), TEXT("RootYawOffset"), TEXT("CardinalDirectionDeadZone"), TEXT("GameplayTag_IsADS"), TEXT("GameplayTag_IsFiring")};
TSharedPtr<FJsonObject> Snapshot(UObject* Object)
{
    const auto Row = MakeShared<FJsonObject>();
    for (const TCHAR* Name : Fields)
    {
        FProperty* Property = Object->GetClass()->FindPropertyByName(Name); check(Property);
        if (auto* Bool = CastField<FBoolProperty>(Property)) Row->SetBoolField(Name, Bool->GetPropertyValue_InContainer(Object));
        else if (auto* Enum = CastField<FEnumProperty>(Property)) Row->SetNumberField(Name, Enum->GetUnderlyingProperty()->GetSignedIntPropertyValue(Enum->ContainerPtrToValuePtr<void>(Object)));
        else if (auto* Numeric = CastField<FNumericProperty>(Property))
        {
            const void* Address = Numeric->ContainerPtrToValuePtr<void>(Object);
            if (Numeric->IsFloatingPoint()) Double(Row, Name, Numeric->GetFloatingPointPropertyValue(Address));
            else Row->SetNumberField(Name, Numeric->GetSignedIntPropertyValue(Address));
        }
        else if (auto* Struct = CastField<FStructProperty>(Property))
        {
            if (Struct->Struct == TBaseStructure<FVector>::Get()) Row->SetObjectField(Name, Vector(*Struct->ContainerPtrToValuePtr<FVector>(Object)));
            else if (Struct->Struct == TBaseStructure<FRotator>::Get())
            {
                const auto& R = *Struct->ContainerPtrToValuePtr<FRotator>(Object); const auto V = MakeShared<FJsonObject>();
                Double(V, TEXT("pitch"), R.Pitch); Double(V, TEXT("yaw"), R.Yaw); Double(V, TEXT("roll"), R.Roll); Row->SetObjectField(Name, V);
            }
            else return {};
        }
        else return {};
    }
    return Row;
}
}

FString UAlsLyraGraphLibrary::ReadMainObservationTrace(UClass* MainClass, USkeletalMesh* Mesh, const FString& RequestsJson)
{
    using namespace LyraMainObservationProbe;
    const auto* Interface = MainClass ? IAnimClassInterface::GetFromClass(MainClass) : nullptr;
    TSharedPtr<FJsonObject> Requests;
    if (!Interface || !Mesh || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Requests)) return {};
    bool Complete = false; Requests->TryGetBoolField(TEXT("completeMain"), Complete);
    const TCHAR* Names[] = { TEXT("UpdateLocationData"), TEXT("UpdateRotationData"), TEXT("UpdateVelocityData"),
        TEXT("UpdateAccelerationData"), TEXT("UpdateWallDetectionHeuristic"), TEXT("UpdateCharacterStateData") };
    UFunction* Functions[6];
    for (int32 I = 0; I < 6; ++I) { Functions[I] = MainClass->FindFunctionByName(Names[I]); if (!Functions[I]) return {}; }
    auto* First = FindFProperty<FBoolProperty>(MainClass, TEXT("IsFirstUpdate"));
    auto* Ads = FindFProperty<FBoolProperty>(MainClass, TEXT("GameplayTag_IsADS"));
    auto* Firing = FindFProperty<FBoolProperty>(MainClass, TEXT("GameplayTag_IsFiring"));
    auto* RootYaw = FindFProperty<FNumericProperty>(MainClass, TEXT("RootYawOffset"));
    if (!First || !Ads || !Firing || !RootYaw) return {};
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const auto& TraceValue : Requests->GetArrayField(TEXT("traces")))
    {
        const auto Trace = TraceValue->AsObject();
        const auto Initialization = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false)
            .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None, nullptr,
            false, ERHIFeatureLevel::Num, &Initialization)); if (!World.IsValid()) return {};
        struct FCleanup { UWorld* World; ~FCleanup() { World->DestroyWorld(false); } } Cleanup{World.Get()};
        FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
        auto* Owner = World->SpawnActor<ACharacter>(Spawn); if (!Owner) return {};
        auto* Movement = Owner->GetCharacterMovement(); auto* Component = Owner->GetMesh();
        Component->SetSkeletalMesh(Mesh); Component->SetAnimInstanceClass(MainClass);
        auto* Main = Component->GetAnimInstance(); if (!Main) return {};
        if (Complete)
        {
            auto* Controller = World->SpawnActor<APlayerController>(Spawn); if (!Controller) return {};
            Controller->Possess(Owner);
        }
        auto& Proxy = FInstanceAccess::Proxy(Main);
        auto* Acceleration = FindFProperty<FStructProperty>(Movement->GetClass(), TEXT("Acceleration"));
        auto* Crouched = FindFProperty<FBoolProperty>(Owner->GetClass(), TEXT("bIsCrouched"));
        if (!Acceleration || Acceleration->Struct != TBaseStructure<FVector>::Get() || !Crouched) return {};
        const auto Initial = Snapshot(Main); if (!Initial) return {};
        const auto TailInitial = Complete ? TailSnapshot(Main) : nullptr;
        if (Complete && !TailInitial) return {};
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (const auto& FrameValue : Trace->GetArrayField(TEXT("frames")))
        {
            const auto Frame = FrameValue->AsObject(); const float Delta = static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            const auto Row = Tick(Main, Owner, Proxy, Interface, Frame, Delta);
            if (!Row) return {}; Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("hz"), Trace->GetNumberField(TEXT("hz")));
        Result->SetObjectField(TEXT("initial"), Initial); Result->SetArrayField(TEXT("frames"), Frames); Traces.Add(MakeShared<FJsonValueObject>(Result));
        if (Complete) Result->SetObjectField(TEXT("tailInitial"), TailInitial);
    }
    const auto Output = MakeShared<FJsonObject>(); Output->SetArrayField(TEXT("traces"), Traces);
    TArray<TSharedPtr<FJsonValue>> Order; for (const TCHAR* Name : Names) Order.Add(MakeShared<FJsonValueString>(Name));
    if (Complete)
        for (const TCHAR* Name : { TEXT("UpdateBlendWeightData"), TEXT("UpdateRootYawOffset"), TEXT("UpdateAimingData"), TEXT("UpdateJumpFallData"), TEXT("ClearFirstUpdate") })
            Order.Add(MakeShared<FJsonValueString>(Name));
    Output->SetArrayField(TEXT("order"), Order);
    FString Json; FJsonSerializer::Serialize(Output, TJsonWriterFactory<>::Create(&Json)); return Json;
}

TSharedPtr<FJsonObject> LyraMainObservationProbe::Tick(UAnimInstance* Main, ACharacter* Owner,
    FAnimInstanceProxy& Proxy, const IAnimClassInterface* Interface, const TSharedPtr<FJsonObject>& Frame, float Delta)
{
    auto* MainClass = Main->GetClass(); auto* Movement = Owner->GetCharacterMovement();
    auto* Acceleration = FindFProperty<FStructProperty>(Movement->GetClass(), TEXT("Acceleration"));
    auto* Crouched = FindFProperty<FBoolProperty>(Owner->GetClass(), TEXT("bIsCrouched"));
    auto* First = FindFProperty<FBoolProperty>(MainClass, TEXT("IsFirstUpdate"));
    auto* Ads = FindFProperty<FBoolProperty>(MainClass, TEXT("GameplayTag_IsADS"));
    auto* Firing = FindFProperty<FBoolProperty>(MainClass, TEXT("GameplayTag_IsFiring"));
    auto* RootYaw = FindFProperty<FNumericProperty>(MainClass, TEXT("RootYawOffset"));
    bool Complete = false; Frame->TryGetBoolField(TEXT("completeMain"), Complete);
    const TCHAR* Names[] = { TEXT("UpdateLocationData"), TEXT("UpdateRotationData"), TEXT("UpdateVelocityData"),
        TEXT("UpdateAccelerationData"), TEXT("UpdateWallDetectionHeuristic"), TEXT("UpdateCharacterStateData") };
    UFunction* Functions[6];
    for (int32 I = 0; I < 6; ++I) { Functions[I] = MainClass->FindFunctionByName(Names[I]); if (!Functions[I]) return {}; }
    if (!Acceleration || !Crouched || !First || !Ads || !Firing || !RootYaw) return {};
    const auto& R = Frame->GetArrayField(TEXT("rotation")); if (R.Num() != 3) return {};
    Owner->SetActorLocationAndRotation(ReadVector(Frame, TEXT("location")), FRotator(R[0]->AsNumber(), R[1]->AsNumber(), R[2]->AsNumber()), false, nullptr, ETeleportType::TeleportPhysics);
    Movement->Velocity = ReadVector(Frame, TEXT("velocity"));
    *Acceleration->ContainerPtrToValuePtr<FVector>(Movement) = ReadVector(Frame, TEXT("acceleration"));
    Movement->MovementMode = static_cast<EMovementMode>(static_cast<int32>(Frame->GetNumberField(TEXT("movementMode"))));
    Crouched->SetPropertyValue_InContainer(Owner, Frame->GetBoolField(TEXT("crouching")));
    if (!Complete) First->SetPropertyValue_InContainer(Main, Frame->GetBoolField(TEXT("first")));
    Ads->SetPropertyValue_InContainer(Main, Frame->GetBoolField(TEXT("ads")));
    Firing->SetPropertyValue_InContainer(Main, Frame->GetBoolField(TEXT("firing")));
    if (!Complete) RootYaw->SetFloatingPointPropertyValue(RootYaw->ContainerPtrToValuePtr<void>(Main), Frame->GetNumberField(TEXT("rootYaw")));
    if (Complete)
    {
        auto* Dash = FindFProperty<FBoolProperty>(MainClass, TEXT("GameplayTag_IsDashing"));
        auto* Enabled = FindFProperty<FBoolProperty>(MainClass, TEXT("bEnableRootYawOffset"));
        auto* Mode = FindFProperty<FByteProperty>(MainClass, TEXT("RootYawOffsetMode"));
        if (!Dash || !Enabled || !Mode || !Owner->GetController()) return {};
        Dash->SetPropertyValue_InContainer(Main, Frame->GetBoolField(TEXT("dashing")));
        Enabled->SetPropertyValue_InContainer(Main, Frame->GetBoolField(TEXT("enabled")));
        Mode->SetPropertyValue_InContainer(Main, static_cast<uint8>(Frame->GetNumberField(TEXT("mode"))));
        Owner->GetController()->SetControlRotation(FRotator(Frame->GetNumberField(TEXT("aimPitch")), 0, 0));
        Movement->GravityScale = static_cast<float>(Frame->GetNumberField(TEXT("gravityScale")));
        const bool Play = Frame->GetBoolField(TEXT("montage"));
        if (Play && !Main->IsAnyMontagePlaying())
        {
            auto* Sequence = LoadObject<UAnimSequence>(nullptr, TEXT("/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Unarmed/MM_Unarmed_Jog_Fwd.MM_Unarmed_Jog_Fwd"));
            if (!Sequence || !Main->PlaySlotAnimationAsDynamicMontage(Sequence, TEXT("DefaultSlot"), 0, 0, 1, 100)) return {};
        }
        else if (!Play && Main->IsAnyMontagePlaying())
        {
            Main->StopAllMontages(0);
            // IsAnyMontagePlaying tests the instance array, including stopped
            // entries. Run native zero-time teardown before gathering this
            // controlled activity input; do not advance a playing montage.
            FInstanceAccess::TeardownStoppedMontages(Main);
        }
        if (Main->IsAnyMontagePlaying() != Play) return {};
    }
    FProxyAccess::Pre(Proxy, Main, Delta);
    Interface->ForEachSubsystem(Main, [&](const FAnimSubsystemInstanceContext& Context)
    {
        if (Context.SubsystemStruct == FAnimSubsystem_PropertyAccess::StaticStruct())
        {
            FAnimSubsystemUpdateContext Game(Context, Main, Delta);
            Context.Subsystem.OnPreUpdate_GameThread(Game); Context.Subsystem.OnPostUpdate_GameThread(Game);
            FAnimSubsystemParallelUpdateContext Worker(Context, Proxy, Delta); Context.Subsystem.OnPreUpdate_WorkerThread(Worker);
        }
        return EAnimSubsystemEnumeration::Continue;
    });
    const auto Row = MakeShared<FJsonObject>(); const auto Input = MakeShared<FJsonObject>();
    Input->SetObjectField(TEXT("location"), Vector(Owner->GetActorLocation())); Input->SetObjectField(TEXT("velocity"), Vector(Owner->GetVelocity()));
    Input->SetObjectField(TEXT("acceleration"), Vector(Movement->GetCurrentAcceleration()));
    const auto Rotation = Owner->GetActorRotation(); const auto Rot = MakeShared<FJsonObject>();
    Double(Rot, TEXT("pitch"), Rotation.Pitch); Double(Rot, TEXT("yaw"), Rotation.Yaw); Double(Rot, TEXT("roll"), Rotation.Roll);
    Input->SetObjectField(TEXT("rotation"), Rot); Input->SetBoolField(TEXT("ground"), Movement->IsMovingOnGround());
    Input->SetBoolField(TEXT("crouching"), Movement->IsCrouching()); Input->SetNumberField(TEXT("movementMode"), Movement->MovementMode);
    Row->SetObjectField(TEXT("input"), Input); Row->SetObjectField(TEXT("before"), Snapshot(Main));
    if (Complete)
    {
        Double(Input, TEXT("aimPitch"), Owner->GetBaseAimRotation().Pitch);
        Double(Input, TEXT("gravity"), Movement->GetGravityZ());
        Input->SetBoolField(TEXT("montage"), Main->IsAnyMontagePlaying());
        Row->SetObjectField(TEXT("tailBefore"), TailSnapshot(Main));
        auto* Function = MainClass->FindFunctionByName(TEXT("BlueprintThreadSafeUpdateAnimation"));
        auto* DeltaProperty = Function ? FindFProperty<FFloatProperty>(Function, TEXT("DeltaTime")) : nullptr;
        if (!DeltaProperty) return {};
        FStructOnScope Parameters(Function); DeltaProperty->SetPropertyValue_InContainer(Parameters.GetStructMemory(), Delta);
        Main->ProcessEvent(Function, Parameters.GetStructMemory());
        Row->SetObjectField(TEXT("after"), Snapshot(Main)); Row->SetObjectField(TEXT("tailAfter"), TailSnapshot(Main));
        return Row;
    }
    TArray<TSharedPtr<FJsonValue>> Stages;
    for (int32 I = 0; I < 6; ++I)
    {
        FStructOnScope Parameters(Functions[I]);
        if (I == 0 || I == 5)
        {
            auto* Parameter = FindFProperty<FDoubleProperty>(Functions[I], TEXT("DeltaTime")); if (!Parameter) return {};
            Parameter->SetPropertyValue_InContainer(Parameters.GetStructMemory(), static_cast<double>(Delta));
        }
        Main->ProcessEvent(Functions[I], Parameters.GetStructMemory());
        auto State = Snapshot(Main); if (!State) return {}; Stages.Add(MakeShared<FJsonValueObject>(State));
    }
    Row->SetArrayField(TEXT("stages"), Stages); return Row;
}

TSharedPtr<FJsonObject> LyraMainObservationProbe::TailSnapshot(UAnimInstance* Main)
{
    const auto Row = MakeShared<FJsonObject>(); auto* Class = Main->GetClass();
    for (const TCHAR* Name : {TEXT("UpperbodyDynamicAdditiveWeight"), TEXT("AimYaw"), TEXT("AimPitch"), TEXT("TimeToJumpApex")})
    {
        auto* Property = FindFProperty<FDoubleProperty>(Class, Name); if (!Property) return {};
        Double(Row, Name, Property->GetPropertyValue_InContainer(Main));
    }
    auto* Mode = FindFProperty<FByteProperty>(Class, TEXT("RootYawOffsetMode"));
    auto* Spring = FindFProperty<FStructProperty>(Class, TEXT("RootYawOffsetSpringState"));
    auto* Enabled = FindFProperty<FBoolProperty>(Class, TEXT("bEnableRootYawOffset"));
    auto* Dash = FindFProperty<FBoolProperty>(Class, TEXT("GameplayTag_IsDashing"));
    auto* Standing = FindFProperty<FStructProperty>(Class, TEXT("RootYawOffsetAngleClamp"));
    auto* Crouched = FindFProperty<FStructProperty>(Class, TEXT("RootYawOffsetAngleClampCrouched"));
    if (!Mode || !Spring || Spring->Struct != FFloatSpringState::StaticStruct() || !Enabled || !Dash || !Standing || !Crouched) return {};
    Row->SetNumberField(TEXT("mode"), Mode->GetPropertyValue_InContainer(Main));
    const auto& State = *Spring->ContainerPtrToValuePtr<FFloatSpringState>(Main);
    auto Number = [&](const TCHAR* Name, float Value)
    { uint32 Bits; FMemory::Memcpy(&Bits, &Value, sizeof(Bits)); Row->SetNumberField(Name, Value); Row->SetNumberField(FString(Name)+TEXT("Bits"), Bits); };
    Number(TEXT("springVelocity"), State.Velocity); Number(TEXT("springPrevious"), State.PrevTarget);
    Row->SetBoolField(TEXT("springValid"), State.bPrevTargetValid); Row->SetBoolField(TEXT("enabled"), Enabled->GetPropertyValue_InContainer(Main));
    Row->SetBoolField(TEXT("dashing"), Dash->GetPropertyValue_InContainer(Main));
    const auto A = *Standing->ContainerPtrToValuePtr<FVector2D>(Main); const auto B = *Crouched->ContainerPtrToValuePtr<FVector2D>(Main);
    Row->SetArrayField(TEXT("standing"), {MakeShared<FJsonValueNumber>(A.X), MakeShared<FJsonValueNumber>(A.Y)});
    Row->SetArrayField(TEXT("crouched"), {MakeShared<FJsonValueNumber>(B.X), MakeShared<FJsonValueNumber>(B.Y)});
    return Row;
}
