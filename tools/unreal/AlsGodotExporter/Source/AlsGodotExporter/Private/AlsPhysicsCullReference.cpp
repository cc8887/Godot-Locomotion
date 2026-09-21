#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/ChaosPhysicalMaterial.h"
#include "Chaos/Collision/ParticlePairMidPhase.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PBDRigidsSolver.h"
#include "Physics/Experimental/PhysScene_Chaos.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsJointSolverReference
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& P);
bool Step(FPhysScene* Scene,float Dt);
}
namespace
{
// Observe the real native midphase immediately before narrow phase. No copied
// distance formula, geometry, constraints or Core-computed result is supplied.
class FCullObserver final : public Chaos::FParticlePairMidPhase
{
public:
    FCullObserver():FParticlePairMidPhase(Chaos::EParticlePairMidPhaseType::Generic){}
    float Distance=-1;
    float Scale() const{return CullDistanceScale;}
protected:
    void ResetImpl() override {}
    void BuildDetectorsImpl() override {}
    int32 GenerateCollisionsImpl(float Dt,float Cull,const Chaos::FVec3f& Movement,const Chaos::FCollisionContext& Context) override
    { Distance=Cull;return 0; }
    void WakeCollisionsImpl(int32 Epoch) override {}
    void InjectCollisionImpl(const Chaos::FPBDCollisionConstraint& Constraint,const Chaos::FCollisionContext& Context) override {}
};
}
bool ExportAlsPhysicsCullReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Cull reference requires a new absolute file."));
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(true)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
    if(!World.IsValid())return Fail(TEXT("Cannot create reference world."));
    GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
    struct FCleanup{UWorld* W;~FCleanup(){W->GetPhysicsScene()->WaitPhysScenes();W->DestroyWorld(false);GEngine->DestroyWorldContext(W);}} Cleanup{World.Get()};
    auto* Scene=World->GetPhysicsScene();auto* Solver=Scene->GetSolver();
    Solver->SetThreadingMode_External(EThreadingModeTemp::SingleThread);Solver->DisableAsyncMode();Solver->SetIsPaused_External(false);
    if(!Step(Scene,1.f/60))return Fail(TEXT("Reference world did not advance."));
    const auto Actual=Solver->GetEvolution()->GetCollisionConstraints().GetDetectorSettings();
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Actual isolated project world detector settings after tick; native FParticlePairMidPhase Init/GenerateCollisions observed before narrow phase; synthetic particle bounds and PreV; non-MACD, no geometry/trajectory parity"));
    auto Settings=MakeShared<FJsonObject>();
    Settings->SetNumberField(TEXT("boundsExpansion"),Actual.BoundsExpansion);
    Settings->SetNumberField(TEXT("velocityInflation"),Actual.BoundsVelocityInflation);
    Settings->SetNumberField(TEXT("maximumVelocityExpansion"),Actual.MaxVelocityBoundsExpansion);
    Settings->SetBoolField(TEXT("allowMacd"),Actual.bAllowMACD);
    Root->SetObjectField(TEXT("detector"),Settings);
    auto Vars=MakeShared<FJsonObject>();
    for(const auto* Name:{TEXT("p.Chaos.Collision.CullDistanceReferenceSize"),TEXT("p.Chaos.Collision.MinCullDistanceScale"),
        TEXT("p.Chaos.Solver.Collision.CullDistance"),TEXT("p.Chaos.Solver.Collision.VelocityBoundsMultiplier"),
        TEXT("p.Chaos.Solver.Collision.MaxVelocityBoundsExpansion")})
    {
        const auto* Var=IConsoleManager::Get().FindConsoleVariable(Name);
        if(!Var)return Fail(TEXT("Missing cull CVar."));
        Vars->SetNumberField(Name,Var->GetFloat());
    }
    Root->SetObjectField(TEXT("cvars"),Vars);
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(int32 Hz:{30,60,120})for(int32 Mode=0;Mode<3;++Mode)for(double Size:{27.,250.,1000.})
    for(int32 Speed=0;Speed<4;++Speed)for(int32 Expansion=0;Expansion<3;++Expansion)
    {
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        for(int32 I=0;I<2;++I)
        {
            const double S=I==0?Size:5000.;
            P[I]->SetGeometry(MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-S*.5),FVec3(S*.5)));
            P[I]->SetX(FVec3(0));P[I]->SetR(FRotation3::Identity);
            P[I]->SetObjectStateLowLevel((Mode==1&&I==1)||(Mode==2&&I==0)?EObjectStateType::Kinematic:EObjectStateType::Dynamic);
            P[I]->SetV(FVec3(0,0,-999)); // Deliberately differs from PreV.
            P[I]->SetPreV(FVec3(Speed==0?0:Speed==1?1:Speed==2?40:10000,-(Speed==2?40:0),0));
        }
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;
        Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto Config=Actual;Config.bAllowMACD=false;Config.bAllowCCD=false;
        if(Expansion==1)Config.BoundsVelocityInflation=0;
        if(Expansion==2)Config.MaxVelocityBoundsExpansion=0;
        FCollisionContext Context;Context.SetSettings(Config);Context.SetAllocator(Allocator.GetContextAllocator(0));
        FCullObserver Observer;Observer.Init(P[0],P[1],{},Context);
        const double Dt=1./Hz;Observer.GenerateCollisions(Config.BoundsExpansion,Dt,Context);
        if(Observer.Distance<0)return Fail(TEXT("Native midphase was not observed."));
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("dt"),Dt);
        Row->SetNumberField(TEXT("baseDistance"),Config.BoundsExpansion);
        Row->SetNumberField(TEXT("velocityInflation"),Config.BoundsVelocityInflation);
        Row->SetNumberField(TEXT("maximumVelocityExpansion"),Config.MaxVelocityBoundsExpansion);
        TArray<TSharedPtr<FJsonValue>> Bodies;
        for(int32 I=0;I<2;++I)
        {
            auto Body=MakeShared<FJsonObject>();
            Body->SetBoolField(TEXT("dynamic"),P[I]->IsDynamic());
            Body->SetBoolField(TEXT("hasBounds"),P[I]->HasBounds());
            Body->SetNumberField(TEXT("boundsSize"),P[I]->LocalBounds().Extents().GetMax());
            Body->SetArrayField(TEXT("preV"),V(FVector(FConstGenericParticleHandle(P[I])->GetPreVf())));
            Bodies.Add(MakeShared<FJsonValueObject>(Body));
        }
        Row->SetArrayField(TEXT("bodies"),Bodies);Row->SetNumberField(TEXT("scale"),Observer.Scale());
        Row->SetNumberField(TEXT("distance"),Observer.Distance);Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Rows);
    FString Json;const auto Writer=TJsonWriterFactory<>::Create(&Json);
    if(!FJsonSerializer::Serialize(Root,Writer)||!FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
        return Fail(TEXT("Cannot save cull reference."));
    return true;
}
