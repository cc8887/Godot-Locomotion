#include "AlsLyraControlRigLibrary.h"

#include "Animation/AnimSequence.h"
#include "Animation/AnimData/IAnimationDataController.h"
#include "Animation/AnimData/IAnimationDataModel.h"
#include "Animation/AnimData/AnimDataModel.h"
#include "Features/IModularFeatures.h"
#include "Animation/Skeleton.h"
#include "Engine/SkeletalMesh.h"
#include "ReferenceSkeleton.h"
#include "Retargeter/IKRetargeter.h"
#include "Retargeter/IKRetargetProcessor.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "AnimNodes/AnimNode_RotateRootBone.h"
#include "UObject/UnrealType.h"

namespace
{
TArray<UE::Anim::DataModel::IAnimationDataModels*> SuspendedSequencerProviders;
bool RawTrackDataModelRegistered = false;
TSharedRef<FJsonObject> TransformRow(const FTransform& Value)
{
    const FVector P = Value.GetTranslation(), S = Value.GetScale3D(); const FQuat Q = Value.GetRotation();
    const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
    auto Numbers = [](std::initializer_list<double> Values)
    {
        TArray<TSharedPtr<FJsonValue>> Result;
        for (double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
        return Result;
    };
    Row->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
    Row->SetArrayField(TEXT("rotation"), Numbers({Q.X, Q.Y, Q.Z, Q.W}));
    Row->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z})); return Row;
}
}

bool UAlsLyraControlRigLibrary::PreferRawTrackDataModel(const bool Enabled)
{
    if (!IsRunningCommandlet()) return false;
    if (Enabled != RawTrackDataModelRegistered)
    {
        auto& Features = IModularFeatures::Get();
        const FName Name = UE::Anim::DataModel::IAnimationDataModels::GetModularFeatureName();
        if (Enabled)
        {
            // Feature implementations use reverse insertion order, and the
            // engine selects the last matching class. A new provider cannot
            // override an already registered Sequencer provider reliably.
            UAnimSequence* Probe = GetMutableDefault<UAnimSequence>();
            for (auto* Provider : Features.GetModularFeatureImplementations<UE::Anim::DataModel::IAnimationDataModels>(Name))
                if (UClass* Model = Provider->GetModelClass(Probe); Model &&
                    Model->GetPathName() == TEXT("/Script/AnimationData.AnimationSequencerDataModel"))
                {
                    SuspendedSequencerProviders.Add(Provider);
                    Features.UnregisterModularFeature(Name, Provider);
                }
        }
        else
        {
            for (auto* Provider : SuspendedSequencerProviders) Features.RegisterModularFeature(Name, Provider);
            SuspendedSequencerProviders.Reset();
        }
        RawTrackDataModelRegistered = Enabled;
    }
    return RawTrackDataModelRegistered == Enabled && (!Enabled ||
        UE::Anim::DataModel::IAnimationDataModels::FindClassForAnimationAsset(GetMutableDefault<UAnimSequence>()) == UAnimDataModel::StaticClass());
}

FString UAlsLyraControlRigLibrary::ReadRootYawNodeSettings(UClass* AnimationClass)
{
    if (!AnimationClass) return {};
    UObject* CDO = AnimationClass->GetDefaultObject();
    TArray<TSharedPtr<FJsonValue>> Nodes;
    for (TFieldIterator<FStructProperty> Property(AnimationClass); Property; ++Property)
        if (Property->Struct == FAnimNode_RotateRootBone::StaticStruct())
        {
            const auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_RotateRootBone>(CDO);
            const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
            Row->SetStringField(TEXT("property"), Property->GetName());
            Row->SetObjectField(TEXT("meshToComponent"), TransformRow(FTransform(Node->MeshToComponent.Quaternion())));
            Row->SetBoolField(TEXT("rotateRootMotionAttribute"), Node->bRotateRootMotionAttribute);
            Nodes.Add(MakeShared<FJsonValueObject>(Row));
        }
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("class"), AnimationClass->GetPathName()); Result->SetArrayField(TEXT("nodes"), Nodes);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraControlRigLibrary::ReadHandBasisCalibration(USkeletalMesh* SourceMesh,
    USkeletalMesh* TargetMesh, UIKRetargeter* Retargeter)
{
    if (!SourceMesh || !TargetMesh || !Retargeter) return {};
    FIKRetargetProcessor Processor;
    FRetargetProfile Profile;
    FRetargetInitParameters Parameters;
    Parameters.SourceSkeletalMesh = SourceMesh; Parameters.TargetSkeletalMesh = TargetMesh;
    Parameters.RetargeterAsset = Retargeter; Parameters.CustomProfile = &Profile;
    Processor.Initialize(Parameters);
    if (!Processor.IsInitialized()) return {};
    const FTransform SourceHand = Processor.GetRetargetPoseBoneTransform(TEXT("hand_r"),
        ERetargetSourceOrTarget::Source, ERetargetBoneSpace::Global);
    const FTransform TargetHand = Processor.GetRetargetPoseBoneTransform(TEXT("hand_r"),
        ERetargetSourceOrTarget::Target, ERetargetBoneSpace::Global);
    const FQuat Basis = (TargetHand.GetRotation().Inverse() * SourceHand.GetRotation()).GetNormalized();
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetObjectField(TEXT("sourceHand"), TransformRow(SourceHand));
    Result->SetObjectField(TEXT("targetHand"), TransformRow(TargetHand));
    Result->SetObjectField(TEXT("handBasis"), TransformRow(FTransform(Basis)));
    Result->SetStringField(TEXT("sourceMesh"), SourceMesh->GetPathName());
    Result->SetStringField(TEXT("targetMesh"), TargetMesh->GetPathName());
    Result->SetStringField(TEXT("retargeter"), Retargeter->GetPathName());
    Result->SetStringField(TEXT("rule"), TEXT("TargetHandRetargetRotation^-1 * SourceHandRetargetRotation; attached local weapon offset in centimeters"));
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

USkeleton* UAlsLyraControlRigLibrary::CreateWeaponSkeleton(USkeleton* Source, USkeleton* Target,
    const FQuat HandBasisRotation)
{
    if (!Source || !Target || !HandBasisRotation.IsNormalized()) return nullptr;
    const FReferenceSkeleton& SourceRef = Source->GetReferenceSkeleton();
    const FReferenceSkeleton& TargetRef = Target->GetReferenceSkeleton();
    const int32 Weapon = SourceRef.FindRawBoneIndex(TEXT("weapon_r"));
    const int32 Hand = TargetRef.FindRawBoneIndex(TEXT("hand_r"));
    if (Weapon == INDEX_NONE || Hand == INDEX_NONE || TargetRef.GetRawBoneNum() != 68 ||
        TargetRef.GetNum() != 79 || TargetRef.FindBoneIndex(TEXT("weapon_r")) != INDEX_NONE) return nullptr;
    const FTransform SourceWeapon = SourceRef.GetRawRefBonePose()[Weapon];
    const FTransform WeaponRest((HandBasisRotation * SourceWeapon.GetRotation()).GetNormalized(),
        HandBasisRotation.RotateVector(SourceWeapon.GetTranslation()), SourceWeapon.GetScale3D());
    USkeleton* Result = DuplicateObject<USkeleton>(Target, GetTransientPackage());
    Result->ClearFlags(RF_Public | RF_Standalone); Result->SetFlags(RF_Transient);
    {
        FReferenceSkeletonModifier Modifier(Result);
        Modifier.Add(FMeshBoneInfo(TEXT("weapon_r"), TEXT("weapon_r"), Hand), WeaponRest);
    }
    if (!Result->AddNewNamedVirtualBone(TEXT("weapon_r"), TEXT("hand_l"), TEXT("VB IK_Hand_L_weaponSpace"))) return nullptr;
    if (Result->GetReferenceSkeleton().GetRawBoneNum() != 69 || Result->GetReferenceSkeleton().GetNum() != 81) return nullptr;
    return Result;
}

UAnimSequence* UAlsLyraControlRigLibrary::CreateWeaponSequence(UAnimSequence* Source, UAnimSequence* Target,
    USkeleton* ExtendedSkeleton, const FQuat HandBasisRotation, UAnimSequence* ExtendedBase)
{
    if (!Source || !Target || !ExtendedSkeleton || !HandBasisRotation.IsNormalized() ||
        ExtendedSkeleton->GetReferenceSkeleton().GetRawBoneNum() != 69) return nullptr;
    const auto SourceModel = Source->GetDataModelInterface(); const auto TargetModel = Target->GetDataModelInterface();
    if (!SourceModel || !TargetModel ||
        TargetModel->IsValidBoneTrackName(TEXT("weapon_r")) ||
        SourceModel->GetNumberOfKeys() != TargetModel->GetNumberOfKeys() ||
        SourceModel->GetFrameRate() != TargetModel->GetFrameRate())
    {
        UE_LOG(LogTemp, Error, TEXT("Cannot extend weapon channel: source=%s sourceModel=%s targetModel=%s sourceTrack=%d targetTrack=%d"),
            *Source->GetPathName(), *GetNameSafe(SourceModel.GetObject()), *GetNameSafe(TargetModel.GetObject()),
            SourceModel ? SourceModel->IsValidBoneTrackName(TEXT("weapon_r")) : false,
            TargetModel ? TargetModel->IsValidBoneTrackName(TEXT("weapon_r")) : false);
        return nullptr;
    }
    if (Target->IsValidAdditive() && Target->RefPoseType != ABPT_LocalAnimFrame && Target->RefPoseSeq != Target &&
        (!ExtendedBase || ExtendedBase->GetSkeleton() != ExtendedSkeleton)) return nullptr;
    PRAGMA_DISABLE_DEPRECATION_WARNINGS
    const FRawAnimSequenceTrack* Track = SourceModel->IsValidBoneTrackName(TEXT("weapon_r")) ?
        &SourceModel->GetBoneTrackByName(TEXT("weapon_r")).InternalTrackData : nullptr;
    PRAGMA_ENABLE_DEPRECATION_WARNINGS
    const int32 Keys = TargetModel->GetNumberOfKeys();
    if (Track && ((Track->PosKeys.Num() != 1 && Track->PosKeys.Num() != Keys) ||
        (Track->RotKeys.Num() != 1 && Track->RotKeys.Num() != Keys) ||
        (!Track->ScaleKeys.IsEmpty() && Track->ScaleKeys.Num() != 1 && Track->ScaleKeys.Num() != Keys))) return nullptr;
    UAnimSequence* Result = DuplicateObject<UAnimSequence>(Target, GetTransientPackage());
    Result->ClearFlags(RF_Public | RF_Standalone); Result->SetFlags(RF_Transient);
    Result->SetSkeleton(ExtendedSkeleton);
    // Adding a raw bone shifts every old virtual index. SetSkeleton alone does
    // not refresh FBoneAnimationTrack::BoneTreeIndex on the duplicated model.
    Result->GetController().UpdateWithSkeleton(ExtendedSkeleton, false);
    if (Target->IsValidAdditive()) Result->RefPoseSeq = Target->RefPoseType == ABPT_LocalAnimFrame ? nullptr :
        Target->RefPoseSeq == Target ? Result : ExtendedBase;
    // BoneTree translation retargeting of existing raw bones uses unchanged
    // reference atoms. The appended channel uses Animation mode. Shift every
    // prior virtual reference by name rather than relying on old logical IDs.
    const FReferenceSkeleton& OldRef = Target->GetSkeleton()->GetReferenceSkeleton();
    const FReferenceSkeleton& NewRef = ExtendedSkeleton->GetReferenceSkeleton();
    const TArray<FTransform> OldAuthored = Target->RetargetSourceAssetReferencePose;
    Result->RetargetSourceAssetReferencePose = NewRef.GetRefBonePose();
    for (int32 Bone = 0; Bone < OldRef.GetNum(); ++Bone)
        if (OldAuthored.IsValidIndex(Bone))
            Result->RetargetSourceAssetReferencePose[NewRef.FindBoneIndex(OldRef.GetBoneName(Bone))] = OldAuthored[Bone];
    // An absent source track uses its reference atom, including while skin/FK
    // tracks animate. Preserve absence rather than manufacturing float keys.
    if (!Track) return Result;
    TArray<FVector3f> Positions, Scales; TArray<FQuat4f> Rotations;
    for (int32 Key = 0; Key < Keys; ++Key)
    {
        const FVector P(Track->PosKeys[Track->PosKeys.Num() == 1 ? 0 : Key]);
        const FQuat Q(Track->RotKeys[Track->RotKeys.Num() == 1 ? 0 : Key]);
        Positions.Add(FVector3f(HandBasisRotation.RotateVector(P)));
        Rotations.Add(FQuat4f((HandBasisRotation * Q).GetNormalized()));
        Scales.Add(Track->ScaleKeys.IsEmpty() ? FVector3f::OneVector : Track->ScaleKeys[Track->ScaleKeys.Num() == 1 ? 0 : Key]);
    }
    IAnimationDataController& Controller = Result->GetController();
    Controller.OpenBracket(FText::FromString(TEXT("Transient Lyra weapon channel")), false);
    const bool Success = Controller.AddBoneCurve(TEXT("weapon_r"), false) &&
        Controller.SetBoneTrackKeys(TEXT("weapon_r"), Positions, Rotations, Scales, false);
    Controller.CloseBracket(false);
    return Success ? Result : nullptr;
}
