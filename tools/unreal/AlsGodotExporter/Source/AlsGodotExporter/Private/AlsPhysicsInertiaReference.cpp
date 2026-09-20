#include "AlsPhysicsAssetExport.h"
#include "Chaos/MassConditioning.h"
#include "Chaos/PBDJointConstraints.h"
#include "Chaos/PBDJointConstraintUtilities.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Engine.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
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

namespace AlsJointSolverReference
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& P);
TSharedRef<FJsonObject> T(const FTransform& P);
bool Step(FPhysScene* Scene,float Dt);
}

bool ExportAlsPhysicsInertiaReference(const FString& Output,FString& Error)
{
    using namespace AlsJointSolverReference; using namespace Chaos;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Inertia reference requires a new absolute output file."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("coordinates"),TEXT("UE actor-local bounds; COM-local extents; cm, kg, inverse kg*cm^2"));
    auto Settings=MakeShared<FJsonObject>();
    const auto CVar=[](const TCHAR* Name){return IConsoleManager::Get().FindConsoleVariable(Name)->GetFloat();};
    const float Distance=CVar(TEXT("p.Chaos.Solver.InertiaConditioning.Distance"));
    const float Rotation=CVar(TEXT("p.Chaos.Solver.InertiaConditioning.RotationRatio"));
    const float Ratio=CVar(TEXT("p.Chaos.Solver.InertiaConditioning.MaxInvInertiaComponentRatio"));
    FInertiaConditioningTolerances Tolerances;
    Tolerances.InvMassTolerance=CVar(TEXT("p.Chaos.InertiaConditioning.InvMassTolerance"));
    Tolerances.InvInertiaTolerance=CVar(TEXT("p.Chaos.InertiaConditioning.InvInertiaTolerance"));
    Tolerances.ExtentTolerance=CVar(TEXT("p.Chaos.InertiaConditioning.ExtentTolerance"));
    Settings->SetBoolField(TEXT("enabled"),CVar(TEXT("p.Chaos.Solver.InertiaConditioning.Enabled"))!=0);
    Settings->SetNumberField(TEXT("maxDistanceCm"),Distance);Settings->SetNumberField(TEXT("maxRotationRatio"),Rotation);
    Settings->SetNumberField(TEXT("maxInvInertiaComponentRatio"),Ratio);
    Settings->SetNumberField(TEXT("inverseMassTolerance"),Tolerances.InvMassTolerance);
    Settings->SetNumberField(TEXT("inverseInertiaTolerance"),Tolerances.InvInertiaTolerance);
    Settings->SetNumberField(TEXT("extentToleranceCm"),Tolerances.ExtentTolerance);
    Root->SetObjectField(TEXT("settings"),Settings);
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(true)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
    if(!World.IsValid())return Fail(TEXT("Cannot create reference world."));
    GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
    struct FCleanup{UWorld* W;~FCleanup(){W->GetPhysicsScene()->WaitPhysScenes();W->DestroyWorld(false);GEngine->DestroyWorldContext(W);}} Cleanup{World.Get()};
    auto* Scene=World->GetPhysicsScene();auto* Solver=Scene->GetSolver();
    Solver->SetThreadingMode_External(EThreadingModeTemp::SingleThread);Solver->DisableAsyncMode();Solver->SetIsPaused_External(false);
    TArray<TSharedPtr<FJsonValue>> Rigs;
    for(const TCHAR* Name:{TEXT("Mannequin"),TEXT("AnimMan")})
    for(const TCHAR* Selected:{TEXT(""),TEXT("spine_02"),TEXT("neck_01")})
    {
        const FString Path=FString(TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/"))+Name+TEXT(".")+Name;
        auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Path);auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;
        if(!Asset)return Fail(TEXT("Missing reference asset."));
        auto* Owner=World->SpawnActor<AActor>();auto* Component=NewObject<USkeletalMeshComponent>(Owner);
        Owner->SetRootComponent(Component);Owner->AddInstanceComponent(Component);Component->SetSkeletalMesh(Mesh);
        Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);Component->SetCollisionResponseToAllChannels(ECR_Ignore);
        Component->SetSimulatePhysics(true);Component->RegisterComponent();Component->SetComponentTickEnabled(false);
        const bool Pair=FCString::Strlen(Selected)>0;
        for(const UPhysicsConstraintTemplate* Template:Asset->ConstraintSetup)
        {
            auto* Joint=Component->FindConstraintInstance(Template->DefaultInstance.JointName);
            if(!Joint)return Fail(TEXT("Missing native constraint."));
            if(Pair&&Joint->ConstraintBone1!=FName(Selected))Joint->TermConstraint();
        }
        for(const USkeletalBodySetup* Setup:Asset->SkeletalBodySetups)
        {
            auto* Body=Component->GetBodyInstance(Setup->BoneName);
            if(!Body)return Fail(TEXT("Missing native body."));
            if(Pair)Body->SetInstanceSimulatePhysics(Setup->BoneName==FName(Selected));
            Body->SetEnableGravity(false);Body->SetResponseToAllChannels(ECR_Ignore);
        }
        if(!Step(Scene,1.f/60))return Fail(TEXT("Warmup did not advance one step."));
        const auto& Constraints=Solver->GetEvolution()->GetJointCombinedConstraints();
        if(Constraints.LinearConstraints.NumConstraints()!=(Pair?1:Asset->ConstraintSetup.Num())||Constraints.NonLinearConstraints.NumConstraints()!=0)
            return Fail(TEXT("Reference topology differs."));
        auto Rig=MakeShared<FJsonObject>();Rig->SetStringField(TEXT("mesh"),Path);Rig->SetStringField(TEXT("physicsAsset"),Asset->GetPathName());
        Rig->SetStringField(TEXT("isolatedChild"),Selected);TArray<TSharedPtr<FJsonValue>> Bodies;
        for(const USkeletalBodySetup* Setup:Asset->SkeletalBodySetups)
        {
            auto* Body=Component->GetBodyInstance(Setup->BoneName);
            const auto* Particle=Body->GetPhysicsActorHandle()->GetHandle_LowLevel()->CastToRigidParticle();
            if(!Particle)return Fail(TEXT("Missing internal particle."));
            const FRigidTransform3 Mass(Particle->CenterOfMass(),Particle->RotationOfMass());
            const auto Bounds=Particle->LocalBounds();const FVec3f CollisionExtent=.5*Bounds.InverseTransformedAABB(Mass).Extents();
            FVec3 Arms(0);TArray<TSharedPtr<FJsonValue>> Connectors;
            for(auto* Handle:Particle->ParticleConstraints())
            {
                if(const auto* Joint=Handle->As<FPBDJointConstraintHandle>())
                {
                    if(FPBDJointUtilities::GetLinearIsFree(Joint->GetSettings()))continue;
                    const auto PairBodies=Joint->GetConstrainedParticles();const int32 Index=PairBodies[0]==Particle?0:1;
                    const auto ActorArm=Joint->GetSettings().ConnectorTransforms[Index].GetTranslation();
                    const auto Arm=Mass.InverseTransformPosition(ActorArm);Arms=FVec3::Max(Arms,Arm.GetAbs());
                    Connectors.Add(MakeShared<FJsonValueArray>(V(ActorArm)));
                }
            }
            const FVec3f Extents=FVec3::Max(FVec3(CollisionExtent),Arms);
            auto B=MakeShared<FJsonObject>();B->SetStringField(TEXT("bone"),Setup->BoneName.ToString());
            B->SetNumberField(TEXT("nativeMassKg"),Body->GetBodyMass());
            B->SetArrayField(TEXT("nativeInertiaKgCm2"),V(Body->GetBodyInertiaTensor()));
            B->SetArrayField(TEXT("localBoundsMin"),V(Bounds.Min()));B->SetArrayField(TEXT("localBoundsMax"),V(Bounds.Max()));
            B->SetObjectField(TEXT("massLocal"),T(FTransform(Mass.GetRotation(),Mass.GetTranslation())));
            B->SetArrayField(TEXT("collisionExtents"),V(FVector(CollisionExtent)));B->SetArrayField(TEXT("constraintExtents"),V(FVector(Extents)));
            B->SetArrayField(TEXT("connectors"),Connectors);B->SetBoolField(TEXT("dynamic"),Particle->IsDynamic());
            B->SetBoolField(TEXT("enabled"),Particle->InertiaConditioningEnabled());
            B->SetNumberField(TEXT("inverseMass"),Particle->InvM());B->SetArrayField(TEXT("inverseInertia"),V(FVector(Particle->InvI())));
            B->SetArrayField(TEXT("actualScale"),V(FVector(Particle->InvIConditioning())));
            B->SetArrayField(TEXT("utilityScale"),V(FVector(CalculateParticleInertiaConditioning(Particle,Distance,Rotation,Ratio,Tolerances))));
            Bodies.Add(MakeShared<FJsonValueObject>(B));
        }
        Rig->SetArrayField(TEXT("bodies"),Bodies);Rigs.Add(MakeShared<FJsonValueObject>(Rig));Owner->Destroy();
    }
    Root->SetArrayField(TEXT("rigs"),Rigs);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json)))return Fail(TEXT("Cannot serialize inertia reference."));
    IFileManager::Get().MakeDirectory(*FPaths::GetPath(Output),true);
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("Cannot write inertia reference."));
}
