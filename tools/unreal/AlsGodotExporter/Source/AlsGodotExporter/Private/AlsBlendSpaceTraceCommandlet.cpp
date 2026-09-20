#include "AlsBlendSpaceTraceCommandlet.h"
#include "AlsAnimationGraphLibrary.h"

#include "Animation/BlendSpace.h"
#include "Animation/AnimSequence.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Misc/Paths.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"
#include "UObject/UnrealType.h"

namespace
{
int32 ExportGridSampling(const FString& OutputPath, bool bFalling = false, bool bAim = false)
{
    const FString Path = bAim
        ? TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look")
        : bFalling
        ? TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/InAir/Detail/ALS_N_Lean_Falling")
        : TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/Detail/ALS_N_Lean");
    UBlendSpace* Space = LoadObject<UBlendSpace>(nullptr, *Path);
    if (!Space || Space->GetBlendSamples().Num() != (bAim ? 3 : 5)) return 10;
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), bAim
        ? TEXT("UE UBlendSpace::FilterInput + UpdateBlendSamples; ALS V4 Aim")
        : bFalling
        ? TEXT("UE UBlendSpace::FilterInput + UpdateBlendSamples; ALS V4 Falling Lean")
        : TEXT("UE UBlendSpace::FilterInput + UpdateBlendSamples; ALS V4 Lean"));
    Root->SetStringField(TEXT("objectPath"), Space->GetPathName());
    Root->SetBoolField(TEXT("grid"), Space->bInterpolateUsingGrid);
    Root->SetNumberField(TEXT("sampleWeightSpeed"), Space->TargetWeightInterpolationSpeedPerSec);
    Root->SetBoolField(TEXT("sampleWeightEase"), Space->bTargetWeightInterpolationEaseInOut);
    Root->SetBoolField(TEXT("meshSpaceSamples"), Space->bContainsRotationOffsetMeshSpaceSamples);
    Root->SetBoolField(TEXT("allowMeshSpace"), Space->bAllowMeshSpaceBlending);
    Root->SetNumberField(TEXT("notifyMode"), Space->NotifyTriggerMode);
    if (bAim)
    {
        const FBoolProperty* Scale = FindFProperty<FBoolProperty>(Space->GetClass(), TEXT("bScaleAnimation"));
        if (!Scale || Space->GetClass()->GetName() != TEXT("BlendSpace1D")) return 16;
        Root->SetBoolField(TEXT("scaleAnimation"), Scale->GetPropertyValue_InContainer(Space));
    }
    for (const TCHAR* Name : {TEXT("PerBoneBlendMode"), TEXT("ManualPerBoneOverrides"), TEXT("PerBoneBlendProfile"), TEXT("AxisToScaleAnimation")})
    {
        const FProperty* Property = Space->GetClass()->FindPropertyByName(Name);
        if (!Property) return 11;
        FString Native;
        Property->ExportTextItem_Direct(Native, Property->ContainerPtrToValuePtr<void>(Space), nullptr, Space, PPF_None);
        Root->SetStringField(Name, Native);
    }
    TArray<TSharedPtr<FJsonValue>> Axes;
    for (int32 Axis = 0; Axis < (bAim ? 1 : 2); ++Axis)
    {
        const auto& Parameter = Space->GetBlendParameter(Axis);
        const auto& Filter = Space->InterpolationParam[Axis];
        const auto Item = MakeShared<FJsonObject>();
        Item->SetStringField(TEXT("name"), Parameter.DisplayName);
        Item->SetNumberField(TEXT("min"), Parameter.Min); Item->SetNumberField(TEXT("max"), Parameter.Max);
        Item->SetNumberField(TEXT("divisions"), Parameter.GridNum); Item->SetBoolField(TEXT("wrap"), Parameter.bWrapInput);
        Item->SetNumberField(TEXT("mode"), Filter.InterpolationType); Item->SetNumberField(TEXT("seconds"), Filter.InterpolationTime);
        Item->SetNumberField(TEXT("damping"), Filter.DampingRatio); Item->SetNumberField(TEXT("maxSpeed"), Filter.MaxSpeed);
        Axes.Add(MakeShared<FJsonValueObject>(Item));
    }
    Root->SetArrayField(TEXT("axes"), Axes);
    TArray<TSharedPtr<FJsonValue>> Samples;
    for (const auto& Sample : Space->GetBlendSamples())
    {
        if (!Sample.Animation || Sample.bMirror || Sample.bUseSingleFrameForBlending) return 12;
        const auto Item = MakeShared<FJsonObject>();
        Item->SetNumberField(TEXT("index"), Samples.Num()); Item->SetStringField(TEXT("path"), Sample.Animation->GetPathName());
        Item->SetNumberField(TEXT("x"), Sample.SampleValue.X); Item->SetNumberField(TEXT("y"), Sample.SampleValue.Y);
        Item->SetNumberField(TEXT("rate"), Sample.RateScale); Item->SetNumberField(TEXT("assetRate"), Sample.Animation->RateScale);
        Item->SetNumberField(TEXT("length"), Sample.Animation->GetPlayLength());
        Item->SetNumberField(TEXT("additiveType"), Sample.Animation->AdditiveAnimType);
        Item->SetNumberField(TEXT("baseType"), Sample.Animation->RefPoseType);
        Item->SetNumberField(TEXT("baseFrame"), Sample.Animation->RefFrameIndex);
        Item->SetStringField(TEXT("basePath"), GetPathNameSafe(Sample.Animation->RefPoseSeq));
        Samples.Add(MakeShared<FJsonValueObject>(Item));
    }
    Root->SetArrayField(TEXT("samples"), Samples);
    TArray<TSharedPtr<FJsonValue>> Grid;
    for (const auto& Element : Space->GetGridSamples())
    {
        const auto Item = MakeShared<FJsonObject>();
        const FVector Position = Space->GetGridPosition(Grid.Num());
        Item->SetNumberField(TEXT("x"), Position.X); Item->SetNumberField(TEXT("y"), Position.Y);
        TArray<TSharedPtr<FJsonValue>> Indices, Weights;
        for (int32 Vertex = 0; Vertex < FEditorElement::MAX_VERTICES; ++Vertex)
        {
            Indices.Add(MakeShared<FJsonValueNumber>(Element.Indices[Vertex]));
            Weights.Add(MakeShared<FJsonValueNumber>(Element.Weights[Vertex]));
        }
        Item->SetArrayField(TEXT("indices"), Indices); Item->SetArrayField(TEXT("weights"), Weights);
        Grid.Add(MakeShared<FJsonValueObject>(Item));
    }
    Root->SetArrayField(TEXT("gridSamples"), Grid);
    auto SampleRow = [Space](const FVector& Input, const FVector& Filtered, float Delta,
        TArray<FBlendSampleData>& Cache, int32& Triangle) -> TSharedPtr<FJsonValue>
    {
        if (!Space->UpdateBlendSamples(Filtered, Delta, Cache, Triangle)) return nullptr;
        const auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("delta"), Delta); Row->SetNumberField(TEXT("x"), Input.X); Row->SetNumberField(TEXT("y"), Input.Y);
        Row->SetNumberField(TEXT("filteredX"), Filtered.X); Row->SetNumberField(TEXT("filteredY"), Filtered.Y);
        TArray<TSharedPtr<FJsonValue>> Weights, Order;
        for (int32 Sample = 0; Sample < Space->GetBlendSamples().Num(); ++Sample)
        {
            float Weight = 0.f;
            for (const auto& Data : Cache) if (Data.SampleDataIndex == Sample) Weight = Data.TotalWeight;
            Weights.Add(MakeShared<FJsonValueNumber>(Weight));
        }
        for (const auto& Data : Cache) Order.Add(MakeShared<FJsonValueNumber>(Data.SampleDataIndex));
        Row->SetArrayField(TEXT("weights"), Weights); Row->SetArrayField(TEXT("order"), Order);
        return MakeShared<FJsonValueObject>(Row);
    };
    TArray<TSharedPtr<FJsonValue>> StaticRows;
    for (int32 X = bAim ? -120 : -10; X <= (bAim ? 120 : 10); ++X)
    for (int32 Y = bAim ? 0 : -10; Y <= (bAim ? 0 : 10); ++Y)
    {
        const FVector Input(bAim ? float(X) : X * .125f, Y * .125f, 0);
        TArray<FBlendSampleData> Cache; int32 Triangle = INDEX_NONE;
        const auto Row = SampleRow(Input, Input, 0, Cache, Triangle);
        if (!Row) return 13;
        StaticRows.Add(Row);
    }
    if (bAim)
    {
        for (const float Pitch : {-90.00001f, -89.99999f, -45.00001f, -44.99999f, -.00001f,
            -.000001f, .000001f, .00001f, 44.99999f, 45.00001f, 89.99999f, 90.00001f})
        {
            TArray<FBlendSampleData> Cache; int32 Triangle = INDEX_NONE;
            const FVector Input(Pitch, 0, 0);
            const auto Row = SampleRow(Input, Input, 0, Cache, Triangle);
            if (!Row) return 17;
            StaticRows.Add(Row);
        }
    }
    Root->SetArrayField(TEXT("staticSamples"), StaticRows);
    TArray<TSharedPtr<FJsonValue>> Runs;
    for (int32 Hz : {30, 60, 120})
    {
        FBlendFilter Filter; Space->InitializeFilter(&Filter);
        TArray<FBlendSampleData> Cache; int32 Triangle = INDEX_NONE;
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 0; Frame < Hz * 4; ++Frame)
        {
            const float Delta = 1.f / Hz;
            const float Time = static_cast<float>(Frame) / Hz;
            const FVector Input = bAim ? FVector(Time < 1 ? -110.f : Time < 2 ? 110.f : Time < 3 ? -45.f : 0.f, 0, 0)
                : FVector(Time < 1 ? -.8f : Time < 2 ? 1.2f : Time < 3 ? -.25f : .7f,
                Time < .5f ? .9f : Time < 1.5f ? -1.2f : Time < 2.5f ? .2f : -.6f, 0);
            const FVector Filtered = Space->FilterInput(&Filter, Input, Delta);
            const auto Row = SampleRow(Input, Filtered, Delta, Cache, Triangle);
            if (!Row) return 14;
            Frames.Add(Row);
        }
        const auto Run = MakeShared<FJsonObject>(); Run->SetNumberField(TEXT("hz"), Hz); Run->SetArrayField(TEXT("frames"), Frames);
        Runs.Add(MakeShared<FJsonValueObject>(Run));
    }
    Root->SetArrayField(TEXT("runs"), Runs);
    FString Text; const auto Writer = TJsonWriterFactory<>::Create(&Text);
    if (!FJsonSerializer::Serialize(Root, Writer) || !FFileHelper::SaveStringToFile(Text, *OutputPath)) return 15;
    UE_LOG(LogTemp, Display, TEXT("%s samples=%d grid=%d static=%d frames=840 assets_saved=0"),
        bAim ? TEXT("ALS_AIM_SAMPLING_NATIVE_OK") : TEXT("ALS_LEAN_NATIVE_OK"), Space->GetBlendSamples().Num(), Grid.Num(), StaticRows.Num());
    return 0;
}
}

UAlsBlendSpaceTraceCommandlet::UAlsBlendSpaceTraceCommandlet()
{
    IsClient = false;
    IsServer = false;
    IsEditor = true;
    LogToConsole = true;
}

bool UAlsAnimationGraphLibrary::ExportAimSampling(const FString& OutputPath)
{
    return !FPaths::IsRelative(OutputPath) && ExportGridSampling(OutputPath, false, true) == 0;
}

int32 UAlsBlendSpaceTraceCommandlet::Main(const FString& Params)
{
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    if (FParse::Param(*Params, TEXT("Aim"))) return ExportGridSampling(OutputPath, false, true);
    if (FParse::Param(*Params, TEXT("FallingLean"))) return ExportGridSampling(OutputPath, true);
    if (FParse::Param(*Params, TEXT("Lean"))) return ExportGridSampling(OutputPath);
    const FString Base = TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/");
    TArray<TSharedPtr<FJsonValue>> Assets;
    for (const TCHAR* Direction : {TEXT("F"), TEXT("B"), TEXT("FL"), TEXT("BL"), TEXT("FR"), TEXT("BR")})
    {
        const FString Name = FString(TEXT("ALS_N_WalkRun_")) + Direction;
        UBlendSpace* Space = LoadObject<UBlendSpace>(nullptr, *(Base + Name));
        if (!Space || !Space->bInterpolateUsingGrid || Space->TargetWeightInterpolationSpeedPerSec != 0.f) return 2;
        const TSharedRef<FJsonObject> Asset = MakeShared<FJsonObject>();
        Asset->SetStringField(TEXT("name"), Name);
        Asset->SetStringField(TEXT("objectPath"), Space->GetPathName());
        Asset->SetBoolField(TEXT("grid"), Space->bInterpolateUsingGrid);
        Asset->SetNumberField(TEXT("sampleWeightSpeed"), Space->TargetWeightInterpolationSpeedPerSec);
        TArray<TSharedPtr<FJsonValue>> Axes;
        for (int32 Axis = 0; Axis < 2; ++Axis)
        {
            const auto& Filter = Space->InterpolationParam[Axis];
            const auto& Parameter = Space->GetBlendParameter(Axis);
            if (Filter.InterpolationType != BSIT_Cubic || Parameter.Min != 0.f || Parameter.Max != 1.f || Parameter.GridNum != 1) return 3;
            const TSharedRef<FJsonObject> Value = MakeShared<FJsonObject>();
            Value->SetStringField(TEXT("mode"), TEXT("Cubic"));
            Value->SetNumberField(TEXT("seconds"), Filter.InterpolationTime);
            Axes.Add(MakeShared<FJsonValueObject>(Value));
        }
        Asset->SetArrayField(TEXT("axes"), Axes);
        TArray<TSharedPtr<FJsonValue>> SampleNames;
        for (const FBlendSample& Sample : Space->GetBlendSamples())
            SampleNames.Add(MakeShared<FJsonValueString>(Sample.Animation->GetName()));
        Asset->SetArrayField(TEXT("sampleNames"), SampleNames);
        TArray<TSharedPtr<FJsonValue>> Runs;
        for (int32 Hz : {30, 60, 120})
        {
            FBlendFilter Filter;
            Space->InitializeFilter(&Filter);
            TArray<FBlendSampleData> Cache;
            int32 Triangle = INDEX_NONE;
            TArray<TSharedPtr<FJsonValue>> Frames;
            for (int32 Frame = 0; Frame < Hz * 4; ++Frame)
            {
                const float Delta = 1.f / Hz;
                const float Time = static_cast<float>(Frame) / Hz;
                const FVector Input(Time < 1 ? 0.2f : Time < 2 ? 1.f : 0.45f,
                    Time < 1.5f || Time >= 3 ? 0.f : 1.f, 0.f);
                const FVector Filtered = Space->FilterInput(&Filter, Input, Delta);
                if (!Space->UpdateBlendSamples(Filtered, Delta, Cache, Triangle)) return 4;
                const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
                Row->SetNumberField(TEXT("delta"), Delta);
                Row->SetNumberField(TEXT("stride"), Input.X);
                Row->SetNumberField(TEXT("gait"), Input.Y);
                Row->SetNumberField(TEXT("filteredStride"), Filtered.X);
                Row->SetNumberField(TEXT("filteredGait"), Filtered.Y);
                TArray<TSharedPtr<FJsonValue>> Weights;
                for (int32 Sample = 0; Sample < Space->GetBlendSamples().Num(); ++Sample)
                {
                    float Weight = 0.f;
                    for (const FBlendSampleData& Data : Cache)
                        if (Data.SampleDataIndex == Sample) Weight = Data.TotalWeight;
                    Weights.Add(MakeShared<FJsonValueNumber>(Weight));
                }
                Row->SetArrayField(TEXT("weights"), Weights);
                Frames.Add(MakeShared<FJsonValueObject>(Row));
            }
            const TSharedRef<FJsonObject> Run = MakeShared<FJsonObject>();
            Run->SetNumberField(TEXT("hz"), Hz);
            Run->SetArrayField(TEXT("frames"), Frames);
            Runs.Add(MakeShared<FJsonValueObject>(Run));
        }
        Asset->SetArrayField(TEXT("runs"), Runs);
        Assets.Add(MakeShared<FJsonValueObject>(Asset));
    }
    const TSharedRef<FJsonObject> Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE UBlendSpace::FilterInput + UpdateBlendSamples; ALS V4"));
    Root->SetArrayField(TEXT("assets"), Assets);
    FString Text;
    const auto Writer = TJsonWriterFactory<>::Create(&Text);
    if (!FJsonSerializer::Serialize(Root, Writer) || !FFileHelper::SaveStringToFile(Text, *OutputPath)) return 5;
    UE_LOG(LogTemp, Display, TEXT("ALS_BLENDSPACE_NATIVE_OK assets=6 rates=3 frames=5040 assets_saved=0"));
    return 0;
}
