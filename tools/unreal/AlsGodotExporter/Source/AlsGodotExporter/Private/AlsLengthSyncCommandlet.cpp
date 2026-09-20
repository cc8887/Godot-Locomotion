#include "AlsLengthSyncCommandlet.h"

#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimSync.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace AlsLengthSyncProbe
{
struct FProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    using FAnimInstanceProxy::PreUpdate;
};
}

UAlsLengthSyncCommandlet::UAlsLengthSyncCommandlet()
{
    IsClient = false; IsServer = false; IsEditor = true; LogToConsole = true;
}

int32 UAlsLengthSyncCommandlet::Main(const FString& Params)
{
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    TArray<UAnimSequence*> Sequences;
    const FString Base = TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/");
    for (const TCHAR* Direction : {TEXT("F"), TEXT("B"), TEXT("L"), TEXT("R")})
        Sequences.Add(LoadObject<UAnimSequence>(nullptr, *(Base + TEXT("Detail/ALS_N_LocoDetail_Accel_") + Direction)));
    Sequences.Add(LoadObject<UAnimSequence>(nullptr, *(Base + TEXT("Detail/ALS_N_Run_BasePose"))));
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE FAnimSync::TickAssetPlayerInstances; actual ALS sequences, non-looping, CanBeLeader, no valid markers"));
    TArray<TSharedPtr<FJsonValue>> Assets;
    for (int32 Index = 0; Index < Sequences.Num(); ++Index)
    {
        const auto Sequence = Sequences[Index];
        if (!Sequence || Sequence->GetPlayLength() <= 0) return 2;
        const auto Markers = Sequence->GetUniqueMarkerNames();
        if (Markers && !Markers->IsEmpty()) { UE_LOG(LogTemp, Error, TEXT("Length-only probe source has markers: %s"), *Sequence->GetPathName()); return 3; }
        const auto Asset = MakeShared<FJsonObject>();
        Asset->SetNumberField(TEXT("id"), Index);
        Asset->SetStringField(TEXT("path"), Sequence->GetPathName());
        Asset->SetNumberField(TEXT("length"), Sequence->GetPlayLength());
        Asset->SetNumberField(TEXT("rateScale"), Sequence->RateScale);
        Asset->SetNumberField(TEXT("markerCount"), Markers ? Markers->Num() : 0);
        Assets.Add(MakeShared<FJsonValueObject>(Asset));
    }
    Root->SetArrayField(TEXT("assets"), Assets);
    TArray<TSharedPtr<FJsonValue>> Traces;
    for (const int32 Hz : {30, 60, 120})
    for (int32 Scenario = 0; Scenario < 9; ++Scenario)
    {
        auto Component = NewObject<USkeletalMeshComponent>();
        auto Instance = NewObject<UAnimInstance>(Component);
        AlsLengthSyncProbe::FProxy Proxy(Instance);
        Proxy.Initialize(Instance);
        UE::Anim::FAnimSync Sync;
        const int32 Count = Scenario == 5 ? 1 : Scenario == 6 ? 8 : Scenario == 7 ? 32 : 4;
        float Times[32] = {};
        int32 Epochs[32] = {};
        FMarkerTickRecord Markers[32];
        FDeltaTimeRecord Deltas[32];
        for (int32 Slot = 0; Slot < Count; ++Slot)
        {
            Times[Slot] = Sequences[Slot % 5]->GetPlayLength() * (.1f + (Slot % 4) * .15f);
            Epochs[Slot] = 1;
        }
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < 50; ++Frame)
        {
            const bool Empty = Scenario == 3 && (Frame == 10 || Frame == 11);
            const float Delta = Scenario == 5 && (Frame == 3 || Frame == 8) ? 0.f : Scenario == 5 && Frame == 12 ? .5f : 1.f / Hz;
            Sync.Reset();
            Proxy.PreUpdate(Instance, Delta);
            TArray<TSharedPtr<FJsonValue>> Inputs;
            for (int32 Insertion = 0; Insertion < Count && !Empty; ++Insertion)
            {
                const int32 Slot = (Insertion + (Scenario == 1 || Scenario == 7 ? Frame : 0)) % Count;
                const int32 AssetIndex = Slot % 5;
                const auto Sequence = Sequences[AssetIndex];
                const bool Reset = (Scenario == 2 || Scenario == 8) && (Frame == 8 || Frame == 12 || Frame == 13) || Scenario == 3 && Frame == 12;
                if (Reset) { Times[Slot] = Sequence->GetPlayLength() * .05f; ++Epochs[Slot]; Markers[Slot].Reset(); }
                const float Weight = Scenario == 1 || Scenario == 7 && Frame % 5 == 0 ? .25f :
                    Scenario == 7 ? .1f + (Slot % 3) * .2f : Slot == (Frame / 8) % Count ? (Frame == 12 ? .9f : .7f) : .1f;
                const float Rate = Scenario == 4 ? (Slot == 0 || Frame >= 16 ? -.75f : 1.25f) : Scenario == 5 && Frame >= 15 ? 0.f : 1.25f;
                const bool Inertial = Reset || Scenario == 6 && Frame == 5;
                const bool OverridePosition = Scenario == 8;
                FAnimTickRecord Tick(Sequence, false, Rate, false, Weight, Times[Slot], Markers[Slot]);
                Tick.DeltaTimeRecord = &Deltas[Slot];
                Tick.bRequestedInertialization = Inertial;
                Sync.AddTickRecord(Tick, UE::Anim::FAnimSyncParams(TEXT("DetailProbe"), EAnimGroupRole::CanBeLeader, EAnimSyncMethod::SyncGroup, OverridePosition));
                const auto Input = MakeShared<FJsonObject>();
                Input->SetNumberField(TEXT("slot"), Slot); Input->SetNumberField(TEXT("asset"), AssetIndex); Input->SetNumberField(TEXT("epoch"), Epochs[Slot]);
                Input->SetNumberField(TEXT("time"), Times[Slot]); Input->SetNumberField(TEXT("weight"), Weight); Input->SetNumberField(TEXT("rate"), Rate);
                Input->SetBoolField(TEXT("inertial"), Inertial); Input->SetBoolField(TEXT("overridePosition"), OverridePosition);
                Inputs.Add(MakeShared<FJsonValueObject>(Input));
            }
            Sync.TickAssetPlayerInstances(Proxy, Delta);
            const auto Group = Sync.GetSyncGroupMapRead().Find(TEXT("DetailProbe"));
            if ((!Empty && (!Group || Group->ActivePlayers.Num() != Count)) ||
                (Empty && Group && !Group->ActivePlayers.IsEmpty())) return 6;
            const auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("delta"), Delta); Row->SetArrayField(TEXT("input"), Inputs);
            Row->SetNumberField(TEXT("leader"), Group && !Group->ActivePlayers.IsEmpty() ? Group->ActivePlayers[Group->GroupLeaderIndex].TimeAccumulator - Times : -1);
            Row->SetNumberField(TEXT("previousRatio"), Group ? Group->PreviousAnimLengthRatio : 0);
            Row->SetNumberField(TEXT("ratio"), Group ? Group->AnimLengthRatio : 0);
            TArray<TSharedPtr<FJsonValue>> Outputs;
            if (Group)
            {
                if (Group->bCanUseMarkerSync) return 4;
                for (const auto& Tick : Group->ActivePlayers)
                {
                    const int32 Slot = static_cast<int32>(Tick.TimeAccumulator - Times);
                    const auto Output = MakeShared<FJsonObject>();
                    Output->SetNumberField(TEXT("slot"), Slot);
                    Output->SetNumberField(TEXT("previous"), Tick.DeltaTimeRecord->GetPrevious());
                    Output->SetNumberField(TEXT("time"), *Tick.TimeAccumulator);
                    Output->SetNumberField(TEXT("advance"), Tick.DeltaTimeRecord->Delta);
                    Outputs.Add(MakeShared<FJsonValueObject>(Output));
                }
            }
            Row->SetArrayField(TEXT("output"), Outputs); Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        const auto Trace = MakeShared<FJsonObject>();
        Trace->SetNumberField(TEXT("hz"), Hz); Trace->SetNumberField(TEXT("scenario"), Scenario); Trace->SetArrayField(TEXT("frames"), Frames);
        Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    Root->SetArrayField(TEXT("traces"), Traces);
    FString Text;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return 5;
    UE_LOG(LogTemp, Display, TEXT("ALS_LENGTH_SYNC_OK assets=5 traces=%d frames=%d assets_saved=0"), Traces.Num(), Traces.Num() * 50);
    return 0;
}
