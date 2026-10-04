#include "AlsLyraGraphLibrary.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimSyncScope.h"
#include "Animation/AnimInertializationSyncScope.h"
#include "AnimNodes/AnimNode_SequenceEvaluator.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UnrealType.h"

namespace LyraEvaluatorProbe
{
void Number(const TSharedPtr<FJsonObject>& Row, const TCHAR* Name, float Value)
{
    uint32 Bits; FMemory::Memcpy(&Bits, &Value, sizeof(Bits));
    Row->SetNumberField(Name, Value); Row->SetNumberField(FString(Name) + TEXT("Bits"), Bits);
}
struct FProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    using FAnimInstanceProxy::PreUpdate;
    using FAnimInstanceProxy::PostUpdate;
    using FAnimInstanceProxy::UpdateAnimation;
};
struct FSourceAccess : FAnimNode_AssetPlayerBase
{
    static float* Time(FAnimNode_AssetPlayerBase& Node) { return &(Node.*&FSourceAccess::InternalTimeAccumulator); }
    static FMarkerTickRecord& Marker(FAnimNode_AssetPlayerBase& Node) { return Node.*&FSourceAccess::MarkerTickRecord; }
};
TSharedRef<FJsonObject> Marker(const FMarkerTickRecord& Value)
{
    const auto Row = MakeShared<FJsonObject>();
    Row->SetNumberField(TEXT("previous"), Value.PreviousMarker.MarkerIndex);
    Row->SetNumberField(TEXT("next"), Value.NextMarker.MarkerIndex);
    Number(Row, TEXT("previousDistance"), Value.PreviousMarker.MarkerIndex == -2 ? 0 : Value.PreviousMarker.TimeToMarker);
    Number(Row, TEXT("nextDistance"), Value.NextMarker.MarkerIndex == -2 ? 0 : Value.NextMarker.TimeToMarker);
    return Row;
}
TSharedRef<FJsonObject> Position(const FMarkerSyncAnimPosition& Value)
{
    const auto Row = MakeShared<FJsonObject>();
    Row->SetStringField(TEXT("previous"), Value.PreviousMarkerName.ToString());
    Row->SetStringField(TEXT("next"), Value.NextMarkerName.ToString());
    Number(Row, TEXT("alpha"), Value.PositionBetweenMarkers); return Row;
}
}

FString UAlsLyraGraphLibrary::ReadEvaluatorSyncTrace(const FString& RequestsJson)
{
    using namespace LyraEvaluatorProbe;
    TSharedPtr<FJsonObject> Requests;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Requests)) return {};
    TArray<UAnimSequence*> Assets;
    for (const auto& Path : Requests->GetArrayField(TEXT("assets")))
    {
        auto* Asset = LoadObject<UAnimSequence>(nullptr, *Path->AsString());
        if (!Asset || !Asset->GetSkeleton()) { UE_LOG(LogTemp, Error, TEXT("LYRA_EVALUATOR_PROBE_FAIL asset=%s"), *Path->AsString()); return {}; } Assets.Add(Asset);
    }
    auto* LoopProperty = FindFProperty<FBoolProperty>(FAnimNode_SequenceEvaluator_Standalone::StaticStruct(), TEXT("bShouldLoop"));
    auto* StartProperty = FindFProperty<FFloatProperty>(FAnimNode_SequenceEvaluator_Standalone::StaticStruct(), TEXT("StartPosition"));
    if (!LoopProperty || !StartProperty) { UE_LOG(LogTemp, Error, TEXT("LYRA_EVALUATOR_PROBE_FAIL properties loop=%d start=%d"), LoopProperty != nullptr, StartProperty != nullptr); return {}; }
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const auto& TraceValue : Requests->GetArrayField(TEXT("traces")))
    {
        const auto Trace = TraceValue->AsObject();
        auto* Component = NewObject<USkeletalMeshComponent>(); auto* Instance = NewObject<UAnimInstance>(Component);
        FProxy Proxy(Instance); Proxy.Initialize(Instance);
        FAnimNode_SequenceEvaluator_Standalone Nodes[2];
        float PlayerTime = 0; FMarkerTickRecord PlayerMarker; FDeltaTimeRecord PlayerDelta;
        // FMarkerPair's constructor only initializes its index. Seed the
        // unused distance storage explicitly for reproducible boundary probes;
        // native Reset/update/tick keep their original behavior thereafter.
        for (auto& Node : Nodes)
        {
            auto& Storage = FSourceAccess::Marker(Node);
            Storage.PreviousMarker.TimeToMarker = 0; Storage.NextMarker.TimeToMarker = 0;
        }
        PlayerMarker.PreviousMarker.TimeToMarker = 0; PlayerMarker.NextMarker.TimeToMarker = 0;
        bool Initialized[2] = { false, false }; TArray<TSharedPtr<FJsonValue>> Frames;
        for (const auto& FrameValue : Trace->GetArrayField(TEXT("frames")))
        {
            const auto Frame = FrameValue->AsObject(); const float Delta = static_cast<float>(Frame->GetNumberField(TEXT("delta")));
            Proxy.PreUpdate(Instance, Delta);
            FAnimationUpdateSharedContext Shared; FAnimationUpdateContext Context(&Proxy, Delta, &Shared);
            UE::Anim::TScopedGraphMessage<UE::Anim::FAnimSyncGroupScope> Scope(Context, Context, FName(TEXT("Probe")), EAnimGroupRole::CanBeLeader);
            TArray<TSharedPtr<FJsonValue>> Inputs; TArray<float*> Pointers;
            for (const auto& InputValue : Frame->GetArrayField(TEXT("inputs")))
            {
                const auto Input = InputValue->AsObject(); const int32 Slot = static_cast<int32>(Input->GetNumberField(TEXT("slot")));
                const int32 AssetIndex = static_cast<int32>(Input->GetNumberField(TEXT("asset")));
                if (Slot < 0 || Slot > 2 || !Assets.IsValidIndex(AssetIndex)) return {};
                auto* Asset = Assets[AssetIndex];
                const bool Looping = Input->GetBoolField(TEXT("looping"));
                const bool Reinitialize = Input->GetBoolField(TEXT("reinitialize"));
                const bool Inertial = Input->GetBoolField(TEXT("inertial"));
                const auto Method = static_cast<EAnimSyncMethod>(static_cast<int32>(Input->GetNumberField(TEXT("method"))));
                const auto Role = static_cast<EAnimGroupRole::Type>(static_cast<int32>(Input->GetNumberField(TEXT("role"))));
                const FName GroupName = Method == EAnimSyncMethod::SyncGroup ? FName(TEXT("Probe")) : NAME_None;
                const float Weight = static_cast<float>(Input->GetNumberField(TEXT("weight")));
                const auto Observation = MakeShared<FJsonObject>(); Observation->Values = Input->Values;
                if (Slot < 2)
                {
                    auto& Node = Nodes[Slot]; Node.SetSequence(Asset); Node.SetGroupName(GroupName); Node.SetGroupMethod(Method); Node.SetGroupRole(Role);
                    Node.SetTeleportToExplicitTime(Input->GetBoolField(TEXT("teleport")));
                    Node.SetReinitializationBehavior(static_cast<ESequenceEvalReinit::Type>(static_cast<int32>(Input->GetNumberField(TEXT("reinitialization")))));
                    LoopProperty->SetPropertyValue_InContainer(&Node, Looping);
                    StartProperty->SetPropertyValue_InContainer(&Node, static_cast<float>(Input->GetNumberField(TEXT("startPosition"))));
                    Node.SetExplicitTime(static_cast<float>(Input->GetNumberField(TEXT("explicitTime"))));
                    const bool Reset = Reinitialize || !Initialized[Slot];
                    if (Reset) { Node.Initialize_AnyThread(FAnimationInitializeContext(&Proxy)); Initialized[Slot] = true; }
                    Observation->SetBoolField(TEXT("reinitialize"), Reset);
                    Number(Observation, TEXT("before"), *FSourceAccess::Time(Node));
                    UE::Anim::TOptionalScopedGraphMessage<UE::Anim::FAnimInertializationSyncScope> InertiaScope(Inertial, Context);
                    Node.Update_AnyThread(Context.FractionalWeight(Weight));
                    Number(Observation, TEXT("prepared"), *FSourceAccess::Time(Node)); Pointers.Add(FSourceAccess::Time(Node));
                }
                else
                {
                    if (Reinitialize) { PlayerTime = static_cast<float>(Input->GetNumberField(TEXT("startPosition"))); PlayerMarker.Reset(); }
                    Number(Observation, TEXT("before"), PlayerTime); Number(Observation, TEXT("prepared"), PlayerTime);
                    FAnimTickRecord Tick(Asset, Looping, static_cast<float>(Input->GetNumberField(TEXT("playRate"))), false, Weight, PlayerTime, PlayerMarker);
                    Tick.DeltaTimeRecord = &PlayerDelta; Tick.bRequestedInertialization = Inertial;
                    Context.GetMessageChecked<UE::Anim::FAnimSyncGroupScope>().AddTickRecord(Tick, UE::Anim::FAnimSyncParams(GroupName, Role, Method)); Pointers.Add(&PlayerTime);
                }
                Inputs.Add(MakeShared<FJsonValueObject>(Observation));
            }
            // Normal proxy entry ticks Sync exactly once. The deprecated
            // TickAssetPlayerInstances wrapper flips its buffers a second time
            // in this build and is not the production update path.
            Proxy.UpdateAnimation();
            Proxy.PostUpdate(Instance);
            const auto* Group = Proxy.GetSyncGroupMapRead().Find(TEXT("Probe"));
            const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("delta"), Delta);
            int32 LeaderSlot = -1;
            if (Group && !Group->ActivePlayers.IsEmpty())
            {
                const auto& Leader = Group->ActivePlayers[Group->GroupLeaderIndex];
                for (int32 Index = 0; Index < Pointers.Num(); ++Index) if (Pointers[Index] == Leader.TimeAccumulator)
                    LeaderSlot = static_cast<int32>(Inputs[Index]->AsObject()->GetNumberField(TEXT("slot")));
                Number(Row, TEXT("leaderScore"), Leader.LeaderScore);
                Row->SetNumberField(TEXT("leaderIndex"), Group->GroupLeaderIndex);
            }
            Row->SetNumberField(TEXT("leader"), LeaderSlot);
            Number(Row, TEXT("previousRatio"), Group ? Group->PreviousAnimLengthRatio : 0);
            Number(Row, TEXT("ratio"), Group ? Group->AnimLengthRatio : 0);
            Row->SetObjectField(TEXT("markerStart"), Position(Group ? Group->MarkerTickContext.GetMarkerSyncStartPosition() : FMarkerSyncAnimPosition()));
            Row->SetObjectField(TEXT("markerEnd"), Position(Group ? Group->MarkerTickContext.GetMarkerSyncEndPosition() : FMarkerSyncAnimPosition()));
            TArray<TSharedPtr<FJsonValue>> Outputs;
            for (int32 Index = 0; Index < Inputs.Num(); ++Index)
            {
                const auto Input = Inputs[Index]->AsObject(); const int32 Slot = static_cast<int32>(Input->GetNumberField(TEXT("slot")));
                const FAnimTickRecord* Record = nullptr;
                if (Group) for (const auto& Candidate : Group->ActivePlayers) if (Candidate.TimeAccumulator == Pointers[Index]) Record = &Candidate;
                for (const auto& Candidate : Proxy.GetUngroupedActivePlayersRead()) if (Candidate.TimeAccumulator == Pointers[Index]) Record = &Candidate;
                if (!Record || !Record->DeltaTimeRecord)
                {
                    UE_LOG(LogTemp, Error, TEXT("LYRA_EVALUATOR_PROBE_FAIL record scenario=%s frame=%d slot=%d grouped=%d independent=%d"),
                        *Trace->GetStringField(TEXT("scenario")), Frames.Num(), Slot, Group ? Group->ActivePlayers.Num() : 0, Proxy.GetUngroupedActivePlayersRead().Num());
                    return {};
                }
                Number(Input, TEXT("preparedRate"), Record->PlayRateMultiplier);
                Input->SetBoolField(TEXT("isEvaluator"), Record->bIsEvaluator);
                const auto Output = MakeShared<FJsonObject>(); Output->SetNumberField(TEXT("slot"), Slot);
                Number(Output, TEXT("time"), *Pointers[Index]);
                Number(Output, TEXT("previous"), Record->DeltaTimeRecord->GetPrevious());
                Number(Output, TEXT("delta"), Record->DeltaTimeRecord->Delta);
                Output->SetObjectField(TEXT("marker"), Marker(*Record->MarkerTickRecord));
                Outputs.Add(MakeShared<FJsonValueObject>(Output));
            }
            Row->SetArrayField(TEXT("inputs"), Inputs); Row->SetArrayField(TEXT("outputs"), Outputs);
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Output = MakeShared<FJsonObject>(); Output->SetNumberField(TEXT("hz"), Trace->GetNumberField(TEXT("hz")));
        Output->SetStringField(TEXT("scenario"), Trace->GetStringField(TEXT("scenario")));
        Output->SetArrayField(TEXT("frames"), Frames); Traces.Add(MakeShared<FJsonValueObject>(Output));
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetArrayField(TEXT("traces"), Traces);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}
