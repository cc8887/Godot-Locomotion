#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsCharacter.h"
#include "Animation/AnimInstanceProxy.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/World.h"
#include "Misc/FileHelper.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FMovementProbeProxy : FAnimInstanceProxy
{
    static void Prepare(FAnimInstanceProxy& P, UAnimInstance* I, float Delta) { (P.*&FMovementProbeProxy::PreUpdate)(I, Delta); }
};
FVector MovementProbeVector(const TSharedPtr<FJsonObject>& Row, const TCHAR* Field)
{
    const auto& V = Row->GetArrayField(Field); return FVector(V[0]->AsNumber(), V[1]->AsNumber(), V[2]->AsNumber());
}
struct FMovementParentProbe : UAlsAnimationInstance
{
    static void Input(UAlsAnimationInstance* I, AAlsCharacter* C, const TSharedPtr<FJsonObject>& R)
    {
        I->*&FMovementParentProbe::Character = C;
        auto& L = I->*&FMovementParentProbe::LocomotionState;
        L.VelocityWorldSpace = MovementProbeVector(R, TEXT("velocity"));
        L.AccelerationWorldSpace = MovementProbeVector(R, TEXT("acceleration"));
        const auto& Q = R->GetArrayField(TEXT("rotation"));
        L.RotationQuaternionWorldSpace = FQuat(Q[0]->AsNumber(), Q[1]->AsNumber(), Q[2]->AsNumber(), Q[3]->AsNumber());
        L.Speed = R->GetNumberField(TEXT("speed")); L.ScaleWorldSpace = R->GetNumberField(TEXT("scale"));
        L.VelocityYawAngleWorldSpace = R->GetNumberField(TEXT("velocityYaw"));
        L.MaxAcceleration = R->GetNumberField(TEXT("maxAcceleration")); L.MaxBrakingDeceleration = R->GetNumberField(TEXT("maxBraking"));
        (I->*&FMovementParentProbe::ViewState).RotationWorldSpace.Yaw = R->GetNumberField(TEXT("viewYaw"));
        (I->*&FMovementParentProbe::PoseState).UnweightedGaitRunningAmount = R->GetNumberField(TEXT("running"));
        (I->*&FMovementParentProbe::PoseState).UnweightedGaitSprintingAmount = R->GetNumberField(TEXT("sprinting"));
        const auto G = R->GetStringField(TEXT("gait"));
        I->*&FMovementParentProbe::Gait = G.IsEmpty() ? FGameplayTag() : FGameplayTag::RequestGameplayTag(FName(*G));
        I->*&FMovementParentProbe::RotationMode = R->GetBoolField(TEXT("velocityMode")) ? AlsRotationModeTags::VelocityDirection : AlsRotationModeTags::ViewDirection;
        FindFProperty<FBoolProperty>(I->GetClass(), TEXT("bPendingUpdate"))->SetPropertyValue_InContainer(I, R->GetBoolField(TEXT("pending")));
        FMovementProbeProxy::Prepare(*GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I), I, R->GetNumberField(TEXT("delta")));
        I->OverrideCurveValue(TEXT("HipsDirectionLock"), R->GetNumberField(TEXT("hipsLock")));
        I->OverrideCurveValue(TEXT("SprintBlock"), R->GetNumberField(TEXT("sprintBlock")));
    }
    static TArray<TSharedPtr<FJsonValue>> State(UAlsAnimationInstance* I)
    {
        const auto& G = I->*&FMovementParentProbe::GroundedState;
        const auto& V = G.VelocityBlend; const auto& Y = G.RotationYawOffsets;
        const auto& L = I->*&FMovementParentProbe::LeanState;
        const auto& S = I->*&FMovementParentProbe::StandingState;
        const auto& C = I->*&FMovementParentProbe::CrouchingState;
        const double D = G.MovementDirection.bForward ? 0 : G.MovementDirection.bBackward ? 1 : G.MovementDirection.bLeft ? 2 : 3;
        TArray<TSharedPtr<FJsonValue>> Result;
        for (double N : {double(!V.bInitializationRequired), double(V.ForwardAmount), double(V.BackwardAmount), double(V.LeftAmount), double(V.RightAmount),
            double(L.RightAmount), double(L.ForwardAmount), D, double(G.HipsDirectionLockAmount), double(Y.ForwardAngle), double(Y.BackwardAngle),
            double(Y.LeftAngle), double(Y.RightAngle), double(S.StrideBlendAmount), double(S.WalkRunBlendAmount), double(S.PlayRate),
            double(S.SprintBlockAmount), double(S.SprintTime), double(S.SprintAccelerationAmount), double(C.StrideBlendAmount), double(C.PlayRate),
            double(S.bPivotActive), double(G.HipsDirection)}) Result.Add(MakeShared<FJsonValueNumber>(N));
        return Result;
    }
};
}
bool UAlsAnimationGraphLibrary::ExportMovementParentTrace(const FString& RequestPath, const FString& OutputPath)
{
    FString Text; TSharedPtr<FJsonObject> Request;
    if (!FFileHelper::LoadFileToString(Text, *RequestPath) || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Request)) return false;
    auto* Class = LoadClass<UAlsAnimationInstance>(nullptr, TEXT("/ALS/ALS/Character/AB_Als.AB_Als_C"));
    if (!Class) return false;
    const auto Init = UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false)
        .CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview, false, NAME_None, nullptr, false, ERHIFeatureLevel::Num, &Init));
    if (!World.IsValid() || !World->IsGameWorld()) return false;
    ON_SCOPE_EXIT { World->DestroyWorld(false); };
    auto* Character = World->SpawnActor<AAlsCharacter>(); if (!Character) return false;
    Character->SetActorTickEnabled(false);
    TArray<TSharedPtr<FJsonValue>> Traces; int32 Count = 0;
    for (const auto& TraceValue : Request->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<UAlsAnimationInstance> Instance(NewObject<UAlsAnimationInstance>(Character->GetMesh(), Class));
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (const auto& FrameValue : TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            const auto Frame = FrameValue->AsObject();
            FMemMark Mark(FMemStack::Get());
            FMovementParentProbe::Input(Instance.Get(), Character, Frame->GetObjectField(TEXT("input")));
            for (const auto& Operation : Frame->GetArrayField(TEXT("operations")))
            {
                const auto Name = Operation->AsString();
                const TSet<FString> Allowed = {TEXT("InitializeGrounded"), TEXT("InitializeLean"), TEXT("RefreshGrounded"),
                    TEXT("RefreshGroundedMovement"), TEXT("InitializeStandingMovement"), TEXT("RefreshStandingMovement"),
                    TEXT("RefreshCrouchingMovement"), TEXT("ActivatePivot"), TEXT("ResetPivot")};
                if (!Allowed.Contains(Name)) return false;
                auto* Function = Instance->FindFunction(FName(*Name)); if (!Function) return false;
                Instance->ProcessEvent(Function, nullptr);
            }
            auto Row = MakeShared<FJsonObject>(); Row->SetObjectField(TEXT("request"), Frame);
            Row->SetArrayField(TEXT("state"), FMovementParentProbe::State(Instance.Get()));
            Frames.Add(MakeShared<FJsonValueObject>(Row)); ++Count;
        }
        auto Trace = MakeShared<FJsonObject>(); Trace->SetStringField(TEXT("name"), TraceValue->AsObject()->GetStringField(TEXT("name")));
        Trace->SetArrayField(TEXT("frames"), Frames); Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"), 1);
    Result->SetObjectField(TEXT("resourceHashes"), Request->GetObjectField(TEXT("resourceHashes"))); Result->SetArrayField(TEXT("traces"), Traces);
    FString Json;
    if (!FJsonSerializer::Serialize(Result, TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)) ||
        !FFileHelper::SaveStringToFile(Json, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp, Display, TEXT("ALS_MOVEMENT_PARENT_OK frames=%d assets_saved=0"), Count); return true;
}
