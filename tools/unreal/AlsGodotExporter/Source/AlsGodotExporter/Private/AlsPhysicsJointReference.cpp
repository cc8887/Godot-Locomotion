#include "AlsPhysicsAssetExport.h"
#include "AlsAnimationGraphLibrary.h"
#include "Chaos/PBDJointConstraintData.h"
#include "Chaos/PBDJointConstraintUtilities.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Engine.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/PhysicsConstraintTemplate.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsJointReference
{
TArray<TSharedPtr<FJsonValue>> V(const Chaos::FVec3& P)
{ return {MakeShared<FJsonValueNumber>(P.X),MakeShared<FJsonValueNumber>(P.Y),MakeShared<FJsonValueNumber>(P.Z)}; }
TArray<TSharedPtr<FJsonValue>> Q(const Chaos::FRotation3& R)
{ return {MakeShared<FJsonValueNumber>(R.X),MakeShared<FJsonValueNumber>(R.Y),MakeShared<FJsonValueNumber>(R.Z),MakeShared<FJsonValueNumber>(R.W)}; }
TSharedRef<FJsonObject> Settings(const Chaos::FPBDJointSettings& S)
{
    auto J=MakeShared<FJsonObject>();
#define BOOL_FIELD(Field) J->SetBoolField(TEXT(#Field), S.Field)
#define NUMBER_FIELD(Field) J->SetNumberField(TEXT(#Field), S.Field)
#define VECTOR_FIELD(Field) J->SetArrayField(TEXT(#Field), V(S.Field))
    BOOL_FIELD(bUseLinearSolver); BOOL_FIELD(bProjectionEnabled); BOOL_FIELD(bMassConditioningEnabled);
    BOOL_FIELD(bShockPropagationEnabled); BOOL_FIELD(bCollisionEnabled);
    BOOL_FIELD(bSoftLinearLimitsEnabled); BOOL_FIELD(bSoftTwistLimitsEnabled); BOOL_FIELD(bSoftSwingLimitsEnabled);
    BOOL_FIELD(bAngularTwistPositionDriveEnabled); BOOL_FIELD(bAngularTwistVelocityDriveEnabled);
    BOOL_FIELD(bAngularSwingPositionDriveEnabled); BOOL_FIELD(bAngularSwingVelocityDriveEnabled);
    BOOL_FIELD(bAngularSLerpPositionDriveEnabled); BOOL_FIELD(bAngularSLerpVelocityDriveEnabled);
    NUMBER_FIELD(Stiffness); NUMBER_FIELD(LinearLimit); NUMBER_FIELD(LinearProjection); NUMBER_FIELD(AngularProjection);
    NUMBER_FIELD(TeleportDistance); NUMBER_FIELD(TeleportAngle); NUMBER_FIELD(ParentInvMassScale); NUMBER_FIELD(ShockPropagation);
    NUMBER_FIELD(SoftLinearStiffness); NUMBER_FIELD(SoftLinearDamping);
    NUMBER_FIELD(SoftTwistStiffness); NUMBER_FIELD(SoftTwistDamping); NUMBER_FIELD(SoftSwingStiffness); NUMBER_FIELD(SoftSwingDamping);
    NUMBER_FIELD(LinearRestitution); NUMBER_FIELD(TwistRestitution); NUMBER_FIELD(SwingRestitution);
    NUMBER_FIELD(LinearContactDistance); NUMBER_FIELD(TwistContactDistance); NUMBER_FIELD(SwingContactDistance);
    VECTOR_FIELD(AngularLimits); VECTOR_FIELD(AngularDriveStiffness); VECTOR_FIELD(AngularDriveDamping);
    VECTOR_FIELD(AngularDriveMaxTorque); VECTOR_FIELD(AngularDriveVelocityTarget);
    VECTOR_FIELD(LinearDriveStiffness); VECTOR_FIELD(LinearDriveDamping); VECTOR_FIELD(LinearDriveMaxForce);
    VECTOR_FIELD(LinearDrivePositionTarget); VECTOR_FIELD(LinearDriveVelocityTarget);
    J->SetArrayField(TEXT("AngularDrivePositionTarget"), Q(S.AngularDrivePositionTarget));
    J->SetNumberField(TEXT("AngularSoftForceMode"), static_cast<int32>(S.AngularSoftForceMode));
    J->SetNumberField(TEXT("AngularDriveForceMode"), static_cast<int32>(S.AngularDriveForceMode));
    TArray<TSharedPtr<FJsonValue>> Linear, Angular, LP, LV;
    for (int32 I=0;I<3;++I)
    {
        Linear.Add(MakeShared<FJsonValueNumber>(static_cast<int32>(S.LinearMotionTypes[I])));
        Angular.Add(MakeShared<FJsonValueNumber>(static_cast<int32>(S.AngularMotionTypes[I])));
        LP.Add(MakeShared<FJsonValueBoolean>(S.bLinearPositionDriveEnabled[I]));
        LV.Add(MakeShared<FJsonValueBoolean>(S.bLinearVelocityDriveEnabled[I]));
    }
    J->SetArrayField(TEXT("LinearMotionTypes"),Linear); J->SetArrayField(TEXT("AngularMotionTypes"),Angular);
    J->SetArrayField(TEXT("bLinearPositionDriveEnabled"),LP); J->SetArrayField(TEXT("bLinearVelocityDriveEnabled"),LV);
#undef BOOL_FIELD
#undef NUMBER_FIELD
#undef VECTOR_FIELD
    return J;
}
}

bool ExportAlsPhysicsJointReference(const FString& Output,FString& Error)
{
    using namespace AlsJointReference; using namespace Chaos;
    const auto Fail=[&](const FString& Message){ Error=Message; return false; };
    if (Output.IsEmpty() || FPaths::IsRelative(Output) || IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Joint reference requires a new absolute output file."));
    const auto Initialization=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Initialization));
    if (!World.IsValid()) return Fail(TEXT("No reference physics world."));
    GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
    struct FCleanup { UWorld* World; ~FCleanup(){World->DestroyWorld(false);GEngine->DestroyWorldContext(World);} } Cleanup{World.Get()};
    auto Root=MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("coordinates"),TEXT("UE native joint frames; cm, kg, radians; axes X=Twist,Y=Swing2,Z=Swing1"));
    Root->SetStringField(TEXT("observation"),TEXT("Live FJointConstraint settings plus FPBDJointUtilities; no joint solver step or contact simulation"));
    TArray<TSharedPtr<FJsonValue>> Meshes;
    for (const TCHAR* Name : {TEXT("Mannequin"),TEXT("AnimMan")})
    {
        const FString Path=FString(TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/"))+Name+TEXT(".")+Name;
        auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Path); auto* Asset=Mesh ? Mesh->GetPhysicsAsset() : nullptr;
        if (!Asset) return Fail(TEXT("Missing native physics asset."));
        auto* Owner=World->SpawnActor<AActor>(); auto* Component=NewObject<USkeletalMeshComponent>(Owner);
        Owner->SetRootComponent(Component); Owner->AddInstanceComponent(Component);
        Component->SetSkeletalMesh(Mesh); Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
        Component->SetSimulatePhysics(true); Component->RegisterComponent(); Component->SetComponentTickEnabled(false);
        auto Row=MakeShared<FJsonObject>(); Row->SetStringField(TEXT("mesh"),Path);
        Row->SetStringField(TEXT("physicsAsset"),Asset->GetPathName()); TArray<TSharedPtr<FJsonValue>> Joints;
        for (const UPhysicsConstraintTemplate* Template : Asset->ConstraintSetup)
        {
            const auto& Original=Template->DefaultInstance;
            auto* Instance=Component->FindConstraintInstance(Original.JointName);
            if (!Instance || !Instance->GetPhysicsConstraintRef().IsValid()) return Fail(TEXT("Native joint not created: ")+Original.JointName.ToString());
            auto J=MakeShared<FJsonObject>(); J->SetStringField(TEXT("joint"),Original.JointName.ToString());
            J->SetStringField(TEXT("child"),Original.ConstraintBone1.ToString()); J->SetStringField(TEXT("parent"),Original.ConstraintBone2.ToString());
            FPBDJointSettings S;
            FPhysicsCommand::ExecuteRead(Instance->GetPhysicsConstraintRef(),[&](const FPhysicsConstraintHandle& Handle)
            { S=static_cast<const FJointConstraint*>(Handle.Constraint)->GetJointSettings(); });
            J->SetObjectField(TEXT("settings"),Settings(S));
            TArray<TSharedPtr<FJsonValue>> Samples;
            for (int32 I=0;I<24;++I)
            {
                // Deliberately sample positive/negative and simultaneous swings,
                // plus non-identity parent orientation. Inputs are recorded exactly.
                const FRotation3 Parent=FRotator(17,-24,36).Quaternion();
                const FRotation3 Relative=FRotator(I==0 ? 0 : I*17-180,I==0 ? 0 : I*29-180,I==0 ? 0 : I*11-90).Quaternion();
                FRotation3 Child=Parent*Relative; Child.EnforceShortestArcWith(Parent);
                FRotation3 Swing,Twist; FPBDJointUtilities::DecomposeSwingTwistLocal(Parent,Child,Swing,Twist);
                FReal T,S1,S2; FPBDJointUtilities::GetSwingTwistAngles(Parent,Child,T,S1,S2);
                FVec3 TwistAxis; FReal TwistAngle; FPBDJointUtilities::GetTwistAxisAngle(Parent,Child,TwistAxis,TwistAngle);
                FVec3 Locked1,Locked2; FReal LockedAngle1,LockedAngle2;
                FPBDJointUtilities::GetLockedSwingAxisAngle(Parent,Child,EJointAngularConstraintIndex::Swing1,Locked1,LockedAngle1);
                FPBDJointUtilities::GetLockedSwingAxisAngle(Parent,Child,EJointAngularConstraintIndex::Swing2,Locked2,LockedAngle2);
                auto Case=MakeShared<FJsonObject>(); Case->SetArrayField(TEXT("parent"),Q(Parent)); Case->SetArrayField(TEXT("child"),Q(Child));
                Case->SetArrayField(TEXT("swing"),Q(Swing)); Case->SetArrayField(TEXT("twist"),Q(Twist));
                Case->SetArrayField(TEXT("angles"),V(FVec3(T,S2,S1))); Case->SetArrayField(TEXT("twistAxis"),V(TwistAxis));
                Case->SetArrayField(TEXT("pyramidY"),V((Parent*Swing)*FJointConstants::Swing2Axis()));
                Case->SetArrayField(TEXT("pyramidZ"),V((Parent*Swing)*FJointConstants::Swing1Axis()));
                Case->SetArrayField(TEXT("lockedY"),V(Locked2)); Case->SetNumberField(TEXT("lockedErrorY"),LockedAngle2);
                Case->SetArrayField(TEXT("lockedZ"),V(Locked1)); Case->SetNumberField(TEXT("lockedErrorZ"),LockedAngle1);
                FVec3 LockX,LockY,LockZ; FPBDJointUtilities::GetLockedRotationAxes(Parent,Child,LockX,LockY,LockZ);
                Case->SetArrayField(TEXT("rotationLockX"),V(LockX)); Case->SetArrayField(TEXT("rotationLockY"),V(LockY)); Case->SetArrayField(TEXT("rotationLockZ"),V(LockZ));
                FRotation3 DriveTarget=Parent*S.AngularDrivePositionTarget; DriveTarget.EnforceShortestArcWith(Child);
                const FRotation3 DriveError=DriveTarget.Inverse()*Child; const FVec3 AxisError=DriveError*FJointConstants::TwistAxis();
                Case->SetArrayField(TEXT("driveError"),V(FVec3(2*DriveError.X,-AxisError.Z,AxisError.Y)));
                Samples.Add(MakeShared<FJsonValueObject>(Case));
            }
            J->SetArrayField(TEXT("samples"),Samples); Joints.Add(MakeShared<FJsonValueObject>(J));
        }
        Row->SetArrayField(TEXT("joints"),Joints); Meshes.Add(MakeShared<FJsonValueObject>(Row)); Owner->Destroy();
    }
    Root->SetArrayField(TEXT("meshes"),Meshes); FString Json;
    if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))) return Fail(TEXT("Cannot encode joint reference."));
    IFileManager::Get().MakeDirectory(*FPaths::GetPath(Output),true);
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM) || Fail(TEXT("Cannot write joint reference."));
}
bool UAlsAnimationGraphLibrary::ExportPhysicsJointReference(const FString& OutputPath)
{
    FString Error; if (ExportAlsPhysicsJointReference(OutputPath,Error)) return true;
    UE_LOG(LogTemp,Error,TEXT("Joint reference failed: %s"),*Error); return false;
}
