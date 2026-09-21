#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/CollisionResolution.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }

bool ExportAlsPhysicsBoxGeometryReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Box geometry requires a new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native UpdateConstraint box-box initial manifolds; six faces, tilted/separated interior and boundary cases; no supplied contacts or persistent cache"));
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(int32 Face=0;Face<6;++Face)for(double Tilt:{0.,.05,.2,.6})for(double Gap:{-.2,.2,3.2})
    for(double Cull:{0.,3.,6.})for(int32 Placement=0;Placement<2;++Placement)
    {
        const FVec3 N=Face==0?FVec3(1,0,0):Face==1?FVec3(-1,0,0):Face==2?FVec3(0,1,0):Face==3?FVec3(0,-1,0):Face==4?FVec3(0,0,1):FVec3(0,0,-1);
        const auto FaceQ=FRotation3::FromRotatedVector(FVec3(0,0,1),N);
        const auto LocalQ=FaceQ*FRotation3::FromAxisAngle(FVec3(0,1,0),Tilt);
        const FVec3 Half0(3.75,13.5,5.5),Half1(100);
        const double Extent=3.75*FMath::Sin(Tilt)+5.5*FMath::Cos(Tilt);
        const auto WorldQ=FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.43);
        const FVec3 WorldP(1e6,-2e6,3e6);
        const FVec3 Offset=FaceQ.RotateVector(FVec3(Placement==0?0:98,0,0));
        const FRigidTransform3 Pose0(WorldP+WorldQ.RotateVector(N*(100+Extent+Gap)+Offset),WorldQ*LocalQ);
        const FRigidTransform3 Pose1(WorldP,WorldQ);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr A=MakeImplicitObjectPtr<TBox<FReal,3>>(-Half0,Half0),B=MakeImplicitObjectPtr<TBox<FReal,3>>(-Half1,Half1);
        P[0]->SetGeometry(A);P[1]->SetGeometry(B);
        for(auto* Body:P){Body->SetX(FVec3(0));Body->SetR(FRotation3::Identity);}
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;
        Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto Constraint=Context->CreateConstraint(P[0],A.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],B.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,EContactShapesType::BoxBox);
        Context->ActivateConstraint(Constraint.Get());Allocator.EndDetectCollisions();
        Constraint->SetShapeWorldTransforms(Pose0,Pose1);
        Collisions::UpdateConstraint(*Constraint,Pose0,Pose1,1./60.);
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("face"),Face);Row->SetNumberField(TEXT("tilt"),Tilt);
        Row->SetNumberField(TEXT("gap"),Gap);Row->SetNumberField(TEXT("cullDistance"),Cull);Row->SetBoolField(TEXT("interior"),Placement==0);
        Row->SetObjectField(TEXT("pose0"),T(FTransform(Pose0)));Row->SetObjectField(TEXT("pose1"),T(FTransform(Pose1)));
        Row->SetArrayField(TEXT("half0"),V(Half0));Row->SetArrayField(TEXT("half1"),V(Half1));
        TArray<TSharedPtr<FJsonValue>> Points;
        for(int32 I=0;I<Constraint->NumManifoldPoints();++I)
        {
            const auto& C=Constraint->GetManifoldPoint(I).ContactPoint;auto Point=MakeShared<FJsonObject>();
            Point->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));Point->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1])));
            Point->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));Point->SetNumberField(TEXT("phi"),C.Phi);
            Points.Add(MakeShared<FJsonValueObject>(Point));
        }
        Row->SetArrayField(TEXT("points"),Points);Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Rows);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save box reference."));
    return true;
}
