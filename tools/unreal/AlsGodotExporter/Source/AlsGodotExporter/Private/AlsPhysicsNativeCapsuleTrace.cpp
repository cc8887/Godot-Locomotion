#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/ShapeInstance.h"
#include "PhysicsProxy/SingleParticlePhysicsProxy.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/Engine.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "PhysicsEngine/BodyInstance.h"
#include "UObject/StrongObjectPtr.h"
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
namespace AlsNativeCapsuleTraceReference
{
using namespace Chaos;
FVec3 Vector(const TSharedPtr<FJsonObject>& J,const TCHAR* Name)
{ const auto& A=J->GetArrayField(Name); return FVec3(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber()); }
FRigidTransform3 Pose(const TSharedPtr<FJsonObject>& J,int32 Index)
{
    const auto& Q=J->GetArrayField(Index==0?TEXT("rotation0"):TEXT("rotation1"));
    return FRigidTransform3(Vector(J,Index==0?TEXT("center0"):TEXT("center1")),FRotation3(FQuat(Q[0]->AsNumber(),Q[1]->AsNumber(),Q[2]->AsNumber(),Q[3]->AsNumber())));
}
}

bool ExportAlsPhysicsNativeCapsuleTrace(const FString& Input,const FString& Output,FString& Error)
{
    using namespace Chaos; using namespace AlsNativeCapsuleTraceReference; using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if (Input.IsEmpty() || FPaths::IsRelative(Input) || Output.IsEmpty() || FPaths::IsRelative(Output) || IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Capsule geometry reference needs a trace and a new absolute output file."));
    TArray<FString> Lines; if (!FFileHelper::LoadFileToStringArray(Lines,*Input)) return Fail(TEXT("Cannot read trace."));
    auto Root=MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),1); Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native UpdateConstraint capsule-box from actual captured leaf poses and cull; capsule copied from current native body after exact endpoint/axis/radius/height match; supplied contacts are comparison only; no solver or persistent manifold"));
    TArray<TSharedPtr<FJsonValue>> Cases; TArray<TSharedPtr<FJsonObject>> Sources; int32 TraceCount=0,PairCount=0;
    for (const auto& Line:Lines)
    {
        const FString Prefix=TEXT("CORE_CONTACT_TRACE "); const int32 At=Line.Find(Prefix);
        if (At==INDEX_NONE) continue;
        TSharedPtr<FJsonObject> Source; if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Line.Mid(At+Prefix.Len())),Source)) return Fail(TEXT("Invalid contact trace JSON."));
        ++TraceCount; Sources.Add(Source);
    }
    const auto Initialization=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(true)
        .RequiresHitProxies(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false)
        .EnableTraceCollision(true).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,
        nullptr,false,ERHIFeatureLevel::Num,&Initialization));
    if(!World.IsValid())return Fail(TEXT("Cannot create native reference world."));
    GEngine->CreateNewWorldContext(EWorldType::GamePreview).SetCurrentWorld(World.Get());
    struct FCleanup{UWorld* World;~FCleanup(){World->DestroyWorld(false);GEngine->DestroyWorldContext(World);}} Cleanup{World.Get()};
    TMap<FString,USkeletalMeshComponent*> Components;
    int32 Skipped=0;
    for (const auto& Source:Sources)
    {
        const auto A=Source->GetObjectField(TEXT("geometry0")),B=Source->GetObjectField(TEXT("geometry1"));
        const auto Type0=A->GetStringField(TEXT("type")),Type1=B->GetStringField(TEXT("type"));
        const bool Swapped=Type1==TEXT("native_capsule") && Type0==TEXT("box");
        if (!Swapped && !(Type0==TEXT("native_capsule") && Type1==TEXT("box"))) { ++Skipped; continue; }
        ++PairCount; const auto CapsuleData=Swapped?B:A,BoxData=Swapped?A:B;
        const auto CapsulePose=Pose(Source,Swapped?1:0),BoxPose=Pose(Source,Swapped?0:1);
        const double Length=CapsuleData->GetNumberField(TEXT("length")),Radius=CapsuleData->GetNumberField(TEXT("radius"));
        const auto Half=Vector(BoxData,TEXT("size"))*.5;
        if (Length<=0 || Radius<=0 || Half.GetMin()<=0) return Fail(TEXT("Invalid primitive dimensions."));
        const FString MeshPath=Source->GetStringField(TEXT("mesh"));
        USkeletalMeshComponent* Component=Components.FindRef(MeshPath);
        if(!Component)
        {
            auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*MeshPath);
            if(!Mesh||!Mesh->GetPhysicsAsset())return Fail(TEXT("Missing trace mesh/physics asset."));
            auto* Owner=World->SpawnActor<AActor>();if(!Owner)return Fail(TEXT("Cannot spawn trace owner."));
            Component=NewObject<USkeletalMeshComponent>(Owner);Owner->SetRootComponent(Component);Owner->AddInstanceComponent(Component);
            Component->SetSkeletalMesh(Mesh);Component->SetCollisionEnabled(ECollisionEnabled::QueryAndPhysics);
            Component->SetSimulatePhysics(true);Component->RegisterComponent();Component->SetComponentTickEnabled(false);
            Components.Add(MeshPath,Component);
        }
        const FName Bone(*Source->GetStringField(Swapped?TEXT("body1"):TEXT("body0")));
        const auto* Instance=Component->GetBodyInstance(Bone);
        if(!Instance||!Instance->IsValidBodyInstance()||!Instance->GetPhysicsActorHandle())return Fail(TEXT("Missing trace native body."));
        const FCapsule* Resolved=nullptr;
        for(const auto& Shape:Instance->GetPhysicsActorHandle()->GetGameThreadAPI().ShapesArray())
        {
            const auto* Leaf=Shape->GetLeafGeometry();
            const auto* Candidate=Leaf?Leaf->GetObject<FCapsule>():nullptr;
            // System.Text.Json writes float fields with shortest round-trip
            // decimals. Restore their declared float storage before exact match.
            if(Candidate&&Candidate->GetX1f()==FVec3f(Vector(CapsuleData,TEXT("endpoint0")))&&
                Candidate->GetAxisf()==FVec3f(Vector(CapsuleData,TEXT("axis")))&&
                Candidate->GetHeightf()==static_cast<float>(Length)&&Candidate->GetRadiusf()==static_cast<float>(Radius))
            {
                if(Resolved)return Fail(TEXT("Ambiguous capsule geometry within trace body."));
                Resolved=Candidate;
            }
        }
        if(!Resolved)return Fail(TEXT("Trace capsule differs from current native leaf; refusing reconstruction."));
        double Cull;
        if(!Source->TryGetNumberField(TEXT("cullDistance"),Cull)||!FMath::IsFinite(Cull)||Cull<0)
            return Fail(TEXT("Trace requires actual finite cull distance."));
        Cull=static_cast<float>(Cull);

        {
            FParticleUniqueIndicesMultithreaded Unique; FPBDRigidsSOAs Particles(Unique); const auto P=Particles.CreateDynamicParticles(2);
            FImplicitObjectPtr Capsule=MakeImplicitObjectPtr<FCapsule>(*Resolved);
            FImplicitObjectPtr Box=MakeImplicitObjectPtr<TBox<FReal,3>>(-Half,Half);
            P[0]->SetGeometry(Capsule); P[1]->SetGeometry(Box);
            for (auto* Body:P) { Body->SetX(FVec3(0)); Body->SetR(FRotation3::Identity); }
            TArrayCollectionArray<bool> Collided; TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
            TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticleMaterials;
            Collided.Resize(2); Materials.Resize(2); PerParticleMaterials.Resize(2);
            FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticleMaterials,nullptr);
            auto& Allocator=Constraints.GetConstraintAllocator(); Allocator.SetMaxContexts(1); Allocator.BeginDetectCollisions(); auto* Context=Allocator.GetContextAllocator(0);
            auto Constraint=Context->CreateConstraint(P[0],Capsule.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
                P[1],Box.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,EContactShapesType::CapsuleBox);
            Context->ActivateConstraint(Constraint.Get()); Allocator.EndDetectCollisions();
            Constraint->SetShapeWorldTransforms(CapsulePose,BoxPose);
            Collisions::UpdateConstraint(*Constraint,CapsulePose,BoxPose,1./60.);
            auto Row=MakeShared<FJsonObject>(); Row->SetObjectField(TEXT("source"),Source); Row->SetBoolField(TEXT("swapped"),Swapped);
            Row->SetNumberField(TEXT("cullDistance"),Cull); Row->SetObjectField(TEXT("capsulePose"),T(FTransform(CapsulePose)));
            Row->SetObjectField(TEXT("boxPose"),T(FTransform(BoxPose))); Row->SetArrayField(TEXT("boxHalf"),V(Half));
            const auto* NativeCapsule=Capsule->GetObject<FCapsule>();
            Row->SetNumberField(TEXT("radius"),NativeCapsule->GetRadiusf()); Row->SetNumberField(TEXT("length"),NativeCapsule->GetHeightf());
            Row->SetArrayField(TEXT("endpoint0"),V(FVec3(NativeCapsule->GetX1f())));
            Row->SetArrayField(TEXT("axis"),V(FVec3(NativeCapsule->GetAxisf())));
            TArray<TSharedPtr<FJsonValue>> Points;
            for (int32 I=0;I<Constraint->NumManifoldPoints();++I)
            {
                const auto& M=Constraint->GetManifoldPoint(I); const auto& C=M.ContactPoint;
                auto Point=MakeShared<FJsonObject>(); Point->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));
                Point->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1]))); Point->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));
                Point->SetNumberField(TEXT("phi"),C.Phi); Point->SetNumberField(TEXT("contactType"),static_cast<int32>(C.ContactType));
                Point->SetBoolField(TEXT("disabled"),M.Flags.bDisabled); Points.Add(MakeShared<FJsonValueObject>(Point));
            }
            Row->SetArrayField(TEXT("points"),Points); Cases.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    if (PairCount==0) return Fail(TEXT("No captured capsule-box pairs."));
    Root->SetNumberField(TEXT("traceCount"),TraceCount); Root->SetNumberField(TEXT("skippedNonCapsuleBoxCount"),Skipped); Root->SetNumberField(TEXT("pairCount"),PairCount); Root->SetArrayField(TEXT("cases"),Cases);
    FString Text; if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return Fail(TEXT("Could not write geometry reference."));
    return true;
}
