#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/ChaosPhysicalMaterial.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"

namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }

bool ExportAlsPhysicsManifoldRestoreReference(const FString& Output,FString& Error)
{
    using namespace Chaos; using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Manifold restoration reference requires a new absolute file."));
    const auto Point=[](const FContactPointf& C,bool Disabled)
    {
        auto O=MakeShared<FJsonObject>(); O->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));
        O->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1]))); O->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));
        O->SetNumberField(TEXT("phi"),C.Phi); O->SetBoolField(TEXT("disabled"),Disabled); return MakeShared<FJsonValueObject>(O);
    };
    auto Root=MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native TryRestoreManifold on successive supplied box contact inputs; no narrow-phase/solver/trajectory equivalence. Empty manifolds reset as in particle-pair midphase."));
    TArray<TSharedPtr<FJsonValue>> Cases;
    for(int32 Count:{1,4,6})for(int32 Scenario=0;Scenario<12;++Scenario)for(bool Rotated:{false,true})
    {
        FParticleUniqueIndicesMultithreaded Unique; FPBDRigidsSOAs Particles(Unique); const auto Pair=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr Shapes[2]={MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-10),FVec3(10)),
            MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-30,-30,-5),FVec3(30,30,5))};
        for(int32 I=0;I<2;++I){Pair[I]->SetGeometry(Shapes[I]);Pair[I]->SetX(FVec3(0));Pair[I]->SetR(FRotation3::Identity);}
        TArrayCollectionArray<bool> Collided; TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticleMaterials; Collided.Resize(2); Materials.Resize(2); PerParticleMaterials.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticleMaterials,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator(); Allocator.SetMaxContexts(1); Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto C=Context->CreateConstraint(Pair[0],Shapes[0].GetReference(),Pair[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            Pair[1],Shapes[1].GetReference(),Pair[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,3,true,EContactShapesType::BoxBox);
        Context->ActivateConstraint(C.Get()); Allocator.EndDetectCollisions();
        auto Case=MakeShared<FJsonObject>(); Case->SetNumberField(TEXT("count"),Count); Case->SetNumberField(TEXT("scenario"),Scenario);
        Case->SetBoolField(TEXT("rotated"),Rotated); Case->SetNumberField(TEXT("tolerance"),.1f*20.f);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(int32 Frame=0;Frame<8;++Frame)
        {
            FVec3 Translation(0,0,14.9); FQuat LocalRotation=FQuat::Identity;
            FVec3 CommonPosition=Rotated?FVec3(1.e6,-2.e6,3.e6):FVec3(0);
            FQuat CommonRotation=Rotated?FQuat(FVector(1,2,3).GetSafeNormal(),.43):FQuat::Identity;
            if(Scenario==0)Translation.X=.1*Frame;
            if(Scenario==1)Translation.X=Frame==0?0:(Frame%2?.39999:.40001);
            if(Scenario==2)LocalRotation=FQuat(FVector::UpVector,.005*Frame);
            if(Scenario==3)CommonPosition+=FVec3(123456.125,-98765.25,54321.5)*Frame;
            if(Scenario==4)CommonRotation=CommonRotation*FQuat(FVector::RightVector,.005*Frame);
            if(Scenario==5&&Frame%2)LocalRotation=FQuat(0,0,0,-1);
            if(Scenario==7||Scenario==8)Translation.X=.02*Frame;
            if(Scenario==9)Translation.Z+=.1*Frame;
            if(Scenario==10)Translation.X=Frame;
            if(Scenario==11)LocalRotation=FQuat(FVector::ForwardVector,Frame==0?0:(Frame%2?.02828:.02829));
            const FRigidTransform3 P0(CommonPosition+CommonRotation.RotateVector(Translation),CommonRotation*LocalRotation);
            const FRigidTransform3 P1(CommonPosition,CommonRotation); C->SetShapeWorldTransforms(P0,P1);
            if(C->NumManifoldPoints()==0)C->ResetManifold();
            const bool Restored=C->GetCanRestoreManifold()&&C->TryRestoreManifold();
            TArray<TSharedPtr<FJsonValue>> Input;
            if(!Restored)
            {
                C->ResetActiveManifoldContacts(); const int32 Num=Scenario==10&&Frame==3?0:Count;
                const auto Relative=P0.GetRelativeTransformNoScale(P1);
                for(int32 I=0;I<Num;++I)
                {
                    FContactPointf P; P.ShapeContactPoints[0]=FVec3f((I&1)?2:-2,(I&2)?2:-2,-10);
                    if(I>=4)P.ShapeContactPoints[0].X+=1;
                    const auto Projected=Relative.TransformPositionNoScale(FVec3(P.ShapeContactPoints[0]));
                    P.ShapeContactPoints[1]=FVec3f(Projected.X,Projected.Y,5); P.ShapeContactNormal=FVec3f(0,0,1);
                    if(Scenario==7)P.ShapeContactPoints[1].X-=1.59f;
                    if(Scenario==8&&I==Num-1)P.ShapeContactPoints[1].X-=1.61f;
                    P.Phi=float(Projected.Z-5); P.ContactType=EContactPointType::VertexPlane;
                    C->AddOneshotManifoldContact(P); const bool Disabled=Scenario==6&&I==Num-1;
                    C->GetManifoldPoint(I).Flags.bDisabled=Disabled; Input.Add(Point(P,Disabled));
                }
                C->SetLastShapeWorldTransforms(P0,P1);
                if(!C->GetCanRestoreManifold())return Fail(TEXT("Native box manifold restoration unexpectedly disabled."));
            }
            TArray<TSharedPtr<FJsonValue>> Points;
            for(int32 I=0;I<C->NumManifoldPoints();++I){const auto& M=C->GetManifoldPoint(I);Points.Add(Point(M.ContactPoint,M.Flags.bDisabled));}
            auto Row=MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("frame"),Frame); Row->SetObjectField(TEXT("pose0"),T(P0));Row->SetObjectField(TEXT("pose1"),T(P1));
            Row->SetBoolField(TEXT("restored"),Restored);Row->SetArrayField(TEXT("input"),Input);Row->SetArrayField(TEXT("points"),Points);
            if(Restored)Row->SetNumberField(TEXT("minimumPhi"),C->GetPhi());
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Case->SetArrayField(TEXT("frames"),Frames); Cases.Add(MakeShared<FJsonValueObject>(Case));
    }
    Root->SetArrayField(TEXT("cases"),Cases); FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)))return Fail(TEXT("Manifold restoration serialization failed."));
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("Manifold restoration reference write failed."));
}
