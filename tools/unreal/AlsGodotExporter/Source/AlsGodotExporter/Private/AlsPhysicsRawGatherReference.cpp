#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/Sphere.h"
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

namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); }
namespace AlsCoupledStepReference {
Chaos::FVec3 ReadV(const TSharedPtr<FJsonObject>& J,const TCHAR* Name);
Chaos::FRigidTransform3 ReadT(const TSharedPtr<FJsonObject>& J);
}

bool ExportAlsPhysicsRawGatherReference(const FString& Inputs,const FString& Output,FString& Error)
{
    using namespace Chaos; using namespace AlsCoupledStepReference; using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Inputs.IsEmpty()||Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Raw Gather needs capture directories and a new absolute output."));
    for(const auto& Entry:TArray<TPair<FString,double>>{
        {TEXT("p.Chaos.PBDCollisionSolver.EnableInitialDepenetration"),1},
        {TEXT("p.Chaos.PBDCollisionSolver.RestitutionUsePreIntegrateVelocity"),0},
        {TEXT("p.Chaos.Solver.Use1DFriction"),0}})
    {
        const auto* Var=IConsoleManager::Get().FindConsoleVariable(*Entry.Key);
        if(!Var||Var->GetFloat()!=Entry.Value)return Fail(TEXT("Raw Gather CVar differs."));
    }
    TArray<FString> Directories,Files; Inputs.ParseIntoArray(Directories,TEXT("|"),true);
    for(const auto& Directory:Directories)
    {
        if(FPaths::IsRelative(Directory)||!IFileManager::Get().DirectoryExists(*Directory))return Fail(TEXT("Capture directory missing."));
        TArray<FString> Found; IFileManager::Get().FindFilesRecursive(Found,*Directory,TEXT("*.json"),true,false);Files.Append(Found);
    }
    Files.Sort();if(Files.IsEmpty()||Files.Num()>64)return Fail(TEXT("Expected 1..64 captures."));
    TArray<TSharedPtr<FJsonValue>> Cases;
    for(const auto& File:Files)
    {
        FString Text;TSharedPtr<FJsonObject> Capture;
        if(!FFileHelper::LoadFileToString(Text,*File)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Capture)||
            !Capture.IsValid()||Capture->GetIntegerField(TEXT("schemaVersion"))!=1)return Fail(TEXT("Invalid capture."));
        const auto Input=Capture->GetObjectField(TEXT("input"));const auto& BodiesIn=Input->GetArrayField(TEXT("bodies"));int32 PairIndex=0;
        for(const auto& Value:Input->GetArrayField(TEXT("contacts")))
        {
            const auto C=Value->AsObject();
            if(!C->HasTypedField<EJson::Object>(TEXT("gather")))return Fail(TEXT("Capture lacks raw Gather input."));
            const auto G=C->GetObjectField(TEXT("gather")),S=G->GetObjectField(TEXT("settings"));
            const auto& Points=G->GetArrayField(TEXT("points"));const float Dt=S->GetNumberField(TEXT("dt"));
            const int32 Indices[2]={C->GetIntegerField(TEXT("body0")),C->GetIntegerField(TEXT("body1"))};
            if(!BodiesIn.IsValidIndex(Indices[0])||!BodiesIn.IsValidIndex(Indices[1])||Points.IsEmpty()||Points.Num()>8||
                Dt<=0||Dt!=float(Input->GetNumberField(TEXT("dt")))||S->GetNumberField(TEXT("maxDepenetrationVelocity"))<0)
                return Fail(TEXT("Unsupported raw Gather dimensions or settings."));
            TSharedPtr<FJsonObject> RawBodies[2]={G->GetObjectField(TEXT("body0")),G->GetObjectField(TEXT("body1"))};
            FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);const auto P=Particles.CreateDynamicParticles(2);
            // No narrow phase runs here. Surrogates select the captured native
            // per-contact/global initial-overlap policy, checked after Setup.
            FImplicitObjectPtr Box=MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-1),FVec3(1));
            FImplicitObjectPtr Sphere=MakeImplicitObjectPtr<TSphere<FReal,3>>(FVec3(0),1);
            for(int32 I=0;I<2;++I)
            {
                P[I]->SetGeometry(S->GetBoolField(TEXT("perContactInitialPhi"))?Sphere:Box);
                P[I]->SetX(FVec3(0));P[I]->SetR(FRotation3::Identity);P[I]->SetCenterOfMass(FVec3(0));P[I]->SetRotationOfMass(FRotation3::Identity);
                if(RawBodies[I]->GetNumberField(TEXT("inverseMass"))==0)P[I]->SetObjectStateLowLevel(EObjectStateType::Kinematic);
                P[I]->SetInitialOverlapDepenetrationVelocity(S->GetNumberField(TEXT("maxDepenetrationVelocity")));
            }
            TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
            TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
            FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
            FPBDCollisionSolverSettings Settings;Settings.MaxPushOutVelocity=S->GetNumberField(TEXT("maxPushOutVelocity"));
            Settings.NumPositionShockPropagationIterations=Settings.NumVelocityShockPropagationIterations=0;Constraints.SetSolverSettings(Settings);
            const_cast<FCollisionDetectorSettings&>(Constraints.GetDetectorSettings()).bDeferNarrowPhase=false;
            auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();auto* Context=Allocator.GetContextAllocator(0);
            auto Constraint=Context->CreateConstraint(P[0],P[0]->GetGeometry(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
                P[1],P[1]->GetGeometry(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,10,true,
                S->GetBoolField(TEXT("perContactInitialPhi"))?EContactShapesType::SphereSphere:EContactShapesType::BoxBox);
            Context->ActivateConstraint(Constraint.Get());
            if(Constraint->UsePerContactInitialPhi()!=S->GetBoolField(TEXT("perContactInitialPhi"))||
                Constraint->GetInitialOverlapDepenetrationVelocity()!=float(S->GetNumberField(TEXT("maxDepenetrationVelocity"))))
                return Fail(TEXT("Native initial overlap policy differs."));
            Constraint->SetShapeWorldTransforms(ReadT(RawBodies[0]->GetObjectField(TEXT("shapeWorld"))),ReadT(RawBodies[1]->GetObjectField(TEXT("shapeWorld"))));
            Constraint->SetIsInitialContact(S->GetBoolField(TEXT("initialManifold")));
            Constraint->SetRestitution(S->GetNumberField(TEXT("restitution")));Constraint->SetRestitutionThreshold(S->GetNumberField(TEXT("restitutionThreshold")));
            const auto Material=C->GetObjectField(TEXT("material"));Constraint->SetStaticFriction(Material->GetNumberField(TEXT("staticFriction")));
            Constraint->SetDynamicFriction(Material->GetNumberField(TEXT("dynamicFriction")));Constraint->SetStiffness(Material->GetNumberField(TEXT("stiffness")));
            Constraint->SetMinFrictionPushOut(Material->GetNumberField(TEXT("minFrictionPushOut")));
            for(int32 I=0;I<Points.Num();++I)
            {
                const auto R=Points[I]->AsObject();FContactPointf Point;
                Point.ShapeContactPoints[0]=FVec3f(ReadV(R,TEXT("point0")));Point.ShapeContactPoints[1]=FVec3f(ReadV(R,TEXT("point1")));
                Point.ShapeContactNormal=FVec3f(ReadV(R,TEXT("normal1")));Point.Phi=0;Point.ContactType=EContactPointType::VertexPlane;
                Constraint->AddOneshotManifoldContact(Point);auto& M=Constraint->GetManifoldPoint(I);
                M.ShapeAnchorPoints[0]=FVec3f(ReadV(R,TEXT("anchor0")));M.ShapeAnchorPoints[1]=FVec3f(ReadV(R,TEXT("anchor1")));
                M.Flags.bHasStaticFrictionAnchor=R->GetBoolField(TEXT("hasAnchor"));M.Flags.bInitialContact=R->GetBoolField(TEXT("initialContact"));
                M.InitialPhi=R->GetNumberField(TEXT("initialPhi"));M.TargetPhi=R->GetNumberField(TEXT("targetPhi"));
                Constraint->SetManifoldPointPositionSolveEnabled(I,!R->GetBoolField(TEXT("disablePosition")));
                Constraint->SetManifoldPointVelocitySolveEnabled(I,!R->GetBoolField(TEXT("disableVelocity")));
                Constraint->SetManifoldPointFrictionEnabled(I,!R->GetBoolField(TEXT("disableFriction")));
            }
            // Seed the previous manifold minimum through the public history API;
            // restore the current point's phi before Gather. No private writes.
            Constraint->ResetSolverResults();auto& First=Constraint->GetManifoldPoint(0);const float Phi=First.InitialPhi;
            First.InitialPhi=S->GetNumberField(TEXT("minInitialPhi"));Constraint->SetSolverResults(0,FVec3f(0),FVec3f(0),0,Dt);First.InitialPhi=Phi;
            if(Constraint->GetMinInitialPhi()!=float(S->GetNumberField(TEXT("minInitialPhi"))))return Fail(TEXT("Initial phi seed differs."));
            Allocator.EndDetectCollisions();auto Solver=Constraints.CreateSceneSolver(0);Solver->AddConstraints();FSolverBodyContainer Bodies;Bodies.Reset(2);Solver->AddBodies(Bodies);
            if(Bodies.Num()!=2)return Fail(TEXT("Native Gather body count differs."));
            for(int32 I=0;I<2;++I)
            {
                const int32 Index=Bodies.GetParticle(I)==P[0]?0:1;const auto Raw=RawBodies[Index],BodyIn=BodiesIn[Indices[Index]]->AsObject();
                auto& Body=Bodies.GetSolverBody(I);Body=FSolverBody::MakeInitialized();const auto Pose=ReadT(BodyIn->GetObjectField(TEXT("predicted")));
                if(ReadV(Raw,TEXT("centerOfMass"))!=Pose.GetTranslation()||float(BodyIn->GetNumberField(TEXT("inverseMass")))!=float(Raw->GetNumberField(TEXT("inverseMass"))))
                    return Fail(TEXT("Capture body binding differs."));
                Body.SetX(Pose.GetTranslation());Body.SetP(Pose.GetTranslation());Body.SetR(Pose.GetRotation());Body.SetQ(Pose.GetRotation());
                Body.SetInvM(Raw->GetNumberField(TEXT("inverseMass")));const_cast<FSolverVec3&>(Body.InvILocal())=FSolverVec3(ReadV(BodyIn,TEXT("inverseInertia")));
                Body.SetInvI(Utilities::ComputeWorldSpaceInertia(Body.Q(),FVec3(Body.InvILocal())));Body.SetV(ReadV(Raw,TEXT("v")));Body.SetW(ReadV(Raw,TEXT("w")));
            }
            Solver->GatherInput(Dt);const auto& Native=static_cast<FPBDCollisionContainerSolver*>(Solver.Get())->GetConstraintSolver(0);
            if(Native.NumManifoldPoints()!=Points.Num())return Fail(TEXT("Native Gather point count differs."));
            TArray<TSharedPtr<FJsonValue>> Out;
            for(int32 I=0;I<Points.Num();++I)
            {
                const auto& Pnt=Native.GetManifoldPoint(I);auto O=MakeShared<FJsonObject>();
                O->SetArrayField(TEXT("arm0"),V(FVec3(Pnt.RelativeContactPoints[0])));O->SetArrayField(TEXT("arm1"),V(FVec3(Pnt.RelativeContactPoints[1])));
                O->SetArrayField(TEXT("normal"),V(FVec3(Pnt.ContactNormal)));O->SetArrayField(TEXT("u"),V(FVec3(Pnt.ContactTangentU)));O->SetArrayField(TEXT("v"),V(FVec3(Pnt.ContactTangentV)));
                O->SetArrayField(TEXT("error"),V(FVec3(Pnt.ContactDeltaNormal,Pnt.ContactDeltaTangentU,Pnt.ContactDeltaTangentV)));
                O->SetNumberField(TEXT("targetVelocity"),Pnt.ContactTargetVelocityNormal);O->SetNumberField(TEXT("initialPhi"),Constraint->GetManifoldPoint(I).InitialPhi);
                O->SetBoolField(TEXT("disablePosition"),Pnt.Flags.bDisablePositionSolve);O->SetBoolField(TEXT("disableVelocity"),Pnt.Flags.bDisableVelocitySolve);O->SetBoolField(TEXT("disableFriction"),Pnt.Flags.bDisableFriction);
                Out.Add(MakeShared<FJsonValueObject>(O));
            }
            auto Case=MakeShared<FJsonObject>();Case->SetStringField(TEXT("mesh"),Input->GetStringField(TEXT("mesh")));Case->SetNumberField(TEXT("frame"),Input->GetNumberField(TEXT("frame")));
            Case->SetNumberField(TEXT("pair"),PairIndex++);Case->SetObjectField(TEXT("capture"),C);Case->SetArrayField(TEXT("nativePoints"),Out);Cases.Add(MakeShared<FJsonValueObject>(Case));
        }
    }
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native GatherInput from captured post-history raw geometry, anchors, overlap state and bodies; surrogate shapes select verified overlap policy; no native narrow phase or history matching claim"));
    Root->SetArrayField(TEXT("cases"),Cases);FString Text;
    return (FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Text))&&FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))||Fail(TEXT("Raw Gather write failed."));
}
