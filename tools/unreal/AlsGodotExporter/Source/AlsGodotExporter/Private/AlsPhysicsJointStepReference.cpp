#include "AlsPhysicsAssetExport.h"
#include "Chaos/Evolution/SolverBodyContainer.h"
#include "Chaos/Evolution/SolverConstraintContainer.h"
#include "Chaos/PBDJointConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "Chaos/Utilities.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"

namespace AlsJointReference { TSharedRef<FJsonObject> Settings(const Chaos::FPBDJointSettings& S); }
namespace AlsJointSolverReference
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& P);
TSharedRef<FJsonObject> T(const FTransform& P);
TSharedRef<FJsonObject> SolverSettings(const Chaos::FPBDJointSolverSettings& S);
}

bool ExportAlsPhysicsJointStepReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Joint step reference requires a new absolute file."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native cached joint container: 8 position iterations, implicit velocity, 2 velocity iterations; locked linear axes; no contact or projection; no saved assets"));
    Root->SetStringField(TEXT("coordinates"),TEXT("UE COM and connector poses; kg, cm, radians"));
    Root->SetBoolField(TEXT("linearContainerSolver"),true);
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(int32 Hz:{30,60,120})for(int32 BodyMode=0;BodyMode<4;++BodyMode)
    for(bool Condition:{false,true})for(bool Rotated:{false,true})for(bool Simd:{false,true})for(int32 AngularMode=0;AngularMode<3;++AngularMode)
    {
        const double Dt=static_cast<double>(1.f/Hz);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);const auto Pair=Particles.CreateDynamicParticles(2);
        for(auto* P:Pair){P->SetX(FVec3(0));P->SetR(FRotation3::Identity);P->SetCenterOfMass(FVec3(0));P->SetRotationOfMass(FRotation3::Identity);}
        const FRotation3 Q0=Rotated?FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.7):FRotation3::Identity;
        const FRotation3 Q1=Q0*FRotation3::FromAxisAngle(FVec3(0,1,0),.4)*FRotation3::FromAxisAngle(FVec3(1,0,0),-.6);
        const FRigidTransform3 Frames[2]={FRigidTransform3(FVec3(.5,-1,2),FRotation3::Identity),
            FRigidTransform3(FVec3(-2,1,.5),Rotated?FRotation3::FromAxisAngle(FVec3(0,0,1),.3):FRotation3::Identity)};
        FPBDJointConstraints Constraints;Constraints.SetUseLinearSolver(true);
        FPBDJointSolverSettings Settings;Settings.bUseSimd=Simd;Settings.MinSolverStiffness=Settings.MaxSolverStiffness=1;
        Settings.bSolvePositionLast=true;Constraints.SetSettings(Settings);
        FPBDJointSettings Joint;Joint.bUseLinearSolver=true;Joint.bMassConditioningEnabled=Condition;Joint.bProjectionEnabled=false;
        Joint.LinearMotionTypes=TVec3<EJointMotionType>(EJointMotionType::Locked);
        Joint.AngularMotionTypes=TVec3<EJointMotionType>(static_cast<EJointMotionType>(AngularMode));
        Joint.bSoftTwistLimitsEnabled=Joint.bSoftSwingLimitsEnabled=AngularMode==1;
        Joint.AngularLimits=FVec3(.2,.25,.3);Joint.SoftTwistStiffness=Joint.SoftSwingStiffness=5000000;
        Joint.SoftTwistDamping=Joint.SoftSwingDamping=5000;
        Joint.bAngularTwistPositionDriveEnabled=Joint.bAngularTwistVelocityDriveEnabled=AngularMode!=0;
        Joint.bAngularSwingPositionDriveEnabled=Joint.bAngularSwingVelocityDriveEnabled=AngularMode!=0;
        Joint.AngularDriveStiffness=FVec3(75);Joint.AngularDriveDamping=FVec3(1.5);
        Joint.ConnectorTransforms={Frames[1],Frames[0]};Constraints.AddConstraint({Pair[1],Pair[0]},Joint);
        auto Solver=Constraints.CreateSceneSolver(0);Solver->AddConstraints();FSolverBodyContainer Bodies;Bodies.Reset(2);Solver->AddBodies(Bodies);
        if(Bodies.Num()!=2||Solver->GetNumConstraints()!=1)return Fail(TEXT("Joint fixture binding failed."));
        FSolverBody* Parent=nullptr;FSolverBody* Child=nullptr;
        for(int32 I=0;I<2;++I)
        {
            auto& B=Bodies.GetSolverBody(I);B=FSolverBody::MakeInitialized();const bool P=Bodies.GetParticle(I)==Pair[0];
            const FRotation3 Q=P?Q0:Q1;const FVec3 Position=P?FVec3(10,20,30):FVec3(12,17,34);
            B.SetX(Position);B.SetP(Position);B.SetR(Q);B.SetQ(Q);
            B.SetInvM(P?(BodyMode==1||BodyMode==3?0:5):(BodyMode>=2?0:.2));
            const_cast<FSolverVec3&>(B.InvILocal())=FSolverVec3(P?FVec3(.03,.00002,.002):FVec3(.004,.001,.02));
            B.SetInvI(Utilities::ComputeWorldSpaceInertia(Q,FVec3(B.InvILocal())));
            B.SetV(P?FVec3(3,1,-2):FVec3(-2,4,.5));B.SetW(P?FVec3(.2,-.1,.3):FVec3(-.3,.1,.2));
            if(P)Parent=&B;else Child=&B;
        }
        if(!Parent||!Child)return Fail(TEXT("Missing bound solver body."));
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("hz"),Hz);Row->SetNumberField(TEXT("dt"),Dt);
        Row->SetNumberField(TEXT("bodyMode"),BodyMode);Row->SetNumberField(TEXT("angularMode"),AngularMode);
        Row->SetBoolField(TEXT("condition"),Condition);Row->SetBoolField(TEXT("rotated"),Rotated);Row->SetBoolField(TEXT("simd"),Simd);
        Row->SetObjectField(TEXT("jointSettings"),AlsJointReference::Settings(Joint));Row->SetObjectField(TEXT("solverSettings"),SolverSettings(Settings));
        Row->SetObjectField(TEXT("parentFrame"),T(FTransform(Frames[0])));Row->SetObjectField(TEXT("childFrame"),T(FTransform(Frames[1])));
        const auto State=[&](const FSolverBody& B)
        {
            auto S=MakeShared<FJsonObject>();S->SetObjectField(TEXT("pose"),T(FTransform(B.CorrectedQ(),B.CorrectedP())));
            S->SetArrayField(TEXT("dp"),V(FVec3(B.DP())));S->SetArrayField(TEXT("dq"),V(FVec3(B.DQ())));
            S->SetArrayField(TEXT("v"),V(FVec3(B.V())));S->SetArrayField(TEXT("w"),V(FVec3(B.W())));return S;
        };
        const auto Both=[&](){auto S=MakeShared<FJsonObject>();S->SetObjectField(TEXT("parent"),State(*Parent));S->SetObjectField(TEXT("child"),State(*Child));return S;};
        Row->SetObjectField(TEXT("initial"),Both());
        Row->SetNumberField(TEXT("parentInverseMass"),Parent->InvM());Row->SetNumberField(TEXT("childInverseMass"),Child->InvM());
        Row->SetArrayField(TEXT("parentInverseInertia"),V(FVec3(Parent->InvILocal())));Row->SetArrayField(TEXT("childInverseInertia"),V(FVec3(Child->InvILocal())));
        Solver->GatherInput(Dt);
        if(Rotated)
        {
            if(Parent->IsDynamic()){Parent->SetDP(FSolverVec3(.1,0,.2));Parent->SetDQ(FSolverVec3(.01,-.02,.03));}
            if(Child->IsDynamic()){Child->SetDP(FSolverVec3(.2,-.1,0));Child->SetDQ(FSolverVec3(-.02,.015,.001));}
        }
        Row->SetObjectField(TEXT("seed"),Both());TArray<TSharedPtr<FJsonValue>> PositionSamples,VelocitySamples;
        for(int32 It=0;It<8;++It){Solver->ApplyPositionConstraints(Dt,It,8);PositionSamples.Add(MakeShared<FJsonValueObject>(Both()));}
        Parent->SetImplicitVelocity(Dt);Child->SetImplicitVelocity(Dt);Row->SetObjectField(TEXT("implicit"),Both());
        for(int32 It=0;It<2;++It){Solver->ApplyVelocityConstraints(Dt,It,2);VelocitySamples.Add(MakeShared<FJsonValueObject>(Both()));}
        Row->SetArrayField(TEXT("positionSamples"),PositionSamples);Row->SetArrayField(TEXT("velocitySamples"),VelocitySamples);
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Rows);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json)))return Fail(TEXT("Joint step serialization failed."));
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("Joint step write failed."));
}
