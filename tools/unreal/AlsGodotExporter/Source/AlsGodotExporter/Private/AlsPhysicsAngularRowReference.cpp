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

namespace AlsJointSolverReference
{
TArray<TSharedPtr<FJsonValue>> V(const FVector& P);
TSharedRef<FJsonObject> T(const FTransform& P);
}

bool ExportAlsPhysicsAngularRowReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Angular rows require a new absolute output file."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native cached joint container position phase; synthetic free-linear angular constraints; no copied solver formula, no saved assets"));
    Root->SetStringField(TEXT("coordinates"),TEXT("UE COM and connector rotations; kg, cm, radians; inverse inertia 1/(kg*cm^2)"));
    Root->SetBoolField(TEXT("linearContainerSolver"),true);Root->SetBoolField(TEXT("simdEnabled"),true);
    Root->SetNumberField(TEXT("iterations"),8);
    TArray<TSharedPtr<FJsonValue>> Rows;
    const auto Run=[&](int32 Hz,int32 Motion,int32 Channel,bool FixedParent,bool Conditioning,bool Rotated,int32 Control=0)
    {
        const double Dt=static_cast<double>(1.f/Hz);
        FParticleUniqueIndicesMultithreaded UniqueIndices;FPBDRigidsSOAs Particles(UniqueIndices);
        const auto Pair=Particles.CreateDynamicParticles(2);
        for(auto* P:Pair){P->SetX(FVec3(0));P->SetR(FRotation3::Identity);P->SetCenterOfMass(FVec3(0));P->SetRotationOfMass(FRotation3::Identity);}
        const FRotation3 Q0=Rotated?FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.7):FRotation3::Identity;
        FRotation3 Q1=Q0*FRotation3::FromAxisAngle(FVec3(0,0,1),Rotated?.5:-.5)*FRotation3::FromAxisAngle(FVec3(0,1,0),Rotated?-.4:.4)*FRotation3::FromAxisAngle(FVec3(1,0,0),Rotated?-.6:.6);
        if(Control==5)Q1=Q0*FRotation3::FromAxisAngle(FVec3(1,0,0),.00001);
        if(Control==9||Control==10)Q1=Q0*FRotation3::FromAxisAngle(FVec3(0,1,0),3.12);
        if(Control==12)Q1=FRotation3(FQuat(-Q1.X,-Q1.Y,-Q1.Z,-Q1.W));
        const FRotation3 F0=Rotated?FRotation3::FromAxisAngle(FVec3(1,0,0),.2):FRotation3::Identity;
        const FRotation3 F1=Rotated?FRotation3::FromAxisAngle(FVec3(0,1,0),-.1):FRotation3::Identity;
        const FRotation3 R0=Rotated&&!FixedParent?FRotation3(FRotation3::FromAxisAngle(FVec3(1,-2,1).GetSafeNormal(),-.03)*Q0):Q0;
        const FRotation3 R1=Rotated?FRotation3(FRotation3::FromAxisAngle(FVec3(-1,2,3).GetSafeNormal(),-.1)*Q1):Q1;
        FPBDJointConstraints Constraints;Constraints.SetUseLinearSolver(true);
        FPBDJointSolverSettings Settings;Settings.bUseSimd=Control!=4&&Control!=7;Settings.bSolvePositionLast=true;
        Settings.MinSolverStiffness=Settings.MaxSolverStiffness=1;Constraints.SetSettings(Settings);
        FPBDJointSettings Joint;Joint.bUseLinearSolver=true;Joint.bProjectionEnabled=false;
        Joint.LinearMotionTypes=TVec3<EJointMotionType>(EJointMotionType::Free);
        Joint.AngularMotionTypes=TVec3<EJointMotionType>(EJointMotionType::Limited);
        if(Motion==1)Joint.AngularMotionTypes[1]=EJointMotionType::Locked;
        if(Motion>=2)Joint.AngularMotionTypes[0]=EJointMotionType::Locked;
        if(Motion>=3)Joint.AngularMotionTypes[1]=EJointMotionType::Locked;
        if(Motion==4)Joint.AngularMotionTypes[2]=EJointMotionType::Locked;
        Joint.bSoftTwistLimitsEnabled=Motion<2;Joint.bSoftSwingLimitsEnabled=Motion!=4;
        if(Channel==1)Joint.AngularMotionTypes=TVec3<EJointMotionType>(EJointMotionType::Free);
        Joint.AngularLimits=FVec3(.2,.25,.3);Joint.SoftTwistStiffness=Joint.SoftSwingStiffness=5000000;
        Joint.SoftTwistDamping=Joint.SoftSwingDamping=5000;
        Joint.AngularSoftForceMode=Rotated?EJointForceMode::Force:EJointForceMode::Acceleration;
        Joint.bAngularTwistPositionDriveEnabled=Joint.bAngularTwistVelocityDriveEnabled=Channel!=0;
        Joint.bAngularSwingPositionDriveEnabled=Joint.bAngularSwingVelocityDriveEnabled=Channel!=0;
        Joint.AngularDriveStiffness=FVec3(Channel==3?37500:75);Joint.AngularDriveDamping=FVec3(Channel==3?0:1.5);
        Joint.AngularDriveForceMode=Joint.AngularSoftForceMode;
        Joint.AngularDrivePositionTarget=FRotation3::FromAxisAngle(FVec3(0,0,1),.1);
        if(Control==1||Control==2||Control==5||Control==7)Joint.AngularMotionTypes=TVec3<EJointMotionType>(EJointMotionType::Free);
        if(Control==1||Control==8)Joint.AngularDriveStiffness=FVec3(0);
        if(Control==5){Joint.AngularDrivePositionTarget=FRotation3::Identity;Joint.AngularDriveDamping=FVec3(0);}
        if(Control==7){Joint.AngularDriveStiffness=FVec3(.00001);Joint.AngularDriveDamping=FVec3(0);}
        Joint.bMassConditioningEnabled=Conditioning;Joint.ConnectorTransforms={FRigidTransform3(FVec3(0),F1),FRigidTransform3(FVec3(0),F0)};
        Constraints.AddConstraint({Pair[1],Pair[0]},Joint);
        auto Solver=Constraints.CreateSceneSolver(0);Solver->AddConstraints();
        FSolverBodyContainer Bodies;Bodies.Reset(2);Solver->AddBodies(Bodies);
        if(Bodies.Num()!=2||Solver->GetNumConstraints()!=1)return false;
        FSolverBody* Parent=nullptr;FSolverBody* Child=nullptr;
        for(int32 I=0;I<2;++I)
        {
            auto& B=Bodies.GetSolverBody(I);B=FSolverBody::MakeInitialized();const bool P=Bodies.GetParticle(I)==Pair[0];
            const auto Q=P?Q0:Q1;const FVec3 InvI=P?FVec3(.03,.00002,.002):FVec3(.004,.001,.02);
            B.SetX(FVec3(0));B.SetP(FVec3(0));B.SetR(P?R0:R1);B.SetQ(Q);B.SetInvM(P?(FixedParent?0:5):(Control==3||Control==11?0:.2));
            // Fixture owns mutable storage; setter calls a non-exported engine helper.
            const_cast<FSolverVec3&>(B.InvILocal())=FSolverVec3(InvI);
            B.SetInvI(Utilities::ComputeWorldSpaceInertia(Q,FVec3(B.InvILocal())));
            if(P)Parent=&B;else Child=&B;
        }
        if(!Parent||!Child)return false;
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("hz"),Hz);Row->SetNumberField(TEXT("dt"),Dt);
        Row->SetNumberField(TEXT("motionCase"),Motion);Row->SetNumberField(TEXT("channelCase"),Channel);
        Row->SetNumberField(TEXT("controlCase"),Control);Row->SetBoolField(TEXT("useSimd"),Settings.bUseSimd);
        Row->SetBoolField(TEXT("fixedParent"),FixedParent);Row->SetBoolField(TEXT("conditioning"),Conditioning);Row->SetBoolField(TEXT("rotated"),Rotated);
        Row->SetNumberField(TEXT("parentInverseMass"),Parent->InvM());Row->SetNumberField(TEXT("childInverseMass"),Child->InvM());
        Row->SetArrayField(TEXT("parentInverseInertia"),V(FVec3(Parent->InvILocal())));Row->SetArrayField(TEXT("childInverseInertia"),V(FVec3(Child->InvILocal())));
        Row->SetObjectField(TEXT("parentInitial"),T(FTransform(R0)));Row->SetObjectField(TEXT("childInitial"),T(FTransform(R1)));
        Row->SetObjectField(TEXT("parentPredicted"),T(FTransform(Q0)));Row->SetObjectField(TEXT("childPredicted"),T(FTransform(Q1)));
        Row->SetObjectField(TEXT("parentFrame"),T(FTransform(F0)));Row->SetObjectField(TEXT("childFrame"),T(FTransform(F1)));
        Row->SetNumberField(TEXT("minParentMassRatio"),Settings.MinParentMassRatio);Row->SetNumberField(TEXT("maxInertiaRatio"),Settings.MaxInertiaRatio);
        Row->SetNumberField(TEXT("angleTolerance"),Settings.AngleTolerance);
        Row->SetArrayField(TEXT("angularMotion"),V(FVector(static_cast<int32>(Joint.AngularMotionTypes[0]),static_cast<int32>(Joint.AngularMotionTypes[1]),static_cast<int32>(Joint.AngularMotionTypes[2]))));
        Row->SetArrayField(TEXT("angularLimits"),V(Joint.AngularLimits));
        Row->SetBoolField(TEXT("softTwist"),Joint.bSoftTwistLimitsEnabled);Row->SetBoolField(TEXT("softSwing"),Joint.bSoftSwingLimitsEnabled);
        Row->SetArrayField(TEXT("limitStiffness"),V(FVector(Joint.SoftTwistStiffness,Joint.SoftSwingStiffness,Joint.SoftSwingStiffness)));
        Row->SetArrayField(TEXT("limitDamping"),V(FVector(Joint.SoftTwistDamping,Joint.SoftSwingDamping,Joint.SoftSwingDamping)));
        Row->SetNumberField(TEXT("hardStiffness"),Joint.Stiffness);
        Row->SetBoolField(TEXT("accelerationMode"),Joint.AngularSoftForceMode==EJointForceMode::Acceleration);
        Row->SetBoolField(TEXT("drivePositionEnabled"),Joint.bAngularTwistPositionDriveEnabled);
        Row->SetBoolField(TEXT("driveVelocityEnabled"),Joint.bAngularTwistVelocityDriveEnabled);
        Row->SetArrayField(TEXT("driveStiffness"),V(Joint.AngularDriveStiffness));Row->SetArrayField(TEXT("driveDamping"),V(Joint.AngularDriveDamping));
        Row->SetObjectField(TEXT("driveTarget"),T(FTransform(Joint.AngularDrivePositionTarget)));
        const FVec3 Seed0=Rotated&&!FixedParent?FVec3(.01,-.02,.03):FVec3(0);
        const FVec3 Seed1=Control==6?FVec3(-1.5,0,0):(Rotated?FVec3(-.02,.015,.001):FVec3(0));
        Row->SetArrayField(TEXT("parentSeedDQ"),V(Seed0));Row->SetArrayField(TEXT("childSeedDQ"),V(Seed1));
        const auto Prepare=[&](){Parent->SetDQ(FSolverVec3(0));Child->SetDQ(FSolverVec3(0));Solver->GatherInput(Dt);Parent->SetDQ(FSolverVec3(Seed0));Child->SetDQ(FSolverVec3(Seed1));};
        Prepare();TArray<TSharedPtr<FJsonValue>> Samples;
        for(int32 It=0;It<8;++It)
        {
            Solver->ApplyPositionConstraints(Dt,It,8);auto Sample=MakeShared<FJsonObject>();
            Sample->SetArrayField(TEXT("parentDQ"),V(FVec3(Parent->DQ())));Sample->SetArrayField(TEXT("childDQ"),V(FVec3(Child->DQ())));
            Sample->SetArrayField(TEXT("parentDP"),V(FVec3(Parent->DP())));Sample->SetArrayField(TEXT("childDP"),V(FVec3(Child->DP())));
            Sample->SetObjectField(TEXT("parentPose"),T(FTransform(Parent->CorrectedQ())));Sample->SetObjectField(TEXT("childPose"),T(FTransform(Child->CorrectedQ())));
            Samples.Add(MakeShared<FJsonValueObject>(Sample));
        }
        Row->SetArrayField(TEXT("samples"),Samples);Prepare();Solver->ApplyPositionConstraints(Dt,0,8);
        Row->SetArrayField(TEXT("resetParentDQ"),V(FVec3(Parent->DQ())));Row->SetArrayField(TEXT("resetChildDQ"),V(FVec3(Child->DQ())));
        Rows.Add(MakeShared<FJsonValueObject>(Row));return true;
    };
    for(int32 Hz:{30,60,120})for(int32 Motion=0;Motion<5;++Motion)for(int32 Channel=0;Channel<4;++Channel)
    for(bool Fixed:{false,true})for(bool Conditioning:{false,true})for(bool Rotated:{false,true})
        if(!Run(Hz,Motion,Channel,Fixed,Conditioning,Rotated))return Fail(TEXT("Angular row fixture failed."));
    for(int32 Hz:{30,60,120})
    {
        if(!Run(Hz,0,1,false,true,true,1)||!Run(Hz,0,0,false,true,false,2)||
           !Run(Hz,0,2,true,true,false,3)||!Run(Hz,0,2,false,true,false,4)||
           !Run(Hz,0,1,false,true,false,5)||!Run(Hz,0,0,true,true,false,6)||
           !Run(Hz,0,1,false,true,false,7)||!Run(Hz,3,2,false,true,true,8)||
           !Run(Hz,1,0,false,true,false,9)||!Run(Hz,0,0,false,true,false,10)||
           !Run(Hz,0,2,false,true,false,11)||!Run(Hz,0,2,false,true,false,12))return Fail(TEXT("Angular control fixture failed."));
    }
    Root->SetArrayField(TEXT("cases"),Rows);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json)))return Fail(TEXT("Angular serialization failed."));
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("Angular reference write failed."));
}
