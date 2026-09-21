#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/Sphere.h"
#include "Chaos/CollisionResolution.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }
bool ExportAlsPhysicsSphereBoxReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* M){Error=M;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))return Fail(TEXT("Sphere-box reference requires new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native UpdateConstraint SphereBox one-shot manifold; synthetic faces/edges/corners/interior/ties/tiny gaps/cull boundaries, transforms and local centers; no solver or trajectory parity"));
    TArray<TSharedPtr<FJsonValue>> Cases;
    TArray<FVec3> Positions;
    for(int32 X=-1;X<=1;++X)for(int32 Y=-1;Y<=1;++Y)for(int32 Z=-1;Z<=1;++Z)
        for(double Scale:{.5,1.,1.000001,1.0001,1.1,1.5})Positions.Add(FVec3(X,Y,Z)*10*Scale);
    for(double Gap:{-.0001,0.,.0001,2.999999,3.,3.000001})Positions.Add(FVec3(15+Gap,0,0));
    for(int32 PoseMode=0;PoseMode<3;++PoseMode)for(int32 CenterMode=0;CenterMode<2;++CenterMode)
    for(double Radius:{.125,5.})for(double Margin:{0.,.2})for(double Cull:{0.,3.})for(const auto& Position:Positions)
    {
        const FVec3 Center=CenterMode==0?FVec3(0):FVec3(7,-3,2);
        const FVec3 Half(10),BoxCenter=CenterMode==0?FVec3(0):FVec3(-2,1,3);
        const auto BoxRotation=PoseMode==0?FRotation3::Identity:FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.43);
        const auto SphereRotation=PoseMode==0?FRotation3::Identity:FRotation3::FromAxisAngle(FVec3(2,-1,3).GetSafeNormal(),.71);
        const FVec3 Origin=PoseMode==2?FVec3(1e6,-2e6,3e6):FVec3(0);
        const FRigidTransform3 BoxPose(Origin,BoxRotation);
        const FVec3 SphereWorld=BoxPose.TransformPositionNoScale(BoxCenter+Position);
        const FRigidTransform3 SpherePose(SphereWorld-SphereRotation.RotateVector(Center),SphereRotation);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr Sphere=MakeImplicitObjectPtr<Chaos::FSphere>(Center,Radius);
        FImplicitObjectPtr Box=MakeImplicitObjectPtr<FImplicitBox3>(BoxCenter-Half,BoxCenter+Half,Margin);
        P[0]->SetGeometry(Sphere);P[1]->SetGeometry(Box);
        for(auto* Particle:P){Particle->SetX(FVec3(0));Particle->SetR(FRotation3::Identity);}
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();auto* Context=Allocator.GetContextAllocator(0);
        auto C=Context->CreateConstraint(P[0],Sphere.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],Box.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,EContactShapesType::SphereBox);
        Context->ActivateConstraint(C.Get());Allocator.EndDetectCollisions();C->SetShapeWorldTransforms(SpherePose,BoxPose);
        Collisions::UpdateConstraint(*C,SpherePose,BoxPose,1./60.);
        auto Row=MakeShared<FJsonObject>();Row->SetArrayField(TEXT("center"),V(FVector(Sphere->GetObject<Chaos::FSphere>()->GetCenterf())));
        Row->SetNumberField(TEXT("radius"),Sphere->GetObject<Chaos::FSphere>()->GetRadiusf());
        Row->SetArrayField(TEXT("boxMin"),V(BoxCenter-Half));Row->SetArrayField(TEXT("boxMax"),V(BoxCenter+Half));
        Row->SetNumberField(TEXT("margin"),Margin);Row->SetNumberField(TEXT("cull"),Cull);
        Row->SetObjectField(TEXT("spherePose"),T(FTransform(SpherePose)));Row->SetObjectField(TEXT("boxPose"),T(FTransform(BoxPose)));
        TArray<TSharedPtr<FJsonValue>> Points;
        for(int32 I=0;I<C->NumManifoldPoints();++I){
            const auto& Point=C->GetManifoldPoint(I).ContactPoint;auto J=MakeShared<FJsonObject>();
            J->SetArrayField(TEXT("point0"),V(FVec3(Point.ShapeContactPoints[0])));J->SetArrayField(TEXT("point1"),V(FVec3(Point.ShapeContactPoints[1])));
            J->SetArrayField(TEXT("normal1"),V(FVec3(Point.ShapeContactNormal)));J->SetNumberField(TEXT("phi"),Point.Phi);
            Points.Add(MakeShared<FJsonValueObject>(J));
        }
        Row->SetArrayField(TEXT("points"),Points);Cases.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Cases);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||!FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
        return Fail(TEXT("Cannot save sphere-box reference."));
    return true;
}
