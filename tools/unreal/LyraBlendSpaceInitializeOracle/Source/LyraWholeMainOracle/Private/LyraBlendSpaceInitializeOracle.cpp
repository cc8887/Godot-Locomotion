#include "LyraWholeMainOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/BlendSpace.h"
#include "AnimNodes/AnimNode_BlendSpacePlayer.h"
#include "AnimNodes/AnimNode_RotationOffsetBlendSpace.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraBlendSpaceInitialize
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_BLENDSPACE_INITIALIZE_FAILED line=%d"),Line);return {};}
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
// Derived member pointers refer to protected fields on the real UE node.
struct FSourceAccess:FAnimNode_BlendSpacePlayer
{
    static float& Internal(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::InternalTimeAccumulator);}
    static float& Weight(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::BlendWeight);}
    static bool& Full(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::bHasBeenFullWeight);}
    static FMarkerTickRecord& Marker(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::MarkerTickRecord);}
    static FDeltaTimeRecord& Delta(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::DeltaTimeRecord);}
    static FBlendFilter& Filter(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::BlendFilter);}
    static TArray<FBlendSampleData>& Samples(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::BlendSampleDataCache);}
    static int32& Triangle(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::CachedTriangulationIndex);}
    static TObjectPtr<UBlendSpace>& Previous(FAnimNode_BlendSpacePlayerBase& N){return N.*(&FSourceAccess::PreviousBlendSpace);}
};
TSharedPtr<FJsonObject> Snapshot(FAnimNode_BlendSpacePlayerBase& N,bool Aim)
{
    auto O=MakeShared<FJsonObject>();const auto& M=FSourceAccess::Marker(N);const auto& D=FSourceAccess::Delta(N);
    O->SetNumberField(TEXT("internal"),FSourceAccess::Internal(N));O->SetNumberField(TEXT("public"),N.GetCurrentAssetTime());
    O->SetNumberField(TEXT("weight"),FSourceAccess::Weight(N));O->SetBoolField(TEXT("fullWeight"),FSourceAccess::Full(N));
    O->SetNumberField(TEXT("previousIndex"),M.PreviousMarker.MarkerIndex);O->SetNumberField(TEXT("nextIndex"),M.NextMarker.MarkerIndex);
    O->SetNumberField(TEXT("previousDistance"),M.PreviousMarker.TimeToMarker);O->SetNumberField(TEXT("nextDistance"),M.NextMarker.TimeToMarker);
    O->SetNumberField(TEXT("deltaPrevious"),D.GetPrevious());O->SetNumberField(TEXT("delta"),D.Delta);O->SetBoolField(TEXT("deltaValid"),D.IsPreviousValid());
    O->SetNumberField(TEXT("triangle"),FSourceAccess::Triangle(N));O->SetNumberField(TEXT("sampleCount"),FSourceAccess::Samples(N).Num());
    auto P=N.GetPosition();O->SetNumberField(TEXT("x"),P.X);O->SetNumberField(TEXT("y"),P.Y);
    O->SetStringField(TEXT("asset"),N.GetBlendSpace()?N.GetBlendSpace()->GetPathName():TEXT(""));
    O->SetStringField(TEXT("previousAsset"),FSourceAccess::Previous(N)?FSourceAccess::Previous(N)->GetPathName():TEXT(""));
    TArray<TSharedPtr<FJsonValue>> Filters;
    for(const auto& F:FSourceAccess::Filter(N).FilterPerAxis)
    {auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("output"),F.LastOutput);Row->SetBoolField(TEXT("valid"),F.IsValid());Filters.Add(MakeShared<FJsonValueObject>(Row));}
    O->SetArrayField(TEXT("filters"),Filters);
    if(Aim){auto& A=static_cast<FAnimNode_RotationOffsetBlendSpace&>(N);O->SetNumberField(TEXT("alpha"),A.ActualAlpha);O->SetBoolField(TEXT("lodEnabled"),A.bIsLODEnabled);}
    return O;
}
}
FString ULyraWholeMainOracleLibrary::ReadBlendSpaceInitialization(const FString& RequestsJson)
{
    using namespace LyraBlendSpaceInitialize;TSharedPtr<FJsonObject> Q;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Q))return Fail(__LINE__);
    auto* Class=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("main")));
    auto* Provider=LoadObject<UClass>(nullptr,*Q->GetStringField(TEXT("provider")));
    auto* Mesh=LoadObject<USkeletalMesh>(nullptr,*Q->GetStringField(TEXT("mesh")));if(!Class||!Provider||!Mesh)return Fail(__LINE__);
    const FMemMark Mark(FMemStack::Get());
    const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));if(!World.IsValid())return Fail(__LINE__);
    struct FCleanup{UWorld* W;~FCleanup(){W->DestroyWorld(false);}} Cleanup{World.Get()};
    auto* Actor=World->SpawnActor<AActor>();if(!Actor)return Fail(__LINE__);
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(Actor,NAME_None,RF_Transient));
    Component->bUseRefPoseOnInitAnim=true;Component->SetDisablePostProcessBlueprint(true);Component->SetCollisionEnabled(ECollisionEnabled::NoCollision);
    Component->SetSkeletalMeshAsset(Mesh);Component->SetAnimInstanceClass(Class);Actor->SetRootComponent(Component.Get());Actor->AddInstanceComponent(Component.Get());Component->RegisterComponent();
    auto* Main=Component->GetAnimInstance();if(!Main)return Fail(__LINE__);Main->LinkAnimClassLayers(Provider);
    const auto& Linked=static_cast<const USkeletalMeshComponent*>(Component.Get())->GetLinkedAnimInstances();if(Linked.Num()!=1)return Fail(__LINE__);
    TArray<TSharedPtr<FJsonValue>> Rows;
    for(bool Aim:{false,true})
    {
        auto* Owner=Aim?Linked[0]:Main;auto& Proxy=FInstanceAccess::Proxy(Owner);
        auto* Interface=IAnimClassInterface::GetFromClass(Owner->GetClass());if(!Interface)return Fail(__LINE__);
        const auto& Properties=Interface->GetAnimNodeProperties();
        for(int32 Id:Aim?TArray<int32>{79,74}:TArray<int32>{22,16,12})
        {
            const int32 Property=Properties.Num()-1-Id;
            if(!Properties.IsValidIndex(Property)||Properties[Property]->Struct!=(Aim?FAnimNode_RotationOffsetBlendSpace::StaticStruct():FAnimNode_BlendSpacePlayer::StaticStruct()))return Fail(__LINE__);
            auto* Source=Proxy.GetMutableNodeFromIndex<FAnimNode_BlendSpacePlayerBase>(Id);
            for(int32 Round=0;Round<2;Round++)
            {
                if(!Aim){auto* Angle=FindFProperty<FNumericProperty>(Owner->GetClass(),TEXT("AdditiveLeanAngle"));if(!Angle)return Fail(__LINE__);Angle->SetFloatingPointPropertyValue(Angle->ContainerPtrToValuePtr<void>(Owner),Round?-17.25:23.5);}
                // Establish the original expose-input asset first, then seed
                // history; the measured call below is still original Init.
                FAnimationInitializeContext Context(&Proxy);Context.SetNodeId(Id);Source->Initialize_AnyThread(Context);
                FSourceAccess::Internal(*Source)=Round?.875f:.375f;FSourceAccess::Weight(*Source)=.75f;FSourceAccess::Full(*Source)=true;
                FSourceAccess::Triangle(*Source)=Round?7:3;FSourceAccess::Previous(*Source)=nullptr;
                auto& M=FSourceAccess::Marker(*Source);M.PreviousMarker=FMarkerPair(0,-.125f);M.NextMarker=FMarkerPair(1,.25f);FSourceAccess::Delta(*Source).Set(.2f,.03125f);
                FBlendSampleData Sample(0);Sample.Time=.4f;Sample.TotalWeight=.75f;FSourceAccess::Samples(*Source)={Sample};
                for(auto& Filter:FSourceAccess::Filter(*Source).FilterPerAxis){Filter.SetToValue(13.5f);Filter.UpdateAndGetFilteredData(-7.25f,.03125f);}
                if(Aim){auto& A=static_cast<FAnimNode_RotationOffsetBlendSpace&>(*Source);A.ActualAlpha=.42f;A.bIsLODEnabled=true;}
                auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("node"),Id);Row->SetNumberField(TEXT("round"),Round);Row->SetBoolField(TEXT("aim"),Aim);Row->SetObjectField(TEXT("before"),Snapshot(*Source,Aim));
                Source->Initialize_AnyThread(Context);Row->SetObjectField(TEXT("after"),Snapshot(*Source,Aim));
                FAnimationCacheBonesContext Bones(&Proxy);Bones.SetNodeId(Id);Source->CacheBones_AnyThread(Bones);Row->SetObjectField(TEXT("cached"),Snapshot(*Source,Aim));Rows.Add(MakeShared<FJsonValueObject>(Row));
            }
        }
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("sources"),Rows);Result->SetNumberField(TEXT("linkedInstances"),Linked.Num());Result->SetBoolField(TEXT("sourceUpdate"),false);
    FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}
