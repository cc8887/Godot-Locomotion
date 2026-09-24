#include "AlsAnimationGraphLibrary.h"
#include "AlsCharacter.h"
#include "AlsCameraComponent.h"
#include "AlsCameraSettings.h"
#include "Animation/AnimInstance.h"
#include "Camera/CameraTypes.h"
#include "Editor.h"
#include "Engine/World.h"
#include "HAL/IConsoleManager.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"

namespace
{
TArray<TSharedPtr<FJsonValue>> Numbers(std::initializer_list<double> Values)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
    return Result;
}
void Vector(const TSharedPtr<FJsonObject>& J, const TCHAR* Name, FVector V) { J->SetArrayField(Name, Numbers({V.X,V.Y,V.Z})); }
void Rotation(const TSharedPtr<FJsonObject>& J, const TCHAR* Name, FRotator R) { J->SetArrayField(Name, Numbers({R.Pitch,R.Yaw,R.Roll})); }
template<class T> T Read(UObject* O, const TCHAR* Name)
{
    auto* P = FindFProperty<FStructProperty>(O->GetClass(), Name);
    check(P); return *P->ContainerPtrToValuePtr<T>(O);
}
TSharedPtr<FJsonObject> State(UAlsCameraComponent* C)
{
    auto J = MakeShared<FJsonObject>();
    for (const TCHAR* Name : {TEXT("PivotTargetLocation"),TEXT("PivotLagLocation"),TEXT("PivotLocation"),TEXT("CameraLocation")})
        Vector(J, Name, Read<FVector>(C, Name));
    Rotation(J, TEXT("rotation"), Read<FRotator>(C, TEXT("CameraRotation")));
    FMinimalViewInfo View; C->GetViewInfo(View); J->SetNumberField(TEXT("fov"), View.FOV);
    auto* Ratio = FindFProperty<FFloatProperty>(C->GetClass(), TEXT("TraceDistanceRatio"));
    check(Ratio); J->SetNumberField(TEXT("ratio"), Ratio->GetPropertyValue_InContainer(C));
    return J;
}
}

bool UAlsAnimationGraphLibrary::ExportCameraComponentTrace(const FString& OutputPath)
{
    if (FPaths::IsRelative(OutputPath)) return false;
    auto* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
    auto* CharacterClass = LoadClass<AAlsCharacter>(nullptr, TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
    auto* CameraClass = LoadClass<UAlsCameraComponent>(nullptr, TEXT("/ALS/ALSCamera/B_Als_CameraComponent.B_Als_CameraComponent_C"));
    if (!World || !CharacterClass || !CameraClass) return false;
    auto* Parallel = IConsoleManager::Get().FindConsoleVariable(TEXT("a.ParallelAnimEvaluation"));
    const int32 OldParallel = Parallel ? Parallel->GetInt() : 0;
    if (Parallel) Parallel->Set(0, ECVF_SetByCode);
    ON_SCOPE_EXIT { if (Parallel) Parallel->Set(OldParallel, ECVF_SetByCode); };
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const int32 Hz : {30,60,120})
    {
        FActorSpawnParameters Spawn; Spawn.ObjectFlags |= RF_Transient;
        Spawn.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
        auto* Character = World->SpawnActor<AAlsCharacter>(CharacterClass, FVector(0,0,100000), FRotator::ZeroRotator, Spawn);
        if (!Character) return false;
        ON_SCOPE_EXIT { World->DestroyActor(Character); };
        Character->SetActorTickEnabled(false); Character->SetActorEnableCollision(false);
        auto* Mesh = Character->GetMesh(); Mesh->bForceRefpose = true;
        Mesh->RefreshBoneTransforms();
        auto* Camera = NewObject<UAlsCameraComponent>(Character, CameraClass, NAME_None, RF_Transient);
        Camera->RegisterComponent(); Camera->SetUpdateAnimationInEditor(true);
        if (!Camera->GetAnimInstance()) return false;
        Camera->GetAnimInstance()->bUseMultiThreadedAnimationUpdate = false;
        auto* SettingsProperty = FindFProperty<FObjectPropertyBase>(Camera->GetClass(), TEXT("Settings"));
        auto* Settings = SettingsProperty ? Cast<UAlsCameraSettings>(SettingsProperty->GetObjectPropertyValue_InContainer(Camera)) : nullptr;
        auto* ViewProperty = FindFProperty<FStructProperty>(Character->GetClass(), TEXT("ViewState"));
        if (!Settings || !ViewProperty) return false;
        auto Trace = MakeShared<FJsonObject>(); Trace->SetNumberField(TEXT("hz"), Hz);
        Camera->Activate(true);
        Trace->SetObjectField(TEXT("initial"), State(Camera));
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < Hz * 4; ++Frame)
        {
            FMemMark Mark(FMemStack::Get());
            const TGuardValue<uint64> GlobalFrame(GFrameCounter, static_cast<uint64>(600000 + Frame));
            const double T = static_cast<double>(Frame) / Hz;
            const float Delta = 1.0f / Hz;
            Character->SetActorLocationAndRotation(FVector(T * 80 + (T >= 3 ? 500 : 0), FMath::Sin(T * 2) * 40, 100000 + FMath::Sin(T) * 20), FRotator(0,T * 35,0));
            ViewProperty->ContainerPtrToValuePtr<FAlsViewState>(Character)->Rotation = FRotator(FMath::Sin(T * 3) * 30, T * 110 - 170, 0);
            Character->SetViewMode(FGameplayTag::RequestGameplayTag(FName(T >= 1 && T < 2 ? TEXT("Als.ViewMode.FirstPerson") : TEXT("Als.ViewMode.ThirdPerson"))));
            Camera->SetRightShoulder(T < 2.5);
            Camera->SetFieldOfViewOverriden(T >= 2.5 && T < 3.5); Camera->SetFieldOfViewOverride(105);
            Mesh->UpdateComponentToWorld(); Mesh->RefreshBoneTransforms();
            auto Input = MakeShared<FJsonObject>(); Input->SetNumberField(TEXT("delta"), Delta);
            Rotation(Input,TEXT("view"),Character->GetViewRotation());
            Vector(Input,TEXT("firstPivot"),Mesh->GetSocketLocation(Settings->ThirdPerson.FirstPivotSocketName));
            Vector(Input,TEXT("secondPivot"),Mesh->GetSocketLocation(Settings->ThirdPerson.SecondPivotSocketName));
            Vector(Input,TEXT("firstPerson"),Camera->GetFirstPersonCameraLocation());
            Vector(Input,TEXT("shoulder"),Camera->GetThirdPersonTraceStartLocation());
            const auto Q = Mesh->GetComponentQuat(); Input->SetArrayField(TEXT("meshRotation"), Numbers({Q.X,Q.Y,Q.Z,Q.W}));
            Input->SetNumberField(TEXT("meshScale"), Mesh->GetComponentScale().Z);
            Input->SetBoolField(TEXT("overrideFov"),Camera->IsFieldOfViewOverriden());
            Input->SetNumberField(TEXT("fovOverride"),Camera->GetFieldOfViewOverride());
            Camera->TickComponent(Delta, LEVELTICK_All, &Camera->PrimaryComponentTick);
            if (Camera->IsRunningParallelEvaluation()) return false;
            auto Curves = MakeShared<FJsonObject>();
            for (const TCHAR* Name : {TEXT("PivotOffsetX"),TEXT("PivotOffsetY"),TEXT("PivotOffsetZ"),TEXT("CameraOffsetX"),TEXT("CameraOffsetY"),TEXT("CameraOffsetZ"),
                TEXT("LocationLagX"),TEXT("LocationLagY"),TEXT("LocationLagZ"),TEXT("RotationLag"),TEXT("TraceOverride"),TEXT("FirstPersonOverride"),TEXT("FovOffset")})
                Curves->SetNumberField(Name, Camera->GetAnimInstance()->GetCurveValue(FName(Name)));
            auto Row = MakeShared<FJsonObject>(); Row->SetObjectField(TEXT("input"),Input); Row->SetObjectField(TEXT("curves"),Curves);
            Row->SetObjectField(TEXT("output"),State(Camera)); Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Trace->SetArrayField(TEXT("frames"),Frames); Traces.Add(MakeShared<FJsonValueObject>(Trace));
        Camera->DestroyComponent();
    }
    auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"),1);
    Result->SetStringField(TEXT("scope"),TEXT("Actual UAlsCameraComponent::TickComponent; reference-pose character; controlled actor/view; clear scene, no movement base; observed native curves."));
    Result->SetArrayField(TEXT("traces"),Traces);
    FString Json;
    if (!FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)) ||
        !FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_CAMERA_COMPONENT_NATIVE_OK traces=3 frames=840 assets_saved=0"));
    return true;
}
