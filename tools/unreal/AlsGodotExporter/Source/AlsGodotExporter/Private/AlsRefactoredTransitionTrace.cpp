#include "AlsAnimationGraphLibrary.h"
#include "AlsAnimationInstance.h"
#include "AlsLinkedAnimationInstance.h"
#include "AlsCharacter.h"
#include "Animation/AnimBlueprint.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimMontage.h"
#include "Animation/AnimMontageEvaluationState.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/Skeleton.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "Engine/World.h"
#include "Editor.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"

namespace
{
struct FTransitionInstanceAccess : UAlsLinkedAnimationInstance
{
    static void ParentTo(UAlsLinkedAnimationInstance* I,UAlsAnimationInstance* P){I->*&FTransitionInstanceAccess::Parent=P;}
    static FAnimInstanceProxy& Proxy(UAnimInstance* I){return *GetProxyOnGameThreadStatic<FAnimInstanceProxy>(I);}
    static void Tick(UAnimInstance* I,float D)
    {
        I->NotifyQueue.AnimNotifies.Reset();I->NotifyQueue.UnfilteredMontageAnimNotifies.Reset();
        (I->*&FTransitionInstanceAccess::Montage_UpdateWeight)(D);
        (I->*&FTransitionInstanceAccess::Montage_Advance)(D);
        (I->*&FTransitionInstanceAccess::UpdateMontageEvaluationData)();
    }
};
struct FTransitionProxyAccess : FAnimInstanceProxy
{
    static const TArray<FMontageEvaluationState>& Evaluations(const FAnimInstanceProxy& P)
    {
        const TArray<FMontageEvaluationState>& (FAnimInstanceProxy::*Read)() const=&FTransitionProxyAccess::GetMontageEvaluationData;
        return (P.*Read)();
    }
};
TSharedPtr<FJsonObject> TransitionPoseJson(const FCompactPose& Pose,const FBlendedCurve& Curve)
{
    const auto Row=MakeShared<FJsonObject>();TArray<TSharedPtr<FJsonValue>> Values;
    for(const auto Bone:Pose.ForEachBoneIndex())
    {
        const auto& T=Pose[Bone];const auto P=T.GetTranslation(),S=T.GetScale3D();const auto Q=T.GetRotation();
        for(double V:{P.X,P.Y,P.Z,Q.X,Q.Y,Q.Z,Q.W,S.X,S.Y,S.Z})Values.Add(MakeShared<FJsonValueNumber>(V));
    }
    const auto Curves=MakeShared<FJsonObject>();Curve.ForEachElement([&](const auto& E){Curves->SetNumberField(E.Name.ToString(),E.Value);});
    Row->SetArrayField(TEXT("pose"),Values);Row->SetObjectField(TEXT("curves"),Curves);return Row;
}
}

bool UAlsAnimationGraphLibrary::ExportRefactoredTransitionTrace(const FString& RequestPath,const FString& OutputPath)
{
    if(FPaths::IsRelative(RequestPath)||FPaths::IsRelative(OutputPath)||RequestPath==OutputPath)return false;
    FString Text;TSharedPtr<FJsonObject> Request;
    if(!FFileHelper::LoadFileToString(Text,*RequestPath)||!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text),Request))return false;
    auto* World=GEditor?GEditor->GetEditorWorldContext().World():nullptr;
    auto* CharacterClass=LoadClass<AAlsCharacter>(nullptr,TEXT("/ALS/ALS/Character/B_Als_Character.B_Als_Character_C"));
    auto* Base=LoadObject<UAnimSequence>(nullptr,TEXT("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose"));
    if(!World||!CharacterClass||!Base)return false;
    const FName Slot(TEXT("Transition"));TArray<TSharedPtr<FJsonValue>> Traces;int32 Total=0;
    for(const auto& TraceValue:Request->GetArrayField(TEXT("traces")))
    {
        const auto Trace=TraceValue->AsObject();const auto Kind=Trace->GetStringField(TEXT("kind"));
        if(Kind!=TEXT("Bow")&&Kind!=TEXT("Rifle")&&Kind!=TEXT("PistolOneHanded")&&Kind!=TEXT("PistolTwoHanded"))return false;
        const auto Path=FString::Printf(TEXT("/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_%s.AB_Als_%s"),*Kind,*Kind);
        auto* Blueprint=LoadObject<UAnimBlueprint>(nullptr,*Path);if(!Blueprint||!Blueprint->GeneratedClass)return false;
        FActorSpawnParameters Spawn;Spawn.ObjectFlags|=RF_Transient;Spawn.SpawnCollisionHandlingOverride=ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
        auto* Character=World->SpawnActor<AAlsCharacter>(CharacterClass,FVector(0,0,100000),FRotator::ZeroRotator,Spawn);
        if(!Character)return false;ON_SCOPE_EXIT{World->DestroyActor(Character);};
        auto* Component=Character->GetMesh();auto* Mesh=Component->GetSkeletalMeshAsset();auto* Parent=Cast<UAlsAnimationInstance>(Component->GetAnimInstance());
        if(!Parent||!Mesh||Mesh->GetSkeleton()!=Base->GetSkeleton())return false;
        auto* Settings=FindFProperty<FObjectPropertyBase>(Parent->GetClass(),TEXT("Settings"));
        const auto* SettingsObject=Settings?Settings->GetObjectPropertyValue_InContainer(Parent):nullptr;
        if(!SettingsObject||SettingsObject->GetPathName()!=TEXT("/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default"))return false;
        TStrongObjectPtr<UAlsLinkedAnimationInstance> Linked(NewObject<UAlsLinkedAnimationInstance>(Component,Blueprint->GeneratedClass));
        Linked->InitializeAnimation(true);FTransitionInstanceAccess::ParentTo(Linked.Get(),Parent);
        auto& Proxy=FTransitionInstanceAccess::Proxy(Parent);auto& Bones=Proxy.GetRequiredBones();TArray<FBoneIndexType> Required;
        for(int32 I=0;I<Mesh->GetSkeleton()->GetReferenceSkeleton().GetNum();++I)Required.Add(static_cast<FBoneIndexType>(I));
        if(Required.Num()!=79)return false;
        Bones.InitializeTo(Required,UE::Anim::FCurveFilterSettings(),*Mesh->GetSkeleton());Bones.SetUseRAWData(true);Bones.SetUseSourceData(false);
        auto* Stance=FindFProperty<FStructProperty>(Parent->GetClass(),TEXT("Stance"));
        auto* Locomotion=FindFProperty<FStructProperty>(Parent->GetClass(),TEXT("LocomotionState"));
        auto* Moving=Locomotion?FindFProperty<FBoolProperty>(Locomotion->Struct,TEXT("bMoving")):nullptr;
        if(!Stance||Stance->Struct!=FGameplayTag::StaticStruct()||!Moving)return false;
        TMap<int32,int32> Identities;int32 Serial=0;TArray<TSharedPtr<FJsonValue>> Frames;
        for(const auto& FrameValue:Trace->GetArrayField(TEXT("frames")))
        {
            const FMemMark Mark(FMemStack::Get());const auto Input=FrameValue->AsObject();const float Delta=static_cast<float>(Input->GetNumberField(TEXT("delta")));
            if(!FMath::IsFinite(Delta)||Delta<0)return false;
            const auto Tag=Input->GetStringField(TEXT("stance"));
            *Stance->ContainerPtrToValuePtr<FGameplayTag>(Parent)=Tag.IsEmpty()?FGameplayTag():FGameplayTag::RequestGameplayTag(FName(*Tag),false);
            Moving->SetPropertyValue_InContainer(Locomotion->ContainerPtrToValuePtr<void>(Parent),Input->GetBoolField(TEXT("moving")));
            FTransitionInstanceAccess::Tick(Parent,Delta);
            FCompactPose Source,Result;Source.SetBoneContainer(&Bones);Result.SetBoneContainer(&Bones);
            FBlendedCurve SourceCurves,ResultCurves;SourceCurves.InitFrom(Bones);ResultCurves.InitFrom(Bones);
            UE::Anim::FStackAttributeContainer SourceAttributes,ResultAttributes;
            FAnimationPoseData SourceData(Source,SourceCurves,SourceAttributes),ResultData(Result,ResultCurves,ResultAttributes);
            Base->GetAnimationPose(SourceData,FAnimExtractContext(0.0,false));
            float SW,BW,TW;Proxy.GetSlotWeight(Slot,SW,BW,TW);Proxy.SlotEvaluatePose(Slot,SourceData,BW,ResultData,SW,TW);
            const auto Row=MakeShared<FJsonObject>();Row->SetObjectField(TEXT("input"),Input);Row->SetObjectField(TEXT("result"),TransitionPoseJson(Result,ResultCurves));
            Row->SetNumberField(TEXT("sourceWeight"),BW);Row->SetNumberField(TEXT("slotWeight"),SW);Row->SetNumberField(TEXT("totalWeight"),TW);
            TArray<TSharedPtr<FJsonValue>> Evaluations,States;
            for(const auto& E:FTransitionProxyAccess::Evaluations(Proxy))
            {
                const auto* Montage=E.Montage.Get();if(!Montage||!Montage->IsValidSlot(Slot))return false;
                const auto& Segment=Montage->SlotAnimTracks[0].AnimTrack.AnimSegments[0];
                const auto Data=MakeShared<FJsonObject>();Data->SetStringField(TEXT("source"),Segment.GetAnimReference()->GetPathName());
                Data->SetNumberField(TEXT("position"),E.MontagePosition);Data->SetNumberField(TEXT("weight"),E.BlendInfo.GetBlendedValue());Evaluations.Add(MakeShared<FJsonValueObject>(Data));
            }
            // Invoke the real generated notification functions after the native montage
            // tick/evaluation snapshot, as main-thread animation notify dispatch does.
            for(const auto& Notify:Input->GetArrayField(TEXT("notifies")))
            {
                const int32 Index=static_cast<int32>(Notify->AsNumber());if(Index<0||Index>1)return false;
                auto* Function=Linked->FindFunction(FName(Index==0?TEXT("AnimNotify_RelaxedToReady"):TEXT("AnimNotify_ReadyToRelaxed")));
                if(!Function)return false;Linked->ProcessEvent(Function,nullptr);
            }
            if(Input->GetBoolField(TEXT("stop")))Parent->StopTransitionAndTurnInPlaceAnimations(static_cast<float>(Input->GetNumberField(TEXT("stopDuration"))));
            for(const auto* I:Parent->MontageInstances)
            {
                if(!I||!I->Montage)continue;
                auto* Id=Identities.Find(I->GetInstanceID());if(!Id)Id=&Identities.Add(I->GetInstanceID(),++Serial);
                const auto Data=MakeShared<FJsonObject>();Data->SetNumberField(TEXT("instance"),*Id);
                Data->SetStringField(TEXT("source"),I->Montage->SlotAnimTracks[0].AnimTrack.AnimSegments[0].GetAnimReference()->GetPathName());
                Data->SetNumberField(TEXT("position"),I->GetPosition());Data->SetNumberField(TEXT("weight"),I->GetWeight());
                Data->SetNumberField(TEXT("desired"),I->GetDesiredWeight());Data->SetNumberField(TEXT("blendTime"),I->GetBlendTime());Data->SetNumberField(TEXT("rate"),I->GetPlayRate());
                Data->SetBoolField(TEXT("playing"),I->IsPlaying());States.Add(MakeShared<FJsonValueObject>(Data));
            }
            Row->SetArrayField(TEXT("evaluation"),Evaluations);Row->SetArrayField(TEXT("instances"),States);Frames.Add(MakeShared<FJsonValueObject>(Row));++Total;
        }
        Linked->UninitializeAnimation();const auto Out=MakeShared<FJsonObject>();Out->SetStringField(TEXT("kind"),Kind);Out->SetNumberField(TEXT("hz"),Trace->GetNumberField(TEXT("hz")));
        Out->SetArrayField(TEXT("frames"),Frames);Traces.Add(MakeShared<FJsonValueObject>(Out));
    }
    const auto Root=MakeShared<FJsonObject>();Root->SetNumberField(TEXT("schemaVersion"),1);Root->SetStringField(TEXT("requestDigest"),Request->GetStringField(TEXT("requestDigest")));Root->SetArrayField(TEXT("traces"),Traces);
    FString Json;if(!FJsonSerializer::Serialize(Root,TJsonWriterFactory<TCHAR,TCondensedJsonPrintPolicy<TCHAR>>::Create(&Json))||!FFileHelper::SaveStringToFile(Json,*OutputPath,FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))return false;
    UE_LOG(LogTemp,Display,TEXT("ALS_TRANSITION_NATIVE_OK traces=%d frames=%d assets_saved=0"),Traces.Num(),Total);return true;
}
