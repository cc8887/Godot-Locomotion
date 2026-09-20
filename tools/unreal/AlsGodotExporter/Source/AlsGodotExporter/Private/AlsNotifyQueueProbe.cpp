#include "AlsNotifyQueueProbe.h"
#include "AlsNotifyWindowCommandlet.h"
#include "Animation/AnimEventsFilterScope.h"
#include "Animation/AnimNotifyEndDataContext.h"
#include "Animation/AnimNotifyLibrary.h"
#include "Animation/AnimNotifyQueue.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSync.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/World.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
struct FQueueScopeFilter final : UE::Anim::IAnimEventsFilterContext
{
    bool Filter;
    int32* Checks;
    FQueueScopeFilter(bool InFilter, int32* InChecks) : Filter(InFilter), Checks(InChecks) {}
    virtual bool ShouldFilterNotify(const FAnimNotifyEventReference&) const override { ++*Checks; return Filter; }
};
struct FQueuePlaybackContext final : UE::Anim::IAnimNotifyEventContextDataInterface
{
    DECLARE_NOTIFY_CONTEXT_INTERFACE(FQueuePlaybackContext)
public:
    int32 Handle;
    explicit FQueuePlaybackContext(int32 InHandle) : Handle(InHandle) {}
};
IMPLEMENT_NOTIFY_CONTEXT_INTERFACE(FQueuePlaybackContext)

struct FPolicy
{
    int32 Object, State, Name;
    float Weight, Chance;
    int32 Filter, Lod;
    bool Request, Server, Follower;
};
struct FQueueMergeProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    using FAnimInstanceProxy::PreUpdate;
    using FAnimInstanceProxy::PostUpdate;
};
}

int32 RunAlsNotifyQueueProbe(const FString& Params)
{
    FString Output;
    if (!FParse::Value(*Params, TEXT("Output="), Output)) return 31;
    auto Source = NewObject<UAlsNotifyQueueProbeSequence>();
    TArray<UObject*> Objects;
    for (int32 i = 0; i < 8; ++i)
        Objects.Add(i == 1 || i == 2 || i == 5
            ? static_cast<UObject*>(NewObject<UAlsNotifyWindowProbeState>(Source))
            : static_cast<UObject*>(NewObject<UAlsNotifyQueueProbeNotify>(Source)));
    const FName Names[] = {TEXT("Shared"), TEXT("Other"), TEXT("State"), TEXT("Named")};
    const FPolicy Policies[] = {
        {0,-1,0,.3f,1,0,0,true,true,false},
        {0,-1,1,.3f,.5f,0,0,true,true,false},
        {-1,1,2,.3f,0,0,0,true,true,false},
        {-1,1,2,.3f,1,0,0,true,true,false},
        {-1,2,2,.3f,.2f,0,0,true,true,false},
        {-1,-1,2,0,1,0,0,true,true,true},
        {3,-1,0,0,0,0,0,true,true,true},
        {4,-1,0,0,.5f,0,0,true,false,true},
        {-1,5,2,.5f,0,1,2,false,true,true},
        {6,-1,0,0,1,1,1,false,true,true},
        {7,-1,0,0,.5f,0,0,true,false,false},
        {-1,-1,3,0,.25f,0,0,true,true,true}
    };
    Source->Notifies.SetNum(UE_ARRAY_COUNT(Policies));
    TArray<TSharedPtr<FJsonValue>> PolicyRows;
    for (int32 i = 0; i < Source->Notifies.Num(); ++i)
    {
        const auto& P = Policies[i]; auto& E = Source->Notifies[i];
        E.Notify = P.Object >= 0 ? CastChecked<UAnimNotify>(Objects[P.Object]) : nullptr;
        E.NotifyStateClass = P.State >= 0 ? CastChecked<UAnimNotifyState>(Objects[P.State]) : nullptr;
        E.NotifyName = Names[P.Name]; E.TriggerWeightThreshold = P.Weight; E.NotifyTriggerChance = P.Chance;
        E.NotifyFilterType = static_cast<ENotifyFilterType::Type>(P.Filter); E.NotifyFilterLOD = P.Lod;
        E.bCanBeFilteredViaRequest = P.Request; E.bTriggerOnDedicatedServer = P.Server; E.bTriggerOnFollower = P.Follower;
        E.MontageTickType = i % 2 ? EMontageNotifyTickType::BranchingPoint : EMontageNotifyTickType::Queued;
        auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("eventId"), i); Row->SetNumberField(TEXT("sourceIndex"), i); Row->SetNumberField(TEXT("trackIndex"), 0);
        Row->SetNumberField(TEXT("notifyObjectId"), P.Object); Row->SetNumberField(TEXT("stateObjectId"), P.State);
        Row->SetNumberField(TEXT("nameId"), P.Name); Row->SetNumberField(TEXT("weightThreshold"), E.TriggerWeightThreshold);
        Row->SetNumberField(TEXT("chance"), E.NotifyTriggerChance); Row->SetNumberField(TEXT("filterType"), E.NotifyFilterType);
        Row->SetNumberField(TEXT("filterLod"), E.NotifyFilterLOD); Row->SetNumberField(TEXT("tickMode"), E.MontageTickType);
        Row->SetBoolField(TEXT("filterViaRequest"), E.bCanBeFilteredViaRequest);
        Row->SetBoolField(TEXT("onDedicatedServer"), E.bTriggerOnDedicatedServer); Row->SetBoolField(TEXT("onFollower"), E.bTriggerOnFollower);
        PolicyRows.Add(MakeShared<FJsonValueObject>(Row));
    }
    auto World = NewObject<UWorld>();
    World->WorldType = EWorldType::PIE;
    auto Component = NewObject<USkeletalMeshComponent>(World);
    if (Component->GetWorld() != World) return 32;
    auto Instance = NewObject<UAnimInstance>(Component);
    FAnimNotifyQueue& Queue = Instance->NotifyQueue;
    auto MergeComponent = NewObject<USkeletalMeshComponent>();
    auto MergeInstance = NewObject<UAnimInstance>(MergeComponent);
    FQueueMergeProxy MergeProxy(MergeInstance);
    MergeProxy.Initialize(MergeInstance);
    TArray<TSharedPtr<FJsonValue>> Cases;
    int32 ScopeChecks = 0;
    const auto RefJson = [&](const FAnimNotifyEventReference& Ref)
    {
        auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("policyIndex"), Ref.GetNotify() ? Ref.GetNotify() - Source->Notifies.GetData() : -1);
        const auto Context = Ref.GetContextData<FQueuePlaybackContext>();
        Row->SetNumberField(TEXT("occurrenceHandleId"), Context ? Context->Handle : -1);
        Row->SetNumberField(TEXT("currentTime"), Ref.GetCurrentAnimationTime());
        Row->SetBoolField(TEXT("activeContext"), Ref.IsActiveContext());
        Row->SetBoolField(TEXT("reachedEnd"), UAnimNotifyLibrary::NotifyStateReachedEnd(Ref));
        Row->SetBoolField(TEXT("hasSource"), Ref.GetSourceObject() != nullptr);
        const auto Filter = static_cast<const FQueueScopeFilter*>(Ref.GetContextData<UE::Anim::IAnimEventsFilterContext>());
        Row->SetBoolField(TEXT("scopeFiltered"), Filter && Filter->Filter);
        return MakeShared<FJsonValueObject>(Row);
    };
    const auto Step = [&](bool Reset, bool Append, bool Leader, bool Dedicated, int32 Lod, float Weight, int32 Pattern)
    {
        World->SetPlayInEditorInitialNetMode(Dedicated ? NM_DedicatedServer : NM_Standalone);
        if ((World->GetNetMode() == NM_DedicatedServer) != Dedicated) return false;
        if (Reset) Instance->ClearQueuedAnimEvents(true);
        Queue.PredictedLODLevel = Lod;
        const uint32 Seed = Queue.RandomStream.GetCurrentSeed();
        auto Row = MakeShared<FJsonObject>();
        Row->SetBoolField(TEXT("reset"), Reset); Row->SetBoolField(TEXT("append"), Append);
        Row->SetBoolField(TEXT("leader"), Leader); Row->SetBoolField(TEXT("dedicatedServer"), Dedicated);
        Row->SetNumberField(TEXT("predictedLod"), Lod); Row->SetNumberField(TEXT("weight"), Weight);
        Row->SetNumberField(TEXT("seed"), Seed);
        TArray<TSharedPtr<FJsonValue>> Before, Incoming, Expected;
        for (const auto& Ref : Queue.AnimNotifies) Before.Add(RefJson(Ref));
        TArray<FAnimNotifyEventReference> Refs;
        const int32 Order[] = {0,0,2,3,4,5,2,6,7,8,9,10,11,-1};
        ScopeChecks = 0;
        for (int32 i = 0; i < UE_ARRAY_COUNT(Order); ++i)
        {
            int32 Index = Pattern == 1 ? Order[UE_ARRAY_COUNT(Order) - 1 - i] : Order[i];
            const bool Valid = Index >= 0;
            FAnimNotifyEventReference Ref(Valid ? &Source->Notifies[Index] : nullptr, Valid ? Source : nullptr);
            float Time = float(Cases.Num() * 100 + i) * .001f;
            FAnimTickRecord Tick; Tick.TimeAccumulator = &Time; Tick.bActiveContext = i % 2 == 0;
            Ref.GatherTickRecordData(Tick);
            Ref.AddContextData<FQueuePlaybackContext>(Cases.Num() * 100 + i);
            Ref.AddContextData<UE::Anim::FAnimNotifyEndDataContext>(i % 3 == 0);
            Ref.AddContextData<FQueueScopeFilter>(Pattern != 2 && i % 4 == 0, &ScopeChecks);
            Refs.Add(Ref); Incoming.Add(RefJson(Ref));
        }
        if (Append)
        {
            // Seed the proxy queue via its normal asset tick, then call the native Append owner.
            Source->QueuedReferences = Refs;
            MergeProxy.PreUpdate(MergeInstance, 0.f);
            UE::Anim::FAnimSync Sync;
            float Time = 0.f; FMarkerTickRecord Marker; FDeltaTimeRecord Delta;
            FAnimTickRecord Tick(Source, false, 1.f, false, 1.f, Time, Marker);
            Tick.DeltaTimeRecord = &Delta;
            Sync.AddTickRecord(Tick, UE::Anim::FAnimSyncParams(NAME_None, EAnimGroupRole::CanBeLeader, EAnimSyncMethod::DoNotSync));
            Sync.TickAssetPlayerInstances(MergeProxy, 0.f);
            MergeProxy.PostUpdate(Instance);
        }
        else Queue.AddAnimNotifies(Leader, Refs, Weight);
        for (const auto& Ref : Queue.AnimNotifies) Expected.Add(RefJson(Ref));
        Row->SetArrayField(TEXT("before"), Before); Row->SetArrayField(TEXT("incoming"), Incoming);
        Row->SetArrayField(TEXT("expected"), Expected);
        Row->SetNumberField(TEXT("candidateSeed"), uint32(Queue.RandomStream.GetCurrentSeed()));
        Row->SetNumberField(TEXT("scopeChecks"), ScopeChecks);
        Cases.Add(MakeShared<FJsonValueObject>(Row)); return true;
    };
    for (bool Dedicated : {false, true})
    for (bool Leader : {false, true})
    for (int32 Lod : {-1,0,1,2,3})
    for (float Weight : {0.f,.29999f,.3f,.5f,1.f})
        if (!Step(true, false, Leader, Dedicated, Lod, Weight, 0)) return 33;
    for (int32 i = 0; i < 48; ++i)
        if (!Step(i % 6 == 0, i % 3 == 2, true, false, 0, 1, i % 3)) return 34;
    auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE FAnimNotifyQueue AddAnimNotifies/Append/Reset; not lifecycle dispatch"));
    Root->SetNumberField(TEXT("initialSeed"), 0x05629063);
    Root->SetArrayField(TEXT("policies"), PolicyRows); Root->SetArrayField(TEXT("cases"), Cases);
    FString Json;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Json)) ||
        !FFileHelper::SaveStringToFile(Json, *Output, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return 35;
    UE_LOG(LogTemp, Display, TEXT("ALS_NOTIFY_QUEUE_OK policies=%d cases=%d assets_saved=0"), PolicyRows.Num(), Cases.Num());
    return 0;
}
