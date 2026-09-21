#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/ChaosPhysicalMaterial.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
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
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "PhysicsProxy/SingleParticlePhysicsProxy.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsJointSolverReference { bool Step(FPhysScene* Scene,float Dt); }

bool ExportAlsPhysicsContactSettings(const FString& Output,FString& Error)
{
    using namespace Chaos;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Contact settings require a new absolute output."));
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(true)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
    if(!World.IsValid())return Fail(TEXT("Cannot create contact settings world."));
    GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
    struct FCleanup{UWorld* W;~FCleanup(){W->GetPhysicsScene()->WaitPhysScenes();W->DestroyWorld(false);GEngine->DestroyWorldContext(W);}} Cleanup{World.Get()};
    auto* Scene=World->GetPhysicsScene();auto* Solver=Scene->GetSolver();
    Solver->SetThreadingMode_External(EThreadingModeTemp::SingleThread);Solver->DisableAsyncMode();Solver->SetIsPaused_External(false);
    if(!AlsJointSolverReference::Step(Scene,1.f/60))return Fail(TEXT("Settings world did not advance."));
    const auto& Actual=Solver->GetEvolution()->GetCollisionConstraints();
    const auto Settings=Actual.GetSolverSettings();
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Project isolated world after tick: collision solver settings; actual asset external particle overlap settings after creation; synthetic native constraint setup/activation, not geometry or trajectory parity"));
    auto Config=MakeShared<FJsonObject>();
    Config->SetNumberField(TEXT("maxPushOutVelocity"),Settings.MaxPushOutVelocity);
    Config->SetNumberField(TEXT("depenetrationVelocity"),Settings.DepenetrationVelocity);
    Config->SetNumberField(TEXT("restitutionThreshold"),Actual.GetRestitutionThreshold());
    Config->SetNumberField(TEXT("positionFrictionIterations"),Settings.NumPositionFrictionIterations);
    Config->SetNumberField(TEXT("velocityFrictionIterations"),Settings.NumVelocityFrictionIterations);
    Config->SetNumberField(TEXT("positionShockIterations"),Settings.NumPositionShockPropagationIterations);
    Config->SetNumberField(TEXT("velocityShockIterations"),Settings.NumVelocityShockPropagationIterations);
    Config->SetBoolField(TEXT("splitImpulse"),Settings.bUseSplitImpulse);
    Root->SetObjectField(TEXT("solver"),Config);
    auto Vars=MakeShared<FJsonObject>();
    for(const auto* Name:{TEXT("p.Chaos.PBDCollisionSolver.EnableInitialDepenetration"),TEXT("p.Chaos.PBDCollisionSolver.RestitutionUsePreIntegrateVelocity")})
    {
        const auto* Var=IConsoleManager::Get().FindConsoleVariable(Name);if(!Var)return Fail(TEXT("Missing gather CVar."));
        Vars->SetNumberField(Name,Var->GetFloat());
    }
    Root->SetObjectField(TEXT("cvars"),Vars);
    TArray<TSharedPtr<FJsonValue>> Rigs;
    for(const TCHAR* Name:{TEXT("Mannequin"),TEXT("AnimMan")})
    {
        const FString Path=FString(TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/"))+Name+TEXT(".")+Name;
        auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Path);auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;
        if(!Asset)return Fail(TEXT("Missing physics asset."));
        auto* Owner=World->SpawnActor<AActor>();if(!Owner)return Fail(TEXT("Cannot spawn settings owner."));
        auto* Component=NewObject<USkeletalMeshComponent>(Owner);Owner->SetRootComponent(Component);Owner->AddInstanceComponent(Component);
        Component->SetSkeletalMesh(Mesh);Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
        Component->SetSimulatePhysics(true);Component->RegisterComponent();Component->SetComponentTickEnabled(false);
        auto Rig=MakeShared<FJsonObject>();Rig->SetStringField(TEXT("mesh"),Path);Rig->SetStringField(TEXT("physicsAsset"),Asset->GetPathName());
        TArray<TSharedPtr<FJsonValue>> Bodies;
        for(int32 I=0;I<Asset->SkeletalBodySetups.Num();++I)
        {
            const USkeletalBodySetup* Setup=Asset->SkeletalBodySetups[I];auto* Body=Setup?Component->GetBodyInstance(Setup->BoneName):nullptr;
            if(!Body||!Body->IsValidBodyInstance())return Fail(TEXT("Missing settings body."));
            auto* Actor=Body->GetPhysicsActorHandle();if(!Actor)return Fail(TEXT("Missing settings particle."));
            auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("index"),I);Row->SetStringField(TEXT("bone"),Setup->BoneName.ToString());
            Row->SetBoolField(TEXT("override"),Setup->DefaultInstance.GetOverrideMaxDepenetrationVelocity());
            Row->SetNumberField(TEXT("authoredVelocity"),Setup->DefaultInstance.GetMaxDepenetrationVelocity());
            Row->SetNumberField(TEXT("particleVelocity"),Actor->GetGameThreadAPI().GetInitialOverlapDepenetrationVelocity());
            Bodies.Add(MakeShared<FJsonValueObject>(Row));
        }
        Rig->SetArrayField(TEXT("bodies"),Bodies);Rigs.Add(MakeShared<FJsonValueObject>(Rig));Owner->Destroy();
    }
    Root->SetArrayField(TEXT("rigs"),Rigs);
    TArray<TSharedPtr<FJsonValue>> Cases;
    for(int32 Mode=0;Mode<3;++Mode)for(float A:{-1.f,0.f,.3f,100.f})for(float B:{-1.f,0.f,.3f,100.f})
    {
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr Box=MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-2),FVec3(2));
        for(int32 I=0;I<2;++I){
            P[I]->SetGeometry(Box);P[I]->SetX(FVec3(0));P[I]->SetR(FRotation3::Identity);
            P[I]->SetObjectStateLowLevel((Mode==1&&I==1)||(Mode==2&&I==0)?EObjectStateType::Kinematic:EObjectStateType::Dynamic);
            P[I]->SetInitialOverlapDepenetrationVelocity(I==0?A:B);
        }
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        Constraints.SetSolverSettings(Settings);Constraints.SetRestitutionThreshold(Actual.GetRestitutionThreshold());
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto C=Context->CreateConstraint(P[0],Box.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],Box.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,10,true,EContactShapesType::BoxBox);
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("mode"),Mode);Row->SetNumberField(TEXT("body0"),A);Row->SetNumberField(TEXT("body1"),B);
        Row->SetNumberField(TEXT("setupVelocity"),C->GetInitialOverlapDepenetrationVelocity());
        Context->ActivateConstraint(C.Get());
        Row->SetNumberField(TEXT("activatedVelocity"),C->GetInitialOverlapDepenetrationVelocity());
        Row->SetBoolField(TEXT("perContact"),C->UsePerContactInitialPhi());
        Cases.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Cases);
    FString Json;const auto Writer=TJsonWriterFactory<>::Create(&Json);
    if(!FJsonSerializer::Serialize(Root,Writer)||!FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
        return Fail(TEXT("Cannot save contact settings."));
    return true;
}
