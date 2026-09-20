#include "AlsNotifyWindowCommandlet.h"
#include "AlsNotifyQueueProbe.h"
#include "AlsNotifyLifecycleProbe.h"
#include "AlsMontageLifecycleProbe.h"

#include "Animation/AnimNotifyLibrary.h"
#include "Animation/AnimNotifyQueue.h"
#include "Animation/AnimSequence.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

UAlsNotifyWindowCommandlet::UAlsNotifyWindowCommandlet()
{
    IsClient = false; IsServer = false; IsEditor = true; LogToConsole = true;
}

int32 UAlsNotifyWindowCommandlet::Main(const FString& Params)
{
    if (FParse::Param(*Params, TEXT("MontageLifecycle")))
    {
        FString Output;
        return FParse::Value(*Params,TEXT("Output="),Output) && UAlsMontageLifecycleProbe::ExportTrace(Output, FParse::Param(*Params, TEXT("IncludeActions"))) ? 0 : 51;
    }
    if (FParse::Param(*Params, TEXT("Queue"))) return RunAlsNotifyQueueProbe(Params);
    if (FParse::Param(*Params, TEXT("Lifecycle"))) return RunAlsNotifyLifecycleProbe(Params);
    const bool bLoopingProbe = FParse::Param(*Params, TEXT("Looping"));
    FString OutputPath, SyncPath, SyncText;
    TSharedPtr<FJsonObject> SyncRoot;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath) ||
        !FParse::Value(*Params, TEXT("SyncTrace="), SyncPath) ||
        !FFileHelper::LoadFileToString(SyncText, *SyncPath) ||
        !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(SyncText), SyncRoot) || !SyncRoot) return 1;
    TArray<UAnimSequence*> Sequences;
    TArray<TSharedPtr<FJsonValue>> Assets, Windows;
    int32 NativeNotifyCount = 0;
    for (const auto& AssetValue : SyncRoot->GetArrayField(TEXT("assets")))
    {
        const auto Asset = AssetValue->AsObject();
        const auto Sequence = LoadObject<UAnimSequence>(nullptr, *Asset->GetStringField(TEXT("path")));
        if (!Sequence || Sequence->GetPlayLength() != static_cast<float>(Asset->GetNumberField(TEXT("length")))) return 2;
        Sequences.Add(Sequence);
    }
    if (Sequences.Num() != 5) return 3;
    // Synthetic sources inherit native extraction without copying FBX import/compression state.
    for (int32 AssetIndex = 0; AssetIndex < 5; ++AssetIndex)
    {
        auto Sequence = NewObject<UAlsNotifyWindowProbeSequence>(GetTransientPackage(), NAME_None, RF_Transient);
        Sequence->ProbeLength = Sequences[AssetIndex]->GetPlayLength();
        const float Length = Sequence->GetPlayLength();
        const auto Add = [&](float Time, float Duration, float StartOffset, float EndOffset)
        {
            auto& Event = Sequence->Notifies.AddDefaulted_GetRef();
            Event.NotifyName = *FString::Printf(TEXT("Window_%d"), Sequence->Notifies.Num() - 1);
            Event.Link(Sequence, Time * Length);
            if (Duration > 0)
            {
                Event.NotifyStateClass = NewObject<UAlsNotifyWindowProbeState>(Sequence);
                Event.SetDuration(Duration * Length);
            }
            Event.TriggerTimeOffset = StartOffset * Length;
            Event.EndTriggerTimeOffset = EndOffset * Length;
        };
        // Deliberately unsorted, with overlapping states, exact endpoints and effective offsets.
        Add(.4f, 0, 0, 0); Add(.2f, .3f, 0, 0); Add(0, 1, 0, 0);
        Add(0, 0, .0001f, 0); Add(1, 0, -.0001f, 0);
        Add(.5f, .2f, -.0001f, .0001f); Add(0, .1f, -.0001f, 0);
        Add(.75f, 0, 0, 0); Add(.2f, .1f, .0001f, -.0001f);
        Sequences.Add(Sequence);
    }
    auto Walk = LoadObject<UAnimSequence>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_F"));
    if (!Walk || Walk->Notifies.IsEmpty()) return 4;
    Sequences.Add(Walk);
    const auto Extract = [&](int32 AssetIndex, float Start, float Delta, int32 Trace, int32 Frame, int32 Slot, bool bLooping = false)
    {
        auto Sequence = Sequences[AssetIndex];
        FAnimTickRecord Tick;
        Tick.bLooping = bLooping;
        FAnimNotifyContext Context = bLooping ? FAnimNotifyContext(Tick) : FAnimNotifyContext();
        Sequence->GetAnimNotifies(Start, Delta, Context);
        const auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("asset"), AssetIndex); Row->SetNumberField(TEXT("start"), Start);
        Row->SetNumberField(TEXT("delta"), Delta); Row->SetNumberField(TEXT("trace"), Trace);
        Row->SetNumberField(TEXT("frame"), Frame); Row->SetNumberField(TEXT("slot"), Slot);
        if (bLoopingProbe)
        {
            Row->SetBoolField(TEXT("looping"), bLooping);
            const float Length = Sequence->GetPlayLength();
            const uint32 MaxPasses = Length > 0 && FMath::Abs(Delta) > Length
                ? FMath::Clamp(uint32(Delta / Length), 2u, 1000u) : 2u;
            Row->SetNumberField(TEXT("nativeMaxPasses"), MaxPasses);
        }
        TArray<TSharedPtr<FJsonValue>> Expected;
        for (const auto& Reference : Context.ActiveNotifies)
        {
            const auto Event = Reference.GetNotify();
            const auto Item = MakeShared<FJsonObject>();
            Item->SetNumberField(TEXT("index"), Event - Sequence->Notifies.GetData());
            Item->SetBoolField(TEXT("finished"), UAnimNotifyLibrary::NotifyStateReachedEnd(Reference));
            Expected.Add(MakeShared<FJsonValueObject>(Item));
        }
        Row->SetArrayField(TEXT("expected"), Expected); Windows.Add(MakeShared<FJsonValueObject>(Row));
    };
    for (int32 Index = 0; Index < Sequences.Num(); ++Index)
    {
        const auto Sequence = Sequences[Index];
        const float Length = Sequence->GetPlayLength();
        const auto Asset = MakeShared<FJsonObject>();
        Asset->SetNumberField(TEXT("id"), Index); Asset->SetNumberField(TEXT("length"), Length);
        Asset->SetBoolField(TEXT("synthetic"), Index >= 5 && Index < 10);
        Asset->SetStringField(TEXT("sourcePath"), Index >= 5 && Index < 10 ? Sequences[Index - 5]->GetPathName() : Sequence->GetPathName());
        TArray<TSharedPtr<FJsonValue>> Events;
        TArray<float> Starts = {0.f, .1f * Length, .2f * Length, .4f * Length, .5f * Length, .75f * Length, Length};
        for (int32 NotifyIndex = 0; NotifyIndex < Sequence->Notifies.Num(); ++NotifyIndex)
        {
            const auto& Event = Sequence->Notifies[NotifyIndex];
            const auto Item = MakeShared<FJsonObject>();
            Item->SetNumberField(TEXT("index"), NotifyIndex);
            Item->SetNumberField(TEXT("authoredTime"), Event.GetTime());
            Item->SetNumberField(TEXT("duration"), Event.GetDuration());
            Item->SetNumberField(TEXT("triggerTime"), Event.GetTriggerTime());
            Item->SetNumberField(TEXT("endTriggerTime"), Event.GetEndTriggerTime());
            Item->SetBoolField(TEXT("state"), Event.NotifyStateClass != nullptr);
            Events.Add(MakeShared<FJsonValueObject>(Item));
            Starts.Add(FMath::Clamp(Event.GetTriggerTime(), 0.f, Length));
            Starts.Add(FMath::Clamp(Event.GetEndTriggerTime(), 0.f, Length));
        }
        if (Index < 5 || Index == 10) NativeNotifyCount += Events.Num();
        Asset->SetArrayField(TEXT("events"), Events); Assets.Add(MakeShared<FJsonValueObject>(Asset));
        for (const float Start : Starts)
            for (const float Scale : {-2.f, -1.f, -.25f, -.0001f, 0.f, .0001f, .25f, 1.f, 2.f})
                Extract(Index, Start, Scale * Length, -1, -1, -1, bLoopingProbe);
        if (bLoopingProbe)
            for (const float Scale : {-1001.f, -3.25f, -2.5f, -2.001f, 2.001f, 2.5f, 3.25f, 1001.f})
                Extract(Index, .5f * Length, Scale * Length, -1, -1, -1, true);
    }
    int32 TraceIndex = 0, SyncWindows = 0;
    for (const auto& TraceValue : SyncRoot->GetArrayField(TEXT("traces")))
    {
        int32 FrameIndex = 0;
        for (const auto& FrameValue : TraceValue->AsObject()->GetArrayField(TEXT("frames")))
        {
            for (const auto& TickValue : FrameValue->AsObject()->GetArrayField(TEXT("output")))
            {
                const auto Tick = TickValue->AsObject();
                const int32 Slot = static_cast<int32>(Tick->GetNumberField(TEXT("slot")));
                Extract(5 + Slot % 5, Tick->GetNumberField(TEXT("previous")), Tick->GetNumberField(TEXT("advance")), TraceIndex, FrameIndex, Slot);
                ++SyncWindows;
            }
            ++FrameIndex;
        }
        ++TraceIndex;
    }
    if (NativeNotifyCount == 0 || SyncWindows == 0) return 5;
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), bLoopingProbe ? 2 : 1);
    Root->SetStringField(TEXT("source"), bLoopingProbe
        ? TEXT("UE UAnimSequenceBase::GetAnimNotifies; looping and non-looping extraction, not NotifyQueue filtering or dispatch")
        : TEXT("UE UAnimSequenceBase::GetAnimNotifies; non-looping extraction, not NotifyQueue filtering or dispatch"));
    Root->SetArrayField(TEXT("assets"), Assets); Root->SetArrayField(TEXT("windows"), Windows);
    FString Text;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return 6;
    UE_LOG(LogTemp, Display, TEXT("ALS_NOTIFY_WINDOW_OK assets=%d native_notifies=%d windows=%d sync_windows=%d assets_saved=0"),
        Assets.Num(), NativeNotifyCount, Windows.Num(), SyncWindows);
    return 0;
}
