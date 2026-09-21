#include "AlsPhysicsAssetExport.h"
#include "Chaos/GJK.h"
#include "Chaos/Convex.h"
#include "Engine/SkeletalMesh.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }

bool ExportAlsPhysicsGjkSearchReference(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("GJK search reference requires a new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native indexed GJKPenetrationWarmStartable on real cooked feet and box; full native result plus GJK cache saved before EPA; cache restore observed on a copy; no provided contacts"));
    auto* GjkEpsilon=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.GJKEpsilon"));
    auto* EpaEpsilon=IConsoleManager::Get().FindConsoleVariable(TEXT("p.Chaos.Collision.EPAEpsilon"));
    if(!GjkEpsilon||!EpaEpsilon)return Fail(TEXT("Missing GJK settings."));
    const double GE=GjkEpsilon->GetFloat(),EE=EpaEpsilon->GetFloat();
    Root->SetNumberField(TEXT("gjkEpsilon"),GE);Root->SetNumberField(TEXT("epaEpsilon"),EE);
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/AnimMan.AnimMan"));
    auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;if(!Asset)return Fail(TEXT("Missing AnimMan physics asset."));
    TArray<TSharedPtr<FJsonValue>> Sequences;
    for(auto Setup:Asset->SkeletalBodySetups)
    {
        Setup->CreatePhysicsMeshes();
        for(const auto& Element:Setup->AggGeom.ConvexElems)
        {
            const auto& Hull=Element.GetChaosConvexMesh();if(!Hull||Hull->GetMargin()!=0)return Fail(TEXT("Expected cooked zero-margin hull."));
            for(int32 Axis=0;Axis<3;++Axis)for(int32 Sign:{-1,1})for(float Margin:{0.f,.2f})for(bool Warm:{false,true})
            {
                auto Seq=MakeShared<FJsonObject>();Seq->SetStringField(TEXT("bone"),Setup->BoneName.ToString());
                Seq->SetNumberField(TEXT("axis"),Axis);Seq->SetNumberField(TEXT("sign"),Sign);Seq->SetBoolField(TEXT("warmStart"),Warm);
                Seq->SetNumberField(TEXT("marginB"),Margin);
                const FVec3 Half(8,10,6);Seq->SetArrayField(TEXT("halfB"),V(Half));
                TBox<FReal,3> Box(-Half,Half);TGJKCoreShape<FConvex> A(*Hull,0.f);TGJKCoreShape<TBox<FReal,3>> B(Box,Margin);
                FGJKSimplexData Cache;TArray<TSharedPtr<FJsonValue>> Frames;int32 Frame=0;
                for(double Distance:{60.,25.,12.,5.,0.,-5.,0.,5.,12.,25.,60.})
                {
                    if(!Warm)Cache.Reset();
                    FVec3 Position(.137*(Frame%3),-.231*(Frame%2),.179);Position[Axis]+=Sign*Distance;
                    const auto Q=FRotation3::FromAxisAngle(FVec3(1,2,3).GetSafeNormal(),Frame*.0315);
                    const FRigidTransform3 Pose(Position,Q);
                    auto RestoreProbe=Cache;FSimplex Restored;FVec3 RestoredPoints[4],RestoredV(-1,0,0);FReal RestoredDistance=FLT_MAX;
                    RestoreProbe.Restore(Pose,Restored,RestoredPoints,RestoredV,RestoredDistance,GE);
                    FReal Penetration=0,Delta=0;FVec3 PA,PB,NA,NB;int32 VA=INDEX_NONE,VB=INDEX_NONE;
                    const bool HaveContact=GJKPenetrationWarmStartable(A,B,Pose,Penetration,PA,PB,NA,NB,VA,VB,Cache,Delta,GE,EE);
                    auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("frame"),Frame++);Row->SetObjectField(TEXT("bToA"),T(FTransform(Pose)));
                    Row->SetNumberField(TEXT("restoredCount"),Restored.NumVerts);Row->SetNumberField(TEXT("count"),Cache.NumVerts);
                    Row->SetBoolField(TEXT("haveContact"),HaveContact);Row->SetNumberField(TEXT("penetration"),Penetration);
                    Row->SetArrayField(TEXT("pointA"),V(PA));Row->SetArrayField(TEXT("pointB"),V(PB));
                    Row->SetArrayField(TEXT("normalA"),V(NA));Row->SetArrayField(TEXT("normalB"),V(NB));
                    Row->SetNumberField(TEXT("vertexA"),VA);Row->SetNumberField(TEXT("vertexB"),VB);Row->SetNumberField(TEXT("maxSupportDelta"),Delta);
                    TArray<TSharedPtr<FJsonValue>> Active;
                    for(int32 I=0;I<Cache.NumVerts;++I)
                    {
                        auto P=MakeShared<FJsonObject>();P->SetArrayField(TEXT("a"),V(Cache.As[I]));P->SetArrayField(TEXT("b"),V(Cache.Bs[I]));
                        P->SetNumberField(TEXT("weight"),Cache.Barycentric[I]);Active.Add(MakeShared<FJsonValueObject>(P));
                    }
                    Row->SetArrayField(TEXT("cache"),Active);Frames.Add(MakeShared<FJsonValueObject>(Row));
                }
                Seq->SetArrayField(TEXT("frames"),Frames);Sequences.Add(MakeShared<FJsonValueObject>(Seq));
            }
        }
    }
    Root->SetArrayField(TEXT("sequences"),Sequences);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save GJK search reference."));
    return true;
}
