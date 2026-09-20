#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "Utility/AlsPrivateMemberAccessor.h"
#include "Animation/AnimInstanceProxy.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Editor.h"
#include "GameFramework/Actor.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Misc/EngineVersion.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

// Use the reference project's own private-member accessor. The calls below
// execute the compiled ALS functions; no copied foot-lock algorithm is present.
ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsProbeTeleport, &UAlsAnimationInstance::ProcessFootLockTeleport,
    void (UAlsAnimationInstance::*)(const FAlsFootUpdateContext&) const)
ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsProbeBaseChange, &UAlsAnimationInstance::ProcessFootLockBaseChange,
    void (UAlsAnimationInstance::*)(const FAlsFootUpdateContext&) const)
ALS_DEFINE_PRIVATE_MEMBER_ACCESSOR(FAlsProbeRefresh, &UAlsAnimationInstance::RefreshFootLock,
    void (UAlsAnimationInstance::*)(const FAlsFootUpdateContext&) const)

namespace
{
using FObject = TSharedPtr<FJsonObject>;
struct FRead
{
    bool Valid = true;
    FObject Object(const FObject& Parent, const TCHAR* Key)
    {
        const FObject* Value = nullptr;
        if (Parent && Parent->TryGetObjectField(Key, Value) && Value && Value->IsValid()) return *Value;
        Valid = false; return MakeShared<FJsonObject>();
    }
    double Number(const FObject& Parent, const TCHAR* Key)
    {
        double Value = 0;
        if (!Parent || !Parent->TryGetNumberField(Key, Value) || !FMath::IsFinite(Value)) Valid = false;
        return Value;
    }
    bool Bool(const FObject& Parent, const TCHAR* Key)
    {
        bool Value = false;
        if (!Parent || !Parent->TryGetBoolField(Key, Value)) Valid = false;
        return Value;
    }
    FVector Vector(const FObject& Parent) { return {Number(Parent, TEXT("X")), Number(Parent, TEXT("Y")), Number(Parent, TEXT("Z"))}; }
    FQuat Rotation(const FObject& Parent)
    {
        FQuat Result{Number(Parent, TEXT("X")), Number(Parent, TEXT("Y")), Number(Parent, TEXT("Z")), Number(Parent, TEXT("W"))};
        if (FMath::Abs(Result.SizeSquared() - 1) > .001) Valid = false;
        return Result;
    }
    FTransform Pose(const FObject& Parent)
    {
        const auto Position = Vector(Object(Parent, TEXT("Position")));
        const auto Quaternion = Rotation(Object(Parent, TEXT("Rotation")));
        const auto Scale = Vector(Object(Parent, TEXT("Scale")));
        if (Scale.GetMin() <= 0) Valid = false;
        return {Quaternion, Position, Scale};
    }
};
FObject WriteVector(const FVector& Value)
{
    auto Object = MakeShared<FJsonObject>();
    Object->SetNumberField(TEXT("X"), Value.X); Object->SetNumberField(TEXT("Y"), Value.Y); Object->SetNumberField(TEXT("Z"), Value.Z);
    return Object;
}
FObject WriteRotation(const FQuat& Value)
{
    auto Object = WriteVector(FVector(Value.X, Value.Y, Value.Z)); Object->SetNumberField(TEXT("W"), Value.W); return Object;
}
FObject WritePose(const FVector& Position, const FQuat& Rotation)
{
    auto Object = MakeShared<FJsonObject>(); Object->SetObjectField(TEXT("Position"), WriteVector(Position));
    Object->SetObjectField(TEXT("Rotation"), WriteRotation(Rotation)); Object->SetObjectField(TEXT("Scale"), WriteVector(FVector::OneVector));
    return Object;
}
FObject WriteState(const FAlsFootState& Foot)
{
    auto Object = MakeShared<FJsonObject>(); Object->SetNumberField(TEXT("Amount"), Foot.LockAmount);
    Object->SetObjectField(TEXT("WorldLock"), WritePose(Foot.LockLocationWorldSpace, Foot.LockRotationWorldSpace));
    Object->SetObjectField(TEXT("BaseLock"), WritePose(FVector(Foot.LockLocationMovementBaseSpace), FQuat(Foot.LockRotationMovementBaseSpace)));
    Object->SetObjectField(TEXT("ComponentLock"), WritePose(FVector(Foot.LockLocation), FQuat(Foot.LockRotation)));
    Object->SetObjectField(TEXT("FinalComponent"), WritePose(FVector(Foot.FinalLocation), FQuat(Foot.FinalRotation)));
    return Object;
}
struct FFootProbeAccess : UAlsAnimationInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* Instance) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(Instance); }
    static FAlsFeetState& Feet(UAlsAnimationInstance& Instance) { return Instance.*&FFootProbeAccess::FeetState; }
    static void Configure(UAlsAnimationInstance& Instance, UAlsAnimationInstanceSettings* ProbeSettings,
        bool Grounded, bool Moving, double Elapsed, bool Changed, bool Relative, const FTransform& Base)
    {
        Instance.*&FFootProbeAccess::Settings = ProbeSettings;
        Instance.*&FFootProbeAccess::LocomotionMode = Grounded ? AlsLocomotionModeTags::Grounded : AlsLocomotionModeTags::InAir;
        (Instance.*&FFootProbeAccess::LocomotionState).bMovingSmooth = Moving;
        Instance.*&FFootProbeAccess::TeleportedTime = Instance.GetWorld()->GetTimeSeconds() - Elapsed;
        auto& Movement = Instance.*&FFootProbeAccess::MovementBase;
        Movement = {}; Movement.bBaseChanged = Changed; Movement.bHasRelativeLocation = Relative;
        Movement.Location = Base.GetLocation(); Movement.Rotation = Base.GetRotation();
    }
};
struct FFootProxyAccess : FAnimInstanceProxy
{
    static void Pre(FAnimInstanceProxy& Proxy, UAnimInstance* Instance, float Delta) { (Proxy.*&FFootProxyAccess::PreUpdate)(Instance, Delta); }
};
bool RunCase(UAlsAnimationInstance& Instance, UAlsAnimationInstanceSettings& Settings, USkeletalMeshComponent& Component,
    const FObject& Trace, float ThighLimit, float FootLimit, FObject& Output)
{
    FRead Read;
    auto Previous = Read.Object(Trace, TEXT("Previous")); auto Input = Read.Object(Trace, TEXT("Input"));
    const bool HasSample = Read.Bool(Previous, TEXT("HasSample"));
    const bool Valid = Read.Bool(Input, TEXT("Valid"));
    const bool BecameValid = Read.Bool(Input, TEXT("BecameValid")) || !HasSample;
    const auto Transform = Read.Pose(Read.Object(Input, TEXT("ComponentTransform")));
    const auto Base = Read.Pose(Read.Object(Input, TEXT("BaseTransform")));
    const auto Target = Read.Pose(Read.Object(Input, TEXT("TargetWorld")));
    const double Identity = Read.Number(Input, TEXT("BaseIdentity"));
    const double OldIdentity = Read.Number(Previous, TEXT("BaseIdentity"));
    const double Elapsed = Read.Number(Input, TEXT("SecondsSinceTeleport"));
    const bool Grounded = Read.Bool(Input, TEXT("Grounded")), Moving = Read.Bool(Input, TEXT("MovingSmooth"));
    const float Delta = static_cast<float>(Read.Number(Input, TEXT("DeltaTime")));
    const float Ik = FMath::Clamp(static_cast<float>(Read.Number(Input, TEXT("IkAmount"))), 0.f, 1.f);
    const float Lock = FMath::Clamp(static_cast<float>(Read.Number(Input, TEXT("LockAmount"))), 0.f, 1.f);
    auto& Feet = FFootProbeAccess::Feet(Instance); Feet = {}; Feet.bValid = Valid; Feet.bBecameValid = BecameValid;
    Feet.PelvisRotation = FQuat4f(Read.Rotation(Read.Object(Input, TEXT("PelvisRotation"))));
    auto& Foot = Feet.Left;
    if (HasSample)
    {
        Foot.LockAmount = static_cast<float>(Read.Number(Previous, TEXT("Amount")));
        const auto World = Read.Pose(Read.Object(Previous, TEXT("WorldLock")));
        Foot.LockLocationWorldSpace = World.GetLocation(); Foot.LockRotationWorldSpace = World.GetRotation();
        const auto LocalBase = Read.Pose(Read.Object(Previous, TEXT("BaseLock")));
        Foot.LockLocationMovementBaseSpace = FVector3f(LocalBase.GetLocation()); Foot.LockRotationMovementBaseSpace = FQuat4f(LocalBase.GetRotation());
        const auto Local = Read.Pose(Read.Object(Previous, TEXT("ComponentLock")));
        Foot.LockLocation = FVector3f(Local.GetLocation()); Foot.LockRotation = FQuat4f(Local.GetRotation());
        const auto Final = Read.Pose(Read.Object(Previous, TEXT("FinalComponent")));
        Foot.FinalLocation = FVector3f(Final.GetLocation()); Foot.FinalRotation = FQuat4f(Final.GetRotation());
    }
    Foot.ThighAxisPelvisSpace = FVector3f(Read.Vector(Read.Object(Input, TEXT("ThighAxisPelvisSpace"))));
    Foot.TargetLocationWorldSpace = Target.GetLocation(); Foot.TargetRotationWorldSpace = Target.GetRotation();
    if (!Read.Valid || !Valid || Delta <= 0 || Delta > 1 || Elapsed < 0 ||
        Identity < 0 || OldIdentity < 0 || Identity > 9007199254740991.0 || OldIdentity > 9007199254740991.0) return false;
    Settings.FootLock.bAllowFootLock = true; Settings.FootLock.ThighAngleLimit = ThighLimit; Settings.FootLock.FootAngleLimit = FootLimit;
    Component.SetWorldTransform(Transform);
    auto& Proxy = FFootProbeAccess::Proxy(&Instance); FFootProxyAccess::Pre(Proxy, &Instance, Delta);
    if (!Proxy.GetComponentTransform().Equals(Transform, .000001)) return false;
    FFootProbeAccess::Configure(Instance, &Settings, Grounded, Moving, Elapsed, Identity != OldIdentity, Identity != 0, Base);
    const FAlsFootUpdateContext Context{Transform, Transform.Inverse(), &Foot, Ik, Lock, Delta};
    FAlsProbeTeleport::Access(Instance, Context);
    FAlsProbeBaseChange::Access(Instance, Context);
    FAlsProbeRefresh::Access(Instance, Context);
    Output = WriteState(Foot); return true;
}
}

bool UAlsAnimationGraphLibrary::ExportBasedFootLockTrace(const FString& RequestPath, const FString& OutputPath)
{
    if (!IsInGameThread() || FPaths::IsRelative(RequestPath) || FPaths::IsRelative(OutputPath) || RequestPath == OutputPath) return false;
    FString Text; TArray<TSharedPtr<FJsonValue>> Frames;
    if (!FFileHelper::LoadFileToString(Text, *RequestPath) || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Frames) || Frames.IsEmpty()) return false;
    UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    if (!World || World->WorldType != EWorldType::Editor) return false;
    auto* Mesh = LoadObject<USkeletalMesh>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"));
    if (!Mesh) return false;
    FActorSpawnParameters Spawn; Spawn.ObjectFlags = RF_Transient;
    AActor* Owner = World->SpawnActor<AActor>(Spawn); if (!Owner) return false;
    ON_SCOPE_EXIT { World->DestroyActor(Owner); };
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Owner, NAME_None, RF_Transient));
    Component->SetSkeletalMesh(Mesh);
    TStrongObjectPtr<UAlsAnimationInstance> Instance(NewObject<UAlsAnimationInstance>(Component.Get(), NAME_None, RF_Transient));
    TStrongObjectPtr<UAlsAnimationInstanceSettings> Settings(NewObject<UAlsAnimationInstanceSettings>(GetTransientPackage(), NAME_None, RF_Transient));
    Instance->InitializeAnimation(true);
    // Capture native initialization before RunCase replaces per-frame state.
    // ALS initializes these axes from the mesh's reference skeleton, not from
    // the animation Skeleton asset used by the Godot raw-source compiler.
    const auto& InitialFeet = FFootProbeAccess::Feet(*Instance);
    const FVector LeftAxis(InitialFeet.Left.ThighAxisPelvisSpace);
    const FVector RightAxis(InitialFeet.Right.ThighAxisPelvisSpace);
    if (!LeftAxis.IsNormalized() || !RightAxis.IsNormalized()) return false;
    auto Rig = MakeShared<FJsonObject>();
    Rig->SetStringField(TEXT("mesh"), Mesh->GetPathName());
    Rig->SetObjectField(TEXT("Left"), WriteVector(LeftAxis));
    Rig->SetObjectField(TEXT("Right"), WriteVector(RightAxis));
    TArray<TSharedPtr<FJsonValue>> Results; int32 Variants = 0;
    for (const auto& Value : Frames)
    {
        FRead Read; auto Frame = Value->AsObject();
        const int32 FrameId = static_cast<int32>(Read.Number(Frame, TEXT("Frame")));
        auto Trace = Read.Object(Frame, TEXT("BasedFootLockTrace"));
        for (const TCHAR* Side : {TEXT("Left"), TEXT("Right")})
        {
            auto Case = Read.Object(Trace, Side); FObject State;
            if (!Read.Valid || !RunCase(*Instance, *Settings, *Component, Case, 90, 40, State)) return false;
            auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("Frame"), FrameId);
            Result->SetStringField(TEXT("Side"), Side); Result->SetObjectField(TEXT("Native"), State);
            if (FrameId >= 610 && FrameId <= 620)
            {
                FObject Unconstrained, ThighOnly, FootOnly;
                if (!RunCase(*Instance, *Settings, *Component, Case, 180, 180, Unconstrained) ||
                    !RunCase(*Instance, *Settings, *Component, Case, 90, 180, ThighOnly) ||
                    !RunCase(*Instance, *Settings, *Component, Case, 180, 40, FootOnly)) return false;
                Result->SetObjectField(TEXT("Unconstrained"), Unconstrained);
                Result->SetObjectField(TEXT("ThighOnly"), ThighOnly); Result->SetObjectField(TEXT("FootOnly"), FootOnly); Variants += 3;
            }
            Results.Add(MakeShared<FJsonValueObject>(Result));
        }
    }
    auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"), 1);
    Result->SetStringField(TEXT("engine"), FEngineVersion::Current().ToString());
    Result->SetStringField(TEXT("scope"), TEXT("Actual compiled ALS-Refactored foot-lock functions; independent per-frame captured Godot inputs; transient Editor objects; no animation graph, physics, or asset saves."));
    Result->SetArrayField(TEXT("results"), Results); Result->SetNumberField(TEXT("variants"), Variants);
    Result->SetObjectField(TEXT("initializedRig"), Rig);
    FString Json;
    if (!FJsonSerializer::Serialize(Result, TJsonWriterFactory<TCHAR, TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)) ||
        !FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp, Display, TEXT("ALS_BASED_FOOT_NATIVE_OK cases=%d variants=%d assets_saved=0"), Results.Num(), Variants);
    return true;
}
