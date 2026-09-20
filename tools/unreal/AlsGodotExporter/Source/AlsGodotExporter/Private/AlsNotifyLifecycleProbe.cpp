#include "AlsNotifyLifecycleProbe.h"
#include "AlsNotifyWindowCommandlet.h"
#include "Animation/ActiveMontageInstanceScope.h"
#include "Animation/AnimationInstanceScope.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimNotifyEndDataContext.h"
#include "Animation/AnimNotifyLibrary.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "HAL/IConsoleManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
struct FLifecycleContext final : UE::Anim::IAnimNotifyEventContextDataInterface
{
    DECLARE_NOTIFY_CONTEXT_INTERFACE(FLifecycleContext)
public:
    int32 Policy, Handle;
    FLifecycleContext(int32 InPolicy, int32 InHandle) : Policy(InPolicy), Handle(InHandle) {}
};
IMPLEMENT_NOTIFY_CONTEXT_INTERFACE(FLifecycleContext)

TArray<TSharedPtr<FJsonValue>>* RecordedCallbacks = nullptr;

TSharedPtr<FJsonObject> InputJson(const FAnimNotifyEventReference& Ref)
{
    const auto Context = Ref.GetContextData<FLifecycleContext>();
    auto R = MakeShared<FJsonObject>();
    R->SetNumberField(TEXT("policyIndex"), Context ? Context->Policy : -1);
    R->SetNumberField(TEXT("occurrenceHandleId"), Context ? Context->Handle : -1);
    R->SetNumberField(TEXT("currentTime"), Ref.GetCurrentAnimationTime());
    R->SetBoolField(TEXT("activeContext"), Ref.IsActiveContext());
    R->SetBoolField(TEXT("reachedEnd"), UAnimNotifyLibrary::NotifyStateReachedEnd(Ref));
    R->SetBoolField(TEXT("hasSource"), Ref.GetSourceObject() != nullptr);
    R->SetBoolField(TEXT("scopeFiltered"), false);
    auto Input = MakeShared<FJsonObject>(); Input->SetObjectField(TEXT("reference"), R);
    const auto Montage = Ref.GetContextData<UE::Anim::FAnimNotifyMontageInstanceContext>();
    const auto Asset = Ref.GetContextData<UE::Anim::FAnimNotifyAssetPlayerInstanceContext>();
    Input->SetNumberField(TEXT("sourceKind"), Montage ? 2 : Asset ? 1 : 0);
    Input->SetNumberField(TEXT("sourceInstanceId"), Montage ? uint32(Montage->MontageInstanceID) : Asset ? Asset->AssetPlayerInstanceID : 0);
    const auto E = Ref.GetNotify();
    Input->SetBoolField(TEXT("noMergeOnConcurrentPlay"), E && E->NotifyStateClass &&
        (E->NotifyStateClass->NotifyStateBehaviorFlags & uint8(EAnimNotifyStateBehaviorFlags::NoMergeOnConcurrentPlay)) != 0);
    Input->SetNumberField(TEXT("duration"), E ? E->GetDuration() : 0);
    return Input;
}
TSharedPtr<FJsonObject> StateJson(const FAnimNotifyEventReference& Ref)
{
    auto State = MakeShared<FJsonObject>(); State->SetObjectField(TEXT("input"), InputJson(Ref));
    State->SetNumberField(TEXT("instanceId"), Ref.GetNotifyInstanceID()); return State;
}
void Record(int32 Kind, float Seconds, const FAnimNotifyEventReference& Ref)
{
    if (!RecordedCallbacks) return;
    auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("kind"), Kind);
    Row->SetNumberField(TEXT("seconds"), Seconds); Row->SetObjectField(TEXT("state"), StateJson(Ref));
    RecordedCallbacks->Add(MakeShared<FJsonValueObject>(Row));
}
struct FInput { int32 Policy, Kind, Instance, Handle; bool End = false, Active = true; };
}
void UAlsNotifyLifecycleProbeNotify::Notify(USkeletalMeshComponent*, UAnimSequenceBase*, const FAnimNotifyEventReference& Ref) { Record(0, 0, Ref); }
void UAlsNotifyLifecycleProbeState::NotifyBegin(USkeletalMeshComponent*, UAnimSequenceBase*, float Duration, const FAnimNotifyEventReference& Ref) { Record(2, Duration, Ref); }
void UAlsNotifyLifecycleProbeState::NotifyTick(USkeletalMeshComponent*, UAnimSequenceBase*, float Delta, const FAnimNotifyEventReference& Ref) { Record(3, Delta, Ref); }
void UAlsNotifyLifecycleProbeState::NotifyEnd(USkeletalMeshComponent*, UAnimSequenceBase*, const FAnimNotifyEventReference& Ref) { Record(1, 0, Ref); }

int32 RunAlsNotifyLifecycleProbe(const FString& Params)
{
    FString Output;
    if (!FParse::Value(*Params, TEXT("Output="), Output)) return 41;
    auto Source = NewObject<UAlsNotifyWindowProbeSequence>();
    auto Component = NewObject<USkeletalMeshComponent>();
    auto Instance = NewObject<UAnimInstance>(Component);
    auto Shared = NewObject<UAlsNotifyLifecycleProbeState>(Source);
    auto Separate = NewObject<UAlsNotifyLifecycleProbeState>(Source);
    auto Other = NewObject<UAlsNotifyLifecycleProbeState>(Source);
    Separate->NotifyStateBehaviorFlags = uint8(EAnimNotifyStateBehaviorFlags::NoMergeOnConcurrentPlay);
    Shared->NotifyStateBehaviorFlags = Other->NotifyStateBehaviorFlags = 0;
    auto Instant = NewObject<UAlsNotifyLifecycleProbeNotify>(Source);
    Source->Notifies.SetNum(5);
    TArray<TSharedPtr<FJsonValue>> Policies, Cases;
    for (int32 i = 0; i < 5; ++i)
    {
        auto& E = Source->Notifies[i];
        E.NotifyStateClass = i < 2 ? Shared : i == 2 ? Separate : i == 3 ? Other : nullptr;
        E.Notify = i == 4 ? Instant : nullptr;
        E.NotifyName = FName(*FString::Printf(TEXT("Probe%d"), i));
        E.SetDuration(i == 4 ? 0.f : .4f + float(i) * .1f);
        auto P = MakeShared<FJsonObject>();
        P->SetNumberField(TEXT("eventId"), i); P->SetNumberField(TEXT("sourceIndex"), i);
        P->SetNumberField(TEXT("trackIndex"), 0); P->SetNumberField(TEXT("nameId"), i);
        P->SetNumberField(TEXT("notifyObjectId"), i == 4 ? 4 : -1);
        P->SetNumberField(TEXT("stateObjectId"), i < 2 ? 0 : i < 4 ? i - 1 : -1);
        P->SetNumberField(TEXT("stateBehaviorFlags"), E.NotifyStateClass ? E.NotifyStateClass->NotifyStateBehaviorFlags : 0);
        P->SetNumberField(TEXT("weightThreshold"), 0); P->SetNumberField(TEXT("chance"), 1);
        P->SetNumberField(TEXT("filterType"), 0); P->SetNumberField(TEXT("filterLod"), 0);
        P->SetNumberField(TEXT("tickMode"), 0); P->SetBoolField(TEXT("filterViaRequest"), true);
        P->SetBoolField(TEXT("onDedicatedServer"), true); P->SetBoolField(TEXT("onFollower"), true);
        Policies.Add(MakeShared<FJsonValueObject>(P));
    }
    auto DispatchPolicy = IConsoleManager::Get().FindConsoleVariable(TEXT("a.Notifies.DispatchPolicy"));
    if (!DispatchPolicy) return 42;
    const int32 OldPolicy = DispatchPolicy->GetInt(); DispatchPolicy->Set(1, ECVF_SetByCode);
    const auto MakeRef = [&](const FInput& Input)
    {
        FAnimNotifyEventReference Ref(Input.Policy >= 0 ? &Source->Notifies[Input.Policy] : nullptr, Input.Policy >= 0 ? Source : nullptr);
        float Time = float(Cases.Num() * 100 + Input.Handle) * .001f;
        FAnimTickRecord Tick; Tick.TimeAccumulator = &Time; Tick.bActiveContext = Input.Active;
        Ref.GatherTickRecordData(Tick);
        Ref.AddContextData<FLifecycleContext>(Input.Policy, Input.Handle);
        Ref.AddContextData<UE::Anim::FAnimNotifyEndDataContext>(Input.End);
        if (Input.Kind == 1) Ref.AddContextData<UE::Anim::FAnimNotifyAssetPlayerInstanceContext>(uint32(Input.Instance));
        if (Input.Kind == 2) Ref.AddContextData<UE::Anim::FAnimNotifyMontageInstanceContext>(Input.Instance);
        return Ref;
    };
    const auto Witness = [&]()
    {
        auto Ref = MakeRef({4,0,0,999}); Instance->TriggerSingleAnimNotify(Ref);
        return Ref.GetNotifyInstanceID();
    };
    const auto Step = [&](const TArray<FInput>& Inputs, int32 Mode, bool SkipGraph, float Delta)
    {
        auto Row = MakeShared<FJsonObject>();
        TArray<TSharedPtr<FJsonValue>> Before, Incoming, Expected, Callbacks;
        for (const auto& Ref : Instance->ActiveAnimNotifyEventReference) Before.Add(MakeShared<FJsonValueObject>(StateJson(Ref)));
        Instance->NotifyQueue.AnimNotifies.Reset();
        for (const auto& Input : Inputs)
        {
            auto Ref = MakeRef(Input); Incoming.Add(MakeShared<FJsonValueObject>(InputJson(Ref)));
            Instance->NotifyQueue.AnimNotifies.Add(Ref);
        }
        // Only controls the native entry's readiness input; the dispatcher and callbacks are real engine code.
        PRAGMA_DISABLE_DEPRECATION_WARNINGS
        Instance->bNeedsUpdate = SkipGraph;
        PRAGMA_ENABLE_DEPRECATION_WARNINGS
        Row->SetNumberField(TEXT("nextInstanceId"), Witness() + 1);
        RecordedCallbacks = &Callbacks;
        if (Mode == 4) Instance->EndNotifyStates();
        else Instance->TriggerAnimNotifies(Delta, static_cast<UAnimInstance::ENotifyTriggerMode>(Mode));
        RecordedCallbacks = nullptr;
        Row->SetNumberField(TEXT("candidateNextInstanceId"), Witness());
        for (const auto& Ref : Instance->ActiveAnimNotifyEventReference) Expected.Add(MakeShared<FJsonValueObject>(StateJson(Ref)));
        auto Context = MakeShared<FJsonObject>(); Context->SetNumberField(TEXT("mode"), Mode);
        Context->SetNumberField(TEXT("deltaSeconds"), Delta); Context->SetBoolField(TEXT("skipAnimGraph"), SkipGraph);
        Context->SetBoolField(TEXT("skipMontages"), false);
        Row->SetObjectField(TEXT("context"), Context); Row->SetArrayField(TEXT("before"), Before);
        Row->SetArrayField(TEXT("queued"), Incoming); Row->SetArrayField(TEXT("expected"), Expected);
        Row->SetArrayField(TEXT("callbacks"), Callbacks); Cases.Add(MakeShared<FJsonValueObject>(Row));
    };
    Step({{0,1,10,0},{2,1,20,1},{4,0,0,2},{3,2,30,3}}, 3, false, .016f);
    Step({{1,1,11,4,true,false},{4,0,0,5},{2,1,20,6},{3,2,30,7}}, 3, false, .033f);
    Step({{2,1,21,8},{0,2,90,9},{4,0,0,10}}, 3, false, 0.f);
    Step({}, 0, true, .1f);
    Step({}, 3, false, .1f);
    // Concurrent references are deliberately injected after queue filtering: this probe tests dispatch, not AddUnique.
    Step({{2,1,10,0},{2,1,11,1},{2,2,10,2},{2,0,0,3},{0,1,1,4},{0,1,2,5}}, 3, false, .02f);
    Step({{2,1,11,0},{2,2,10,1},{2,1,10,2},{2,0,0,3},{1,1,7,4}}, 3, false, .02f);
    Step({}, 4, true, .02f);
    for (int32 i = 0; i < 160; ++i)
    {
        TArray<FInput> Inputs;
        for (int32 j = 0; j < 7; ++j)
        {
            if ((i + j) % 5 == 0) continue;
            const int32 Kind = (i / 4 + j) % 3;
            Inputs.Add({(i + j) % 5, Kind, Kind == 0 ? 0 : (i / 3 + j) % 4, j, (i + j) % 4 == 0, j % 2 == 0});
        }
        Inputs.Add({-1,0,0,99});
        Step(Inputs, i % 13 == 0 ? 4 : i % 4, i % 3 == 0, i % 3 == 0 ? 0.f : i % 3 == 1 ? 1.f / 30 : 1.f / 120);
    }
    Step({}, 4, false, 0);
    DispatchPolicy->Set(OldPolicy, ECVF_SetByCode);
    auto Root = MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE UAnimInstance TriggerAnimNotifies/EndNotifyStates, class callbacks, isolated dispatch after queue"));
    Root->SetStringField(TEXT("allocatorObservation"), TEXT("Before/after class Notify witnesses consume global IDs outside each measured dispatch; no wrap in fixture."));
    Root->SetArrayField(TEXT("policies"), Policies); Root->SetArrayField(TEXT("cases"), Cases);
    FString Text; const auto Writer = TJsonWriterFactory<TCHAR, TPrettyJsonPrintPolicy<TCHAR>>::Create(&Text);
    if (!FJsonSerializer::Serialize(Root, Writer) || !FFileHelper::SaveStringToFile(Text, *Output, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return 43;
    UE_LOG(LogTemp, Display, TEXT("ALS_NOTIFY_LIFECYCLE cases=%d policies=%d assets_saved=0"), Cases.Num(), Policies.Num());
    return 0;
}
