using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using System.Runtime.CompilerServices;

namespace GodotAls.Animation;

public enum AlsPoseModifierFailureStage : byte
{
    None,
    AfterAim,
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
            AlsPoseModifierFailureStage.None);
    }
}

public struct AlsPoseModifierOutput
{
    public ulong PoseDigest;
    public long DeterministicElapsedTicks;
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

    private readonly Skeleton3D _skeleton;
    private readonly Node3D _visualRoot;
    private readonly AlsPoseScratch _scratch;
    private readonly Transform3D[] _rests;
    private readonly byte[] _weightKinds;
    private readonly ClipBinding _base;
    private readonly ClipBinding _down;
    private readonly ClipBinding _forward;
    private readonly ClipBinding _up;
    private readonly double _baseTime;
    private readonly int _baseAnimationId;
    private readonly IAlsSkeletonPoseWriter? _testWriter;
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
        IAlsSkeletonPoseWriter? testWriter)
    {
        _skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        _visualRoot = visualRoot ?? throw new ArgumentNullException(nameof(visualRoot));
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(animationSet);
        ArgumentNullException.ThrowIfNull(profile);
        _testWriter = testWriter;
        if (!GodotObject.IsInstanceValid(skeleton) || !GodotObject.IsInstanceValid(visualRoot) ||
            profile.SkeletonId < 0 || profile.SkeletonId >= animationSet.Skeletons.Length)
        {
            throw new InvalidOperationException("Pose modifier received an invalid runtime rig or profile.");
        }

        var skeletonDefinition = animationSet.Skeletons[profile.SkeletonId];
        AlsAnimationBinder.ValidateTargetSkeleton(skeleton, skeletonDefinition, "P4 component pose modifier");
        _scratch = new AlsPoseScratch(skeleton);
        _rests = new Transform3D[_scratch.BoneCount];
        _weightKinds = new byte[_scratch.BoneCount];
        for (var boneId = 0; boneId < _rests.Length; boneId++)
        {
            _rests[boneId] = skeleton.GetBoneRest(boneId);
            var expectedParent = skeletonDefinition.PhysicalBones[boneId].ParentPhysicalId;
            if (_scratch.Parents[boneId] != expectedParent || !IsFinite(_rests[boneId]))
            {
                throw new InvalidOperationException(
                    $"P4 modifier skeleton parent/rest mismatch: bone={boneId}");
            }
        }

        CompileAimMasks(profile, skeletonDefinition);
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
    }

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

        try
        {
            long deterministicTicks = _scratch.BoneCount * 4L;
            var hasInfluence = input.HeadWeight > 0f ||
                input.SpineWeight > 0f || input.UpperBodyWeight > 0f;
            if (hasInfluence)
            {
                if (!SampleAimPoses(in input) || !ApplyAim(in input))
                {
                    return FailAfterCapture(
                        capturedRoot, AlsP4ReasonCode.NonFiniteInput, out reason);
                }
                deterministicTicks += _scratch.BoneCount * 20L;
                deterministicTicks += _scratch.AffectedCount * 8L;
            }

            if (input.InjectFailure == AlsPoseModifierFailureStage.AfterAim)
            {
                return FailAfterCapture(
                    capturedRoot, AlsP4ReasonCode.InvalidRuntimeState, out reason);
            }

            var writeTransactions = 0;
            if (hasInfluence)
            {
                if (!WriteAffectedPose())
                {
                    return FailAfterCapture(
                        capturedRoot, AlsP4ReasonCode.NonFiniteInput, out reason);
                }
                writeTransactions = 1;
                deterministicTicks += _scratch.AffectedCount * 4L;
            }

            var poseDigest = ComputePoseDigest();
            deterministicTicks += _scratch.BoneCount * 4L;
            var candidate = new AlsPoseModifierOutput
            {
                PoseDigest = poseDigest,
                DeterministicElapsedTicks = deterministicTicks,
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
            if (_skeleton.GetBoneParent(boneId) != parent ||
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
            }
        }
        if (orderedCount != count)
        {
            throw new InvalidOperationException("P4 Aim mask ordering failed.");
        }
        _scratch.AffectedCount = count;
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
            if (!IsFinite(pose))
            {
                return false;
            }
            _scratch.OriginalPose[boneId] = pose;
            _scratch.LocalPose[boneId] = _rests[boneId] * pose;
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
    private bool SampleAimPoses(in AlsPoseModifierInput input)
    {
        var downTime = _down.Animation.Length * input.AimPhase;
        var forwardTime = _forward.Animation.Length * input.AimPhase;
        var upTime = _up.Animation.Length * input.AimPhase;
        return SampleClip(_base, _baseTime, _scratch.BaseLocalPose, _scratch.BaseComponentPose) &&
            SampleClip(_down, downTime, _scratch.DownLocalPose, _scratch.DownComponentPose) &&
            SampleClip(_forward, forwardTime, _scratch.ForwardLocalPose, _scratch.ForwardComponentPose) &&
            SampleClip(_up, upTime, _scratch.UpLocalPose, _scratch.UpComponentPose);
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
                : Vector3.Zero;
            var rotation = clip.RotationTracks[boneId] >= 0
                ? clip.Animation.RotationTrackInterpolate(clip.RotationTracks[boneId], time)
                : Quaternion.Identity;
            var scale = clip.ScaleTracks[boneId] >= 0
                ? clip.Animation.ScaleTrackInterpolate(clip.ScaleTracks[boneId], time)
                : Vector3.One;
            if (!IsFinite(position) || !IsFinite(rotation) || !IsFinite(scale) ||
                rotation.LengthSquared() <= 1e-12f)
            {
                return false;
            }
            var pose = PoseTransform(position, rotation, scale);
            locals[boneId] = _rests[boneId] * pose;
            if (!IsFinite(locals[boneId]))
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
            if (!IsFinite(components[boneId]))
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
        for (var orderIndex = 0; orderIndex < _scratch.AffectedCount; orderIndex++)
        {
            var boneId = _scratch.AffectedOrder[orderIndex];
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
            if (!IsFinite(aimComponent) || !IsFinite(aimLocal))
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
            var localDelta = aimLocal * _scratch.BaseLocalPose[boneId].AffineInverse();
            var componentDelta = aimComponent *
                _scratch.BaseComponentPose[boneId].AffineInverse();
            Transform3D resultComponent;
            if (_weightKinds[boneId] is WeightArm or WeightHand)
            {
                var localResult = Transform3D.Identity.InterpolateWith(localDelta, weight) *
                    _scratch.LocalPose[boneId];
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
            if (!IsFinite(localDelta) || !IsFinite(componentDelta) ||
                !IsFinite(resultComponent))
            {
                return false;
            }
            _scratch.ComponentPose[boneId] = resultComponent;
            _scratch.LocalPose[boneId] = parent < 0
                ? resultComponent
                : parentComponent.AffineInverse() * resultComponent;
            if (!IsFinite(_scratch.LocalPose[boneId]))
            {
                return false;
            }
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private bool WriteAffectedPose()
    {
        for (var orderIndex = 0; orderIndex < _scratch.AffectedCount; orderIndex++)
        {
            var boneId = _scratch.AffectedOrder[orderIndex];
            var pose = _rests[boneId].AffineInverse() * _scratch.LocalPose[boneId];
            var position = pose.Origin;
            var rotation = pose.Basis.GetRotationQuaternion();
            var scale = pose.Basis.Scale;
            if (!IsFinite(position) || !IsFinite(rotation) || !IsFinite(scale) ||
                rotation.LengthSquared() <= 1e-12f)
            {
                return false;
            }
            _scratch.ResultPositions[boneId] = position;
            _scratch.ResultRotations[boneId] = rotation.Normalized();
            _scratch.ResultScales[boneId] = scale;
        }

        for (var orderIndex = 0; orderIndex < _scratch.AffectedCount; orderIndex++)
        {
            var boneId = _scratch.AffectedOrder[orderIndex];
            SetBonePosePosition(boneId, _scratch.ResultPositions[boneId]);
            SetBonePoseRotation(boneId, _scratch.ResultRotations[boneId]);
            SetBonePoseScale(boneId, _scratch.ResultScales[boneId]);
        }
        return true;
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
            !IsUnit(input.UpperBodyWeight) || !IsUnit(input.ArmLocalWeight))
        {
            reason = AlsP4ReasonCode.NonFiniteInput;
            return false;
        }
        var aimWeight = input.AimDownWeight + input.AimForwardWeight + input.AimUpWeight;
        if (!float.IsFinite(aimWeight) || MathF.Abs(aimWeight - 1f) > 1e-5f ||
            input.InjectFailure is not (AlsPoseModifierFailureStage.None or
                AlsPoseModifierFailureStage.AfterAim))
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
