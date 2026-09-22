#include "AlsPhysicsAssetExport.h"
#include "Chaos/Island/IslandManager.h"
#include "Chaos/Framework/Parallel.h"
#include "Chaos/ShapeInstance.h"
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
TSharedRef<FJsonObject> T(const FTransform& P);
TSharedRef<FJsonObject> Body(FBodyInstance& B,bool SleepDiagnostics);
}
namespace AlsPhysicsExport { TSharedRef<FJsonObject> T(const FTransform& Transform); }
namespace AlsCoupledStepReference {
Chaos::FVec3 ReadV(const TSharedPtr<FJsonObject>& J,const TCHAR* Name);
Chaos::FRigidTransform3 ReadT(const TSharedPtr<FJsonObject>& J);
}

bool ExportAlsPhysicsWorldReference(const FString& Inputs,const FString& Output,FString& Error,int32 ContactFrames,int32 ContactStart)
{
    using namespace Chaos;using namespace AlsJointSolverReference;using namespace AlsCoupledStepReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(ContactFrames<0||ContactFrames>10||ContactStart<1||ContactStart>1200)return Fail(TEXT("Invalid contact diagnostic window."));
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
        if(ContactFrames>0&&ContactStart+ContactFrames-1>Steps)return Fail(TEXT("Contact window exceeds world setup."));
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
        // The diagnostic callbacks must observe completed inline integration.
        // Do not change scheduling CVars to make a reference pass this guard.
        if(ContactFrames>0&&ShouldExecuteTasks(Bodies.Num()+EnvironmentBodies.Num()))
            return Fail(TEXT("Step observations require inline particle integration."));
        int32 ObservedFrame=0;TArray<TSharedPtr<FJsonValue>> StepObservations;
        auto* Evolution=Solver->GetEvolution();
        const auto ObserveStep=[&](const FReal StepDt,const TCHAR* Stage)
        {
            if(ObservedFrame<ContactStart||ObservedFrame>=ContactStart+ContactFrames)return;
            auto Observation=MakeShared<FJsonObject>();Observation->SetStringField(TEXT("stage"),Stage);
            Observation->SetNumberField(TEXT("dt"),StepDt);TArray<TSharedPtr<FJsonValue>> BodyStates;
            for(int32 I=0;I<Bodies.Num();++I)
            {
                const auto* P=Bodies[I]->GetPhysicsActorHandle()->GetHandle_LowLevel()->CastToRigidParticle();
                auto S=MakeShared<FJsonObject>();S->SetStringField(TEXT("name"),BodyInputs[I]->AsObject()->GetStringField(TEXT("name")));
                S->SetNumberField(TEXT("objectState"),static_cast<int32>(P->ObjectState()));
                S->SetObjectField(TEXT("initialActor"),T(FTransform(FQuat(P->GetR()),FVector(P->GetX()))));
                S->SetObjectField(TEXT("predictedActor"),T(FTransform(FQuat(P->GetQ()),FVector(P->GetP()))));
                S->SetObjectField(TEXT("initialCom"),T(FTransform(P->GetTransformXRCom())));
                S->SetObjectField(TEXT("predictedCom"),T(FTransform(P->GetTransformPQCom())));
                S->SetObjectField(TEXT("massLocal"),T(FTransform(FQuat(P->RotationOfMass()),FVector(P->CenterOfMass()))));
                S->SetArrayField(TEXT("v"),V(FVector(P->GetV())));S->SetArrayField(TEXT("w"),V(FVector(P->GetW())));
                S->SetArrayField(TEXT("acceleration"),V(FVector(P->Acceleration())));
                S->SetArrayField(TEXT("angularAcceleration"),V(FVector(P->AngularAcceleration())));
                S->SetArrayField(TEXT("linearImpulseVelocity"),V(FVector(P->LinearImpulseVelocity())));
                S->SetArrayField(TEXT("angularImpulseVelocity"),V(FVector(P->AngularImpulseVelocity())));
                S->SetNumberField(TEXT("linearDamping"),P->LinearEtherDrag());S->SetNumberField(TEXT("angularDamping"),P->AngularEtherDrag());
                S->SetNumberField(TEXT("inverseMass"),P->InvM());S->SetArrayField(TEXT("conditionedInverseInertia"),V(FVector(P->ConditionedInvI())));
                BodyStates.Add(MakeShared<FJsonValueObject>(S));
            }
            Observation->SetArrayField(TEXT("bodies"),BodyStates);StepObservations.Add(MakeShared<FJsonValueObject>(Observation));
        };
        struct FClearStepCallbacks
        {
            FPBDRigidsEvolutionGBF* E;
            ~FClearStepCallbacks(){E->SetPreIntegrateCallback(nullptr);E->SetPostIntegrateCallback(nullptr);E->SetPreSolveCallback(nullptr);}
        } ClearStepCallbacks{Evolution};
        if(ContactFrames>0)
        {
            Evolution->SetPreIntegrateCallback([&](FReal StepDt){ObserveStep(StepDt,TEXT("preIntegrate"));});
            Evolution->SetPostIntegrateCallback([&](FReal StepDt){ObserveStep(StepDt,TEXT("postIntegrate"));});
            Evolution->SetPreSolveCallback([&](FReal StepDt){ObserveStep(StepDt,TEXT("preSolve"));});
        }
        TArray<TSharedPtr<FJsonValue>> Samples;int32 AllSleepFrame=INDEX_NONE,Held=0;double LastMaxV=0,LastMaxW=0;
        for(int32 Frame=0;Frame<=Steps;++Frame)
        {
            ObservedFrame=Frame;StepObservations.Reset();
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
                    auto* Particle=Instance->GetPhysicsActorHandle()->GetHandle_LowLevel()->CastToRigidParticle();
                    State->SetArrayField(TEXT("conditionedInverseInertia"),V(FVector(Particle->ConditionedInvI())));
                    const auto* Island=Solver->GetEvolution()->GetIslandManager().GetParticleIsland(Particle);
                    State->SetNumberField(TEXT("islandSleepCounter"),Island?Island->GetSleepCounter():-1);
                    if(Frame>=ContactStart&&Frame<ContactStart+ContactFrames)
                    {
                        State->SetNumberField(TEXT("objectState"),static_cast<int32>(Particle->ObjectState()));
                        State->SetNumberField(TEXT("particleGlobalId"),Particle->ParticleID().GlobalID);
                        State->SetNumberField(TEXT("particleLocalId"),Particle->ParticleID().LocalID);
                        State->SetNumberField(TEXT("graphLevel"),Solver->GetEvolution()->GetIslandManager().GetParticleLevel(Particle));
                        State->SetArrayField(TEXT("inflatedBoundsMin"),V(Particle->WorldSpaceInflatedBounds().Min()));
                        State->SetArrayField(TEXT("inflatedBoundsMax"),V(Particle->WorldSpaceInflatedBounds().Max()));
                        TArray<TSharedPtr<FJsonValue>> Shapes;
                        for(const auto& Shape:Particle->ShapesArray())
                        {
                            auto S=MakeShared<FJsonObject>();S->SetBoolField(TEXT("simulation"),Shape->GetSimEnabled());
                            // Observe the actual simulation leaf, not authored dimensions or
                            // the external asset-export proxy. This is read-only diagnostics.
                            const auto* Leaf=Shape->GetLeafGeometry();
                            if(!Leaf)return Fail(TEXT("Missing world diagnostic leaf geometry."));
                            S->SetStringField(TEXT("leafType"),Leaf->GetTypeName().ToString());
                            S->SetNumberField(TEXT("leafMargin"),Leaf->GetMarginf());
                            S->SetArrayField(TEXT("leafBoundsMin"),V(FVector(Leaf->BoundingBox().Min())));
                            S->SetArrayField(TEXT("leafBoundsMax"),V(FVector(Leaf->BoundingBox().Max())));
                            S->SetObjectField(TEXT("leafLocal"),AlsPhysicsExport::T(FTransform(Shape->GetLeafRelativeTransform())));
                            S->SetBoolField(TEXT("query"),Shape->GetQueryEnabled());
                            S->SetStringField(TEXT("filter"),Shape->GetCombinedShapeFilterData().GetShapeFilterData().ToString());
                            const auto& Filter=Shape->GetCombinedShapeFilterData().GetShapeFilterData();
                            S->SetNumberField(TEXT("channel"),Filter.GetCollisionChannelIndex());
                            S->SetStringField(TEXT("blockChannels"),FString::Printf(TEXT("%016llX"),Filter.GetBlockChannels()));
                            S->SetStringField(TEXT("overlapChannels"),FString::Printf(TEXT("%016llX"),Filter.GetOverlapChannels()));
                            S->SetNumberField(TEXT("maskFilter"),Filter.GetMaskFilter());
                            Shapes.Add(MakeShared<FJsonValueObject>(S));
                        }
                        State->SetArrayField(TEXT("shapeFilters"),Shapes);
                    }
                }
                States.Add(MakeShared<FJsonValueObject>(State));
            }
            if(Frame>0&&Awake==0){if(AllSleepFrame==INDEX_NONE)AllSleepFrame=Frame;Held++;}else if(Frame>0){AllSleepFrame=INDEX_NONE;Held=0;}
            Sample->SetNumberField(TEXT("awakeBodies"),Awake);
            if(Frame>=ContactStart&&Frame<ContactStart+ContactFrames)
            {
                if(StepObservations.Num()!=3)return Fail(TEXT("Expected exactly three native step observations."));
                Sample->SetArrayField(TEXT("stepObservations"),StepObservations);
                const auto BodyName=[&](const FGeometryParticleHandle* Particle)->FString
                {
                    for(int32 I=0;I<Bodies.Num();++I)if(Bodies[I]->GetPhysicsActorHandle()->GetHandle_LowLevel()==Particle)
                        return BodyInputs[I]->AsObject()->GetStringField(TEXT("name"));
                    for(int32 I=0;I<EnvironmentBodies.Num();++I)if(EnvironmentBodies[I]->GetPhysicsActorHandle()->GetHandle_LowLevel()==Particle)
                        return FString::Printf(TEXT("environment_%d"),I);
                    return TEXT("unknown");
                };
                TArray<TSharedPtr<FJsonValue>> Contacts;
                Sample->SetNumberField(TEXT("collisionSolverType"),static_cast<int32>(Solver->GetEvolution()->GetCollisionConstraints().GetSolverType()));
                for(const auto* C:Solver->GetEvolution()->GetCollisionConstraints().GetConstraints())
                {
                    auto R=MakeShared<FJsonObject>();const auto A=BodyName(C->GetParticle0()),B=BodyName(C->GetParticle1());
                    if(A==TEXT("unknown")||B==TEXT("unknown"))return Fail(TEXT("Unmapped native contact particle."));
                    R->SetStringField(TEXT("body0"),A);R->SetStringField(TEXT("body1"),B);
                    const auto ShapeIndex=[](const FGeometryParticleHandle* P,const FShapeInstance* S)->int32
                    {for(int32 I=0;I<P->ShapesArray().Num();++I)if(P->ShapesArray()[I].Get()==S)return I;return INDEX_NONE;};
                    const auto S0=ShapeIndex(C->GetParticle0(),C->GetShape0()),S1=ShapeIndex(C->GetParticle1(),C->GetShape1());
                    if(S0==INDEX_NONE||S1==INDEX_NONE)return Fail(TEXT("Unmapped native contact shape."));
                    R->SetNumberField(TEXT("shape0"),S0);R->SetNumberField(TEXT("shape1"),S1);
                    R->SetBoolField(TEXT("disabled"),C->GetDisabled());R->SetBoolField(TEXT("probe"),C->GetIsProbe());
                    R->SetNumberField(TEXT("shapeType"),static_cast<int32>(C->GetShapesType()));
                    R->SetNumberField(TEXT("cull"),C->GetCullDistance());
                    R->SetBoolField(TEXT("restored"),C->WasManifoldRestored());
                    // This is the island graph order, not the allocator's array
                    // order or a claim about a later colored solver partition.
                    const auto* Edge=C->GetConstraintGraphEdge();
                    auto& Graph=Solver->GetEvolution()->GetIslandManager();
                    R->SetBoolField(TEXT("inGraph"),Edge!=nullptr);
                    if(Edge)
                    {
                        R->SetNumberField(TEXT("graphOrder"),Graph.GetIslandArrayIndex(Edge));
                        R->SetNumberField(TEXT("graphLevel"),Graph.GetConstraintLevel(Edge));
                        R->SetNumberField(TEXT("graphColor"),Graph.GetConstraintColor(Edge));
                        R->SetNumberField(TEXT("graphInsertionKey"),static_cast<uint32>(Edge->GetSortKey()));
                        R->SetBoolField(TEXT("graphSleeping"),Edge->IsSleeping());
                    }
                    R->SetBoolField(TEXT("initialContact"),C->IsInitialContact());
                    R->SetBoolField(TEXT("perContactInitialPhi"),C->UsePerContactInitialPhi());
                    R->SetNumberField(TEXT("minInitialPhi"),C->GetMinInitialPhi());
                    R->SetNumberField(TEXT("depenetrationVelocity"),C->GetInitialOverlapDepenetrationVelocity());
                    TArray<TSharedPtr<FJsonValue>> Points;
                    for(int32 I=0;I<C->NumManifoldPoints();++I)
                    {
                        const auto& M=C->GetManifoldPoint(I);const auto& P=M.ContactPoint;const auto& Result=C->GetManifoldPointResult(I);
                        auto J=MakeShared<FJsonObject>();J->SetArrayField(TEXT("point0"),V(FVec3(P.ShapeContactPoints[0])));
                        J->SetArrayField(TEXT("point1"),V(FVec3(P.ShapeContactPoints[1])));J->SetArrayField(TEXT("normal1"),V(FVec3(P.ShapeContactNormal)));
                        J->SetNumberField(TEXT("phi"),P.Phi);J->SetNumberField(TEXT("initialPhi"),M.InitialPhi);
                        J->SetBoolField(TEXT("disabled"),M.Flags.bDisabled);J->SetBoolField(TEXT("active"),C->IsManifoldPointActive(I));
                        J->SetBoolField(TEXT("initialContact"),M.Flags.bInitialContact);
                        J->SetBoolField(TEXT("hasAnchor"),M.Flags.bHasStaticFrictionAnchor);
                        J->SetBoolField(TEXT("restored"),M.Flags.bWasRestored);
                        J->SetArrayField(TEXT("anchor0"),V(FVec3(M.ShapeAnchorPoints[0])));
                        J->SetArrayField(TEXT("anchor1"),V(FVec3(M.ShapeAnchorPoints[1])));
                        J->SetBoolField(TEXT("resultValid"),Result.bIsValid);
                        J->SetArrayField(TEXT("pushOut"),V(FVec3(Result.NetPushOut)));J->SetArrayField(TEXT("impulse"),V(FVec3(Result.NetImpulse)));
                        Points.Add(MakeShared<FJsonValueObject>(J));
                    }
                    R->SetArrayField(TEXT("points"),Points);Contacts.Add(MakeShared<FJsonValueObject>(R));
                }
                Sample->SetArrayField(TEXT("contactsAfterSolve"),Contacts);
            }
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
