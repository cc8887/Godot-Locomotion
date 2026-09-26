#include "AlsAnimationGraphLibrary.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "Curves/CurveFloat.h"
#include "Dom/JsonObject.h"
#include "JsonObjectConverter.h"
#include "Serialization/JsonSerializer.h"

FString UAlsAnimationGraphLibrary::ReadAirSettings(UAlsAnimationInstanceSettings* Settings)
{
    if (!Settings || !Settings->InAir.LeanAmountCurve) return TEXT("");
    auto Root=MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"),1);
    Root->SetStringField(TEXT("source"),Settings->GetPathName());
    Root->SetNumberField(TEXT("leanHalfLife"),Settings->General.LeanInterpolationHalfLife);
    UCurveFloat* Curve=Settings->InAir.LeanAmountCurve;
    auto Item=MakeShared<FJsonObject>();auto Data=MakeShared<FJsonObject>();
    Item->SetStringField(TEXT("path"),Curve->GetPathName());
    if (!FJsonObjectConverter::UStructToJsonObject(FRichCurve::StaticStruct(),&Curve->FloatCurve,Data)) return TEXT("");
    Item->SetObjectField(TEXT("curve"),Data);
    float Min,Max;Curve->GetTimeRange(Min,Max);if (!(Max>Min)) return TEXT("");
    TArray<TSharedPtr<FJsonValue>> Samples;
    auto Sample=[&](float Input)
    {
        auto Row=MakeShared<FJsonObject>();Row->SetNumberField(TEXT("input"),Input);
        Row->SetNumberField(TEXT("value"),Curve->GetFloatValue(Input));Samples.Add(MakeShared<FJsonValueObject>(Row));
    };
    for(int32 Index=-20;Index<=220;++Index)Sample(Min+(Max-Min)*(Index/200.f));
    for(const auto& Key:Curve->FloatCurve.GetConstRefOfKeys())Sample(Key.Time);
    Item->SetArrayField(TEXT("verification"),Samples);Root->SetObjectField(TEXT("leanCurve"),Item);
    FString Result;auto Writer=TJsonWriterFactory<>::Create(&Result);
    return FJsonSerializer::Serialize(Root,Writer)?Result:TEXT("");
}
