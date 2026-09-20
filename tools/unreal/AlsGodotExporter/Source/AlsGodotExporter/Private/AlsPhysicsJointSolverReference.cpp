#include "AlsPhysicsAssetExport.h"
#include "AlsAnimationGraphLibrary.h"
#include "Chaos/PBDJointConstraintData.h"
#include "Chaos/PBDJointConstraintUtilities.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Engine.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PBDRigidsSolver.h"
#include "Physics/Experimental/PhysScene_Chaos.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/PhysicsConstraintTemplate.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "PhysicsProxy/SingleParticlePhysicsProxy.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsJointReference { TSharedRef<FJsonObject> Settings(const Chaos::FPBDJointSettings& S); }
namespace AlsJointSolverReference
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& P)
{ return {MakeShared<FJsonValueNumber>(P.X),MakeShared<FJsonValueNumber>(P.Y),MakeShared<FJsonValueNumber>(P.Z)}; }
TSharedRef<FJsonObject> T(const FTransform& P)
{
    auto J=MakeShared<FJsonObject>(); const auto Q=P.GetRotation();
    J->SetArrayField(TEXT("position"),V(P.GetLocation()));
    J->SetArrayField(TEXT("rotation"),{MakeShared<FJsonValueNumber>(Q.X),MakeShared<FJsonValueNumber>(Q.Y),MakeShared<FJsonValueNumber>(Q.Z),MakeShared<FJsonValueNumber>(Q.W)});
    return J;
}
TSharedRef<FJsonObject> SolverSettings(const Chaos::FPBDJointSolverSettings& S)
{
    auto J=MakeShared<FJsonObject>();
#define N(F) J->SetNumberField(TEXT(#F),S.F)
#define B(F) J->SetBoolField(TEXT(#F),S.F)
    N(SwingTwistAngleTolerance); N(PositionTolerance); N(AngleTolerance);
    N(MinParentMassRatio); N(MaxInertiaRatio); N(MinSolverStiffness); N(MaxSolverStiffness);
    N(NumIterationsAtMaxSolverStiffness); N(NumShockPropagationIterations);
    B(bUseSimd); B(bSortEnabled); B(bSolvePositionLast); B(bUsePositionBasedDrives);
    B(bEnableTwistLimits); B(bEnableSwingLimits); B(bEnableDrives);
    N(LinearStiffnessOverride); N(TwistStiffnessOverride); N(SwingStiffnessOverride);
    N(LinearProjectionOverride); N(AngularProjectionOverride); N(ShockPropagationOverride);
    N(LinearDriveStiffnessOverride); N(LinearDriveDampingOverride);
    N(AngularDriveStiffnessOverride); N(AngularDriveDampingOverride);
    N(SoftLinearStiffnessOverride); N(SoftLinearDampingOverride);
    N(SoftTwistStiffnessOverride); N(SoftTwistDampingOverride);
    N(SoftSwingStiffnessOverride); N(SoftSwingDampingOverride);
#undef N
#undef B
    return J;
}
TSharedRef<FJsonObject> Body(FBodyInstance& B)
{
    auto J=MakeShared<FJsonObject>();
    J->SetObjectField(TEXT("world"),T(B.GetUnrealWorldTransform()));
    J->SetArrayField(TEXT("linearVelocity"),V(B.GetUnrealWorldVelocity()));
    J->SetArrayField(TEXT("angularVelocity"),V(B.GetUnrealWorldAngularVelocityInRadians()));
    J->SetBoolField(TEXT("awake"),B.IsInstanceAwake());
    return J;
}
bool Step(FPhysScene* Scene,float Dt)
{
    const FVector Gravity=FVector::ZeroVector;
    const double Before=Scene->GetSolver()->GetSolverTime();
    Scene->SetUpForFrame(&Gravity,Dt,0,Dt,Dt,1,false);
    Scene->StartFrame(); Scene->WaitPhysScenes(); Scene->EndFrame();
    return FMath::IsNearlyEqual(Scene->GetSolver()->GetSolverTime()-Before,static_cast<double>(Dt),1.e-6);
}
TSharedRef<FJsonObject> Condition(double InvMP,double InvMC,const Chaos::FVec3& InvIP,const Chaos::FVec3& InvIC,double MinRatio,double MaxRatio)
{
    double OutMP,OutMC;Chaos::FVec3 OutIP,OutIC;
    Chaos::FPBDJointUtilities::ConditionInverseMassAndInertia(InvMP,InvMC,InvIP,InvIC,MinRatio,MaxRatio,OutMP,OutMC,OutIP,OutIC);
    auto C=MakeShared<FJsonObject>();
    C->SetNumberField(TEXT("minParentMassRatio"),MinRatio);C->SetNumberField(TEXT("maxInertiaRatio"),MaxRatio);
    C->SetNumberField(TEXT("parentInverseMass"),InvMP);C->SetNumberField(TEXT("childInverseMass"),InvMC);
    C->SetArrayField(TEXT("parentInverseInertia"),V(InvIP));C->SetArrayField(TEXT("childInverseInertia"),V(InvIC));
    C->SetNumberField(TEXT("resultParentInverseMass"),OutMP);C->SetNumberField(TEXT("resultChildInverseMass"),OutMC);
    C->SetArrayField(TEXT("resultParentInverseInertia"),V(OutIP));C->SetArrayField(TEXT("resultChildInverseInertia"),V(OutIC));
    return C;
}
}

bool ExportAlsPhysicsJointSolverReference(const FString& Output,FString& Error,bool DisableSleep)
{
    using namespace AlsJointSolverReference; using namespace Chaos;
    const auto Fail=[&](const FString& Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Solver reference requires a new absolute output file."));
    auto* SleepVar=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Solver.Sleep.Enabled"));
    if(!SleepVar)return Fail(TEXT("Missing native sleep switch."));
    struct FRestoreSleep
    {
        IConsoleVariable* Var;int32 Previous;bool Changed;
        ~FRestoreSleep(){if(Changed)Var->SetWithCurrentPriority(Previous);}
    } RestoreSleep{SleepVar,SleepVar->GetInt(),DisableSleep};
    if(DisableSleep)SleepVar->SetWithCurrentPriority(0);
    if(DisableSleep&&SleepVar->GetInt()!=0)return Fail(TEXT("Cannot isolate awake solver trajectories."));
    auto Root=MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("coordinates"),TEXT("UE world bone transforms; cm, kg, radians; force kg*cm/s^2, torque kg*cm^2/s^2"));
    Root->SetStringField(TEXT("observation"),TEXT("Actual synchronous Chaos scene steps; isolated fixed-parent native pair; no contact, gravity, animation tick or saved assets"));
    Root->SetNumberField(TEXT("stepsPerCase"),12);
    if(DisableSleep)Root->SetBoolField(TEXT("sleepEnabled"),false);
    auto CVars=MakeShared<FJsonObject>();
    for(const TCHAR* Name:{TEXT("p.Chaos.Solver.InertiaConditioning.Enabled"),TEXT("p.Chaos.Solver.InertiaConditioning.Distance"),
        TEXT("p.Chaos.Solver.InertiaConditioning.RotationRatio"),TEXT("p.Chaos.Solver.InertiaConditioning.MaxInvInertiaComponentRatio"),
        TEXT("p.Chaos.InertiaConditioning.InvMassTolerance"),TEXT("p.Chaos.InertiaConditioning.InvInertiaTolerance"),TEXT("p.Chaos.InertiaConditioning.ExtentTolerance")})
    {
        const auto* Var=IConsoleManager::Get().FindConsoleVariable(Name);
        if(!Var)return Fail(TEXT("Missing inertia conditioning CVar."));
        CVars->SetNumberField(Name,Var->GetFloat());
    }
    Root->SetObjectField(TEXT("bodyInertiaConditioningCVars"),CVars);
    TArray<TSharedPtr<FJsonValue>> Cases;
    // Reuse one preview world. Allocating a renderer scene per case queues many
    // large GPU reservations before the Editor can service its render thread.
    // Bodies and constraints are still recreated and checked for each case.
    const auto Initialization=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(true)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Initialization));
    if(!World.IsValid())return Fail(TEXT("Cannot create solver reference world."));
    GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
    struct FCleanup{UWorld* W;~FCleanup(){W->GetPhysicsScene()->WaitPhysScenes();W->DestroyWorld(false);GEngine->DestroyWorldContext(W);}} Cleanup{World.Get()};
    auto* Scene=World->GetPhysicsScene();auto* Solver=Scene->GetSolver();
    Solver->SetThreadingMode_External(EThreadingModeTemp::SingleThread);Solver->DisableAsyncMode();Solver->SetIsPaused_External(false);
    for(const TCHAR* Name:{TEXT("Mannequin"),TEXT("AnimMan")})
    for(const TCHAR* ChildName:{TEXT("spine_02"),TEXT("neck_01")})
    for(const int32 Hz:{30,60,120})
    for(int32 Axis=0;Axis<3;++Axis)
    for(const double Sign:{-1.0,1.0})
    for(const bool bFastDrive:{false,true})
    {
        const FString MeshPath=FString(TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/"))+Name+TEXT(".")+Name;
        auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*MeshPath);auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;
        if(!Asset)return Fail(TEXT("Missing pair asset."));
        auto* Owner=World->SpawnActor<AActor>();auto* Component=NewObject<USkeletalMeshComponent>(Owner);
        Owner->SetRootComponent(Component);Owner->AddInstanceComponent(Component);
        Component->SetSkeletalMesh(Mesh);Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
        Component->SetCollisionResponseToAllChannels(ECR_Ignore);Component->SetSimulatePhysics(true);
        Component->RegisterComponent();Component->SetComponentTickEnabled(false);
        FConstraintInstance* Joint=nullptr;
        for(const UPhysicsConstraintTemplate* Template:Asset->ConstraintSetup)
        {
            auto* C=Component->FindConstraintInstance(Template->DefaultInstance.JointName);
            if(!C)return Fail(TEXT("Missing native joint instance."));
            if(C->ConstraintBone1==FName(ChildName))Joint=C;
            else C->TermConstraint();
        }
        if(!Joint||!Joint->GetPhysicsConstraintRef().IsValid())return Fail(TEXT("Missing selected pair."));
        for(const USkeletalBodySetup* Setup:Asset->SkeletalBodySetups)
        {
            auto* B=Component->GetBodyInstance(Setup->BoneName);if(!B)return Fail(TEXT("Missing native body."));
            B->SetInstanceSimulatePhysics(Setup->BoneName==Joint->ConstraintBone1);
            B->SetEnableGravity(false);B->SetResponseToAllChannels(ECR_Ignore);
        }
        auto* Parent=Component->GetBodyInstance(Joint->ConstraintBone2);auto* Child=Component->GetBodyInstance(Joint->ConstraintBone1);
        if(!Parent||!Child)return Fail(TEXT("Missing pair body."));
        if(bFastDrive)Joint->SetAngularDriveParams(25000,0,0); // ALS full-speed case, native conversion applies scale.
        const float Dt=1.f/Hz;
        if(!Step(Scene,Dt))return Fail(TEXT("Warmup did not advance exactly one native step."));
        if(Parent->IsInstanceSimulatingPhysics()||!Child->IsInstanceSimulatingPhysics())return Fail(TEXT("Pair is not fixed-parent/dynamic-child."));
        const FTransform ParentFrame=Joint->GetRefFrame(EConstraintFrame::Frame2)*Parent->GetUnrealWorldTransform();
        FTransform ChildWorld=Child->GetUnrealWorldTransform();
        const FVector Direction=ParentFrame.GetRotation().RotateVector(FVector(Axis==0,Axis==1,Axis==2));
        const FQuat Turn(Direction,Sign*.6);
        ChildWorld.SetLocation(ParentFrame.GetLocation()+Turn.RotateVector(ChildWorld.GetLocation()-ParentFrame.GetLocation()));
        ChildWorld.SetRotation((Turn*ChildWorld.GetRotation()).GetNormalized());
        Child->SetBodyTransform(ChildWorld,ETeleportType::TeleportPhysics);
        Child->SetLinearVelocity(FVector::ZeroVector,false);Child->SetAngularVelocityInRadians(FVector::ZeroVector,false);
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("mesh"),MeshPath);Row->SetStringField(TEXT("physicsAsset"),Asset->GetPathName());
        Row->SetStringField(TEXT("parent"),Joint->ConstraintBone2.ToString());Row->SetStringField(TEXT("child"),ChildName);
        Row->SetNumberField(TEXT("hz"),Hz);Row->SetNumberField(TEXT("dt"),Dt);Row->SetNumberField(TEXT("axis"),Axis);
        Row->SetNumberField(TEXT("perturbation"),Sign*.6);Row->SetBoolField(TEXT("fullSpeedDrive"),bFastDrive);
        Row->SetObjectField(TEXT("parentFrame"),T(Joint->GetRefFrame(EConstraintFrame::Frame2)));
        Row->SetObjectField(TEXT("childFrame"),T(Joint->GetRefFrame(EConstraintFrame::Frame1)));
        TArray<TSharedPtr<FJsonValue>> Masses;
        for(auto* B:{Parent,Child})
        {
            auto M=MakeShared<FJsonObject>();M->SetNumberField(TEXT("massKg"),B->GetBodyMass());
            M->SetArrayField(TEXT("inertiaKgCm2"),V(B->GetBodyInertiaTensor()));M->SetObjectField(TEXT("massLocal"),T(B->GetMassSpaceLocal()));
            M->SetNumberField(TEXT("linearDamping"),B->LinearDamping);M->SetNumberField(TEXT("angularDamping"),B->AngularDamping);
            M->SetNumberField(TEXT("positionIterations"),B->PositionSolverIterationCount);M->SetNumberField(TEXT("velocityIterations"),B->VelocitySolverIterationCount);
            // The isolated solver is synchronous and WaitPhysScenes has completed.
            // Read the internal particle only at this idle boundary: its conditioned
            // inertia is not the raw tensor exposed by FBodyInstance.
            const auto* Particle=B->GetPhysicsActorHandle()->GetHandle_LowLevel()->CastToRigidParticle();
            if(!Particle)return Fail(TEXT("No internal rigid particle after warmup."));
            M->SetArrayField(TEXT("bodyInverseInertiaScale"),V(FVector(Particle->InvIConditioning())));
            M->SetArrayField(TEXT("bodyConditionedInverseInertia"),V(FVector(Particle->ConditionedInvI())));
            M->SetBoolField(TEXT("bodyInertiaConditioningEnabled"),Particle->InertiaConditioningEnabled());
            Masses.Add(MakeShared<FJsonValueObject>(M));
        }
        Row->SetArrayField(TEXT("bodies"),Masses);
        FPBDJointSettings Settings;FPhysicsCommand::ExecuteRead(Joint->GetPhysicsConstraintRef(),[&](const FPhysicsConstraintHandle& H){Settings=static_cast<const FJointConstraint*>(H.Constraint)->GetJointSettings();});
        Row->SetObjectField(TEXT("jointSettings"),AlsJointReference::Settings(Settings));
        auto* Evolution=Solver->GetEvolution();
        const auto& EffectiveSolver=Evolution->GetJointCombinedConstraints().LinearConstraints.GetSettings();
        if(Evolution->GetJointCombinedConstraints().LinearConstraints.NumConstraints()!=1||
            Evolution->GetJointCombinedConstraints().NonLinearConstraints.NumConstraints()!=0)return Fail(TEXT("Pair isolation left extra native constraints."));
        Row->SetObjectField(TEXT("solverSettings"),SolverSettings(EffectiveSolver));
        Row->SetNumberField(TEXT("positionIterations"),Evolution->GetNumPositionIterations());
        Row->SetNumberField(TEXT("velocityIterations"),Evolution->GetNumVelocityIterations());
        Row->SetNumberField(TEXT("projectionIterations"),Evolution->GetNumProjectionIterations());
        TArray<TSharedPtr<FJsonValue>> Conditioning;
        const FVec3 InvIC=FVec3(Child->GetPhysicsActorHandle()->GetHandle_LowLevel()->CastToRigidParticle()->ConditionedInvI());
        Conditioning.Add(MakeShared<FJsonValueObject>(Condition(0,1.0/Child->GetBodyMass(),FVec3::Zero(),InvIC,EffectiveSolver.MinParentMassRatio,EffectiveSolver.MaxInertiaRatio)));
        Row->SetArrayField(TEXT("jointConditioningMath"),Conditioning);
        if(Cases.IsEmpty())
        {
            // Separate utility probes exercise branches absent from fixed-parent
            // trajectories. Never label a kinematic zero tensor as dynamic inertia.
            TArray<TSharedPtr<FJsonValue>> MathCases;
            for(int32 I=0;I<8;++I)
            {
                const double MP=I%4==0?0:I%4==3?.1:10,MC=I%4==1?0:I%4==3?10:.1;
                MathCases.Add(MakeShared<FJsonValueObject>(Condition(MP,MC,FVec3(1,.5,.05),FVec3(.01,.005,.001),
                    I<4?EffectiveSolver.MinParentMassRatio:0,I<4?EffectiveSolver.MaxInertiaRatio:0)));
            }
            Root->SetArrayField(TEXT("conditioningUtilityCases"),MathCases);
        }
        TArray<TSharedPtr<FJsonValue>> Samples;
        const double InitialTime=Solver->GetSolverTime();
        for(int32 Frame=0;Frame<=12;++Frame)
        {
            if(Frame>0&&!Step(Scene,Dt))return Fail(TEXT("Reference scene did not advance exactly one step."));
            auto Sample=MakeShared<FJsonObject>();Sample->SetNumberField(TEXT("frame"),Frame);Sample->SetNumberField(TEXT("time"),Solver->GetSolverTime()-InitialTime);
            Sample->SetObjectField(TEXT("parent"),Body(*Parent));Sample->SetObjectField(TEXT("child"),Body(*Child));
            FVector Force=FVector::ZeroVector,Torque=FVector::ZeroVector;
            // Frame zero is a teleport, so the previous warmup force is stale.
            if(Frame>0)Joint->GetConstraintForce(Force,Torque);
            Sample->SetBoolField(TEXT("forceAvailable"),Frame>0);
            Sample->SetArrayField(TEXT("force"),V(Force));Sample->SetArrayField(TEXT("torque"),V(Torque));
            const auto P=(Joint->GetRefFrame(EConstraintFrame::Frame2)*Parent->GetUnrealWorldTransform()).GetRotation();
            auto C=(Joint->GetRefFrame(EConstraintFrame::Frame1)*Child->GetUnrealWorldTransform()).GetRotation();C.EnforceShortestArcWith(P);
            FReal Twist,Swing1,Swing2;FPBDJointUtilities::GetSwingTwistAngles(P,C,Twist,Swing1,Swing2);
            Sample->SetArrayField(TEXT("angles"),V(FVector(Twist,Swing2,Swing1)));
            Samples.Add(MakeShared<FJsonValueObject>(Sample));
        }
        Row->SetArrayField(TEXT("samples"),Samples);Cases.Add(MakeShared<FJsonValueObject>(Row));
        Owner->Destroy();
    }
    Root->SetArrayField(TEXT("cases"),Cases);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json)))return Fail(TEXT("Cannot encode solver reference."));
    IFileManager::Get().MakeDirectory(*FPaths::GetPath(Output),true);
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("Cannot write solver reference."));
}
bool UAlsAnimationGraphLibrary::ExportPhysicsJointSolverReference(const FString& OutputPath)
{
    FString Error;if(ExportAlsPhysicsJointSolverReference(OutputPath,Error))return true;
    UE_LOG(LogTemp,Error,TEXT("Joint solver reference failed: %s"),*Error);return false;
}
