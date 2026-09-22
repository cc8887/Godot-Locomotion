#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/Capsule.h"
#include "Chaos/Sphere.h"
#include "Chaos/Convex.h"
#include "Chaos/ImplicitObjectScaled.h"
#include "Chaos/Collision/CollisionUtil.h"
#include "Chaos/Collision/ParticlePairMidPhase.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "Engine/SkeletalMesh.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"

namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }
namespace
{
class FBoundsOwner final : public Chaos::FParticlePairMidPhase
{
public:
    FBoundsOwner():FParticlePairMidPhase(Chaos::EParticlePairMidPhaseType::Generic){}
protected:
    void ResetImpl() override {}
    void BuildDetectorsImpl() override {}
    int32 GenerateCollisionsImpl(float,float,const Chaos::FVec3f&,const Chaos::FCollisionContext&) override {return 0;}
    void WakeCollisionsImpl(int32) override {}
    void InjectCollisionImpl(const Chaos::FPBDCollisionConstraint&,const Chaos::FCollisionContext&) override {}
};
}
bool ExportAlsPhysicsShapeBoundsReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* M){Error=M;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Shape bounds requires a new absolute output."));
    const auto* Enabled=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.EnableBoundsChecks"));
    if(!Enabled||Enabled->GetInt()!=1)return Fail(TEXT("Expected native bounds checks enabled."));
    struct FGeometry {FString Name;FImplicitObjectPtr Shape;};
    TArray<FGeometry> Geometry;
    Geometry.Add({TEXT("box"),MakeImplicitObjectPtr<FImplicitBox3>(FVec3(-10,-.25,-.25),FVec3(10,.25,.25))});
    Geometry.Add({TEXT("sphere"),MakeImplicitObjectPtr<FImplicitSphere3>(FVec3(7,-3,2),1.25)});
    Geometry.Add({TEXT("capsule"),MakeImplicitObjectPtr<FCapsule>(FVec3(7,-3,-8),FVec3(9.13,1.37,12.71),2.125)});
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/AnimMan.AnimMan"));
    auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;if(!Asset)return Fail(TEXT("Missing AnimMan asset."));
    for(auto Setup:Asset->SkeletalBodySetups)
    {
        Setup->CreatePhysicsMeshes();
        for(const auto& Element:Setup->AggGeom.ConvexElems)
        {
            const auto Hull=Element.GetChaosConvexMesh();if(!Hull)return Fail(TEXT("Missing cooked convex."));
            Geometry.Add({Setup->BoneName.ToString(),MakeImplicitObjectPtr<TImplicitObjectScaled<FConvex>>(Hull,FVec3(1.5,.8,1.2),.6178215742111206)});
        }
    }
    if(Geometry.Num()!=5)return Fail(TEXT("Expected two native cooked feet."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native FSingleShapePairCollisionDetector GenerateCollision with deferred narrow phase isolates DoBoundsOverlap; cold/prior-active/gap epochs; actual shape bounds and flags; no contact geometry or solver trajectory parity"));
    Root->SetBoolField(TEXT("boundsChecksEnabled"),true);
    TArray<TSharedPtr<FJsonValue>> Shapes;
    for(const auto& G:Geometry)
    {
        auto J=MakeShared<FJsonObject>();J->SetStringField(TEXT("name"),G.Name);
        J->SetArrayField(TEXT("min"),V(G.Shape->BoundingBox().Min()));J->SetArrayField(TEXT("max"),V(G.Shape->BoundingBox().Max()));
        const bool Sphere=G.Name==TEXT("sphere"),Capsule=G.Name==TEXT("capsule");
        J->SetStringField(TEXT("kind"),Sphere?TEXT("sphere"):Capsule?TEXT("capsule"):TEXT("polygon"));
        if(Sphere)
        {
            const auto& S=G.Shape->GetObjectChecked<FImplicitSphere3>();
            J->SetArrayField(TEXT("endpoint0"),V(FVec3(S.GetCenterf())));J->SetArrayField(TEXT("endpoint1"),V(FVec3(S.GetCenterf())));
            J->SetNumberField(TEXT("radius"),S.GetRadiusf());
        }
        if(Capsule)
        {
            const auto& C=G.Shape->GetObjectChecked<FCapsule>();
            J->SetArrayField(TEXT("endpoint0"),V(FVec3(C.GetX1f())));J->SetArrayField(TEXT("endpoint1"),V(FVec3(C.GetX2f())));
            J->SetArrayField(TEXT("axis"),V(FVec3(C.GetAxisf())));J->SetNumberField(TEXT("height"),C.GetHeightf());
            J->SetNumberField(TEXT("radius"),C.GetRadiusf());
        }
        Shapes.Add(MakeShared<FJsonValueObject>(J));
    }
    Root->SetArrayField(TEXT("shapes"),Shapes);TArray<TSharedPtr<FJsonValue>> Rows;
    const TArray<FVec3> Offsets={FVec3(0),FVec3(0,1,0),FVec3(0,4,0),FVec3(0,10,0),FVec3(25,0,0),FVec3(0,0,30),FVec3(-12,8,3)};
    for(int32 A=0;A<Geometry.Num();++A)for(int32 B=0;B<Geometry.Num();++B)
    for(double Angle:{0.,.35,.7853981633974483,1.2})for(int32 Placement=0;Placement<Offsets.Num();++Placement)
    for(float Cull:{0.f,3.f,6.f})for(int32 History=0;History<3;++History)
    {
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        P[0]->SetGeometry(Geometry[A].Shape);P[1]->SetGeometry(Geometry[B].Shape);
        const auto SetPose=[&](int32 I,const FRigidTransform3& Pose)
        {P[I]->SetX(Pose.GetTranslation());P[I]->SetR(Pose.GetRotation());P[I]->SetP(Pose.GetTranslation());P[I]->SetQ(Pose.GetRotation());P[I]->UpdateWorldSpaceState(Pose,FVec3(0));};
        for(int32 I=0;I<2;++I)SetPose(I,FRigidTransform3(-P[I]->GetGeometry()->BoundingBox().Center(),FRotation3::Identity));
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;
        Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);
        FCollisionDetectorSettings Settings;Settings.bDeferNarrowPhase=true;Settings.bAllowManifoldReuse=false;Settings.bAllowCCD=false;Settings.bAllowMACD=false;
        FCollisionContext Context;Context.SetSettings(Settings);Context.SetAllocator(Allocator.GetContextAllocator(0));
        FBoundsOwner Owner;Owner.Init(P[0],P[1],{},Context);
        FSingleShapePairCollisionDetector Detector(P[0],P[0]->ShapesArray()[0].Get(),P[1],P[1]->ShapesArray()[0].Get(),{},EContactShapesType::GenericConvexConvex,Owner);
        if(History>0)
        {
            Allocator.BeginDetectCollisions();
            if(Detector.GenerateCollision(1.f/60,6,FVec3f(0),Context)!=1)return Fail(TEXT("Cannot seed native prior activity."));
            Allocator.EndDetectCollisions();
            if(History==2){Allocator.BeginDetectCollisions();Allocator.EndDetectCollisions();}
        }
        const auto Q=FRotation3::FromAxisAngle(FVec3(0,0,1),Angle);
        FRotation3 QB=Q;if(Placement%2)QB=Q*FRotation3::FromAxisAngle(FVec3(0,1,0),.19);
        const FVec3 Translation(1000,-2000,3000);
        const FRigidTransform3 Pose0(Translation-Q.RotateVector(Geometry[A].Shape->BoundingBox().Center()),Q);
        const FRigidTransform3 Pose1(Translation+Q.RotateVector(Offsets[Placement])-QB.RotateVector(Geometry[B].Shape->BoundingBox().Center()),QB);
        SetPose(0,Pose0);SetPose(1,Pose1);Allocator.BeginDetectCollisions();
        const int32 Epoch=Allocator.GetCurrentEpoch();const bool Prior=Detector.IsUsedSince(Epoch-1);
        if(Prior!=(History==1))return Fail(TEXT("Unexpected native epoch continuity."));
        float DistanceSize=0;
        const auto Flags=Private::CalculateImplicitBoundsTestFlags(P[0],P[0]->ShapesArray()[0]->GetLeafGeometry(),P[0]->ShapesArray()[0].Get(),
            P[1],P[1]->ShapesArray()[0]->GetLeafGeometry(),P[1]->ShapesArray()[0].Get(),DistanceSize);
        const int32 Active=Detector.GenerateCollision(1.f/60,Cull,FVec3f(0),Context);Allocator.EndDetectCollisions();
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("shape0"),A);Row->SetNumberField(TEXT("shape1"),B);
        Row->SetNumberField(TEXT("history"),History);Row->SetBoolField(TEXT("collidedLastStep"),Prior);
        Row->SetNumberField(TEXT("cull"),Cull);Row->SetBoolField(TEXT("allowed"),Active==1);
        Row->SetBoolField(TEXT("aabb"),Flags.bEnableAABBCheck);Row->SetBoolField(TEXT("obb0"),Flags.bEnableOBBCheck0);
        Row->SetBoolField(TEXT("obb1"),Flags.bEnableOBBCheck1);Row->SetBoolField(TEXT("distance"),Flags.bEnableDistanceCheck);
        Row->SetNumberField(TEXT("distanceSize"),DistanceSize);
        Row->SetObjectField(TEXT("pose0"),T(FTransform(Pose0)));Row->SetObjectField(TEXT("pose1"),T(FTransform(Pose1)));
        for(int32 I=0;I<2;++I)
        {
            const auto Bounds=P[I]->ShapesArray()[0]->GetWorldSpaceShapeBounds();
            Row->SetArrayField(*FString::Printf(TEXT("min%d"),I),V(Bounds.Min()));
            Row->SetArrayField(*FString::Printf(TEXT("max%d"),I),V(Bounds.Max()));
        }
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Rows);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save shape bounds reference."));
    return true;
}
