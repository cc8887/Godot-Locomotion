#include "AlsPhysicsAssetExport.h"
#include "Chaos/Convex.h"
#include "Chaos/CollisionResolution.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "Engine/SkeletalMesh.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }

bool ExportAlsPhysicsConvexPairReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Convex pair reference requires a new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Actual UpdateConstraint initial one-shot manifolds; ordered pairs of raw cooked zero-margin AnimMan feet; no supplied GJK, planes or contacts"));
    for(const auto& Pair:TArray<TPair<FString,FString>>{
        {TEXT("gjkEpsilon"),TEXT("p.Chaos.Collision.GJKEpsilon")},
        {TEXT("epaEpsilon"),TEXT("p.Chaos.Collision.EPAEpsilon")},
        {TEXT("minimumFaceSearchDistance"),TEXT("p.Chaos.Collision.Manifold.MinFaceSearchDistance")},
        {TEXT("planeNormalEpsilon"),TEXT("p.Chaos.Collision.Manifold.PlaneContactNormalEpsilon")}})
    {
        auto* Setting=IConsoleManager::Get().FindConsoleVariable(*Pair.Value);
        if(!Setting)return Fail(TEXT("Missing native manifold setting."));
        Root->SetNumberField(Pair.Key,Setting->GetFloat());
    }
    auto* UseGjk2=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.UseGJK2"));
    auto* ForceZero=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.Manifold.ForceOneShotManifoldEdgeEdgeCaseZeroCullDistance"));
    if(!UseGjk2||!ForceZero||UseGjk2->GetBool())return Fail(TEXT("Expected native indexed GJK route."));
    Root->SetBoolField(TEXT("forceEdgeZeroCull"),ForceZero->GetBool());
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/AnimMan.AnimMan"));
    auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;if(!Asset)return Fail(TEXT("Missing AnimMan physics asset."));
    struct FShape{FString Bone;FImplicitObjectPtr Geometry;};TArray<FShape> Shapes;
    for(auto Setup:Asset->SkeletalBodySetups)
    {
        Setup->CreatePhysicsMeshes();
        for(const auto& Element:Setup->AggGeom.ConvexElems)
        {
            const auto& Hull=Element.GetChaosConvexMesh();if(!Hull||Hull->GetMargin()!=0)return Fail(TEXT("Expected zero-margin cooked hull."));
            Shapes.Add({Setup->BoneName.ToString(),FImplicitObjectPtr(Hull.GetReference())});
        }
    }
    if(Shapes.Num()!=2)return Fail(TEXT("Expected two cooked feet."));
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(int32 A=0;A<2;++A)for(int32 B=0;B<2;++B)for(int32 Axis=0;Axis<3;++Axis)for(int32 Sign:{-1,1})
    for(double Distance:{0.,8.,16.,24.,40.,60.})for(double Angle:{0.,.13,.6})for(double Cull:{0.,3.,6.})
    {
        FVec3 Position(.137,-.231,.179);Position[Axis]+=Sign*Distance;
        const FRigidTransform3 Pose0=FRigidTransform3::Identity;
        const FRigidTransform3 Pose1(Position,FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),Angle));
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        P[0]->SetGeometry(Shapes[A].Geometry);P[1]->SetGeometry(Shapes[B].Geometry);
        for(auto* Body:P){Body->SetX(FVec3(0));Body->SetR(FRotation3::Identity);}
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;
        Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto Constraint=Context->CreateConstraint(P[0],Shapes[A].Geometry.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],Shapes[B].Geometry.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,EContactShapesType::GenericConvexConvex);
        Context->ActivateConstraint(Constraint.Get());Allocator.EndDetectCollisions();
        if(Constraint->GetCollisionMargin0()!=0||Constraint->GetCollisionMargin1()!=0)return Fail(TEXT("Expected zero pair margins."));
        Constraint->SetShapeWorldTransforms(Pose0,Pose1);Collisions::UpdateConstraint(*Constraint,Pose0,Pose1,1./60.);
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("bone0"),Shapes[A].Bone);Row->SetStringField(TEXT("bone1"),Shapes[B].Bone);
        Row->SetObjectField(TEXT("shape1To0"),T(FTransform(Pose1)));Row->SetNumberField(TEXT("cullDistance"),Cull);
        TArray<TSharedPtr<FJsonValue>> Points;
        for(int32 I=0;I<Constraint->NumManifoldPoints();++I)
        {
            const auto& C=Constraint->GetManifoldPoint(I).ContactPoint;auto Point=MakeShared<FJsonObject>();
            Point->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));Point->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1])));
            Point->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));Point->SetNumberField(TEXT("phi"),C.Phi);
            Point->SetStringField(TEXT("feature"),C.ContactType==EContactPointType::EdgeEdge?TEXT("EdgeEdge"):C.ContactType==EContactPointType::PlaneVertex?TEXT("PlaneVertex"):C.ContactType==EContactPointType::VertexPlane?TEXT("VertexPlane"):TEXT("Other"));
            Points.Add(MakeShared<FJsonValueObject>(Point));
        }
        Row->SetArrayField(TEXT("points"),Points);Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Rows);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save convex pair reference."));
    return true;
}
