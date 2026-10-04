#include "LyraWholeMainOracleLibrary.h"
#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_SequencePlayer.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Actor.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraSequenceInitialize
{
FString Fail(int32 Line){UE_LOG(LogTemp,Error,TEXT("LYRA_SEQUENCE_INITIALIZE_FAILED line=%d"),Line);return {};}
struct FInstanceAccess:UAnimInstance
{static FAnimInstanceProxy& Proxy(UAnimInstance* A){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A);}};
// Legal derived member pointers access protected data on the original node.
// The probe executes the original Initialize/CacheBones methods, never copies
// or substitutes their implementation. No source Update or asset saves occur.
struct FSourceAccess:FAnimNode_AssetPlayerBase
{
    static float& Internal(FAnimNode_AssetPlayerBase& N){return N.*static_cast<float FAnimNode_AssetPlayerBase::*>(&FSourceAccess::InternalTimeAccumulator);}
    static float& Weight(FAnimNode_AssetPlayerBase& N){return N.*static_cast<float FAnimNode_AssetPlayerBase::*>(&FSourceAccess::BlendWeight);}
    static bool& Full(FAnimNode_AssetPlayerBase& N){return N.*static_cast<bool FAnimNode_AssetPlayerBase::*>(&FSourceAccess::bHasBeenFullWeight);}
    static FMarkerTickRecord& Marker(FAnimNode_AssetPlayerBase& N){return N.*static_cast<FMarkerTickRecord FAnimNode_AssetPlayerBase::*>(&FSourceAccess::MarkerTickRecord);}
    static FDeltaTimeRecord& Delta(FAnimNode_AssetPlayerBase& N){return N.*static_cast<FDeltaTimeRecord FAnimNode_AssetPlayerBase::*>(&FSourceAccess::DeltaTimeRecord);}
    static bool Valid(FAnimNode_AssetPlayerBase& N){return N.*static_cast<bool FAnimNode_AssetPlayerBase::*>(&FSourceAccess::bHasValidAssetPlayerInstanceID);}
};
TSharedPtr<FJsonObject> Snapshot(FAnimNode_AssetPlayerBase& N)
{
    auto O=MakeShared<FJsonObject>();auto& M=FSourceAccess::Marker(N);auto& D=FSourceAccess::Delta(N);
    O->SetNumberField(TEXT("internal"),FSourceAccess::Internal(N));O->SetNumberField(TEXT("public"),N.GetAccumulatedTime());
    O->SetNumberField(TEXT("weight"),N.GetCachedBlendWeight());O->SetBoolField(TEXT("fullWeight"),FSourceAccess::Full(N));
    O->SetNumberField(TEXT("previousIndex"),M.PreviousMarker.MarkerIndex);O->SetNumberField(TEXT("nextIndex"),M.NextMarker.MarkerIndex);
    O->SetNumberField(TEXT("previousDistance"),M.PreviousMarker.TimeToMarker);O->SetNumberField(TEXT("nextDistance"),M.NextMarker.TimeToMarker);
    O->SetNumberField(TEXT("deltaPrevious"),D.GetPrevious());O->SetNumberField(TEXT("delta"),D.Delta);O->SetBoolField(TEXT("deltaValid"),D.IsPreviousValid());
    O->SetBoolField(TEXT("validInstanceId"),FSourceAccess::Valid(N));
    O->SetStringField(TEXT("asset"),N.GetAnimAsset()?N.GetAnimAsset()->GetPathName():TEXT(""));return O;
}
}
FString ULyraWholeMainOracleLibrary::ReadSequenceInitialization(const FString& RequestsJson)
{
    using namespace LyraSequenceInitialize;TSharedPtr<FJsonObject> Q;
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
    auto* Owner=Linked[0];auto& Proxy=FInstanceAccess::Proxy(Owner);auto* Interface=IAnimClassInterface::GetFromClass(Owner->GetClass());if(!Interface)return Fail(__LINE__);
    const auto& Properties=Interface->GetAnimNodeProperties();TArray<TSharedPtr<FJsonValue>> Rows;
    for(const auto& Value:Q->GetArrayField(TEXT("providerNodes")))
    {
        const auto Node=Value->AsObject();const auto Type=Node->GetStringField(TEXT("type"));
        const bool Evaluator=Type==FAnimNode_SequenceEvaluator::StaticStruct()->GetPathName();
        if(!Evaluator&&Type!=FAnimNode_SequencePlayer::StaticStruct()->GetPathName())continue;
        const int32 Id=Node->GetIntegerField(TEXT("index"));const int32 Property=Properties.Num()-1-Id;
        if(!Properties.IsValidIndex(Property)||Properties[Property]->Struct->GetPathName()!=Type)return Fail(__LINE__);
        auto* Source=Proxy.GetMutableNodeFromIndex<FAnimNode_AssetPlayerBase>(Id);
        for(int32 Round=0;Round<2;Round++)
        {
            FSourceAccess::Internal(*Source)=Round?.875f:.375f;FSourceAccess::Weight(*Source)=.75f;FSourceAccess::Full(*Source)=true;
            auto& M=FSourceAccess::Marker(*Source);M.PreviousMarker=FMarkerPair(0,-.125f);M.NextMarker=FMarkerPair(1,.25f);
            FSourceAccess::Delta(*Source).Set(.2f,.03125f);
            bool ExplicitWritable=false;if(Evaluator)ExplicitWritable=static_cast<FAnimNode_SequenceEvaluator*>(Source)->SetExplicitTime(Round?.9375f:.625f);
            auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("node"),Id);Row->SetNumberField(TEXT("round"),Round);Row->SetBoolField(TEXT("evaluator"),Evaluator);
            Row->SetBoolField(TEXT("explicitWritable"),ExplicitWritable);Row->SetObjectField(TEXT("before"),Snapshot(*Source));
            FAnimationInitializeContext Context(&Proxy);Context.SetNodeId(Id);Source->Initialize_AnyThread(Context);
            Row->SetObjectField(TEXT("after"),Snapshot(*Source));
            FAnimationCacheBonesContext Bones(&Proxy);Bones.SetNodeId(Id);Source->CacheBones_AnyThread(Bones);Row->SetObjectField(TEXT("cached"),Snapshot(*Source));
            Rows.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("sources"),Rows);Result->SetBoolField(TEXT("sourceUpdate"),false);
    Result->SetNumberField(TEXT("linkedInstances"),Linked.Num());FString Json;FJsonSerializer::Serialize(Result,TJsonWriterFactory<>::Create(&Json));return Json;
}
