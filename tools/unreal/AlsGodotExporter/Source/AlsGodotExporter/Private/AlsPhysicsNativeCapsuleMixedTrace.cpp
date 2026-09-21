#include "AlsPhysicsAssetExport.h"
#include "Chaos/Convex.h"
#include "Chaos/ImplicitObjectScaled.h"
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
namespace AlsNativeCapsuleMixedReference
{
using namespace Chaos;
FVec3 Vector(const TSharedPtr<FJsonObject>& J,const TCHAR* Name)
{ const auto& A=J->GetArrayField(Name); return FVec3(A[0]->AsNumber(),A[1]->AsNumber(),A[2]->AsNumber()); }
FRigidTransform3 Pose(const TSharedPtr<FJsonObject>& J,int32 Index)
{
    const auto& Q=J->GetArrayField(Index==0?TEXT("rotation0"):TEXT("rotation1"));
    return FRigidTransform3(Vector(J,Index==0?TEXT("center0"):TEXT("center1")),FRotation3(FQuat(Q[0]->AsNumber(),Q[1]->AsNumber(),Q[2]->AsNumber(),Q[3]->AsNumber())));
}
FString Fingerprint(const FConvex& Hull)
{
    uint64 Hash=14695981039346656037ULL;
    const auto Word=[&](uint32 Value){for(int32 I=0;I<4;++I){Hash=(Hash^static_cast<uint8>(Value))*1099511628211ULL;Value>>=8;}};
    const auto Scalar=[&](float Value){uint32 Bits;FMemory::Memcpy(&Bits,&Value,4);Word(Bits);};
    const auto Vector=[&](FVec3 Value){Scalar(static_cast<float>(Value.X));Scalar(static_cast<float>(Value.Y));Scalar(static_cast<float>(Value.Z));};
    Word(Hull.NumVertices());Word(Hull.NumPlanes());Scalar(Hull.GetMarginf());
    for(int32 I=0;I<Hull.NumVertices();++I)Vector(Hull.GetVertex(I));
    for(int32 I=0;I<Hull.NumPlanes();++I)
    {
        FVec3 N,X;Hull.GetPlaneNX(I,N,X);Vector(N);Vector(X);Word(Hull.NumPlaneVertices(I));
        for(int32 J=0;J<Hull.NumPlaneVertices(I);++J)Word(Hull.GetPlaneVertex(I,J));
    }
    for(int32 I=0;I<Hull.NumVertices();++I)
    {
        int32 P0=INDEX_NONE,P1=INDEX_NONE,P2=INDEX_NONE;Word(Hull.GetVertexPlanes3(I,P0,P1,P2));
        Word(P0);Word(P1);Word(P2);
    }
    return FString::Printf(TEXT("%016llx"),Hash);
}

}

bool ExportAlsPhysicsNativeCapsuleMixedTrace(const FString& Input,const FString& Output,FString& Error)
{
    using namespace Chaos; using namespace AlsNativeCapsuleMixedReference; using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if (Input.IsEmpty() || FPaths::IsRelative(Input) || Output.IsEmpty() || FPaths::IsRelative(Output) || IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Capsule geometry reference needs a trace and a new absolute output file."));
    TArray<FString> Lines; if (!FFileHelper::LoadFileToStringArray(Lines,*Input)) return Fail(TEXT("Cannot read trace."));
    auto Root=MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),1); Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native UpdateConstraint capsule-capsule/capsule-convex; exact native capsule match and cooked convex fingerprint/scale/margin match; actual poses, float cull and dynamic ownership; source contacts never drive output"));
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
        const bool CapsulePair=Type0==TEXT("native_capsule")&&Type1==TEXT("native_capsule");
        const bool Swapped=Type1==TEXT("native_capsule")&&Type0==TEXT("native_convex");
        if(!CapsulePair&&!Swapped&&!(Type0==TEXT("native_capsule")&&Type1==TEXT("native_convex"))){++Skipped;continue;}
        ++PairCount;
        const auto Data0=Swapped?B:A,Data1=Swapped?A:B;
        const auto Pose0=Pose(Source,Swapped?1:0),Pose1=Pose(Source,Swapped?0:1);
        bool Dynamic0,Dynamic1;
        if(!Data0->TryGetBoolField(TEXT("dynamic"),Dynamic0)||!Data1->TryGetBoolField(TEXT("dynamic"),Dynamic1))
            return Fail(TEXT("Trace requires dynamic body ownership for both shapes."));
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
        const auto Resolve=[&](const TSharedPtr<FJsonObject>& Data,int32 Index)->FImplicitObjectPtr
        {
            const auto* Instance=Component->GetBodyInstance(FName(*Source->GetStringField(Index==0?TEXT("body0"):TEXT("body1"))));
            if(!Instance||!Instance->IsValidBodyInstance()||!Instance->GetPhysicsActorHandle())return nullptr;
            const FImplicitObject* Match=nullptr;
            for(const auto& Shape:Instance->GetPhysicsActorHandle()->GetGameThreadAPI().ShapesArray())
            {
                const auto* Leaf=Shape->GetLeafGeometry();if(!Leaf)continue;
                bool Matches=false;
                if(Data->GetStringField(TEXT("type"))==TEXT("native_capsule"))
                {
                    const auto* C=Leaf->GetObject<FCapsule>();
                    Matches=C&&C->GetX1f()==FVec3f(Vector(Data,TEXT("endpoint0")))&&C->GetAxisf()==FVec3f(Vector(Data,TEXT("axis")))&&
                        C->GetHeightf()==static_cast<float>(Data->GetNumberField(TEXT("length")))&&C->GetRadiusf()==static_cast<float>(Data->GetNumberField(TEXT("radius")));
                }
                else
                {
                    const FImplicitObject* Inner=Leaf;FVec3 Scale(1);
                    if(IsScaled(Leaf->GetType())){const auto* S=static_cast<const FImplicitObjectScaled*>(Leaf);Inner=S->GetInnerObject().Get();Scale=FVec3(S->GetScale());}
                    else if(IsInstanced(Leaf->GetType()))Inner=static_cast<const FImplicitObjectInstanced*>(Leaf)->GetInnerObject().Get();
                    const auto* Hull=Inner->GetObject<FConvex>();
                    Matches=Hull&&Scale==Vector(Data,TEXT("scale"))&&Leaf->GetMarginf()==static_cast<float>(Data->GetNumberField(TEXT("margin")))&&
                        Fingerprint(*Hull)==Data->GetStringField(TEXT("fingerprint"));
                }
                if(Matches){if(Match)return nullptr;Match=Leaf;}
            }
            return Match?Match->CopyGeometry():FImplicitObjectPtr();
        };
        auto Geometry0=Resolve(Data0,Swapped?1:0),Geometry1=Resolve(Data1,Swapped?0:1);
        if(!Geometry0||!Geometry1)return Fail(TEXT("Trace geometry differs from native body or is ambiguous; refusing reconstruction."));
        double Cull;
        if(!Source->TryGetNumberField(TEXT("cullDistance"),Cull)||!FMath::IsFinite(Cull)||Cull<0||Cull>MAX_flt)
            return Fail(TEXT("Trace requires actual finite cull distance."));
        Cull=static_cast<float>(Cull);

        {
            FParticleUniqueIndicesMultithreaded Unique; FPBDRigidsSOAs Particles(Unique); const auto P=Particles.CreateDynamicParticles(2);
            P[0]->SetGeometry(Geometry0); P[1]->SetGeometry(Geometry1);
            P[0]->SetObjectStateLowLevel(Dynamic0?EObjectStateType::Dynamic:EObjectStateType::Kinematic);
            P[1]->SetObjectStateLowLevel(Dynamic1?EObjectStateType::Dynamic:EObjectStateType::Kinematic);
            for (auto* Body:P) { Body->SetX(FVec3(0)); Body->SetR(FRotation3::Identity); }
            TArrayCollectionArray<bool> Collided; TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
            TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticleMaterials;
            Collided.Resize(2); Materials.Resize(2); PerParticleMaterials.Resize(2);
            FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticleMaterials,nullptr);
            auto& Allocator=Constraints.GetConstraintAllocator(); Allocator.SetMaxContexts(1); Allocator.BeginDetectCollisions(); auto* Context=Allocator.GetContextAllocator(0);
            auto Constraint=Context->CreateConstraint(P[0],Geometry0.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
                P[1],Geometry1.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,CapsulePair?EContactShapesType::CapsuleCapsule:EContactShapesType::CapsuleConvex);
            Context->ActivateConstraint(Constraint.Get()); Allocator.EndDetectCollisions();
            Constraint->SetShapeWorldTransforms(Pose0,Pose1);
            Collisions::UpdateConstraint(*Constraint,Pose0,Pose1,1./60.);
            auto Row=MakeShared<FJsonObject>(); Row->SetObjectField(TEXT("source"),Source); Row->SetBoolField(TEXT("swapped"),Swapped);
            Row->SetNumberField(TEXT("cullDistance"),Cull);Row->SetStringField(TEXT("kind"),CapsulePair?TEXT("capsule_pair"):TEXT("capsule_convex"));
            Row->SetObjectField(TEXT("pose0"),T(FTransform(Pose0)));Row->SetObjectField(TEXT("pose1"),T(FTransform(Pose1)));
            TArray<TSharedPtr<FJsonValue>> Points;
            for (int32 I=0;I<Constraint->NumManifoldPoints();++I)
            {
                const auto& M=Constraint->GetManifoldPoint(I); const auto& C=M.ContactPoint;
                if(C.ShapeContactPoints[0].ContainsNaN()||C.ShapeContactPoints[1].ContainsNaN()||C.ShapeContactNormal.ContainsNaN()||!FMath::IsFinite(C.Phi))
                    return Fail(TEXT("Nonfinite native contact; this replay requires separate degenerate handling."));
                auto Point=MakeShared<FJsonObject>(); Point->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));
                Point->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1]))); Point->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));
                Point->SetNumberField(TEXT("phi"),C.Phi); Point->SetNumberField(TEXT("contactType"),static_cast<int32>(C.ContactType));
                Point->SetBoolField(TEXT("disabled"),M.Flags.bDisabled); Points.Add(MakeShared<FJsonValueObject>(Point));
            }
            Row->SetArrayField(TEXT("points"),Points); Cases.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    if (PairCount==0) return Fail(TEXT("No captured capsule mixed pairs."));
    Root->SetNumberField(TEXT("traceCount"),TraceCount); Root->SetNumberField(TEXT("skippedOtherCount"),Skipped); Root->SetNumberField(TEXT("pairCount"),PairCount); Root->SetArrayField(TEXT("cases"),Cases);
    FString Text; if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return Fail(TEXT("Could not write geometry reference."));
    return true;
}
