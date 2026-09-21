#include "AlsPhysicsAssetExport.h"
#include "Chaos/Simplex.h"
#include "Chaos/Convex.h"
#include "Engine/SkeletalMesh.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Math/RandomStream.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); }

bool ExportAlsPhysicsGjkPrimitivesReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("GJK primitives require a new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native double indexed SimplexFindClosestToOrigin and real cooked foot SupportCoreScaled at zero margin; active arrays only; not full GJK/EPA or contact generation"));
    auto* UseGjk2=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.UseGJK2"));
    auto* GjkEpsilon=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.GJKEpsilon"));
    auto* EpaEpsilon=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.EPAEpsilon"));
    if(!UseGjk2||!GjkEpsilon||!EpaEpsilon)return Fail(TEXT("Missing native GJK settings."));
    Root->SetBoolField(TEXT("useGjk2"),UseGjk2->GetBool());Root->SetNumberField(TEXT("gjkEpsilon"),GjkEpsilon->GetFloat());
    Root->SetNumberField(TEXT("epaEpsilon"),EpaEpsilon->GetFloat());
    TArray<TSharedPtr<FJsonValue>> Cases,Hulls;
    FRandomStream Random(90421);
    for(int32 Count=1;Count<=4;++Count)for(int32 Sample=0;Sample<144;++Sample)
    {
        FVec3 P[4],A[4],B[4];FReal W[4]={0,0,0,0};FSimplex Ids;Ids.NumVerts=Count;
        const double Scale=Sample%3==0?1.e-9:Sample%3==1?1.:1.e6;
        for(int32 I=0;I<4;++I)
        {
            P[I]=FVec3(Random.FRandRange(-4,4),Random.FRandRange(-4,4),Random.FRandRange(-4,4))*Scale;
            if(Sample<12) P[I]=FVec3(I-1.5,Sample%2==0?0.:1.,0)*Scale;
            if(Sample>=12&&Sample<24)P[I]=FVec3(1,2,3)*Scale;
            if(Sample>=24&&Sample<36)P[I]=FVec3((I&1)?1:-1,(I&2)?1:-1,0)*Scale;
            if(Sample>=36&&Sample<48)P[I]=I==0?FVec3(1,0,0)*Scale:I==1?FVec3(0,1,0)*Scale:I==2?FVec3(0,0,1)*Scale:FVec3(-1,-1,-1)*Scale;
            if(Sample>=48&&Sample<60)P[I]=I==0?FVec3(0):FVec3(I,I*.5,I*.25)*Scale;
            A[I]=FVec3(I+1,13+I,29-I);B[I]=A[I]-P[I];Ids[I]=I;
        }
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("inputCount"),Count);Row->SetNumberField(TEXT("sample"),Sample);
        TArray<TSharedPtr<FJsonValue>> Inputs,As,Bs;
        for(int32 I=0;I<Count;++I){Inputs.Add(MakeShared<FJsonValueArray>(V(P[I])));As.Add(MakeShared<FJsonValueArray>(V(A[I])));Bs.Add(MakeShared<FJsonValueArray>(V(B[I])));}
        Row->SetArrayField(TEXT("points"),Inputs);Row->SetArrayField(TEXT("witnessA"),As);Row->SetArrayField(TEXT("witnessB"),Bs);
        const FVec3 Closest=SimplexFindClosestToOrigin(P,Ids,W,A,B);
        Row->SetArrayField(TEXT("closest"),V(Closest));Row->SetNumberField(TEXT("count"),Ids.NumVerts);
        TArray<TSharedPtr<FJsonValue>> Active;
        for(int32 I=0;I<Ids.NumVerts;++I)
        {
            auto Point=MakeShared<FJsonObject>();Point->SetArrayField(TEXT("point"),V(P[I]));Point->SetArrayField(TEXT("a"),V(A[I]));
            Point->SetArrayField(TEXT("b"),V(B[I]));Point->SetNumberField(TEXT("weight"),W[I]);Active.Add(MakeShared<FJsonValueObject>(Point));
        }
        Row->SetArrayField(TEXT("active"),Active);Cases.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/AnimMan.AnimMan"));
    auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;if(!Asset)return Fail(TEXT("Missing AnimMan physics asset."));
    for(auto Setup:Asset->SkeletalBodySetups)
    {
        Setup->CreatePhysicsMeshes();
        for(const auto& Element:Setup->AggGeom.ConvexElems)
        {
            const auto& Hull=Element.GetChaosConvexMesh();if(!Hull||Hull->GetMargin()!=0)return Fail(TEXT("Expected cooked zero-margin foot hull."));
            auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("bone"),Setup->BoneName.ToString());
            TArray<TSharedPtr<FJsonValue>> Vertices,Supports;
            for(int32 I=0;I<Hull->NumVertices();++I)Vertices.Add(MakeShared<FJsonValueArray>(V(FVec3(Hull->GetVertex(I)))));
            for(int32 I=0;I<128;++I)for(int32 ScaleId=0;ScaleId<4;++ScaleId)
            {
                FVec3 Direction=I==0?FVec3(0):I<=6?FVec3(I<=2?(I==1?1:-1):0,I>=3&&I<=4?(I==3?1:-1):0,I>=5?(I==5?1:-1):0):
                    FVec3(Random.FRandRange(-1,1),Random.FRandRange(-1,1),Random.FRandRange(-1,1));
                if(I>=7&&I<16)Direction=FVec3(1.+(I-11)*1.e-8,1,1);
                const FVec3 Scale=ScaleId==0?FVec3(1):ScaleId==1?FVec3(.75,2,1.25):ScaleId==2?FVec3(-1,2,.5):FVec3(0,1,1);
                int32 Vertex=INDEX_NONE;FReal Delta=17;
                const FVec3 Point=Hull->SupportCoreScaled(Direction,0,Scale,&Delta,Vertex);
                auto S=MakeShared<FJsonObject>();S->SetArrayField(TEXT("direction"),V(Direction));S->SetArrayField(TEXT("scale"),V(Scale));
                S->SetArrayField(TEXT("point"),V(Point));S->SetNumberField(TEXT("vertex"),Vertex);S->SetNumberField(TEXT("supportDelta"),Delta);
                Supports.Add(MakeShared<FJsonValueObject>(S));
            }
            Row->SetArrayField(TEXT("vertices"),Vertices);Row->SetArrayField(TEXT("supports"),Supports);Hulls.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    Root->SetArrayField(TEXT("simplexCases"),Cases);Root->SetArrayField(TEXT("hulls"),Hulls);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save GJK primitives reference."));
    return true;
}
