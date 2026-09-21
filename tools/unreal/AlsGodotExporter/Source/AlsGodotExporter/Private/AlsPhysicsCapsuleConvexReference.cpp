#include "AlsPhysicsAssetExport.h"
#include "Chaos/Convex.h"
#include "Chaos/ImplicitObjectScaled.h"
#include "Engine/SkeletalMesh.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
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
bool ExportAlsPhysicsCapsuleConvexReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* M){Error=M;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))return Fail(TEXT("Capsule-convex reference requires new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native UpdateConstraint CapsuleConvex; actual cooked AnimMan feet, raw/instanced/scaled including actual tiny scale and reflected/nonuniform scale; wrapper margins retained; no solver trajectory parity"));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/AnimMan.AnimMan"));
    auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;if(!Asset)return Fail(TEXT("Missing AnimMan physics asset."));
    struct FFoot{FString Bone;FConvexPtr Hull;};TArray<FFoot> Feet;
    for(auto Setup:Asset->SkeletalBodySetups)
    {
        Setup->CreatePhysicsMeshes();
        for(const auto& Element:Setup->AggGeom.ConvexElems)
        {
            const auto& Hull=Element.GetChaosConvexMesh();if(!Hull||Hull->GetMargin()!=0)return Fail(TEXT("Expected zero-margin cooked hull."));
            Feet.Add({Setup->BoneName.ToString(),Hull});
        }
    }
    if(Feet.Num()!=2)return Fail(TEXT("Expected two cooked feet."));
    TArray<TSharedPtr<FJsonValue>> Cases;
    const TArray<FVec3> Positions={FVec3(0),FVec3(10,0,0),FVec3(-10,0,0),FVec3(0,10,0),FVec3(0,-10,0),FVec3(0,0,10),FVec3(0,0,-10),FVec3(20,10,0),FVec3(-20,-10,0)};
    for(const auto& Foot:Feet)for(int32 Mode=0;Mode<5;++Mode)
    for(int32 PoseMode=0;PoseMode<2;++PoseMode)
    for(double Radius:{.125,5.})for(double Height:{1.,20.})for(double Cull:{0.,3.})
    for(double Tilt:{0.,.011,.706})for(const auto& Position:Positions)
    {
        const FVec3 Center(7,-3,2);
        const FVec3 Scale=Mode==2?FVec3(1,1,.9999998807907104):Mode==3?FVec3(1.5,.8,1.2):Mode==4?FVec3(-1,1,1):FVec3(1);
        const double Margin=Mode==0?0:Mode==1?.6178215146064758:.6178215742111206;
        const auto LocalRotation=FRotation3::FromAxisAngle(FVec3(0,1,0),Tilt);
        const FVec3 Axis=LocalRotation.RotateVector(FVec3(0,0,1));
        const auto BoxRotation=PoseMode==0?FRotation3::Identity:FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.43);
        const auto CapsuleRotation=PoseMode==0?FRotation3::Identity:FRotation3::FromAxisAngle(FVec3(2,-1,3).GetSafeNormal(),.71);
        const FVec3 Origin=PoseMode==1?FVec3(1e6,-2e6,3e6):FVec3(0);
        const FRigidTransform3 BoxPose(Origin,BoxRotation);
        const FRigidTransform3 CapsulePose(BoxPose.TransformPositionNoScale(Position)-CapsuleRotation.RotateVector(Center),CapsuleRotation);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr Capsule=MakeImplicitObjectPtr<FCapsule>(Center-Axis*(Height*.5),Center+Axis*(Height*.5),Radius);
        FImplicitObjectPtr Box=Mode==0?FImplicitObjectPtr(Foot.Hull.GetReference()):Mode==1?
            FImplicitObjectPtr(MakeImplicitObjectPtr<TImplicitObjectInstanced<FConvex>>(Foot.Hull,Margin)):
            FImplicitObjectPtr(MakeImplicitObjectPtr<TImplicitObjectScaled<FConvex>>(Foot.Hull,Scale,Margin));
        P[0]->SetGeometry(Capsule);P[1]->SetGeometry(Box);
        for(auto* Particle:P){Particle->SetX(FVec3(0));Particle->SetR(FRotation3::Identity);}
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();auto* Context=Allocator.GetContextAllocator(0);
        auto C=Context->CreateConstraint(P[0],Capsule.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],Box.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,EContactShapesType::CapsuleConvex);
        Context->ActivateConstraint(C.Get());Allocator.EndDetectCollisions();C->SetShapeWorldTransforms(CapsulePose,BoxPose);
        Collisions::UpdateConstraint(*C,CapsulePose,BoxPose,1./60.);
        const auto* Native=Capsule->GetObject<FCapsule>();auto Row=MakeShared<FJsonObject>();
        Row->SetArrayField(TEXT("endpoint0"),V(FVec3(Native->GetX1f())));Row->SetArrayField(TEXT("axis"),V(FVec3(Native->GetAxisf())));
        Row->SetNumberField(TEXT("radius"),Native->GetRadiusf());Row->SetNumberField(TEXT("length"),Native->GetHeightf());
        Row->SetStringField(TEXT("bone"),Foot.Bone);Row->SetNumberField(TEXT("mode"),Mode);
        Row->SetArrayField(TEXT("scale"),V(Scale));Row->SetNumberField(TEXT("wrapperMargin"),Margin);Row->SetNumberField(TEXT("cullDistance"),Cull);
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
        return Fail(TEXT("Cannot save capsule-convex reference."));
    return true;
}
