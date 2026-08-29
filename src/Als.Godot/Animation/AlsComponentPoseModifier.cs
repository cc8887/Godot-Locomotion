using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using System.Runtime.CompilerServices;

namespace GodotAls.Animation;

public enum AlsPoseModifierFailureStage : byte
{
    None,
    AfterAim,
    AfterPelvis,
    AfterLeftFoot,
}

internal enum AlsPoseAffineTestFixture : byte
{
    None,
    SingularBase,
    NearSingularBase,
    SingularAim,
    NearSingularAim,
}

public readonly record struct AlsPoseModifierInput(
    float AimPhase,
    float AimDownWeight,
    float AimForwardWeight,
    float AimUpWeight,
    float HeadWeight,
    float SpineWeight,
    float UpperBodyWeight,
    float ArmLocalWeight,
    AlsPoseModifierFailureStage InjectFailure)
{
    public System.Numerics.Vector3 PelvisOffset { get; init; }

    public AlsFootPoseOutput LeftFootPose { get; init; }

    public AlsFootPoseOutput RightFootPose { get; init; }

    public float LeftFootIkWeight { get; init; }

    public float RightFootIkWeight { get; init; }

    public static AlsPoseModifierInput FromResult(in AlsFrameResult result)
    {
        var pitch = Math.Clamp(result.AimRelativePitch / (MathF.PI * 0.5f), -1f, 1f);
        var down = pitch < 0f ? -pitch : 0f;
        var up = pitch > 0f ? pitch : 0f;
        var forward = 1f - down - up;
        var phase = 0.5f + result.AimRelativeYaw / (MathF.PI * 2f);
        phase -= MathF.Floor(phase);
        return new AlsPoseModifierInput(
            phase,
            down,
            forward,
            up,
            result.HeadWeight,
            result.SpineWeight,
            result.UpperBodyWeight,
            0f,
            AlsPoseModifierFailureStage.None)
        {
            PelvisOffset = result.PelvisOffset,
            LeftFootPose = result.LeftFootPose,
            RightFootPose = result.RightFootPose,
            LeftFootIkWeight = result.LeftFootIkWeight,
            RightFootIkWeight = result.RightFootIkWeight,
        };
    }
}

public struct AlsPoseModifierOutput
{
    public ulong PoseDigest;
    public long OperationTicks;
    public int WriteTransactionCount;
    public int AffectedBoneCount;
    public float ArmLocalWeight;
    public float ArmMeshWeight;
    public int AdditiveBaseAnimationId;
}

internal interface IAlsSkeletonPoseWriter
{
    void SetBonePosePosition(int boneId, in Vector3 value);

    void SetBonePoseRotation(int boneId, in Quaternion value);

    void SetBonePoseScale(int boneId, in Vector3 value);
}

public sealed class AlsComponentPoseModifier : IDisposable
{
    private const ulong DigestOffsetBasis = 14695981039346656037UL;
    private const ulong DigestPrime = 1099511628211UL;
    private const byte WeightSpine = 1;
    private const byte WeightHead = 2;
    private const byte WeightArm = 3;
    private const byte WeightHand = 4;
    private const byte WeightFoot = 5;
    private const float MinimumAffineAxisScale = 1e-6f;
    private const float MaximumAffineAxisRatio = 1e6f;
    private const float MinimumRelativeDeterminant = 1e-5f;

    private readonly Skeleton3D _skeleton;
    private readonly Node3D _visualRoot;
    private readonly AlsPoseScratch _scratch;
    private readonly Transform3D[] _rests;
    private readonly Transform3D[] _baseLocalInverses;
    private readonly Transform3D[] _baseComponentInverses;
    private readonly string[] _boneNames;
    private readonly byte[] _weightKinds;
    private readonly ClipBinding _base;
    private readonly ClipBinding _down;
    private readonly ClipBinding _forward;
    private readonly ClipBinding _up;
    private readonly double _baseTime;
    private readonly int _baseAnimationId;
    private readonly AlsCompiledFootRig _footRig;
    private readonly AlsFootPlacementSettings _footSettings;
    private readonly Transform3D _leftTargetToFootBind;
    private readonly Transform3D _rightTargetToFootBind;
    private readonly Vector3 _leftBindPoleInPelvis;
    private readonly Vector3 _rightBindPoleInPelvis;
    private readonly IAlsSkeletonPoseWriter? _testWriter;
    private readonly AlsPoseAffineTestFixture _testAffineFixture;
    private ulong _skeletonVersion;
    private int _nameValidationCount;
    private int _disposed;

    public AlsComponentPoseModifier(
        Skeleton3D skeleton,
        Node3D visualRoot,
        AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition animationSet,
        AlsPoseAnimationProfile profile)
        : this(skeleton, visualRoot, library, animationSet, profile, null)
    {
    }

    internal AlsComponentPoseModifier(
        Skeleton3D skeleton,
        Node3D visualRoot,
        AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition animationSet,
        AlsPoseAnimationProfile profile,
        IAlsSkeletonPoseWriter? testWriter,
        AlsPoseAffineTestFixture testAffineFixture = AlsPoseAffineTestFixture.None)
    {
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        _visualRoot = visualRoot ?? throw new ArgumentNullException(nameof(visualRoot));
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(animationSet);
        ArgumentNullException.ThrowIfNull(profile);
        _testWriter = testWriter;
        _testAffineFixture = testAffineFixture;
        if (!GodotObject.IsInstanceValid(skeleton) || !GodotObject.IsInstanceValid(visualRoot) ||
            profile.SkeletonId < 0 || profile.SkeletonId >= animationSet.Skeletons.Length)
        {
            throw new InvalidOperationException("Pose modifier received an invalid runtime rig or profile.");
        }

        var skeletonDefinition = animationSet.Skeletons[profile.SkeletonId];
        AlsAnimationBinder.ValidateTargetSkeleton(skeleton, skeletonDefinition, "P4 component pose modifier");
        _scratch = new AlsPoseScratch(skeleton);
        _rests = new Transform3D[_scratch.BoneCount];
        _baseLocalInverses = new Transform3D[_scratch.BoneCount];
        _baseComponentInverses = new Transform3D[_scratch.BoneCount];
        _boneNames = new string[_scratch.BoneCount];
        _weightKinds = new byte[_scratch.BoneCount];
        for (var boneId = 0; boneId < _rests.Length; boneId++)
        {
            _rests[boneId] = skeleton.GetBoneRest(boneId);
            // Binder has already established the source identity case-insensitively.
            // Cache the validated runtime spelling so the hot check is exact and allocation-free.
            _boneNames[boneId] = skeleton.GetBoneName(boneId);
            var expectedParent = skeletonDefinition.PhysicalBones[boneId].ParentPhysicalId;
            if (_scratch.Parents[boneId] != expectedParent ||
                !IsAffineInvertible(_rests[boneId]))
            {
                throw new InvalidOperationException(
                    $"P4 modifier skeleton parent/rest mismatch: bone={boneId}");
            }
        }

        _footRig = profile.FootRig;
        _footSettings = profile.Feet;
        CompileAimMasks(profile, skeletonDefinition);
        CompileFootAffectedSet();
        if (!BuildComponents(_rests, _scratch.BindComponentPose) ||
            !TryCompileFootCalibration(
                _footRig.Left,
                out var leftTargetToFootBind,
                out var leftBindPoleInPelvis) ||
            !TryCompileFootCalibration(
                _footRig.Right,
                out var rightTargetToFootBind,
                out var rightBindPoleInPelvis))
        {
            throw new InvalidOperationException("P4 foot rig bind calibration failed.");
        }
        _leftTargetToFootBind = leftTargetToFootBind;
        _leftBindPoleInPelvis = leftBindPoleInPelvis;
        _rightTargetToFootBind = rightTargetToFootBind;
        _rightBindPoleInPelvis = rightBindPoleInPelvis;
        var downDefinition = GetAnimationDefinition(animationSet, profile.Aim.DownAnimationId);
        var forwardDefinition = GetAnimationDefinition(animationSet, profile.Aim.ForwardAnimationId);
        var upDefinition = GetAnimationDefinition(animationSet, profile.Aim.UpAnimationId);
        _baseAnimationId = profile.Aim.AdditiveBasePoseAnimationId;
        var baseDefinition = GetAnimationDefinition(animationSet, _baseAnimationId);
        if (downDefinition.AdditiveBasePoseAnimationId != _baseAnimationId ||
            forwardDefinition.AdditiveBasePoseAnimationId != _baseAnimationId ||
            upDefinition.AdditiveBasePoseAnimationId != _baseAnimationId ||
            downDefinition.AdditiveBasePoseFrame != forwardDefinition.AdditiveBasePoseFrame ||
            downDefinition.AdditiveBasePoseFrame != upDefinition.AdditiveBasePoseFrame ||
            baseDefinition.FrameRateNumerator <= 0 || baseDefinition.FrameRateDenominator <= 0)
        {
            throw new InvalidOperationException("P4 Aim sweeps do not share one validated additive base frame.");
        }
        _baseTime = (double)downDefinition.AdditiveBasePoseFrame *
            baseDefinition.FrameRateDenominator / baseDefinition.FrameRateNumerator;

        _base = BindClip(library, _baseAnimationId);
        _down = BindClip(library, profile.Aim.DownAnimationId);
        _forward = BindClip(library, profile.Aim.ForwardAnimationId);
        _up = BindClip(library, profile.Aim.UpAnimationId);
        if (!SampleClip(_base, _baseTime, _scratch.BaseLocalPose, _scratch.BaseComponentPose))
        {
            throw new InvalidOperationException("P4 additive base pose is non-invertible.");
        }
        for (var boneId = 0; boneId < _scratch.BoneCount; boneId++)
        {
            if (!TryAffineInverse(
                    _scratch.BaseLocalPose[boneId],
                    out _baseLocalInverses[boneId]) ||
                !TryAffineInverse(
                    _scratch.BaseComponentPose[boneId],
                    out _baseComponentInverses[boneId]))
            {
                throw new InvalidOperationException(
                    $"P4 additive base pose inverse failed: bone={boneId}");
            }
        }
        _skeletonVersion = skeleton.GetVersion();
    }

    internal int NameValidationCount => Volatile.Read(ref _nameValidationCount);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool TryApply(
        in AlsPoseModifierInput input,
        ref AlsPoseModifierOutput output,
        out AlsP4ReasonCode reason)
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            !GodotObject.IsInstanceValid(_skeleton) ||
            !GodotObject.IsInstanceValid(_visualRoot) ||
            !ValidateCachedTopology())
        {
            reason = AlsP4ReasonCode.InvalidRuntimeState;
            return false;
        }
        if (!Validate(in input, out reason))
        {
            return false;
        }

        var capturedRoot = _visualRoot.GlobalTransform;
        if (!IsFinite(capturedRoot) || !CaptureCurrentPose())
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }

        var writeStarted = false;
        try
        {
            Array.Clear(_scratch.Modified);
            long operationTicks = _scratch.BoneCount * 4L;
            var hasInfluence = input.HeadWeight > 0f ||
                input.SpineWeight > 0f || input.UpperBodyWeight > 0f;
            if (hasInfluence)
            {
                if (!SampleAimPoses(in input, out var sampledClipCount))
                {
                    reason = AlsP4ReasonCode.NonFiniteInput;
                    return false;
                }
                ApplyTestAffineFixture();
                if (!ApplyAim(in input))
                {
                    reason = AlsP4ReasonCode.NonFiniteInput;
                    return false;
                }
                operationTicks += _scratch.BoneCount * sampledClipCount * 5L;
                operationTicks += _scratch.AimAffectedCount * 8L;
            }

            if (input.InjectFailure == AlsPoseModifierFailureStage.AfterAim)
            {
                reason = AlsP4ReasonCode.InvalidRuntimeState;
                return false;
            }

            var hasFootInfluence = input.LeftFootIkWeight > 0f ||
                input.RightFootIkWeight > 0f ||
                input.PelvisOffset != System.Numerics.Vector3.Zero;
            if (hasFootInfluence)
            {
                if (!ApplyPelvis(input.PelvisOffset))
                {
                    reason = AlsP4ReasonCode.NonFiniteInput;
                    return false;
                }
                operationTicks += _scratch.BoneCount * 2L;
                if (input.InjectFailure == AlsPoseModifierFailureStage.AfterPelvis)
                {
                    reason = AlsP4ReasonCode.InvalidRuntimeState;
                    return false;
                }

                if (input.LeftFootIkWeight > 0f &&
                    !ApplyLeg(
                        _footRig.Left,
                        input.LeftFootPose,
                        input.LeftFootIkWeight,
                        _leftTargetToFootBind,
                        _leftBindPoleInPelvis))
                {
                    reason = AlsP4ReasonCode.NonFiniteInput;
                    return false;
                }
                operationTicks += _scratch.BoneCount * 3L;
                if (input.InjectFailure == AlsPoseModifierFailureStage.AfterLeftFoot)
                {
                    reason = AlsP4ReasonCode.InvalidRuntimeState;
                    return false;
                }

                if (input.RightFootIkWeight > 0f &&
                    !ApplyLeg(
                        _footRig.Right,
                        input.RightFootPose,
                        input.RightFootIkWeight,
                        _rightTargetToFootBind,
                        _rightBindPoleInPelvis))
                {
                    reason = AlsP4ReasonCode.NonFiniteInput;
                    return false;
                }
                operationTicks += _scratch.BoneCount * 3L;
            }

            var writeTransactions = 0;
            if (hasInfluence || hasFootInfluence)
            {
                if (!WriteAffectedPose(ref writeStarted))
                {
                    if (writeStarted)
                    {
                        return FailAfterCapture(
                            capturedRoot, AlsP4ReasonCode.NonFiniteInput, out reason);
                    }
                    reason = AlsP4ReasonCode.NonFiniteInput;
                    return false;
                }
                writeTransactions = 1;
                operationTicks += _scratch.AffectedCount * 4L;
            }

            var poseDigest = ComputePoseDigest();
            operationTicks += _scratch.BoneCount * 4L;
            var candidate = new AlsPoseModifierOutput
            {
                PoseDigest = poseDigest,
                OperationTicks = operationTicks,
                WriteTransactionCount = writeTransactions,
                AffectedBoneCount = _scratch.AffectedCount,
                ArmLocalWeight = input.ArmLocalWeight,
                ArmMeshWeight = 1f - input.ArmLocalWeight,
                AdditiveBaseAnimationId = _baseAnimationId,
            };
            output = candidate;
            reason = AlsP4ReasonCode.None;
            return true;
        }
        catch
        {
            if (!writeStarted)
            {
                reason = AlsP4ReasonCode.InvalidRuntimeState;
                return false;
            }
            return FailAfterCapture(
                capturedRoot, AlsP4ReasonCode.InvalidRuntimeState, out reason);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool ValidateCachedTopology()
    {
        if (_skeleton.GetBoneCount() != _scratch.BoneCount ||
            _scratch.Order.Length != _scratch.BoneCount ||
            _scratch.AffectedCount <= 0 ||
            _scratch.AffectedCount > _scratch.AffectedOrder.Length)
        {
            return false;
        }

        var currentVersion = _skeleton.GetVersion();
        if (currentVersion != _skeletonVersion)
        {
            Interlocked.Increment(ref _nameValidationCount);
            for (var boneId = 0; boneId < _scratch.BoneCount; boneId++)
            {
                if (_skeleton.FindBone(_boneNames[boneId]) != boneId)
                {
                    return false;
                }
            }
            _skeletonVersion = currentVersion;
        }

        Array.Clear(_scratch.ValidationVisited);
        for (var index = 0; index < _scratch.Order.Length; index++)
        {
            var boneId = _scratch.Order[index];
            if ((uint)boneId >= (uint)_scratch.BoneCount ||
                _scratch.ValidationVisited[boneId])
            {
                return false;
            }
            var parent = _scratch.Parents[boneId];
            if (_skeleton.GetBoneRest(boneId) != _rests[boneId] ||
                _skeleton.GetBoneParent(boneId) != parent ||
                (parent >= 0 && !_scratch.ValidationVisited[parent]))
            {
                return false;
            }
            _scratch.ValidationVisited[boneId] = true;
        }

        Array.Clear(_scratch.ValidationVisited);
        for (var index = 0; index < _scratch.AffectedCount; index++)
        {
            var boneId = _scratch.AffectedOrder[index];
            if ((uint)boneId >= (uint)_scratch.BoneCount ||
                _scratch.ValidationVisited[boneId] ||
                !_scratch.Affected[boneId] ||
                _weightKinds[boneId] == 0)
            {
                return false;
            }
            _scratch.ValidationVisited[boneId] = true;
        }
        for (var boneId = 0; boneId < _scratch.BoneCount; boneId++)
        {
            if (_scratch.Affected[boneId] != _scratch.ValidationVisited[boneId])
            {
                return false;
            }
        }
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _base.Dispose();
        _down.Dispose();
        _forward.Dispose();
        _up.Dispose();
    }

    private void CompileAimMasks(
        AlsPoseAnimationProfile profile,
        AlsSkeletonDefinition skeletonDefinition)
    {
        var count = 0;
        foreach (var mask in profile.Masks.Entries)
        {
            var weightKind = mask.Kind switch
            {
                AlsPoseMaskKind.UpperBody => WeightSpine,
                AlsPoseMaskKind.Head => WeightHead,
                AlsPoseMaskKind.LeftArm or AlsPoseMaskKind.RightArm => WeightArm,
                AlsPoseMaskKind.LeftHand or AlsPoseMaskKind.RightHand => WeightHand,
                _ => (byte)0,
            };
            if (weightKind == 0)
            {
                continue;
            }
            foreach (var logicalId in mask.BoneIds)
            {
                if ((uint)logicalId >= (uint)skeletonDefinition.LogicalToPhysical.Length)
                {
                    throw new InvalidOperationException($"P4 Aim mask logical bone is invalid: {logicalId}");
                }
                var physicalId = skeletonDefinition.LogicalToPhysical[logicalId];
                if (physicalId < 0)
                {
                    // Virtual logical bones have no Skeleton3D pose channel.
                    continue;
                }
                if ((uint)physicalId >= (uint)_scratch.BoneCount || _scratch.Affected[physicalId])
                {
                    throw new InvalidOperationException($"P4 Aim mask physical bone is invalid or duplicated: {physicalId}");
                }
                _scratch.Affected[physicalId] = true;
                _scratch.AimAffected[physicalId] = true;
                _weightKinds[physicalId] = weightKind;
                count++;
            }
        }
        if (count == 0)
        {
            throw new InvalidOperationException("P4 Aim masks do not affect any physical bone.");
        }
        var orderedCount = 0;
        for (var index = 0; index < _scratch.Order.Length; index++)
        {
            var boneId = _scratch.Order[index];
            if (_scratch.Affected[boneId])
            {
                _scratch.AffectedOrder[orderedCount++] = boneId;
                _scratch.AimAffectedOrder[orderedCount - 1] = boneId;
            }
        }
        if (orderedCount != count)
        {
            throw new InvalidOperationException("P4 Aim mask ordering failed.");
        }
        _scratch.AffectedCount = count;
        _scratch.AimAffectedCount = count;
    }

    private void CompileFootAffectedSet()
    {
        ValidateLeg(_footRig.Left);
        ValidateLeg(_footRig.Right);
        if ((uint)_footRig.PelvisBoneId >= (uint)_scratch.BoneCount ||
            _footRig.Left.ThighBoneId == _footRig.Right.ThighBoneId ||
            _footRig.Left.KneeBoneId == _footRig.Right.KneeBoneId ||
            _footRig.Left.FootBoneId == _footRig.Right.FootBoneId)
        {
            throw new InvalidOperationException("P4 compiled foot rig is invalid.");
        }

        Mark(_footRig.PelvisBoneId);
        Mark(_footRig.Left.ThighBoneId);
        Mark(_footRig.Left.KneeBoneId);
        Mark(_footRig.Left.FootBoneId);
        Mark(_footRig.Right.ThighBoneId);
        Mark(_footRig.Right.KneeBoneId);
        Mark(_footRig.Right.FootBoneId);

        var count = 0;
        for (var index = 0; index < _scratch.Order.Length; index++)
        {
            var boneId = _scratch.Order[index];
            if (_scratch.Affected[boneId])
            {
                _scratch.AffectedOrder[count++] = boneId;
            }
        }
        _scratch.AffectedCount = count;

        void Mark(int boneId)
        {
            if (!_scratch.Affected[boneId])
            {
                _scratch.Affected[boneId] = true;
                _weightKinds[boneId] = WeightFoot;
            }
        }

        void ValidateLeg(in AlsCompiledLegChain leg)
        {
            if ((uint)leg.ThighBoneId >= (uint)_scratch.BoneCount ||
                (uint)leg.KneeBoneId >= (uint)_scratch.BoneCount ||
                (uint)leg.FootBoneId >= (uint)_scratch.BoneCount ||
                _scratch.Parents[leg.ThighBoneId] != _footRig.PelvisBoneId ||
                _scratch.Parents[leg.KneeBoneId] != leg.ThighBoneId ||
                _scratch.Parents[leg.FootBoneId] != leg.KneeBoneId)
            {
                throw new InvalidOperationException(
                    "P4 physical leg chain does not match Skeleton3D parents.");
            }
        }
    }

    private bool TryCompileFootCalibration(
        in AlsCompiledLegChain leg,
        out Transform3D targetToFootBind,
        out Vector3 bindPoleInPelvis)
    {
        targetToFootBind = default;
        bindPoleInPelvis = default;
        if (!TryAffineInverse(_skeleton.GlobalTransform, out var worldToSkeleton))
        {
            return false;
        }

        var characterWorldRotation = _visualRoot.GlobalTransform.Basis
            .Orthonormalized().GetRotationQuaternion().Normalized();
        var skeletonWorldRotation = _skeleton.GlobalTransform.Basis
            .Orthonormalized().GetRotationQuaternion().Normalized();
        var targetBindRotation = skeletonWorldRotation.Inverse() * characterWorldRotation;
        var footBind = _scratch.BindComponentPose[leg.FootBoneId];
        var targetBind = new Transform3D(
            new Basis(targetBindRotation.Normalized()),
            footBind.Origin);
        if (!TryAffineInverse(targetBind, out var targetBindInverse))
        {
            return false;
        }
        targetToFootBind = targetBindInverse * footBind;

        var hip = _scratch.BindComponentPose[leg.ThighBoneId].Origin;
        var knee = _scratch.BindComponentPose[leg.KneeBoneId].Origin;
        var foot = footBind.Origin;
        var axis = foot - hip;
        var axisLengthSquared = axis.LengthSquared();
        if (axisLengthSquared <= 1e-12f)
        {
            return false;
        }
        var pole = knee - hip - axis * ((knee - hip).Dot(axis) / axisLengthSquared);
        if (pole.LengthSquared() <= 1e-12f)
        {
            pole = DeterministicPerpendicular(axis.Normalized());
        }
        else
        {
            pole = pole.Normalized();
        }
        var pelvisBasis = _scratch.BindComponentPose[_footRig.PelvisBoneId]
            .Basis.Orthonormalized();
        bindPoleInPelvis = pelvisBasis.Inverse() * pole;
        return IsAffineInvertible(targetToFootBind) && IsFinite(bindPoleInPelvis);
    }

    private ClipBinding BindClip(AlsAnimationLibraryBuildResult library, int animationId)
    {
        if (!library.ClipNames.TryGetValue(animationId, out var clipName))
        {
            throw new InvalidOperationException($"P4 modifier animation is absent from the library: {animationId}");
        }
        var animation = library.Library.GetAnimation(clipName)
            ?? throw new InvalidOperationException($"P4 modifier could not borrow animation: {animationId}");
        try
        {
            return new ClipBinding(animation, _skeleton, _scratch.BoneCount);
        }
        catch
        {
            animation.Dispose();
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool CaptureCurrentPose()
    {
        for (var boneId = 0; boneId < _scratch.BoneCount; boneId++)
        {
            var position = _skeleton.GetBonePosePosition(boneId);
            var rotation = _skeleton.GetBonePoseRotation(boneId);
            var scale = _skeleton.GetBonePoseScale(boneId);
            if (!IsFinite(position) || !IsFinite(rotation) || !IsFinite(scale) ||
                rotation.LengthSquared() <= 1e-12f)
            {
                return false;
            }
            _scratch.OriginalPositions[boneId] = position;
            _scratch.OriginalRotations[boneId] = rotation;
            _scratch.OriginalScales[boneId] = scale;
            var pose = PoseTransform(position, rotation, scale);
            if (!IsAffineInvertible(pose))
            {
                return false;
            }
            _scratch.OriginalPose[boneId] = pose;
            _scratch.LocalPose[boneId] = pose;
        }
        if (!BuildComponents(_scratch.LocalPose, _scratch.OriginalComponentPose))
        {
            return false;
        }
        Array.Copy(
            _scratch.OriginalComponentPose,
            _scratch.ComponentPose,
            _scratch.BoneCount);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool SampleAimPoses(in AlsPoseModifierInput input, out int sampledClipCount)
    {
        sampledClipCount = 0;
        var downTime = _down.Animation.Length * input.AimPhase;
        var forwardTime = _forward.Animation.Length * input.AimPhase;
        var upTime = _up.Animation.Length * input.AimPhase;
        if (input.AimDownWeight > 0f)
        {
            sampledClipCount++;
            if (!SampleClip(_down, downTime, _scratch.DownLocalPose, _scratch.DownComponentPose))
            {
                return false;
            }
        }
        if (input.AimForwardWeight > 0f)
        {
            sampledClipCount++;
            if (!SampleClip(
                    _forward,
                    forwardTime,
                    _scratch.ForwardLocalPose,
                    _scratch.ForwardComponentPose))
            {
                return false;
            }
        }
        if (input.AimUpWeight > 0f)
        {
            sampledClipCount++;
            if (!SampleClip(_up, upTime, _scratch.UpLocalPose, _scratch.UpComponentPose))
            {
                return false;
            }
        }
        return sampledClipCount > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool SampleClip(
        ClipBinding clip,
        double time,
        Transform3D[] locals,
        Transform3D[] components)
    {
        if (!double.IsFinite(time) || time < 0.0 || time > clip.Animation.Length + 1e-8)
        {
            return false;
        }
        time = Math.Min(time, clip.Animation.Length);
        for (var boneId = 0; boneId < _scratch.BoneCount; boneId++)
        {
            var position = clip.PositionTracks[boneId] >= 0
                ? clip.Animation.PositionTrackInterpolate(clip.PositionTracks[boneId], time)
                : _rests[boneId].Origin;
            var rotation = clip.RotationTracks[boneId] >= 0
                ? clip.Animation.RotationTrackInterpolate(clip.RotationTracks[boneId], time)
                : _rests[boneId].Basis.Orthonormalized()
                    .GetRotationQuaternion().Normalized();
            var scale = clip.ScaleTracks[boneId] >= 0
                ? clip.Animation.ScaleTrackInterpolate(clip.ScaleTracks[boneId], time)
                : _rests[boneId].Basis.Scale;
            if (!IsFinite(position) || !IsFinite(rotation) || !IsFinite(scale) ||
                rotation.LengthSquared() <= 1e-12f)
            {
                return false;
            }
            var pose = PoseTransform(position, rotation, scale);
            locals[boneId] = pose;
            if (!IsAffineInvertible(locals[boneId]))
            {
                return false;
            }
        }
        return BuildComponents(locals, components);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool BuildComponents(Transform3D[] locals, Transform3D[] components)
    {
        for (var index = 0; index < _scratch.Order.Length; index++)
        {
            var boneId = _scratch.Order[index];
            var parent = _scratch.Parents[boneId];
            components[boneId] = parent < 0
                ? locals[boneId]
                : components[parent] * locals[boneId];
            if (!IsAffineInvertible(components[boneId]))
            {
                return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool ApplyAim(in AlsPoseModifierInput input)
    {
        var armMeshWeight = 1f - input.ArmLocalWeight;
        Array.Clear(_scratch.ComponentInverseValid);
        for (var orderIndex = 0; orderIndex < _scratch.AimAffectedCount; orderIndex++)
        {
            var boneId = _scratch.AimAffectedOrder[orderIndex];
            var parent = _scratch.Parents[boneId];
            var parentComponent = parent < 0
                ? Transform3D.Identity
                : _scratch.ComponentPose[parent];
            var aimComponent = BlendAim(
                _scratch.DownComponentPose[boneId],
                _scratch.ForwardComponentPose[boneId],
                _scratch.UpComponentPose[boneId],
                input.AimDownWeight,
                input.AimForwardWeight,
                input.AimUpWeight);
            var aimLocal = BlendAim(
                _scratch.DownLocalPose[boneId],
                _scratch.ForwardLocalPose[boneId],
                _scratch.UpLocalPose[boneId],
                input.AimDownWeight,
                input.AimForwardWeight,
                input.AimUpWeight);
            if (!IsAffineInvertible(aimComponent) || !IsAffineInvertible(aimLocal))
            {
                return false;
            }

            var weight = _weightKinds[boneId] switch
            {
                WeightHead => input.HeadWeight,
                WeightSpine => input.SpineWeight,
                WeightArm or WeightHand => input.UpperBodyWeight,
                _ => 0f,
            };
            if (!TryComposeLocalResultFromInverse(
                    in _scratch.LocalPose[boneId],
                    in _baseLocalInverses[boneId],
                    in aimLocal,
                    weight,
                    out var localResult))
            {
                return false;
            }
            var componentDelta = aimComponent * _baseComponentInverses[boneId];
            Transform3D resultComponent;
            if (_weightKinds[boneId] is WeightArm or WeightHand)
            {
                var fullLocalComponent = parentComponent * localResult;
                var fullMeshComponent = Transform3D.Identity.InterpolateWith(
                        componentDelta,
                        weight) *
                    _scratch.OriginalComponentPose[boneId];
                resultComponent = fullLocalComponent.InterpolateWith(
                    fullMeshComponent,
                    armMeshWeight);
            }
            else
            {
                resultComponent = Transform3D.Identity.InterpolateWith(componentDelta, weight) *
                    _scratch.OriginalComponentPose[boneId];
            }
            if (!IsAffineInvertible(componentDelta) || !IsAffineInvertible(resultComponent))
            {
                return false;
            }
            _scratch.ComponentPose[boneId] = resultComponent;
            _scratch.Modified[boneId] = true;
            if (parent < 0)
            {
                _scratch.LocalPose[boneId] = resultComponent;
            }
            else
            {
                if (!_scratch.ComponentInverseValid[parent])
                {
                    if (!TryAffineInverse(
                            parentComponent,
                            out _scratch.ComponentInverses[parent]))
                    {
                        return false;
                    }
                    _scratch.ComponentInverseValid[parent] = true;
                }
                _scratch.LocalPose[boneId] =
                    _scratch.ComponentInverses[parent] * resultComponent;
            }
            if (!IsAffineInvertible(_scratch.LocalPose[boneId]))
            {
                return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool ApplyPelvis(in System.Numerics.Vector3 worldOffsetValue)
    {
        var worldOffset = ToGodot(worldOffsetValue);
        if (worldOffset == Vector3.Zero)
        {
            return true;
        }
        if (!TryAffineInverse(_skeleton.GlobalTransform, out var worldToSkeleton))
        {
            return false;
        }
        var componentOffset = worldToSkeleton.Basis * worldOffset;
        var pelvisBoneId = _footRig.PelvisBoneId;
        var desiredComponent = _scratch.ComponentPose[pelvisBoneId];
        desiredComponent.Origin += componentOffset;
        return TrySetPosePositionFromComponent(pelvisBoneId, desiredComponent) &&
            BuildComponents(_scratch.LocalPose, _scratch.ComponentPose);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool ApplyLeg(
        in AlsCompiledLegChain leg,
        in AlsFootPoseOutput target,
        float weight,
        in Transform3D targetToFootBind,
        in Vector3 bindPoleInPelvis)
    {
        if (!TryAffineInverse(_skeleton.GlobalTransform, out var worldToSkeleton))
        {
            return false;
        }
        var targetWorld = new Transform3D(
            new Basis(ToGodot(target.Rotation).Normalized()),
            ToGodot(target.Position));
        var desiredFoot = worldToSkeleton * targetWorld * targetToFootBind;
        if (!IsAffineInvertible(desiredFoot))
        {
            return false;
        }

        var hip = _scratch.ComponentPose[leg.ThighBoneId].Origin;
        var knee = _scratch.ComponentPose[leg.KneeBoneId].Origin;
        var foot = _scratch.ComponentPose[leg.FootBoneId].Origin;
        var targetPosition = foot.Lerp(desiredFoot.Origin, weight);
        var upperLength = hip.DistanceTo(knee);
        var lowerLength = knee.DistanceTo(foot);
        var targetDelta = targetPosition - hip;
        var targetDistance = targetDelta.Length();
        if (!float.IsFinite(upperLength) || !float.IsFinite(lowerLength) ||
            upperLength <= 1e-6f || lowerLength <= 1e-6f ||
            !float.IsFinite(targetDistance))
        {
            return false;
        }
        var targetDirection = targetDistance > 1e-6f
            ? targetDelta / targetDistance
            : (foot - hip).Normalized();
        var minimumReach = MathF.Abs(upperLength - lowerLength) + 1e-5f;
        // Core has already enforced the gameplay reach limit around the capsule hip.
        // The physical thigh origin is not that point, so the rig solve clamps only
        // against the authored chain length to avoid collapsing a valid bind pose.
        var maximumReach = upperLength + lowerLength - 1e-5f;
        if (maximumReach < minimumReach)
        {
            return false;
        }
        var solvedDistance = Math.Clamp(targetDistance, minimumReach, maximumReach);
        var solvedFootPosition = hip + targetDirection * solvedDistance;
        var along = (upperLength * upperLength + solvedDistance * solvedDistance -
                     lowerLength * lowerLength) / (2f * solvedDistance);
        var heightSquared = MathF.Max(upperLength * upperLength - along * along, 0f);
        var pole = ProjectPole(knee - hip, targetDirection);
        if (pole.LengthSquared() <= 1e-10f)
        {
            pole = _scratch.ComponentPose[_footRig.PelvisBoneId].Basis
                .Orthonormalized() * bindPoleInPelvis;
            pole = ProjectPole(pole, targetDirection);
        }
        if (pole.LengthSquared() <= 1e-10f)
        {
            pole = DeterministicPerpendicular(targetDirection);
        }
        else
        {
            pole = pole.Normalized();
        }
        var desiredKnee = hip + targetDirection * along +
                          pole * MathF.Sqrt(heightSquared);

        if (!TrySwing(
                knee - hip,
                desiredKnee - hip,
                MathF.PI,
                out var thighSwing))
        {
            return false;
        }
        var thighRotation = (thighSwing * _scratch.ComponentPose[leg.ThighBoneId]
            .Basis.Orthonormalized().GetRotationQuaternion()).Normalized();
        if (!TrySetPoseRotationFromComponent(leg.ThighBoneId, thighRotation) ||
            !BuildComponents(_scratch.LocalPose, _scratch.ComponentPose))
        {
            return false;
        }

        knee = _scratch.ComponentPose[leg.KneeBoneId].Origin;
        foot = _scratch.ComponentPose[leg.FootBoneId].Origin;
        if (!TrySwing(
                foot - knee,
                solvedFootPosition - knee,
                MathF.PI,
                out var kneeSwing))
        {
            return false;
        }
        var kneeRotation = (kneeSwing * _scratch.ComponentPose[leg.KneeBoneId]
            .Basis.Orthonormalized().GetRotationQuaternion()).Normalized();
        if (!TrySetPoseRotationFromComponent(leg.KneeBoneId, kneeRotation) ||
            !BuildComponents(_scratch.LocalPose, _scratch.ComponentPose))
        {
            return false;
        }

        var currentFootRotation = _scratch.ComponentPose[leg.FootBoneId]
            .Basis.Orthonormalized().GetRotationQuaternion().Normalized();
        var targetFootRotation = desiredFoot.Basis.Orthonormalized()
            .GetRotationQuaternion().Normalized();
        var angle = QuaternionAngle(currentFootRotation, targetFootRotation);
        var rotationWeight = angle <= 1e-6f
            ? weight
            : MathF.Min(weight, _footSettings.MaximumFootAngleRadians / angle);
        var footRotation = currentFootRotation.Slerp(
            targetFootRotation,
            Math.Clamp(rotationWeight, 0f, 1f)).Normalized();
        return TrySetPoseRotationFromComponent(leg.FootBoneId, footRotation) &&
            BuildComponents(_scratch.LocalPose, _scratch.ComponentPose);
    }

    private bool TrySetPosePositionFromComponent(
        int boneId,
        in Transform3D desiredComponent)
    {
        var parent = _scratch.Parents[boneId];
        var parentComponent = parent < 0
            ? Transform3D.Identity
            : _scratch.ComponentPose[parent];
        if (!TryAffineInverse(parentComponent, out var parentInverse))
        {
            return false;
        }
        var desiredLocal = parentInverse * desiredComponent;
        var position = desiredLocal.Origin;
        if (!IsFinite(position))
        {
            return false;
        }
        _scratch.LocalPose[boneId] = PoseTransform(
            position,
            _scratch.OriginalRotations[boneId],
            _scratch.OriginalScales[boneId]);
        _scratch.Modified[boneId] = true;
        return IsAffineInvertible(_scratch.LocalPose[boneId]);
    }

    private bool TrySetPoseRotationFromComponent(
        int boneId,
        in Quaternion desiredComponentRotation)
    {
        var parent = _scratch.Parents[boneId];
        var parentComponent = parent < 0
            ? Transform3D.Identity
            : _scratch.ComponentPose[parent];
        var desiredComponent = new Transform3D(
            new Basis(desiredComponentRotation),
            _scratch.ComponentPose[boneId].Origin);
        if (!TryAffineInverse(parentComponent, out var parentInverse))
        {
            return false;
        }
        var desiredLocal = parentInverse * desiredComponent;
        var rotation = desiredLocal.Basis.Orthonormalized()
            .GetRotationQuaternion().Normalized();
        if (!IsFinite(rotation) || rotation.LengthSquared() <= 1e-12f)
        {
            return false;
        }
        _scratch.LocalPose[boneId] = PoseTransform(
            _scratch.OriginalPositions[boneId],
            rotation,
            _scratch.OriginalScales[boneId]);
        _scratch.Modified[boneId] = true;
        return IsAffineInvertible(_scratch.LocalPose[boneId]);
    }

    private static Vector3 ProjectPole(in Vector3 value, in Vector3 axis) =>
        value - axis * value.Dot(axis);

    private static Vector3 DeterministicPerpendicular(in Vector3 axis)
    {
        var reference = MathF.Abs(axis.Dot(Vector3.Up)) < 0.75f
            ? Vector3.Up
            : MathF.Abs(axis.Dot(Vector3.Right)) < 0.75f
                ? Vector3.Right
                : Vector3.Back;
        return axis.Cross(reference).Normalized();
    }

    private static bool TrySwing(
        in Vector3 fromValue,
        in Vector3 toValue,
        float maximumAngle,
        out Quaternion swing)
    {
        swing = Quaternion.Identity;
        var fromLength = fromValue.Length();
        var toLength = toValue.Length();
        if (fromLength <= 1e-6f || toLength <= 1e-6f ||
            !float.IsFinite(maximumAngle) || maximumAngle < 0f)
        {
            return false;
        }
        var from = fromValue / fromLength;
        var to = toValue / toLength;
        var dot = Math.Clamp(from.Dot(to), -1f, 1f);
        var angle = MathF.Min(MathF.Acos(dot), maximumAngle);
        if (angle <= 1e-6f)
        {
            return true;
        }
        var axis = from.Cross(to);
        if (axis.LengthSquared() <= 1e-10f)
        {
            axis = DeterministicPerpendicular(from);
        }
        else
        {
            axis = axis.Normalized();
        }
        swing = new Quaternion(axis, angle).Normalized();
        return IsFinite(swing);
    }

    private static float QuaternionAngle(in Quaternion left, in Quaternion right)
    {
        var dot = MathF.Abs(left.Dot(right));
        return 2f * MathF.Acos(Math.Clamp(dot, 0f, 1f));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool WriteAffectedPose(ref bool writeStarted)
    {
        for (var orderIndex = 0; orderIndex < _scratch.AffectedCount; orderIndex++)
        {
            var boneId = _scratch.AffectedOrder[orderIndex];
            if (!_scratch.Modified[boneId])
            {
                continue;
            }
            var pose = _scratch.LocalPose[boneId];
            var position = pose.Origin;
            var rotation = pose.Basis.GetRotationQuaternion();
            var scale = pose.Basis.Scale;
            if (!IsFinite(position) || !IsFinite(rotation) || !IsFinite(scale) ||
                rotation.LengthSquared() <= 1e-12f)
            {
                return false;
            }
            if (_weightKinds[boneId] == WeightFoot)
            {
                _scratch.ResultPositions[boneId] = boneId == _footRig.PelvisBoneId
                    ? position
                    : _scratch.OriginalPositions[boneId];
                _scratch.ResultRotations[boneId] = boneId == _footRig.PelvisBoneId
                    ? _scratch.OriginalRotations[boneId]
                    : rotation.Normalized();
                _scratch.ResultScales[boneId] = _scratch.OriginalScales[boneId];
            }
            else
            {
                _scratch.ResultPositions[boneId] = position;
                _scratch.ResultRotations[boneId] = rotation.Normalized();
                _scratch.ResultScales[boneId] = scale;
            }
        }

        for (var orderIndex = 0; orderIndex < _scratch.AffectedCount; orderIndex++)
        {
            var boneId = _scratch.AffectedOrder[orderIndex];
            if (!_scratch.Modified[boneId])
            {
                continue;
            }
            writeStarted = true;
            SetBonePosePosition(boneId, _scratch.ResultPositions[boneId]);
            SetBonePoseRotation(boneId, _scratch.ResultRotations[boneId]);
            SetBonePoseScale(boneId, _scratch.ResultScales[boneId]);
        }
        return true;
    }

    private void ApplyTestAffineFixture()
    {
        if (_testAffineFixture == AlsPoseAffineTestFixture.None)
        {
            return;
        }
        var boneId = _scratch.AimAffectedOrder[0];
        var epsilon = _testAffineFixture is AlsPoseAffineTestFixture.NearSingularBase or
            AlsPoseAffineTestFixture.NearSingularAim
                ? 1e-7f
                : 0f;
        var invalidBasis = new Basis(
            Vector3.Right,
            new Vector3(1f, epsilon, 0f),
            Vector3.Back);
        if (_testAffineFixture is AlsPoseAffineTestFixture.SingularBase or
            AlsPoseAffineTestFixture.NearSingularBase)
        {
            _scratch.BaseLocalPose[boneId] = new Transform3D(
                invalidBasis,
                _scratch.BaseLocalPose[boneId].Origin);
            _scratch.BaseComponentPose[boneId] = new Transform3D(
                invalidBasis,
                _scratch.BaseComponentPose[boneId].Origin);
            _baseLocalInverses[boneId] = new Transform3D(invalidBasis, Vector3.Zero);
            _baseComponentInverses[boneId] = new Transform3D(invalidBasis, Vector3.Zero);
        }
        else
        {
            _scratch.DownLocalPose[boneId] = new Transform3D(
                invalidBasis,
                _scratch.DownLocalPose[boneId].Origin);
            _scratch.ForwardLocalPose[boneId] = new Transform3D(
                invalidBasis,
                _scratch.ForwardLocalPose[boneId].Origin);
            _scratch.UpLocalPose[boneId] = new Transform3D(
                invalidBasis,
                _scratch.UpLocalPose[boneId].Origin);
            _scratch.DownComponentPose[boneId] = new Transform3D(
                invalidBasis,
                _scratch.DownComponentPose[boneId].Origin);
            _scratch.ForwardComponentPose[boneId] = new Transform3D(
                invalidBasis,
                _scratch.ForwardComponentPose[boneId].Origin);
            _scratch.UpComponentPose[boneId] = new Transform3D(
                invalidBasis,
                _scratch.UpComponentPose[boneId].Origin);
        }
    }

    private bool FailAfterCapture(
        in Transform3D root,
        AlsP4ReasonCode failureReason,
        out AlsP4ReasonCode reason)
    {
        reason = TryRestoreOriginalPose(root)
            ? failureReason
            : AlsP4ReasonCode.PoseRestoreFailed;
        return false;
    }

    private bool TryRestoreOriginalPose(in Transform3D root)
    {
        var restored = true;
        try
        {
            _visualRoot.GlobalTransform = root;
        }
        catch
        {
            restored = false;
        }
        for (var boneId = 0; boneId < _scratch.BoneCount; boneId++)
        {
            try
            {
                SetBonePosePosition(boneId, _scratch.OriginalPositions[boneId]);
            }
            catch
            {
                restored = false;
            }
            try
            {
                SetBonePoseRotation(boneId, _scratch.OriginalRotations[boneId]);
            }
            catch
            {
                restored = false;
            }
            try
            {
                SetBonePoseScale(boneId, _scratch.OriginalScales[boneId]);
            }
            catch
            {
                restored = false;
            }
        }
        try
        {
            restored &= _visualRoot.GlobalTransform == root;
            for (var boneId = 0; boneId < _scratch.BoneCount; boneId++)
            {
                restored &= _skeleton.GetBonePosePosition(boneId) ==
                    _scratch.OriginalPositions[boneId];
                restored &= _skeleton.GetBonePoseRotation(boneId) ==
                    _scratch.OriginalRotations[boneId];
                restored &= _skeleton.GetBonePoseScale(boneId) ==
                    _scratch.OriginalScales[boneId];
            }
        }
        catch
        {
            restored = false;
        }
        return restored;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBonePosePosition(int boneId, in Vector3 value)
    {
        if (_testWriter is null)
        {
            _skeleton.SetBonePosePosition(boneId, value);
        }
        else
        {
            _testWriter.SetBonePosePosition(boneId, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBonePoseRotation(int boneId, in Quaternion value)
    {
        if (_testWriter is null)
        {
            _skeleton.SetBonePoseRotation(boneId, value);
        }
        else
        {
            _testWriter.SetBonePoseRotation(boneId, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBonePoseScale(int boneId, in Vector3 value)
    {
        if (_testWriter is null)
        {
            _skeleton.SetBonePoseScale(boneId, value);
        }
        else
        {
            _testWriter.SetBonePoseScale(boneId, value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private ulong ComputePoseDigest()
    {
        var digest = DigestOffsetBasis;
        for (var boneId = 0; boneId < _scratch.BoneCount; boneId++)
        {
            Append(ref digest, _skeleton.GetBonePosePosition(boneId));
            Append(ref digest, _skeleton.GetBonePoseRotation(boneId));
            Append(ref digest, _skeleton.GetBonePoseScale(boneId));
        }
        Append(ref digest, _visualRoot.GlobalTransform.Origin);
        return digest;
    }

    private static bool Validate(
        in AlsPoseModifierInput input,
        out AlsP4ReasonCode reason)
    {
        if (!IsUnit(input.AimPhase) || !IsUnit(input.AimDownWeight) ||
            !IsUnit(input.AimForwardWeight) || !IsUnit(input.AimUpWeight) ||
            !IsUnit(input.HeadWeight) || !IsUnit(input.SpineWeight) ||
            !IsUnit(input.UpperBodyWeight) || !IsUnit(input.ArmLocalWeight) ||
            !IsUnit(input.LeftFootIkWeight) || !IsUnit(input.RightFootIkWeight) ||
            !IsFinite(input.PelvisOffset) ||
            (input.LeftFootIkWeight > 0f && !IsValidFootTarget(input.LeftFootPose)) ||
            (input.RightFootIkWeight > 0f && !IsValidFootTarget(input.RightFootPose)))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }
        var aimWeight = input.AimDownWeight + input.AimForwardWeight + input.AimUpWeight;
        if (!float.IsFinite(aimWeight) || MathF.Abs(aimWeight - 1f) > 1e-5f ||
            input.InjectFailure is not (AlsPoseModifierFailureStage.None or
                AlsPoseModifierFailureStage.AfterAim or
                AlsPoseModifierFailureStage.AfterPelvis or
                AlsPoseModifierFailureStage.AfterLeftFoot))
        {
            reason = AlsP4ReasonCode.InvalidSettings;
            return false;
        }
        reason = AlsP4ReasonCode.None;
        return true;
    }

    private static Transform3D BlendAim(
        in Transform3D down,
        in Transform3D forward,
        in Transform3D up,
        float downWeight,
        float forwardWeight,
        float upWeight)
    {
        Transform3D result;
        var accumulated = downWeight + forwardWeight;
        if (accumulated > 0f)
        {
            result = downWeight <= 0f
                ? forward
                : forwardWeight <= 0f
                    ? down
                    : down.InterpolateWith(forward, forwardWeight / accumulated);
        }
        else
        {
            result = up;
        }
        return upWeight > 0f && accumulated > 0f
            ? result.InterpolateWith(up, upWeight)
            : result;
    }

    internal static bool TryComposeLocalResult(
        in Transform3D currentLocal,
        in Transform3D baseLocal,
        in Transform3D aimLocal,
        float weight,
        out Transform3D result)
    {
        result = default;
        if (!float.IsFinite(weight) || weight < 0f || weight > 1f ||
            !IsAffineInvertible(currentLocal) ||
            !IsAffineInvertible(aimLocal) ||
            !TryAffineInverse(baseLocal, out var baseInverse))
        {
            return false;
        }
        return TryComposeLocalResultFromInverse(
            in currentLocal,
            in baseInverse,
            in aimLocal,
            weight,
            out result);
    }

    private static bool TryComposeLocalResultFromInverse(
        in Transform3D currentLocal,
        in Transform3D baseInverse,
        in Transform3D aimLocal,
        float weight,
        out Transform3D result)
    {
        result = default;
        var delta = baseInverse * aimLocal;
        if (!IsAffineInvertible(delta))
        {
            return false;
        }
        result = currentLocal * Transform3D.Identity.InterpolateWith(delta, weight);
        return IsAffineInvertible(result);
    }

    private static bool TryAffineInverse(in Transform3D value, out Transform3D inverse)
    {
        inverse = default;
        if (!IsAffineInvertible(value))
        {
            return false;
        }
        inverse = value.AffineInverse();
        return IsAffineInvertible(inverse);
    }

    private static bool IsAffineInvertible(in Transform3D value)
    {
        if (!IsFinite(value))
        {
            return false;
        }
        var scaleX = value.Basis.X.Length();
        var scaleY = value.Basis.Y.Length();
        var scaleZ = value.Basis.Z.Length();
        var minimumScale = MathF.Min(scaleX, MathF.Min(scaleY, scaleZ));
        var maximumScale = MathF.Max(scaleX, MathF.Max(scaleY, scaleZ));
        var scaleProduct = scaleX * scaleY * scaleZ;
        var determinant = MathF.Abs(value.Basis.Determinant());
        return float.IsFinite(scaleProduct) && float.IsFinite(determinant) &&
            minimumScale >= MinimumAffineAxisScale &&
            maximumScale / minimumScale <= MaximumAffineAxisRatio &&
            determinant >= scaleProduct * MinimumRelativeDeterminant;
    }

    private static AlsAnimationDefinition GetAnimationDefinition(
        AlsAnimationSetDefinition set,
        int animationId)
    {
        if ((uint)animationId >= (uint)set.Animations.Length)
        {
            throw new InvalidOperationException($"P4 Aim animation ID is invalid: {animationId}");
        }
        return set.Animations[animationId];
    }

    private static Transform3D PoseTransform(
        in Vector3 position,
        in Quaternion rotation,
        in Vector3 scale) =>
        new(new Basis(rotation.Normalized()).Scaled(scale), position);

    private static bool IsUnit(float value) =>
        float.IsFinite(value) && value >= 0f && value <= 1f;

    private static bool IsFinite(in Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(in Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool IsFinite(in Basis value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(in Transform3D value) =>
        IsFinite(value.Basis) && IsFinite(value.Origin);

    private static bool IsFinite(in System.Numerics.Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(in System.Numerics.Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool IsValidFootTarget(in AlsFootPoseOutput target) =>
        IsFinite(target.Position) && IsFinite(target.Rotation) &&
        target.Rotation.LengthSquared() > 1e-12f &&
        IsUnit(target.LockAmount) && target.PlatformId >= -1;

    private static Vector3 ToGodot(in System.Numerics.Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static Quaternion ToGodot(in System.Numerics.Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    private static void Append(ref ulong digest, float value)
    {
        var bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        for (var shift = 0; shift < 32; shift += 8)
        {
            digest ^= (byte)(bits >> shift);
            digest *= DigestPrime;
        }
    }

    private static void Append(ref ulong digest, in Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, in Quaternion value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
        Append(ref digest, value.W);
    }

    private sealed class ClipBinding : IDisposable
    {
        public ClipBinding(Godot.Animation animation, Skeleton3D skeleton, int boneCount)
        {
            Animation = animation;
            PositionTracks = new int[boneCount];
            RotationTracks = new int[boneCount];
            ScaleTracks = new int[boneCount];
            Array.Fill(PositionTracks, -1);
            Array.Fill(RotationTracks, -1);
            Array.Fill(ScaleTracks, -1);
            for (var trackIndex = 0; trackIndex < animation.GetTrackCount(); trackIndex++)
            {
                using var path = animation.TrackGetPath(trackIndex);
                var pathText = path.ToString();
                var separator = pathText.LastIndexOf(':');
                if (separator < 0 || separator == pathText.Length - 1)
                {
                    throw new InvalidOperationException($"P4 Aim track has no exact bone: {pathText}");
                }
                var boneId = skeleton.FindBone(pathText[(separator + 1)..]);
                if (boneId < 0)
                {
                    throw new InvalidOperationException($"P4 Aim track bone is absent: {pathText}");
                }
                var destination = animation.TrackGetType(trackIndex) switch
                {
                    Godot.Animation.TrackType.Position3D => PositionTracks,
                    Godot.Animation.TrackType.Rotation3D => RotationTracks,
                    Godot.Animation.TrackType.Scale3D => ScaleTracks,
                    var type => throw new InvalidOperationException(
                        $"P4 Aim track type is unsupported: {type}"),
                };
                if (destination[boneId] >= 0)
                {
                    throw new InvalidOperationException(
                        $"P4 Aim clip duplicates a bone track: bone={boneId} type={animation.TrackGetType(trackIndex)}");
                }
                destination[boneId] = trackIndex;
            }
        }

        public Godot.Animation Animation { get; }

        public int[] PositionTracks { get; }

        public int[] RotationTracks { get; }

        public int[] ScaleTracks { get; }

        public void Dispose() => Animation.Dispose();
    }
}
