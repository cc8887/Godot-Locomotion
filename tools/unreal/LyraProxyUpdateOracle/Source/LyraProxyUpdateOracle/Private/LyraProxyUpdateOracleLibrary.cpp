#include "LyraProxyUpdateOracleLibrary.h"
#include "HAL/IConsoleManager.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_Root.h"
#include "Animation/AnimNode_StateMachine.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "Animation/AnimNode_LinkedAnimLayer.h"
#include "Animation/AnimNode_Inertialization.h"
#include "Animation/Skeleton.h"
#include "Animation/BlendSpace.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimMontageEvaluationState.h"
#include "Animation/AttributesRuntime.h"
#include "Animation/BuiltInAttributeTypes.h"
#include "Animation/BlendProfile.h"
#include "Animation/AnimRootMotionProvider.h"
#include "AnimNodes/AnimNode_LayeredBoneBlend.h"
#include "AnimNodes/AnimNode_BlendSpacePlayer.h"
#include "AnimNodes/AnimNode_RotationOffsetBlendSpace.h"
#include "AnimNodes/AnimNode_TwoWayBlend.h"
#include "AnimNodes/AnimNode_ApplyAdditive.h"
#include "AnimNodes/AnimNode_RotateRootBone.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "BoneControllers/AnimNode_OrientationWarping.h"
#include "AnimNode_ControlRig.h"
#include "ControlRig.h"
#include "RigVMCore/RigVM.h"
#include "RigVMCore/RigVMMemoryStorageStruct.h"
#include "Components/SkeletalMeshComponent.h"
#include "Components/BoxComponent.h"
#include "AbilitySystemComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/PhysicsVolume.h"
#include "GameFramework/PlayerController.h"
#include "Components/CapsuleComponent.h"
#include "UObject/StrongObjectPtr.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

namespace LyraWholeMainProbe
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_WHOLE_MAIN_FAILED line=%d"),Line);return {};}
TArray<TSharedPtr<FJsonValue>> Numbers(std::initializer_list<double> Values)
{TArray<TSharedPtr<FJsonValue>> R;for(double V:Values){check(FMath::IsFinite(V));R.Add(MakeShared<FJsonValueNumber>(V));}return R;}
TSharedPtr<FJsonObject> PhysicalInput(ACharacter* Owner,USkeletalMeshComponent* Component)
{
    auto R=MakeShared<FJsonObject>();auto* Move=Owner->GetCharacterMovement();
    const FVector Location=Owner->GetActorLocation(),Velocity=Move->Velocity,Acceleration=Move->GetCurrentAcceleration();const FRotator Rotation=Owner->GetActorRotation();
    R->SetArrayField(TEXT("location"),Numbers({Location.X,Location.Y,Location.Z}));R->SetArrayField(TEXT("rotation"),Numbers({Rotation.Pitch,Rotation.Yaw,Rotation.Roll}));
    R->SetArrayField(TEXT("velocity"),Numbers({Velocity.X,Velocity.Y,Velocity.Z}));R->SetArrayField(TEXT("acceleration"),Numbers({Acceleration.X,Acceleration.Y,Acceleration.Z}));
    R->SetBoolField(TEXT("ground"),Move->IsMovingOnGround());R->SetBoolField(TEXT("crouching"),Owner->bIsCrouched);R->SetNumberField(TEXT("movementMode"),(int32)Move->MovementMode);
    R->SetNumberField(TEXT("aimPitch"),Owner->GetBaseAimRotation().Pitch);R->SetNumberField(TEXT("gravity"),Move->GetGravityZ());
    const FTransform ComponentTransform=Component->GetComponentTransform();const FVector P=ComponentTransform.GetLocation(),S=ComponentTransform.GetScale3D();const FQuat Q=ComponentTransform.GetRotation(),Relative=Component->GetRelativeTransform().GetRotation();
    auto Transform=MakeShared<FJsonObject>();Transform->SetArrayField(TEXT("position"),Numbers({P.X,P.Y,P.Z}));Transform->SetArrayField(TEXT("rotation"),Numbers({Q.X,Q.Y,Q.Z,Q.W}));Transform->SetArrayField(TEXT("scale"),Numbers({S.X,S.Y,S.Z}));R->SetObjectField(TEXT("component"),Transform);
    R->SetArrayField(TEXT("relativeRotation"),Numbers({Relative.X,Relative.Y,Relative.Z,Relative.W}));
    const FVector FloorPoint=Move->CurrentFloor.HitResult.ImpactPoint,FloorNormal=Move->CurrentFloor.HitResult.ImpactNormal;
    R->SetBoolField(TEXT("floorBlocking"),Move->CurrentFloor.bBlockingHit);R->SetArrayField(TEXT("floorPoint"),Numbers({FloorPoint.X,FloorPoint.Y,FloorPoint.Z}));R->SetArrayField(TEXT("floorNormal"),Numbers({FloorNormal.X,FloorNormal.Y,FloorNormal.Z}));
    auto Movement=MakeShared<FJsonObject>();const auto* Last=FindFProperty<FStructProperty>(Move->GetClass(),TEXT("LastUpdateVelocity"));check(Last);const FVector LastVelocity=*Last->ContainerPtrToValuePtr<FVector>(Move);
    Movement->SetArrayField(TEXT("lastUpdateVelocity"),Numbers({LastVelocity.X,LastVelocity.Y,LastVelocity.Z}));Movement->SetBoolField(TEXT("separate"),Move->bUseSeparateBrakingFriction);
    Movement->SetNumberField(TEXT("brakingFriction"),Move->BrakingFriction);Movement->SetNumberField(TEXT("groundFriction"),Move->GroundFriction);Movement->SetNumberField(TEXT("factor"),Move->BrakingFrictionFactor);Movement->SetNumberField(TEXT("deceleration"),Move->BrakingDecelerationWalking);R->SetObjectField(TEXT("movement"),Movement);
    return R;
}
TSharedPtr<FJsonObject> MotorProfile(ACharacter* Owner)
{
    auto R=MakeShared<FJsonObject>();auto* M=Owner->GetCharacterMovement();auto* Capsule=Owner->GetCapsuleComponent();
    R->SetStringField(TEXT("characterClass"),Owner->GetClass()->GetPathName());R->SetStringField(TEXT("movementClass"),M->GetClass()->GetPathName());
    R->SetNumberField(TEXT("radius"),Capsule->GetUnscaledCapsuleRadius());R->SetNumberField(TEXT("halfHeight"),Capsule->GetUnscaledCapsuleHalfHeight());
    for(const TCHAR* Name:{TEXT("MaxWalkSpeed"),TEXT("MaxWalkSpeedCrouched"),TEXT("MinAnalogWalkSpeed"),TEXT("MaxAcceleration"),TEXT("GroundFriction"),TEXT("BrakingFriction"),TEXT("BrakingFrictionFactor"),TEXT("BrakingDecelerationWalking"),TEXT("BrakingDecelerationFalling"),TEXT("BrakingSubStepTime"),TEXT("GravityScale"),TEXT("JumpZVelocity"),TEXT("AirControl"),TEXT("AirControlBoostMultiplier"),TEXT("AirControlBoostVelocityThreshold"),TEXT("FallingLateralFriction"),TEXT("CrouchedHalfHeight"),TEXT("MaxStepHeight"),TEXT("WalkableFloorAngle"),TEXT("MaxSimulationTimeStep")})
    {auto* P=FindFProperty<FFloatProperty>(M->GetClass(),Name);check(P);R->SetNumberField(Name,P->GetPropertyValue_InContainer(M));}
    R->SetBoolField(TEXT("bUseSeparateBrakingFriction"),M->bUseSeparateBrakingFriction);
    R->SetNumberField(TEXT("gravityZ"),M->GetGravityZ());R->SetNumberField(TEXT("terminalVelocity"),M->GetPhysicsVolume()->TerminalVelocity);
    R->SetNumberField(TEXT("MaxSimulationIterations"),FindFProperty<FIntProperty>(M->GetClass(),TEXT("MaxSimulationIterations"))->GetPropertyValue_InContainer(M));
    R->SetNumberField(TEXT("JumpMaxHoldTime"),FindFProperty<FFloatProperty>(Owner->GetClass(),TEXT("JumpMaxHoldTime"))->GetPropertyValue_InContainer(Owner));
    R->SetNumberField(TEXT("JumpMaxCount"),FindFProperty<FIntProperty>(Owner->GetClass(),TEXT("JumpMaxCount"))->GetPropertyValue_InContainer(Owner));
    R->SetBoolField(TEXT("bApplyGravityWhileJumping"),FindFProperty<FBoolProperty>(M->GetClass(),TEXT("bApplyGravityWhileJumping"))->GetPropertyValue_InContainer(M));
    for(const TCHAR* Name:{TEXT("bUseFlatBaseForFloorChecks"),TEXT("bMaintainHorizontalGroundVelocity"),TEXT("bAlwaysCheckFloor"),TEXT("bCanWalkOffLedges"),TEXT("bCanWalkOffLedgesWhenCrouching")})
    {auto* P=FindFProperty<FBoolProperty>(M->GetClass(),Name);check(P);R->SetBoolField(Name,P->GetPropertyValue_InContainer(M));}
    for(const TCHAR* Name:{TEXT("PerchRadiusThreshold"),TEXT("PerchAdditionalHeight")})
    {auto* P=FindFProperty<FFloatProperty>(M->GetClass(),Name);check(P);R->SetNumberField(Name,P->GetPropertyValue_InContainer(M));}
    R->SetNumberField(TEXT("MaxJumpApexAttemptsPerSimulation"),M->MaxJumpApexAttemptsPerSimulation);
    R->SetNumberField(TEXT("minimumFloorDistance"),UCharacterMovementComponent::MIN_FLOOR_DIST);
    R->SetNumberField(TEXT("maximumFloorDistance"),UCharacterMovementComponent::MAX_FLOOR_DIST);
    R->SetNumberField(TEXT("sweepEdgeRejectDistance"),UCharacterMovementComponent::SWEEP_EDGE_REJECT_DISTANCE);
    R->SetBoolField(TEXT("canCrouch"),M->GetNavAgentPropertiesRef().bCanCrouch);return R;
}
TSharedPtr<FJsonObject> PenetrationProfile(ACharacter* Owner)
{
    auto R=MakeShared<FJsonObject>();auto* M=Owner->GetCharacterMovement();
    for(const TCHAR* Name:{TEXT("MaxDepenetrationWithGeometry"),TEXT("MaxDepenetrationWithGeometryAsProxy"),TEXT("MaxDepenetrationWithPawn"),TEXT("MaxDepenetrationWithPawnAsProxy")})
    {auto* P=FindFProperty<FFloatProperty>(M->GetClass(),Name);check(P);R->SetNumberField(Name,P->GetPropertyValue_InContainer(M));}
    for(const TCHAR* Name:{TEXT("p.PenetrationPullbackDistance"),TEXT("p.PenetrationOverlapCheckInflation"),TEXT("p.InitialOverlapTolerance")})
    {auto* V=IConsoleManager::Get().FindConsoleVariable(Name);check(V);R->SetNumberField(Name,V->GetFloat());}
    R->SetNumberField(TEXT("p.MoveIgnoreFirstBlockingOverlap"),IConsoleManager::Get().FindConsoleVariable(TEXT("p.MoveIgnoreFirstBlockingOverlap"))->GetInt());
    R->SetNumberField(TEXT("role"),Owner->GetLocalRole());return R;
}
struct FMovementAccess:UCharacterMovementComponent
{
    static void Calc(UCharacterMovementComponent* M,float Delta,float Friction,float Deceleration)
    {(M->*&FMovementAccess::CalcVelocity)(Delta,Friction,false,Deceleration);}
    static FVector FallAcceleration(UCharacterMovementComponent* M,float Delta)
    {return (M->*&FMovementAccess::GetFallingLateralAcceleration)(Delta);}
    static FVector FallVelocity(UCharacterMovementComponent* M,const FVector& V,float Delta)
    {return (M->*&FMovementAccess::NewFallVelocity)(V,FVector(0,0,M->GetGravityZ()),Delta);}
    static void GroundMove(UCharacterMovementComponent* M,const FVector& V,float Delta,FStepDownResult* Down)
    {(M->*&FMovementAccess::MoveAlongFloor)(V,Delta,Down);}
    static void Height(UCharacterMovementComponent* M)
    {(M->*&FMovementAccess::AdjustFloorHeight)();}
    static void Falling(UCharacterMovementComponent* M,float Delta)
    {(M->*&FMovementAccess::PhysFalling)(Delta,0);}
    static int32& Apex(UCharacterMovementComponent* M)
    {return M->*&FMovementAccess::NumJumpApexAttempts;}
    static FRandomStream& Random(UCharacterMovementComponent* M)
    {return M->*&FMovementAccess::RandomStream;}
};
TArray<TSharedPtr<FJsonValue>> MovementKernel(ACharacter* Owner)
{
    auto* M=Owner->GetCharacterMovement();auto* C=Owner->GetCapsuleComponent();
    const FTransform SavedTransform=Owner->GetActorTransform();const FVector SavedVelocity=M->Velocity;
    const float Radius=C->GetUnscaledCapsuleRadius(),SavedHalf=C->GetUnscaledCapsuleHalfHeight();
    const EMovementMode SavedMode=M->MovementMode;const FFindFloorResult SavedFloor=M->CurrentFloor;
    const bool SavedTeleport=M->bJustTeleported;
    TArray<AActor*> Scratch;FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
    for(const FVector& Spec:{FVector(1200,100,15),FVector(1600,100,27.5)})
    {
        auto* A=Owner->GetWorld()->SpawnActor<AActor>(Spawn);check(A);Scratch.Add(A);
        auto* B=NewObject<UBoxComponent>(A,NAME_None,RF_Transient);A->SetRootComponent(B);
        B->SetBoxExtent(FVector(Spec.Y,100,Spec.Z));B->SetCollisionProfileName(TEXT("BlockAll"));B->SetCollisionResponseToAllChannels(ECR_Block);
        B->SetWorldLocation(FVector(Spec.X,0,Spec.Z));A->AddInstanceComponent(B);B->RegisterComponent();
    }
    struct FCase{const TCHAR* Name;double X;FVector Delta;};
    const TArray<FCase> Cases={{TEXT("free"),100,FVector(10,2,0)},{TEXT("wall-normal"),580,FVector(20,0,0)},
        {TEXT("wall-tangent"),580,FVector(20,5,0)},{TEXT("wall-short"),589.9,FVector(.5,.01,0)},
        {TEXT("wall-up"),589,FVector(10,1,8)},{TEXT("floor-down"),100,FVector(0,0,-10)},
        {TEXT("step30"),1040,FVector(100,5,0)},{TEXT("step55"),1440,FVector(100,5,0)}};
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(float Half:{SavedHalf,M->CrouchedHalfHeight})for(int Kind:{0,1})for(const auto& Test:Cases)
    {
        C->SetCapsuleSize(Radius,Half,false);const FVector P(Test.X,0,Half+2);
        Owner->SetActorLocation(P,false,nullptr,ETeleportType::TeleportPhysics);M->MovementMode=MOVE_Walking;M->bJustTeleported=false;
        M->K2_FindFloor(P,M->CurrentFloor);FHitResult Hit(1);FStepDownResult Down;
        const float DeltaTime=1.f/60.f;M->Velocity=Test.Delta/DeltaTime;
        if(Kind==0)M->SafeMoveUpdatedComponent(Test.Delta,C->GetComponentQuat(),true,Hit);
        else
        {
            FMovementAccess::GroundMove(M,M->Velocity,DeltaTime,&Down);
            if(Down.bComputedFloor)M->CurrentFloor=Down.FloorResult;else M->K2_FindFloor(Owner->GetActorLocation(),M->CurrentFloor);
            if(M->CurrentFloor.IsWalkableFloor())FMovementAccess::Height(M);
            M->Velocity=(Owner->GetActorLocation()-P)/DeltaTime;M->Velocity.Z=0;
        }
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("name"),Test.Name);R->SetStringField(TEXT("kind"),Kind==0?TEXT("sweep"):TEXT("ground"));
        R->SetNumberField(TEXT("halfHeight"),Half);R->SetNumberField(TEXT("radius"),Radius);R->SetNumberField(TEXT("delta"),DeltaTime);
        R->SetArrayField(TEXT("start"),Numbers({P.X,P.Y,P.Z}));R->SetArrayField(TEXT("motion"),Numbers({Test.Delta.X,Test.Delta.Y,Test.Delta.Z}));
        const FVector After=Owner->GetActorLocation(),V=M->Velocity;
        R->SetArrayField(TEXT("after"),Numbers({After.X,After.Y,After.Z}));R->SetArrayField(TEXT("velocity"),Numbers({V.X,V.Y,V.Z}));
        R->SetBoolField(TEXT("blocking"),Hit.bBlockingHit);R->SetBoolField(TEXT("penetrating"),Hit.bStartPenetrating);R->SetNumberField(TEXT("time"),Hit.Time);
        const FVector Point=Hit.ImpactPoint,Normal=Hit.Normal,Impact=Hit.ImpactNormal,Location=Hit.Location;
        R->SetArrayField(TEXT("point"),Numbers({Point.X,Point.Y,Point.Z}));R->SetArrayField(TEXT("normal"),Numbers({Normal.X,Normal.Y,Normal.Z}));
        R->SetArrayField(TEXT("impactNormal"),Numbers({Impact.X,Impact.Y,Impact.Z}));R->SetArrayField(TEXT("hitLocation"),Numbers({Location.X,Location.Y,Location.Z}));
        R->SetBoolField(TEXT("stepFloorComputed"),Down.bComputedFloor);R->SetBoolField(TEXT("walkable"),M->CurrentFloor.IsWalkableFloor());R->SetNumberField(TEXT("floorDistance"),M->CurrentFloor.FloorDist);
        Rows.Add(MakeShared<FJsonValueObject>(R));
    }
    C->SetCapsuleSize(Radius,SavedHalf,false);Owner->SetActorTransform(SavedTransform,false,nullptr,ETeleportType::TeleportPhysics);
    M->Velocity=SavedVelocity;M->MovementMode=SavedMode;M->CurrentFloor=SavedFloor;M->bJustTeleported=SavedTeleport;
    for(auto* A:Scratch)A->Destroy();return Rows;
}
TArray<TSharedPtr<FJsonValue>> AirPhysicalKernel(ACharacter* Owner)
{
    auto* M=Owner->GetCharacterMovement();auto* C=Owner->GetCapsuleComponent();
    auto* Acc=FindFProperty<FStructProperty>(M->GetClass(),TEXT("Acceleration"));
    auto* Analog=FindFProperty<FFloatProperty>(M->GetClass(),TEXT("AnalogInputModifier"));
    auto* Crouch=FindFProperty<FBoolProperty>(Owner->GetClass(),TEXT("bIsCrouched"));check(Acc&&Analog&&Crouch);
    const FTransform SavedTransform=Owner->GetActorTransform();const FVector SavedVelocity=M->Velocity,SavedAcceleration=*Acc->ContainerPtrToValuePtr<FVector>(M);
    const float Radius=C->GetUnscaledCapsuleRadius(),SavedHalf=C->GetUnscaledCapsuleHalfHeight(),SavedAnalog=Analog->GetPropertyValue_InContainer(M);
    const EMovementMode SavedMode=M->MovementMode;const FFindFloorResult SavedFloor=M->CurrentFloor;
    const bool SavedTeleport=M->bJustTeleported,SavedCrouch=Crouch->GetPropertyValue_InContainer(Owner),SavedMaintain=M->bCrouchMaintainsBaseLocation;
    const int32 SavedApex=FMovementAccess::Apex(M);const FRandomStream SavedRandom=FMovementAccess::Random(M);
    auto* SavedBase=Owner->GetMovementBase();const FName SavedBaseBone=Owner->GetBasedMovement().BoneName;const bool SavedForceFloor=M->bForceNextFloorCheck;
    TArray<AActor*> Scratch;FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
    for(int32 Kind:{0,1})
    {
        auto* A=Owner->GetWorld()->SpawnActor<AActor>(Spawn);check(A);Scratch.Add(A);
        auto* B=NewObject<UBoxComponent>(A,NAME_None,RF_Transient);A->SetRootComponent(B);
        B->SetBoxExtent(Kind==0?FVector(100,25,1000):FVector(200,200,5));
        B->SetCollisionProfileName(TEXT("BlockAll"));B->SetCollisionResponseToAllChannels(ECR_Block);
        B->SetWorldLocation(Kind==0?FVector(600,1080,1000):FVector(1500,0,100));
        if(Kind==1)B->SetWorldRotation(FRotator(-30,0,0));A->AddInstanceComponent(B);B->RegisterComponent();
    }
    struct FCase{const TCHAR* Name;double X,Y,Gap;FVector Velocity,Input;};
    const TArray<FCase> Cases={
        {TEXT("free-neutral"),100,0,200,FVector(100,50,300),FVector::ZeroVector},
        {TEXT("free-control"),100,0,200,FVector(25,0,300),FVector(1,0,0)},
        {TEXT("apex-control"),100,0,200,FVector(45,0,5),FVector(1,0,0)},
        {TEXT("apex-neutral"),100,0,200,FVector(300,50,5),FVector::ZeroVector},
        {TEXT("wall-rising"),589.274,0,2,FVector(600,15,500),FVector(1,0,0)},
        {TEXT("wall-falling"),589.5,0,80,FVector(600,-15,-500),FVector(1,0,0)},
        {TEXT("land-control"),100,0,2,FVector(300,10,-300),FVector(1,0,0)},
        {TEXT("land-brake"),100,0,2,FVector(300,100,-300),FVector::ZeroVector},
        {TEXT("floor-penetration"),100,0,-1,FVector(30,0,-50),FVector::ZeroVector},
        {TEXT("terminal"),100,0,5000,FVector(80,100,-3990),FVector::ZeroVector},
        {TEXT("corner-rising"),580,1010,30,FVector(600,600,200),FVector(1,1,0).GetSafeNormal()},
        {TEXT("slope-falling"),1500,0,120,FVector(-300,0,-300),FVector(-1,0,0)},
        {TEXT("wall-penetration"),590.5,0,30,FVector(-30,0,50),FVector::ZeroVector},
        {TEXT("corner-penetration"),590.5,1020.5,30,FVector(-30,-30,50),FVector::ZeroVector}};
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(float Half:{SavedHalf,M->CrouchedHalfHeight})for(float Delta:{1.f/30.f,1.f/60.f,1.f/120.f,.1f})for(const auto& Test:Cases)
    {
        C->SetCapsuleSize(Radius,Half,false);const FVector Start(Test.X,Test.Y,Half+Test.Gap),Acceleration=Test.Input*M->MaxAcceleration;
        Owner->SetActorLocation(Start,false,nullptr,ETeleportType::TeleportPhysics);
        M->MovementMode=MOVE_Falling;M->CurrentFloor.Clear();M->bCrouchMaintainsBaseLocation=false;M->bJustTeleported=false;
        Crouch->SetPropertyValue_InContainer(Owner,Half==M->CrouchedHalfHeight);M->Velocity=Test.Velocity;
        *Acc->ContainerPtrToValuePtr<FVector>(M)=Acceleration;Analog->SetPropertyValue_InContainer(M,Test.Input.Size());
        FMovementAccess::Apex(M)=0;FMovementAccess::Random(M).Initialize(12345);
        FMovementAccess::Falling(M,Delta);
        const FVector After=Owner->GetActorLocation(),Velocity=M->Velocity;
        auto R=MakeShared<FJsonObject>();R->SetStringField(TEXT("name"),Test.Name);R->SetNumberField(TEXT("halfHeight"),Half);R->SetNumberField(TEXT("radius"),Radius);R->SetNumberField(TEXT("delta"),Delta);
        R->SetArrayField(TEXT("start"),Numbers({Start.X,Start.Y,Start.Z}));R->SetArrayField(TEXT("velocity"),Numbers({Test.Velocity.X,Test.Velocity.Y,Test.Velocity.Z}));
        R->SetArrayField(TEXT("acceleration"),Numbers({Acceleration.X,Acceleration.Y,Acceleration.Z}));R->SetNumberField(TEXT("analog"),Test.Input.Size());
        R->SetArrayField(TEXT("after"),Numbers({After.X,After.Y,After.Z}));R->SetArrayField(TEXT("output"),Numbers({Velocity.X,Velocity.Y,Velocity.Z}));
        R->SetNumberField(TEXT("movementMode"),M->MovementMode);R->SetBoolField(TEXT("grounded"),M->IsMovingOnGround());R->SetNumberField(TEXT("apexAttempts"),FMovementAccess::Apex(M));
        R->SetBoolField(TEXT("walkable"),M->CurrentFloor.IsWalkableFloor());R->SetNumberField(TEXT("floorDistance"),M->CurrentFloor.FloorDist);
        R->SetNumberField(TEXT("randomSeedBefore"),12345);R->SetNumberField(TEXT("randomSeedAfter"),FMovementAccess::Random(M).GetCurrentSeed());
        R->SetNumberField(TEXT("forceJumpPeakSubstep"),IConsoleManager::Get().FindConsoleVariable(TEXT("p.ForceJumpPeakSubstep"))->GetInt());
        Rows.Add(MakeShared<FJsonValueObject>(R));
    }
    C->SetCapsuleSize(Radius,SavedHalf,false);Owner->SetActorTransform(SavedTransform,false,nullptr,ETeleportType::TeleportPhysics);
    M->Velocity=SavedVelocity;M->MovementMode=SavedMode;M->CurrentFloor=SavedFloor;M->bJustTeleported=SavedTeleport;M->bCrouchMaintainsBaseLocation=SavedMaintain;
    *Acc->ContainerPtrToValuePtr<FVector>(M)=SavedAcceleration;Analog->SetPropertyValue_InContainer(M,SavedAnalog);Crouch->SetPropertyValue_InContainer(Owner,SavedCrouch);
    FMovementAccess::Apex(M)=SavedApex;FMovementAccess::Random(M)=SavedRandom;
    Owner->SetBase(SavedBase,SavedBaseBone);M->bForceNextFloorCheck=SavedForceFloor;
    for(auto* A:Scratch)A->Destroy();return Rows;
}
TArray<TSharedPtr<FJsonValue>> FloorKernel(ACharacter* Owner)
{
    auto* M=Owner->GetCharacterMovement();auto* C=Owner->GetCapsuleComponent();
    const float SavedRadius=C->GetUnscaledCapsuleRadius(),SavedHalf=C->GetUnscaledCapsuleHalfHeight();
    const EMovementMode SavedMode=M->MovementMode;
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(float Half:{SavedHalf,M->CrouchedHalfHeight})
    {
        C->SetCapsuleSize(SavedRadius,Half,false);
        TArray<FVector> Locations;
        for(double Gap:{-1.,0.,.05,1.8,1.9,2.,2.4,3.,25.,60.})Locations.Add(FVector(0,0,Half+Gap));
        for(double X:{589.,589.5,590.,590.1,624.,650.,9990.,10000.,10010.,10020.,10030.,10034.,10036.,10100.})Locations.Add(FVector(X,0,Half+2.));
        for(int Kind:{0,1,2})for(const FVector& P:Locations)
        {
            M->MovementMode=Kind==2?MOVE_Falling:MOVE_Walking;
            const float Adjust=Kind==2?-UCharacterMovementComponent::MAX_FLOOR_DIST:UCharacterMovementComponent::MAX_FLOOR_DIST+UE_KINDA_SMALL_NUMBER;
            const float Distance=FMath::Max(UCharacterMovementComponent::MAX_FLOOR_DIST,M->MaxStepHeight+Adjust);
            FFindFloorResult F;if(Kind==0)M->K2_ComputeFloorDist(P,Distance,Distance,SavedRadius,F);else M->K2_FindFloor(P,F);
            auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("halfHeight"),Half);R->SetNumberField(TEXT("radius"),SavedRadius);R->SetNumberField(TEXT("sweepDistance"),Distance);R->SetNumberField(TEXT("lineDistance"),Distance);
            R->SetStringField(TEXT("query"),Kind==0?TEXT("compute"):TEXT("find"));R->SetBoolField(TEXT("walking"),Kind!=2);
            R->SetArrayField(TEXT("location"),Numbers({P.X,P.Y,P.Z}));R->SetBoolField(TEXT("blocking"),F.bBlockingHit);R->SetBoolField(TEXT("walkable"),F.bWalkableFloor);R->SetBoolField(TEXT("lineTrace"),F.bLineTrace);
            R->SetNumberField(TEXT("floorDistance"),F.FloorDist);R->SetNumberField(TEXT("lineDistanceResult"),F.LineDist);R->SetBoolField(TEXT("penetrating"),F.HitResult.bStartPenetrating);R->SetNumberField(TEXT("time"),F.HitResult.Time);
            const FVector Pnt=F.HitResult.ImpactPoint,N=F.HitResult.ImpactNormal;R->SetArrayField(TEXT("point"),Numbers({Pnt.X,Pnt.Y,Pnt.Z}));R->SetArrayField(TEXT("normal"),Numbers({N.X,N.Y,N.Z}));Rows.Add(MakeShared<FJsonValueObject>(R));
        }
    }
    C->SetCapsuleSize(SavedRadius,SavedHalf,false);M->MovementMode=SavedMode;return Rows;
}
TArray<TSharedPtr<FJsonValue>> VelocityKernel(ACharacter* Owner)
{
    auto* M=Owner->GetCharacterMovement();auto* Acc=FindFProperty<FStructProperty>(M->GetClass(),TEXT("Acceleration"));auto* Analog=FindFProperty<FFloatProperty>(M->GetClass(),TEXT("AnalogInputModifier"));auto* Crouch=FindFProperty<FBoolProperty>(Owner->GetClass(),TEXT("bIsCrouched"));check(Acc&&Analog&&Crouch);
    const FVector SavedVelocity=M->Velocity,SavedAcceleration=*Acc->ContainerPtrToValuePtr<FVector>(M);const float SavedAnalog=Analog->GetPropertyValue_InContainer(M);const bool SavedCrouch=Crouch->GetPropertyValue_InContainer(Owner);
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(float Delta:{0.f,1.f/120.f,1.f/60.f,1.f/30.f,.1f})for(bool Crouching:{false,true})
    for(const FVector V:{FVector::ZeroVector,FVector(5,0,0),FVector(300,0,0),FVector(600,0,0),FVector(800,-100,0)})
    for(const FVector Input:{FVector::ZeroVector,FVector(1,0,0),FVector(-1,0,0),FVector(.3,.4,0),FVector(0,1,0)})
    {
        M->Velocity=V;const FVector A=Input*M->MaxAcceleration;*Acc->ContainerPtrToValuePtr<FVector>(M)=A;const float Modifier=Input.Size();Analog->SetPropertyValue_InContainer(M,Modifier);Crouch->SetPropertyValue_InContainer(Owner,Crouching);
        FMovementAccess::Calc(M,Delta,M->GroundFriction,M->BrakingDecelerationWalking);
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("delta"),Delta);R->SetBoolField(TEXT("crouching"),Crouching);R->SetNumberField(TEXT("analog"),Modifier);R->SetArrayField(TEXT("velocity"),Numbers({V.X,V.Y,V.Z}));R->SetArrayField(TEXT("acceleration"),Numbers({A.X,A.Y,A.Z}));const auto Out=M->Velocity;R->SetArrayField(TEXT("output"),Numbers({Out.X,Out.Y,Out.Z}));Rows.Add(MakeShared<FJsonValueObject>(R));
    }
    M->Velocity=SavedVelocity;*Acc->ContainerPtrToValuePtr<FVector>(M)=SavedAcceleration;Analog->SetPropertyValue_InContainer(M,SavedAnalog);Crouch->SetPropertyValue_InContainer(Owner,SavedCrouch);return Rows;
}
TArray<TSharedPtr<FJsonValue>> FallingKernel(ACharacter* Owner)
{
    auto* M=Owner->GetCharacterMovement();auto* Acc=FindFProperty<FStructProperty>(M->GetClass(),TEXT("Acceleration"));auto* Analog=FindFProperty<FFloatProperty>(M->GetClass(),TEXT("AnalogInputModifier"));auto* Crouch=FindFProperty<FBoolProperty>(Owner->GetClass(),TEXT("bIsCrouched"));check(Acc&&Analog&&Crouch);
    const FVector SavedVelocity=M->Velocity,SavedAcceleration=*Acc->ContainerPtrToValuePtr<FVector>(M);const float SavedAnalog=Analog->GetPropertyValue_InContainer(M);const bool SavedCrouch=Crouch->GetPropertyValue_InContainer(Owner);const EMovementMode SavedMode=M->MovementMode;
    M->MovementMode=MOVE_Falling;Crouch->SetPropertyValue_InContainer(Owner,false);TArray<TSharedPtr<FJsonValue>> Rows;
    for(float D:{0.f,1.f/120.f,1.f/60.f,1.f/30.f,.1f})
    for(const FVector V:{FVector(0,0,500),FVector(25,0,300),FVector(50,0,-300),FVector(300,-100,500),FVector(800,100,-300),FVector(0,0,-3990),FVector(300,0,-5000)})
    for(const FVector Input:{FVector::ZeroVector,FVector(1,0,0),FVector(-1,0,0),FVector(.3,.4,0),FVector(0,1,0)})
    {
        M->Velocity=V;const FVector A=Input*M->MaxAcceleration;*Acc->ContainerPtrToValuePtr<FVector>(M)=A;const float Modifier=Input.Size();Analog->SetPropertyValue_InContainer(M,Modifier);
        const FVector FallingAcceleration=FMovementAccess::FallAcceleration(M,D);*Acc->ContainerPtrToValuePtr<FVector>(M)=FallingAcceleration;
        M->Velocity.Z=0;FMovementAccess::Calc(M,D,M->FallingLateralFriction,M->BrakingDecelerationFalling);M->Velocity.Z=V.Z;
        const FVector Out=FMovementAccess::FallVelocity(M,M->Velocity,D),Delta=.5f*(V+Out)*D;
        auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("delta"),D);R->SetNumberField(TEXT("analog"),Modifier);R->SetArrayField(TEXT("velocity"),Numbers({V.X,V.Y,V.Z}));R->SetArrayField(TEXT("acceleration"),Numbers({A.X,A.Y,A.Z}));R->SetArrayField(TEXT("fallAcceleration"),Numbers({FallingAcceleration.X,FallingAcceleration.Y,FallingAcceleration.Z}));R->SetArrayField(TEXT("output"),Numbers({Out.X,Out.Y,Out.Z}));R->SetArrayField(TEXT("displacement"),Numbers({Delta.X,Delta.Y,Delta.Z}));Rows.Add(MakeShared<FJsonValueObject>(R));
    }
    M->Velocity=SavedVelocity;*Acc->ContainerPtrToValuePtr<FVector>(M)=SavedAcceleration;Analog->SetPropertyValue_InContainer(M,SavedAnalog);Crouch->SetPropertyValue_InContainer(Owner,SavedCrouch);M->MovementMode=SavedMode;return Rows;
}
TSharedPtr<FJsonObject> PoseData(const FPoseContext& C,const FReferenceSkeleton& Ref)
{
    auto R=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Bones;
    for(auto B:C.Pose.ForEachBoneIndex())
    {
        auto O=MakeShared<FJsonObject>();const auto& T=C.Pose[B];auto P=T.GetLocation(),S=T.GetScale3D();auto Q=T.GetRotation();
        O->SetArrayField(TEXT("position"),Numbers({P.X,P.Y,P.Z}));O->SetArrayField(TEXT("rotation"),Numbers({Q.X,Q.Y,Q.Z,Q.W}));O->SetArrayField(TEXT("scale"),Numbers({S.X,S.Y,S.Z}));Bones.Add(MakeShared<FJsonValueObject>(O));
    }
    R->SetArrayField(TEXT("pose"),Bones);auto Curves=MakeShared<FJsonObject>();
    C.Curve.ForEachElement([&](const auto& E){auto O=MakeShared<FJsonObject>();O->SetNumberField(TEXT("value"),E.Value);O->SetNumberField(TEXT("flags"),(uint32)E.Flags);Curves->SetObjectField(E.Name.ToString(),O);});R->SetObjectField(TEXT("curves"),Curves);
    TArray<TSharedPtr<FJsonValue>> Attrs;const auto& A=C.CustomAttributes;
    for(int32 I=0;I<A.GetUniqueTypes().Num();I++)
    {
        auto* Type=A.GetUniqueTypes()[I].Get();if(Type!=FIntegerAnimationAttribute::StaticStruct()&&Type!=FTransformAnimationAttribute::StaticStruct())return {};
        for(int32 J=0;J<A.GetKeys(I).Num();J++)
        {
            const auto& K=A.GetKeys(I)[J];auto O=MakeShared<FJsonObject>();O->SetStringField(TEXT("name"),K.GetName().ToString());O->SetStringField(TEXT("namespace"),K.GetNamespace().ToString());O->SetStringField(TEXT("bone"),Ref.GetBoneName(K.GetIndex()).ToString());O->SetStringField(TEXT("type"),Type->GetPathName());
            if(Type==FTransformAnimationAttribute::StaticStruct())
            {
                if(K.GetName()!=UE::Anim::IAnimRootMotionProvider::AttributeName)return {};
                const auto& T=A.GetValues(I)[J].template GetPtr<FTransformAnimationAttribute>()->Value;auto P=T.GetLocation(),S=T.GetScale3D();auto Q=T.GetRotation();
                O->SetArrayField(TEXT("position"),Numbers({P.X,P.Y,P.Z}));O->SetArrayField(TEXT("rotation"),Numbers({Q.X,Q.Y,Q.Z,Q.W}));O->SetArrayField(TEXT("scale"),Numbers({S.X,S.Y,S.Z}));R->SetObjectField(TEXT("rootMotion"),O);
            }
            else{O->SetNumberField(TEXT("value"),A.GetValues(I)[J].template GetPtr<FIntegerAnimationAttribute>()->Value);O->SetStringField(TEXT("blend"),UE::Anim::Attributes::GetAttributeBlendType(K)==ECustomAttributeBlendType::Override?TEXT("Override"):TEXT("Blend"));Attrs.Add(MakeShared<FJsonValueObject>(O));}
        }
    }
    R->SetArrayField(TEXT("attributes"),Attrs);return R;
}
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
TSharedPtr<FJsonObject> Fields(UObject* A);
struct FRigNodeAccess:FAnimNode_ControlRigBase
{static float Alpha(const FAnimNode_ControlRig& N){return N.*&FRigNodeAccess::InternalBlendAlpha;}};
TSharedPtr<FJsonObject> RigParameters(FAnimNode_ControlRig* Node)
{
    auto* Rig=Node?Node->GetControlRig():nullptr;auto Result=Rig?Fields(Rig):MakeShared<FJsonObject>();
    if(Rig){Result->SetNumberField(TEXT("rigDelta"),Rig->GetDeltaTime());Result->SetNumberField(TEXT("nodeAlpha"),FRigNodeAccess::Alpha(*Node));Result->SetBoolField(TEXT("initRequired"),Rig->IsInitRequired());Result->SetBoolField(TEXT("constructionRequired"),Rig->IsConstructionRequired());}
    return Result;
}
TSharedPtr<FJsonObject> RigCollisionSnapshot(FAnimNode_ControlRig* Node)
{
    auto Result=MakeShared<FJsonObject>();auto* Rig=Node?Node->GetControlRig():nullptr;if(!Rig)return Result;
    auto* VM=Rig->GetVM();auto& Context=Rig->GetRigVMExtendedExecuteContext();const auto& Public=Context.GetPublicData<FControlRigExecuteContext>();
    const auto& Transform=Public.GetToWorldSpaceTransform();auto P=Transform.GetLocation(),S=Transform.GetScale3D();auto Q=Transform.GetRotation();
    auto World=MakeShared<FJsonObject>();World->SetArrayField(TEXT("position"),Numbers({P.X,P.Y,P.Z}));World->SetArrayField(TEXT("rotation"),Numbers({Q.X,Q.Y,Q.Z,Q.W}));World->SetArrayField(TEXT("scale"),Numbers({S.X,S.Y,S.Z}));Result->SetObjectField(TEXT("world"),World);
    const auto& Bytecode=VM->GetByteCode();const auto Instructions=Bytecode.GetInstructions();auto* Work=VM->GetWorkMemory(Context);auto* Literal=VM->GetLiteralMemory();TArray<TSharedPtr<FJsonValue>> Rows;
    for(int32 I=0;I<Instructions.Num();I++)
    {
        const auto& Op=Instructions[I];if(Op.OpCode!=ERigVMOpCode::Execute)continue;
        const auto& Execute=Bytecode.GetOpAt<FRigVMExecuteOp>(Op);if(VM->GetFunctionNames()[Execute.CallableIndex]!=TEXT("FRigUnit_SphereTraceByTraceChannel::Execute"))continue;
        const auto Operands=Bytecode.GetOperandsForOp(Op);if(Operands.Num()!=8)return {};
        auto Data=[&](int32 N)->uint8*{const auto& A=Operands[N];check(A.GetRegisterOffset()==INDEX_NONE);return (A.GetMemoryType()==ERigVMMemoryType::Work?Work:Literal)->GetData<uint8>(A.GetRegisterIndex());};
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("instruction"),I);
        for(const auto& Pair:{TPair<int32,const TCHAR*>(0,TEXT("start")),{1,TEXT("end")},{5,TEXT("position")},{6,TEXT("normal")}})
        {const auto& V=*reinterpret_cast<FVector*>(Data(Pair.Key));Row->SetArrayField(Pair.Value,Numbers({V.X,V.Y,V.Z}));}
        Row->SetNumberField(TEXT("traceChannel"),*Data(2));Row->SetNumberField(TEXT("radius"),*reinterpret_cast<float*>(Data(3)));Row->SetBoolField(TEXT("hit"),*reinterpret_cast<bool*>(Data(4)));Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("queries"),Rows);return Result;
}
template<class Tag,typename Tag::Type Member>struct TProxyMember{friend typename Tag::Type ProxyAccess(Tag){return Member;}};
struct FTraversalFrameMember{using Type=uint64 FGraphTraversalCounter::*;friend Type ProxyAccess(FTraversalFrameMember);};
template struct TProxyMember<FTraversalFrameMember,&FGraphTraversalCounter::LastSyncronizedFrame>;
TSharedPtr<FJsonObject> ProxyPhases(const FAnimInstanceProxy& P)
{
    auto J=MakeShared<FJsonObject>();
    auto Add=[&](const TCHAR* Name,const FGraphTraversalCounter& C)
    {auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("counter"),C.Get());V->SetNumberField(TEXT("frame"),C.HasEverBeenUpdated()?static_cast<double>(C.*ProxyAccess(FTraversalFrameMember{})):-1);J->SetObjectField(Name,V);};
    Add(TEXT("initialization"),P.GetInitializationCounter());Add(TEXT("bones"),P.GetCachedBonesCounter());
    Add(TEXT("update"),P.GetUpdateCounter());Add(TEXT("evaluation"),P.GetEvaluationCounter());return J;
}
struct FLayerBlendAccess:FAnimNode_LayeredBoneBlend
{static const TArray<FPerBoneBlendWeight>& Weights(FAnimNode_LayeredBoneBlend& N){return N.*&FLayerBlendAccess::CurrentBoneBlendWeights;}};
struct FProxyAccess:FAnimInstanceProxy
{
    static const TArray<FMontageEvaluationState>& Frozen(FAnimInstanceProxy& P)
    {const auto Read=static_cast<const TArray<FMontageEvaluationState>&(FAnimInstanceProxy::*)()const>(&FProxyAccess::GetMontageEvaluationData);return (P.*Read)();}
    // Component registration already initialized instance-level nodes. Repeating
    // InitializeRootNode calls OnInitializeAnimInstance and unlinks dynamically
    // installed item layers; only initialize the existing graph after remapping.
    static void Init(FAnimInstanceProxy& P){(P.*&FProxyAccess::InitializeRootNode_WithRoot)(P.GetRootNode());(P.*&FProxyAccess::CacheBones)();}
    static void Objects(FAnimInstanceProxy& P,UAnimInstance* A){(P.*&FProxyAccess::InitializeObjects)(A);}
    static void Bones(FAnimInstanceProxy& P,USkeleton* S)
    {TArray<FBoneIndexType> B;for(int32 I=0;I<S->GetReferenceSkeleton().GetNum();I++)B.Add((FBoneIndexType)I);P.GetRequiredBones().InitializeTo(B,UE::Anim::FCurveFilterSettings(),*S);P.GetRequiredBones().SetUseRAWData(true);P.GetRequiredBones().SetDisableRetargeting(false);(P.*&FProxyAccess::CachedBonesCounter).Increment();}
    // A newly linked instance has already been initialized by the engine.
    // Refresh adapted masks/bone references without initializing Main or any
    // linked state machine again. Each original function retains its history.
    static void Recache(FAnimInstanceProxy& P,FAnimNode_Base* Root)
    {const auto Invalidate=static_cast<void(FAnimInstanceProxy::*)(const UE::Anim::FCurveFilterSettings&)>(&FProxyAccess::RecalcRequiredCurves);(P.*Invalidate)(UE::Anim::FCurveFilterSettings());(P.*&FProxyAccess::CacheBones_WithRoot)(Root);}
    static void Publish(FAnimInstanceProxy& P,const FPoseContext& C)
    {auto& Map=(P.*static_cast<TMap<FName,float>&(FAnimInstanceProxy::*)(EAnimCurveType)>(&FProxyAccess::GetAnimationCurves))(EAnimCurveType::AttributeCurve);Map.Reset();C.Curve.ForEachElement([&](const auto& E){Map.Add(E.Name,E.Value);});}
};
bool Set(UObject* A,const FString& Name,const TSharedPtr<FJsonValue>& V)
{
    auto* P=A->GetClass()->FindPropertyByName(FName(*Name));if(auto* B=CastField<FBoolProperty>(P)){B->SetPropertyValue_InContainer(A,V->AsBool());return true;}
    if(auto* N=CastField<FNumericProperty>(P)){auto* D=N->ContainerPtrToValuePtr<void>(A);if(N->IsFloatingPoint())N->SetFloatingPointPropertyValue(D,V->AsNumber());else N->SetIntPropertyValue(D,(int64)V->AsNumber());return true;}return false;
}
FVector Vector(const TSharedPtr<FJsonObject>& O,const TCHAR* Name)
{const auto& A=O->GetArrayField(Name);check(A.Num()==3);return FVector(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber());}
TSharedPtr<FJsonObject> Fields(UObject* A)
{
    auto R=MakeShared<FJsonObject>();
    for(TFieldIterator<FProperty> I(A->GetClass());I;++I)
    {
        auto* P=*I;const auto Name=P->GetName();const void* V=P->ContainerPtrToValuePtr<void>(A);
        if(auto* B=CastField<FBoolProperty>(P))R->SetBoolField(Name,B->GetPropertyValue(V));
        else if(auto* N=CastField<FNumericProperty>(P))R->SetNumberField(Name,N->IsFloatingPoint()?N->GetFloatingPointPropertyValue(V):N->GetSignedIntPropertyValue(V));
        else if(auto* S=CastField<FStructProperty>(P);S&&S->Struct==TBaseStructure<FVector>::Get()){auto Q=*static_cast<const FVector*>(V);R->SetArrayField(Name,Numbers({Q.X,Q.Y,Q.Z}));}
    }return R;
}
bool Bind(UAnimInstance* A,const TSharedPtr<FJsonObject>& B,const TMap<FString,UAnimSequence*>& Assets)
{
    for(const auto& G:B->Values)
    {
        auto* P=A->GetClass()->FindPropertyByName(FName(*G.Key));
        if(auto* O=CastField<FObjectPropertyBase>(P)){auto* S=Assets.FindRef(G.Value->AsString());if(!S)return false;O->SetObjectPropertyValue_InContainer(A,S);continue;}
        auto* S=CastField<FStructProperty>(P);if(!S)return false;void* D=S->ContainerPtrToValuePtr<void>(A);
        for(const auto& V:G.Value->AsObject()->Values){FObjectPropertyBase* F=nullptr;for(TFieldIterator<FObjectPropertyBase> I(S->Struct);I;++I)if(I->GetName().StartsWith(FString(V.Key)+TEXT("_")))F=*I;auto* Sequence=Assets.FindRef(V.Value->AsString());if(!F||!Sequence)return false;F->SetObjectPropertyValue(F->ContainerPtrToValuePtr<void>(D),Sequence);}
    }return true;
}
bool Remap(FProperty* P,void* Value,const TMap<FString,UAnimSequence*>& Sequences,const TMap<UBlendSpace*,UBlendSpace*>& Spaces)
{
    if(auto* Object=CastField<FObjectPropertyBase>(P))
    {
        auto* Asset=Object->GetObjectPropertyValue(Value);
        if(auto* Sequence=Cast<UAnimSequence>(Asset))
        {if(auto* Target=Sequences.FindRef(Sequence->GetPathName()))Object->SetObjectPropertyValue(Value,Target);}
        else if(auto* Space=Cast<UBlendSpace>(Asset))
        {if(auto* Target=Spaces.FindRef(Space))Object->SetObjectPropertyValue(Value,Target);}
    }
    else if(auto* Struct=CastField<FStructProperty>(P))
    {for(TFieldIterator<FProperty> I(Struct->Struct);I;++I)if(!Remap(*I,I->ContainerPtrToValuePtr<void>(Value),Sequences,Spaces))return false;}
    else if(auto* Array=CastField<FArrayProperty>(P))
    {FScriptArrayHelper Helper(Array,Value);for(int32 I=0;I<Helper.Num();I++)if(!Remap(Array->Inner,Helper.GetRawPtr(I),Sequences,Spaces))return false;}
    return true;
}
struct FTap:FAnimNode_Base
{
    FPoseLink Child;FString Name;const FReferenceSkeleton* Ref=nullptr;FAnimNode_BlendSpacePlayer* SampleNode=nullptr;TArray<TSharedPtr<FJsonValue>> Updates,Outputs;
    void Initialize_AnyThread(const FAnimationInitializeContext& C) override{Child.Initialize(C);}
    void CacheBones_AnyThread(const FAnimationCacheBonesContext& C) override{Child.CacheBones(C);}
    void Update_AnyThread(const FAnimationUpdateContext& C) override{auto O=MakeShared<FJsonObject>();O->SetStringField(TEXT("hook"),Name);O->SetObjectField(TEXT("proxyPhases"),ProxyPhases(*C.AnimInstanceProxy));O->SetNumberField(TEXT("weight"),C.GetFinalBlendWeight());O->SetBoolField(TEXT("active"),C.IsActive());Updates.Add(MakeShared<FJsonValueObject>(O));Child.Update(C);}
    void Evaluate_AnyThread(FPoseContext& C) override
    {
        Child.Evaluate(C);auto O=MakeShared<FJsonObject>();O->SetStringField(TEXT("hook"),Name);O->SetObjectField(TEXT("proxyPhases"),ProxyPhases(*C.AnimInstanceProxy));O->SetObjectField(TEXT("output"),PoseData(C,*Ref));Outputs.Add(MakeShared<FJsonValueObject>(O));
        // Read the original BlendSpace sampling boundary after its original
        // evaluation. No update, state, clock or supplied pose is substituted.
        if(SampleNode){FPoseContext S(C);SampleNode->FAnimNode_BlendSpacePlayer::Evaluate_AnyThread(S);auto A=MakeShared<FJsonObject>();A->SetStringField(TEXT("hook"),Name+TEXT("_Additive"));A->SetObjectField(TEXT("output"),PoseData(S,*Ref));Outputs.Add(MakeShared<FJsonValueObject>(A));}
    }
};
}

bool ULyraProxyUpdateOracleLibrary::CopySourceNotifies(UAnimSequence* Source,UAnimSequence* Target)
{
    if(!Source||!Target||Source==Target||!Target->HasAnyFlags(RF_Transient)||Target->GetOutermost()!=GetTransientPackage()||Source->GetPlayLength()!=Target->GetPlayLength())return false;
    Target->Notifies=Source->Notifies;return true;
}

FString ULyraProxyUpdateOracleLibrary::ReadTrace(USkeletalMesh* Mesh,USkeleton* Skeleton,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraWholeMainProbe;TSharedPtr<FJsonObject> Q;if(!Mesh||!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    const auto& Paths=Q->GetArrayField(TEXT("sequencePaths"));if(Paths.Num()!=Sequences.Num())return Fail(__LINE__);TMap<FString,UAnimSequence*> Assets;
    for(int32 I=0;I<Sequences.Num();I++){auto* S=Sequences[I];if(!S||S->GetSkeleton()!=Skeleton)return Fail(__LINE__);S->WaitOnExistingCompression(true);Assets.Add(Paths[I]->AsString(),S);}
    TMap<FString,UAnimSequence*> SourceAssets;for(const auto& Entry:Q->GetObjectField(TEXT("sourceTargets"))->Values){auto* Target=Assets.FindRef(Entry.Value->AsString());if(!Target)return Fail(__LINE__);SourceAssets.Add(FString(Entry.Key),Target);}
    auto* MC=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("mainClass")));const auto* MI=MC?IAnimClassInterface::GetFromClass(MC):nullptr;if(!MI)return Fail(__LINE__);
    TArray<TStrongObjectPtr<UAnimMontage>> MontageStorage;TArray<UAnimMontage*> Montages;TArray<TStrongObjectPtr<UBlendProfile>> MontageProfiles;
    const TArray<TSharedPtr<FJsonValue>>* MontagePaths=nullptr;
    if(Q->TryGetArrayField(TEXT("montagePaths"),MontagePaths))
    {
        auto Mapping=Q->GetObjectField(TEXT("montageTargets"));TMap<const UBlendProfile*,UBlendProfile*> ProfileMap;
        auto Adapt=[&](const UBlendProfile* Original)->UBlendProfile*
        {
            if(!Original)return nullptr;if(auto** Found=ProfileMap.Find(Original))return *Found;
            MontageProfiles.Emplace(NewObject<UBlendProfile>(GetTransientPackage()));auto* P=MontageProfiles.Last().Get();P->OwningSkeleton=Skeleton;P->Mode=Original->Mode;
            for(int32 B=0;B<81;B++){const int32 Index=Original->GetEntryIndex(Skeleton->GetReferenceSkeleton().GetBoneName(B));if(Index!=INDEX_NONE)P->SetBoneBlendScale(B,Original->GetEntryBlendScale(Index),false,true);}
            ProfileMap.Add(Original,P);return P;
        };
        for(const auto& Path:*MontagePaths)
        {
            auto* Original=LoadObject<UAnimMontage>(nullptr,*Path->AsString());if(!Original)return Fail(__LINE__);
            MontageStorage.Emplace(DuplicateObject<UAnimMontage>(Original,GetTransientPackage()));auto* M=MontageStorage.Last().Get();M->ClearFlags(RF_Public|RF_Standalone);M->SetFlags(RF_Transient);M->SetSkeleton(Skeleton);
            for(auto& Slot:M->SlotAnimTracks)
            {
                Skeleton->SetSlotGroupName(Slot.SlotName,Original->GetGroupName());
                for(auto& Segment:Slot.AnimTrack.AnimSegments){const UAnimSequenceBase* A=Segment.GetAnimReference();if(!A||!Mapping->HasField(A->GetPathName()))return Fail(__LINE__);auto* Target=Assets.FindRef(Mapping->GetStringField(A->GetPathName()));if(!Target)return Fail(__LINE__);Segment.SetAnimReference(Target);}
            }
            M->BlendProfileIn=Adapt(Original->BlendProfileIn);M->BlendProfileOut=Adapt(Original->BlendProfileOut);Montages.Add(M);
        }
    }
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Q->GetArrayField(TEXT("traces")))
    {
        FMemMark Mark(FMemStack::Get());auto T=TV->AsObject();auto* LC=LoadObject<UClass>(nullptr,*T->GetStringField(TEXT("class")));const auto* LI=LC?IAnimClassInterface::GetFromClass(LC):nullptr;if(!LI)return Fail(__LINE__);
        const bool Physics=T->GetStringField(TEXT("case"))==TEXT("physics");
        const TSharedPtr<FJsonObject>* FunctionGroups=nullptr;
        const bool Multi=T->TryGetObjectField(TEXT("functionGroups"),FunctionGroups);
        // Controlled transient metadata fixture; retain each compiled graph root.
        auto& MutableFunctions=const_cast<TArray<FAnimBlueprintFunction>&>(LI->GetAnimBlueprintFunctions());
        struct FGroupRestore{TArray<FAnimBlueprintFunction>& Target;TArray<FAnimBlueprintFunction> Original;~FGroupRestore(){Target=MoveTemp(Original);}} GroupRestore{MutableFunctions,MutableFunctions};
        if(Multi){if(Physics||T->GetStringField(TEXT("case"))==TEXT("rebind"))return Fail(__LINE__);for(const auto& E:(*FunctionGroups)->Values){auto* Fn=MutableFunctions.FindByPredicate([&](const auto& F){return F.Name==FName(*E.Key);});if(!Fn||!Fn->bImplemented)return Fail(__LINE__);Fn->Group=FName(*E.Value->AsString());}}
        auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!W.IsValid())return Fail(__LINE__);
        struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{W.Get()};FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;
        auto* CharacterClass=Physics?LoadClass<ACharacter>(nullptr,*T->GetStringField(TEXT("characterClass"))):ACharacter::StaticClass();if(!CharacterClass)return Fail(__LINE__);
        auto* Owner=W->SpawnActor<ACharacter>(CharacterClass,Physics?FVector(0,0,92):FVector::ZeroVector,FRotator::ZeroRotator,Spawn);auto* Controller=W->SpawnActor<APlayerController>(Spawn);auto* Ground=W->SpawnActor<AActor>(Spawn);if(!Owner||!Controller||!Ground)return Fail(__LINE__);Controller->Possess(Owner);
        // Original melee notifies dispatch a gameplay event. Supply its real
        // component boundary without installing abilities or replacing notifies.
        TStrongObjectPtr<UAbilitySystemComponent> AbilitySystem;
        if(MontagePaths){AbilitySystem.Reset(NewObject<UAbilitySystemComponent>(Owner,NAME_None,RF_Transient));Owner->AddInstanceComponent(AbilitySystem.Get());AbilitySystem->RegisterComponent();AbilitySystem->InitAbilityActorInfo(Owner,Owner);}
        TStrongObjectPtr<UBoxComponent> Floor(NewObject<UBoxComponent>(Ground,NAME_None,RF_Transient));Ground->SetRootComponent(Floor.Get());Floor->SetBoxExtent(FVector(10000,10000,5));Floor->SetCollisionProfileName(TEXT("BlockAll"));Floor->SetWorldLocation(FVector(0,0,-5));Ground->AddInstanceComponent(Floor.Get());Floor->RegisterComponent();
        TArray<TStrongObjectPtr<UBoxComponent>> Obstacles;
        if(Physics)
        {
            // Block the original Rig's custom Traversable trace as well as
            // the Pawn channel used by CharacterMovement.
            Floor->SetCollisionResponseToAllChannels(ECR_Block);
            // A spawned controller has no LocalPlayer in this isolated world.
            // Use the native GameMode designation so TickComponent executes
            // ControlledCharacterMove rather than the remote-client branch.
            Controller->SetAsLocalPlayerController();
            if(Owner->GetCharacterMovement()->GetClass()->GetPathName()!=TEXT("/Script/LyraGame.LyraCharacterMovementComponent"))return Fail(__LINE__);
            for(const auto& V:T->GetArrayField(TEXT("obstacles")))
            {auto O=V->AsObject();auto* A=W->SpawnActor<AActor>(Spawn);if(!A)return Fail(__LINE__);Obstacles.Emplace(NewObject<UBoxComponent>(A,NAME_None,RF_Transient));auto* B=Obstacles.Last().Get();A->SetRootComponent(B);B->SetBoxExtent(Vector(O,TEXT("extent")));B->SetCollisionProfileName(TEXT("BlockAll"));B->SetCollisionResponseToAllChannels(ECR_Block);B->SetWorldLocation(Vector(O,TEXT("center")));A->AddInstanceComponent(B);B->RegisterComponent();}
            Owner->GetCharacterMovement()->SetMovementMode(MOVE_Walking);
        }
        const auto Kernel=Physics?VelocityKernel(Owner):TArray<TSharedPtr<FJsonValue>>{};
        const auto FallKernel=Physics?FallingKernel(Owner):TArray<TSharedPtr<FJsonValue>>{};
        const auto FloorRows=Physics?FloorKernel(Owner):TArray<TSharedPtr<FJsonValue>>{};
        const auto MoveRows=Physics?MovementKernel(Owner):TArray<TSharedPtr<FJsonValue>>{};
        const auto AirRows=Physics?AirPhysicalKernel(Owner):TArray<TSharedPtr<FJsonValue>>{};
        const auto InitialMotor=Physics?MotorProfile(Owner):TSharedPtr<FJsonObject>{};
        TStrongObjectPtr<USkeletalMesh> Carrier(DuplicateObject<USkeletalMesh>(Mesh,GetTransientPackage()));Carrier->ClearFlags(RF_Public|RF_Standalone);Carrier->SetFlags(RF_Transient);
        TStrongObjectPtr<USkeletalMeshComponent> C(Physics?Owner->GetMesh():NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));C->VisibilityBasedAnimTickOption=EVisibilityBasedAnimTickOption::AlwaysTickPoseAndRefreshBones;C->bUseRefPoseOnInitAnim=true;C->SetDisablePostProcessBlueprint(true);C->SetCollisionEnabled(ECollisionEnabled::NoCollision);C->SetSkeletalMesh(Carrier.Get());C->SetAnimInstanceClass(MC);
        if(!Physics){C->SetupAttachment(Owner->GetRootComponent());Owner->AddInstanceComponent(C.Get());C->RegisterComponent();}
        else{C->SetRelativeRotation(FQuat::Identity);C->SetRelativeLocation(FVector(0,0,-Owner->GetCapsuleComponent()->GetScaledCapsuleHalfHeight()));}
        auto* Main=C->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(LC);auto LayerFor=[&](FName Function)->UAnimInstance*{for(auto* P:MI->GetLinkedAnimLayerNodeProperties()){auto* N=P->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);if(N->Layer==Function)return N->GetTargetInstance<UAnimInstance>();}return nullptr;};
        auto* Layer=Multi?LayerFor(TEXT("FullBody_CycleState")):Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));
        TArray<UAnimInstance*> LayerInstances=static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances();
        if(!Layer||LayerInstances.IsEmpty()||(!Multi&&LayerInstances.Num()!=1))return Fail(__LINE__);
        for(auto* A:LayerInstances)if(!Bind(A,T->GetObjectField(TEXT("bindings")),Assets))return Fail(__LINE__);
        auto& MP=FInstanceAccess::Proxy(Main);auto* LP=&FInstanceAccess::Proxy(Layer);Carrier->SetSkeleton(Skeleton);FProxyAccess::Objects(MP,Main);FProxyAccess::Objects(*LP,Layer);if(MP.GetSkeleton()!=Skeleton||LP->GetSkeleton()!=Skeleton)return Fail(__LINE__);FProxyAccess::Bones(MP,Skeleton);FProxyAccess::Bones(*LP,Skeleton);
        if(Multi)for(auto* A:LayerInstances){auto& P=FInstanceAccess::Proxy(A);FProxyAccess::Objects(P,A);if(P.GetSkeleton()!=Skeleton)return Fail(__LINE__);FProxyAccess::Bones(P,Skeleton);}
        auto* Blueprint=Cast<UAnimBlueprint>(MC->ClassGeneratedBy);
        UE_LOG(LogTemp,Display,TEXT("LYRA_WHOLE_MAIN_ROOT profile=%s root=%p expected=%p status=%d functions=%d linkedFunctions=%d"),*T->GetStringField(TEXT("profile")),MP.GetRootNode(),MP.GetMutableNodeFromIndex<FAnimNode_Root>(85),Blueprint?(int32)Blueprint->Status:-1,MI->GetAnimBlueprintFunctions().Num(),LI->GetAnimBlueprintFunctions().Num());
        for(const auto& Function:MI->GetAnimBlueprintFunctions())UE_LOG(LogTemp,Display,TEXT("LYRA_WHOLE_MAIN_FUNCTION %s property=%s"),*Function.Name.ToString(),*GetNameSafe(Function.OutputPoseNodeProperty));
        if(MP.GetRootNode()!=MP.GetMutableNodeFromIndex<FAnimNode_Root>(85))return Fail(__LINE__);
        TArray<TStrongObjectPtr<UBlendSpace>> Spaces;TArray<TStrongObjectPtr<UBlendProfile>> Masks;TMap<UBlendSpace*,UBlendSpace*> SourceSpaces;
        for(const auto& SV:Q->GetArrayField(TEXT("spaces")))
        {
            auto S=SV->AsObject();auto* Original=LoadObject<UBlendSpace>(nullptr,*S->GetStringField(TEXT("source")));if(!Original)return Fail(__LINE__);
            Spaces.Emplace(DuplicateObject<UBlendSpace>(Original,GetTransientPackage()));auto* B=Spaces.Last().Get();B->ClearFlags(RF_Public|RF_Standalone);B->SetFlags(RF_Transient);B->SetSkeleton(Skeleton);
            SourceSpaces.Add(Original,B);
            const auto& Samples=S->GetArrayField(TEXT("samples"));if(Samples.Num()!=B->GetBlendSamples().Num())return Fail(__LINE__);
            for(int32 I=0;I<Samples.Num();I++)if(!B->ReplaceSampleAnimation(I,Assets.FindRef(Samples[I]->AsString())))return Fail(__LINE__);B->ValidateSampleData();B->ResampleData();
            for(auto* A:([&](){auto R=LayerInstances;R.Insert(Main,0);return R;})()){auto* AI=IAnimClassInterface::GetFromClass(A->GetClass());for(auto* P:AI->GetAnimNodeProperties())if(P->Struct->IsChildOf(FAnimNode_BlendSpacePlayer::StaticStruct())){auto* N=P->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(A);if(N->GetBlendSpace()==Original)N->SetBlendSpace(B);}for(TFieldIterator<FObjectPropertyBase> I(A->GetClass());I;++I)if(I->GetObjectPropertyValue_InContainer(A)==Original)I->SetObjectPropertyValue_InContainer(A,B);}
        }
        auto AdaptInstance=[&](UAnimInstance* A)->bool
        {
            auto* AI=IAnimClassInterface::GetFromClass(A->GetClass());
            for(auto* P:AI->GetAnimNodeProperties())if(P->Struct->IsChildOf(FAnimNode_BlendSpacePlayer::StaticStruct()))
            {auto* N=P->ContainerPtrToValuePtr<FAnimNode_BlendSpacePlayer>(A);if(auto* B=SourceSpaces.FindRef(N->GetBlendSpace()))N->SetBlendSpace(B);}
            for(TFieldIterator<FProperty> I(A->GetClass());I;++I)if(!Remap(*I,I->ContainerPtrToValuePtr<void>(A),SourceAssets,SourceSpaces))return false;
            for(auto* P:AI->GetAnimNodeProperties())
            {
                if(P->Struct->IsChildOf(FAnimNode_SequencePlayer::StaticStruct()))
                {auto* N=P->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(A);auto* S=N->GetSequence();if(S&&S->GetSkeleton()!=Skeleton){auto* Target=SourceAssets.FindRef(S->GetPathName());if(!Target||!N->SetSequence(Target))return false;}}
                if(P->Struct->IsChildOf(FAnimNode_SequenceEvaluatorBase::StaticStruct()))
                {auto* N=P->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluatorBase>(A);auto* S=N->GetSequence();if(S&&S->GetSkeleton()!=Skeleton){auto* Target=SourceAssets.FindRef(S->GetPathName());if(!Target||!N->SetSequence(Target))return false;}}
                if(P->Struct==FAnimNode_LayeredBoneBlend::StaticStruct())
                {auto* N=P->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(A);for(auto& Original:N->BlendMasks)if(Original){Masks.Emplace(NewObject<UBlendProfile>(GetTransientPackage()));auto* M=Masks.Last().Get();M->OwningSkeleton=Skeleton;M->Mode=EBlendProfileMode::BlendMask;for(int32 B=0;B<81;B++){float Weight=0;for(const auto& E:Original->ProfileEntries)if(E.BoneReference.BoneName==Skeleton->GetReferenceSkeleton().GetBoneName(B)){Weight=E.BlendScale;break;}M->SetBoneBlendScale(B,Weight,false,true);}Original=M;}N->InvalidatePerBoneBlendWeights();}
                if(P->Struct==FAnimNode_OrientationWarping::StaticStruct())
                {auto* N=P->ContainerPtrToValuePtr<FAnimNode_OrientationWarping>(A);TArray<FBoneReference> Bones;for(auto B:N->SpineBones){if(B.BoneName==TEXT("spine_04")||B.BoneName==TEXT("spine_05"))B.BoneName=TEXT("spine_03");if(!Bones.ContainsByPredicate([&](const auto& E){return E.BoneName==B.BoneName;}))Bones.Add(B);}N->SpineBones=Bones;}
            }
            return true;
        };
        if(!AdaptInstance(Main))return Fail(__LINE__);
        for(auto* A:LayerInstances)if(!AdaptInstance(A))return Fail(__LINE__);
        // Wrap original result links; taps forward the complete original graph,
        // never provide controlled poses, weights, transitions or clocks.
        const int32 LinkedFunctions=LI->GetAnimBlueprintFunctions().Num();
        TArray<TStrongObjectPtr<UAnimInstance>> InstanceStorage;for(auto* A:LayerInstances)InstanceStorage.Emplace(A);
        TArray<FTap> Taps;Taps.SetNum(LinkedFunctions+10);TArray<FAnimNode_Root*> Roots;TArray<FPoseLink> Links;
        struct FRestore{TArray<FAnimNode_Root*>& R;TArray<FPoseLink>& L;~FRestore(){for(int32 I=0;I<R.Num();I++)R[I]->Result=L[I];}} Restore{Roots,Links};
        auto WrapRoots=[&]()->bool
        {
            if(LI->GetAnimBlueprintFunctions().Num()!=LinkedFunctions)return false;
            for(int32 I=0;I<LinkedFunctions;I++)
            {const auto& F=LI->GetAnimBlueprintFunctions()[I];if(!F.OutputPoseNodeProperty||(!Taps[I].Name.IsEmpty()&&Taps[I].Name!=F.Name.ToString()))return false;auto* Target=Multi&&F.Name!=TEXT("AnimGraph")?LayerFor(F.Name):Layer;if(!Target)return false;auto* R=F.OutputPoseNodeProperty->ContainerPtrToValuePtr<FAnimNode_Root>(Target);Roots.Add(R);Links.Add(R->Result);Taps[I].Child=R->Result;Taps[I].Name=F.Name.ToString();Taps[I].Ref=&Skeleton->GetReferenceSkeleton();R->Result.SetLinkNode(&Taps[I]);}
            return true;
        };
        if(!WrapRoots())return Fail(__LINE__);
        auto* Inertia=MP.GetMutableNodeFromIndex<FAnimNode_Inertialization>(75);if(!Inertia)return Fail(__LINE__);
        TArray<FPoseLink*> DiagnosticLinks;TArray<FPoseLink> OriginalDiagnosticLinks;
        struct FRestoreLinks{TArray<FPoseLink*>& Links;TArray<FPoseLink>& Original;~FRestoreLinks(){for(int32 I=0;I<Links.Num();I++)*Links[I]=Original[I];}} RestoreDiagnostic{DiagnosticLinks,OriginalDiagnosticLinks};
        auto TapLink=[&](void* Node,UScriptStruct* Type,const TCHAR* Pin,const TCHAR* Name,FAnimNode_BlendSpacePlayer* Sample=nullptr)
        {
            auto* Property=FindFProperty<FStructProperty>(Type,Pin);if(!Node||!Property||Property->Struct!=FPoseLink::StaticStruct())return false;
            auto* Link=Property->ContainerPtrToValuePtr<FPoseLink>(Node);auto& Tap=Taps[LinkedFunctions+DiagnosticLinks.Num()];Tap.Child=*Link;Tap.Name=Name;Tap.Ref=&Skeleton->GetReferenceSkeleton();Tap.SampleNode=Sample;DiagnosticLinks.Add(Link);OriginalDiagnosticLinks.Add(*Link);Link->SetLinkNode(&Tap);return true;
        };
        auto* AimProxy=Multi?&FInstanceAccess::Proxy(LayerFor(TEXT("FullBody_Aiming"))):LP;
        auto* Blend=AimProxy->GetMutableNodeFromIndex<FAnimNode_TwoWayBlend>(77);auto* Relaxed=AimProxy->GetMutableNodeFromIndex<FAnimNode_RotationOffsetBlendSpace>(79);auto* Ready=AimProxy->GetMutableNodeFromIndex<FAnimNode_RotationOffsetBlendSpace>(74);
        if(!TapLink(Inertia,FAnimNode_Inertialization::StaticStruct(),TEXT("Source"),TEXT("Main_InertiaInput"))||
           !TapLink(MP.GetMutableNodeFromIndex<FAnimNode_RotateRootBone>(72),FAnimNode_RotateRootBone::StaticStruct(),TEXT("BasePose"),TEXT("Main_InertiaOutput"))||
           !TapLink(MP.GetMutableNodeFromIndex<FAnimNode_LayeredBoneBlend>(0),FAnimNode_LayeredBoneBlend::StaticStruct(),TEXT("BasePose"),TEXT("Main_Dynamic"))||
           !TapLink(MP.GetMutableNodeFromIndex<FAnimNode_ApplyAdditive>(3),FAnimNode_ApplyAdditive::StaticStruct(),TEXT("Base"),TEXT("Main_Lower"))||
           !TapLink(Blend,FAnimNode_TwoWayBlend::StaticStruct(),TEXT("A"),TEXT("Aim_Relaxed"),Relaxed)||
           !TapLink(Blend,FAnimNode_TwoWayBlend::StaticStruct(),TEXT("B"),TEXT("Aim_Ready"),Ready)||
           !TapLink(Relaxed,FAnimNode_RotationOffsetBlendSpace::StaticStruct(),TEXT("BasePose"),TEXT("Aim_RelaxedInput"))||
           !TapLink(Ready,FAnimNode_RotationOffsetBlendSpace::StaticStruct(),TEXT("BasePose"),TEXT("Aim_ReadyInput"))||
           !TapLink(MP.GetMutableNodeFromIndex<FAnimNode_SaveCachedPose>(78),FAnimNode_SaveCachedPose::StaticStruct(),TEXT("Pose"),TEXT("Main_Upper")))return Fail(__LINE__);
        auto* Upper=MP.GetMutableNodeFromIndex<FAnimNode_LayeredBoneBlend>(0);if(!Upper||Upper->BlendPoses.Num()!=1)return Fail(__LINE__);
        auto& UpperTap=Taps[LinkedFunctions+DiagnosticLinks.Num()];UpperTap.Child=Upper->BlendPoses[0];UpperTap.Name=TEXT("Main_UpperSource");UpperTap.Ref=&Skeleton->GetReferenceSkeleton();DiagnosticLinks.Add(&Upper->BlendPoses[0]);OriginalDiagnosticLinks.Add(Upper->BlendPoses[0]);Upper->BlendPoses[0].SetLinkNode(&UpperTap);
        auto* Rig=MP.GetMutableNodeFromIndex<FAnimNode_ControlRig>(73);if(!Rig)return Fail(__LINE__);FindFProperty<FBoolProperty>(FAnimNode_ControlRig::StaticStruct(),TEXT("bSetRefPoseFromSkeleton"))->SetPropertyValue_InContainer(Rig,T->GetBoolField(TEXT("alsReference")));
        FProxyAccess::Init(MP);
        TArray<TSharedPtr<FJsonValue>> RigEvents;FDelegateHandle RigInitializedHandle,RigExecutedHandle;auto* EventRig=Rig->GetControlRig();
        auto RecordRigEvent=[&](URigVMHost* Host,const FName& Event,bool Initialized)
        {auto E=MakeShared<FJsonObject>();E->SetStringField(TEXT("event"),Event.ToString());E->SetBoolField(TEXT("initialized"),Initialized);E->SetNumberField(TEXT("delta"),Host->GetDeltaTime());RigEvents.Add(MakeShared<FJsonValueObject>(E));};
        if(Physics&&EventRig){RigInitializedHandle=EventRig->OnInitialized_AnyThread().AddLambda([&](URigVMHost* Host,const FName& Event){RecordRigEvent(Host,Event,true);});RigExecutedHandle=EventRig->OnExecuted_AnyThread().AddLambda([&](URigVMHost* Host,const FName& Event){RecordRigEvent(Host,Event,false);});}
        struct FRigEventRestore{UControlRig* Rig;FDelegateHandle Init,Execute;~FRigEventRestore(){if(Rig){Rig->OnInitialized_AnyThread().Remove(Init);Rig->OnExecuted_AnyThread().Remove(Execute);}}} RigEventRestore{EventRig,RigInitializedHandle,RigExecutedHandle};
        for(auto* Property:MI->GetLinkedAnimLayerNodeProperties()){auto* Target=Property->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main)->GetTargetInstance<UAnimInstance>();if(!Target||(!Multi&&Target!=Layer)||!LayerInstances.Contains(Target))return Fail(__LINE__);}
        auto InstanceRows=[&](){TArray<TSharedPtr<FJsonValue>> R;for(int32 I=0;I<LayerInstances.Num();++I){auto* A=LayerInstances[I];auto E=MakeShared<FJsonObject>();E->SetNumberField(TEXT("owner"),I);E->SetStringField(TEXT("class"),A->GetClass()->GetPathName());E->SetObjectField(TEXT("fields"),Fields(A));TArray<TSharedPtr<FJsonValue>> Players;const auto& Props=IAnimClassInterface::GetFromClass(A->GetClass())->GetAnimNodeProperties();for(int32 N=0;N<Props.Num();++N){double Time;UAnimSequenceBase* Asset=nullptr;if(Props[N]->Struct->IsChildOf(FAnimNode_SequencePlayer::StaticStruct())){auto* P=Props[N]->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(A);Time=P->GetAccumulatedTime();Asset=P->GetSequence();}else if(Props[N]->Struct->IsChildOf(FAnimNode_SequenceEvaluatorBase::StaticStruct())){auto* P=Props[N]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluatorBase>(A);Time=P->GetCurrentAssetTime();Asset=P->GetSequence();}else continue;auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("node"),Props.Num()-1-N);V->SetNumberField(TEXT("time"),Time);V->SetStringField(TEXT("asset"),GetPathNameSafe(Asset));Players.Add(MakeShared<FJsonValueObject>(V));}E->SetArrayField(TEXT("players"),Players);R.Add(MakeShared<FJsonValueObject>(E));}return R;};
        auto CallRows=[&](){TArray<TSharedPtr<FJsonValue>> R;for(auto* P:MI->GetLinkedAnimLayerNodeProperties()){auto* N=P->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main);auto E=MakeShared<FJsonObject>();E->SetStringField(TEXT("node"),P->GetName());E->SetStringField(TEXT("function"),N->Layer.ToString());E->SetNumberField(TEXT("owner"),LayerInstances.IndexOfByKey(N->GetTargetInstance<UAnimInstance>()));const auto* Fn=LI->GetAnimBlueprintFunctions().FindByPredicate([&](const auto& F){return F.Name==N->Layer;});if(Fn)E->SetStringField(TEXT("group"),Fn->Group.ToString());R.Add(MakeShared<FJsonValueObject>(E));}return R;};
        FString CurrentProfile=T->GetStringField(TEXT("profile"));int32 OwnerIndex=0;
        auto Binding=[&]()->TSharedPtr<FJsonObject>
        {
            const auto& Nodes=MI->GetLinkedAnimLayerNodeProperties();if(Nodes.Num()!=14||static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances().Num()!=1)return nullptr;
            for(auto* P:Nodes)if(P->ContainerPtrToValuePtr<FAnimNode_LinkedAnimLayer>(Main)->GetTargetInstance<UAnimInstance>()!=Layer)return nullptr;
            auto B=MakeShared<FJsonObject>();B->SetStringField(TEXT("profile"),CurrentProfile);B->SetStringField(TEXT("class"),Layer->GetClass()->GetPathName());B->SetNumberField(TEXT("owner"),OwnerIndex);B->SetNumberField(TEXT("nodes"),Nodes.Num());B->SetNumberField(TEXT("activeInstances"),1);return B;
        };
        auto JointPhases=[&](){auto J=MakeShared<FJsonObject>();J->SetObjectField(TEXT("main"),ProxyPhases(MP));TArray<TSharedPtr<FJsonValue>> R;for(int32 I=0;I<LayerInstances.Num();++I){auto E=MakeShared<FJsonObject>();E->SetNumberField(TEXT("owner"),I);E->SetObjectField(TEXT("phases"),ProxyPhases(FInstanceAccess::Proxy(LayerInstances[I])));R.Add(MakeShared<FJsonValueObject>(E));}J->SetArrayField(TEXT("providers"),R);return J;};
        auto InitialProxyPhases=JointPhases();
        TArray<TSharedPtr<FJsonValue>> Rows;uint64 FrameCounter=GFrameCounter;struct FCounterRestore{uint64 Value;~FCounterRestore(){GFrameCounter=Value;}} CounterRestore{FrameCounter};
        for(const auto& FV:T->GetArrayField(TEXT("frames")))
        {
            FMemMark FrameMark(FMemStack::Get());auto F=FV->AsObject();float D=F->GetNumberField(TEXT("delta"));++GFrameCounter;
            RigEvents.Reset();
            auto O=F->GetObjectField(TEXT("observation"));auto* Move=Owner->GetCharacterMovement();
            if(Physics)
            {
                auto Input=F->GetObjectField(TEXT("control"));const FRotator Control(Input->GetNumberField(TEXT("pitch")),Input->GetNumberField(TEXT("yaw")),0);Controller->SetControlRotation(Control);Owner->FaceRotation(Control,D);
                if(Input->GetBoolField(TEXT("crouching")))Owner->Crouch();else Owner->UnCrouch();
                if(Input->GetBoolField(TEXT("jump")))Owner->Jump();else Owner->StopJumping();
                Owner->AddMovementInput(Vector(Input,TEXT("direction")),1,true);
                Move->TickComponent(D,LEVELTICK_All,nullptr);
                // Canonical target ALS model stays at the actual capsule feet.
                C->SetRelativeLocation(FVector(0,0,-Owner->GetCapsuleComponent()->GetScaledCapsuleHalfHeight()));
            }
            else
            {
            auto Rotation=Vector(O,TEXT("rotation"));Owner->SetActorLocationAndRotation(Vector(O,TEXT("location")),FRotator(Rotation.X,Rotation.Y,Rotation.Z),false,nullptr,ETeleportType::TeleportPhysics);
            Move->Velocity=Vector(O,TEXT("velocity"));auto* Acc=FindFProperty<FStructProperty>(Move->GetClass(),TEXT("Acceleration"));if(!Acc)return Fail(__LINE__);*Acc->ContainerPtrToValuePtr<FVector>(Move)=Vector(O,TEXT("acceleration"));Move->MovementMode=(EMovementMode)(int32)O->GetNumberField(TEXT("movementMode"));Move->GravityScale=O->GetNumberField(TEXT("gravityScale"));FindFProperty<FBoolProperty>(Owner->GetClass(),TEXT("bIsCrouched"))->SetPropertyValue_InContainer(Owner,O->GetBoolField(TEXT("crouching")));
            auto* LastVelocity=FindFProperty<FStructProperty>(Move->GetClass(),TEXT("LastUpdateVelocity"));if(!LastVelocity)return Fail(__LINE__);*LastVelocity->ContainerPtrToValuePtr<FVector>(Move)=Move->Velocity;
            Controller->SetControlRotation(FRotator(O->GetNumberField(TEXT("aimPitch")),0,0));Move->CurrentFloor.bBlockingHit=Move->IsMovingOnGround();Move->CurrentFloor.HitResult.ImpactPoint=FVector(Vector(O,TEXT("location")).X,Vector(O,TEXT("location")).Y,0);Move->CurrentFloor.HitResult.ImpactNormal=FVector::UpVector;
            }
            for(const auto& P:F->GetObjectField(TEXT("mainProperties"))->Values)if(!Set(Main,FString(P.Key),P.Value))return Fail(__LINE__);
            for(auto* A:LayerInstances)for(const auto& P:F->GetObjectField(TEXT("layerProperties"))->Values)if(!Set(A,FString(P.Key),P.Value))return Fail(__LINE__);
            auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("proxyBefore"),JointPhases());Row->SetObjectField(TEXT("physicalInput"),PhysicalInput(Owner,C.Get()));Row->SetObjectField(TEXT("mainBefore"),Fields(Main));Row->SetObjectField(TEXT("layerBefore"),Fields(Layer));
            if(T->GetStringField(TEXT("case"))==TEXT("rebind")){auto B=Binding();if(!B)return Fail(__LINE__);Row->SetObjectField(TEXT("binding"),B);}
            if(Multi){Row->SetArrayField(TEXT("instancesBefore"),InstanceRows());Row->SetArrayField(TEXT("calls"),CallRows());}
            for(auto& Tap:Taps){Tap.Updates.Reset();Tap.Outputs.Reset();}
            Main->PreUpdateLinkedInstances(D);for(auto* A:LayerInstances)A->UpdateAnimation(D,false,UAnimInstance::EUpdateAnimationFlag::ForceParallelUpdate);Main->UpdateAnimation(D,false,UAnimInstance::EUpdateAnimationFlag::ForceParallelUpdate);Main->ParallelUpdateAnimation();Main->PostUpdateAnimation();
            Row->SetObjectField(TEXT("proxyUpdated"),JointPhases());Row->SetObjectField(TEXT("mainUpdated"),Fields(Main));Row->SetObjectField(TEXT("layerUpdated"),Fields(Layer));
            if(Multi)Row->SetArrayField(TEXT("instancesUpdated"),InstanceRows());
            if(MontagePaths)
            {
                TArray<TSharedPtr<FJsonValue>> Frozen;
                for(const auto& E:FProxyAccess::Frozen(MP)){const int32 Asset=Montages.IndexOfByKey(E.Montage.Get());if(Asset<0)return Fail(__LINE__);auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("asset"),Asset);R->SetNumberField(TEXT("position"),E.MontagePosition);R->SetNumberField(TEXT("weight"),E.BlendInfo.GetBlendedValue());R->SetNumberField(TEXT("previous"),E.DeltaTimeRecord.GetPrevious());R->SetNumberField(TEXT("delta"),E.DeltaTimeRecord.Delta);R->SetBoolField(TEXT("profile"),E.ActiveBlendProfile!=nullptr);Frozen.Add(MakeShared<FJsonValueObject>(R));}
                Row->SetArrayField(TEXT("frozen"),Frozen);
            }
            TArray<TSharedPtr<FJsonValue>> Evaluators;
            const auto& LayerProperties=LI->GetAnimNodeProperties();for(int32 I=0;I<LayerProperties.Num();I++)if(LayerProperties[I]->Struct->IsChildOf(FAnimNode_SequenceEvaluatorBase::StaticStruct()))
            {
                auto* N=LayerProperties[I]->ContainerPtrToValuePtr<FAnimNode_SequenceEvaluatorBase>(Layer);auto E=MakeShared<FJsonObject>();E->SetNumberField(TEXT("node"),LayerProperties.Num()-1-I);E->SetNumberField(TEXT("time"),N->GetCurrentAssetTime());E->SetNumberField(TEXT("explicit"),N->GetExplicitTime());E->SetStringField(TEXT("sequence"),GetPathNameSafe(N->GetSequence()));Evaluators.Add(MakeShared<FJsonValueObject>(E));
            }
            Row->SetArrayField(TEXT("evaluators"),Evaluators);
            TArray<TSharedPtr<FJsonValue>> SequencePlayers;
            for(int32 I=0;I<LayerProperties.Num();I++)if(LayerProperties[I]->Struct->IsChildOf(FAnimNode_SequencePlayer::StaticStruct()))
            {
                auto* N=LayerProperties[I]->ContainerPtrToValuePtr<FAnimNode_SequencePlayer>(Layer);auto E=MakeShared<FJsonObject>();E->SetNumberField(TEXT("node"),LayerProperties.Num()-1-I);E->SetNumberField(TEXT("time"),N->GetAccumulatedTime());E->SetStringField(TEXT("sequence"),GetPathNameSafe(N->GetSequence()));SequencePlayers.Add(MakeShared<FJsonValueObject>(E));
            }
            Row->SetArrayField(TEXT("sequencePlayers"),SequencePlayers);
            TArray<TSharedPtr<FJsonValue>> CurrentWeights;for(const auto& BoneWeight:FLayerBlendAccess::Weights(*Upper))CurrentWeights.Add(MakeShared<FJsonValueNumber>(BoneWeight.BlendWeight));Row->SetArrayField(TEXT("upperWeights"),CurrentWeights);
            const auto& BoneWeights=FLayerBlendAccess::Weights(*Upper);if(BoneWeights.Num()!=81||Upper->BlendMasks.Num()!=1)return Fail(__LINE__);
            for(int32 B=0;B<81;B++){float Expected=0;for(const auto& Entry:Upper->BlendMasks[0]->ProfileEntries)if(Entry.BoneReference.BoneName==Skeleton->GetReferenceSkeleton().GetBoneName(B)){Expected=Entry.BlendScale;break;}if(BoneWeights[B].BlendWeight!=Expected)return Fail(__LINE__);}
            if(F->GetBoolField(TEXT("evaluate")))
            {
                if(Physics)Row->SetObjectField(TEXT("rigBeforeEvaluation"),RigParameters(MP.GetMutableNodeFromIndex<FAnimNode_ControlRig>(73)));
                Main->PreEvaluateAnimation();FCompactPose Bones;FBlendedHeapCurve Curves;UE::Anim::FHeapAttributeContainer Attributes;FParallelEvaluationData Data{Curves,Bones,Attributes};Main->ParallelEvaluateAnimation(false,Carrier.Get(),Data);FPoseContext Out(&MP);Out.Pose.CopyBonesFrom(Bones);Out.Curve.CopyFrom(Curves);Out.CustomAttributes.CopyFrom(Attributes);auto Pose=PoseData(Out,Skeleton->GetReferenceSkeleton());if(!Pose)return Fail(__LINE__);Row->SetObjectField(TEXT("output"),Pose);FProxyAccess::Publish(MP,Out);for(auto* A:LayerInstances)A->CopyCurveValues(*Main);Main->PostEvaluateAnimation();
            }
            if(Physics){auto Collision=RigCollisionSnapshot(Rig);if(!Collision)return Fail(__LINE__);Row->SetObjectField(TEXT("rigCollision"),Collision);Row->SetObjectField(TEXT("rigAfterEvaluation"),RigParameters(Rig));Row->SetArrayField(TEXT("rigEvents"),RigEvents);}
            TArray<TSharedPtr<FJsonValue>> Updates,Outputs;for(auto& Tap:Taps){Updates.Append(Tap.Updates);Outputs.Append(Tap.Outputs);}Row->SetArrayField(TEXT("updates"),Updates);Row->SetArrayField(TEXT("layerOutputs"),Outputs);
            if(Updates.IsEmpty()||(F->GetBoolField(TEXT("evaluate"))&&Outputs.IsEmpty()))return Fail(__LINE__);
            Row->SetObjectField(TEXT("proxyAfter"),JointPhases());Row->SetObjectField(TEXT("mainAfter"),Fields(Main));Row->SetObjectField(TEXT("layerAfter"),Fields(Layer));
            if(Multi){Row->SetArrayField(TEXT("instancesAfter"),InstanceRows());bool Relink=false;if(F->TryGetBoolField(TEXT("relink"),Relink)&&Relink){const auto Before=static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances();Main->LinkAnimClassLayers(LC);const auto After=static_cast<const USkeletalMeshComponent*>(C.Get())->GetLinkedAnimInstances();if(Before!=After)return Fail(__LINE__);Row->SetArrayField(TEXT("instancesRelinked"),InstanceRows());}}
            Main->DispatchQueuedAnimEvents();Rows.Add(MakeShared<FJsonValueObject>(Row));
            const TArray<TSharedPtr<FJsonValue>>* Commands=nullptr;
            if(F->TryGetArrayField(TEXT("commands"),Commands))for(const auto& CV:*Commands)
            {
                auto Command=CV->AsObject();const int32 Asset=Command->GetNumberField(TEXT("asset"));if(!Montages.IsValidIndex(Asset))return Fail(__LINE__);
                if(Command->GetBoolField(TEXT("stop")))Main->Montage_Stop(Command->GetNumberField(TEXT("blend")),Montages[Asset]);
                else if(Main->Montage_Play(Montages[Asset],Command->GetNumberField(TEXT("rate")),EMontagePlayReturnType::MontageLength,Command->GetNumberField(TEXT("start")),Command->GetBoolField(TEXT("stopGroup")))<=0)return Fail(__LINE__);
            }
            const TSharedPtr<FJsonObject>* Change=nullptr;
            if(F->TryGetObjectField(TEXT("rebind"),Change))
            {
                auto* NextClass=LoadObject<UClass>(nullptr,*(*Change)->GetStringField(TEXT("class")));if(!NextClass)return Fail(__LINE__);
                auto* Previous=Layer;const bool Changed=Previous->GetClass()!=NextClass;
                auto Rebind=MakeShared<FJsonObject>();Rebind->SetBoolField(TEXT("changed"),Changed);Rebind->SetObjectField(TEXT("before"),Binding());Rebind->SetObjectField(TEXT("mainBefore"),Fields(Main));
                Rebind->SetArrayField(TEXT("frozenBefore"),Row->GetArrayField(TEXT("frozen")));
                if(Changed)
                {
                    for(int32 I=0;I<Roots.Num();I++)Roots[I]->Result=Links[I];
                    for(int32 I=4;I<=7;I++)*DiagnosticLinks[I]=OriginalDiagnosticLinks[I];
                }
                Main->LinkAnimClassLayers(NextClass);Layer=Main->GetLinkedAnimLayerInstanceByGroup(TEXT("ItemAnimLayers"));
                if(!Layer||Changed!=(Previous!=Layer)||Changed!=(*Change)->GetBoolField(TEXT("changed")))return Fail(__LINE__);
                if(Changed)
                {
                    InstanceStorage.Emplace(Layer);LayerInstances.Reset();LayerInstances.Add(Layer);LC=NextClass;LI=IAnimClassInterface::GetFromClass(LC);LP=&FInstanceAccess::Proxy(Layer);
                    if(!LI||!Bind(Layer,(*Change)->GetObjectField(TEXT("bindings")),Assets)||!AdaptInstance(Layer))return Fail(__LINE__);
                    FProxyAccess::Objects(*LP,Layer);if(LP->GetSkeleton()!=Skeleton)return Fail(__LINE__);FProxyAccess::Bones(*LP,Skeleton);
                    if(!WrapRoots())return Fail(__LINE__);
                    auto ReplaceTap=[&](int32 I,void* Node,UScriptStruct* Type,const TCHAR* Pin,FAnimNode_BlendSpacePlayer* Sample=nullptr)->bool
                    {
                        auto* P=FindFProperty<FStructProperty>(Type,Pin);if(!Node||!P||P->Struct!=FPoseLink::StaticStruct())return false;
                        auto* Link=P->ContainerPtrToValuePtr<FPoseLink>(Node);auto& Tap=Taps[LinkedFunctions+I];Tap.Child=*Link;Tap.SampleNode=Sample;DiagnosticLinks[I]=Link;OriginalDiagnosticLinks[I]=*Link;Link->SetLinkNode(&Tap);return true;
                    };
                    Blend=LP->GetMutableNodeFromIndex<FAnimNode_TwoWayBlend>(77);Relaxed=LP->GetMutableNodeFromIndex<FAnimNode_RotationOffsetBlendSpace>(79);Ready=LP->GetMutableNodeFromIndex<FAnimNode_RotationOffsetBlendSpace>(74);
                    if(!ReplaceTap(4,Blend,FAnimNode_TwoWayBlend::StaticStruct(),TEXT("A"),Relaxed)||!ReplaceTap(5,Blend,FAnimNode_TwoWayBlend::StaticStruct(),TEXT("B"),Ready)||
                       !ReplaceTap(6,Relaxed,FAnimNode_RotationOffsetBlendSpace::StaticStruct(),TEXT("BasePose"))||!ReplaceTap(7,Ready,FAnimNode_RotationOffsetBlendSpace::StaticStruct(),TEXT("BasePose")))return Fail(__LINE__);
                    for(const auto& Function:LI->GetAnimBlueprintFunctions())FProxyAccess::Recache(*LP,Function.OutputPoseNodeProperty->ContainerPtrToValuePtr<FAnimNode_Root>(Layer));
                    ++OwnerIndex;
                }
                CurrentProfile=(*Change)->GetStringField(TEXT("profile"));auto After=Binding();if(!After)return Fail(__LINE__);
                Rebind->SetObjectField(TEXT("after"),After);Rebind->SetObjectField(TEXT("mainAfter"),Fields(Main));Row->SetObjectField(TEXT("rebind"),Rebind);
                TArray<TSharedPtr<FJsonValue>> FrozenAfter;
                for(const auto& E:FProxyAccess::Frozen(MP)){const int32 Asset=Montages.IndexOfByKey(E.Montage.Get());if(Asset<0)return Fail(__LINE__);auto V=MakeShared<FJsonObject>();V->SetNumberField(TEXT("asset"),Asset);V->SetNumberField(TEXT("position"),E.MontagePosition);V->SetNumberField(TEXT("weight"),E.BlendInfo.GetBlendedValue());V->SetNumberField(TEXT("previous"),E.DeltaTimeRecord.GetPrevious());V->SetNumberField(TEXT("delta"),E.DeltaTimeRecord.Delta);V->SetBoolField(TEXT("profile"),E.ActiveBlendProfile!=nullptr);FrozenAfter.Add(MakeShared<FJsonValueObject>(V));}
                Rebind->SetArrayField(TEXT("frozenAfter"),FrozenAfter);
            }
        }
        auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("proxyInitial"),InitialProxyPhases);R->SetStringField(TEXT("profile"),T->GetStringField(TEXT("profile")));R->SetNumberField(TEXT("hz"),T->GetNumberField(TEXT("hz")));R->SetArrayField(TEXT("frames"),Rows);if(Multi){R->SetArrayField(TEXT("calls"),CallRows());R->SetNumberField(TEXT("instanceCount"),LayerInstances.Num());R->SetBoolField(TEXT("multipleOriginalGraphInstances"),true);}if(Physics){R->SetObjectField(TEXT("motorProfile"),InitialMotor);R->SetArrayField(TEXT("velocityKernel"),Kernel);R->SetArrayField(TEXT("fallingKernel"),FallKernel);R->SetArrayField(TEXT("floorKernel"),FloorRows);R->SetArrayField(TEXT("movementKernel"),MoveRows);R->SetArrayField(TEXT("airPhysicalKernel"),AirRows);R->SetObjectField(TEXT("penetrationProfile"),PenetrationProfile(Owner));R->SetBoolField(TEXT("actualCharacterMovement"),true);}Traces.Add(MakeShared<FJsonValueObject>(R));C->UnregisterComponent();
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("traces"),Traces);FString Json;FJsonSerializer::Serialize(R,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json));return Json;
}
