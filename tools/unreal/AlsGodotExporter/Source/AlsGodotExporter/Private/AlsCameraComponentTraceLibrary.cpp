#include "AlsAnimationGraphLibrary.h"
#include "AlsCharacter.h"
#include "AlsCameraComponent.h"
#include "AlsCameraSettings.h"
#include "Animation/AnimInstance.h"
#include "Camera/CameraTypes.h"
#include "Components/BoxComponent.h"
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
    const bool bMovingBase = FPlatformMisc::GetEnvironmentVariable(TEXT("ALS_CAMERA_COMPONENT_BASE")) == TEXT("1");
    const bool bCollision = FPlatformMisc::GetEnvironmentVariable(TEXT("ALS_CAMERA_COMPONENT_COLLISION")) == TEXT("1");
    if (bMovingBase && bCollision) return false;
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
        AActor* BaseOwner = bMovingBase || bCollision ? World->SpawnActor<AActor>(Spawn) : nullptr;
        ON_SCOPE_EXIT { if (BaseOwner) World->DestroyActor(BaseOwner); };
        UBoxComponent* Bases[2] = {nullptr,nullptr};
        UBoxComponent* Wall = nullptr;
        if (bCollision)
        {
            if (!BaseOwner) return false;
            Wall = NewObject<UBoxComponent>(BaseOwner, NAME_None, RF_Transient);
            Wall->SetMobility(EComponentMobility::Movable);
            Wall->SetCollisionEnabled(ECollisionEnabled::QueryOnly);
            Wall->SetCollisionResponseToAllChannels(ECR_Block); Wall->RegisterComponent();
        }
        if (bMovingBase)
        {
            if (!BaseOwner) return false;
            for (auto& Base : Bases)
            {
                Base = NewObject<UBoxComponent>(BaseOwner, NAME_None, RF_Transient);
                Base->SetMobility(EComponentMobility::Movable);
                Base->SetCollisionEnabled(ECollisionEnabled::NoCollision); Base->RegisterComponent();
            }
        }
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
            int32 BaseId = 0;
            if (bMovingBase)
            {
                BaseId = T < 1.5 ? 1 : T < 3.3 ? 2 : 0;
                for (int32 Index = 0; Index < 2; ++Index)
                    Bases[Index]->SetWorldLocationAndRotation(FVector(T*50 + Index*90,FMath::Sin(T)*80-Index*40,100000),
                        FRotator(15*FMath::Sin(T*2),T*55+Index*30,10*FMath::Cos(T)));
                FMovementBaseInterfaceData NewBase;
                if (BaseId) NewBase.Set(Bases[BaseId-1]);
                Character->SetBase(&NewBase);
                if (BaseId)
                {
                    const auto Transform = Bases[BaseId-1]->GetComponentTransform();
                    Character->SetActorLocationAndRotation(Transform.TransformPosition(FVector(40+(T>=3?500:0),0,100)),
                        Transform.GetRotation()*FRotator(0,T*35,0).Quaternion());
                    Character->SaveRelativeBasedMovement(Transform.InverseTransformPosition(Character->GetActorLocation()),
                        FRotator(0,T*35,0),true);
                }
            }
            ViewProperty->ContainerPtrToValuePtr<FAlsViewState>(Character)->Rotation = FRotator(FMath::Sin(T * 3) * 30, T * 110 - 170, 0);
            Character->SetViewMode(FGameplayTag::RequestGameplayTag(FName(T >= 1 && T < 2 ? TEXT("Als.ViewMode.FirstPerson") : TEXT("Als.ViewMode.ThirdPerson"))));
            Camera->SetRightShoulder(T < 2.5);
            Camera->SetFieldOfViewOverriden(T >= 2.5 && T < 3.5); Camera->SetFieldOfViewOverride(105);
            if (bCollision)
            {
                Character->SetActorLocationAndRotation(FVector(0,0,100000),FRotator::ZeroRotator);
                ViewProperty->ContainerPtrToValuePtr<FAlsViewState>(Character)->Rotation = FRotator::ZeroRotator;
                Character->SetViewMode(FGameplayTag::RequestGameplayTag(FName(TEXT("Als.ViewMode.ThirdPerson"))));
                Camera->SetRightShoulder(true); Camera->SetFieldOfViewOverriden(false);
            }
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
            if (bCollision)
            {
                const auto Shoulder = Camera->GetThirdPersonTraceStartLocation();
                FVector WallLocation; FVector Extent;
                if (T >= 2 && T < 3)
                {
                    // Inflated-sphere recovery towards the actor, then towards
                    // the opposite side (native rejection). No copied MTD math.
                    const double Toward = Character->GetActorLocation().Y >= Shoulder.Y ? 1 : -1;
                    const double Normal = T < 2.5 ? Toward : -Toward;
                    WallLocation = Shoulder - FVector(0,Normal*20,0); Extent = FVector(1000,10,1000);
                }
                else
                {
                    const double Distance = T < 1 || T >= 3 ? 600 : 150+60*FMath::Sin(T*6);
                    WallLocation = Shoulder-FVector(Distance,0,0); Extent = FVector(10,1000,1000);
                }
                Wall->SetBoxExtent(Extent); Wall->SetWorldLocation(WallLocation);
                Vector(Input,TEXT("wallLocation"),Wall->GetComponentLocation());
                Vector(Input,TEXT("wallExtent"),Wall->GetUnscaledBoxExtent());
                Vector(Input,TEXT("ownerLocation"),Character->GetActorLocation());
            }
            if (bMovingBase)
            {
                const auto& Based = Character->GetBasedMovement();
                auto Location = FVector::ZeroVector; auto RotationValue = FQuat::Identity;
                if (Based.HasRelativeRotation())
                    MovementBaseUtility::GetMovementBaseTransform(&Based.MovementBaseInterfaceData,Based.BoneName,Location,RotationValue);
                if ((BaseId != 0) != Based.HasRelativeRotation()) return false;
                Input->SetNumberField(TEXT("baseId"),BaseId);
                Input->SetBoolField(TEXT("relativeBaseRotation"),Based.HasRelativeRotation());
                Vector(Input,TEXT("baseLocation"),Location);
                Input->SetArrayField(TEXT("baseRotation"),Numbers({RotationValue.X,RotationValue.Y,RotationValue.Z,RotationValue.W}));
            }
            Camera->TickComponent(Delta, LEVELTICK_All, &Camera->PrimaryComponentTick);
            if (Camera->IsRunningParallelEvaluation()) return false;
            auto Curves = MakeShared<FJsonObject>();
            for (const TCHAR* Name : {TEXT("PivotOffsetX"),TEXT("PivotOffsetY"),TEXT("PivotOffsetZ"),TEXT("CameraOffsetX"),TEXT("CameraOffsetY"),TEXT("CameraOffsetZ"),
                TEXT("LocationLagX"),TEXT("LocationLagY"),TEXT("LocationLagZ"),TEXT("RotationLag"),TEXT("TraceOverride"),TEXT("FirstPersonOverride"),TEXT("FovOffset")})
                Curves->SetNumberField(Name, Camera->GetAnimInstance()->GetCurveValue(FName(Name)));
            auto Row = MakeShared<FJsonObject>(); Row->SetObjectField(TEXT("input"),Input); Row->SetObjectField(TEXT("curves"),Curves);
            auto Output = State(Camera);
            if (bMovingBase)
            {
                Vector(Output,TEXT("baseLocalPivotLag"),Read<FVector>(Camera,TEXT("PivotLagLocationMovementBaseSpace")));
                const auto LocalRotation = Read<FQuat>(Camera,TEXT("CameraRotationMovementBaseSpace"));
                Output->SetArrayField(TEXT("baseLocalRotation"),Numbers({LocalRotation.X,LocalRotation.Y,LocalRotation.Z,LocalRotation.W}));
            }
            Row->SetObjectField(TEXT("output"),Output); Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Trace->SetArrayField(TEXT("frames"),Frames); Traces.Add(MakeShared<FJsonValueObject>(Trace));
        Camera->DestroyComponent();
    }
    auto Result = MakeShared<FJsonObject>(); Result->SetNumberField(TEXT("schemaVersion"),1);
    Result->SetStringField(TEXT("scope"),bMovingBase ? TEXT("Actual camera component; controlled moving/tilted bases, switch during first-person and leave; reference-pose character; no collision; observed native curves.") : TEXT("Actual UAlsCameraComponent::TickComponent; reference-pose character; controlled actor/view; clear scene, no movement base; observed native curves."));
    if (bCollision) Result->SetStringField(TEXT("scope"),TEXT("Actual camera component and native box queries; clear, swept wall, towards/away initial penetration, release; reference pose and observed curves; no base."));
    Result->SetArrayField(TEXT("traces"),Traces);
    FString Json;
    if (!FJsonSerializer::Serialize(Result,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)) ||
        !FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_CAMERA_COMPONENT_NATIVE_OK traces=3 frames=840 assets_saved=0"));
    return true;
}
