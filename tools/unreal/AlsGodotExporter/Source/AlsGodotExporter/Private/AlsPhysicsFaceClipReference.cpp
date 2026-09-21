#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/Convex.h"
#include "Chaos/CollisionResolution.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }

bool ExportAlsPhysicsFaceClipReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Face clipping reference requires a new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native initial prism-box UpdateConstraint; exported authored-selected faces, no supplied contacts; planeCase classifies the observed native contact type and normal, not GJK parity"));
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(int32 Sides:{3,4,5,8,16,40})for(int32 Face=0;Face<6;++Face)
    for(double Tilt:{0.,.07,.3})for(double Offset:{0.,8.,11.})for(double Gap:{-.2,.2})
    {
        TArray<FConvex::FVec3Type> Vertices;
        for(int32 Z:{-1,1})for(int32 I=0;I<Sides;++I)
        {
            const double Angle=2.*PI*I/Sides+.173;
            Vertices.Add(FConvex::FVec3Type(4*FMath::Cos(Angle),4*FMath::Sin(Angle),Z*2.));
        }
        auto A=MakeImplicitObjectPtr<FConvex>(Vertices,0.);
        auto B=MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-10),FVec3(10));
        const auto* Hull=A->GetObject<FConvex>();
        int32 IncidentFace=INDEX_NONE;
        for(int32 I=0;I<Hull->NumPlanes();++I)
        {
            FVec3 N,X;Hull->GetPlaneNX(I,N,X);
            if(N.Z < -.99999){IncidentFace=I;break;}
        }
        if(IncidentFace==INDEX_NONE)return Fail(TEXT("Prism has no bottom face."));
        const FVec3 N=Face==0?FVec3(-1,0,0):Face==1?FVec3(0,-1,0):Face==2?FVec3(0,0,-1):Face==3?FVec3(1,0,0):Face==4?FVec3(0,1,0):FVec3(0,0,1);
        const FVec3 X=N*10;
        const auto FaceQ=N.Z < -.999 ? FRotation3::FromAxisAngle(FVec3(1,0,0),PI) : FRotation3::FromRotatedVector(FVec3(0,0,1),N);
        const auto Q=FaceQ*FRotation3::FromAxisAngle(FVec3(0,1,0),Tilt);
        double Minimum=DBL_MAX;
        for(int32 I=0;I<Hull->NumVertices();++I)Minimum=FMath::Min(Minimum,FVec3::DotProduct(Q.RotateVector(FVec3(Hull->GetVertex(I))),N));
        const FRigidTransform3 Pose0(N*(10-Minimum+Gap)+FaceQ.RotateVector(FVec3(Offset,.137,0)),Q);
        const FRigidTransform3 Pose1=FRigidTransform3::Identity;
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);auto P=Particles.CreateDynamicParticles(2);
        P[0]->SetGeometry(A);P[1]->SetGeometry(B);
        for(auto* Body:P){Body->SetX(FVec3(0));Body->SetR(FRotation3::Identity);}
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;
        Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto Constraint=Context->CreateConstraint(P[0],A.GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            P[1],B.GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,3.,true,EContactShapesType::BoxConvex);
        Context->ActivateConstraint(Constraint.Get());Allocator.EndDetectCollisions();
        Constraint->SetShapeWorldTransforms(Pose0,Pose1);
        Collisions::UpdateConstraint(*Constraint,Pose0,Pose1,1./60.);
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("sides"),Sides);Row->SetNumberField(TEXT("face"),Face);
        Row->SetNumberField(TEXT("tilt"),Tilt);Row->SetNumberField(TEXT("offset"),Offset);Row->SetNumberField(TEXT("gap"),Gap);
        Row->SetObjectField(TEXT("incidentToReference"),T(FTransform(Pose0)));
        Row->SetArrayField(TEXT("referenceNormal"),V(N));Row->SetArrayField(TEXT("referencePoint"),V(X));
        TArray<TSharedPtr<FJsonValue>> Incident,Reference,Points;
        for(int32 I=0;I<Hull->NumPlaneVertices(IncidentFace);++I)Incident.Add(MakeShared<FJsonValueArray>(V(FVec3(Hull->GetVertex(Hull->GetPlaneVertex(IncidentFace,I))))));
        // Authored box face inputs; TBox inline accessors depend on private non-exported static tables.
        const int32 BoxFaces[24]={0,4,6,2,0,1,5,4,0,2,3,1,1,3,7,5,2,6,7,3,4,5,7,6};
        for(int32 I=0;I<4;++I){const int32 Index=BoxFaces[Face*4+I];Reference.Add(MakeShared<FJsonValueArray>(V(FVec3((Index&1)?10:-10,(Index&2)?10:-10,(Index&4)?10:-10))));}
        bool PlaneCase=Constraint->NumManifoldPoints()>0;
        for(int32 I=0;I<Constraint->NumManifoldPoints();++I)
        {
            const auto& C=Constraint->GetManifoldPoint(I).ContactPoint;auto Point=MakeShared<FJsonObject>();
            Point->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));Point->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1])));
            Point->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));Point->SetNumberField(TEXT("phi"),C.Phi);
            PlaneCase &= C.ContactType==EContactPointType::VertexPlane && FVec3::DotProduct(FVec3(C.ShapeContactNormal),N)>.999999;
            Points.Add(MakeShared<FJsonValueObject>(Point));
        }
        Row->SetArrayField(TEXT("incidentFace"),Incident);Row->SetArrayField(TEXT("referenceFace"),Reference);
        Row->SetBoolField(TEXT("planeCase"),PlaneCase);Row->SetArrayField(TEXT("points"),Points);Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"),Rows);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save face clipping reference."));
    return true;
}
