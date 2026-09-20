#include "AlsPoseCacheCommandlet.h"

#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimNode_SaveCachedPose.h"
#include "Animation/AnimSequence.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/MemStack.h"
#include "Misc/Parse.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace AlsCacheLifecycleProbe
{
struct FSource : FAnimNode_Base
{
    int32 Id = 0, Initializes = 0, BoneCaches = 0, Updates = 0, Evaluations = 0;
    virtual void Initialize_AnyThread(const FAnimationInitializeContext&) override { ++Initializes; }
    virtual void CacheBones_AnyThread(const FAnimationCacheBonesContext&) override { ++BoneCaches; }
    virtual void Update_AnyThread(const FAnimationUpdateContext&) override { ++Updates; }
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    {
        ++Evaluations;
        Output.ResetToRefPose();
        for (const auto Bone : Output.Pose.ForEachBoneIndex())
            Output.Pose[Bone] = FTransform(FQuat::Identity, FVector(Id, Evaluations, Bone.GetInt()), FVector::OneVector);
        Output.Curve.Empty();
        Output.Curve.Set(TEXT("Present"), Evaluations + Id * .25f);
        if (Evaluations % 2) Output.Curve.Set(TEXT("Optional"), -.5f * Evaluations);
    }
};

struct FCache : FAnimNode_SaveCachedPose
{
    bool HasNativeUpdateCounter() const { return UpdateCounter.HasEverBeenUpdated(); }
};

struct FProxy : FAnimInstanceProxy
{
    using FAnimInstanceProxy::FAnimInstanceProxy;
    using FAnimInstanceProxy::Initialize;
    FSource Sources[2];
    FCache Caches[2];
    UAnimInstance* Instance = nullptr;
    TFunction<void()>* EvaluateBody = nullptr;
    TArray<TSharedPtr<FJsonValue>> Operations;
    uint64 InitFrame = 100, BoneFrame = 100, EvalFrame = 100;
    float Delta = 0;

    virtual bool Evaluate(FPoseContext& Output) override
    {
        check(EvaluateBody);
        (*EvaluateBody)();
        Output.ResetToRefPose();
        return true;
    }

    static void SetCounter(FGraphTraversalCounter& Counter, int32 Increments, uint64 Frame)
    {
        const TGuardValue<uint64> FrameGuard(GFrameCounter, Frame);
        Counter.Reset();
        for (int32 Index = 0; Index < Increments; ++Index) Counter.Increment();
    }
    void SetInit(int32 Increments, uint64 Frame) { SetCounter(InitializationCounter, Increments, Frame); InitFrame = Frame; }
    void SetBones(int32 Increments, uint64 Frame) { SetCounter(CachedBonesCounter, Increments, Frame); BoneFrame = Frame; }
    void SetEval(int32 Increments, uint64 Frame) { SetCounter(EvaluationCounter, Increments, Frame); EvalFrame = Frame; }
    void SetUpdate(int32 Increments, uint64 Frame) { SetCounter(UpdateCounter, Increments, Frame); }

    TSharedRef<FJsonObject> Record(const TCHAR* Operation, int32 Cache = -1,
        const FGraphTraversalCounter* Counter = nullptr, uint64 Frame = 0)
    {
        const auto Row = MakeShared<FJsonObject>();
        Row->SetStringField(TEXT("op"), Operation);
        if (Cache >= 0)
        {
            Row->SetNumberField(TEXT("cache"), Cache);
            Row->SetNumberField(TEXT("initializations"), Sources[Cache].Initializes);
            Row->SetNumberField(TEXT("boneCaches"), Sources[Cache].BoneCaches);
            Row->SetNumberField(TEXT("updates"), Sources[Cache].Updates);
            Row->SetNumberField(TEXT("evaluations"), Sources[Cache].Evaluations);
            Row->SetBoolField(TEXT("nativeUpdateCounterSet"), Caches[Cache].HasNativeUpdateCounter());
        }
        if (Counter)
        {
            Row->SetNumberField(TEXT("counter"), Counter->Get());
            Row->SetNumberField(TEXT("globalFrame"), Frame);
        }
        Operations.Add(MakeShared<FJsonValueObject>(Row));
        return Row;
    }

    void Init(int32 Cache)
    {
        Caches[Cache].Initialize_AnyThread(FAnimationInitializeContext(this));
        Record(TEXT("initialize"), Cache, &InitializationCounter, InitFrame);
    }
    void Bones(int32 Cache)
    {
        Caches[Cache].CacheBones_AnyThread(FAnimationCacheBonesContext(this));
        Record(TEXT("bones"), Cache, &CachedBonesCounter, BoneFrame);
    }
    void Tick(int32 Cache)
    {
        FAnimationUpdateSharedContext Shared;
        Caches[Cache].Update_AnyThread(FAnimationUpdateContext(this, Delta, &Shared));
        Caches[Cache].PostGraphUpdate();
        Record(TEXT("update"), Cache)->SetNumberField(TEXT("delta"), Delta);
    }
    void Read(int32 Cache, bool Mutate = false)
    {
        FPoseContext Output(this);
        Caches[Cache].Evaluate_AnyThread(Output);
        const auto Row = Record(TEXT("read"), Cache, &EvaluationCounter, EvalFrame);
        TArray<TSharedPtr<FJsonValue>> Positions;
        for (const auto Bone : Output.Pose.ForEachBoneIndex())
        {
            const auto Position = Output.Pose[Bone].GetTranslation();
            TArray<TSharedPtr<FJsonValue>> Values;
            for (const double Value : {Position.X, Position.Y, Position.Z}) Values.Add(MakeShared<FJsonValueNumber>(Value));
            Positions.Add(MakeShared<FJsonValueArray>(Values));
        }
        Row->SetArrayField(TEXT("positions"), Positions);
        const auto Curves = MakeShared<FJsonObject>();
        Output.Curve.ForEachElement([&](const auto& Curve) { Curves->SetNumberField(Curve.Name.ToString(), Curve.Value); });
        Row->SetObjectField(TEXT("curves"), Curves);
        Row->SetBoolField(TEXT("mutateOutput"), Mutate);
        if (Mutate)
        {
            Output.Pose[FCompactPoseBoneIndex(0)].SetTranslation(FVector(-99));
            Output.Curve.Empty();
            Output.Curve.Set(TEXT("Mutated"), -99);
        }
    }
    void Pass(TFunction<void()> Body)
    {
        Record(TEXT("push"));
        const TGuardValue<TFunction<void()>*> BodyGuard(EvaluateBody, &Body);
        FCompactPose Pose;
        Pose.SetBoneContainer(&GetRequiredBones());
        Pose.ResetToRefPose();
        FBlendedHeapCurve Curve;
        UE::Anim::FHeapAttributeContainer Attributes;
        FParallelEvaluationData Data{Curve, Pose, Attributes};
        // This exported engine entry owns the real FCachedPoseScope, including nested calls.
        Instance->ParallelEvaluateAnimation(false, nullptr, Data);
        Record(TEXT("pop"));
    }
};
}

FAnimInstanceProxy* UAlsPoseCacheProbeAnimInstance::CreateAnimInstanceProxy() { return new AlsCacheLifecycleProbe::FProxy(this); }
void UAlsPoseCacheProbeAnimInstance::DestroyAnimInstanceProxy(FAnimInstanceProxy* Proxy) { delete static_cast<AlsCacheLifecycleProbe::FProxy*>(Proxy); }
FAnimInstanceProxy& UAlsPoseCacheProbeAnimInstance::ProbeProxy() { return GetProxyOnGameThread<AlsCacheLifecycleProbe::FProxy>(); }

UAlsPoseCacheCommandlet::UAlsPoseCacheCommandlet()
{
    IsClient = false; IsServer = false; IsEditor = true; LogToConsole = true;
}

int32 UAlsPoseCacheCommandlet::Main(const FString& Params)
{
    using namespace AlsCacheLifecycleProbe;
    FString OutputPath;
    if (!FParse::Value(*Params, TEXT("Output="), OutputPath)) return 1;
    const auto Sequence = LoadObject<UAnimSequence>(nullptr,
        TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_F"));
    if (!Sequence || !Sequence->GetSkeleton()) return 2;
    const auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("Native SaveCachedPose lifecycle via UAnimInstance::ParallelEvaluateAnimation; synthetic four-bone/curve payload, no custom attributes"));
    TArray<TSharedPtr<FJsonValue>> Traces;
    int32 TotalOperations = 0;
    for (const int32 Hz : {30, 60, 120})
    {
        const FMemMark Mark(FMemStack::Get());
        const TGuardValue<uint64> FrameGuard(GFrameCounter, 100);
        auto Component = NewObject<USkeletalMeshComponent>();
        auto Instance = NewObject<UAlsPoseCacheProbeAnimInstance>(Component);
        auto& Proxy = static_cast<FProxy&>(Instance->ProbeProxy());
        Proxy.Initialize(Instance);
        Proxy.Instance = Instance; Proxy.Delta = 1.f / Hz;
        TArray<FBoneIndexType> Required = {0, 1, 2, 3};
        Proxy.GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Sequence->GetSkeleton());
        Proxy.SetInit(1, 100); Proxy.SetBones(1, 100); Proxy.SetEval(1, 100); Proxy.SetUpdate(1, 100);
        for (int32 Cache = 0; Cache < 2; ++Cache)
        {
            Proxy.Sources[Cache].Id = Cache;
            Proxy.Caches[Cache].Pose.SetLinkNode(&Proxy.Sources[Cache]);
            Proxy.Init(Cache); Proxy.Bones(Cache); Proxy.Tick(Cache);
        }
        Proxy.Init(0);
        Proxy.Pass([&]
        {
            Proxy.Read(0, true); Proxy.Read(0); Proxy.Read(1);
            Proxy.Pass([&] { Proxy.Read(0); Proxy.Read(0); });
            Proxy.Read(0);
            Proxy.Bones(0); Proxy.Read(0);
            Proxy.SetBones(2, 100); Proxy.Bones(0); Proxy.Read(0);
            Proxy.SetInit(2, 100); Proxy.Init(0); Proxy.Tick(0); Proxy.Read(0);
            Proxy.SetEval(2, 100); Proxy.Read(0);
            Proxy.SetEval(2, 101); Proxy.Read(0);
            Proxy.Pass([&] { Proxy.SetEval(3, 101); Proxy.Read(0); });
            Proxy.SetEval(2, 101); Proxy.Read(0);
        });
        Proxy.Pass([&] { Proxy.Read(0); });
        Proxy.SetUpdate(8, 105); Proxy.Init(0); Proxy.Tick(0);
        Proxy.SetInit(2, 105); Proxy.Init(0);
        Proxy.Pass([&]
        {
            Proxy.Read(0);
            Proxy.SetBones(2, 106); Proxy.Bones(0); Proxy.Read(0);
        });
        Proxy.SetInit(3, 105); Proxy.Init(0); Proxy.Tick(0);
        Proxy.Pass([&] { Proxy.Read(0); });
        Proxy.SetInit(32768, 200); Proxy.Init(0);
        Proxy.SetInit(32769, 201); Proxy.Init(0); Proxy.Init(0);
        Proxy.SetInit(65535, 202); Proxy.Init(0);
        Proxy.SetInit(65536, 203); Proxy.Init(0);
        const auto Trace = MakeShared<FJsonObject>();
        Trace->SetNumberField(TEXT("hz"), Hz);
        Trace->SetArrayField(TEXT("operations"), Proxy.Operations);
        TotalOperations += Proxy.Operations.Num();
        Traces.Add(MakeShared<FJsonValueObject>(Trace));
    }
    Root->SetArrayField(TEXT("traces"), Traces);
    FString Text;
    if (!FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text, *OutputPath, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return 3;
    UE_LOG(LogTemp, Display, TEXT("ALS_POSE_CACHE_LIFECYCLE_OK traces=%d operations=%d assets_saved=0"), Traces.Num(), TotalOperations);
    return 0;
}
