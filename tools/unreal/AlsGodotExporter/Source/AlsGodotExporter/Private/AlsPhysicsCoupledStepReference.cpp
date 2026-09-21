#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/ChaosPhysicalMaterial.h"
#include "Chaos/Collision/PBDCollisionContainerSolver.h"
#include "Chaos/Evolution/SolverBodyContainer.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDJointConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "Chaos/Utilities.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"

namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }
namespace AlsCoupledStepReference
{
using namespace Chaos;
FVec3 ReadV(const TSharedPtr<FJsonObject>& J,const TCHAR* Name)
{ const auto& A=J->GetArrayField(Name); return FVec3(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber()); }
FRotation3 ReadQ(const TArray<TSharedPtr<FJsonValue>>& A)
{ return FRotation3(FQuat(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber(),A[3]->AsNumber())); }
FRigidTransform3 ReadT(const TSharedPtr<FJsonObject>& J)
{ return FRigidTransform3(ReadV(J,TEXT("position")),ReadQ(J->GetArrayField(TEXT("rotation")))); }
FPBDJointSettings ReadJoint(const TSharedPtr<FJsonObject>& J)
{
    FPBDJointSettings S;
#define B(F) S.F=J->GetBoolField(TEXT(#F))
#define N(F) S.F=J->GetNumberField(TEXT(#F))
#define V(F) S.F=ReadV(J,TEXT(#F))
    B(bUseLinearSolver); B(bProjectionEnabled); B(bMassConditioningEnabled); B(bShockPropagationEnabled); B(bCollisionEnabled);
    B(bSoftLinearLimitsEnabled); B(bSoftTwistLimitsEnabled); B(bSoftSwingLimitsEnabled);
    B(bAngularTwistPositionDriveEnabled); B(bAngularTwistVelocityDriveEnabled); B(bAngularSwingPositionDriveEnabled); B(bAngularSwingVelocityDriveEnabled);
    B(bAngularSLerpPositionDriveEnabled); B(bAngularSLerpVelocityDriveEnabled);
    N(Stiffness); N(LinearLimit); N(LinearProjection); N(AngularProjection); N(TeleportDistance); N(TeleportAngle); N(ParentInvMassScale); N(ShockPropagation);
    N(SoftLinearStiffness); N(SoftLinearDamping); N(SoftTwistStiffness); N(SoftTwistDamping); N(SoftSwingStiffness); N(SoftSwingDamping);
    N(LinearRestitution); N(TwistRestitution); N(SwingRestitution); N(LinearContactDistance); N(TwistContactDistance); N(SwingContactDistance);
    V(AngularLimits); V(AngularDriveStiffness); V(AngularDriveDamping); V(AngularDriveMaxTorque); V(AngularDriveVelocityTarget);
    V(LinearDriveStiffness); V(LinearDriveDamping); V(LinearDriveMaxForce); V(LinearDrivePositionTarget); V(LinearDriveVelocityTarget);
    S.AngularDrivePositionTarget=ReadQ(J->GetArrayField(TEXT("AngularDrivePositionTarget")));
    S.AngularSoftForceMode=static_cast<EJointForceMode>(J->GetIntegerField(TEXT("AngularSoftForceMode")));
    S.AngularDriveForceMode=static_cast<EJointForceMode>(J->GetIntegerField(TEXT("AngularDriveForceMode")));
    for (int32 I=0;I<3;++I)
    {
        S.LinearMotionTypes[I]=static_cast<EJointMotionType>(J->GetArrayField(TEXT("LinearMotionTypes"))[I]->AsNumber());
        S.AngularMotionTypes[I]=static_cast<EJointMotionType>(J->GetArrayField(TEXT("AngularMotionTypes"))[I]->AsNumber());
        S.bLinearPositionDriveEnabled[I]=J->GetArrayField(TEXT("bLinearPositionDriveEnabled"))[I]->AsBool();
        S.bLinearVelocityDriveEnabled[I]=J->GetArrayField(TEXT("bLinearVelocityDriveEnabled"))[I]->AsBool();
    }
#undef B
#undef N
#undef V
    return S;
}
}

bool ExportAlsPhysicsCoupledStepReference(const FString& Inputs,const FString& Output,FString& Error)
{
    using namespace Chaos; using namespace AlsCoupledStepReference; using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if (Inputs.IsEmpty() || Output.IsEmpty() || FPaths::IsRelative(Output) || IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Coupled reference needs input directories and a new absolute output file."));
    TArray<FString> Directories,Files; Inputs.ParseIntoArray(Directories,TEXT("|"),true);
    for (const auto& Directory:Directories)
    {
        if (FPaths::IsRelative(Directory) || !IFileManager::Get().DirectoryExists(*Directory)) return Fail(TEXT("Input directory missing or relative."));
        TArray<FString> Found; IFileManager::Get().FindFilesRecursive(Found,*Directory,TEXT("*.json"),true,false); Files.Append(Found);
    }
    Files.Sort(); if (Files.IsEmpty() || Files.Num()>64) return Fail(TEXT("Expected 1..64 capture files."));
    for (const auto& Entry:TArray<TPair<FString,double>>{
        {TEXT("p.Chaos.PBDCollisionSolver.Position.StaticFriction.Stiffness"),.5},
        {TEXT("p.Chaos.PBDCollisionSolver.Velocity.StaticFriction.Stiffness"),1},
        {TEXT("p.Chaos.PBDCollisionSolver.Velocity.FrictionEnabled"),1},
        {TEXT("p.Chaos.PBDCollisionSolver.Velocity.AveragePointEnabled"),0},
        {TEXT("p.Chaos.Solver.Use1DFriction"),0}, {TEXT("p.Chaos.Joint.VelProjectionAlpha"),.1f}})
    {
        const auto* Var=IConsoleManager::Get().FindConsoleVariable(*Entry.Key);
        if (!Var || Var->GetFloat()!=Entry.Value) return Fail(TEXT("Coupled replay CVar differs from captured contract."));
    }
    auto Root=MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),1); Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native contact + cached joint containers share solver bodies; captured gathered rows/order/optional contact shock levels are inputs; no narrow phase, integration or sleeping; outputs never read from capture"));
    TArray<TSharedPtr<FJsonValue>> Cases;
    for (const auto& File:Files)
    {
        FString Text; TSharedPtr<FJsonObject> Capture;
        if (!FFileHelper::LoadFileToString(Text,*File) || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Capture) ||
            !Capture.IsValid() || Capture->GetIntegerField(TEXT("schemaVersion"))!=1) return Fail(TEXT("Invalid capture."));
        const auto Input=Capture->GetObjectField(TEXT("input")); const auto& BodyInputs=Input->GetArrayField(TEXT("bodies"));
        const auto& JointInputs=Input->GetArrayField(TEXT("joints")); const auto& ContactInputs=Input->GetArrayField(TEXT("contacts"));
        const double Dt=Input->GetNumberField(TEXT("dt")); const int32 Count=BodyInputs.Num();
        if (Count<2 || Count>256 || JointInputs.IsEmpty() || Dt<=0 ||
            Input->GetIntegerField(TEXT("positionIterations"))!=8 || Input->GetIntegerField(TEXT("velocityIterations"))!=2)
            return Fail(TEXT("Unsupported captured solve dimensions."));
        FParticleUniqueIndicesMultithreaded Unique; FPBDRigidsSOAs Particles(Unique); const auto P=Particles.CreateDynamicParticles(Count);
        FImplicitObjectPtr Box=MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-1),FVec3(1));
        for (int32 I=0;I<Count;++I)
        {
            P[I]->SetGeometry(Box); P[I]->SetX(FVec3(0)); P[I]->SetR(FRotation3::Identity);
            P[I]->SetCenterOfMass(FVec3(0)); P[I]->SetRotationOfMass(FRotation3::Identity);
            if (BodyInputs[I]->AsObject()->GetNumberField(TEXT("inverseMass"))==0) P[I]->SetObjectStateLowLevel(EObjectStateType::Kinematic);
        }
        FPBDJointConstraints Joints; Joints.SetUseLinearSolver(true); FPBDJointSolverSettings JointSettings;
        const auto S=Input->GetObjectField(TEXT("solverSettings"));
        JointSettings.bUseSimd=S->GetBoolField(TEXT("bUseSimd")); JointSettings.bSolvePositionLast=S->GetBoolField(TEXT("bSolvePositionLast"));
        JointSettings.MinSolverStiffness=S->GetNumberField(TEXT("MinSolverStiffness")); JointSettings.MaxSolverStiffness=S->GetNumberField(TEXT("MaxSolverStiffness"));
        JointSettings.AngleTolerance=S->GetNumberField(TEXT("AngleTolerance")); JointSettings.PositionTolerance=S->GetNumberField(TEXT("PositionTolerance"));
        JointSettings.MinParentMassRatio=S->GetNumberField(TEXT("MinParentMassRatio")); JointSettings.MaxInertiaRatio=S->GetNumberField(TEXT("MaxInertiaRatio"));
        JointSettings.bSortEnabled=S->GetBoolField(TEXT("bSortEnabled"));
        JointSettings.bUsePositionBasedDrives=S->GetBoolField(TEXT("bUsePositionBasedDrives"));
        JointSettings.SwingTwistAngleTolerance=S->GetNumberField(TEXT("SwingTwistAngleTolerance"));
        JointSettings.NumShockPropagationIterations=0; Joints.SetSettings(JointSettings);
        for (const auto& Value:JointInputs)
        {
            const auto J=Value->AsObject(); const int32 Parent=J->GetIntegerField(TEXT("parent")),Child=J->GetIntegerField(TEXT("child"));
            if (!P.IsValidIndex(Parent) || !P.IsValidIndex(Child)) return Fail(TEXT("Joint body index invalid."));
            auto Settings=ReadJoint(J->GetObjectField(TEXT("jointSettings")));
            Settings.ConnectorTransforms={ReadT(J->GetObjectField(TEXT("childFrame"))),ReadT(J->GetObjectField(TEXT("parentFrame")))};
            Joints.AddConstraint({P[Child],P[Parent]},Settings);
        }
        TArrayCollectionArray<bool> Collided; TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticleMaterials;
        Collided.Resize(Count); Materials.Resize(Count); PerParticleMaterials.Resize(Count);
        FPBDCollisionConstraints Collisions(Particles,Collided,Materials,PerParticleMaterials,nullptr);
        FPBDCollisionSolverSettings ContactSettings; ContactSettings.NumPositionShockPropagationIterations=ContactSettings.NumVelocityShockPropagationIterations=0;
        if(Input->HasField(TEXT("contactShock")))
        {
            const auto Shock=Input->GetObjectField(TEXT("contactShock"));
            ContactSettings.NumPositionShockPropagationIterations=Shock->GetIntegerField(TEXT("positionIterations"));
            ContactSettings.NumVelocityShockPropagationIterations=Shock->GetIntegerField(TEXT("velocityIterations"));
            for(const auto& Entry:TArray<TPair<FString,FString>>{
                {TEXT("p.Chaos.PBDCollisionSolver.Position.MinInvMassScale"),TEXT("positionScale")},
                {TEXT("p.Chaos.PBDCollisionSolver.Velocity.MinInvMassScale"),TEXT("velocityScale")}})
            {
                const auto* Var=IConsoleManager::Get().FindConsoleVariable(*Entry.Key);
                if(!Var||Var->GetFloat()!=float(Shock->GetNumberField(Entry.Value)))return Fail(TEXT("Captured shock settings differ from native CVars."));
            }
        }
        ContactSettings.NumPositionFrictionIterations=4; ContactSettings.NumVelocityFrictionIterations=1; Collisions.SetSolverSettings(ContactSettings);
        const_cast<FCollisionDetectorSettings&>(Collisions.GetDetectorSettings()).bDeferNarrowPhase=false;
        auto& Allocator=Collisions.GetConstraintAllocator(); Allocator.SetMaxContexts(1); Allocator.BeginDetectCollisions(); auto* Context=Allocator.GetContextAllocator(0);
        TArray<FPBDCollisionConstraintPtr> OwnedContacts;
        for (const auto& Value:ContactInputs)
        {
            const auto C=Value->AsObject(); const int32 A=C->GetIntegerField(TEXT("body0")),B=C->GetIntegerField(TEXT("body1"));
            if (!P.IsValidIndex(A) || !P.IsValidIndex(B)) return Fail(TEXT("Contact body index invalid."));
            auto Constraint=Context->CreateConstraint(P[A],Box.GetReference(),P[A]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
                P[B],Box.GetReference(),P[B]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,10,true,EContactShapesType::BoxBox);
            Context->ActivateConstraint(Constraint.Get()); const auto M=C->GetObjectField(TEXT("material"));
            Constraint->SetStaticFriction(M->GetNumberField(TEXT("staticFriction"))); Constraint->SetDynamicFriction(M->GetNumberField(TEXT("dynamicFriction")));
            Constraint->SetMinFrictionPushOut(M->GetNumberField(TEXT("minFrictionPushOut"))); Constraint->SetStiffness(M->GetNumberField(TEXT("stiffness")));
            Constraint->SetShapeWorldTransforms(FRigidTransform3::Identity,FRigidTransform3::Identity);
            // Reserve native manifold storage with harmless placeholders. After
            // native Gather, replace INPUT rows only and recompute native mass.
            const auto& Points=C->GetArrayField(TEXT("points"));
            for (int32 I=0;I<Points.Num();++I)
            {
                FContactPointf Point; Point.ShapeContactNormal=FVec3f(0,0,1); Point.Phi=-.1f;
                Point.ShapeContactPoints[0]=FVec3f(I,0,-.1f); Point.ShapeContactPoints[1]=FVec3f(I,0,0);
                Point.ContactType=EContactPointType::VertexPlane; Constraint->AddOneshotManifoldContact(Point);
            }
            OwnedContacts.Add(MoveTemp(Constraint));
        }
        Allocator.EndDetectCollisions(); auto ContactSolver=Collisions.CreateSceneSolver(0); ContactSolver->AddConstraints();
        auto JointSolver=Joints.CreateSceneSolver(1); JointSolver->AddConstraints(); FSolverBodyContainer Bodies; Bodies.Reset(Count);
        ContactSolver->AddBodies(Bodies); JointSolver->AddBodies(Bodies);
        TArray<FSolverBody> Unbound; Unbound.SetNum(Count); TArray<FSolverBody*> B; B.SetNum(Count);
        for (int32 I=0;I<Count;++I) B[I]=&Unbound[I];
        for (int32 I=0;I<Bodies.Num();++I) B[P.IndexOfByKey(Bodies.GetParticle(I))]=&Bodies.GetSolverBody(I);
        for (int32 I=0;I<Count;++I)
        {
            const auto In=BodyInputs[I]->AsObject(); auto& Body=*B[I]; Body=FSolverBody::MakeInitialized();
            const auto Initial=ReadT(In->GetObjectField(TEXT("initial"))),Predicted=ReadT(In->GetObjectField(TEXT("predicted")));
            Body.SetX(Initial.GetTranslation()); Body.SetR(Initial.GetRotation()); Body.SetP(Predicted.GetTranslation()); Body.SetQ(Predicted.GetRotation());
            Body.SetInvM(In->GetNumberField(TEXT("inverseMass"))); const_cast<FSolverVec3&>(Body.InvILocal())=FSolverVec3(ReadV(In,TEXT("inverseInertia")));
            Body.SetInvI(Utilities::ComputeWorldSpaceInertia(Body.Q(),FVec3(Body.InvILocal())));
            Body.SetV(ReadV(In,TEXT("v"))); Body.SetW(ReadV(In,TEXT("w")));
            if(Input->HasField(TEXT("contactShock")))Body.SetLevel(In->GetIntegerField(TEXT("level")));
        }
        ContactSolver->GatherInput(Dt); JointSolver->GatherInput(Dt);
        auto* Container=static_cast<FPBDCollisionContainerSolver*>(ContactSolver.Get());
        if (Container->GetNumConstraints()!=ContactInputs.Num()) return Fail(TEXT("Native contact count changed."));
        for (int32 I=0;I<ContactInputs.Num();++I)
        {
            auto& Native=const_cast<Private::FPBDCollisionSolver&>(Container->GetConstraintSolver(I)); const auto C=ContactInputs[I]->AsObject();
            if (&Native.SolverBody0().SolverBody()!=B[C->GetIntegerField(TEXT("body0"))] ||
                &Native.SolverBody1().SolverBody()!=B[C->GetIntegerField(TEXT("body1"))]) return Fail(TEXT("Native contact order changed."));
            const auto& Points=C->GetArrayField(TEXT("points")); if (Native.NumManifoldPoints()!=Points.Num()) return Fail(TEXT("Native point count changed."));
            for (int32 J=0;J<Points.Num();++J)
            {
                const auto In=Points[J]->AsObject(); const auto E=ReadV(In,TEXT("error"));
                Native.InitManifoldPoint(J,Dt,FSolverVec3(ReadV(In,TEXT("arm0"))),FSolverVec3(ReadV(In,TEXT("arm1"))),
                    FSolverVec3(ReadV(In,TEXT("normal"))),FSolverVec3(ReadV(In,TEXT("u"))),FSolverVec3(ReadV(In,TEXT("v"))),E.X,E.Y,E.Z,
                    In->GetNumberField(TEXT("targetVelocity")),In->GetBoolField(TEXT("disablePosition")),In->GetBoolField(TEXT("disableVelocity")),In->GetBoolField(TEXT("disableFriction")),false);
            }
            Native.FinalizeManifold();
        }
        TArray<TSharedPtr<FJsonValue>> Samples;
        const auto Snapshot=[&](const TCHAR* Stage,int32 Iteration)
        {
            auto Sample=MakeShared<FJsonObject>(); Sample->SetStringField(TEXT("stage"),Stage); Sample->SetNumberField(TEXT("iteration"),Iteration);
            TArray<TSharedPtr<FJsonValue>> States;
            for (auto* Body:B)
            {
                auto O=MakeShared<FJsonObject>(); O->SetObjectField(TEXT("predicted"),T(FTransform(Body->Q(),Body->P())));
                O->SetArrayField(TEXT("dp"),V(FVec3(Body->DP()))); O->SetArrayField(TEXT("dq"),V(FVec3(Body->DQ())));
                O->SetArrayField(TEXT("v"),V(FVec3(Body->V()))); O->SetArrayField(TEXT("w"),V(FVec3(Body->W()))); States.Add(MakeShared<FJsonValueObject>(O));
            }
            Sample->SetArrayField(TEXT("bodies"),States); Samples.Add(MakeShared<FJsonValueObject>(Sample));
        };
        for (int32 I=0;I<8;++I) { ContactSolver->ApplyPositionConstraints(Dt,I,8); Snapshot(TEXT("position_contacts"),I); JointSolver->ApplyPositionConstraints(Dt,I,8); Snapshot(TEXT("position_joints"),I); }
        for (auto* Body:B) Body->SetImplicitVelocity(Dt); Snapshot(TEXT("implicit"),0);
        for (int32 I=0;I<2;++I) { ContactSolver->ApplyVelocityConstraints(Dt,I,2); Snapshot(TEXT("velocity_contacts"),I); JointSolver->ApplyVelocityConstraints(Dt,I,2); Snapshot(TEXT("velocity_joints"),I); }
        for (auto* Body:B) Body->ApplyCorrections(); Snapshot(TEXT("projection_input"),0);
        JointSolver->PreApplyProjectionConstraints(Dt); JointSolver->ApplyProjectionConstraints(Dt,0,1); Snapshot(TEXT("projection"),0);
        for (auto* Body:B) Body->ApplyCorrections(); Snapshot(TEXT("corrected"),0);
        auto Case=MakeShared<FJsonObject>(); Case->SetObjectField(TEXT("capture"),Capture); Case->SetArrayField(TEXT("nativeSamples"),Samples); Cases.Add(MakeShared<FJsonValueObject>(Case));
    }
    Root->SetArrayField(TEXT("cases"),Cases); FString Text;
    if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Text)) || !FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
        return Fail(TEXT("Could not write coupled reference."));
    return true;
}
