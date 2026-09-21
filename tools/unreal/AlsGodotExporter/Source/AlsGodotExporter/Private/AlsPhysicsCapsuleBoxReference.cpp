#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/Capsule.h"
#include "Chaos/CollisionResolution.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }
bool ExportAlsPhysicsCapsuleBoxReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* M){Error=M;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))return Fail(TEXT("Capsule-box reference requires new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native UpdateConstraint CapsuleBox; independent face/edge/corner/deep penetration/cull samples with native stored local endpoints and axes; no solver trajectory parity"));
    TArray<TSharedPtr<FJsonValue>> Cases;TArray<FVec3> Positions;
    for(int32 X=-1;X<=1;++X)for(int32 Y=-1;Y<=1;++Y)for(int32 Z=-1;Z<=1;++Z)Positions.Add(FVec3(X,Y,Z)*10);
    for(double X:{15.,18.,18.000001})Positions.Add(FVec3(X,0,0));
    for(int32 PoseMode=0;PoseMode<3;++PoseMode)for(int32 CenterMode=0;CenterMode<2;++CenterMode)
    for(double Radius:{.125,5.})for(double Height:{1.,20.})for(double Cull:{0.,3.})
    for(double Tilt:{0.,.009,.011,.4,.706,1.2})for(const auto& Position:Positions)
    {
        const FVec3 Center=CenterMode==0?FVec3(0):FVec3(7,-3,2),Half(10);
        const auto LocalRotation=FRotation3::FromAxisAngle(FVec3(0,1,0),Tilt);
        const FVec3 Axis=LocalRotation.RotateVector(FVec3(0,0,1));
        const auto BoxRotation=PoseMode==0?FRotation3::Identity:FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.43);
        const auto CapsuleRotation=PoseMode==0?FRotation3::Identity:FRotation3::FromAxisAngle(FVec3(2,-1,3).GetSafeNormal(),.71);
        const FVec3 Origin=PoseMode==2?FVec3(1e6,-2e6,3e6):FVec3(0);
        const FRigidTransform3 BoxPose(Origin,BoxRotation);
        const FRigidTransform3 CapsulePose(BoxPose.TransformPositionNoScale(Position)-CapsuleRotation.RotateVector(Center),CapsuleRotation);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr Capsule=MakeImplicitObjectPtr<FCapsule>(Center-Axis*(Height*.5),Center+Axis*(Height*.5),Radius);
        FImplicitObjectPtr Box=MakeImplicitObjectPtr<FImplicitBox3>(-Half,Half);
        P[0]->SetGeometry(Capsule);P[1]->SetGeometry(Box);
        for(auto* Particle:P){Particle->SetX(FVec3(0));Particle->SetR(FRotation3::Identity);}
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();auto* Context=Allocator.GetContextAllocator(0);
        auto C=Context->CreateConstraint(P[0],Capsule.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],Box.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,EContactShapesType::CapsuleBox);
        Context->ActivateConstraint(C.Get());Allocator.EndDetectCollisions();C->SetShapeWorldTransforms(CapsulePose,BoxPose);
        Collisions::UpdateConstraint(*C,CapsulePose,BoxPose,1./60.);
        const auto* Native=Capsule->GetObject<FCapsule>();auto Row=MakeShared<FJsonObject>();
        Row->SetArrayField(TEXT("endpoint0"),V(FVec3(Native->GetX1f())));Row->SetArrayField(TEXT("axis"),V(FVec3(Native->GetAxisf())));
        Row->SetNumberField(TEXT("radius"),Native->GetRadiusf());Row->SetNumberField(TEXT("length"),Native->GetHeightf());
        Row->SetArrayField(TEXT("boxHalf"),V(Half));Row->SetNumberField(TEXT("cullDistance"),Cull);
        Row->SetObjectField(TEXT("capsulePose"),T(FTransform(CapsulePose)));Row->SetObjectField(TEXT("boxPose"),T(FTransform(BoxPose)));
        TArray<TSharedPtr<FJsonValue>> Points;
        for(int32 I=0;I<C->NumManifoldPoints();++I){
            const auto& Point=C->GetManifoldPoint(I).ContactPoint;auto J=MakeShared<FJsonObject>();
            J->SetArrayField(TEXT("point0"),V(FVec3(Point.ShapeContactPoints[0])));J->SetArrayField(TEXT("point1"),V(FVec3(Point.ShapeContactPoints[1])));
            J->SetArrayField(TEXT("normal1"),V(FVec3(Point.ShapeContactNormal)));J->SetNumberField(TEXT("phi"),Point.Phi);
            J->SetNumberField(TEXT("contactType"),static_cast<int32>(Point.ContactType));Points.Add(MakeShared<FJsonValueObject>(J));
        }
        Row->SetArrayField(TEXT("points"),Points);Cases.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Cases);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||!FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
        return Fail(TEXT("Cannot save capsule-box reference."));
    return true;
}
