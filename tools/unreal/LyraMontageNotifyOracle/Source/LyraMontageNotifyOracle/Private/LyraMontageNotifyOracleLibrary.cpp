#include "LyraMontageNotifyOracleLibrary.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimNotifyQueue.h"
#include "Animation/AnimNotifyLibrary.h"
#include "Animation/AnimSequence.h"
#include "Animation/ActiveMontageInstanceScope.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FWeightMember {using Type=float FAnimMontageInstance::*;friend Type OracleMember(FWeightMember);};
struct FInterruptedMember {using Type=bool FAnimMontageInstance::*;friend Type OracleMember(FInterruptedMember);};
struct FQueueMember {using Type=FAnimNotifyQueue FAnimInstanceProxy::*;friend Type OracleMember(FQueueMember);};
template<class Tag,typename Tag::Type Member> struct TOracleMember {friend typename Tag::Type OracleMember(Tag){return Member;}};
template struct TOracleMember<FWeightMember,&FAnimMontageInstance::NotifyWeight>;
template struct TOracleMember<FInterruptedMember,&FAnimMontageInstance::bInterrupted>;
template struct TOracleMember<FQueueMember,&FAnimInstanceProxy::NotifyQueue>;
struct FProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    using FAnimInstanceProxy::PreUpdate;
    using FAnimInstanceProxy::PostUpdate;
    FAnimNotifyQueue& Queue(){return this->*OracleMember(FQueueMember{});}
};
const FName SlotNames[]={TEXT("UpperBody"),TEXT("UpperBodyAdditive"),TEXT("FullBodyAdditivePreAim"),TEXT("AdditiveHitReact"),TEXT("FullBody")};
}

FString ULyraMontageNotifyOracleLibrary::ReadTrace(const FString& RequestsJson)
{
    TSharedPtr<FJsonObject> Requests;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return {};
    TMap<FString,TStrongObjectPtr<UAnimMontage>> Assets;
    TMap<FString,TStrongObjectPtr<UAnimSequence>> Sources;
    const UWorld::InitializationValues Init=UWorld::InitializationValues().AllowAudioPlayback(false)
        .RequiresHitProxies(false).CreatePhysicsScene(false).CreateNavigation(false).CreateAISystem(false).ShouldSimulatePhysics(false);
    TStrongObjectPtr<UWorld> World(UWorld::CreateWorld(EWorldType::PIE,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
    if(!World.IsValid())return {};
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(World.Get()));
    TArray<TSharedPtr<FJsonValue>> Traces,Metadata;
    for(const auto& PathValue:Requests->GetArrayField(TEXT("assets")))
    {
        const FString Path=PathValue->AsString();auto Montage=LoadObject<UAnimMontage>(nullptr,*Path);if(!Montage)return {};
        Assets.Add(Path,TStrongObjectPtr<UAnimMontage>(Montage));
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("asset"),Path);TArray<TSharedPtr<FJsonValue>> Tracks;
        for(const auto& Track:Montage->SlotAnimTracks)
        {
            if(Track.AnimTrack.AnimSegments.Num()!=1)return {};
            const auto& Segment=Track.AnimTrack.AnimSegments[0];auto R=MakeShared<FJsonObject>();
            R->SetStringField(TEXT("slot"),Track.SlotName.ToString());R->SetStringField(TEXT("asset"),Segment.GetAnimReference()->GetPathName());
            R->SetNumberField(TEXT("rate"),Segment.GetValidPlayRate());R->SetNumberField(TEXT("length"),Segment.GetLength());
            R->SetNumberField(TEXT("start"),Segment.AnimStartTime);R->SetNumberField(TEXT("end"),Segment.AnimEndTime);
            Tracks.Add(MakeShared<FJsonValueObject>(R));
        }
        Row->SetArrayField(TEXT("tracks"),Tracks);Metadata.Add(MakeShared<FJsonValueObject>(Row));
    }
    for(const auto& TraceValue:Requests->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<UAnimInstance> Main(NewObject<UAnimInstance>(Component.Get())),SourceOwner(NewObject<UAnimInstance>(Component.Get()));
        FProxy Proxy(Main.Get());Proxy.Initialize(Main.Get());for(FName Slot:SlotNames)Proxy.RegisterSlotNodeWithAnimInstance(Slot);
        TMap<int64,TUniquePtr<FAnimMontageInstance>> Instances;TMap<int32,int64> Identities;
        const auto Reference=[&](const FAnimNotifyEventReference& Ref)
        {
            auto Asset=CastChecked<UAnimSequenceBase>(Ref.GetSourceObject());auto Row=MakeShared<FJsonObject>();
            Row->SetStringField(TEXT("asset"),Asset->GetPathName());Row->SetNumberField(TEXT("index"),Ref.GetNotify()-Asset->Notifies.GetData());
            const auto* Context=Ref.GetContextData<UE::Anim::FAnimNotifyMontageInstanceContext>();
            Row->SetNumberField(TEXT("instance"),Context?Identities.FindChecked(Context->MontageInstanceID):-1);
            Row->SetNumberField(TEXT("current"),Ref.GetCurrentAnimationTime());Row->SetBoolField(TEXT("active"),Ref.IsActiveContext());
            Row->SetBoolField(TEXT("end"),UAnimNotifyLibrary::NotifyStateReachedEnd(Ref));return MakeShared<FJsonValueObject>(Row);
        };
        const auto References=[&](const TArray<FAnimNotifyEventReference>& Refs)
        {TArray<TSharedPtr<FJsonValue>> Rows;for(const auto& Ref:Refs)Rows.Add(Reference(Ref));return Rows;};
        TArray<TSharedPtr<FJsonValue>> Frames;const auto Trace=TraceValue->AsObject();
        for(const auto& FrameValue:Trace->GetArrayField(TEXT("frames")))
        {
            const auto Input=FrameValue->AsObject();Main->ClearQueuedAnimEvents(true);SourceOwner->ClearQueuedAnimEvents(true);
            World->SetPlayInEditorInitialNetMode(Input->GetBoolField(TEXT("server"))?NM_DedicatedServer:NM_Standalone);
            Proxy.PreUpdate(Main.Get(),Input->GetNumberField(TEXT("delta")));
            auto& Queue=Main->NotifyQueue;auto& SourceQueue=SourceOwner->NotifyQueue;
            Queue.PredictedLODLevel=SourceQueue.PredictedLODLevel=Input->GetIntegerField(TEXT("lod"));
            auto Out=MakeShared<FJsonObject>();Out->SetNumberField(TEXT("montageSeedBefore"),uint32(Queue.RandomStream.GetCurrentSeed()));
            Out->SetNumberField(TEXT("sourceSeedBefore"),uint32(SourceQueue.RandomStream.GetCurrentSeed()));
            for(const auto& MontageValue:Input->GetArrayField(TEXT("montages")))
            {
                const auto Tick=MontageValue->AsObject();const int64 Id=int64(Tick->GetNumberField(TEXT("instance")));
                auto Montage=Assets.FindChecked(Tick->GetStringField(TEXT("asset"))).Get();
                if(!Instances.Contains(Id))
                {
                    auto Instance=MakeUnique<FAnimMontageInstance>(Main.Get());Instance->Initialize(Montage);
                    Identities.Add(Instance->GetInstanceID(),Id);Instances.Add(Id,MoveTemp(Instance));
                }
                auto& Instance=*Instances[Id];if(Instance.Montage!=Montage)return {};
                Instance.*OracleMember(FWeightMember{})=Tick->GetNumberField(TEXT("weight"));
                Instance.*OracleMember(FInterruptedMember{})=Tick->GetBoolField(TEXT("interrupted"));
                Instance.HandleEvents(Tick->GetNumberField(TEXT("previous")),Tick->GetNumberField(TEXT("current")),nullptr);
            }
            Out->SetArrayField(TEXT("direct"),References(Queue.AnimNotifies));TArray<TSharedPtr<FJsonValue>> SlotRows;
            for(const auto& Pair:Queue.UnfilteredMontageAnimNotifies)
            {auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("slot"),Pair.Key.ToString());Row->SetArrayField(TEXT("events"),References(Pair.Value.Notifies));SlotRows.Add(MakeShared<FJsonValueObject>(Row));}
            Out->SetArrayField(TEXT("slots"),SlotRows);
            for(const auto& SourceValue:Input->GetArrayField(TEXT("sources")))
            {
                const auto Tick=SourceValue->AsObject();const FString Path=Tick->GetStringField(TEXT("asset"));
                if(!Sources.Contains(Path))Sources.Add(Path,TStrongObjectPtr<UAnimSequence>(LoadObject<UAnimSequence>(nullptr,*Path)));
                auto Sequence=Sources[Path].Get();if(!Sequence)return {};float Current=Tick->GetNumberField(TEXT("current"));
                FAnimTickRecord Record;Record.TimeAccumulator=&Current;Record.bLooping=Tick->GetBoolField(TEXT("looping"));Record.bActiveContext=Tick->GetBoolField(TEXT("active"));
                FAnimNotifyContext Context(Record);Sequence->GetAnimNotifies(Tick->GetNumberField(TEXT("previous")),Tick->GetNumberField(TEXT("delta")),Context);
                SourceQueue.AddAnimNotifies(Tick->GetBoolField(TEXT("leader")),Context.ActiveNotifies,Tick->GetNumberField(TEXT("weight")));
            }
            Out->SetArrayField(TEXT("source"),References(SourceQueue.AnimNotifies));
            const int32 Relevant=Input->GetIntegerField(TEXT("currentRelevant"));
            for(int32 I=0;I<UE_ARRAY_COUNT(SlotNames);I++)Proxy.UpdateSlotNodeWeight(SlotNames[I],(Relevant&(1<<I))?1.f:0.f,1.f);
            Proxy.FlipBufferWriteIndex();int32 NativeRelevant=0;
            for(int32 I=0;I<UE_ARRAY_COUNT(SlotNames);I++)if(Proxy.IsSlotNodeRelevantForNotifies(SlotNames[I]))NativeRelevant|=1<<I;
            Proxy.Queue().AnimNotifies=SourceQueue.AnimNotifies;Proxy.PostUpdate(Main.Get());
            Out->SetNumberField(TEXT("relevant"),NativeRelevant);Out->SetArrayField(TEXT("queue"),References(Queue.AnimNotifies));
            Out->SetNumberField(TEXT("montageSeedAfter"),uint32(Queue.RandomStream.GetCurrentSeed()));
            Out->SetNumberField(TEXT("sourceSeedAfter"),uint32(SourceQueue.RandomStream.GetCurrentSeed()));Frames.Add(MakeShared<FJsonValueObject>(Out));
        }
        auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));Row->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));
        Row->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("assets"),Metadata);Result->SetArrayField(TEXT("traces"),Traces);
    FString Text;auto Writer=TJsonWriterFactory<>::Create(&Text);const bool Success=FJsonSerializer::Serialize(Result,Writer);
    World->DestroyWorld(false);return Success?Text:FString();
}
