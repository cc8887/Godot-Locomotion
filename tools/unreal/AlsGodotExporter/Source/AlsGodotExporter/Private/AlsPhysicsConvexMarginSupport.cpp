#include "AlsPhysicsAssetExport.h"
#include "Chaos/Convex.h"
#include "Engine/SkeletalMesh.h"
#include "HAL/FileManager.h"
#include "Math/RandomStream.h"
#include "Misc/EngineVersion.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "PhysicsEngine/PhysicsAsset.h"
#include "PhysicsEngine/SkeletalBodySetup.h"
#include "Serialization/JsonSerializer.h"
namespace AlsJointSolverReference { TArray<TSharedPtr<FJsonValue>> V(const FVector& P); }

bool ExportAlsPhysicsConvexMarginSupport(const FString& Output,FString& Error)
{
    using namespace Chaos;using namespace AlsJointSolverReference;
    const auto Fail=[&](const TCHAR* Message){Error=Message;return false;};
    if(Output.IsEmpty()||FPaths::IsRelative(Output)||IFileManager::Get().FileExists(*Output))
        return Fail(TEXT("Convex margin support requires a new absolute output."));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/AnimMan.AnimMan"));
    auto* Asset=Mesh?Mesh->GetPhysicsAsset():nullptr;if(!Asset)return Fail(TEXT("Missing physics asset."));
    auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("engine"),FEngineVersion::Current().ToString());
    Root->SetStringField(TEXT("observation"),TEXT("Native cooked foot FConvex SupportCore/SupportCoreScaled and GetMarginAdjustedVertex variants; explicit pair margins and resolved scales, delta initialized to 17; not full collision parity"));
    TArray<TSharedPtr<FJsonValue>> Hulls;FRandomStream Random(92417);
    for(auto Setup:Asset->SkeletalBodySetups)
    {
        Setup->CreatePhysicsMeshes();
        for(const auto& Element:Setup->AggGeom.ConvexElems)
        {
            const auto& Hull=Element.GetChaosConvexMesh();
            if(!Hull||Hull->GetMarginf()!=0)return Fail(TEXT("Expected cooked zero-inner-margin hull."));
            auto H=MakeShared<FJsonObject>();H->SetStringField(TEXT("bone"),Setup->BoneName.ToString());
            TArray<TSharedPtr<FJsonValue>> Vertices,Rows;
            for(int32 I=0;I<Hull->NumVertices();++I)Vertices.Add(MakeShared<FJsonValueArray>(V(FVec3(Hull->GetVertex(I)))));
            for(int32 Mode=0;Mode<5;++Mode)for(double Margin:{0.,.05,static_cast<double>(.6178215742111206f),1.})
            {
                const bool Scaled=Mode!=0;
                const FVec3 Scale=Mode<=1?FVec3(1):Mode==2?FVec3(1,1,static_cast<double>(.9999998807907104f)):
                    Mode==3?FVec3(.75,2,1.25):FVec3(-1,2,.5);
                for(int32 Kind=0;Kind<2;++Kind)
                {
                    if(Kind==1&&Margin==0)continue;
                    for(int32 I=0;I<(Kind==0?128:Hull->NumVertices());++I)
                    {
                        FVec3 Direction=I==0?FVec3(0):I<=6?FVec3(I<=2?(I==1?1:-1):0,I>=3&&I<=4?(I==3?1:-1):0,I>=5?(I==5?1:-1):0):
                            FVec3(Random.FRandRange(-1,1),Random.FRandRange(-1,1),Random.FRandRange(-1,1));
                        int32 Vertex=Kind==0?INDEX_NONE:I;FReal Delta=17;FVec3 Point;
                        if(Kind==0)Point=Scaled?Hull->SupportCoreScaled(Direction,Margin,Scale,&Delta,Vertex):Hull->SupportCore(Direction,Margin,&Delta,Vertex);
                        else Point=Scaled?Hull->GetMarginAdjustedVertexScaled(Vertex,Margin,Scale,&Delta):Hull->GetMarginAdjustedVertex(Vertex,Margin,&Delta);
                        auto R=MakeShared<FJsonObject>();R->SetBoolField(TEXT("scaled"),Scaled);R->SetBoolField(TEXT("directVertex"),Kind==1);
                        R->SetNumberField(TEXT("margin"),Margin);R->SetArrayField(TEXT("scale"),V(Scale));R->SetArrayField(TEXT("direction"),V(Direction));
                        R->SetNumberField(TEXT("vertex"),Vertex);R->SetArrayField(TEXT("point"),V(Point));R->SetNumberField(TEXT("delta"),Delta);
                        Rows.Add(MakeShared<FJsonValueObject>(R));
                    }
                }
            }
            H->SetArrayField(TEXT("vertices"),Vertices);H->SetArrayField(TEXT("cases"),Rows);Hulls.Add(MakeShared<FJsonValueObject>(H));
        }
    }
    if(Hulls.Num()!=2)return Fail(TEXT("Expected both cooked feet."));
    Root->SetArrayField(TEXT("hulls"),Hulls);FString Json;
    if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Json))||
        !FFileHelper::SaveStringToFile(Json,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return Fail(TEXT("Cannot publish convex margin support."));
    return true;
}
