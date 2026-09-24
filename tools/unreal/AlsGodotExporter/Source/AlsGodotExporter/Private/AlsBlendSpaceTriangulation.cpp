#include "AlsSourceAnimationLibrary.h"
#include "Animation/BlendSpace.h"
#include "JsonObjectConverter.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"

FString UAlsSourceAnimationLibrary::ReadBlendSpaceTriangulationReference(UBlendSpace* BlendSpace, const FString& InputsJson)
{
    TSharedPtr<FJsonObject> Inputs;
    if (!BlendSpace || !FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(InputsJson), Inputs)) return {};
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), BlendSpace->GetPathName());
    Result->SetObjectField(TEXT("data"), FJsonObjectConverter::UStructToJsonObject(BlendSpace->GetBlendSpaceData()));
    TArray<TSharedPtr<FJsonValue>> Rows;
    int32 Cache = INDEX_NONE;
    for (const TSharedPtr<FJsonValue>& Value : Inputs->GetArrayField(TEXT("inputs")))
    {
        const TArray<TSharedPtr<FJsonValue>>& XY = Value->AsArray();
        if (XY.Num() != 2) return {};
        const FVector Input(XY[0]->AsNumber(), XY[1]->AsNumber(), 0);
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetArrayField(TEXT("input"), XY);
        Row->SetNumberField(TEXT("previousCache"), Cache);
        TArray<FBlendSampleData> Samples;
        if (!BlendSpace->GetSamplesFromBlendInput(Input, Samples, Cache, true)) return {};
        Row->SetNumberField(TEXT("cache"), Cache);
        TArray<TSharedPtr<FJsonValue>> Weights;
        for (const FBlendSampleData& Sample : Samples)
        {
            const TSharedRef<FJsonObject> Item = MakeShared<FJsonObject>();
            Item->SetNumberField(TEXT("sample"), Sample.SampleDataIndex);
            Item->SetNumberField(TEXT("weight"), Sample.GetClampedWeight());
            Weights.Add(MakeShared<FJsonValueObject>(Item));
        }
        Row->SetArrayField(TEXT("weights"), Weights);
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("cases"), Rows);
    FString Json;
    return FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)) ? Json : FString();
}
