#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/ChaosPhysicalMaterial.h"
#include "Chaos/Collision/PBDCollisionContainerSolver.h"
#include "Chaos/Evolution/SolverBodyContainer.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "Chaos/Utilities.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"

namespace AlsJointSolverReference
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& P);
TSharedRef<FJsonObject> T(const FTransform& P);
}

bool ExportAlsPhysicsContactReference(const FString& Output,FString& Error,bool GatherGeometry)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Contact reference requires a new absolute file."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("coordinates"),TEXT("UE world COM contact offsets; cm, kg, radians; normal points from body1 to body0"));
    Root->SetStringField(TEXT("observation"),TEXT("Native Gauss-Seidel collision container, synthetic persistent manifolds; gather cache and 8 position + implicit velocity + 2 velocity iterations; no narrow phase, integration, split impulse, shock propagation or saved assets"));
    auto Vars=MakeShared<FJsonObject>();
    for(const auto& Entry:TArray<TPair<FString,double>>{
        {TEXT("p.Chaos.PBDCollisionSolver.Position.SolveEnabled"),1},
        {TEXT("p.Chaos.PBDCollisionSolver.Velocity.SolveEnabled"),1},
        {TEXT("p.Chaos.PBDCollisionSolver.Position.StaticFriction.Stiffness"),.5},
        {TEXT("p.Chaos.PBDCollisionSolver.Velocity.StaticFriction.Stiffness"),1},
        {TEXT("p.Chaos.PBDCollisionSolver.Velocity.FrictionEnabled"),1},
        {TEXT("p.Chaos.PBDCollisionSolver.Velocity.AveragePointEnabled"),0},
        {TEXT("p.Chaos.Solver.Use1DFriction"),0}})
    {
        const auto* CVar=IConsoleManager::Get().FindConsoleVariable(*Entry.Key);
        if(!CVar||CVar->GetFloat()!=Entry.Value)return Fail(TEXT("Contact solver CVar contract differs."));
        Vars->SetNumberField(Entry.Key,CVar->GetFloat());
    }
    Root->SetObjectField(TEXT("cvars"),Vars);TArray<TSharedPtr<FJsonValue>> Rows;
    if(GatherGeometry)
    {
        Root->SetNumberField(TEXT("schemaVersion"),2);
        for(const auto& Entry:TArray<TPair<FString,double>>{
            {TEXT("p.Chaos.PBDCollisionSolver.EnableInitialDepenetration"),1},
            {TEXT("p.Chaos.PBDCollisionSolver.RestitutionUsePreIntegrateVelocity"),0}})
        {
            const auto* CVar=IConsoleManager::Get().FindConsoleVariable(*Entry.Key);
            if(!CVar||CVar->GetFloat()!=Entry.Value)return Fail(TEXT("Contact gather CVar contract differs."));
            Vars->SetNumberField(Entry.Key,CVar->GetFloat());
        }
    }
    for(int32 Hz:{30,60,120})for(int32 Mode=0;Mode<3;++Mode)for(int32 Count:{1,4})
    for(int32 Scenario=0;Scenario<(GatherGeometry?12:8);++Scenario)for(bool Seeded:{false,true})
    {
        const double Dt=static_cast<double>(1.f/Hz);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);const auto Pair=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr Box=MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-2),FVec3(2));
        for(auto* P:Pair){P->SetGeometry(Box);P->SetX(FVec3(0));P->SetR(FRotation3::Identity);P->SetCenterOfMass(FVec3(0));P->SetRotationOfMass(FRotation3::Identity);}
        if(GatherGeometry)
        {
            for(int32 I=0;I<2;++I)
            {
                Pair[I]->SetObjectStateLowLevel((Mode==1&&I==1)||(Mode==2&&I==0)?EObjectStateType::Kinematic:EObjectStateType::Dynamic);
                Pair[I]->SetInitialOverlapDepenetrationVelocity(Scenario>=10?.3f:0.f);
            }
        }
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticleMaterials;
        Collided.Resize(2);Materials.Resize(2);PerParticleMaterials.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticleMaterials,nullptr);
        FPBDCollisionSolverSettings Settings;Settings.NumPositionShockPropagationIterations=Settings.NumVelocityShockPropagationIterations=0;
        Settings.NumPositionFrictionIterations=4;Settings.NumVelocityFrictionIterations=1;
        Settings.MaxPushOutVelocity=Scenario==7?3:0;Constraints.SetSolverSettings(Settings);
        const_cast<FCollisionDetectorSettings&>(Constraints.GetDetectorSettings()).bDeferNarrowPhase=false;
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto Constraint=Context->CreateConstraint(Pair[0],Box.GetReference(),Pair[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            Pair[1],Box.GetReference(),Pair[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,10,true,EContactShapesType::BoxBox);
        // Activation resolves per-particle materials and resets modifications;
        // apply synthetic contact inputs only after that native initialization.
        Context->ActivateConstraint(Constraint.Get());
        Constraint->SetShapeWorldTransforms(FRigidTransform3::Identity,FRigidTransform3::Identity);
        const FVec3 Origin=GatherGeometry&&Seeded?FVec3(100000.123,-200000.456,300000.789):FVec3(0);
        if(GatherGeometry&&Seeded)
            Constraint->SetShapeWorldTransforms(
                FRigidTransform3(Origin+FVec3(.3,-.7,.1),FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.4)),
                FRigidTransform3(Origin,FRotation3::FromAxisAngle(FVec3(2,1,-1).GetSafeNormal(),-.8)));
        if(GatherGeometry)Constraint->SetIsInitialContact(!Seeded);
        Constraint->SetStaticFriction(Scenario==0?0:.6);Constraint->SetDynamicFriction(Scenario==0?0:.4);
        Constraint->SetMinFrictionPushOut(Scenario==3?.05:0);Constraint->SetStiffness(Seeded?.8:1);
        Constraint->SetRestitution(Scenario==2?.5:0);Constraint->SetRestitutionThreshold(2000);
        const FVec3f Normal=Scenario==8?FVec3f(0,1,0):Seeded?FVec3f(.3,.4,.8660254).GetSafeNormal():FVec3f(0,0,1);
        for(int32 I=0;I<Count;++I)
        {
            FContactPointf C;C.ShapeContactNormal=Normal;C.Phi=Scenario==3?.1f:-.2f;
            C.ShapeContactPoints[1]=FVec3f((I&1)?1:-1,(I&2)?1:-1,0);C.ShapeContactPoints[0]=C.ShapeContactPoints[1]+C.Phi*Normal;
            if(GatherGeometry&&Seeded)
            {
                const auto& S0=Constraint->GetShapeWorldTransform0();const auto& S1=Constraint->GetShapeWorldTransform1();
                C.ShapeContactPoints[0]=FVec3f(S0.InverseTransformPositionNoScale(
                    S1.TransformPositionNoScale(FVec3(C.ShapeContactPoints[1]))+C.Phi*S1.TransformVectorNoScale(FVec3(Normal))));
            }
            C.ContactType=EContactPointType::VertexPlane;Constraint->AddOneshotManifoldContact(C);
            auto& M=Constraint->GetManifoldPoint(I);M.Flags.bInitialContact=false;
            M.Flags.bHasStaticFrictionAnchor=Scenario==1;
            M.ShapeAnchorPoints[0]=C.ShapeContactPoints[0]+FVec3f(.001,.002,0);M.ShapeAnchorPoints[1]=C.ShapeContactPoints[1];
            if(GatherGeometry){M.Flags.bInitialContact=Scenario==10;M.InitialPhi=Scenario==11?-.15f:0;M.TargetPhi=Scenario==9?.02f:0;}
            Constraint->SetManifoldPointPositionSolveEnabled(I,Scenario!=4);
            Constraint->SetManifoldPointVelocitySolveEnabled(I,Scenario!=6);
            Constraint->SetManifoldPointFrictionEnabled(I,Scenario!=5);
        }
        Allocator.EndDetectCollisions();
        // AddConstraints performs Reset inside the Chaos DLL. Calling the inline
        // base Reset from a plugin would reference unexported partition helpers.
        auto Solver=Constraints.CreateSceneSolver(0);Solver->AddConstraints();
        FSolverBodyContainer Bodies;Bodies.Reset(2);Solver->AddBodies(Bodies);
        if(Bodies.Num()!=2||Solver->GetNumConstraints()!=1)return Fail(TEXT("Contact fixture body binding failed."));
        FSolverBody* B[2]={nullptr,nullptr};
        for(int32 I=0;I<2;++I)
        {
            auto& Body=Bodies.GetSolverBody(I);const int32 Index=Bodies.GetParticle(I)==Pair[0]?0:1;B[Index]=&Body;Body=FSolverBody::MakeInitialized();
            const FRotation3 Q=Seeded?FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),Index==0?.7:-.4):FRotation3::Identity;
            Body.SetX(FVec3(0,0,Index==0?1:-2));Body.SetP(Body.X());Body.SetR(Q);Body.SetQ(Q);
            if(GatherGeometry){Body.SetX(Origin+FVec3(0,0,Index==0?1:-2));Body.SetP(Body.X());}
            Body.SetInvM((Mode==1&&Index==1)||(Mode==2&&Index==0)?0:Index==0?.5:.2);
            const_cast<FSolverVec3&>(Body.InvILocal())=Index==0?FSolverVec3(.03,.02,.01):FSolverVec3(.01,.04,.02);
            Body.SetInvI(Utilities::ComputeWorldSpaceInertia(Q,FVec3(Body.InvILocal())));
            Body.SetV(Index==0?FVec3(Scenario==1?.01:30,Scenario==1?.02:4,-80):FVec3(2,-1,3));
            Body.SetW(Index==0?FVec3(.2,-.1,.3):FVec3(-.1,.05,.1));
            if(GatherGeometry&&(Scenario==8||Scenario>=10)){Body.SetV(FVec3(0));Body.SetW(FVec3(0));}
        }
        TSharedPtr<FJsonObject> Geometry;
        if(GatherGeometry)
        {
            Geometry=MakeShared<FJsonObject>();
            Geometry->SetObjectField(TEXT("shape0"),T(FTransform(Constraint->GetShapeWorldTransform0())));
            Geometry->SetObjectField(TEXT("shape1"),T(FTransform(Constraint->GetShapeWorldTransform1())));
            Geometry->SetNumberField(TEXT("restitution"),Constraint->GetRestitution());
            Geometry->SetNumberField(TEXT("threshold"),Constraint->GetRestitutionThreshold());
            Geometry->SetNumberField(TEXT("maxPushOutVelocity"),Settings.MaxPushOutVelocity);
            Geometry->SetNumberField(TEXT("maxDepenetrationVelocity"),Constraint->GetInitialOverlapDepenetrationVelocity());
            Geometry->SetBoolField(TEXT("perContactInitialPhi"),Constraint->UsePerContactInitialPhi());
            Geometry->SetBoolField(TEXT("initialManifold"),Constraint->IsInitialContact());
            Geometry->SetNumberField(TEXT("minInitialPhi"),Constraint->GetMinInitialPhi());
            TArray<TSharedPtr<FJsonValue>> Raw;
            for(int32 I=0;I<Count;++I)
            {
                const auto& M=Constraint->GetManifoldPoint(I);auto O=MakeShared<FJsonObject>();
                O->SetArrayField(TEXT("point0"),V(FVec3(M.ContactPoint.ShapeContactPoints[0])));
                O->SetArrayField(TEXT("point1"),V(FVec3(M.ContactPoint.ShapeContactPoints[1])));
                O->SetArrayField(TEXT("normal1"),V(FVec3(M.ContactPoint.ShapeContactNormal)));
                O->SetArrayField(TEXT("anchor0"),V(FVec3(M.ShapeAnchorPoints[0])));O->SetArrayField(TEXT("anchor1"),V(FVec3(M.ShapeAnchorPoints[1])));
                O->SetBoolField(TEXT("hasAnchor"),M.Flags.bHasStaticFrictionAnchor);O->SetBoolField(TEXT("initialContact"),M.Flags.bInitialContact);
                O->SetNumberField(TEXT("initialPhi"),M.InitialPhi);O->SetNumberField(TEXT("targetPhi"),M.TargetPhi);
                O->SetBoolField(TEXT("disablePosition"),M.Flags.bDisablePositionSolve);
                O->SetBoolField(TEXT("disableVelocity"),M.Flags.bDisableVelocitySolve);O->SetBoolField(TEXT("disableFriction"),M.Flags.bDisableFriction);
                Raw.Add(MakeShared<FJsonValueObject>(O));
            }
            Geometry->SetArrayField(TEXT("points"),Raw);
        }
        Solver->GatherInput(Dt);
        const auto* Container=static_cast<const FPBDCollisionContainerSolver*>(Solver.Get());
        const auto& Native=Container->GetConstraintSolver(0);
        if(Native.NumManifoldPoints()!=Count)return Fail(TEXT("Native contact count differs."));
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("hz"),Hz);Row->SetNumberField(TEXT("dt"),Dt);
        if(GatherGeometry)Row->SetObjectField(TEXT("geometry"),Geometry);
        Row->SetNumberField(TEXT("bodyMode"),Mode);Row->SetNumberField(TEXT("scenario"),Scenario);Row->SetBoolField(TEXT("seeded"),Seeded);
        Row->SetNumberField(TEXT("stiffness"),Constraint->GetStiffness());Row->SetNumberField(TEXT("staticFriction"),Native.GetStaticFriction());
        Row->SetNumberField(TEXT("dynamicFriction"),Native.GetDynamicFriction());Row->SetNumberField(TEXT("velocityFriction"),Native.GetVelocityFriction());
        Row->SetNumberField(TEXT("minFrictionPushOut"),Constraint->GetMinFrictionPushOut());
        TArray<TSharedPtr<FJsonValue>> Inputs,BodyInputs;
        for(int32 I=0;I<2;++I){auto O=MakeShared<FJsonObject>();O->SetObjectField(TEXT("pose"),T(FTransform(B[I]->Q(),B[I]->P())));
            O->SetNumberField(TEXT("inverseMass"),B[I]->InvM());O->SetArrayField(TEXT("inverseInertia"),V(FVec3(B[I]->InvILocal())));BodyInputs.Add(MakeShared<FJsonValueObject>(O));}
        Row->SetArrayField(TEXT("bodies"),BodyInputs);
        for(int32 I=0;I<Count;++I)
        {
            const auto& P=Native.GetManifoldPoint(I);auto O=MakeShared<FJsonObject>();
            O->SetArrayField(TEXT("arm0"),V(FVec3(P.RelativeContactPoints[0])));O->SetArrayField(TEXT("arm1"),V(FVec3(P.RelativeContactPoints[1])));
            O->SetArrayField(TEXT("normal"),V(FVec3(P.ContactNormal)));O->SetArrayField(TEXT("u"),V(FVec3(P.ContactTangentU)));O->SetArrayField(TEXT("v"),V(FVec3(P.ContactTangentV)));
            O->SetArrayField(TEXT("error"),V(FVec3(P.ContactDeltaNormal,P.ContactDeltaTangentU,P.ContactDeltaTangentV)));
            O->SetArrayField(TEXT("mass"),V(FVec3(P.ContactMassNormal,P.ContactMassTangentU,P.ContactMassTangentV)));
            O->SetNumberField(TEXT("targetVelocity"),P.ContactTargetVelocityNormal);
            if(GatherGeometry)O->SetNumberField(TEXT("initialPhi"),Constraint->GetManifoldPoint(I).InitialPhi);
            O->SetBoolField(TEXT("disablePosition"),P.Flags.bDisablePositionSolve);O->SetBoolField(TEXT("disableVelocity"),P.Flags.bDisableVelocitySolve);O->SetBoolField(TEXT("disableFriction"),P.Flags.bDisableFriction);
            Inputs.Add(MakeShared<FJsonValueObject>(O));
        }
        Row->SetArrayField(TEXT("points"),Inputs);
        if(Seeded)for(int32 I=0;I<2;++I)if(B[I]->IsDynamic()){B[I]->SetDP(FSolverVec3(.02,-.01,I==0?.04:-.03));B[I]->SetDQ(FSolverVec3(.01,-.02,.03));}
        const auto Snapshot=[&]()
        {
            auto S=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> States,Points;
            for(int32 I=0;I<2;++I){auto O=MakeShared<FJsonObject>();O->SetArrayField(TEXT("dp"),V(FVec3(B[I]->DP())));O->SetArrayField(TEXT("dq"),V(FVec3(B[I]->DQ())));
                O->SetArrayField(TEXT("v"),V(FVec3(B[I]->V())));O->SetArrayField(TEXT("w"),V(FVec3(B[I]->W())));States.Add(MakeShared<FJsonValueObject>(O));}
            for(int32 I=0;I<Count;++I){const auto& P=Native.GetManifoldPoint(I);auto O=MakeShared<FJsonObject>();
                O->SetArrayField(TEXT("pushOut"),V(FVec3(P.NetPushOutNormal,P.NetPushOutTangentU,P.NetPushOutTangentV)));
                O->SetArrayField(TEXT("impulse"),V(FVec3(P.NetImpulseNormal,P.NetImpulseTangentU,P.NetImpulseTangentV)));
                O->SetNumberField(TEXT("frictionRatio"),P.StaticFrictionRatio);Points.Add(MakeShared<FJsonValueObject>(O));}
            S->SetArrayField(TEXT("bodies"),States);S->SetArrayField(TEXT("points"),Points);return S;
        };
        Row->SetObjectField(TEXT("seed"),Snapshot());TArray<TSharedPtr<FJsonValue>> Positions,Velocities;
        for(int32 It=0;It<8;++It){Solver->ApplyPositionConstraints(Dt,It,8);Positions.Add(MakeShared<FJsonValueObject>(Snapshot()));}
        for(auto* Body:B)Body->SetImplicitVelocity(Dt);Row->SetObjectField(TEXT("implicit"),Snapshot());
        for(int32 It=0;It<2;++It){Solver->ApplyVelocityConstraints(Dt,It,2);Velocities.Add(MakeShared<FJsonValueObject>(Snapshot()));}
        Row->SetArrayField(TEXT("positionSamples"),Positions);Row->SetArrayField(TEXT("velocitySamples"),Velocities);Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Rows);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json)))return Fail(TEXT("Contact serialization failed."));
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("Contact reference write failed."));
}
