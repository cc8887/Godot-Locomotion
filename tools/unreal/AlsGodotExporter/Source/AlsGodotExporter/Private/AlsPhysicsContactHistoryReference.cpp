#include "AlsPhysicsAssetExport.h"
#include "Chaos/Box.h"
#include "Chaos/Sphere.h"
#include "Chaos/ChaosPhysicalMaterial.h"
#include "Chaos/PBDCollisionConstraints.h"
#include "Chaos/PBDRigidsSOAs.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"

namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); }

bool ExportAlsPhysicsContactHistoryReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Contact history reference requires a new absolute file."));
    auto* Simple=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.SimpleAssignContact"));
    auto* Restore=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.Manifold.EnableFrictionRestore"));
    const auto* Exact=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.Manifold.FrictionExactPositionTolerance"));
    const auto* Near=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.Manifold.FrictionNearPositionTolerance"));
    if(!Simple||!Restore||!Exact||!Near||Simple->GetInt()!=1||Restore->GetInt()!=1||Exact->GetFloat()!=.2f||Near->GetFloat()!=1)
        return Fail(TEXT("Contact history CVar contract differs."));
    const int32 OriginalSimple=Simple->GetInt(),OriginalRestore=Restore->GetInt();
    ON_SCOPE_EXIT { Simple->SetWithCurrentPriority(OriginalSimple);Restore->SetWithCurrentPriority(OriginalRestore); };
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native Activate/AssignSavedManifoldPoints and SetSolverResults; six successive synthetic manifolds, including an empty frame; supplied friction ratios and updated initial phi, no narrow phase or dynamics"));
    Root->SetNumberField(TEXT("exactTolerance"),Exact->GetFloat());Root->SetNumberField(TEXT("nearTolerance"),Near->GetFloat());
    TArray<TSharedPtr<FJsonValue>> Cases;
    for(bool SimpleMode:{true,false})for(int32 Quadratic=0;Quadratic<4;++Quadratic)for(int32 Count:{1,4,8})for(int32 Scenario=0;Scenario<8;++Scenario)
    {
        Simple->SetWithCurrentPriority(SimpleMode?1:0);Restore->SetWithCurrentPriority(Scenario==7?0:1);
        FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);const auto Pair=Particles.CreateDynamicParticles(2);
        FImplicitObjectPtr Shapes[2];
        for(int32 I=0;I<2;++I)
        {
            if(Quadratic&(1<<I))Shapes[I]=MakeImplicitObjectPtr<TSphere<FReal,3>>(FVec3(0),2);
            else Shapes[I]=MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-2),FVec3(2));
            Pair[I]->SetGeometry(Shapes[I]);Pair[I]->SetX(FVec3(0));Pair[I]->SetR(FRotation3::Identity);
        }
        TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
        TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticleMaterials;
        Collided.Resize(2);Materials.Resize(2);PerParticleMaterials.Resize(2);
        FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticleMaterials,nullptr);
        auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();
        auto* Context=Allocator.GetContextAllocator(0);
        auto Constraint=Context->CreateConstraint(Pair[0],Shapes[0].GetReference(),Pair[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
            Pair[1],Shapes[1].GetReference(),Pair[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,10,true,EContactShapesType::BoxBox);
        Context->ActivateConstraint(Constraint.Get());Allocator.EndDetectCollisions();
        auto Case=MakeShared<FJsonObject>();Case->SetBoolField(TEXT("simple"),SimpleMode);Case->SetBoolField(TEXT("restore"),Scenario!=7);
        Case->SetBoolField(TEXT("quadratic0"),Constraint->IsQuadratic0());Case->SetBoolField(TEXT("quadratic1"),Constraint->IsQuadratic1());
        Case->SetNumberField(TEXT("count"),Count);Case->SetNumberField(TEXT("scenario"),Scenario);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(int32 Frame=0;Frame<6;++Frame)
        {
            Constraint->ResetActiveManifoldContacts();
            const int32 Num=Frame==3?0:Count;
            auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("frame"),Frame);
            Row->SetNumberField(TEXT("priorMinInitialPhi"),Constraint->GetMinInitialPhi());
            TArray<TSharedPtr<FJsonValue>> Inputs;
            for(int32 I=0;I<Num;++I)
            {
                const int32 Index=Scenario==4&&Frame%2?Num-1-I:Scenario==6&&Frame>0?0:I;
                FContactPointf C;C.ShapeContactPoints[0]=FVec3f(.5f*Index,0,-.2f);C.ShapeContactPoints[1]=FVec3f(.75f*Index,0,0);
                const float Shift=(Scenario==1?.3f:Scenario==2?.4f:Scenario==3?.2f:.05f)*Frame;
                C.ShapeContactPoints[0].X+=Shift;C.ShapeContactPoints[1].X+=Shift;
                if(Scenario==5){C.ShapeContactPoints[0].X+=3.f*Frame;C.ShapeContactPoints[1].X-=Shift;}
                C.ShapeContactNormal=FVec3f(0,0,1);C.Phi=-.2f;C.ContactType=EContactPointType::VertexPlane;
                Constraint->AddOneshotManifoldContact(C);
                auto& M=Constraint->GetManifoldPoint(I);M.Flags.bDisabled=Scenario==6&&Frame>0&&I%3==0;
                auto O=MakeShared<FJsonObject>();O->SetArrayField(TEXT("point0"),V(FVec3(C.ShapeContactPoints[0])));
                O->SetArrayField(TEXT("point1"),V(FVec3(C.ShapeContactPoints[1])));O->SetArrayField(TEXT("normal1"),V(FVec3(C.ShapeContactNormal)));
                O->SetBoolField(TEXT("disabled"),M.Flags.bDisabled);Inputs.Add(MakeShared<FJsonValueObject>(O));
            }
            Row->SetArrayField(TEXT("input"),Inputs);Constraint->Activate();
            TArray<TSharedPtr<FJsonValue>> Assigned,Results;
            for(int32 I=0;I<Num;++I)
            {
                const auto& M=Constraint->GetManifoldPoint(I);auto O=MakeShared<FJsonObject>();
                O->SetArrayField(TEXT("anchor0"),V(FVec3(M.ShapeAnchorPoints[0])));O->SetArrayField(TEXT("anchor1"),V(FVec3(M.ShapeAnchorPoints[1])));
                O->SetNumberField(TEXT("initialPhi"),M.InitialPhi);O->SetBoolField(TEXT("hasAnchor"),M.Flags.bHasStaticFrictionAnchor);
                O->SetBoolField(TEXT("initialContact"),M.Flags.bInitialContact);Assigned.Add(MakeShared<FJsonValueObject>(O));
            }
            Constraint->ResetSolverResults();
            for(int32 I=0;I<Num;++I)
            {
                auto& M=Constraint->GetManifoldPoint(I);
                const float Ratios[]={1.f-1.e-4f,1.e-4f,.9998f,.00005f};
                const float Ratio=M.Flags.bDisabled?0:Scenario==1?.35f:Scenario==2?0:Scenario==3?Ratios[I%4]:Scenario==6?.75f:1;
                if(!M.Flags.bDisabled)M.InitialPhi=-.01f*(Frame+1)*(I+1);
                auto R=MakeShared<FJsonObject>();R->SetNumberField(TEXT("ratio"),Ratio);R->SetNumberField(TEXT("initialPhi"),M.InitialPhi);
                Results.Add(MakeShared<FJsonValueObject>(R));
                Constraint->SetSolverResults(I,FVec3f(0),FVec3f(0),Ratio,1.f/60);
            }
            TArray<TSharedPtr<FJsonValue>> Saved;
            for(int32 I=0;I<Constraint->NumSavedManifoldPoints();++I)
            {
                const auto& P=Constraint->GetSavedManifoldPoint(I);auto O=MakeShared<FJsonObject>();
                O->SetArrayField(TEXT("anchor0"),V(FVec3(P.ShapeContactPoints[0])));O->SetArrayField(TEXT("anchor1"),V(FVec3(P.ShapeContactPoints[1])));
                O->SetNumberField(TEXT("initialPhi"),P.InitialPhi);Saved.Add(MakeShared<FJsonValueObject>(O));
            }
            Row->SetArrayField(TEXT("assigned"),Assigned);Row->SetArrayField(TEXT("results"),Results);Row->SetArrayField(TEXT("saved"),Saved);
            Row->SetNumberField(TEXT("minInitialPhi"),Constraint->GetMinInitialPhi());Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Case->SetArrayField(TEXT("frames"),Frames);Cases.Add(MakeShared<FJsonValueObject>(Case));
    }
    Root->SetArrayField(TEXT("cases"),Cases);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json)))return Fail(TEXT("History serialization failed."));
    return FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)||Fail(TEXT("History reference write failed."));
}
