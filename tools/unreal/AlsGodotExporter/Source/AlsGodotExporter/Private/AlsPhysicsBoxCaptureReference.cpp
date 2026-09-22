#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/CollisionResolution.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"

namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }
namespace AlsCoupledStepReference {
Chaos::FVec3 ReadV(const TSharedPtr<FJsonObject>& J,const TCHAR* Name);
Chaos::FRigidTransform3 ReadT(const TSharedPtr<FJsonObject>& J);
}

bool ExportAlsPhysicsBoxCaptureReference(const FString& Input,const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;using namespace AlsCoupledStepReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(FPaths::IsRelative(Input)||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Box capture needs an absolute input and new output."));
    FString Text;TSharedPtr<FJsonObject> Root;
    if(!FFileHelper::LoadFileToString(Text,*Input)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Root)||
        !Root.IsValid()||Root->GetIntegerField(TEXT("schemaVersion"))!=1)return Fail(TEXT("Invalid box capture input."));
    const auto& Inputs=Root->GetArrayField(TEXT("cases"));if(Inputs.IsEmpty()||Inputs.Num()>256)return Fail(TEXT("Expected 1..256 poses."));
    TArray<TSharedPtr<FJsonValue>> Cases;bool Seeded=false,RestoreCapture=false;
    for(const auto& Value:Inputs)
    {
        const auto In=Value->AsObject();const FVec3 Half0=ReadV(In,TEXT("half0")),Half1=ReadV(In,TEXT("half1"));
        if(Half0.Min()<=0||Half1.Min()<=0)return Fail(TEXT("Invalid box half extents."));
        const auto Pose0=ReadT(In->GetObjectField(TEXT("pose0"))),Pose1=ReadT(In->GetObjectField(TEXT("pose1")));
        const float Margin0=In->GetNumberField(TEXT("margin0"));if(Margin0<0)return Fail(TEXT("Invalid box margin."));
        const bool HasRetained=In->HasTypedField<EJson::Object>(TEXT("retained"));
        const FReal Cull=HasRetained?In->GetNumberField(TEXT("cullDistance")):6;
        if(!FMath::IsFinite(Cull)||Cull<0)return Fail(TEXT("Invalid cull distance."));
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr Geometry0=MakeImplicitObjectPtr<FImplicitBox3>(-Half0,Half0,Margin0);
        FImplicitObjectPtr Geometry1=MakeImplicitObjectPtr<FImplicitBox3>(-Half1,Half1,0);
        P[0]->SetGeometry(Geometry0);P[1]->SetGeometry(Geometry1);
        for(auto* Body:P){Body->SetX(FVec3(0));Body->SetR(FRotation3::Identity);}
        P[1]->SetObjectStateLowLevel(EObjectStateType::Static);
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto Constraint=Context->CreateConstraint(P[0],Geometry0.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],Geometry1.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,Cull,true,EContactShapesType::BoxBox);
        Context->ActivateConstraint(Constraint.Get());Allocator.EndDetectCollisions();
        if(Constraint->GetCollisionMargin0()!=Margin0||Constraint->GetCollisionMargin1()!=0)return Fail(TEXT("Pair margins differ."));
        if(HasRetained)
        {
            RestoreCapture=true;const auto Retained=In->GetObjectField(TEXT("retained"));
            const auto& Previous=Retained->GetArrayField(TEXT("points"));
            if(Previous.IsEmpty()||Previous.Num()>8)return Fail(TEXT("Restore capture requires 1..8 retained points."));
            // Geometry determines the native constraint tolerance. Do not replace
            // it with a caller-supplied value to conceal a runtime mismatch.
            const float Tolerance=.1f*FMath::Min(float(Geometry0->BoundingBox().Extents().GetAbsMax()),float(Geometry1->BoundingBox().Extents().GetAbsMax()));
            if(Tolerance!=float(Retained->GetNumberField(TEXT("tolerance"))))return Fail(TEXT("Captured and native geometry tolerances differ."));
            Constraint->ResetActiveManifoldContacts();
            for(int32 I=0;I<Previous.Num();++I)
            {
                const auto Source=Previous[I]->AsObject();FContactPointf Contact;
                Contact.ShapeContactPoints[0]=FVec3f(ReadV(Source,TEXT("point0")));
                Contact.ShapeContactPoints[1]=FVec3f(ReadV(Source,TEXT("point1")));
                Contact.ShapeContactNormal=FVec3f(ReadV(Source,TEXT("normal1")));
                Contact.Phi=Source->GetNumberField(TEXT("phi"));Contact.ContactType=EContactPointType::VertexPlane;
                Constraint->AddOneshotManifoldContact(Contact);
                auto& Manifold=Constraint->GetManifoldPoint(I);
                Manifold.InitialShapeContactPoints[0]=FVec3f(ReadV(Source,TEXT("initial0")));
                Manifold.InitialShapeContactPoints[1]=FVec3f(ReadV(Source,TEXT("initial1")));
                Manifold.Flags.bDisabled=Source->GetBoolField(TEXT("disabled"));
            }
            const auto& Q=Retained->GetArrayField(TEXT("rotationDelta"));
            if(Q.Num()!=4)return Fail(TEXT("Invalid reference rotation."));
            const FRotation3 ReferenceRotation(FQuat(Q[0]->AsNumber(),Q[1]->AsNumber(),Q[2]->AsNumber(),Q[3]->AsNumber()));
            // This public setter stores translation0-translation1 and inverse(q0)*q1
            // as floats. These constructed poses reproduce those captured fields;
            // they are not a claimed prior pair of physical world poses.
            Constraint->SetLastShapeWorldTransforms(
                FRigidTransform3(FVec3(FVec3f(ReadV(Retained,TEXT("positionDelta")))),FRotation3::Identity),
                FRigidTransform3(FVec3(0),ReferenceRotation));
            if(!Constraint->GetCanRestoreManifold())return Fail(TEXT("Native box restoration unexpectedly disabled."));
            Constraint->SetShapeWorldTransforms(Pose0,Pose1);
            const bool Restored=Constraint->TryRestoreManifold();
            auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("input"),In);
            Row->SetBoolField(TEXT("restored"),Restored);Row->SetNumberField(TEXT("tolerance"),Tolerance);
            TArray<TSharedPtr<FJsonValue>> Points;
            if(Restored)
            {
                Row->SetNumberField(TEXT("minimumPhi"),Constraint->GetPhi());
                Row->SetBoolField(TEXT("withinCullDistance"),Constraint->GetPhi()<=Cull);
                for(int32 I=0;I<Constraint->NumManifoldPoints();++I)
                {
                    const auto& M=Constraint->GetManifoldPoint(I);const auto& C=M.ContactPoint;auto J=MakeShared<FJsonObject>();
                    J->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));
                    J->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1])));
                    J->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));
                    J->SetNumberField(TEXT("phi"),C.Phi);J->SetBoolField(TEXT("disabled"),M.Flags.bDisabled);
                    Points.Add(MakeShared<FJsonValueObject>(J));
                }
            }
            Row->SetArrayField(TEXT("points"),Points);Cases.Add(MakeShared<FJsonValueObject>(Row));continue;
        }
        const bool HasSeed=In->HasTypedField<EJson::Array>(TEXT("cacheBefore"));Seeded|=HasSeed;
        if(HasSeed)
        {
            const auto& Seed=In->GetArrayField(TEXT("cacheBefore"));if(Seed.Num()>4)return Fail(TEXT("Invalid GJK seed count."));
            auto& Cache=Constraint->GetGJKWarmStartData();Cache.NumVerts=Seed.Num();
            for(int32 I=0;I<Seed.Num();++I)
            {const auto SeedRow=Seed[I]->AsObject();Cache.As[I]=ReadV(SeedRow,TEXT("a"));Cache.Bs[I]=ReadV(SeedRow,TEXT("b"));Cache.Barycentric[I]=SeedRow->GetNumberField(TEXT("weight"));}
        }
        auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("input"),In);
        Row->SetObjectField(TEXT("shape1To0"),T(FTransform(Pose1.GetRelativeTransformNoScale(Pose0))));
        TArray<TSharedPtr<FJsonValue>> Passes,Caches;
        for(int32 Pass=0;Pass<2;++Pass)
        {
            // Re-run narrow phase with the same pose. Pass 1 keeps the GJK
            // cache produced by pass 0; neither pass restores a manifold.
            Constraint->ResetActiveManifoldContacts();Constraint->SetShapeWorldTransforms(Pose0,Pose1);
            Collisions::UpdateConstraint(*Constraint,Pose0,Pose1,1./120.);
            TArray<TSharedPtr<FJsonValue>> Points;
            for(int32 I=0;I<Constraint->NumManifoldPoints();++I)
            {
                const auto& C=Constraint->GetManifoldPoint(I).ContactPoint;auto J=MakeShared<FJsonObject>();
                J->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));
                J->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1])));
                J->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));J->SetNumberField(TEXT("phi"),C.Phi);
                Points.Add(MakeShared<FJsonValueObject>(J));
            }
            Passes.Add(MakeShared<FJsonValueArray>(Points));
            if(HasSeed)
            {
                TArray<TSharedPtr<FJsonValue>> Saved;const auto& Cache=Constraint->GetGJKWarmStartData();
                for(int32 I=0;I<Cache.NumVerts;++I)
                {auto J=MakeShared<FJsonObject>();J->SetArrayField(TEXT("a"),V(Cache.As[I]));J->SetArrayField(TEXT("b"),V(Cache.Bs[I]));J->SetNumberField(TEXT("weight"),Cache.Barycentric[I]);Saved.Add(MakeShared<FJsonValueObject>(J));}
                Caches.Add(MakeShared<FJsonValueArray>(Saved));
            }
        }
        Row->SetArrayField(TEXT("passes"),Passes);if(HasSeed)Row->SetArrayField(TEXT("caches"),Caches);Cases.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetNumberField(TEXT("schemaVersion"),1);
    Result->SetStringField(TEXT("observation"),TEXT("Native BoxBox UpdateConstraint at captured world poses, cull 6 cm, cold then same-pose GJK warm start; no actual trajectory cache or manifold restore replay"));
    if(Seeded)Result->SetStringField(TEXT("observation"),TEXT("Native BoxBox UpdateConstraint from captured poses and actual Core query input GJK caches, cull 6 cm; native recalculates points/cache, no manifold restore or full-world lifecycle replay"));
    if(RestoreCapture)Result->SetStringField(TEXT("observation"),TEXT("Native TryRestoreManifold from captured nonempty retained points/initial points/reference deltas/current world poses and actual box geometry; owner identity/epoch eligibility supplied, not full-world lifecycle or trajectory replay"));
    Result->SetObjectField(TEXT("provenance"),Root->GetObjectField(TEXT("provenance")));Result->SetArrayField(TEXT("cases"),Cases);
    return (FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Text))&&
        FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))||Fail(TEXT("Box capture write failed."));
}
