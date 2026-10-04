#include "AlsHandControlLibrary.h"

#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimSequence.h"
#include "Animation/AnimationPoseData.h"
#include "Animation/Skeleton.h"
#include "BoneControllers/AnimNode_HandIKRetargeting.h"
#include "BoneControllers/AnimNode_TwoBoneIK.h"
#include "BoneControllers/AnimNode_CopyBone.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/UnrealType.h"

namespace { TSharedPtr<FJsonValue> Atom(const FTransform& Value); }

FString UAlsHandControlLibrary::ReadLogicalHandChain(USkeleton* Skeleton, UClass* LayerClass,
    const FString& LocalPoseJson, float HandFKWeight, float RetargetAlpha, float RightAlpha, float LeftAlpha)
{
    if (!Skeleton || !LayerClass || Skeleton->GetReferenceSkeleton().GetNum() != 81) return {};
    for (float Alpha : {HandFKWeight, RetargetAlpha, RightAlpha, LeftAlpha})
        if (!FMath::IsFinite(Alpha) || Alpha < 0 || Alpha > 1) return {};
    TSharedPtr<FJsonObject> Input;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(LocalPoseJson), Input)) return {};
    const auto& Rows = Input->GetArrayField(TEXT("pose")); if (Rows.Num() != 81) return {};
    FAnimNode_HandIKRetargeting Retarget; FAnimNode_CopyBone Copy;
    FAnimNode_TwoBoneIK Right, Left;
    int32 Count = 0; UObject* CDO = LayerClass->GetDefaultObject();
    for (TFieldIterator<FStructProperty> Property(LayerClass); Property; ++Property)
    {
        if (Property->Struct == FAnimNode_HandIKRetargeting::StaticStruct())
        { Retarget = *Property->ContainerPtrToValuePtr<FAnimNode_HandIKRetargeting>(CDO); ++Count; }
        if (Property->Struct == FAnimNode_CopyBone::StaticStruct())
        {
            auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_CopyBone>(CDO);
            if (Node->SourceBone.BoneName == TEXT("VB IK_Hand_L_weaponSpace") && Node->TargetBone.BoneName == TEXT("ik_hand_l"))
            { Copy = *Node; ++Count; }
        }
        if (Property->Struct == FAnimNode_TwoBoneIK::StaticStruct())
        {
            auto* Node = Property->ContainerPtrToValuePtr<FAnimNode_TwoBoneIK>(CDO);
            if (Node->IKBone.BoneName == TEXT("hand_r")) { Right = *Node; ++Count; }
            if (Node->IKBone.BoneName == TEXT("hand_l")) { Left = *Node; ++Count; }
        }
    }
    if (Count != 4 || Retarget.IKBonesToMove.Num() != 1 || Retarget.IKBonesToMove[0].BoneName != TEXT("ik_hand_gun")) return {};
    Retarget.HandFKWeight = HandFKWeight;
    const FMemMark Mark(FMemStack::Get()); TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < 81; ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
    FCompactPose Pose; Pose.SetBoneContainer(&Container);
    for (const auto Bone : Pose.ForEachBoneIndex())
    {
        const auto Row = Rows[Bone.GetInt()]->AsObject(); const auto& P = Row->GetArrayField(TEXT("position"));
        const auto& Q = Row->GetArrayField(TEXT("rotation")); const auto& S = Row->GetArrayField(TEXT("scale"));
        if (P.Num() != 3 || Q.Num() != 4 || S.Num() != 3) return {};
        FTransform Value(FQuat(Q[0]->AsNumber(), Q[1]->AsNumber(), Q[2]->AsNumber(), Q[3]->AsNumber()),
            FVector(P[0]->AsNumber(), P[1]->AsNumber(), P[2]->AsNumber()), FVector(S[0]->AsNumber(), S[1]->AsNumber(), S[2]->AsNumber()));
        if (Value.ContainsNaN() || !Value.IsRotationNormalized()) return {}; Pose[Bone] = Value;
    }
    Retarget.RightHandFK.Initialize(Container); Retarget.LeftHandFK.Initialize(Container);
    Retarget.RightHandIK.Initialize(Container); Retarget.LeftHandIK.Initialize(Container); Retarget.IKBonesToMove[0].Initialize(Container);
    Copy.SourceBone.Initialize(Container); Copy.TargetBone.Initialize(Container);
    auto InitIK = [&](FAnimNode_TwoBoneIK& Node)
    {
        Node.IKBone.Initialize(Container); Node.EffectorTarget.InitializeBoneReferences(Container); Node.JointTarget.InitializeBoneReferences(Container);
        Node.CachedLowerLimbIndex = Container.GetParentBoneIndex(Node.IKBone.GetCompactPoseIndex(Container));
        Node.CachedUpperLimbIndex = Container.GetParentBoneIndex(Node.CachedLowerLimbIndex);
    };
    InitIK(Right); InitIK(Left);
    FAnimInstanceProxy Proxy; FComponentSpacePoseContext Output(&Proxy); Output.Pose.InitPose(Pose);
    TArray<TSharedPtr<FJsonValue>> Stages;
    auto Evaluate = [&](auto& Node, float Alpha)
    {
        if (!Node.IsValidToEvaluate(Skeleton, Container)) return false;
        if (Alpha > ZERO_ANIMWEIGHT_THRESH)
        { TArray<FBoneTransform> Changes; Node.EvaluateSkeletalControl_AnyThread(Output, Changes); Output.Pose.LocalBlendCSBoneTransforms(Changes, Alpha); }
        FCompactPose After = Output.Pose.GetPose(); FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(Output.Pose, After);
        TArray<TSharedPtr<FJsonValue>> Atoms; for (const auto Bone : After.ForEachBoneIndex()) Atoms.Add(Atom(After[Bone]));
        Stages.Add(MakeShared<FJsonValueArray>(Atoms)); return true;
    };
    if (!Evaluate(Retarget, RetargetAlpha) || !Evaluate(Copy, 1) || !Evaluate(Right, RightAlpha) || !Evaluate(Left, LeftAlpha)) return {};
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>(); Result->SetArrayField(TEXT("stages"), Stages);
    Result->SetNumberField(TEXT("fkWeight"), Retarget.HandFKWeight);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsHandControlLibrary::ReadWeaponSpaceSamples(UAnimSequence* Animation)
{
    if (!Animation || !Animation->GetSkeleton()) return {};
    const FMemMark Mark(FMemStack::Get());
    USkeleton* Skeleton = Animation->GetSkeleton();
    const FReferenceSkeleton& Reference = Skeleton->GetReferenceSkeleton();
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
    Container.SetUseRAWData(true);
    Container.SetUseSourceData(false);
    Container.SetDisableRetargeting(false);
    const TArray<FName> Selected = {TEXT("hand_r"), TEXT("hand_l"), TEXT("weapon_r"), TEXT("VB IK_Hand_L_weaponSpace")};
    TArray<FCompactPoseBoneIndex> Indices;
    TArray<TSharedPtr<FJsonValue>> Names, Rows;
    for (const FName Name : Selected)
    {
        const int32 Bone = Reference.FindBoneIndex(Name);
        if (Bone == INDEX_NONE) return {};
        Indices.Add(Container.GetCompactPoseIndexFromSkeletonPoseIndex(FSkeletonPoseBoneIndex(Bone)));
        Names.Add(MakeShared<FJsonValueString>(Name.ToString()));
    }
    const int32 Keys = Animation->GetNumberOfSampledKeys();
    if (Keys < 2 || Keys > 100000) return {};
    const bool Additive = Animation->IsValidAdditive();
    // Include every key and every interval midpoint. These are native evaluated
    // atoms, including virtual tracks/base-pose conversion, not FK reconstruction.
    for (int32 Sample = 0; Sample <= 2 * (Keys - 1); ++Sample)
    {
        FCompactPose Pose; Pose.SetBoneContainer(&Container);
        FBlendedCurve Curve; Curve.InitFrom(Container);
        UE::Anim::FStackAttributeContainer Attributes;
        FAnimationPoseData Data(Pose, Curve, Attributes);
        const double Time = Animation->GetPlayLength() * Sample / (2.0 * (Keys - 1));
        FAnimExtractContext Extract(Time, false);
        Extract.bExtractWithRootMotionProvider = false;
        Animation->GetAnimationPose(Data, Extract);
        const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("seconds"), Time);
        TArray<TSharedPtr<FJsonValue>> Local, Component;
        for (const auto Bone : Indices)
            Local.Add(Atom(Pose[Bone]));
        Row->SetArrayField(TEXT("local"), Local);
        // Additive atoms have zero-relative scale and mesh rotations. They are
        // not an absolute local hierarchy and must not enter FCSPose here.
        if (!Additive)
        {
            FCSPose<FCompactPose> CS; CS.InitPose(Pose);
            for (const auto Bone : Indices)
                Component.Add(Atom(CS.GetComponentSpaceTransform(Bone)));
            Row->SetArrayField(TEXT("component"), Component);
        }
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), Animation->GetPathName());
    Result->SetBoolField(TEXT("additive"), Additive);
    Result->SetNumberField(TEXT("keyCount"), Keys);
    Result->SetArrayField(TEXT("names"), Names);
    Result->SetArrayField(TEXT("samples"), Rows);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json));
    return Json;
}

namespace
{
TSharedPtr<FJsonValue> Atom(const FTransform& Value)
{
    const TSharedRef<FJsonObject> Row = MakeShared<FJsonObject>();
    const FVector P = Value.GetTranslation(), S = Value.GetScale3D();
    const FQuat Q = Value.GetRotation();
    Row->SetArrayField(TEXT("position"), {MakeShared<FJsonValueNumber>(P.X), MakeShared<FJsonValueNumber>(P.Y), MakeShared<FJsonValueNumber>(P.Z)});
    Row->SetArrayField(TEXT("rotation"), {MakeShared<FJsonValueNumber>(Q.X), MakeShared<FJsonValueNumber>(Q.Y), MakeShared<FJsonValueNumber>(Q.Z), MakeShared<FJsonValueNumber>(Q.W)});
    Row->SetArrayField(TEXT("scale"), {MakeShared<FJsonValueNumber>(S.X), MakeShared<FJsonValueNumber>(S.Y), MakeShared<FJsonValueNumber>(S.Z)});
    return MakeShared<FJsonValueObject>(Row);
}
}

FString UAlsHandControlLibrary::ReadWeaponSpaceCopyPose(USkeleton* Skeleton, const FString& LocalPoseJson,
                                                       const float Alpha)
{
    if (!Skeleton || !FMath::IsFinite(Alpha) || Alpha < 0 || Alpha > 1) return {};
    TSharedPtr<FJsonObject> Input;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(LocalPoseJson), Input)) return {};
    const FReferenceSkeleton& Reference = Skeleton->GetReferenceSkeleton();
    const auto& Rows = Input->GetArrayField(TEXT("pose"));
    if (Rows.Num() != Reference.GetNum()) return {};
    const FMemMark Mark(FMemStack::Get());
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
    FCompactPose Pose; Pose.SetBoneContainer(&Container);
    for (const auto Bone : Pose.ForEachBoneIndex())
    {
        const auto Row = Rows[Bone.GetInt()]->AsObject();
        const auto& P = Row->GetArrayField(TEXT("position"));
        const auto& Q = Row->GetArrayField(TEXT("rotation"));
        const auto& S = Row->GetArrayField(TEXT("scale"));
        if (P.Num() != 3 || Q.Num() != 4 || S.Num() != 3) return {};
        const FTransform Value(FQuat(Q[0]->AsNumber(), Q[1]->AsNumber(), Q[2]->AsNumber(), Q[3]->AsNumber()),
            FVector(P[0]->AsNumber(), P[1]->AsNumber(), P[2]->AsNumber()),
            FVector(S[0]->AsNumber(), S[1]->AsNumber(), S[2]->AsNumber()));
        if (Value.ContainsNaN() || !Value.IsRotationNormalized()) return {};
        Pose[Bone] = Value;
    }
    FAnimInstanceProxy Proxy; FComponentSpacePoseContext Output(&Proxy); Output.Pose.InitPose(Pose);
    FAnimNode_CopyBone Node;
    Node.SourceBone.BoneName = TEXT("VB IK_Hand_L_weaponSpace");
    Node.TargetBone.BoneName = TEXT("ik_hand_l");
    Node.SourceBone.Initialize(Container); Node.TargetBone.Initialize(Container);
    Node.bCopyTranslation = true; Node.bCopyRotation = true; Node.bCopyScale = false;
    Node.ControlSpace = BCS_ComponentSpace;
    if (!Node.IsValidToEvaluate(Skeleton, Container)) return {};
    TArray<FBoneTransform> Changes;
    Node.EvaluateSkeletalControl_AnyThread(Output, Changes);
    Output.Pose.LocalBlendCSBoneTransforms(Changes, Alpha);
    FCompactPose After = Output.Pose.GetPose();
    FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(Output.Pose, After);
    TArray<TSharedPtr<FJsonValue>> Atoms;
    for (const auto Bone : After.ForEachBoneIndex()) Atoms.Add(Atom(After[Bone]));
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetNumberField(TEXT("alpha"), Alpha);
    Result->SetNumberField(TEXT("changedBones"), Changes.Num());
    Result->SetArrayField(TEXT("after"), Atoms);
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsHandControlLibrary::ReadHandRetargetPose(UAnimSequence* Animation, const double TimeSeconds,
                                                    const float HandFKWeight, const float Alpha)
{
    if (!Animation || !Animation->GetSkeleton() || Animation->IsValidAdditive() ||
        !FMath::IsFinite(TimeSeconds) || !FMath::IsFinite(HandFKWeight) || !FMath::IsFinite(Alpha) ||
        Alpha < 0 || Alpha > 1) return {};
    const FMemMark Mark(FMemStack::Get());
    USkeleton* Skeleton = Animation->GetSkeleton();
    const FReferenceSkeleton& Reference = Skeleton->GetReferenceSkeleton();
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
    Container.SetUseRAWData(true);
    Container.SetDisableRetargeting(false);
    FCompactPose Pose;
    Pose.SetBoneContainer(&Container);
    FBlendedCurve Curve;
    Curve.InitFrom(Container);
    UE::Anim::FStackAttributeContainer Attributes;
    FAnimationPoseData PoseData(Pose, Curve, Attributes);
    FAnimExtractContext Extract(TimeSeconds, true);
    Extract.bExtractWithRootMotionProvider = false;
    Animation->GetAnimationPose(PoseData, Extract);

    FAnimInstanceProxy Proxy;
    FComponentSpacePoseContext Output(&Proxy);
    Output.Pose.InitPose(Pose);
    FAnimNode_HandIKRetargeting Node;
    Node.HandFKWeight = HandFKWeight;
    Node.RightHandFK.BoneName = TEXT("hand_r"); Node.LeftHandFK.BoneName = TEXT("hand_l");
    Node.RightHandIK.BoneName = TEXT("ik_hand_r"); Node.LeftHandIK.BoneName = TEXT("ik_hand_l");
    FBoneReference Gun; Gun.BoneName = TEXT("ik_hand_gun");
    Node.IKBonesToMove.Add(Gun);
    Node.RightHandFK.Initialize(Container); Node.LeftHandFK.Initialize(Container);
    Node.RightHandIK.Initialize(Container); Node.LeftHandIK.Initialize(Container);
    Node.IKBonesToMove[0].Initialize(Container);
    if (!Node.IsValidToEvaluate(Skeleton, Container)) return {};
    TArray<FBoneTransform> Changes;
    Node.EvaluateSkeletalControl_AnyThread(Output, Changes);
    if (Changes.Num() > 0) Output.Pose.LocalBlendCSBoneTransforms(Changes, Alpha);
    FCompactPose After = Output.Pose.GetPose();
    FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(Output.Pose, After);

    TArray<TSharedPtr<FJsonValue>> BeforeRows, AfterRows, Names;
    for (const FCompactPoseBoneIndex Bone : Pose.ForEachBoneIndex())
    {
        const int32 Index = Container.GetSkeletonPoseIndexFromCompactPoseIndex(Bone).GetInt();
        Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Index).ToString()));
        BeforeRows.Add(Atom(Pose[Bone])); AfterRows.Add(Atom(After[Bone]));
    }
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), Animation->GetPathName());
    Result->SetNumberField(TEXT("timeSeconds"), TimeSeconds);
    Result->SetNumberField(TEXT("handFKWeight"), HandFKWeight);
    Result->SetNumberField(TEXT("alpha"), Alpha);
    Result->SetNumberField(TEXT("changedBones"), Changes.Num());
    Result->SetArrayField(TEXT("names"), Names);
    Result->SetArrayField(TEXT("before"), BeforeRows);
    Result->SetArrayField(TEXT("after"), AfterRows);
    FString Json;
    FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json));
    return Json;
}

FString UAlsHandControlLibrary::ReadTwoBoneHandPose(UAnimSequence* Animation, const double TimeSeconds,
                                                 const bool RightHand, const bool RetargetFirst,
                                                 const float Alpha, const FVector EffectorOffset)
{
    if (!Animation || !Animation->GetSkeleton() || Animation->IsValidAdditive() ||
        !FMath::IsFinite(TimeSeconds) || !FMath::IsFinite(Alpha) || Alpha < 0 || Alpha > 1 ||
        EffectorOffset.ContainsNaN()) return {};
    const FMemMark Mark(FMemStack::Get());
    USkeleton* Skeleton = Animation->GetSkeleton();
    const FReferenceSkeleton& Reference = Skeleton->GetReferenceSkeleton();
    TArray<FBoneIndexType> Required;
    for (int32 Bone = 0; Bone < Reference.GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
    FBoneContainer Container(Required, UE::Anim::FCurveFilterSettings(), *Skeleton);
    Container.SetUseRAWData(true);
    Container.SetDisableRetargeting(false);
    FCompactPose Pose;
    Pose.SetBoneContainer(&Container);
    FBlendedCurve Curve;
    Curve.InitFrom(Container);
    UE::Anim::FStackAttributeContainer Attributes;
    FAnimationPoseData PoseData(Pose, Curve, Attributes);
    FAnimExtractContext Extract(TimeSeconds, true);
    Extract.bExtractWithRootMotionProvider = false;
    Animation->GetAnimationPose(PoseData, Extract);
    FAnimInstanceProxy Proxy;
    FComponentSpacePoseContext Output(&Proxy);
    Output.Pose.InitPose(Pose);
    if (RetargetFirst)
    {
        FAnimNode_HandIKRetargeting Retarget;
        Retarget.HandFKWeight = 1;
        Retarget.RightHandFK.BoneName = TEXT("hand_r"); Retarget.LeftHandFK.BoneName = TEXT("hand_l");
        Retarget.RightHandIK.BoneName = TEXT("ik_hand_r"); Retarget.LeftHandIK.BoneName = TEXT("ik_hand_l");
        FBoneReference Gun; Gun.BoneName = TEXT("ik_hand_gun"); Retarget.IKBonesToMove.Add(Gun);
        Retarget.RightHandFK.Initialize(Container); Retarget.LeftHandFK.Initialize(Container);
        Retarget.RightHandIK.Initialize(Container); Retarget.LeftHandIK.Initialize(Container);
        Retarget.IKBonesToMove[0].Initialize(Container);
        if (!Retarget.IsValidToEvaluate(Skeleton, Container)) return {};
        TArray<FBoneTransform> Changes;
        Retarget.EvaluateSkeletalControl_AnyThread(Output, Changes);
        if (Changes.Num()) Output.Pose.LocalBlendCSBoneTransforms(Changes, 1);
    }
    FCompactPose Before = Output.Pose.GetPose();
    FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(Output.Pose, Before);
    FAnimNode_TwoBoneIK Node;
    Node.IKBone.BoneName = RightHand ? TEXT("hand_r") : TEXT("hand_l");
    Node.EffectorTarget.BoneReference.BoneName = RightHand ? TEXT("ik_hand_r") : TEXT("ik_hand_l");
    Node.JointTarget.BoneReference.BoneName = RightHand ? TEXT("lowerarm_r") : TEXT("lowerarm_l");
    Node.EffectorLocationSpace = BCS_BoneSpace;
    Node.JointTargetLocationSpace = BCS_BoneSpace;
    Node.EffectorLocation = EffectorOffset;
    Node.JointTargetLocation = FVector(0, RightHand ? 50 : -50, 0);
    Node.bTakeRotationFromEffectorSpace = !RightHand;
    // The node's InitializeBoneReferences override is private. Populate its
    // public references/cache exactly as that implementation does for this
    // isolated container; evaluation and local blend remain the real UE code.
    Node.IKBone.Initialize(Container);
    Node.EffectorTarget.InitializeBoneReferences(Container);
    Node.JointTarget.InitializeBoneReferences(Container);
    Node.CachedLowerLimbIndex = Container.GetParentBoneIndex(Node.IKBone.GetCompactPoseIndex(Container));
    Node.CachedUpperLimbIndex = Container.GetParentBoneIndex(Node.CachedLowerLimbIndex);
    if (!Node.IsValidToEvaluate(Skeleton, Container)) return {};
    TArray<FBoneTransform> Changes;
    Node.EvaluateSkeletalControl_AnyThread(Output, Changes);
    Output.Pose.LocalBlendCSBoneTransforms(Changes, Alpha);
    FCompactPose After = Output.Pose.GetPose();
    FCSPose<FCompactPose>::ConvertComponentPosesToLocalPosesSafe(Output.Pose, After);
    TArray<TSharedPtr<FJsonValue>> BeforeRows, AfterRows, Names;
    for (const FCompactPoseBoneIndex Bone : Pose.ForEachBoneIndex())
    {
        const int32 Index = Container.GetSkeletonPoseIndexFromCompactPoseIndex(Bone).GetInt();
        Names.Add(MakeShared<FJsonValueString>(Reference.GetBoneName(Index).ToString()));
        BeforeRows.Add(Atom(Before[Bone])); AfterRows.Add(Atom(After[Bone]));
    }
    const TSharedRef<FJsonObject> Result = MakeShared<FJsonObject>();
    Result->SetStringField(TEXT("source"), Animation->GetPathName());
    Result->SetStringField(TEXT("side"), RightHand ? TEXT("right") : TEXT("left"));
    Result->SetNumberField(TEXT("timeSeconds"), TimeSeconds);
    Result->SetNumberField(TEXT("alpha"), Alpha);
    Result->SetBoolField(TEXT("retargetFirst"), RetargetFirst);
    Result->SetBoolField(TEXT("takeEffectorRotation"), Node.bTakeRotationFromEffectorSpace);
    Result->SetBoolField(TEXT("allowStretching"), Node.bAllowStretching);
    Result->SetBoolField(TEXT("allowTwist"), Node.bAllowTwist);
    Result->SetBoolField(TEXT("maintainEffectorRelativeRotation"), Node.bMaintainEffectorRelRot);
    Result->SetArrayField(TEXT("effectorOffset"), {MakeShared<FJsonValueNumber>(EffectorOffset.X),
        MakeShared<FJsonValueNumber>(EffectorOffset.Y), MakeShared<FJsonValueNumber>(EffectorOffset.Z)});
    Result->SetNumberField(TEXT("changedBones"), Changes.Num());
    Result->SetArrayField(TEXT("names"), Names);
    Result->SetArrayField(TEXT("before"), BeforeRows);
    Result->SetArrayField(TEXT("after"), AfterRows);
    FString Json;
    FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json));
    return Json;
}
