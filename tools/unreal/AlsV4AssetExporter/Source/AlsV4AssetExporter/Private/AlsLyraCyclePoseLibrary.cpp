#include "AlsLyraGraphLibrary.h"

#include "Animation/AnimClassInterface.h"
#include "Animation/AnimInstanceProxy.h"
#include "Animation/AnimInstance.h"
#include "Animation/AnimSequence.h"
#include "Animation/BlendProfile.h"
#include "Animation/Skeleton.h"
#include "Animation/AttributesRuntime.h"
#include "Animation/AnimationSettings.h"
#include "Animation/BuiltInAttributeTypes.h"
#include "Animation/AnimRootMotionProvider.h"
#include "Animation/AnimNodeSpaceConversions.h"
#include "BoneControllers/AnimNode_OrientationWarping.h"
#include "BoneControllers/AnimNode_StrideWarping.h"
#include "Modules/ModuleManager.h"
#include "AnimNodes/AnimNode_LayeredBoneBlend.h"
#include "AnimationRuntime.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "UObject/StrongObjectPtr.h"
#include "Components/SkeletalMeshComponent.h"
#include "Engine/SkeletalMesh.h"
#include "UObject/UnrealType.h"

namespace LyraCyclePoseProbe
{
TArray<TSharedPtr<FJsonValue>> Numbers(std::initializer_list<double> Values)
{
    TArray<TSharedPtr<FJsonValue>> Result;
    for (double Value : Values) Result.Add(MakeShared<FJsonValueNumber>(Value));
    return Result;
}
TSharedPtr<FJsonObject> PoseData(const FCompactPose& Pose, const FBlendedCurve& Curves,
    const UE::Anim::FStackAttributeContainer& Attributes, const FReferenceSkeleton& Reference)
{
    const auto Result = MakeShared<FJsonObject>(); TArray<TSharedPtr<FJsonValue>> Atoms;
    for (const auto Bone : Pose.ForEachBoneIndex())
    {
        const FTransform& Value = Pose[Bone]; const auto Atom = MakeShared<FJsonObject>();
        const FVector P = Value.GetTranslation(), S = Value.GetScale3D(); const FQuat Q = Value.GetRotation();
        Atom->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
        Atom->SetArrayField(TEXT("rotation"), Numbers({Q.X, Q.Y, Q.Z, Q.W}));
        Atom->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z})); Atoms.Add(MakeShared<FJsonValueObject>(Atom));
    }
    Result->SetArrayField(TEXT("pose"), Atoms); const auto CurveValues = MakeShared<FJsonObject>();
    Curves.ForEachElement([&](const auto& Element)
    {
        const auto Value = MakeShared<FJsonObject>(); Value->SetNumberField(TEXT("value"), Element.Value);
        Value->SetNumberField(TEXT("flags"), static_cast<uint32>(Element.Flags));
        CurveValues->SetObjectField(Element.Name.ToString(), Value);
    });
    Result->SetObjectField(TEXT("curves"), CurveValues); TArray<TSharedPtr<FJsonValue>> AttributeValues;
    const auto& Types = Attributes.GetUniqueTypes();
    for (int32 TypeIndex = 0; TypeIndex < Types.Num(); ++TypeIndex)
    {
        if (Types[TypeIndex] != FIntegerAnimationAttribute::StaticStruct() &&
            Types[TypeIndex] != FTransformAnimationAttribute::StaticStruct()) return nullptr;
        const auto& Keys = Attributes.GetKeys(TypeIndex); const auto& Values = Attributes.GetValues(TypeIndex);
        for (int32 Index = 0; Index < Keys.Num(); ++Index)
        {
            const auto Row = MakeShared<FJsonObject>(); const auto& Key = Keys[Index];
            Row->SetStringField(TEXT("name"), Key.GetName().ToString()); Row->SetStringField(TEXT("namespace"), Key.GetNamespace().ToString());
            Row->SetStringField(TEXT("bone"), Reference.GetBoneName(Key.GetIndex()).ToString());
            Row->SetStringField(TEXT("type"), Types[TypeIndex]->GetPathName());
            if (Types[TypeIndex] == FTransformAnimationAttribute::StaticStruct())
            {
                if (Key.GetName() != UE::Anim::IAnimRootMotionProvider::AttributeName) return nullptr;
                const FTransform& Transform = Values[Index].template GetPtr<FTransformAnimationAttribute>()->Value;
                const FVector P = Transform.GetTranslation(), S = Transform.GetScale3D(); const FQuat Q = Transform.GetRotation();
                Row->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
                Row->SetArrayField(TEXT("rotation"), Numbers({Q.X, Q.Y, Q.Z, Q.W}));
                Row->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z}));
                Result->SetObjectField(TEXT("rootMotion"), Row); continue;
            }
            Row->SetNumberField(TEXT("value"), Values[Index].template GetPtr<FIntegerAnimationAttribute>()->Value);
            Row->SetStringField(TEXT("blend"), UE::Anim::Attributes::GetAttributeBlendType(Key) == ECustomAttributeBlendType::Override ? TEXT("Override") : TEXT("Blend"));
            AttributeValues.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    Result->SetArrayField(TEXT("attributes"), AttributeValues); return Result;
}
struct FProxy : FAnimInstanceProxy
{
    explicit FProxy(UAnimInstance* Instance) : FAnimInstanceProxy(Instance) { InitializeObjects(Instance); }
    void Step(UAnimInstance* Instance, float Delta, int32 Ticks)
    {
        for (int32 Index = 0; Index < Ticks; ++Index) UpdateCounter.Increment();
        PreUpdate(Instance, Delta);
    }
    void Setup(USkeleton* InSkeleton)
    {
        TArray<FBoneIndexType> Required;
        for (int32 Bone = 0; Bone < InSkeleton->GetReferenceSkeleton().GetNum(); ++Bone) Required.Add(static_cast<FBoneIndexType>(Bone));
        GetRequiredBones().InitializeTo(Required, UE::Anim::FCurveFilterSettings(), *InSkeleton);
        GetRequiredBones().SetUseRAWData(true); GetRequiredBones().SetDisableRetargeting(false);
    }
};
struct FNodeAccess : FAnimNode_LayeredBoneBlend
{
    static void Refresh(FAnimNode_LayeredBoneBlend& Node)
    {
        FAnimationRuntime::UpdateDesiredBoneWeight(Node.*&FNodeAccess::DesiredBoneBlendWeights,
            Node.*&FNodeAccess::CurrentBoneBlendWeights, Node.BlendWeights);
        Node.bHasRelevantPoses = FAnimWeight::IsRelevant(Node.BlendWeights[0]);
    }
    static TSharedPtr<FJsonObject> CurveBindings(FAnimNode_LayeredBoneBlend& Node)
    {
        const auto Result = MakeShared<FJsonObject>();
        (Node.*&FNodeAccess::CurvePoseSourceIndices).ForEachElement([&](const auto& Element)
        { Result->SetNumberField(Element.Name.ToString(), Element.Index); });
        return Result;
    }
};
struct FSequenceLeaf : FAnimNode_Base
{
    UAnimSequence* Sequence = nullptr; double Time = 0; bool bProvider = false; FDeltaTimeRecord Delta;
    virtual void Evaluate_AnyThread(FPoseContext& Output) override
    {
        FAnimationPoseData Data(Output); FAnimExtractContext Extract(Time, false, Delta, true);
        // Authored-data stage only. Generated Transform RootMotion attributes
        // require the real DeltaTimeRecord and are a separate mandatory warp input.
        Extract.bExtractWithRootMotionProvider = bProvider; Sequence->GetAnimationPose(Data, Extract);
    }
};
TArray<TSharedPtr<FJsonValue>> DataProbes(const FBoneContainer& Container, const FReferenceSkeleton& Reference, bool bTransform = false)
{
    TArray<TSharedPtr<FJsonValue>> Rows;
    UAnimationSettings* Settings = GetMutableDefault<UAnimationSettings>();
    const FName Name = bTransform ? UE::Anim::IAnimRootMotionProvider::AttributeName : FName(TEXT("LyraCycleIntegerBlendProbe"));
    const int32 Bone = bTransform ? 0 : Reference.FindBoneIndex(TEXT("hand_r"));
    const UE::Anim::FAttributeId Id(Name, FCompactPoseBoneIndex(Bone));
    for (int32 Mode = 0; Mode < 2; ++Mode)
    {
        TGuardValue<ECustomAttributeBlendType> Guard(Settings->DefaultAttributeBlendMode,
            Mode == 0 ? ECustomAttributeBlendType::Blend : ECustomAttributeBlendType::Override);
        for (int32 Presence = 0; Presence < 4; ++Presence)
        for (float Weight : {0.f, 1e-5f, 1.00001e-5f, .15f, .5f, .500001f, .8f, 1.f, 1.2f})
        {
            const FMemMark Mark(FMemStack::Get()); FCompactPose Base, Child[1], Output;
            Base.SetBoneContainer(&Container); Child[0].SetBoneContainer(&Container); Output.SetBoneContainer(&Container);
            Base.ResetToRefPose(); Child[0].ResetToRefPose();
            FBlendedCurve BaseCurve, ChildCurve[1], OutputCurve;
            BaseCurve.InitFrom(Container); ChildCurve[0].InitFrom(Container); OutputCurve.InitFrom(Container);
            BaseCurve.Set(TEXT("ProbeShared"), 17); BaseCurve.Set(TEXT("ProbeBaseOnly"), -7);
            ChildCurve[0].Set(TEXT("ProbeShared"), -11); ChildCurve[0].Set(TEXT("ProbeChildOnly"), 9);
            BaseCurve.SetFlags(TEXT("ProbeShared"), static_cast<UE::Anim::ECurveElementFlags>(1));
            ChildCurve[0].SetFlags(TEXT("ProbeShared"), static_cast<UE::Anim::ECurveElementFlags>(2));
            UE::Anim::FStackAttributeContainer BaseAttributes, ChildAttributes[1], OutputAttributes;
            if (bTransform)
            {
                if (Presence & 1) BaseAttributes.FindOrAdd<FTransformAnimationAttribute>(Id)->Value =
                    FTransform(FQuat(FVector::UpVector, 0.6), FVector(17, -7, 3), FVector(1.2, 0.8, 1.5));
                if (Presence & 2) ChildAttributes[0].FindOrAdd<FTransformAnimationAttribute>(Id)->Value =
                    FTransform(FQuat(FVector::ForwardVector, -0.9) * -1.0, FVector(-11, 9, -5), FVector(0.7, 1.3, 0.9));
            }
            else
            {
                if (Presence & 1) BaseAttributes.FindOrAdd<FIntegerAnimationAttribute>(Id)->Value = 17;
                if (Presence & 2) ChildAttributes[0].FindOrAdd<FIntegerAnimationAttribute>(Id)->Value = -11;
            }
            TArray<FPerBoneBlendWeight> Weights; Weights.AddZeroed(81); Weights[Bone].BlendWeight = Weight;
            FAnimationPoseData OutputData(Output, OutputCurve, OutputAttributes);
            FAnimationRuntime::BlendPosesPerBoneFilter(Base, Child, BaseCurve, ChildCurve, BaseAttributes,
                ChildAttributes, OutputData, Weights, FAnimationRuntime::EBlendPosesPerBoneFilterFlags::MeshSpaceRotation, ECurveBlendOption::Override);
            const auto Row = MakeShared<FJsonObject>(); Row->SetBoolField(TEXT("override"), Mode == 1);
            Row->SetNumberField(TEXT("presence"), Presence); Row->SetNumberField(TEXT("weight"), Weight);
            Row->SetObjectField(TEXT("output"), PoseData(Output, OutputCurve, OutputAttributes, Reference));
            Rows.Add(MakeShared<FJsonValueObject>(Row));
        }
    }
    return Rows;
}
}

FString UAlsLyraGraphLibrary::ReadCycleLayerPoseTrace(USkeleton* Skeleton, UClass* LayerClass,
    const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson)
{
    using namespace LyraCyclePoseProbe;
    if (!Skeleton || !LayerClass || Skeleton->GetReferenceSkeleton().GetNum() != 81 || Sequences.IsEmpty()) return {};
    for (const auto* Sequence : Sequences) if (!Sequence || Sequence->GetSkeleton() != Skeleton || Sequence->IsValidAdditive()) return {};
    TSharedPtr<FJsonObject> Requests;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Requests)) return {};
    bool bProvider = false; Requests->TryGetBoolField(TEXT("generatedRootMotion"), bProvider);
    if (bProvider)
    {
        FModuleManager::Get().LoadModuleChecked<IModuleInterface>(TEXT("AnimationWarpingRuntime"));
        if (!UE::Anim::IAnimRootMotionProvider::Get()) return {};
    }
    IAnimClassInterface* Class = IAnimClassInterface::GetFromClass(LayerClass); if (!Class) return {};
    const FAnimBlueprintFunction* Function = IAnimClassInterface::FindAnimBlueprintFunction(Class, TEXT("FullBody_CycleState"));
    if (!Function) return {};
    // Get the actual compiled closure, including native/property index translation.
    TSharedPtr<FJsonObject> Graph;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(ReadCycleLayerGraph(LayerClass)), Graph)) return {};
    int32 BlendIndex = INDEX_NONE, OrientationIndex = INDEX_NONE, StrideIndex = INDEX_NONE;
    for (const auto& Value : Graph->GetArrayField(TEXT("nodes")))
    {
        if (Value->AsObject()->GetStringField(TEXT("type")) == FAnimNode_LayeredBoneBlend::StaticStruct()->GetPathName())
            BlendIndex = static_cast<int32>(Value->AsObject()->GetNumberField(TEXT("index")));
        if (Value->AsObject()->GetStringField(TEXT("type")) == FAnimNode_OrientationWarping::StaticStruct()->GetPathName())
            OrientationIndex = static_cast<int32>(Value->AsObject()->GetNumberField(TEXT("index")));
        if (Value->AsObject()->GetStringField(TEXT("type")) == FAnimNode_StrideWarping::StaticStruct()->GetPathName())
            StrideIndex = static_cast<int32>(Value->AsObject()->GetNumberField(TEXT("index")));
    }
    const auto& Properties = Class->GetAnimNodeProperties();
    if (BlendIndex < 0 || BlendIndex >= Properties.Num()) return {};
    const auto* Original = Properties[Properties.Num() - 1 - BlendIndex]->ContainerPtrToValuePtr<FAnimNode_LayeredBoneBlend>(LayerClass->GetDefaultObject());
    if (Original->BlendMasks.Num() != 1 || !Original->BlendMasks[0] || Original->BlendPoses.Num() != 1 ||
        !Original->bMeshSpaceRotationBlend || Original->bMeshSpaceScaleBlend || Original->bRootSpaceRotationBlend || Original->CurveBlendOption != ECurveBlendOption::Override) return {};
    const auto& Reference = Skeleton->GetReferenceSkeleton();
    TStrongObjectPtr<UBlendProfile> Mask(NewObject<UBlendProfile>(GetTransientPackage()));
    Mask->OwningSkeleton = Skeleton; Mask->Mode = EBlendProfileMode::BlendMask;
    TArray<TSharedPtr<FJsonValue>> MaskWeights;
    for (int32 Bone = 0; Bone < 81; ++Bone)
    {
        float Weight = 0;
        for (const auto& Entry : Original->BlendMasks[0]->ProfileEntries)
            if (Entry.BoneReference.BoneName == Reference.GetBoneName(Bone)) { Weight = Entry.BlendScale; break; }
        Mask->SetBoneBlendScale(Bone, Weight, false, true); MaskWeights.Add(MakeShared<FJsonValueNumber>(Weight));
    }
    // A transient, unregistered carrier supplies the proxy's skeleton only.
    // No component animation initialization, world tick or geometry evaluation.
    TStrongObjectPtr<USkeletalMesh> Mesh(NewObject<USkeletalMesh>(GetTransientPackage())); Mesh->SetSkeleton(Skeleton);
    TStrongObjectPtr<USkeletalMeshComponent> Component(NewObject<USkeletalMeshComponent>(GetTransientPackage()));
    Component->SetEnableAnimation(false); Component->SetSkeletalMeshAsset(Mesh.Get());
    TStrongObjectPtr<UAnimInstance> Instance(NewObject<UAnimInstance>(Component.Get()));
    const FMemMark Mark(FMemStack::Get()); FProxy Proxy(Instance.Get()); Proxy.Setup(Skeleton);
    FAnimNode_LayeredBoneBlend Node = *Original; FSequenceLeaf Base, Child;
    Base.bProvider = Child.bProvider = bProvider;
    Node.BasePose = FPoseLink(); Node.BasePose.SetLinkNode(&Base);
    Node.BlendPoses[0] = FPoseLink(); Node.BlendPoses[0].SetLinkNode(&Child); Node.BlendMasks[0] = Mask.Get();
    Node.InvalidatePerBoneBlendWeights(); Node.Initialize_AnyThread(FAnimationInitializeContext(&Proxy));
    Node.CacheBones_AnyThread(FAnimationCacheBonesContext(&Proxy));
    const auto Result = MakeShared<FJsonObject>(); Result->SetArrayField(TEXT("mask"), MaskWeights);
    bool bOrientation = false; Requests->TryGetBoolField(TEXT("orientation"), bOrientation);
    bool bStride = false; Requests->TryGetBoolField(TEXT("stride"), bStride);
    if (bStride && !bOrientation) return {};
    FAnimNode_OrientationWarping Warp; FAnimNode_ConvertLocalToComponentSpace ToComponent;
    FAnimNode_StrideWarping Stride;
    FAnimNode_ConvertComponentToLocalSpace ToLocal;
    if (bOrientation)
    {
        if (!bProvider || OrientationIndex < 0 || OrientationIndex >= Properties.Num()) return {};
        Warp = *Properties[Properties.Num() - 1 - OrientationIndex]->ContainerPtrToValuePtr<FAnimNode_OrientationWarping>(LayerClass->GetDefaultObject());
        if (Warp.Mode != EWarpingEvaluationMode::Graph || Warp.RotationAxis != EAxis::Z ||
            Warp.WarpingSpace != EOrientationWarpingSpace::ComponentTransform || Warp.CurrentAnimAsset ||
            Warp.bUseManualRootMotionVelocity || Warp.CurrentAnimAssetTime != 0) return {};
        TArray<TSharedPtr<FJsonValue>> OriginalSpines, AdaptedSpines;
        TArray<FBoneReference> Adapted;
        for (const auto& Bone : Warp.SpineBones)
        {
            OriginalSpines.Add(MakeShared<FJsonValueString>(Bone.BoneName.ToString()));
            FName Name = Bone.BoneName;
            if (Name == TEXT("spine_04") || Name == TEXT("spine_05")) Name = TEXT("spine_03");
            if (Reference.FindBoneIndex(Name) == INDEX_NONE) return {};
            if (!Adapted.ContainsByPredicate([Name](const FBoneReference& Existing) { return Existing.BoneName == Name; }))
            { FBoneReference Mapped; Mapped.BoneName = Name; Adapted.Add(Mapped); AdaptedSpines.Add(MakeShared<FJsonValueString>(Name.ToString())); }
        }
        Warp.SpineBones = Adapted;
        ToComponent.LocalPose.SetLinkNode(&Node); Warp.ComponentPose.SetLinkNode(&ToComponent); ToLocal.ComponentPose.SetLinkNode(&Warp);
        ToLocal.Initialize_AnyThread(FAnimationInitializeContext(&Proxy)); ToLocal.CacheBones_AnyThread(FAnimationCacheBonesContext(&Proxy));
        if (!Warp.IsValidToEvaluate(Skeleton, Proxy.GetRequiredBones())) return {};
        const auto Mapping = MakeShared<FJsonObject>(); Mapping->SetArrayField(TEXT("originalSpines"), OriginalSpines);
        Mapping->SetArrayField(TEXT("adaptedSpines"), AdaptedSpines);
        Mapping->SetBoolField(TEXT("hasPredictionAsset"), Warp.CurrentAnimAsset != nullptr);
        Mapping->SetNumberField(TEXT("predictionTime"), Warp.CurrentAnimAssetTime);
        Result->SetObjectField(TEXT("orientationPolicy"), Mapping);
    }
    if (bStride)
    {
        if (StrideIndex < 0 || StrideIndex >= Properties.Num()) return {};
        Stride = *Properties[Properties.Num() - 1 - StrideIndex]->ContainerPtrToValuePtr<FAnimNode_StrideWarping>(LayerClass->GetDefaultObject());
        if (Stride.Mode != EWarpingEvaluationMode::Graph || !Stride.bDisableIfMissingRootMotion) return {};
        Stride.ComponentPose.SetLinkNode(&Warp); ToLocal.ComponentPose.SetLinkNode(&Stride);
        ToLocal.Initialize_AnyThread(FAnimationInitializeContext(&Proxy)); ToLocal.CacheBones_AnyThread(FAnimationCacheBonesContext(&Proxy));
        if (!Stride.IsValidToEvaluate(Skeleton, Proxy.GetRequiredBones())) return {};
        const auto Policy = MakeShared<FJsonObject>(); TArray<TSharedPtr<FJsonValue>> Feet;
        for (const auto& Foot : Stride.FootDefinitions)
        {
            const auto Entry = MakeShared<FJsonObject>();
            Entry->SetStringField(TEXT("ik"), Foot.IKFootBone.BoneName.ToString());
            Entry->SetStringField(TEXT("fk"), Foot.FKFootBone.BoneName.ToString());
            Entry->SetStringField(TEXT("thigh"), Foot.ThighBone.BoneName.ToString());
            Feet.Add(MakeShared<FJsonValueObject>(Entry));
        }
        Policy->SetArrayField(TEXT("feet"), Feet);
        Policy->SetStringField(TEXT("pelvis"), Stride.PelvisBone.BoneName.ToString());
        Policy->SetStringField(TEXT("footRoot"), Stride.IKFootRootBone.BoneName.ToString());
        Policy->SetNumberField(TEXT("rk4UpdateRate"), RK4_SPRING_INTERPOLATOR_UPDATE_RATE);
        Policy->SetNumberField(TEXT("rk4MaxIterations"), RK4_SPRING_INTERPOLATOR_MAX_ITER);
        Result->SetObjectField(TEXT("stridePolicy"), Policy);
    }
    Result->SetObjectField(TEXT("curveBindings"), FNodeAccess::CurveBindings(Node)); TArray<TSharedPtr<FJsonValue>> Rows;
    for (const auto& Value : Requests->GetArrayField(TEXT("frames")))
    {
        const auto& Frame = Value->AsObject(); const auto Row = MakeShared<FJsonObject>();
        Row->SetNumberField(TEXT("frame"), Frame->GetNumberField(TEXT("frame")));
        const FMemMark FrameMark(FMemStack::Get());
        const int32 BaseIndex = static_cast<int32>(Frame->GetNumberField(TEXT("base")));
        const int32 ChildIndex = static_cast<int32>(Frame->GetNumberField(TEXT("child")));
        if (!Sequences.IsValidIndex(BaseIndex) || !Sequences.IsValidIndex(ChildIndex)) return {};
        Base.Sequence = Sequences[BaseIndex]; Base.Time = Frame->GetNumberField(TEXT("time"));
        Child.Sequence = Sequences[ChildIndex]; Child.Time = Frame->GetNumberField(TEXT("childTime"));
        if (bProvider)
        {
            Base.Delta.Set(static_cast<float>(Frame->GetNumberField(TEXT("previous"))), static_cast<float>(Frame->GetNumberField(TEXT("delta"))));
            Child.Delta.Set(static_cast<float>(Frame->GetNumberField(TEXT("childPrevious"))), static_cast<float>(Frame->GetNumberField(TEXT("childDelta"))));
        }
        Node.BlendWeights[0] = static_cast<float>(Frame->GetNumberField(TEXT("weight"))); FNodeAccess::Refresh(Node);
        FPoseContext Output(&Proxy);
        if (bOrientation)
        {
            const auto Input = Frame->GetObjectField(TEXT("orientation"));
            const auto Rotation = Input->GetArrayField(TEXT("relativeRotation"));
            Component->SetRelativeRotation(FQuat(Rotation[0]->AsNumber(), Rotation[1]->AsNumber(), Rotation[2]->AsNumber(), Rotation[3]->AsNumber()));
            Proxy.Step(Instance.Get(), static_cast<float>(Input->GetNumberField(TEXT("delta"))), static_cast<int32>(Input->GetNumberField(TEXT("ticks"))));
            if (Input->GetBoolField(TEXT("reinitialize")))
            {
                if (bStride) Stride.Initialize_AnyThread(FAnimationInitializeContext(&Proxy));
                else Warp.Initialize_AnyThread(FAnimationInitializeContext(&Proxy));
            }
            Warp.LocomotionAngle = static_cast<float>(Input->GetNumberField(TEXT("angle")));
            const auto Direction = Input->GetArrayField(TEXT("direction"));
            Warp.LocomotionDirection = FVector(Direction[0]->AsNumber(), Direction[1]->AsNumber(), Direction[2]->AsNumber());
            Warp.SetAlpha(static_cast<float>(Input->GetNumberField(TEXT("alpha"))));
            FAnimationUpdateSharedContext SharedContext;
            if (FAnimWeight::IsRelevant(Warp.GetAlpha()))
                Warp.UpdateInternal(FAnimationUpdateContext(&Proxy, static_cast<float>(Input->GetNumberField(TEXT("delta"))), &SharedContext).FractionalWeight(static_cast<float>(Input->GetNumberField(TEXT("weight")))));
            if (bStride)
            {
                const auto StrideInput = Frame->GetObjectField(TEXT("stride"));
                Stride.LocomotionSpeed = static_cast<float>(StrideInput->GetNumberField(TEXT("speed")));
                Stride.SetAlpha(static_cast<float>(StrideInput->GetNumberField(TEXT("alpha"))));
                if (FAnimWeight::IsRelevant(Stride.GetAlpha()))
                    Stride.UpdateInternal(FAnimationUpdateContext(&Proxy, static_cast<float>(Input->GetNumberField(TEXT("delta"))), &SharedContext));
            }
            ToLocal.Evaluate_AnyThread(Output);
        }
        else Node.Evaluate_AnyThread(Output);
        const auto OutputData = PoseData(Output.Pose, Output.Curve, Output.CustomAttributes, Reference); if (!OutputData) return {};
        Row->SetObjectField(TEXT("output"), OutputData); Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    Result->SetArrayField(TEXT("rows"), Rows); Result->SetBoolField(TEXT("generatedRootMotion"), bProvider);
    bool bIncludeProbes = false;
    if (Requests->TryGetBoolField(TEXT("includeProbes"), bIncludeProbes) && bIncludeProbes)
        Result->SetArrayField(TEXT("probes"), DataProbes(Proxy.GetRequiredBones(), Reference, bProvider));
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}

FString UAlsLyraGraphLibrary::ReadRootMotionTrace(const TArray<UAnimSequence*>& Sequences, const FString& RequestsJson)
{
    using namespace LyraCyclePoseProbe;
    const FMemMark Mark(FMemStack::Get());
    TSharedPtr<FJsonObject> Requests;
    if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(RequestsJson), Requests)) return {};
    FModuleManager::Get().LoadModuleChecked<IModuleInterface>(TEXT("AnimationWarpingRuntime"));
    const auto* Provider = UE::Anim::IAnimRootMotionProvider::Get(); if (!Provider) return {};
    auto Atom = [](const FTransform& Value)
    {
        const auto Row = MakeShared<FJsonObject>();
        const FVector P = Value.GetTranslation(), S = Value.GetScale3D(); const FQuat Q = Value.GetRotation();
        Row->SetArrayField(TEXT("position"), Numbers({P.X, P.Y, P.Z}));
        Row->SetArrayField(TEXT("rotation"), Numbers({Q.X, Q.Y, Q.Z, Q.W}));
        Row->SetArrayField(TEXT("scale"), Numbers({S.X, S.Y, S.Z})); return Row;
    };
    TArray<TSharedPtr<FJsonValue>> Rows;
    for (const auto& Value : Requests->GetArrayField(TEXT("ranges")))
    {
        const FMemMark RangeMark(FMemStack::Get());
        const auto Request = Value->AsObject(); const int32 Index = static_cast<int32>(Request->GetNumberField(TEXT("sequence")));
        if (!Sequences.IsValidIndex(Index) || !Sequences[Index] || Sequences[Index]->IsValidAdditive()) return {};
        const auto* Sequence = Sequences[Index];
        const float Previous = static_cast<float>(Request->GetNumberField(TEXT("previous")));
        const float Delta = static_cast<float>(Request->GetNumberField(TEXT("delta")));
        const bool bLooping = Request->GetBoolField(TEXT("looping"));
        FDeltaTimeRecord Record; Record.Set(Previous, Delta);
        const FAnimExtractContext Extract(Previous, true, Record, bLooping);
        const auto Row = MakeShared<FJsonObject>(); Row->SetNumberField(TEXT("sequence"), Index);
        Row->SetNumberField(TEXT("case"), Request->GetNumberField(TEXT("case")));
        FTransform Sample;
        const FAnimExtractContext SampleContext(Request->GetNumberField(TEXT("sampleTime")));
        Sequence->GetBoneTransform(Sample, FSkeletonPoseBoneIndex(0), SampleContext, true);
        Row->SetObjectField(TEXT("rawSample"), Atom(Sample));
        // ExtractRootMotion decides raw/compressed through the real transient
        // sequence's current data validity, exactly as the provider does.
        Row->SetObjectField(TEXT("rootSample"), Atom(Sequence->ExtractRootTrackTransform(SampleContext, nullptr)));
        Row->SetObjectField(TEXT("extracted"), Atom(Sequence->ExtractRootMotion(Extract)));
        UE::Anim::FStackAttributeContainer Attributes;
        if (Sequence->HasRootMotion()) Provider->SampleRootMotion(Record, *Sequence, bLooping, Attributes);
        FTransform Provided; const bool bPresent = Provider->ExtractRootMotion(Attributes, Provided);
        Row->SetBoolField(TEXT("present"), bPresent); Row->SetObjectField(TEXT("provided"), Atom(Provided));
        Rows.Add(MakeShared<FJsonValueObject>(Row));
    }
    const auto Result = MakeShared<FJsonObject>(); Result->SetArrayField(TEXT("rows"), Rows);
    Result->SetStringField(TEXT("name"), UE::Anim::IAnimRootMotionProvider::AttributeName.ToString());
    Result->SetStringField(TEXT("type"), FTransformAnimationAttribute::StaticStruct()->GetPathName());
    const UE::Anim::FAttributeId Id(UE::Anim::IAnimRootMotionProvider::AttributeName, FCompactPoseBoneIndex(0));
    Result->SetStringField(TEXT("namespace"), Id.GetNamespace().ToString());
    Result->SetStringField(TEXT("blend"), UE::Anim::Attributes::GetAttributeBlendType(Id) == ECustomAttributeBlendType::Override ? TEXT("Override") : TEXT("Blend"));
    FString Json; FJsonSerializer::Serialize(Result, TJsonWriterFactory<>::Create(&Json)); return Json;
}
