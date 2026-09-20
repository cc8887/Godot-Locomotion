#include "AlsPoseBlendCommandlet.h"

#include "Animation/BlendSpace.h"
#include "AnimationRuntime.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
TSharedRef<FJsonObject> TransformJson(const FTransform& Pose)
{
    const auto Result = MakeShared<FJsonObject>();
    const FVector Position = Pose.GetTranslation();
    const FQuat Rotation = Pose.GetRotation();
    const FVector Scale = Pose.GetScale3D();
    auto Array = [](std::initializer_list<double> Values)
    {
        TArray<TSharedPtr<FJsonValue>> Output;
        for (double Value : Values) Output.Add(MakeShared<FJsonValueNumber>(Value));
        return Output;
    };
    Result->SetArrayField(TEXT("position"), Array({Position.X, Position.Y, Position.Z}));
    Result->SetArrayField(TEXT("rotation"), Array({Rotation.X, Rotation.Y, Rotation.Z, Rotation.W}));
    Result->SetArrayField(TEXT("scale"), Array({Scale.X, Scale.Y, Scale.Z}));
    return Result;
}
}

UAlsPoseBlendCommandlet::UAlsPoseBlendCommandlet()
{
    IsClient = false;
    IsServer = false;
    IsEditor = true;
    LogToConsole = true;
}

int32 UAlsPoseBlendCommandlet::Main(const FString& Params)
{
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE FTransform operations and UBlendSpace native sample order; synthetic local poses, not AnimBP pose traces"));
    TArray<FTransform> Poses;
    TArray<TSharedPtr<FJsonValue>> Sources;
    for (int32 Index = 0; Index < 8; ++Index)
    {
        FQuat Rotation = FRotator(Index * 41 - 83, Index * 97 + 12, Index * 23 - 66).Quaternion();
        if (Index % 2) Rotation = Rotation * -1;
        FTransform Pose(Rotation, FVector(Index * .13, Index * -.21, Index * .07),
            FVector(1 + Index * .03, 1 - Index * .02, 1 + Index * .04));
        Poses.Add(Pose);
        Sources.Add(MakeShared<FJsonValueObject>(TransformJson(Pose)));
    }
    Root->SetArrayField(TEXT("poses"), Sources);
    TArray<TSharedPtr<FJsonValue>> Cases;
    for (int32 Case = 0; Case < 64; ++Case)
    {
        TArray<float> Weights;
        float Total = 0;
        for (int32 Index = 0; Index < 8; ++Index)
        {
            const float Weight = ((Case * 17 + Index * 11) % 37) / 37.f;
            Weights.Add(Weight);
            Total += Weight;
        }
        TArray<TSharedPtr<FJsonValue>> WeightJson;
        for (float& Weight : Weights)
        {
            Weight /= Total;
            WeightJson.Add(MakeShared<FJsonValueNumber>(Weight));
        }
        FTransform Mixed = Poses[0] * ScalarRegister(Weights[0]);
        for (int32 Index = 1; Index < 8; ++Index)
            Mixed.AccumulateWithShortestRotation(Poses[Index], ScalarRegister(Weights[Index]));
        Mixed.NormalizeRotation();
        const auto Row = MakeShared<FJsonObject>();
        Row->SetArrayField(TEXT("weights"), WeightJson);
        Row->SetObjectField(TEXT("weighted"), TransformJson(Mixed));
        TArray<TSharedPtr<FJsonValue>> Steps;
        FTransform Body = Mixed;
        FTransform Leg = Mixed;
        for (int32 Index = 0; Index < Case % 12 + 1; ++Index)
        {
            const int32 Target = (Case + Index * 3) % 8;
            const float Alpha = ((Case * 7 + Index * 19) % 101) / 100.f;
            const float LegAlpha = Alpha * 2 / (Alpha * 2 + (1 - Alpha) / 2);
            Body = Body * ScalarRegister(1 - Alpha);
            Body.AccumulateWithShortestRotation(Poses[Target], ScalarRegister(Alpha));
            Leg = Leg * ScalarRegister(1 - LegAlpha);
            Leg.AccumulateWithShortestRotation(Poses[Target], ScalarRegister(LegAlpha));
            const auto Step = MakeShared<FJsonObject>();
            Step->SetNumberField(TEXT("target"), Target);
            Step->SetNumberField(TEXT("alpha"), Alpha);
            Step->SetObjectField(TEXT("bodyRaw"), TransformJson(Body));
            Step->SetObjectField(TEXT("legRaw"), TransformJson(Leg));
            Steps.Add(MakeShared<FJsonValueObject>(Step));
        }
        Body.NormalizeRotation();
        Leg.NormalizeRotation();
        Row->SetArrayField(TEXT("steps"), Steps);
        Row->SetObjectField(TEXT("body"), TransformJson(Body));
        Row->SetObjectField(TEXT("leg"), TransformJson(Leg));
        Cases.Add(MakeShared<FJsonValueObject>(Row));
    }
    Root->SetArrayField(TEXT("cases"), Cases);

    TArray<TSharedPtr<FJsonValue>> Grids;
    const float Inputs[] = {0, .000005f, .00001f, .25f, .5f, .75f, .99999f, 1};
    const FString Base = TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_WalkRun_");
    for (const TCHAR* Direction : {TEXT("F"), TEXT("B"), TEXT("FL"), TEXT("BL"), TEXT("FR"), TEXT("BR")})
    {
        UBlendSpace* Space = LoadObject<UBlendSpace>(nullptr, *(Base + Direction));
        if (!Space || !Space->bInterpolateUsingGrid || Space->TargetWeightInterpolationSpeedPerSec != 0) return 2;
        TArray<FBlendSampleData> Cache;
        int32 Triangle = INDEX_NONE;
        for (float X : Inputs)
        for (float Y : Inputs)
        {
            if (!Space->UpdateBlendSamples(FVector(X, Y, 0), 1.f / 60, Cache, Triangle)) return 3;
            const auto Row = MakeShared<FJsonObject>();
            Row->SetStringField(TEXT("asset"), Space->GetName());
            Row->SetNumberField(TEXT("x"), X);
            Row->SetNumberField(TEXT("y"), Y);
            TArray<TSharedPtr<FJsonValue>> Order;
            TArray<TSharedPtr<FJsonValue>> Weights;
            for (const FBlendSampleData& Sample : Cache)
            {
                // Map native sample indices to spatial corners: PoseWalk, Walk, PoseRun, Run.
                const FVector Point = Space->GetBlendSamples()[Sample.SampleDataIndex].SampleValue;
                Order.Add(MakeShared<FJsonValueNumber>(static_cast<int32>(Point.X + Point.Y * 2)));
                Weights.Add(MakeShared<FJsonValueNumber>(Sample.TotalWeight));
            }
            Row->SetArrayField(TEXT("order"), Order);
            Row->SetArrayField(TEXT("weights"), Weights);
            Grids.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    Root->SetArrayField(TEXT("grids"), Grids);
    if (FParse::Param(*Params, TEXT("IncludeAdditive")))
    {
        TArray<TSharedPtr<FJsonValue>> AdditiveCases;
        const float Alphas[] = {0, .000005f, .00001f, .000011f, .25f, .5f, .999989f, .99999f, 1};
        for (int32 Index = 0; Index < 32; ++Index)
        {
            FTransform Reference = Poses[(Index + 3) % 8];
            Reference.SetScale3D(FVector(Index % 4 == 0 ? 0 : -.8, Index % 4 == 1 ? 1e-9 : 1.2, .7));
            const FTransform Target = Poses[Index % 8];
            FTransform Additive = Target;
            FAnimationRuntime::ConvertTransformToAdditive(Additive, Reference);
            const FTransform BasePose = Poses[(Index + 5) % 8];
            for (const float Alpha : Alphas)
            {
                FTransform Result = BasePose;
                if (FAnimWeight::IsRelevant(Alpha))
                {
                    if (FAnimWeight::IsFullWeight(Alpha)) Result.AccumulateWithAdditiveScale(Additive, ScalarRegister(Alpha));
                    else FTransform::BlendFromIdentityAndAccumulate(Result, Additive, ScalarRegister(Alpha));
                }
                Result.NormalizeRotation();
                const auto Row = MakeShared<FJsonObject>();
                Row->SetObjectField(TEXT("reference"), TransformJson(Reference));
                Row->SetObjectField(TEXT("target"), TransformJson(Target));
                Row->SetObjectField(TEXT("base"), TransformJson(BasePose));
                Row->SetObjectField(TEXT("additive"), TransformJson(Additive));
                Row->SetNumberField(TEXT("alpha"), Alpha);
                Row->SetObjectField(TEXT("output"), TransformJson(Result));
                AdditiveCases.Add(MakeShared<FJsonValueObject>(Row));
            }
        }
        Root->SetArrayField(TEXT("additiveCases"), AdditiveCases);
        TArray<TSharedPtr<FJsonValue>> DetailCases;
        for (int32 Index = 0; Index < 64; ++Index)
        {
            TArray<TSharedPtr<FJsonValue>> AbsoluteJson;
            TArray<TSharedPtr<FJsonValue>> WeightJson;
            const FTransform& Reference = Poses[(Index + 3) % 8];
            const FTransform& BasePose = Poses[(Index + 6) % 8];
            float Desired[4];
            float Total = 0;
            for (int32 Channel = 0; Channel < 4; ++Channel)
            {
                Desired[Channel] = Index == 0 ? 0 : Index == 1 ? (Channel == 0 ? 1 : .000005f) :
                    ((Index * 13 + Channel * 7) % 31) / 31.f;
                Total += Desired[Channel];
            }
            FTransform Mixed = Reference;
            bool First = true;
            for (int32 Channel = 0; Channel < 4; ++Channel)
            {
                const FTransform& Absolute = Poses[(Index + Channel) % 8];
                AbsoluteJson.Add(MakeShared<FJsonValueObject>(TransformJson(Absolute)));
                WeightJson.Add(MakeShared<FJsonValueNumber>(Desired[Channel]));
                const float Weight = Total > ZERO_ANIMWEIGHT_THRESH ? Desired[Channel] / Total : 0;
                if (Weight <= ZERO_ANIMWEIGHT_THRESH) continue;
                FTransform Additive = Absolute;
                FAnimationRuntime::ConvertTransformToAdditive(Additive, Reference);
                if (First) Mixed = Additive * ScalarRegister(Weight);
                else Mixed.AccumulateWithShortestRotation(Additive, ScalarRegister(Weight));
                First = false;
            }
            Mixed.NormalizeRotation();
            FTransform Output = BasePose;
            Output.AccumulateWithAdditiveScale(Mixed, ScalarRegister(1.f));
            Output.NormalizeRotation();
            const auto Row = MakeShared<FJsonObject>();
            Row->SetArrayField(TEXT("sources"), AbsoluteJson);
            Row->SetArrayField(TEXT("weights"), WeightJson);
            Row->SetObjectField(TEXT("reference"), TransformJson(Reference));
            Row->SetObjectField(TEXT("base"), TransformJson(BasePose));
            Row->SetObjectField(TEXT("output"), TransformJson(Output));
            DetailCases.Add(MakeShared<FJsonValueObject>(Row));
        }
        Root->SetArrayField(TEXT("detailCases"), DetailCases);
        UE_LOG(LogTemp, Display, TEXT("ALS_LOCAL_ADDITIVE_OK cases=%d detail=%d assets_saved=0"), AdditiveCases.Num(), DetailCases.Num());
    }
    FString Text;
    const auto Writer = TJsonWriterFactory<>::Create(&Text);
    if (!FJsonSerializer::Serialize(Root, Writer) || !FFileHelper::SaveStringToFile(Text, *OutputPath)) return 4;
    UE_LOG(LogTemp, Display, TEXT("ALS_POSE_BLEND_OK cases=%d grids=%d assets_saved=0"), Cases.Num(), Grids.Num());
    return 0;
}
