#include "AlsPhysicsAssetExport.h"
#include "Chaos/Evolution/SolverBodyContainer.h"
#include "Chaos/Evolution/SolverConstraintContainer.h"
#include "Chaos/PBDJointConstraints.h"
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

bool ExportAlsPhysicsProjectionReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Projection reference requires a new absolute output file."));
    const auto* VelocityAlpha=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Joint.VelProjectionAlpha"));
    const auto* Exponential=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.ExponentialMapForRotationIntegration"));
    if(!VelocityAlpha||!Exponential)return Fail(TEXT("Missing projection/rotation CVar."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("coordinates"),TEXT("UE COM world poses; cm, kg, radians; inverse inertia 1/(kg*cm^2)"));
    Root->SetStringField(TEXT("observation"),TEXT("Engine-created joint container solver, native projection phase only, synthetic locked-linear pairs; no copied solver formula or saved assets"));
    Root->SetNumberField(TEXT("velocityProjectionAlpha"),VelocityAlpha->GetFloat());
    Root->SetBoolField(TEXT("exponentialRotationIntegration"),Exponential->GetBool());
    Root->SetBoolField(TEXT("linearContainerSolver"),true);
    TArray<TSharedPtr<FJsonValue>> Rows;
    // Calls through the exported container factory and virtual solver API keep
    // the implementation in Chaos.dll; the cached solver itself is not exported.
    const auto Run=[&](int32 Hz,int32 Axis,double Offset,bool Rotated,bool ParentCorrection,int32 Mode)
    {
        FParticleUniqueIndicesMultithreaded UniqueIndices;FPBDRigidsSOAs Particles(UniqueIndices);
        const auto Pair=Particles.CreateDynamicParticles(2);
        for(auto* P:Pair){P->SetX(FVec3(0));P->SetR(FRotation3::Identity);P->SetCenterOfMass(FVec3(0));P->SetRotationOfMass(FRotation3::Identity);}
        const FRotation3 ParentQ=Rotated?FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.7):FRotation3::Identity;
        const FRotation3 ChildQ=Rotated?FRotation3::FromAxisAngle(FVec3(2,-1,1).GetSafeNormal(),-.4):FRotation3::Identity;
        const FRotation3 FrameQ=Rotated?FRotation3::FromAxisAngle(FVec3(1,0,0),.2):FRotation3::Identity;
        const FVec3 ParentP(100,20,-10),Arm0(10,3,5),Arm1(-5,12,7);
        const FRigidTransform3 Frame0(Arm0,FrameQ),Frame1(Arm1,FRotation3::Identity);
        FVec3 Displacement(0);Displacement[Axis]=Offset;
        const FVec3 ChildP=ParentP+ParentQ*Arm0+(ParentQ*FrameQ)*Displacement-ChildQ*Arm1;
        FPBDJointConstraints Constraints;FPBDJointSolverSettings Settings;Settings.bUseSimd=true;
        Constraints.SetUseLinearSolver(true);Constraints.SetSettings(Settings);
        FPBDJointSettings Joint;Joint.bUseLinearSolver=true;Joint.bProjectionEnabled=Mode!=1;
        Joint.LinearMotionTypes=TVec3<EJointMotionType>(EJointMotionType::Locked);
        Joint.AngularMotionTypes=TVec3<EJointMotionType>(EJointMotionType::Free);
        Joint.LinearProjection=Mode==2?0:Mode==3?.5:1;Joint.AngularProjection=0;
        Joint.TeleportDistance=Mode==4?0:5;Joint.TeleportAngle=UE_PI;
        Joint.ConnectorTransforms={Frame1,Frame0};
        Constraints.AddConstraint({Pair[1],Pair[0]},Joint);
        auto Solver=Constraints.CreateSceneSolver(0);Solver->AddConstraints();
        FSolverBodyContainer Bodies;Bodies.Reset(2);Solver->AddBodies(Bodies);
        if(Solver->GetNumConstraints()!=1||Bodies.Num()!=2)return false;
        FSolverBody* Parent=nullptr;FSolverBody* Child=nullptr;
        for(int32 I=0;I<Bodies.Num();++I)
        {
            auto& B=Bodies.GetSolverBody(I);B=FSolverBody::MakeInitialized();
            const bool IsParent=Bodies.GetParticle(I)==Pair[0];
            const auto P=IsParent?ParentP:ChildP;const auto Q=IsParent?ParentQ:ChildQ;
            const FVec3 InvI=IsParent?FVec3(.003,.005,.002):FVec3(.004,.001,.002);
            B.SetX(P);B.SetP(P);B.SetR(Q);B.SetQ(Q);B.SetInvM(IsParent?.1:Mode==5?0:.2);
            // SetInvILocal calls a non-exported method. This synthetic fixture
            // owns a mutable body; initialize its exposed storage and world
            // tensor explicitly, without copying any projection implementation.
            const_cast<FSolverVec3&>(B.InvILocal())=FSolverVec3(InvI);
            B.SetInvI(Utilities::ComputeWorldSpaceInertia(Q,FVec3(B.InvILocal())));
            B.SetV(IsParent?FVec3(1,2,3):FVec3(-1,-2,3));B.SetW(FVec3(.1,.2,.3));
            if(IsParent)Parent=&B;else Child=&B;
        }
        if(!Parent||!Child)return false;
        const double Dt=static_cast<double>(1.f/Hz);
        Solver->GatherInput(Dt);Solver->PreApplyProjectionConstraints(Dt);
        // Represents another joint's correction after all joints cached their
        // projection geometry. Parent must stay unchanged by this joint.
        const FVec3 ParentDP=ParentCorrection?FVec3(.2,-.3,.1):FVec3(0);
        const FVec3 ParentDQ=ParentCorrection?FVec3(.002,.003,-.001):FVec3(0);
        Parent->SetDP(FSolverVec3(ParentDP));Parent->SetDQ(FSolverVec3(ParentDQ));
        Solver->ApplyProjectionConstraints(Dt,0,1);
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("hz"),Hz);Row->SetNumberField(TEXT("dt"),Dt);
        Row->SetNumberField(TEXT("axis"),Axis);Row->SetNumberField(TEXT("offsetCm"),Offset);Row->SetNumberField(TEXT("mode"),Mode);
        Row->SetBoolField(TEXT("rotated"),Rotated);Row->SetBoolField(TEXT("parentCorrection"),ParentCorrection);
        Row->SetBoolField(TEXT("enabled"),Joint.bProjectionEnabled);Row->SetNumberField(TEXT("linearAlpha"),Joint.LinearProjection);
        Row->SetNumberField(TEXT("teleportDistanceCm"),Joint.TeleportDistance);
        Row->SetNumberField(TEXT("stiffness"),Joint.Stiffness);Row->SetNumberField(TEXT("childInverseMass"),Child->InvM());
        Row->SetArrayField(TEXT("childInverseInertia"),V(FVec3(Child->InvILocal())));
        Row->SetObjectField(TEXT("parentPose"),T(FTransform(ParentQ,ParentP)));
        Row->SetObjectField(TEXT("childPose"),T(FTransform(ChildQ,ChildP)));
        Row->SetObjectField(TEXT("parentFrame"),T(FTransform(FrameQ,Arm0)));
        Row->SetObjectField(TEXT("childFrame"),T(FTransform(FQuat::Identity,Arm1)));
        Row->SetArrayField(TEXT("parentDP"),V(ParentDP));Row->SetArrayField(TEXT("parentDQ"),V(ParentDQ));
        Row->SetArrayField(TEXT("resultParentDP"),V(FVec3(Parent->DP())));Row->SetArrayField(TEXT("resultParentDQ"),V(FVec3(Parent->DQ())));
        Row->SetArrayField(TEXT("resultChildDP"),V(FVec3(Child->DP())));Row->SetArrayField(TEXT("resultChildDQ"),V(FVec3(Child->DQ())));
        Row->SetArrayField(TEXT("resultChildV"),V(FVec3(Child->V())));Row->SetArrayField(TEXT("resultChildW"),V(FVec3(Child->W())));
        Row->SetObjectField(TEXT("resultChildPose"),T(FTransform(Child->CorrectedQ(),Child->CorrectedP())));
        Rows.Add(MakeShared<FJsonValueObject>(Row));return true;
    };
    for(const int32 Hz:{30,60,120})for(const int32 Axis:{0,1,2})
    for(const double Offset:{-30.,-5.1,-5.,0.,5.,5.1,30.})
    for(const bool Rotated:{false,true})for(const bool Correction:{false,true})
        if(!Run(Hz,Axis,Offset,Rotated,Correction,0))return Fail(TEXT("Native projection fixture failed to bind."));
    for(int32 Mode=1;Mode<=5;++Mode)for(const double Offset:{1.,30.})
        if(!Run(60,1,Offset,true,true,Mode))return Fail(TEXT("Native projection control failed to bind."));
    Root->SetArrayField(TEXT("cases"),Rows);
    FString Json;if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json)))return Fail(TEXT("Projection serialization failed."));
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("Projection reference write failed."));
}
