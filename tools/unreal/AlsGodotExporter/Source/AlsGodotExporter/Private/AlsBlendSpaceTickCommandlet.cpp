#include "AlsBlendSpaceTickCommandlet.h"

#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimSync.h"
#include "Animation/BlendSpace.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace AlsBlendSpaceTickProbe
{
struct FProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    using FAnimInstanceProxy::PreUpdate;
    using FAnimInstanceProxy::PostUpdate;
};

TSharedRef<FJsonObject> Marker(const FMarkerTickRecord& Record)
{
    const auto Result = MakeShared<FJsonObject>();
    Result->SetNumberField(TEXT("previous"), Record.PreviousMarker.MarkerIndex);
    Result->SetNumberField(TEXT("next"), Record.NextMarker.MarkerIndex);
    // Reset invalidates indices, but intentionally leaves native distance storage uninitialized.
    Result->SetNumberField(TEXT("previousDistance"), Record.PreviousMarker.MarkerIndex == MarkerIndexSpecialValues::Uninitialized ? 0 : Record.PreviousMarker.TimeToMarker);
    Result->SetNumberField(TEXT("nextDistance"), Record.NextMarker.MarkerIndex == MarkerIndexSpecialValues::Uninitialized ? 0 : Record.NextMarker.TimeToMarker);
    return Result;
}

TSharedRef<FJsonObject> Position(const FMarkerSyncAnimPosition& Position)
{
    const auto Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("previous"), Position.PreviousMarkerName.ToString());
    Result->SetStringField(TEXT("next"), Position.NextMarkerName.ToString());
    Result->SetNumberField(TEXT("alpha"), Position.PositionBetweenMarkers);
    return Result;
}

TArray<TSharedPtr<FJsonValue>> Samples(const TArray<FBlendSampleData>& Cache)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (const auto& Sample : Cache)
    {
        const auto Json = MakeShared<FJsonObject>();
        Json->SetNumberField(TEXT("index"), Sample.SampleDataIndex);
        Json->SetNumberField(TEXT("weight"), Sample.TotalWeight);
        Json->SetNumberField(TEXT("sampleRate"), Sample.SamplePlayRate);
        Json->SetNumberField(TEXT("time"), Sample.Time);
        Json->SetNumberField(TEXT("previous"), Sample.PreviousTime);
        Json->SetBoolField(TEXT("deltaValid"), Sample.DeltaTimeRecord.IsPreviousValid());
        Json->SetNumberField(TEXT("deltaPrevious"), Sample.DeltaTimeRecord.IsPreviousValid() ? Sample.DeltaTimeRecord.GetPrevious() : 0);
        Json->SetNumberField(TEXT("delta"), Sample.DeltaTimeRecord.Delta);
        Json->SetObjectField(TEXT("marker"), Marker(Sample.MarkerTickRecord));
        Result.Add(MakeShared<FJsonValueObject>(Json));
    }
    return Result;
}
}

UAlsBlendSpaceTickCommandlet::UAlsBlendSpaceTickCommandlet()
{
    IsClient = false; IsServer = false; IsEditor = true; LogToConsole = true;
}

int32 UAlsBlendSpaceTickCommandlet::Main(const FString& Params)
{
    using namespace AlsBlendSpaceTickProbe;
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    const FString Base = TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/");
    TArray<UAnimationAsset*> Assets;
    for (const TCHAR* Direction : {TEXT("F"), TEXT("B"), TEXT("FL"), TEXT("BL"), TEXT("FR"), TEXT("BR")})
        Assets.Add(LoadObject<UBlendSpace>(nullptr, *(Base + TEXT("ALS_N_WalkRun_") + Direction)));
    Assets.Add(LoadObject<UAnimSequence>(nullptr, *(Base + TEXT("ALS_N_Sprint_F"))));
    Assets.Add(LoadObject<UAnimSequence>(nullptr, *(Base + TEXT("ALS_N_Sprint_F_Impulse"))));
    Assets.Add(LoadObject<UBlendSpace>(nullptr, *(Base + TEXT("Detail/ALS_N_Lean"))));
    TArray<TSharedPtr<FJsonValue>> AssetJson;
    auto DescribeSequence = [](const UAnimSequence* Sequence)
    {
        const auto Json = MakeShared<FJsonObject>();
        Json->SetStringField(TEXT("path"), Sequence->GetPathName());
        Json->SetNumberField(TEXT("length"), Sequence->GetPlayLength());
        Json->SetNumberField(TEXT("rate"), Sequence->RateScale);
        TArray<TSharedPtr<FJsonValue>> Markers;
        for (const auto& Marker : Sequence->AuthoredSyncMarkers)
        {
            const auto Item = MakeShared<FJsonObject>();
            Item->SetStringField(TEXT("name"), Marker.MarkerName.ToString());
            Item->SetNumberField(TEXT("time"), Marker.Time);
            Markers.Add(MakeShared<FJsonValueObject>(Item));
        }
        Json->SetArrayField(TEXT("markers"), Markers);
        return Json;
    };
    for (int32 Index = 0; Index < Assets.Num(); ++Index)
    {
        if (!Assets[Index]) return 2;
        const auto Json = MakeShared<FJsonObject>();
        Json->SetNumberField(TEXT("id"), Index);
        Json->SetStringField(TEXT("path"), Assets[Index]->GetPathName());
        if (const auto Space = Cast<UBlendSpace>(Assets[Index]))
        {
            Json->SetBoolField(TEXT("matchPhases"), Space->bShouldMatchSyncPhases);
            Json->SetBoolField(TEXT("legacyLength"), Space->bUseLegacySamplePointAnimationLengthCalculations);
            Json->SetBoolField(TEXT("allowMarkers"), Space->bAllowMarkerBasedSync);
            Json->SetNumberField(TEXT("notifyMode"), Space->NotifyTriggerMode);
            TArray<TSharedPtr<FJsonValue>> Samples;
            for (const auto& Sample : Space->GetBlendSamples())
            {
                if (!Sample.Animation || Sample.bMirror || Sample.bUseSingleFrameForBlending) return 3;
                const auto JsonSample = DescribeSequence(Sample.Animation);
                JsonSample->SetNumberField(TEXT("index"), Samples.Num());
                JsonSample->SetNumberField(TEXT("sampleRate"), Sample.RateScale);
                JsonSample->SetNumberField(TEXT("x"), Sample.SampleValue.X);
                JsonSample->SetNumberField(TEXT("y"), Sample.SampleValue.Y);
                Samples.Add(MakeShared<FJsonValueObject>(JsonSample));
            }
            Json->SetArrayField(TEXT("samples"), Samples);
        }
        else Json->SetObjectField(TEXT("sequence"), DescribeSequence(CastChecked<UAnimSequence>(Assets[Index])));
        AssetJson.Add(MakeShared<FJsonValueObject>(Json));
    }
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const int32 Hz : {30, 60, 120})
    for (int32 Scenario = 0; Scenario < 7; ++Scenario)
    {
        auto Component = NewObject<USkeletalMeshComponent>();
        auto Instance = NewObject<UAnimInstance>(Component);
        FProxy Proxy(Instance); Proxy.Initialize(Instance);
        UE::Anim::FAnimSync Sync;
        float Times[8] = {.2f, .2f, .93f, .2f, .33f, .2f, 0, 0};
        FMarkerTickRecord Markers[8]; FDeltaTimeRecord Deltas[8];
        FBlendFilter Filters[8]; TArray<FBlendSampleData> Caches[8];
        const int32 Count = Scenario == 0 || Scenario == 6 ? 1 : Scenario == 5 ? 8 : 6;
        for (int32 Slot = 0; Slot < Count; ++Slot)
            if (const auto Space = Cast<UBlendSpace>(Assets[Scenario == 6 ? 8 : Slot])) Space->InitializeFilter(&Filters[Slot]);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < 64; ++Frame)
        {
            const bool Empty = Scenario == 4 && (Frame == 20 || Frame == 21);
            const float Delta = Scenario == 3 && Frame == 20 ? 0.f : Scenario == 3 && Frame == 40 ? .7f : 1.f / Hz;
            Sync.Reset(); Proxy.PreUpdate(Instance, Delta);
            // The instance is only an output sink. Proxy.PreUpdate resets the actual filtering queue.
            Instance->NotifyQueue.AnimNotifies.Reset();
            TArray<TSharedPtr<FJsonValue>> Inputs;
            for (int32 Insertion = 0; Insertion < Count && !Empty; ++Insertion)
            {
                const int32 Slot = Scenario == 2 ? (Insertion + Frame) % Count : Insertion;
                const int32 AssetIndex = Scenario == 6 ? 8 : Slot;
                auto Asset = Assets[AssetIndex];
                const bool Reset = Scenario == 4 && Frame == 22 || Scenario == 5 && (Frame == 20 || Frame == 40);
                if (Reset)
                {
                    Times[Slot] = .1f; Markers[Slot].Reset(); Deltas[Slot] = FDeltaTimeRecord(); Caches[Slot].Reset();
                    if (const auto Space = Cast<UBlendSpace>(Asset)) Space->InitializeFilter(&Filters[Slot]);
                }
                const float Rate = Scenario == 3 ? (Frame < 16 ? 0.f : Frame < 40 ? -1.25f : 2.f) : 1.25f;
                const float Weight = Scenario == 2 ? .25f : Slot == (Frame / 8) % Count ? .8f : .1f;
                const FVector Input(Scenario == 6 ? .7f * FMath::Sin(Frame * .12f) : Frame < 16 ? .2f : Frame < 48 ? 1.f : .5f,
                    Scenario == 6 ? .5f * FMath::Cos(Frame * .1f) : Frame < 24 ? 0.f : Frame < 48 ? 1.f : .5f, 0);
                const auto Row = MakeShared<FJsonObject>();
                Row->SetNumberField(TEXT("slot"), Slot); Row->SetNumberField(TEXT("asset"), AssetIndex);
                Row->SetNumberField(TEXT("time"), Times[Slot]); Row->SetNumberField(TEXT("rate"), Rate);
                Row->SetNumberField(TEXT("weight"), Weight); Row->SetNumberField(TEXT("x"), Input.X); Row->SetNumberField(TEXT("y"), Input.Y);
                Row->SetBoolField(TEXT("reset"), Reset); Row->SetObjectField(TEXT("marker"), Marker(Markers[Slot]));
                Row->SetArrayField(TEXT("cache"), Samples(Caches[Slot]));
                Inputs.Add(MakeShared<FJsonValueObject>(Row));
                FAnimTickRecord Tick;
                if (const auto Space = Cast<UBlendSpace>(Asset))
                    Tick = FAnimTickRecord(Space, Input, Caches[Slot], Filters[Slot], true, Rate, false, false, Weight, Times[Slot], Markers[Slot]);
                else Tick = FAnimTickRecord(CastChecked<UAnimSequence>(Asset), true, Rate, false, Weight, Times[Slot], Markers[Slot]);
                Tick.DeltaTimeRecord = &Deltas[Slot]; Tick.bRequestedInertialization = Reset;
                Sync.AddTickRecord(Tick, UE::Anim::FAnimSyncParams(Scenario == 6 ? NAME_None : FName(TEXT("Probe")),
                    EAnimGroupRole::CanBeLeader, Scenario == 6 ? EAnimSyncMethod::DoNotSync : EAnimSyncMethod::SyncGroup));
            }
            Sync.TickAssetPlayerInstances(Proxy, Delta);
            // Transfer queued references through the public proxy lifecycle; do not dispatch callbacks.
            Proxy.PostUpdate(Instance);
            const auto Group = Sync.GetSyncGroupMapRead().Find(TEXT("Probe"));
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("delta"), Delta); Row->SetArrayField(TEXT("input"), Inputs);
            Row->SetNumberField(TEXT("leader"), Group && !Group->ActivePlayers.IsEmpty() ? Group->ActivePlayers[Group->GroupLeaderIndex].TimeAccumulator - Times : -1);
            Row->SetNumberField(TEXT("previousRatio"), Group ? Group->PreviousAnimLengthRatio : 0);
            Row->SetNumberField(TEXT("ratio"), Group ? Group->AnimLengthRatio : 0);
            Row->SetBoolField(TEXT("markerSync"), Group && Group->bCanUseMarkerSync);
            if (Group)
            {
                Row->SetObjectField(TEXT("markerStart"), Position(Group->MarkerTickContext.GetMarkerSyncStartPosition()));
                Row->SetObjectField(TEXT("markerEnd"), Position(Group->MarkerTickContext.GetMarkerSyncEndPosition()));
            }
            TArray<TSharedPtr<FJsonValue>> Outputs;
            for (int32 Slot = 0; Slot < Count && !Empty; ++Slot)
            {
                const auto Output = MakeShared<FJsonObject>();
                Output->SetNumberField(TEXT("slot"), Slot); Output->SetNumberField(TEXT("time"), Times[Slot]);
                Output->SetNumberField(TEXT("previous"), Deltas[Slot].GetPrevious()); Output->SetNumberField(TEXT("delta"), Deltas[Slot].Delta);
                Output->SetObjectField(TEXT("marker"), Marker(Markers[Slot]));
                Output->SetArrayField(TEXT("samples"), Samples(Caches[Slot]));
                if (const auto Space = Cast<UBlendSpace>(Assets[Scenario == 6 ? 8 : Slot]))
                {
                    Output->SetNumberField(TEXT("length"), Space->GetAnimationLengthFromSampleData(Caches[Slot]));
                    Output->SetNumberField(TEXT("filteredX"), Filters[Slot].GetFilterLastOutput().X);
                    Output->SetNumberField(TEXT("filteredY"), Filters[Slot].GetFilterLastOutput().Y);
                }
                Outputs.Add(MakeShared<FJsonValueObject>(Output));
            }
            Row->SetArrayField(TEXT("output"), Outputs);
            TArray<TSharedPtr<FJsonValue>> Notifies;
            for (const auto& Reference : Instance->NotifyQueue.AnimNotifies)
            {
                const auto Notify = Reference.GetNotify(); const auto Source = Cast<UAnimSequence>(Reference.GetSourceObject());
                if (!Notify || !Source) return 4;
                const auto Json = MakeShared<FJsonObject>();
                Json->SetStringField(TEXT("source"), Source->GetPathName());
                Json->SetNumberField(TEXT("index"), Notify - Source->Notifies.GetData());
                Json->SetNumberField(TEXT("time"), Reference.GetCurrentAnimationTime());
                Notifies.Add(MakeShared<FJsonValueObject>(Json));
            }
            Row->SetArrayField(TEXT("notifies"), Notifies);
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Trace = MakeShared<FJsonObject>();
        Trace->SetNumberField(TEXT("hz"), Hz); Trace->SetNumberField(TEXT("scenario"), Scenario); Trace->SetArrayField(TEXT("frames"), Frames);
        Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE FAnimSync::TickAssetPlayerInstances with real ALS BlendSpaces and Sprint sequences; no AnimBP simulation"));
    Root->SetArrayField(TEXT("assets"), AssetJson); Root->SetArrayField(TEXT("traces"), Traces);
    FString Text;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return 5;
    UE_LOG(LogTemp, Display, TEXT("ALS_BLENDSPACE_TICK_OK assets=9 traces=%d frames=%d assets_saved=0"), Traces.Num(), Traces.Num() * 64);
    return 0;
}
