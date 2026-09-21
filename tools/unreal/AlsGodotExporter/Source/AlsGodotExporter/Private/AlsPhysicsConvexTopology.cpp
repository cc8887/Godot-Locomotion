#include "AlsPhysicsAssetExport.h"
#include "Chaos/Convex.h"
#include "Engine/SkeletalMesh.h"
#include "HAL/FileManager.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "Serialization/JsonSerializer.h"

namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); TSharedRef<FJsonObject> T(const FTransform& P); }
namespace AlsPhysicsExport { TSharedRef<FJsonObject> T(const FTransform& P); }

bool ExportAlsPhysicsConvexTopology(const FString& Output,FString& Error)
{
    using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Convex topology requires a new absolute output."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("coordinates"),TEXT("UE unscaled convex-local centimeters; native cooked float vertices and planes; element transform separate"));
    Root->SetStringField(TEXT("observation"),TEXT("FKConvexElem GetChaosConvexMesh after CreatePhysicsMeshes; native face loops in original order; no inferred triangle merging; no simulation or saved assets"));
    TArray<TSharedPtr<FJsonValue>> Rigs;
    for(const TCHAR* Name:{TEXT("Mannequin"),TEXT("AnimMan")})
    {
        const FString Path=FString(TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/"))+Name+TEXT(".")+Name;
        auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Path);auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;
        if(!Asset)return Fail(TEXT("Missing physics asset."));
        auto Rig=MakeShared<FJsonObject>();Rig->SetStringField(TEXT("mesh"),Path);Rig->SetStringField(TEXT("physicsAsset"),Asset->GetPathName());
        TArray<TSharedPtr<FJsonValue>> Shapes;
        for(int32 BodyIndex=0;BodyIndex<Asset->SkeletalBodySetups.Num();++BodyIndex)
        {
            auto* Setup=Asset->SkeletalBodySetups[BodyIndex].Get();if(!Setup)return Fail(TEXT("Missing body setup."));
            Setup->CreatePhysicsMeshes();
            const auto& Geometry=Setup->AggGeom;
            for(int32 I=0;I<Geometry.ConvexElems.Num();++I)
            {
                const auto& Element=Geometry.ConvexElems[I];const auto& Hull=Element.GetChaosConvexMesh();
                if(!Hull||Hull->NumVertices()<4||Hull->NumPlanes()<4)return Fail(TEXT("Missing native cooked convex."));
                auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("bodyIndex"),BodyIndex);
                Row->SetStringField(TEXT("bone"),Setup->BoneName.ToString());Row->SetNumberField(TEXT("convexIndex"),I);
                Row->SetNumberField(TEXT("shapeIndex"),Geometry.SphereElems.Num()+Geometry.BoxElems.Num()+Geometry.SphylElems.Num()+I);
                Row->SetObjectField(TEXT("local"),AlsPhysicsExport::T(Element.GetTransform()));Row->SetNumberField(TEXT("margin"),Hull->GetMargin());
                Row->SetNumberField(TEXT("windingOrder"),Hull->GetWindingOrder());
                TArray<TSharedPtr<FJsonValue>> SourceVertices,SourceIndices,Vertices,Faces;
                for(const auto& P:Element.VertexData)SourceVertices.Add(MakeShared<FJsonValueArray>(V(P)));
                for(int32 Index:Element.IndexData)SourceIndices.Add(MakeShared<FJsonValueNumber>(Index));
                for(int32 Vertex=0;Vertex<Hull->NumVertices();++Vertex)Vertices.Add(MakeShared<FJsonValueArray>(V(FVector(Hull->GetVertex(Vertex)))));
                for(int32 Face=0;Face<Hull->NumPlanes();++Face)
                {
                    const int32 Count=Hull->NumPlaneVertices(Face);if(Count<3)return Fail(TEXT("Invalid native convex face."));
                    Chaos::FVec3 N,X;Hull->GetPlaneNX(Face,N,X);
                    auto Plane=MakeShared<FJsonObject>();Plane->SetArrayField(TEXT("normal"),V(N));Plane->SetArrayField(TEXT("point"),V(X));
                    TArray<TSharedPtr<FJsonValue>> Loop;
                    for(int32 Vertex=0;Vertex<Count;++Vertex)
                    {
                        const int32 Index=Hull->GetPlaneVertex(Face,Vertex);
                        if(Index<0||Index>=Hull->NumVertices())return Fail(TEXT("Invalid native face vertex."));
                        Loop.Add(MakeShared<FJsonValueNumber>(Index));
                    }
                    Plane->SetArrayField(TEXT("vertices"),Loop);Faces.Add(MakeShared<FJsonValueObject>(Plane));
                }
                Row->SetArrayField(TEXT("sourceVertices"),SourceVertices);Row->SetArrayField(TEXT("sourceIndices"),SourceIndices);
                Row->SetArrayField(TEXT("vertices"),Vertices);Row->SetArrayField(TEXT("faces"),Faces);Shapes.Add(MakeShared<FJsonValueObject>(Row));
            }
        }
        Rig->SetArrayField(TEXT("shapes"),Shapes);Rigs.Add(MakeShared<FJsonValueObject>(Rig));
    }
    Root->SetArrayField(TEXT("rigs"),Rigs);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot save native convex topology."));
    return true;
}
