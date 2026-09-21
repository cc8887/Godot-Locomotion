#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/Capsule.h"
#include "Chaos/Convex.h"
#include "Chaos/Sphere.h"
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

bool ExportAlsPhysicsMarginReference(const FString& Output,FString& Error)
{
    using namespace Chaos;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Margin reference requires a new absolute output."));
    auto* Setting=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.ConvexZeroMargin"));
    if(!Setting)return Fail(TEXT("Missing native margin setting."));
    struct FRestore { IConsoleVariable* Setting; float Value; ~FRestore(){Setting->Set(Value,ECVF_SetByCode);} } Restore{Setting,Setting->GetFloat()};
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetNumberField(TEXT("observedConvexZeroMargin"),Restore.Value);
    Root->SetStringField(TEXT("observation"),TEXT("Actual collision constraint Setup margins; ordered shape pairs, static/kinematic/dynamic/sleeping states; zero-margin CVar process-local sweep; no simulated contacts or saved assets"));
    struct FShape { FString Name; FImplicitObjectPtr Geometry; };
    TArray<FShape> Shapes;
    for(double Margin:{0.,.2,.6})Shapes.Add({FString::Printf(TEXT("box_%.1f"),Margin),MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-8,-10,-6),FVec3(8,10,6),Margin)});
    Shapes.Add({TEXT("sphere"),MakeImplicitObjectPtr<TSphere<FReal,3>>(FVec3(0),2.)});
    Shapes.Add({TEXT("capsule"),MakeImplicitObjectPtr<FCapsule>(FVec3(0,0,-5),FVec3(0,0,5),3.)});
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/AnimMan.AnimMan"));
    auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;if(!Asset)return Fail(TEXT("Missing AnimMan physics asset."));
    for(auto Setup:Asset->SkeletalBodySetups)
    {
        Setup->CreatePhysicsMeshes();
        for(const auto& Element:Setup->AggGeom.ConvexElems)
        {
            const auto& Hull=Element.GetChaosConvexMesh();if(!Hull)return Fail(TEXT("Missing cooked foot."));
            Shapes.Add({Setup->BoneName.ToString(),FImplicitObjectPtr(Hull.GetReference())});
        }
    }
    if(Shapes.Num()!=7)return Fail(TEXT("Expected five primitive fixtures and two actual cooked feet."));
    const EObjectStateType States[]={EObjectStateType::Static,EObjectStateType::Kinematic,EObjectStateType::Dynamic,EObjectStateType::Sleeping};
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(float Minimum:{0.f,.05f})for(int32 A=0;A<Shapes.Num();++A)for(int32 B=0;B<Shapes.Num();++B)
    for(int32 State0=0;State0<4;++State0)for(int32 State1=0;State1<4;++State1)
    {
        Setting->Set(Minimum,ECVF_SetByCode);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        P[0]->SetGeometry(Shapes[A].Geometry);P[1]->SetGeometry(Shapes[B].Geometry);
        for(auto* Body:P){Body->SetX(FVec3(0));Body->SetR(FRotation3::Identity);}
        P[0]->SetObjectStateLowLevel(States[State0]);P[1]->SetObjectStateLowLevel(States[State1]);
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;
        Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto Constraint=Context->CreateConstraint(P[0],Shapes[A].Geometry.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],Shapes[B].Geometry.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,3.,true,EContactShapesType::ConvexConvex);
        Allocator.EndDetectCollisions();
        auto Row=MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("shape0"),Shapes[A].Name);Row->SetStringField(TEXT("shape1"),Shapes[B].Name);
        Row->SetNumberField(TEXT("state0"),State0);Row->SetNumberField(TEXT("state1"),State1);
        Row->SetNumberField(TEXT("shapeMargin0"),Shapes[A].Geometry->GetMarginf());Row->SetNumberField(TEXT("shapeMargin1"),Shapes[B].Geometry->GetMarginf());
        Row->SetNumberField(TEXT("minimum"),Setting->GetFloat());
        Row->SetBoolField(TEXT("dynamic0"),FConstGenericParticleHandle(P[0])->IsDynamic());Row->SetBoolField(TEXT("dynamic1"),FConstGenericParticleHandle(P[1])->IsDynamic());
        Row->SetBoolField(TEXT("quadratic0"),Constraint->IsQuadratic0());Row->SetBoolField(TEXT("quadratic1"),Constraint->IsQuadratic1());
        Row->SetNumberField(TEXT("margin0"),Constraint->GetCollisionMargin0());Row->SetNumberField(TEXT("margin1"),Constraint->GetCollisionMargin1());
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Rows);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save margin reference."));
    return true;
}
