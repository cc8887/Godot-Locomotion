#include "AlsMontageLifecycleProbe.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimMontageEvaluationState.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
struct FAlsMontageProbeProxy final : FAnimInstanceProxy
{
    explicit FAlsMontageProbeProxy(UAnimInstance* Instance) : FAnimInstanceProxy(Instance) {}
    const TArray<FMontageEvaluationState>& Evaluations() const { return GetMontageEvaluationData(); }
};
}
FAnimInstanceProxy* UAlsMontageLifecycleProbe::CreateAnimInstanceProxy() { return new FAlsMontageProbeProxy(this); }

bool UAlsMontageLifecycleProbe::ExportAdditiveTrace(const FString& Output)
{
    const auto Mesh = LoadObject<USkeletalMesh>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"));
    if (!Mesh) return false;
    TArray<UAnimSequence*> Sequences;
    for (const TCHAR* Name : {TEXT("ALS_N_Transition_L"), TEXT("ALS_N_Transition_R"), TEXT("ALS_N_TurnIP_L90"), TEXT("ALS_N_TurnIP_L90")})
    {
        const FString Folder = Sequences.Num() < 2 ? TEXT("Transitions/") : TEXT("TurnInPlace/");
        const FString Path = TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/") + Folder + Name + TEXT(".") + Name;
        auto Sequence = LoadObject<UAnimSequence>(nullptr, *Path);
        if (!Sequence || Sequence->GetSkeleton() != Mesh->GetSkeleton() || Sequence->RateScale != 1.f) return false;
        Sequences.Add(Sequence);
    }
    const FName Grounded(TEXT("Grounded Slot")), Standing(TEXT("(N) Turn/Rotate"));
    const auto SerializePose = [](const FCompactPose& Pose, const FBlendedCurve& Curve)
    {
        auto Row = MakeShared<FJsonObject>();
        TArray<TSharedPtr<FJsonValue>> Values;
        for (const auto Bone : Pose.ForEachBoneIndex())
        {
            const auto& T = Pose[Bone]; const auto P = T.GetTranslation(), S = T.GetScale3D(); const auto Q = T.GetRotation();
            for (double V : {P.X,P.Y,P.Z,Q.X,Q.Y,Q.Z,Q.W,S.X,S.Y,S.Z}) Values.Add(MakeShared<FJsonValueNumber>(V));
        }
        Row->SetArrayField(TEXT("pose"), Values);
        auto Curves = MakeShared<FJsonObject>();
        Curve.ForEachElement([&](const auto& E) { Curves->SetNumberField(E.Name.ToString(), E.Value); });
        Row->SetObjectField(TEXT("curves"), Curves); return Row;
    };
    auto Root = MakeShared<FJsonObject>();
    Root->SetNumberField(TEXT("schemaVersion"), 1);
    Root->SetStringField(TEXT("source"), TEXT("UE native SlotEvaluatePose and FAnimMontageInstance, raw assets, no gameplay notifies"));
    TArray<TSharedPtr<FJsonValue>> Assets, Cases;
    for (int32 I = 0; I < Sequences.Num(); ++I)
    {
        auto A = MakeShared<FJsonObject>(); A->SetStringField(TEXT("path"), Sequences[I]->GetPathName());
        A->SetNumberField(TEXT("length"), Sequences[I]->GetPlayLength()); A->SetNumberField(TEXT("additive"), static_cast<int32>(Sequences[I]->AdditiveAnimType));
        A->SetNumberField(TEXT("slot"), I == 3 ? 0 : 3); A->SetNumberField(TEXT("group"), 1);
        A->SetBoolField(TEXT("syntheticSlotAssignment"), I == 2);
        Assets.Add(MakeShared<FJsonValueObject>(A));
    }
    struct FCommand { int32 Frame, Asset; float Rate = 1.75f, Start = .3f, In = .2f, Out = .2f, Trigger = 0.f; bool StopGroup = true; };
    const auto Run = [&](const FString& Name, float Delta, int32 Count, const TArray<FCommand>& Commands)
    {
        auto Component = NewObject<USkeletalMeshComponent>(); Component->SetSkeletalMesh(Mesh);
        auto Instance = NewObject<UAlsMontageLifecycleProbe>(Component); Instance->InitializeAnimation();
        Instance->RootMotionMode = ERootMotionMode::NoRootMotionExtraction;
        auto& Proxy = Instance->GetProxyOnGameThread<FAlsMontageProbeProxy>();
        auto& Bones = Proxy.GetRequiredBones();
        TArray<FBoneIndexType> Required;
        for (int32 I = 0; I < Mesh->GetRefSkeleton().GetNum(); ++I) Required.Add(static_cast<FBoneIndexType>(I));
        Bones.InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *Mesh);
        Bones.SetUseRAWData(true); Bones.SetUseSourceData(false); Bones.SetDisableRetargeting(false);
        TMap<UAnimMontage*, int32> AssetIds; TMap<int32, int32> Identities; int32 Serial = 0;
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 1; Frame <= Count; ++Frame)
        {
            const FMemMark Mark(FMemStack::Get());
            Instance->ProbeTick(Delta);
            FCompactPose Source, Result; Source.SetBoneContainer(&Bones); Result.SetBoneContainer(&Bones);
            FBlendedCurve SourceCurve, ResultCurve; SourceCurve.InitFrom(Bones); ResultCurve.InitFrom(Bones);
            UE::Anim::FStackAttributeContainer SourceAttributes, ResultAttributes;
            FAnimationPoseData SourceData(Source, SourceCurve, SourceAttributes), ResultData(Result, ResultCurve, ResultAttributes);
            Sequences[2]->GetAnimationPose(SourceData, FAnimExtractContext(.37, false));
            if (Cases.IsEmpty() && Frame == 1)
            {
                Root->SetObjectField(TEXT("base"), SerializePose(Source, SourceCurve));
                TArray<TSharedPtr<FJsonValue>> Parents, Names;
                for (const auto Bone : Source.ForEachBoneIndex())
                {
                    Parents.Add(MakeShared<FJsonValueNumber>(Bones.GetParentBoneIndex(Bone).GetInt()));
                    Names.Add(MakeShared<FJsonValueString>(Bones.GetReferenceSkeleton().GetBoneName(Bones.MakeMeshPoseIndex(Bone).GetInt()).ToString()));
                }
                Root->SetArrayField(TEXT("parents"), Parents); Root->SetArrayField(TEXT("bones"), Names);
            }
            float SlotWeight, SourceWeight, TotalWeight; Proxy.GetSlotWeight(Grounded, SlotWeight, SourceWeight, TotalWeight);
            auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("frame"), Frame);
            Row->SetNumberField(TEXT("sourceWeight"), SourceWeight); Row->SetNumberField(TEXT("slotWeight"), SlotWeight); Row->SetNumberField(TEXT("totalWeight"), TotalWeight);
            TArray<TSharedPtr<FJsonValue>> Evaluations, Requests, States;
            TArray<int32> EvaluationIds;
            for (const auto Item : Instance->MontageInstances)
                if (Item && Item->Montage && Item->GetWeight() > ZERO_ANIMWEIGHT_THRESH) EvaluationIds.Add(Identities.FindChecked(Item->GetInstanceID()));
            int32 Index = 0;
            for (const auto& E : Proxy.Evaluations())
            {
                auto Montage = E.Montage.Get(); if (!Montage || !AssetIds.Contains(Montage)) return false;
                auto Eval = MakeShared<FJsonObject>(); Eval->SetNumberField(TEXT("instance"), EvaluationIds[Index++]);
                Eval->SetNumberField(TEXT("asset"), AssetIds[Montage]); Eval->SetNumberField(TEXT("position"), E.MontagePosition); Eval->SetNumberField(TEXT("weight"), E.BlendInfo.GetBlendedValue());
                if (Montage->IsValidSlot(Grounded))
                {
                    FCompactPose Sample; Sample.SetBoneContainer(&Bones); FBlendedCurve Curve; Curve.InitFrom(Bones);
                    UE::Anim::FStackAttributeContainer Attributes; FAnimationPoseData Data(Sample, Curve, Attributes);
                    FAnimExtractContext Extract(static_cast<double>(E.MontagePosition), false, E.DeltaTimeRecord);
                    Montage->GetAnimationData(Grounded)->GetAnimationPose(Data, Extract);
                    FBlendedCurve MontageCurve; MontageCurve.InitFrom(Bones); Montage->EvaluateCurveData(MontageCurve, Extract); Curve.Combine(MontageCurve);
                    Eval->SetObjectField(TEXT("sample"), SerializePose(Sample, Curve));
                }
                Evaluations.Add(MakeShared<FJsonValueObject>(Eval));
            }
            Proxy.SlotEvaluatePose(Grounded, SourceData, SourceWeight, ResultData, SlotWeight, TotalWeight);
            Row->SetObjectField(TEXT("result"), SerializePose(Result, ResultCurve));
            for (const auto& C : Commands)
            {
                if (C.Frame != Frame) continue;
                auto Montage = C.StopGroup
                    ? Instance->PlaySlotAnimationAsDynamicMontage(Sequences[C.Asset], C.Asset == 3 ? Standing : Grounded, C.In, C.Out, C.Rate, 1, C.Trigger, C.Start)
                    : UAnimMontage::CreateSlotAnimationAsDynamicMontage(Sequences[C.Asset], Grounded, C.In, C.Out, C.Rate, 1, C.Trigger, C.Start);
                if (Montage && !C.StopGroup && Instance->Montage_Play(Montage, C.Rate, EMontagePlayReturnType::MontageLength, C.Start, false) <= 0) return false;
                if (!Montage) return false;
                AssetIds.Add(Montage, C.Asset);
                auto R = MakeShared<FJsonObject>(); R->SetNumberField(TEXT("asset"), C.Asset); R->SetNumberField(TEXT("rate"), C.Rate);
                R->SetNumberField(TEXT("start"), C.Start); R->SetNumberField(TEXT("in"), C.In); R->SetNumberField(TEXT("out"), C.Out); R->SetNumberField(TEXT("trigger"), C.Trigger);
                R->SetBoolField(TEXT("stopGroup"), C.StopGroup);
                Requests.Add(MakeShared<FJsonValueObject>(R));
            }
            for (const auto Item : Instance->MontageInstances)
            {
                if (!Item || !Item->Montage) continue;
                auto Id = Identities.Find(Item->GetInstanceID()); if (!Id) Id = &Identities.Add(Item->GetInstanceID(), ++Serial);
                auto S = MakeShared<FJsonObject>(); S->SetNumberField(TEXT("instance"), *Id); S->SetNumberField(TEXT("asset"), AssetIds.FindChecked(Item->Montage));
                S->SetNumberField(TEXT("position"), Item->GetPosition()); S->SetNumberField(TEXT("weight"), Item->GetWeight());
                S->SetBoolField(TEXT("playing"), Item->IsPlaying()); S->SetBoolField(TEXT("active"), Item->IsActive());
                States.Add(MakeShared<FJsonValueObject>(S));
            }
            Row->SetArrayField(TEXT("evaluation"), Evaluations); Row->SetArrayField(TEXT("commands"), Requests); Row->SetArrayField(TEXT("instances"), States);
            Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Instance->UninitializeAnimation();
        auto Case = MakeShared<FJsonObject>(); Case->SetStringField(TEXT("name"), Name); Case->SetNumberField(TEXT("delta"), Delta);
        Case->SetArrayField(TEXT("frames"), Frames); Cases.Add(MakeShared<FJsonValueObject>(Case)); return true;
    };
    for (int32 Hz : {30,60,120}) for (int32 Asset : {0,1})
        if (!Run(FString::Printf(TEXT("natural_%d_%d"),Hz,Asset),1.f/Hz,Hz*2,{{1,Asset}})) return false;
    if (!Run(TEXT("replace"),.01f,180,{{1,0},{20,1,1.5f},{27,0,1.75f,.3f,.025f}}) ||
        !Run(TEXT("ordinary_grounded"),.01f,180,{{1,2},{20,0},{30,1,1.5f,.3f,.025f},{35,2}}) ||
        !Run(TEXT("turn_shared_group"),.01f,180,{{1,0},{20,3},{40,1},{50,3}}) ||
        !Run(TEXT("same_frame"),.01f,180,{{1,0},{1,1},{2,0},{2,1},{2,0}}) ||
        !Run(TEXT("zero_rate"),.05f,30,{{1,0,0.f}}) ||
        !Run(TEXT("zero_blend"),.025f,90,{{1,1,1.75f,.3f,0,0}}) ||
        !Run(TEXT("reverse"),.025f,100,{{1,0,-1.5f,Sequences[0]->GetPlayLength()}}) ||
        !Run(TEXT("custom_trigger"),.025f,120,{{1,1,1.5f,.3f,.2f,.4f,.6f}}) ||
        !Run(TEXT("uninterrupted_overlap"),.01f,180,{{1,2,1.2f,.3f,.2f,.2f,0,false},{5,0,1.75f,.3f,.2f,.2f,0,false},{8,1,1.5f,.3f,.2f,.2f,0,false}})) return false;
    Root->SetArrayField(TEXT("assets"),Assets); Root->SetArrayField(TEXT("cases"),Cases);
    FString Text;
    if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_ADDITIVE_SLOT_NATIVE_OK assets=%d cases=%d assets_saved=0"),Assets.Num(),Cases.Num());
    return true;
}


void UAlsMontageLifecycleProbe::ProbeTick(float Delta)
{
    // This lifecycle probe does not dispatch gameplay notifications.
    NotifyQueue.AnimNotifies.Reset();
    NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
    Montage_UpdateWeight(Delta);
    Montage_Advance(Delta);
    UpdateMontageEvaluationData();
}

bool UAlsMontageLifecycleProbe::ExportTrace(const FString& Output, bool IncludeActions)
{
    const auto Mesh = LoadObject<USkeletalMesh>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin"));
    if (!Mesh) return false;
    TArray<UAnimSequence*> Sequences;
    TArray<TSharedPtr<FJsonValue>> Assets, Cases;
    for (const TCHAR* Stance : { TEXT("N"), TEXT("CLF") })
    for (const TCHAR* Direction : { TEXT("L90"), TEXT("R90"), TEXT("L180"), TEXT("R180") })
    {
        const FString Name = FString::Printf(TEXT("ALS_%s_TurnIP_%s"), Stance, Direction);
        const FString Path = TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/") + Name + TEXT(".") + Name;
        const auto Sequence = LoadObject<UAnimSequence>(nullptr, *Path);
        if (!Sequence || Sequence->GetSkeleton() != Mesh->GetSkeleton()) return false;
        auto A = MakeShared<FJsonObject>(); A->SetStringField(TEXT("path"), Path);
        A->SetNumberField(TEXT("length"), Sequence->GetPlayLength());
        A->SetNumberField(TEXT("slot"), Sequences.Num() / 4); Assets.Add(MakeShared<FJsonValueObject>(A)); Sequences.Add(Sequence);
    }
    UAnimMontage* Roll = nullptr;
    if (IncludeActions)
    {
        Roll = LoadObject<UAnimMontage>(nullptr, TEXT("/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions/ALS_N_LandRoll_F_Montage_Default.ALS_N_LandRoll_F_Montage_Default"));
        if (!Roll || Roll->GetSkeleton() != Mesh->GetSkeleton()) return false;
        auto A = MakeShared<FJsonObject>(); A->SetStringField(TEXT("path"), Roll->GetPathName());
        A->SetNumberField(TEXT("length"), Roll->GetPlayLength()); A->SetNumberField(TEXT("slot"), 2);
        A->SetNumberField(TEXT("in"), Roll->BlendIn.GetBlendTime()); A->SetNumberField(TEXT("out"), Roll->BlendOut.GetBlendTime());
        A->SetNumberField(TEXT("inOption"), static_cast<int32>(Roll->BlendIn.GetBlendOption()));
        A->SetNumberField(TEXT("outOption"), static_cast<int32>(Roll->BlendOut.GetBlendOption()));
        A->SetNumberField(TEXT("trigger"), Roll->BlendOutTriggerTime); A->SetBoolField(TEXT("auto"), Roll->bEnableAutoBlendOut);
        A->SetStringField(TEXT("group"), Roll->GetGroupName().ToString());
        A->SetNumberField(TEXT("rateScale"), Roll->RateScale);
        A->SetNumberField(TEXT("blendInMode"), static_cast<int32>(Roll->BlendModeIn));
        A->SetNumberField(TEXT("blendOutMode"), static_cast<int32>(Roll->BlendModeOut));
        A->SetBoolField(TEXT("blendProfiles"), Roll->BlendProfileIn != nullptr || Roll->BlendProfileOut != nullptr);
        A->SetBoolField(TEXT("customBlendCurves"), Roll->BlendIn.GetCustomCurve() != nullptr || Roll->BlendOut.GetCustomCurve() != nullptr);
        A->SetBoolField(TEXT("hasRootMotion"), Roll->HasRootMotion());
        Assets.Add(MakeShared<FJsonValueObject>(A));
    }
    struct FCommand { int32 Frame, Asset; float Rate = 1.2f, Start = 0.f, In = .2f, Out = .2f, Trigger = 0.f;
        bool StopGroup = true; int32 StopInstance = 0; };
    const auto Run = [&](const FString& Name, float Delta, int32 Count, const TArray<FCommand>& Commands)
    {
        auto Component = NewObject<USkeletalMeshComponent>(); Component->SetSkeletalMesh(Mesh);
        auto Instance = NewObject<UAlsMontageLifecycleProbe>(Component); Instance->InitializeAnimation();
        Instance->RootMotionMode = ERootMotionMode::NoRootMotionExtraction;
        TMap<UAnimMontage*, int32> MontageAssets, MontageIds;
        TMap<int32, int32> Identities; int32 Serial = 0;
        const auto Snapshot = [&]()
        {
            TArray<TSharedPtr<FJsonValue>> Result;
            for (const FAnimMontageInstance* Item : Instance->MontageInstances)
            {
                if (!Item || !Item->Montage || !MontageAssets.Contains(Item->Montage)) continue;
                int32* Id = Identities.Find(Item->GetInstanceID());
                if (!Id) Id = &Identities.Add(Item->GetInstanceID(), ++Serial);
                MontageIds.Add(Item->Montage, *Id);
                const FAlphaBlend& Blend = Item->GetBlend();
                auto Row = MakeShared<FJsonObject>();
                Row->SetNumberField(TEXT("instance"), *Id); Row->SetNumberField(TEXT("asset"), MontageAssets[Item->Montage]);
                Row->SetNumberField(TEXT("position"), Item->GetPosition()); Row->SetNumberField(TEXT("rate"), Item->GetPlayRate());
                Row->SetNumberField(TEXT("weight"), Item->GetWeight()); Row->SetNumberField(TEXT("desired"), Item->GetDesiredWeight());
                Row->SetNumberField(TEXT("blendTime"), Item->GetBlendTime()); Row->SetNumberField(TEXT("alpha"), Blend.GetAlpha());
                Row->SetNumberField(TEXT("remaining"), Blend.GetBlendTimeRemaining());
                Row->SetBoolField(TEXT("playing"), Item->IsPlaying()); Row->SetBoolField(TEXT("active"), Item->IsActive());
                Result.Add(MakeShared<FJsonValueObject>(Row));
            }
            return Result;
        };
        TArray<TSharedPtr<FJsonValue>> Frames;
        for (int32 Frame = 1; Frame <= Count; ++Frame)
        {
            Instance->ProbeTick(Delta);
            TArray<TSharedPtr<FJsonValue>> Evaluation, Requests;
            TArray<int32> EvaluationIds;
            // Evaluation has no instance ID. Pair by the native builder's exact
            // instance order and weight filter, not the montage asset pointer.
            for (const FAnimMontageInstance* Item : Instance->MontageInstances)
                if (Item && Item->Montage && Item->GetWeight() > ZERO_ANIMWEIGHT_THRESH)
                    EvaluationIds.Add(Identities.FindChecked(Item->GetInstanceID()));
            int32 EvaluationIndex = 0;
            for (const auto& Item : Instance->GetProxyOnGameThread<FAlsMontageProbeProxy>().Evaluations())
            {
                UAnimMontage* Montage = Item.Montage.Get(); if (!Montage || !MontageAssets.Contains(Montage)) return false;
                auto E = MakeShared<FJsonObject>(); E->SetNumberField(TEXT("instance"), EvaluationIds[EvaluationIndex++]);
                E->SetNumberField(TEXT("asset"), MontageAssets[Montage]); E->SetNumberField(TEXT("position"), Item.MontagePosition);
                E->SetNumberField(TEXT("weight"), Item.BlendInfo.GetBlendedValue()); Evaluation.Add(MakeShared<FJsonValueObject>(E));
            }
            for (const FCommand& Command : Commands)
            {
                if (Command.Frame != Frame) continue;
                auto Request = MakeShared<FJsonObject>();
                Request->SetNumberField(TEXT("asset"), Command.Asset); Request->SetNumberField(TEXT("rate"), Command.Rate);
                Request->SetNumberField(TEXT("start"), Command.Start); Request->SetNumberField(TEXT("in"), Command.In);
                Request->SetNumberField(TEXT("out"), Command.Out); Request->SetNumberField(TEXT("trigger"), Command.Trigger);
                if (IncludeActions)
                {
                    Request->SetBoolField(TEXT("stopGroup"), Command.StopGroup);
                    Request->SetNumberField(TEXT("stopInstance"), Command.StopInstance);
                }
                if (Command.StopInstance > 0)
                {
                    bool Stopped = false;
                    for (FAnimMontageInstance* Item : Instance->MontageInstances)
                        if (Item && Identities.FindRef(Item->GetInstanceID()) == Command.StopInstance)
                        {
                            FMontageBlendSettings StopSettings;
                            StopSettings.Blend.BlendTime = Command.In;
                            StopSettings.Blend.BlendOption = EAlphaBlendOption::HermiteCubic;
                            Item->Stop(StopSettings);
                            Stopped = true; break;
                        }
                    Request->SetBoolField(TEXT("played"), Stopped);
                    Requests.Add(MakeShared<FJsonValueObject>(Request)); continue;
                }
                const FName Slot(Command.Asset < 4 ? TEXT("(N) Turn/Rotate") : TEXT("(CLF) Turn/Rotate"));
                UAnimMontage* Montage = Command.Asset == 8
                    ? (Instance->Montage_Play(Roll, Command.Rate, EMontagePlayReturnType::MontageLength, Command.Start, Command.StopGroup) > 0 ? Roll : nullptr)
                    : Instance->PlaySlotAnimationAsDynamicMontage(Sequences[Command.Asset], Slot,
                        Command.In, Command.Out, Command.Rate, 1, Command.Trigger, Command.Start);
                Request->SetBoolField(TEXT("played"), Montage != nullptr);
                if (Montage) MontageAssets.Add(Montage, Command.Asset);
                Requests.Add(MakeShared<FJsonValueObject>(Request));
            }
            auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("frame"), Frame);
            Row->SetArrayField(TEXT("evaluation"), Evaluation); Row->SetArrayField(TEXT("commands"), Requests);
            Row->SetArrayField(TEXT("instances"), Snapshot()); Frames.Add(MakeShared<FJsonValueObject>(Row));
        }
        Instance->UninitializeAnimation();
        auto Case = MakeShared<FJsonObject>(); Case->SetStringField(TEXT("name"), Name); Case->SetNumberField(TEXT("delta"), Delta);
        Case->SetArrayField(TEXT("frames"), Frames); Cases.Add(MakeShared<FJsonValueObject>(Case)); return true;
    };
    if (!IncludeActions)
    {
    for (int32 Hz : { 30, 60, 120 })
    for (int32 Asset = 0; Asset < 8; ++Asset)
        if (!Run(FString::Printf(TEXT("natural_%d_%d"), Hz, Asset), 1.f / Hz, Hz * 4, { {1, Asset} })) return false;
    TArray<FCommand> Rapid;
    for (int32 Frame = 1; Frame <= 32; ++Frame) Rapid.Add({Frame, Frame % 8, 1.2f, .1f, Frame == 24 ? .025f : .2f, .2f, 0});
    if (!Run(TEXT("rapid_cross_stance"), .01f, 150, Rapid)) return false;
    if (!Run(TEXT("same_frame_replace"), .016f, 240, {{1,0},{1,0},{1,4},{2,1},{8,5}})) return false;
    for (int32 Asset : {0, 4})
    {
        const float End = Sequences[Asset]->GetPlayLength();
        if (!Run(FString::Printf(TEXT("reverse_%d"), Asset), .025f, 140, {{1,Asset,-1.2f,End}}) ||
            !Run(FString::Printf(TEXT("zero_rate_%d"), Asset), .05f, 30, {{1,Asset,0,.37f}}) ||
            !Run(FString::Printf(TEXT("zero_blend_%d"), Asset), .05f, 90, {{1,Asset,1.2f,0,0,0,0}}) ||
            !Run(FString::Printf(TEXT("start_end_%d"), Asset), .025f, 30, {{1,Asset,1.2f,End}}) ||
            !Run(FString::Printf(TEXT("custom_trigger_%d"), Asset), .05f, 90, {{1,Asset,1.2f,0,.3f,.4f,.6f}}) ||
            !Run(FString::Printf(TEXT("default_trigger_%d"), Asset), .05f, 90, {{1,Asset,1.2f,0,.3f,.4f,-1}}) ||
            !Run(FString::Printf(TEXT("large_delta_%d"), Asset), .8f, 8, {{1,Asset},{3,(Asset+1)%8}})) return false;
    }
    }
    else
    {
        for (int32 Hz : {30,60,120})
            if (!Run(FString::Printf(TEXT("roll_%d"), Hz), 1.f / Hz, Hz * 4, {{1,8,1.f}})) return false;
        if (!Run(TEXT("roll_and_turn"), .01f, 250, {{1,8,1.f},{1,0},{8,4},{15,1}}) ||
            !Run(TEXT("turn_then_roll"), .01f, 250, {{1,0},{4,8,1.f},{8,4},{15,8,1.f}}) ||
            !Run(TEXT("roll_same_frame"), .01f, 250, {{1,8,1.f},{1,8,1.f},{2,8,1.f},{3,8,1.f}}) ||
            !Run(TEXT("roll_no_group_stop"), .01f, 250, {{1,8,1.f},{4,8,1.f,0,.2f,.2f,0,false},{8,8,1.f,0,.2f,.2f,0,false}}) ||
            !Run(TEXT("roll_reverse"), .025f, 100, {{1,8,-1.f,Roll->GetPlayLength()}}) ||
            !Run(TEXT("roll_zero_rate"), .025f, 100, {{1,8,0.f,.37f}}) ||
            !Run(TEXT("roll_stop_old_instance"), .01f, 250, {{1,8,1.f},{4,8,1.f},{5,8,1.f,0,.025f,.2f,0,true,1}}) ||
            !Run(TEXT("roll_stop_active"), .01f, 250, {{1,8,1.f},{15,8,1.f,0,.2f,.2f,0,true,1}})) return false;
    }
    auto Root = MakeShared<FJsonObject>(); Root->SetNumberField(TEXT("schemaVersion"),IncludeActions ? 2 : 1);
    Root->SetStringField(TEXT("source"),TEXT("UE FAnimMontageInstance via UAnimInstance real weight/advance/evaluation"));
    Root->SetArrayField(TEXT("assets"),Assets); Root->SetArrayField(TEXT("cases"),Cases);
    FString Text; if (!FJsonSerializer::Serialize(Root,TJsonWriterFactory<>::Create(&Text)) ||
        !FFileHelper::SaveStringToFile(Text,*Output,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM)) return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_MONTAGE_LIFECYCLE_OK assets=%d cases=%d assets_saved=0"),Assets.Num(),Cases.Num());
    return true;
}
