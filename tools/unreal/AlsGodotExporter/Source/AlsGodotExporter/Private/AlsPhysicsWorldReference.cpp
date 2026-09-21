#include "AlsPhysicsAssetExport.h"
#include "Chaos/Island/IslandManager.h"
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
#include "PhysicsEngine/BodySetup.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "PhysicsProxy/SingleParticlePhysicsProxy.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace AlsJointSolverReference {
TArray<TSharedPtr<FJsonValue>> V(const FVector& P);
TSharedRef<FJsonObject> Body(FBodyInstance& B,bool SleepDiagnostics);
}
namespace AlsCoupledStepReference {
Chaos::FVec3 ReadV(const TSharedPtr<FJsonObject>& J,const TCHAR* Name);
Chaos::FRigidTransform3 ReadT(const TSharedPtr<FJsonObject>& J);
}

bool ExportAlsPhysicsWorldReference(const FString& Inputs,const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;using namespace AlsCoupledStepReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(FPaths::IsRelative(Inputs)||!IFileManager::Get().DirectoryExists(*Inputs)||Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("World reference needs a capture directory and new absolute output."));
    const auto* Sleep=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Solver.Sleep.Enabled"));
    if(!Sleep||Sleep->GetInt()!=1)return Fail(TEXT("Native sleeping must be enabled."));
    TArray<FString> Files;IFileManager::Get().FindFiles(Files,*(Inputs/TEXT("*.json")),true,false);Files.Sort();
    if(Files.IsEmpty()||Files.Num()>12)return Fail(TEXT("Expected 1..12 world setups."));
    TArray<TSharedPtr<FJsonValue>> Cases;
    for(const auto& File:Files)
    {
        FString Text;TSharedPtr<FJsonObject> Input;
        if(!FFileHelper::LoadFileToString(Text,*(Inputs/File))||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Input)||
            !Input.IsValid()||Input->GetIntegerField(TEXT("schemaVersion"))!=1)return Fail(TEXT("Invalid world setup."));
        const int32 Hz=Input->GetIntegerField(TEXT("hz")),Steps=Input->GetIntegerField(TEXT("steps"));
        if((Hz!=30&&Hz!=60&&Hz!=120)||Steps!=Hz*10)return Fail(TEXT("Expected fixed ten-second world setup."));
        const float Dt=1.f/Hz;const FVector Gravity=ReadV(Input,TEXT("gravity"));
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true).RequiresHitProxies(false)
            .CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(true).EnableTraceCollision(true).SetTransactional(false);
        TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
        if(!World.IsValid())return Fail(TEXT("Cannot create native world."));
        GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
        struct FCleanup{UWorld* W;~FCleanup(){W->GetPhysicsScene()->WaitPhysScenes();W->DestroyWorld(false);GEngine->DestroyWorldContext(W);}} Cleanup{World.Get()};
        auto* Scene=World->GetPhysicsScene();auto* Solver=Scene->GetSolver();
        Solver->SetThreadingMode_External(EThreadingModeTemp::SingleThread);Solver->DisableAsyncMode();Solver->SetIsPaused_External(false);
        auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Input->GetStringField(TEXT("mesh")));auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;
        const auto& BodyInputs=Input->GetArrayField(TEXT("bodies"));
        if(!Asset||Asset->SkeletalBodySetups.Num()!=BodyInputs.Num())return Fail(TEXT("Native body asset count differs."));
        auto* Owner=World->SpawnActor<AActor>();auto* Component=NewObject<USkeletalMeshComponent>(Owner);
        Owner->SetRootComponent(Component);Owner->AddInstanceComponent(Component);Component->SetSkeletalMesh(Mesh);
        Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);Component->SetCollisionObjectType(ECC_PhysicsBody);
        Component->SetCollisionResponseToAllChannels(ECR_Block);Component->SetSimulatePhysics(true);Component->RegisterComponent();Component->SetComponentTickEnabled(false);
        TArray<FBodyInstance*> Bodies;
        for(int32 I=0;I<BodyInputs.Num();++I)
        {
            const auto B=BodyInputs[I]->AsObject();const auto Name=Asset->SkeletalBodySetups[I]->BoneName;
            if(Name.ToString()!=B->GetStringField(TEXT("name")))return Fail(TEXT("Native body order differs."));
            auto* Instance=Component->GetBodyInstance(Name);if(!Instance||!Instance->IsValidBodyInstance())return Fail(TEXT("Native body missing."));
            Instance->SetInstanceSimulatePhysics(B->GetBoolField(TEXT("dynamic")));Instance->SetEnableGravity(B->GetBoolField(TEXT("gravity")));
            Instance->SetBodyTransform(FTransform(ReadT(B->GetObjectField(TEXT("actor")))),ETeleportType::TeleportPhysics);
            Instance->SetLinearVelocity(ReadV(B,TEXT("v")),false);Instance->SetAngularVelocityInRadians(ReadV(B,TEXT("w")),false);Instance->WakeInstance();Bodies.Add(Instance);
        }
        // Independent native static bodies reconstruct the entire captured scene,
        // not just a guessed infinite floor. Their material matches Core's
        // homogeneous resolved material contract for this diagnostic.
        TArray<TStrongObjectPtr<UBodySetup>> EnvironmentSetups;TArray<TUniquePtr<FBodyInstance>> EnvironmentBodies;
        TArray<bool> EnvironmentKinematic;
        struct FTermEnvironment{TArray<TUniquePtr<FBodyInstance>>& B;~FTermEnvironment(){for(auto& P:B)P->TermBody();}} TermEnvironment{EnvironmentBodies};
        for(const auto& Value:Input->GetArrayField(TEXT("environment")))
        {
            const auto E=Value->AsObject(),G=E->GetObjectField(TEXT("geometry"));auto* Setup=NewObject<UBodySetup>();
            EnvironmentSetups.Emplace(Setup);Setup->CollisionTraceFlag=CTF_UseSimpleAsComplex;
            if(G->GetStringField(TEXT("type"))==TEXT("box"))
            {FKBoxElem Box;const auto Size=ReadV(G,TEXT("size"));Box.X=Size.X;Box.Y=Size.Y;Box.Z=Size.Z;Setup->AggGeom.BoxElems.Add(Box);}
            else if(G->GetStringField(TEXT("type"))==TEXT("convex"))
            {
                FKConvexElem Convex;for(const auto& Vertex:G->GetArrayField(TEXT("vertices")))
                {const auto& A=Vertex->AsArray();Convex.VertexData.Add(FVector(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber()));}
                Convex.UpdateElemBox();Setup->AggGeom.ConvexElems.Add(MoveTemp(Convex));
            }
            else return Fail(TEXT("Unsupported captured environment shape."));
            Setup->CreatePhysicsMeshes();auto Instance=MakeUnique<FBodyInstance>();
            Instance->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);Instance->SetObjectType(ECC_WorldStatic);Instance->SetResponseToAllChannels(ECR_Block);
            bool Kinematic=false;E->TryGetBoolField(TEXT("kinematic"),Kinematic);
            Instance->InitBody(Setup,FTransform(ReadT(E->GetObjectField(TEXT("world")))),nullptr,Scene,FInitBodySpawnParams(!Kinematic,false));
            if(!Instance->IsValidBodyInstance())return Fail(TEXT("Native environment body missing."));
            Instance->SetPhysMaterialOverride(Bodies[0]->GetSimplePhysicalMaterial());EnvironmentBodies.Add(MoveTemp(Instance));EnvironmentKinematic.Add(Kinematic);
        }
        TArray<TSharedPtr<FJsonValue>> Samples;int32 AllSleepFrame=INDEX_NONE,Held=0;double LastMaxV=0,LastMaxW=0;
        for(int32 Frame=0;Frame<=Steps;++Frame)
        {
            if(Frame>0)
            {
                const double Before=Solver->GetSolverTime();Scene->SetUpForFrame(&Gravity,Dt,0,Dt,Dt,1,false);
                Scene->StartFrame();Scene->WaitPhysScenes();Scene->EndFrame();
                if(!FMath::IsNearlyEqual(Solver->GetSolverTime()-Before,static_cast<double>(Dt),1.e-6))return Fail(TEXT("Native scene step duration differs."));
                for(int32 I=0;I<EnvironmentBodies.Num();++I)
                {
                    const auto* Particle=EnvironmentBodies[I]->GetPhysicsActorHandle()->GetHandle_LowLevel();
                    if(Particle->ObjectState()!=(EnvironmentKinematic[I]?EObjectStateType::Kinematic:EObjectStateType::Static))
                        return Fail(TEXT("Native environment motion type differs."));
                }
            }
            auto Sample=MakeShared<FJsonObject>();Sample->SetNumberField(TEXT("frame"),Frame);TArray<TSharedPtr<FJsonValue>> States;
            int32 Awake=0;
            for(int32 I=0;I<Bodies.Num();++I)
            {
                auto* Instance=Bodies[I];auto State=Body(*Instance,Frame>0);State->SetStringField(TEXT("name"),BodyInputs[I]->AsObject()->GetStringField(TEXT("name")));
                if(Instance->IsInstanceSimulatingPhysics()&&Instance->IsInstanceAwake())Awake++;
                if(Frame>=Steps-Hz){LastMaxV=FMath::Max(LastMaxV,Instance->GetUnrealWorldVelocity().Size());LastMaxW=FMath::Max(LastMaxW,Instance->GetUnrealWorldAngularVelocityInRadians().Size());}
                if(Frame>0)
                {
                    const auto* Particle=Instance->GetPhysicsActorHandle()->GetHandle_LowLevel()->CastToRigidParticle();
                    State->SetArrayField(TEXT("conditionedInverseInertia"),V(FVector(Particle->ConditionedInvI())));
                    const auto* Island=Solver->GetEvolution()->GetIslandManager().GetParticleIsland(Particle);
                    State->SetNumberField(TEXT("islandSleepCounter"),Island?Island->GetSleepCounter():-1);
                }
                States.Add(MakeShared<FJsonValueObject>(State));
            }
            if(Frame>0&&Awake==0){if(AllSleepFrame==INDEX_NONE)AllSleepFrame=Frame;Held++;}else if(Frame>0){AllSleepFrame=INDEX_NONE;Held=0;}
            Sample->SetNumberField(TEXT("awakeBodies"),Awake);
            Sample->SetNumberField(TEXT("contactPairs"),Solver->GetEvolution()->GetCollisionConstraints().NumConstraints());
            Sample->SetNumberField(TEXT("linearJoints"),Solver->GetEvolution()->GetJointCombinedConstraints().LinearConstraints.NumConstraints());
            Sample->SetNumberField(TEXT("nonlinearJoints"),Solver->GetEvolution()->GetJointCombinedConstraints().NonLinearConstraints.NumConstraints());
            Sample->SetArrayField(TEXT("bodies"),States);Samples.Add(MakeShared<FJsonValueObject>(Sample));
        }
        auto Case=MakeShared<FJsonObject>();Case->SetObjectField(TEXT("setup"),Input);Case->SetNumberField(TEXT("dtUsed"),Dt);
        Case->SetNumberField(TEXT("allSleepFrame"),AllSleepFrame);Case->SetNumberField(TEXT("heldSleepingFrames"),Held);
        Case->SetBoolField(TEXT("oneSecondSleepBudget"),Held>=Hz);Case->SetNumberField(TEXT("lastSecondMaxLinear"),LastMaxV);Case->SetNumberField(TEXT("lastSecondMaxAngular"),LastMaxW);
        Case->SetArrayField(TEXT("samples"),Samples);Cases.Add(MakeShared<FJsonValueObject>(Case));
    }
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Full native PhysicsAsset and captured fixed environment, synchronous Chaos scene, supplied initial actor states; native float dt recorded; no Core trajectory or expected sleep status drives simulation"));
    Root->SetArrayField(TEXT("cases"),Cases);FString Text;
    return (FJsonSerializer::Serialize(Root,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Text))&&FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))||Fail(TEXT("Native world write failed."));
}
