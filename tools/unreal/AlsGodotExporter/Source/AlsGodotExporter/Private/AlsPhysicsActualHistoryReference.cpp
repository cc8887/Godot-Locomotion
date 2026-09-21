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
#include "Serialization/JsonSerializer.h"

namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); }
namespace AlsCoupledStepReference { Chaos::FVec3 ReadV(const TSharedPtr<FJsonObject>& J,const TCHAR* Name); }

bool ExportAlsPhysicsActualHistoryReference(const FString& Inputs,const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;using namespace AlsCoupledStepReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Inputs.IsEmpty()||Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))return Fail(TEXT("Actual history needs input directories and a new absolute output."));
    for(const auto& Entry:TArray<TPair<FString,double>>{
        {TEXT("p.Chaos.Collision.SimpleAssignContact"),1},{TEXT("p.Chaos.Collision.Manifold.EnableFrictionRestore"),1},
        {TEXT("p.Chaos.Collision.Manifold.FrictionExactPositionTolerance"),.2f},{TEXT("p.Chaos.Collision.Manifold.FrictionNearPositionTolerance"),1}})
    {const auto* Var=IConsoleManager::Get().FindConsoleVariable(*Entry.Key);if(!Var||Var->GetFloat()!=Entry.Value)return Fail(TEXT("Actual history CVar differs."));}
    TArray<FString> Directories,Files;Inputs.ParseIntoArray(Directories,TEXT("|"),true);
    for(const auto& Directory:Directories)
    {
        if(FPaths::IsRelative(Directory)||!IFileManager::Get().DirectoryExists(*Directory))return Fail(TEXT("Capture directory missing."));
        TArray<FString> Found;IFileManager::Get().FindFilesRecursive(Found,*Directory,TEXT("*.json"),true,false);Files.Append(Found);
    }
    Files.Sort();if(Files.IsEmpty()||Files.Num()>64)return Fail(TEXT("Expected 1..64 history captures."));
    const auto SavedJson=[&](const FPBDCollisionConstraint& Constraint)
    {
        TArray<TSharedPtr<FJsonValue>> Values;
        for(int32 I=0;I<Constraint.NumSavedManifoldPoints();++I)
        {const auto& P=Constraint.GetSavedManifoldPoint(I);auto J=MakeShared<FJsonObject>();J->SetArrayField(TEXT("anchor0"),V(FVec3(P.ShapeContactPoints[0])));
         J->SetArrayField(TEXT("anchor1"),V(FVec3(P.ShapeContactPoints[1])));J->SetNumberField(TEXT("initialPhi"),P.InitialPhi);Values.Add(MakeShared<FJsonValueObject>(J));}
        return Values;
    };
    TArray<TSharedPtr<FJsonValue>> Cases;
    for(const auto& File:Files)
    {
        FString Text;TSharedPtr<FJsonObject> Capture;
        if(!FFileHelper::LoadFileToString(Text,*File)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Capture)||!Capture.IsValid()||
            !Capture->HasTypedField<EJson::Array>(TEXT("historyInputs"))||!Capture->HasTypedField<EJson::Array>(TEXT("historyResults")))return Fail(TEXT("Capture lacks history boundaries."));
        const auto& Histories=Capture->GetArrayField(TEXT("historyInputs"));const auto& Results=Capture->GetArrayField(TEXT("historyResults"));
        if(Histories.Num()!=Results.Num())return Fail(TEXT("History results differ in count."));
        const auto Input=Capture->GetObjectField(TEXT("input"));
        for(int32 PairIndex=0;PairIndex<Histories.Num();++PairIndex)
        {
            const auto H=Histories[PairIndex]->AsObject(),Match=H->GetObjectField(TEXT("matching"));
            const auto& Detected=H->GetArrayField(TEXT("detected"));const auto& Prior=H->GetArrayField(TEXT("saved"));const auto& R=Results[PairIndex]->AsArray();
            if(Detected.Num()>8||Prior.Num()>8||R.Num()!=Detected.Num()||!Match->GetBoolField(TEXT("simpleAssignment"))||!Match->GetBoolField(TEXT("restoreFriction"))||
                float(Match->GetNumberField(TEXT("exactTolerance")))!=.2f||Match->GetNumberField(TEXT("nearTolerance"))!=1)return Fail(TEXT("Unsupported actual history input."));
            FParticleUniqueIndicesMultithreaded Unique;FPBDRigidsSOAs Particles(Unique);const auto P=Particles.CreateDynamicParticles(2);FImplicitObjectPtr Shapes[2];
            for(int32 I=0;I<2;++I)
            {
                if(Match->GetBoolField(I==0?TEXT("quadratic0"):TEXT("quadratic1")))Shapes[I]=MakeImplicitObjectPtr<TSphere<FReal,3>>(FVec3(0),1);
                else Shapes[I]=MakeImplicitObjectPtr<TBox<FReal,3>>(FVec3(-1),FVec3(1));
                P[I]->SetGeometry(Shapes[I]);P[I]->SetX(FVec3(0));P[I]->SetR(FRotation3::Identity);
            }
            TArrayCollectionArray<bool> Collided;TArrayCollectionArray<TSerializablePtr<FChaosPhysicsMaterial>> Materials;
            TArrayCollectionArray<TUniquePtr<FChaosPhysicsMaterial>> PerParticle;Collided.Resize(2);Materials.Resize(2);PerParticle.Resize(2);
            FPBDCollisionConstraints Constraints(Particles,Collided,Materials,PerParticle,nullptr);
            auto& Allocator=Constraints.GetConstraintAllocator();Allocator.SetMaxContexts(1);Allocator.BeginDetectCollisions();auto* Context=Allocator.GetContextAllocator(0);
            auto Constraint=Context->CreateConstraint(P[0],Shapes[0].GetReference(),P[0]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,
                P[1],Shapes[1].GetReference(),P[1]->ShapesArray()[0].Get(),nullptr,FRigidTransform3::Identity,10,true,EContactShapesType::BoxBox);
            Context->ActivateConstraint(Constraint.Get());Allocator.EndDetectCollisions();
            // Seed only eligible old anchors through public native scatter. The
            // capture's world identity/epoch decision is explicit input here.
            const bool Initial=H->GetBoolField(TEXT("initialManifold"));
            if(!Initial)
            {
                for(const auto& Value:Prior)
                {
                    const auto S=Value->AsObject();FContactPointf C;C.ShapeContactPoints[0]=FVec3f(ReadV(S,TEXT("anchor0")));
                    C.ShapeContactPoints[1]=FVec3f(ReadV(S,TEXT("anchor1")));C.ShapeContactNormal=FVec3f(0,0,1);C.Phi=0;C.ContactType=EContactPointType::VertexPlane;
                    Constraint->AddOneshotManifoldContact(C);
                }
                Constraint->ResetSolverResults();
                for(int32 I=0;I<Prior.Num();++I)
                {Constraint->GetManifoldPoint(I).InitialPhi=Prior[I]->AsObject()->GetNumberField(TEXT("initialPhi"));Constraint->SetSolverResults(I,FVec3f(0),FVec3f(0),1,1.f/60);}
            }
            auto Seed=SavedJson(*Constraint);Constraint->ResetActiveManifoldContacts();
            for(const auto& Value:Detected)
            {
                const auto D=Value->AsObject();FContactPointf C;C.ShapeContactPoints[0]=FVec3f(ReadV(D,TEXT("point0")));
                C.ShapeContactPoints[1]=FVec3f(ReadV(D,TEXT("point1")));C.ShapeContactNormal=FVec3f(ReadV(D,TEXT("normal1")));C.Phi=0;C.ContactType=EContactPointType::VertexPlane;
                Constraint->AddOneshotManifoldContact(C);Constraint->GetManifoldPoint(Constraint->NumManifoldPoints()-1).Flags.bDisabled=D->GetBoolField(TEXT("disabled"));
            }
            Constraint->Activate();TArray<TSharedPtr<FJsonValue>> Assigned;
            for(int32 I=0;I<Detected.Num();++I)
            {
                const auto& M=Constraint->GetManifoldPoint(I);auto J=MakeShared<FJsonObject>();J->SetArrayField(TEXT("anchor0"),V(FVec3(M.ShapeAnchorPoints[0])));
                J->SetArrayField(TEXT("anchor1"),V(FVec3(M.ShapeAnchorPoints[1])));J->SetNumberField(TEXT("initialPhi"),M.InitialPhi);
                J->SetBoolField(TEXT("hasAnchor"),M.Flags.bHasStaticFrictionAnchor);J->SetBoolField(TEXT("initialContact"),M.Flags.bInitialContact);Assigned.Add(MakeShared<FJsonValueObject>(J));
            }
            Constraint->ResetSolverResults();
            for(int32 I=0;I<Detected.Num();++I)
            {
                auto& M=Constraint->GetManifoldPoint(I);const auto Result=R[I]->AsObject();
                if(!M.Flags.bDisabled)M.InitialPhi=Result->GetNumberField(TEXT("initialPhi"));
                Constraint->SetSolverResults(I,FVec3f(0),FVec3f(0),M.Flags.bDisabled?0:Result->GetNumberField(TEXT("ratio")),1.f/60);
            }
            auto Case=MakeShared<FJsonObject>();Case->SetStringField(TEXT("mesh"),Input->GetStringField(TEXT("mesh")));Case->SetNumberField(TEXT("frame"),Input->GetNumberField(TEXT("frame")));
            Case->SetStringField(TEXT("scenario"),FPaths::GetCleanFilename(FPaths::GetPath(File)));Case->SetObjectField(TEXT("input"),H);Case->SetArrayField(TEXT("results"),R);
            Case->SetArrayField(TEXT("nativeSeed"),Seed);Case->SetArrayField(TEXT("nativeAssigned"),Assigned);Case->SetArrayField(TEXT("nativeSaved"),SavedJson(*Constraint));
            Case->SetNumberField(TEXT("nativeMinInitialPhi"),Constraint->GetMinInitialPhi());Cases.Add(MakeShared<FJsonValueObject>(Case));
        }
    }
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native AssignSavedManifoldPoints and SetSolverResults from actual captured prior anchors, detected points and solved friction ratios; eligibility and solve results are supplied inputs, not native trajectory or identity lifecycle validation"));
    Root->SetArrayField(TEXT("cases"),Cases);FString Text;
    return (FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Text))&&FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))||Fail(TEXT("Actual history write failed."));
}
