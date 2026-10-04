#include "AlsSourceAnimationLibrary.h"
#include "Animation/AnimSequence.h"
#include "Animation/BlendSpace.h"
#include "Animation/AnimNotifies/AnimNotify.h"
#include "Animation/AnimNotifies/AnimNotifyState.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

FString UAlsSourceAnimationLibrary::ReadSourceSyncMetadata(UAnimationAsset* Animation)
{
    if (!Animation) return {};
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), Animation->GetPathName());
    TArray<TSharedPtr<FJsonValue>> Names;
    if (const TArray<FName>* Markers = Animation->GetUniqueMarkerNames())
        for (const FName Name : *Markers) Names.Add(MakeShared<FJsonValueString>(Name.ToString()));
    Result->SetArrayField(TEXT("markerNames"), Names);
    if (const UAnimSequence* Sequence = Cast<UAnimSequence>(Animation))
    {
        Result->SetNumberField(TEXT("length"), Sequence->GetPlayLength());
        Result->SetNumberField(TEXT("rateScale"), Sequence->RateScale);
        TArray<TSharedPtr<FJsonValue>> Markers;
        for (const FAnimSyncMarker& Marker : Sequence->AuthoredSyncMarkers)
        {
            const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("index"), Markers.Num());
            Row->SetStringField(TEXT("name"), Marker.MarkerName.ToString());
            Row->SetNumberField(TEXT("time"), Marker.Time);
            Row->SetNumberField(TEXT("track"), Marker.TrackIndex);
            Markers.Add(MakeShared<FJsonValueObject>(Row));
        }
        Result->SetArrayField(TEXT("markers"), Markers);
    }
    else if (const UBlendSpace* Space = Cast<UBlendSpace>(Animation))
    {
        Result->SetBoolField(TEXT("legacyLength"), Space->bUseLegacySamplePointAnimationLengthCalculations);
        Result->SetBoolField(TEXT("matchPhases"), Space->bShouldMatchSyncPhases);
        Result->SetBoolField(TEXT("allowMarkers"), Space->bAllowMarkerBasedSync);
        Result->SetNumberField(TEXT("notifyMode"), Space->NotifyTriggerMode);
        TArray<TSharedPtr<FJsonValue>> Samples;
        for (const FBlendSample& Sample : Space->GetBlendSamples())
        {
            if (!Sample.Animation) return {};
            const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
            Row->SetStringField(TEXT("sequence"), Sample.Animation->GetPathName());
            Row->SetNumberField(TEXT("rateScale"), Sample.RateScale);
            Row->SetBoolField(TEXT("mirror"), Sample.bMirror);
            Row->SetBoolField(TEXT("singleFrame"), Sample.bUseSingleFrameForBlending);
            Samples.Add(MakeShared<FJsonValueObject>(Row));
        }
        Result->SetArrayField(TEXT("samples"), Samples);
    }
    else return {};
    FString Json;
    return FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)) ? Json : FString();
}

FString UAlsSourceAnimationLibrary::ReadSourceNotifyMetadata(UAnimSequenceBase* Animation)
{
    if (!Animation) return {};
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), Animation->GetPathName());
    Result->SetNumberField(TEXT("length"), Animation->GetPlayLength());
    TArray<TSharedPtr<FJsonValue>> Events;
    for (const FAnimNotifyEvent& Event : Animation->Notifies)
    {
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("index"), Events.Num());
        Row->SetStringField(TEXT("name"), Event.NotifyName.ToString());
        Row->SetStringField(TEXT("notifyClass"), Event.Notify ? Event.Notify->GetClass()->GetPathName() : FString());
        Row->SetStringField(TEXT("notifyStateClass"), Event.NotifyStateClass ? Event.NotifyStateClass->GetClass()->GetPathName() : FString());
        Row->SetNumberField(TEXT("time"), Event.GetTime());
        Row->SetNumberField(TEXT("duration"), Event.GetDuration());
        Row->SetNumberField(TEXT("triggerTime"), Event.GetTriggerTime());
        Row->SetNumberField(TEXT("endTriggerTime"), Event.GetEndTriggerTime());
        Row->SetNumberField(TEXT("triggerTimeOffset"), Event.TriggerTimeOffset);
        Row->SetNumberField(TEXT("endTriggerTimeOffset"), Event.EndTriggerTimeOffset);
        Row->SetNumberField(TEXT("track"), Event.TrackIndex);
        Row->SetNumberField(TEXT("triggerWeightThreshold"), Event.TriggerWeightThreshold);
        Row->SetNumberField(TEXT("triggerChance"), Event.NotifyTriggerChance);
        Row->SetNumberField(TEXT("filterType"), Event.NotifyFilterType.GetValue());
        Row->SetNumberField(TEXT("filterLod"), Event.NotifyFilterLOD);
        Row->SetBoolField(TEXT("canBeFilteredViaRequest"), Event.bCanBeFilteredViaRequest);
        Row->SetBoolField(TEXT("triggerOnDedicatedServer"), Event.bTriggerOnDedicatedServer);
        Row->SetBoolField(TEXT("triggerOnFollower"), Event.bTriggerOnFollower);
        Row->SetBoolField(TEXT("branchingPoint"), Event.IsBranchingPoint());
        Events.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("events"), Events);
    FString Json;
    return FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)) ? Json : FString();
}
