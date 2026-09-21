#include "AlsPhysicsAssetExport.h"
#include "Chaos/Capsule.h"
#include "Chaos/CollisionResolution.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }
bool ExportAlsPhysicsCapsulePairReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* M){Error=M;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))return Fail(TEXT("Capsule pair reference requires new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native UpdateConstraint CapsuleCapsule; float relative-space one-shot, dynamic/kinematic ownership, aligned/crossed/deep/end contacts; no solver trajectory parity"));
    for(const TCHAR* Name:{TEXT("CapsuleAxisAlignedThreshold"),TEXT("CapsuleDeepPenetrationFraction"),TEXT("CapsuleRadialContactFraction")}){
        auto* CVar=IConsoleManager::Get().FindConsoleVariable(*(FString(TEXT("p.Chaos.Collision.Manifold."))+Name));
        if(!CVar)return Fail(TEXT("Missing capsule manifold CVar."));Root->SetNumberField(Name,CVar->GetFloat());
    }
    TArray<TSharedPtr<FJsonValue>> Cases;
    const TArray<FVec3> Positions={FVec3(0),FVec3(0,8,0),FVec3(0,12,0),FVec3(0,13,0),FVec3(0,13.0001,0),FVec3(0,5,20),FVec3(0,0,25)};
    for(int32 PoseMode=0;PoseMode<3;++PoseMode)for(int32 CenterMode=0;CenterMode<2;++CenterMode)for(int32 Motion=0;Motion<3;++Motion)
    for(int32 RadiusMode=0;RadiusMode<2;++RadiusMode)for(double Height:{1.,20.})for(double Cull:{0.,3.})
    for(double Tilt:{0.,.009,.65,1.2,UE_DOUBLE_PI*.5,UE_DOUBLE_PI,2.5})for(const auto& Position:Positions)
    {
        const FVec3 CenterA=CenterMode==0?FVec3(0):FVec3(7,-3,2),CenterB=CenterMode==0?FVec3(0):FVec3(-2,1,3);
        const double RadiusA=RadiusMode==0?5:2,RadiusB=RadiusMode==0?5:7;
        const auto Rotation=PoseMode==0?FRotation3::Identity:FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.43);
        const auto RotationA=Rotation*FRotation3::FromAxisAngle(FVec3(0,1,0),Tilt);
        const FVec3 Origin=PoseMode==2?FVec3(1e6,-2e6,3e6):FVec3(0);
        const FRigidTransform3 PoseB(Origin-Rotation.RotateVector(CenterB),Rotation);
        const FRigidTransform3 PoseA(Origin+Rotation.RotateVector(Position)-RotationA.RotateVector(CenterA),RotationA);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr A=MakeImplicitObjectPtr<FCapsule>(CenterA-FVec3(0,0,Height*.5),CenterA+FVec3(0,0,Height*.5),RadiusA);
        FImplicitObjectPtr B=MakeImplicitObjectPtr<FCapsule>(CenterB-FVec3(0,0,10),CenterB+FVec3(0,0,10),RadiusB);
        P[0]->SetGeometry(A);P[1]->SetGeometry(B);
        for(auto* Particle:P){Particle->SetX(FVec3(0));Particle->SetR(FRotation3::Identity);Particle->SetObjectStateLowLevel(EObjectStateType::Dynamic);}
        if(Motion==1)P[0]->SetObjectStateLowLevel(EObjectStateType::Kinematic);if(Motion==2)P[1]->SetObjectStateLowLevel(EObjectStateType::Kinematic);
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();auto* Context=Allocator.GetContextAllocator(0);
        auto C=Context->CreateConstraint(P[0],A.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],B.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,EContactShapesType::CapsuleCapsule);
        Context->ActivateConstraint(C.Get());Allocator.EndDetectCollisions();C->SetShapeWorldTransforms(PoseA,PoseB);
        Collisions::UpdateConstraint(*C,PoseA,PoseB,1./60.);
        auto Row=MakeShared<FJsonObject>();
        const auto Geometry=[&](const FCapsule& Capsule){auto J=MakeShared<FJsonObject>();J->SetArrayField(TEXT("endpoint0"),V(FVec3(Capsule.GetX1f())));
            J->SetArrayField(TEXT("axis"),V(FVec3(Capsule.GetAxisf())));J->SetNumberField(TEXT("height"),Capsule.GetHeightf());J->SetNumberField(TEXT("radius"),Capsule.GetRadiusf());return J;};
        Row->SetObjectField(TEXT("a"),Geometry(*A->GetObject<FCapsule>()));Row->SetObjectField(TEXT("b"),Geometry(*B->GetObject<FCapsule>()));
        Row->SetBoolField(TEXT("dynamicA"),P[0]->ObjectState()==EObjectStateType::Dynamic);Row->SetBoolField(TEXT("dynamicB"),P[1]->ObjectState()==EObjectStateType::Dynamic);
        Row->SetNumberField(TEXT("cull"),Cull);Row->SetObjectField(TEXT("poseA"),T(FTransform(PoseA)));Row->SetObjectField(TEXT("poseB"),T(FTransform(PoseB)));
        TArray<TSharedPtr<FJsonValue>> Points;
        for(int32 I=0;I<C->NumManifoldPoints();++I){const auto& Point=C->GetManifoldPoint(I).ContactPoint;auto J=MakeShared<FJsonObject>();
            J->SetArrayField(TEXT("point0"),V(FVec3(Point.ShapeContactPoints[0])));J->SetArrayField(TEXT("point1"),V(FVec3(Point.ShapeContactPoints[1])));
            J->SetArrayField(TEXT("normal1"),V(FVec3(Point.ShapeContactNormal)));J->SetNumberField(TEXT("phi"),Point.Phi);Points.Add(MakeShared<FJsonValueObject>(J));}
        Row->SetArrayField(TEXT("points"),Points);Cases.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Cases);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||!FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save capsule pair reference."));
    return true;
}
