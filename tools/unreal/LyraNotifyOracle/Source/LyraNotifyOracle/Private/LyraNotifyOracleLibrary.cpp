#include "LyraNotifyOracleLibrary.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNotifyQueue.h"
#include "Animation/AnimNotifyLibrary.h"
#include "Animation/AnimSync.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/AnimSequence.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
FString Write(const TSharedPtr<FJsonObject>& Row)
{FString Text;auto Writer=TJsonWriterFactory<>::Create(&Text);return FJsonSerializer::Serialize(Row.ToSharedRef(),Writer)?Text:FString();}
struct FSyncMember
{using Type=UE::Anim::FAnimSync FAnimInstanceProxy::*;friend Type NativeSync(FSyncMember);};
struct FQueueMember
{using Type=FAnimNotifyQueue FAnimInstanceProxy::*;friend Type NativeSync(FQueueMember);};
template<class Tag,typename Tag::Type Member> struct TOracleMember
{friend typename Tag::Type NativeSync(Tag){return Member;}};
template struct TOracleMember<FSyncMember,&FAnimInstanceProxy::Sync>;
template struct TOracleMember<FQueueMember,&FAnimInstanceProxy::NotifyQueue>;
struct FProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    FAnimNotifyQueue& Queue(){return this->*NativeSync(FQueueMember{});}
    void Tick(float Delta){(this->*NativeSync(FSyncMember{})).TickAssetPlayerInstances(*this,Delta);}
};
struct FHandleContext final : UE::Anim::IAnimNotifyEventContextDataInterface
{
    DECLARE_NOTIFY_CONTEXT_INTERFACE(FHandleContext)
public:
    int32 Handle;
    explicit FHandleContext(int32 Value):Handle(Value){}
};
IMPLEMENT_NOTIFY_CONTEXT_INTERFACE(FHandleContext)
TSharedPtr<FJsonValue> Reference(const FAnimNotifyEventReference& Ref)
{
    const auto* Sequence=CastChecked<UAnimSequenceBase>(Ref.GetSourceObject());
    auto Row=MakeShared<FJsonObject>();
    Row->SetStringField(TEXT("asset"),Sequence->GetPathName());
    Row->SetNumberField(TEXT("index"),Ref.GetNotify()-Sequence->Notifies.GetData());
    Row->SetNumberField(TEXT("handle"),Ref.GetContextData<FHandleContext>()->Handle);
    Row->SetNumberField(TEXT("current"),Ref.GetCurrentAnimationTime());
    Row->SetBoolField(TEXT("active"),Ref.IsActiveContext());
    Row->SetBoolField(TEXT("end"),UAnimNotifyLibrary::NotifyStateReachedEnd(Ref));
    return MakeShared<FJsonValueObject>(Row);
}
}

FString ULyraNotifyOracleLibrary::ReadSyncQueueOwnership(UAnimSequence* Sequence)
{
    if(!Sequence||Sequence->Notifies.IsEmpty())return {};
    auto Component=NewObject<USkeletalMeshComponent>();
    TStrongObjectPtr<UAnimInstance> Main(NewObject<UAnimInstance>(Component)),Linked(NewObject<UAnimInstance>(Component));
    FProxy MainProxy(Main.Get()),LinkedProxy(Linked.Get());
    MainProxy.Initialize(Main.Get());LinkedProxy.Initialize(Linked.Get());
    float MainTime=0,LinkedTime=0;FMarkerTickRecord MainMarker,LinkedMarker;
    FDeltaTimeRecord MainDelta,LinkedDelta;
    FAnimTickRecord MainTick(Sequence,false,1,false,1,MainTime,MainMarker),LinkedTick(Sequence,false,1,false,1,LinkedTime,LinkedMarker);
    MainTick.DeltaTimeRecord=&MainDelta;LinkedTick.DeltaTimeRecord=&LinkedDelta;
    FAnimationUpdateSharedContext SharedContext;
    FAnimationUpdateContext MainContext(&MainProxy,Sequence->GetPlayLength(),&SharedContext);
    {
        UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> RootScope(MainContext,MainContext);
        auto ChildContext=MainContext;ChildContext.AnimInstanceProxy=&LinkedProxy;
        auto& MainScope=MainContext.GetMessageChecked<UE::Anim::FAnimSyncGroupScope>();
        auto& LinkedScope=ChildContext.GetMessageChecked<UE::Anim::FAnimSyncGroupScope>();
        if(&MainScope!=&LinkedScope)return {};
        MainScope.AddTickRecord(MainTick,{},UE::Anim::FAnimSyncDebugInfo(MainContext));
        LinkedScope.AddTickRecord(LinkedTick,{},UE::Anim::FAnimSyncDebugInfo(ChildContext));
    }
    const uint32 LinkedSeed=LinkedProxy.Queue().RandomStream.GetCurrentSeed();
    MainProxy.Tick(Sequence->GetPlayLength());
    auto Row=MakeShared<FJsonObject>();Row->SetStringField(TEXT("asset"),Sequence->GetPathName());
    Row->SetNumberField(TEXT("mainCount"),MainProxy.Queue().AnimNotifies.Num());
    Row->SetNumberField(TEXT("linkedCount"),LinkedProxy.Queue().AnimNotifies.Num());
    Row->SetNumberField(TEXT("mainSeed"),MainProxy.Queue().RandomStream.GetCurrentSeed());
    Row->SetNumberField(TEXT("linkedSeed"),LinkedProxy.Queue().RandomStream.GetCurrentSeed());
    Row->SetNumberField(TEXT("linkedSeedBefore"),LinkedSeed);
    Row->SetNumberField(TEXT("mainTime"),MainTime);Row->SetNumberField(TEXT("linkedTime"),LinkedTime);
    return Write(Row);
}

FString ULyraNotifyOracleLibrary::ReadQueueTrace(const FString& RequestsJson)
{
    TSharedPtr<FJsonObject> Requests;
    if(!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Requests))return {};
    TMap<FString,TStrongObjectPtr<UAnimSequenceBase>> Assets;
    auto World=NewObject<UWorld>();World->WorldType=EWorldType::PIE;
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(World));
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TraceValue:Requests->GetArrayField(TEXT("traces")))
    {
        TStrongObjectPtr<UAnimInstance> Instance(NewObject<UAnimInstance>(Component.Get()));
        FAnimNotifyQueue& Queue=Instance->NotifyQueue;int32 NextHandle=0;
        const auto Trace=TraceValue->AsObject();TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FrameValue:Trace->GetArrayField(TEXT("frames")))
        {
            const auto Frame=FrameValue->AsObject();Instance->ClearQueuedAnimEvents(true);
            World->SetPlayInEditorInitialNetMode(Frame->GetBoolField(TEXT("server"))?NM_DedicatedServer:NM_Standalone);
            Queue.PredictedLODLevel=Frame->GetIntegerField(TEXT("lod"));
            auto Output=MakeShared<FJsonObject>();Output->SetNumberField(TEXT("seedBefore"),uint32(Queue.RandomStream.GetCurrentSeed()));
            TArray<TSharedPtr<FJsonValue>> Windows;
            for(const auto& TickValue:Frame->GetArrayField(TEXT("ticks")))
            {
                const auto Input=TickValue->AsObject();const FString Path=Input->GetStringField(TEXT("asset"));
                if(!Assets.Contains(Path))Assets.Add(Path,TStrongObjectPtr<UAnimSequenceBase>(LoadObject<UAnimSequenceBase>(nullptr,*Path)));
                UAnimSequenceBase* Sequence=Assets[Path].Get();if(!Sequence)return {};
                float Time=Input->GetNumberField(TEXT("current"));
                FAnimTickRecord Tick;Tick.TimeAccumulator=&Time;Tick.bLooping=Input->GetBoolField(TEXT("looping"));Tick.bActiveContext=Input->GetBoolField(TEXT("active"));
                FAnimNotifyContext Context(Tick);Sequence->GetAnimNotifies(Input->GetNumberField(TEXT("previous")),Input->GetNumberField(TEXT("delta")),Context);
                TArray<TSharedPtr<FJsonValue>> Extracted;
                for(auto& Ref:Context.ActiveNotifies)
                {
                    Ref.AddContextData<FHandleContext>(NextHandle++);Extracted.Add(Reference(Ref));
                }
                auto Window=MakeShared<FJsonObject>();Window->SetArrayField(TEXT("events"),Extracted);Windows.Add(MakeShared<FJsonValueObject>(Window));
                Queue.AddAnimNotifies(Input->GetBoolField(TEXT("leader")),Context.ActiveNotifies,Input->GetNumberField(TEXT("weight")));
            }
            TArray<TSharedPtr<FJsonValue>> Queued;for(const auto& Ref:Queue.AnimNotifies)Queued.Add(Reference(Ref));
            Output->SetArrayField(TEXT("windows"),Windows);Output->SetArrayField(TEXT("queue"),Queued);
            Output->SetNumberField(TEXT("seedAfter"),uint32(Queue.RandomStream.GetCurrentSeed()));Frames.Add(MakeShared<FJsonValueObject>(Output));
        }
        auto OutTrace=MakeShared<FJsonObject>();OutTrace->SetStringField(TEXT("profile"),Trace->GetStringField(TEXT("profile")));
        OutTrace->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));OutTrace->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(OutTrace));
    }
    auto Result=MakeShared<FJsonObject>();Result->SetArrayField(TEXT("traces"),Traces);return Write(Result);
}
