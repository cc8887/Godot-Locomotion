#include "AlsLyraMontageSamplingLibrary.h"
#include "AlsLyraPoseProbe.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimMontageEvaluationState.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/BlendProfile.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "GameFramework/Character.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace LyraMontageSamplingProbe
{
FString Fail(int32 Line) { UE_LOG(LogTemp,Error,TEXT("LYRA_MONTAGE_SAMPLING_FAILED line=%d"),Line); return {}; }
struct FInstanceAccess : UAnimInstance
{
    static FAnimInstanceProxy& Proxy(UAnimInstance* A) { return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(A); }
    static void Tick(UAnimInstance* A,float D)
    {
        A->NotifyQueue.AnimNotifies.Reset(); A->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
        (A->*&FInstanceAccess::Montage_UpdateWeight)(D); (A->*&FInstanceAccess::Montage_Advance)(D);
        (A->*&FInstanceAccess::UpdateMontageEvaluationData)();
    }
};
struct FProxyAccess : FAnimInstanceProxy
{
    static const TArray<FMontageEvaluationState>& Frozen(FAnimInstanceProxy& P)
    {
        const auto Read=static_cast<const TArray<FMontageEvaluationState>& (FAnimInstanceProxy::*)() const>(&FProxyAccess::GetMontageEvaluationData);
        return (P.*Read)();
    }
};
TSharedPtr<FJsonObject> Blend(const FAlphaBlend& B,float Start)
{
    auto R=MakeShared<FJsonObject>();
    R->SetNumberField(TEXT("alpha"),B.GetAlpha()); R->SetNumberField(TEXT("begin"),B.GetBeginValue());
    R->SetNumberField(TEXT("desired"),B.GetDesiredValue()); R->SetNumberField(TEXT("startAlpha"),Start);
    R->SetNumberField(TEXT("weight"),B.GetBlendedValue()); R->SetNumberField(TEXT("option"),static_cast<uint8>(B.GetBlendOption()));
    return R;
}
}

FString UAlsLyraMontageSamplingLibrary::ReadTimeTrace(UClass* MainClass,USkeletalMesh* Mesh,
    const TArray<UAnimMontage*>& Montages,const FString& RequestsJson)
{
    using namespace LyraMontageSamplingProbe;
    TSharedPtr<FJsonObject> Request;
    if(!MainClass||!Mesh||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Request)) return Fail(__LINE__);
    TArray<const UBlendProfile*> Profiles; TArray<TSharedPtr<FJsonValue>> ProfileRows,Bindings;
    const auto& BoneNames=Request->GetArrayField(TEXT("boneNames"));
    for(auto* M:Montages)
    {
        if(!M) return Fail(__LINE__);
        for(auto* B:{M->BlendProfileIn.Get(),M->BlendProfileOut.Get()}) if(B) Profiles.AddUnique(B);
    }
    for(auto* B:Profiles)
    {
        auto R=MakeShared<FJsonObject>(); R->SetStringField(TEXT("path"),B->GetPathName());
        R->SetStringField(TEXT("skeleton"),B->GetSkeleton()->GetPathName()); R->SetNumberField(TEXT("mode"),static_cast<uint8>(B->Mode));
        TArray<TSharedPtr<FJsonValue>> Entries,Factors;
        for(const auto& E:B->ProfileEntries)
        {
            auto V=MakeShared<FJsonObject>(); V->SetStringField(TEXT("bone"),E.BoneReference.BoneName.ToString());
            V->SetNumberField(TEXT("scale"),E.BlendScale); Entries.Add(MakeShared<FJsonValueObject>(V));
        }
        // Target policy is explicitly name based, independent of Manny indices.
        for(const auto& N:BoneNames)
        {
            const int32 Entry=B->GetEntryIndex(FName(*N->AsString()));
            Factors.Add(MakeShared<FJsonValueNumber>(Entry==INDEX_NONE?1.f:B->GetEntryBlendScale(Entry)));
        }
        R->SetArrayField(TEXT("entries"),Entries); R->SetArrayField(TEXT("factors"),Factors); ProfileRows.Add(MakeShared<FJsonValueObject>(R));
    }
    for(auto* M:Montages)
    {
        auto R=MakeShared<FJsonObject>(); R->SetNumberField(TEXT("in"),Profiles.IndexOfByKey(M->BlendProfileIn.Get()));
        R->SetNumberField(TEXT("out"),Profiles.IndexOfByKey(M->BlendProfileOut.Get())); Bindings.Add(MakeShared<FJsonValueObject>(R));
    }
    TArray<TSharedPtr<FJsonValue>> MathRows;
    for(const auto& V:Request->GetArrayField(TEXT("mathCases")))
    {
        auto Q=V->AsObject(); FAlphaBlend B;
        B.SetBlendOption(static_cast<EAlphaBlendOption>(static_cast<int32>(Q->GetNumberField(TEXT("option")))));
        B.SetValueRange(Q->GetNumberField(TEXT("begin")),Q->GetNumberField(TEXT("desired"))); B.Update(0);
        B.SetAlpha(Q->GetNumberField(TEXT("alpha")));
        auto R=Blend(B,Q->GetNumberField(TEXT("startAlpha")));
        R->SetNumberField(TEXT("boneWeight"),UBlendProfile::CalculateBoneWeight(Q->GetNumberField(TEXT("factor")),
            static_cast<EBlendProfileMode>(static_cast<int32>(Q->GetNumberField(TEXT("mode")))),B,
            Q->GetNumberField(TEXT("startAlpha")),B.GetBlendedValue(),false)); MathRows.Add(MakeShared<FJsonValueObject>(R));
    }
    TArray<TSharedPtr<FJsonValue>> Traces;
    for(const auto& TV:Request->GetArrayField(TEXT("traces")))
    {
        const auto Init=UWorld::InitializationValues().AllowAudioPlayback(false).CreatePhysicsScene(false).CreateNavigation(false)
            .CreateAISystem(false).ShouldSimulatePhysics(false).SetTransactional(false);
        TStrongObjectPtr<UWorld> W(UWorld::CreateWorld(EWorldType::GamePreview,false,NAME_None,nullptr,false,ERHIFeatureLevel::Num,&Init));
        if(!W.IsValid()) return Fail(__LINE__);
        struct FCleanup { UWorld* World; ~FCleanup(){World->DestroyWorld(false);} } Cleanup{W.Get()};
        FActorSpawnParameters Spawn; Spawn.ObjectFlags|=RF_Transient; auto* Owner=W->SpawnActor<ACharacter>(Spawn);
        if(!Owner) return Fail(__LINE__);
        TStrongObjectPtr<USkeletalMeshComponent> C(NewObject<USkeletalMeshComponent>(Owner,NAME_None,RF_Transient));
        C->bUseRefPoseOnInitAnim=true; C->SetDisablePostProcessBlueprint(true); C->SetCollisionEnabled(ECollisionEnabled::NoCollision);
        C->SetSkeletalMesh(Mesh); C->SetAnimInstanceClass(MainClass); C->SetupAttachment(Owner->GetRootComponent());
        Owner->AddInstanceComponent(C.Get()); C->RegisterComponent(); auto* Main=C->GetAnimInstance();
        if(!Main) return Fail(__LINE__); auto& Proxy=FInstanceAccess::Proxy(Main);
        TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FV:TV->AsObject()->GetArrayField(TEXT("frames")))
        {
            auto F=FV->AsObject(); FInstanceAccess::Tick(Main,F->GetNumberField(TEXT("delta")));
            TArray<TSharedPtr<FJsonValue>> Frozen;
            for(const auto& E:FProxyAccess::Frozen(Proxy))
            {
                auto R=Blend(E.BlendInfo,E.BlendStartAlpha); auto* B=E.ActiveBlendProfile;
                const int32 Id=Profiles.IndexOfByKey(B);
                if(B&&Id==INDEX_NONE) return Fail(__LINE__);
                R->SetNumberField(TEXT("asset"),Montages.IndexOfByKey(E.Montage.Get()));
                R->SetNumberField(TEXT("previous"),E.DeltaTimeRecord.GetPrevious()); R->SetNumberField(TEXT("delta"),E.DeltaTimeRecord.Delta);
                R->SetNumberField(TEXT("position"),E.MontagePosition); R->SetNumberField(TEXT("profile"),Id);
                if(B)
                {
                    TArray<TSharedPtr<FJsonValue>> Weights;
                    for(const auto& V:ProfileRows[Id]->AsObject()->GetArrayField(TEXT("factors")))
                        Weights.Add(MakeShared<FJsonValueNumber>(UBlendProfile::CalculateBoneWeight(V->AsNumber(),B->Mode,E.BlendInfo,E.BlendStartAlpha,E.BlendInfo.GetBlendedValue(),false)));
                    R->SetArrayField(TEXT("boneWeights"),Weights);
                }
                Frozen.Add(MakeShared<FJsonValueObject>(R));
            }
            auto R=MakeShared<FJsonObject>(); R->SetArrayField(TEXT("frozen"),Frozen); Frames.Add(MakeShared<FJsonValueObject>(R));
            // Preserve pre-Blueprint proxy freeze: commands cannot change this frame.
            for(const auto& CV:F->GetArrayField(TEXT("commands")))
            {
                auto Q=CV->AsObject(); const int32 Asset=Q->GetNumberField(TEXT("asset"));
                if(!Montages.IsValidIndex(Asset)) return Fail(__LINE__);
                bool InstanceStop=false; Q->TryGetBoolField(TEXT("instanceStop"),InstanceStop);
                if(InstanceStop)
                {
                    for(int32 I=Main->MontageInstances.Num()-1;I>=0;--I)
                    {
                        auto* Instance=Main->MontageInstances[I];
                        if(Instance&&Instance->IsValid()&&Instance->Montage==Montages[Asset])
                        { Instance->Stop(FAlphaBlend(Montages[Asset]->BlendOut,Q->GetNumberField(TEXT("blend")))); break; }
                    }
                }
                else if(Q->GetBoolField(TEXT("stop"))) Main->Montage_Stop(Q->GetNumberField(TEXT("blend")),Montages[Asset]);
                else if(Main->Montage_Play(Montages[Asset],Q->GetNumberField(TEXT("rate")),EMontagePlayReturnType::MontageLength,
                    Q->GetNumberField(TEXT("start")),Q->GetBoolField(TEXT("stopGroup")))<=0) return Fail(__LINE__);
            }
        }
        auto R=MakeShared<FJsonObject>(); R->SetNumberField(TEXT("hz"),TV->AsObject()->GetNumberField(TEXT("hz")));
        R->SetArrayField(TEXT("frames"),Frames); Traces.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>(); R->SetArrayField(TEXT("profiles"),ProfileRows); R->SetArrayField(TEXT("bindings"),Bindings);
    R->SetArrayField(TEXT("mathCases"),MathRows); R->SetArrayField(TEXT("traces"),Traces);
    FString Result; FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Result)); return Result;
}

FString UAlsLyraMontageSamplingLibrary::ReadTrackSamples(USkeleton* Skeleton,
    const TArray<UAnimMontage*>& Montages,const TArray<UAnimSequence*>& Sequences,const FString& RequestsJson)
{
    using namespace LyraMontageSamplingProbe;
    TSharedPtr<FJsonObject> Request;
    if(!Skeleton||Skeleton->GetReferenceSkeleton().GetNum()!=81||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson),Request))return Fail(__LINE__);
    for(auto* S:Sequences){if(!S||S->GetSkeleton()!=Skeleton)return Fail(__LINE__);S->WaitOnExistingCompression(true);}
    TArray<FBoneIndexType> Required;for(int32 I=0;I<81;++I)Required.Add((FBoneIndexType)I);
    FBoneContainer Bones(Required,UE::Anim::FCurveFilterSettings(),*Skeleton);
    Bones.SetUseRAWData(true);Bones.SetDisableRetargeting(false);
    const auto Mapping=Request->GetObjectField(TEXT("sequenceIndices"));TArray<TSharedPtr<FJsonValue>> Rows;
    for(const auto& V:Request->GetArrayField(TEXT("samples")))
    {
        FMemMark Mark(FMemStack::Get());auto Q=V->AsObject();const int32 Id=Q->GetNumberField(TEXT("asset"));
        const int32 TrackIndex=Q->GetNumberField(TEXT("track"));
        if(!Montages.IsValidIndex(Id)||!Montages[Id]||!Montages[Id]->SlotAnimTracks.IsValidIndex(TrackIndex))return Fail(__LINE__);
        auto* M=Montages[Id];FAnimTrack Track=M->SlotAnimTracks[TrackIndex].AnimTrack;
        if(Track.AnimSegments.Num()!=1)return Fail(__LINE__);
        auto& Segment=Track.AnimSegments[0];const UAnimSequenceBase* Original=Segment.GetAnimReference();
        const int32 Index=Mapping->GetNumberField(Original->GetPathName());if(!Sequences.IsValidIndex(Index))return Fail(__LINE__);
        Segment.SetAnimReference(Sequences[Index]);
        FCompactPose Pose;Pose.SetBoneContainer(&Bones);FBlendedCurve Curve;Curve.InitFrom(Bones);
        UE::Anim::FStackAttributeContainer Attributes;FAnimationPoseData Data(Pose,Curve,Attributes);
        FDeltaTimeRecord Delta;Delta.Set(Q->GetNumberField(TEXT("previous")),Q->GetNumberField(TEXT("delta")));
        const float Position=Q->GetNumberField(TEXT("position"));const bool Extract=Q->GetBoolField(TEXT("extract"));
        FAnimExtractContext Context(static_cast<double>(Position),Extract,Delta);
        Track.GetAnimationPose(Data,Context);
        auto R=MakeShared<FJsonObject>();R->SetObjectField(TEXT("sequence"),LyraCyclePoseProbe::PoseData(Pose,Curve,Attributes,Skeleton->GetReferenceSkeleton()));
        FBlendedCurve MontageCurve;MontageCurve.InitFrom(Bones);M->EvaluateCurveData(MontageCurve,Position);
        Curve.Combine(MontageCurve);
        R->SetObjectField(TEXT("output"),LyraCyclePoseProbe::PoseData(Pose,Curve,Attributes,Skeleton->GetReferenceSkeleton()));
        float SequenceTime=0;const float ClampedTime=FMath::Clamp(static_cast<double>(Position),0.f,Track.GetLength());
        if(Segment.GetAnimationData(ClampedTime,SequenceTime)!=Sequences[Index])return Fail(__LINE__);
        R->SetNumberField(TEXT("time"),SequenceTime);R->SetNumberField(TEXT("sequencePrevious"),static_cast<float>(static_cast<double>(SequenceTime)-Delta.Delta));
        R->SetBoolField(TEXT("extract"),Extract&&Sequences[Index]->HasRootMotion());Rows.Add(MakeShared<FJsonValueObject>(R));
    }
    auto R=MakeShared<FJsonObject>();R->SetArrayField(TEXT("rows"),Rows);FString Result;FJsonSerializer::Serialize(R,TJsonWriterFactory<>::Create(&Result));return Result;
}
