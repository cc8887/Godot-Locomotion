#include "AlsAnimationGraphLibrary.h"
#include "Settings/AlsAnimationInstanceSettings.h"
#include "Curves/CurveFloat.h"
#include "Dom/JsonObject.h"
#include "JsonObjectConverter.h"
#include "Serialization/JsonSerializer.h"

FString UAlsAnimationGraphLibrary::ReadMovementSettings(UAlsAnimationInstanceSettings* Settings)
{
    if (!Settings) return TEXT("");
    const auto ExportObject = FJsonObjectConverter::CustomExportCallback::CreateLambda(
        [](FProperty* Property, const void* Value) -> TSharedPtr<FJsonValue>
        {
            if (const auto* ObjectProperty = CastField<FObjectPropertyBase>(Property))
            {
                const UObject* Object = ObjectProperty->GetObjectPropertyValue(Value);
                return MakeShared<FJsonValueString>(Object ? Object->GetPathName() : TEXT(""));
            }
            return nullptr;
        });
    auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), Settings->GetPathName());
    auto AddSettings = [&](const TCHAR* Name, const UStruct* Type, const void* Data)
    {
        auto Item = MakeShared<FJsonObject>();
        if (!FJsonObjectConverter::UStructToJsonObject(Type, Data, Item, 0, 0, &ExportObject)) return false;
        Root->SetObjectField(Name, Item); return true;
    };
    if (!AddSettings(TEXT("general"), FAlsGeneralAnimationSettings::StaticStruct(), &Settings->General) ||
        !AddSettings(TEXT("grounded"), FAlsGroundedSettings::StaticStruct(), &Settings->Grounded) ||
        !AddSettings(TEXT("standing"), FAlsStandingSettings::StaticStruct(), &Settings->Standing) ||
        !AddSettings(TEXT("crouching"), FAlsCrouchingSettings::StaticStruct(), &Settings->Crouching)) return TEXT("");
    TSet<UCurveFloat*> Unique;
    for (UCurveFloat* Curve : {Settings->Grounded.RotationYawOffsetForwardCurve.Get(), Settings->Grounded.RotationYawOffsetBackwardCurve.Get(),
        Settings->Grounded.RotationYawOffsetLeftCurve.Get(), Settings->Grounded.RotationYawOffsetRightCurve.Get(),
        Settings->Standing.StrideBlendAmountWalkCurve.Get(), Settings->Standing.StrideBlendAmountRunCurve.Get(),
        Settings->Crouching.StrideBlendAmountCurve.Get()})
    {
        if (!Curve) return TEXT("");
        Unique.Add(Curve);
    }
    TArray<UCurveFloat*> Ordered = Unique.Array();
    Ordered.Sort([](const UCurveFloat& A, const UCurveFloat& B) { return A.GetPathName() < B.GetPathName(); });
    TArray<TSharedPtr<FJsonValue>> Curves;
    for (UCurveFloat* Curve : Ordered)
    {
        auto Item = MakeShared<FJsonObject>(); auto Data = MakeShared<FJsonObject>();
        Item->SetStringField(TEXT("path"), Curve->GetPathName());
        if (!FJsonObjectConverter::UStructToJsonObject(FRichCurve::StaticStruct(), &Curve->FloatCurve, Data, 0, 0, &ExportObject)) return TEXT("");
        Item->SetObjectField(TEXT("curve"), Data);
        float Min, Max; Curve->GetTimeRange(Min, Max);
        if (!(Max > Min)) return TEXT("");
        TArray<TSharedPtr<FJsonValue>> Samples;
        auto Sample = [&](float Input)
        {
            auto Row = MakeShared<FJsonObject>();
            Row->SetNumberField(TEXT("input"), Input); Row->SetNumberField(TEXT("value"), Curve->GetFloatValue(Input));
            Samples.Add(MakeShared<FJsonValueObject>(Row));
        };
        // Native float inputs across the whole domain, with out-of-range samples.
        for (int32 Index = -20; Index <= 220; ++Index) Sample(Min + (Max - Min) * (Index / 200.f));
        for (const auto& Key : Curve->FloatCurve.GetConstRefOfKeys()) Sample(Key.Time);
        Item->SetArrayField(TEXT("verification"), Samples); Curves.Add(MakeShared<FJsonValueObject>(Item));
    }
    Root->SetArrayField(TEXT("curves"), Curves);
    FString Result; auto Writer = TJsonWriterFactory<>::Create(&Result);
    return FJsonSerializer::Serialize(Root, Writer) ? Result : TEXT("");
}
