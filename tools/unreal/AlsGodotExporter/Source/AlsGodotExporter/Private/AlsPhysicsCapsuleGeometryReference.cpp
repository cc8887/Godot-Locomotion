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
namespace AlsCapsuleGeometryReference
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

bool ExportAlsPhysicsCapsuleGeometryReference(const FString& Input,const FString& Output,FString& Error)
{
    using namespace Chaos; using namespace AlsCapsuleGeometryReference; using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if (Input.IsEmpty() || FPaths::IsRelative(Input) || Output.IsEmpty() || FPaths::IsRelative(Output) || IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Capsule geometry reference needs a trace and a new absolute output file."));
    TArray<FString> Lines; if (!FFileHelper::LoadFileToStringArray(Lines,*Input)) return Fail(TEXT("Cannot read trace."));
    auto Root=MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),1); Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native UpdateConstraint capsule-box manifold from captured original shape geometry/poses, cull 0 and 3 cm; no supplied contacts drive native output; no solver or persistent manifold"));
    TArray<TSharedPtr<FJsonValue>> Cases; TArray<TSharedPtr<FJsonObject>> Sources; int32 TraceCount=0,PairCount=0;
    for (const auto& Line:Lines)
    {
        const FString Prefix=TEXT("CORE_CONTACT_TRACE "); const int32 At=Line.Find(Prefix);
        if (At==INDEX_NONE) continue;
        TSharedPtr<FJsonObject> Source; if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Line.Mid(At+Prefix.Len())),Source)) return Fail(TEXT("Invalid contact trace JSON."));
        ++TraceCount; Sources.Add(Source);
    }
    // Independent orientation/separation coverage includes cylinder, end-cap,
    // near duplicate removal, separated supports, all six faces and large world.
    int32 Synthetic=0;
    for (int32 Face=0;Face<6;++Face) for (double Tilt:{0.,.009,.011,.05,.15,.4,.706,.708,1.}) for (double Gap:{-.2,.2,3.2})
    {
        const FVec3 N=Face==0?FVec3(1,0,0):Face==1?FVec3(-1,0,0):Face==2?FVec3(0,1,0):Face==3?FVec3(0,-1,0):Face==4?FVec3(0,0,1):FVec3(0,0,-1);
        const FRotation3 FaceRotation=FRotation3::FromRotatedVector(FVec3(0,0,1),N);
        const FRotation3 LocalQ=FaceRotation*FRotation3::FromAxisAngle(FVec3(0,1,0),FMath::Acos(Tilt));
        const FRotation3 WorldQ=FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),.43);
        const FVec3 WorldP(1e6,-2e6,3e6),Half(100);
        const FVec3 CapsuleP=WorldP+WorldQ.RotateVector(N*(100+5+10*Tilt+Gap));
        const auto CapsuleT=T(FTransform(WorldQ*LocalQ,CapsuleP)),BoxT=T(FTransform(WorldQ,WorldP));
        auto Source=MakeShared<FJsonObject>(); Source->SetNumberField(TEXT("frame"),Synthetic++); Source->SetStringField(TEXT("mesh"),TEXT("synthetic"));
        Source->SetStringField(TEXT("body0"),TEXT("capsule")); Source->SetStringField(TEXT("body1"),TEXT("box"));
        Source->SetNumberField(TEXT("shape0"),0); Source->SetNumberField(TEXT("shape1"),1);
        Source->SetArrayField(TEXT("center0"),V(CapsuleP)); Source->SetArrayField(TEXT("center1"),V(WorldP));
        Source->SetArrayField(TEXT("rotation0"),CapsuleT->GetArrayField(TEXT("rotation"))); Source->SetArrayField(TEXT("rotation1"),BoxT->GetArrayField(TEXT("rotation")));
        auto C=MakeShared<FJsonObject>(); C->SetStringField(TEXT("type"),TEXT("capsule")); C->SetNumberField(TEXT("radius"),5); C->SetNumberField(TEXT("length"),20);
        auto B=MakeShared<FJsonObject>(); B->SetStringField(TEXT("type"),TEXT("box")); B->SetArrayField(TEXT("size"),V(Half*2));
        Source->SetObjectField(TEXT("geometry0"),C); Source->SetObjectField(TEXT("geometry1"),B);
        Source->SetArrayField(TEXT("contacts"),{}); Sources.Add(Source);
    }
    for (const auto& Source:Sources)
    {
        const auto A=Source->GetObjectField(TEXT("geometry0")),B=Source->GetObjectField(TEXT("geometry1"));
        const auto Type0=A->GetStringField(TEXT("type")),Type1=B->GetStringField(TEXT("type"));
        const bool Swapped=Type1==TEXT("capsule") && Type0==TEXT("box");
        if (!Swapped && !(Type0==TEXT("capsule") && Type1==TEXT("box"))) continue;
        ++PairCount; const auto CapsuleData=Swapped?B:A,BoxData=Swapped?A:B;
        const auto CapsulePose=Pose(Source,Swapped?1:0),BoxPose=Pose(Source,Swapped?0:1);
        const double Length=CapsuleData->GetNumberField(TEXT("length")),Radius=CapsuleData->GetNumberField(TEXT("radius"));
        const auto Half=Vector(BoxData,TEXT("size"))*.5;
        if (Length<=0 || Radius<=0 || Half.GetMin()<=0) return Fail(TEXT("Invalid primitive dimensions."));
        for (double Cull:{0.,3.})
        {
            FParticleUniqueIndicesMultithreaded Unique; FPBDRigidsSOAs Particles(Unique); const auto P=Particles.CreateDynamicParticles(2);
            FImplicitObjectPtr Capsule=MakeImplicitObjectPtr<FCapsule>(FVec3(0,0,-Length*.5),FVec3(0,0,Length*.5),Radius);
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
    Root->SetNumberField(TEXT("traceCount"),TraceCount); Root->SetNumberField(TEXT("syntheticCount"),Synthetic); Root->SetNumberField(TEXT("pairCount"),PairCount); Root->SetArrayField(TEXT("cases"),Cases);
    FString Text; if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return Fail(TEXT("Could not write geometry reference."));
    return true;
}
