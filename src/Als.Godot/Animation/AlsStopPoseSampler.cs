using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace GodotAls.Animation;

internal readonly record struct AlsStopSelectorState(bool LeftInitialized, bool RightInitialized,
    float LeftAlternate, float RightAlternate);

/// <summary>Fixed Plant evaluators. No playback clock, event dispatch, or outer Stop state machine.</summary>
internal sealed class AlsStopPoseSampler
{
    private readonly AlsStopPoseProfile _profile;
    private readonly int[] _parents;
    private readonly float[][] _masks;
    private readonly AlsLocalPose[] _samples;
    private readonly AlsLocalPose[] _layerScratch;
    private readonly NQuaternion[] _rotationScratch;
    private readonly Dictionary<string, float>[] _curves;
    private readonly int _bones;
    private readonly AlsPrecisePose[] _preciseSamples, _preciseLayerScratch;
    private readonly AlsQuaternion[] _preciseRotationScratch;

    public AlsStopPoseSampler(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set, AlsStopPoseProfile profile)
    {
        _profile = profile;
        var poseSources = library.MovementSources(set, profile.SkeletonId);
        _bones = poseSources.BoneCount; _parents = poseSources.Parents.ToArray();
        var rest = poseSources.ReferencePose.ToArray();
        _preciseSamples = new AlsPrecisePose[12 * _bones]; _preciseLayerScratch = new AlsPrecisePose[_bones];
        _preciseRotationScratch = new AlsQuaternion[_bones * 3];
        _samples = new AlsLocalPose[12 * _bones];
        _layerScratch = new AlsLocalPose[_bones];
        _rotationScratch = new NQuaternion[_bones * 3];
        _curves = new Dictionary<string, float>[12];
        _masks = [new float[_bones], new float[_bones]];
        for (var side = 0; side < 2; side++)
        {
            var plant = side == 0 ? profile.Left : profile.Right;
            foreach (var bone in plant.AffectedLogicalIds) _masks[side][bone] = 1;
            for (var source = 0; source < AlsStopPlantComposer.SampleCount; source++)
            {
                var slot = side * 6 + source;
                var sample = plant.Samples[source];
                if (!library.ClipNames.TryGetValue(sample.AnimationId, out var name))
                    throw new InvalidOperationException("Plant source is absent from the animation closure.");
                using var clip = poseSources.Create(sample.AnimationId);
                clip.Sample(rest, sample.TimeSeconds, _samples.AsSpan(slot * _bones, _bones));
                clip.SamplePrecise(sample.TimeSeconds, _preciseSamples.AsSpan(slot * _bones, _bones));
                var animation = set.Animations[sample.AnimationId];
                _curves[slot] = new(StringComparer.OrdinalIgnoreCase);
                foreach (var curve in animation.Curves)
                {
                    var value = clip.Curve(sample.TimeSeconds, curve.SourceName);
                    if (value.Present) _curves[slot].Add(curve.SourceName, value.Value);
                }
            }
        }
    }

    internal ReadOnlySpan<AlsLocalPose> FixedSample(bool leftFoot, int source)
    {
        if ((uint)source >= 6) throw new ArgumentOutOfRangeException(nameof(source));
        return _samples.AsSpan(((leftFoot ? 0 : 6) + source) * _bones, _bones);
    }

    public AlsStopSelectorState Prepare(bool leftFoot, in AlsStopSelectorState previous,
        int trackedHips, NVector4 velocity, float delta)
    {
        if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        Span<float> weights = stackalloc float[6];
        AlsStopPlantComposer.SampleWeights(velocity, previous.LeftAlternate, previous.RightAlternate, weights);
        var plant = leftFoot ? _profile.Left : _profile.Right;
        var next = previous;
        if (weights[2] + weights[3] > AlsPoseBlender.WeightThreshold)
            next = next with { LeftInitialized = true, LeftAlternate = Advance(previous.LeftInitialized,
                previous.LeftAlternate, trackedHips == plant.LeftSelector.AlternateHipsValue, plant.LeftSelector, delta) };
        if (weights[4] + weights[5] > AlsPoseBlender.WeightThreshold)
            next = next with { RightInitialized = true, RightAlternate = Advance(previous.RightInitialized,
                previous.RightAlternate, trackedHips == plant.RightSelector.AlternateHipsValue, plant.RightSelector, delta) };
        return next;
    }

    public void Compose(bool leftFoot, in AlsStopSelectorState state, NVector4 velocity,
        ReadOnlySpan<AlsLocalPose> basis, Span<AlsLocalPose> output) =>
        AlsStopPlantComposer.Compose(basis, _samples.AsSpan((leftFoot ? 0 : 6) * _bones, 6 * _bones), velocity,
            state.LeftAlternate, state.RightAlternate, _parents, _masks[leftFoot ? 0 : 1],
            _layerScratch, _rotationScratch, output);

    public float SampleCurve(bool leftFoot, in AlsStopSelectorState state, NVector4 velocity, string name, float baseValue) =>
        SampleCurveWithPresence(leftFoot, state, velocity, name, new(baseValue)).Value;

    public AlsInertialCurve SampleCurveWithPresence(bool leftFoot, in AlsStopSelectorState state, NVector4 velocity, string name, AlsInertialCurve baseValue)
    {
        var plant = leftFoot ? _profile.Left : _profile.Right;
        var producerName = AlsRefactoredV4SourceCurves.V4ProducerName(name);
        if (producerName == plant.FootLockCurve) return new(plant.FootLockValue);
        Span<float> weights = stackalloc float[6];
        AlsStopPlantComposer.SampleWeights(velocity, state.LeftAlternate, state.RightAlternate, weights);
        var present = false;
        var value = 0f;
        for (var i = 0; i < 6; i++)
            if (weights[i] > 0 && _curves[(leftFoot ? 0 : 6) + i].TryGetValue(producerName, out var source))
            {
                present = true;
                value += weights[i] * source;
            }
        // Override combines named curves, preserving base-only curves, then ModifyCurve writes FootLock.
        return present ? new(value) : baseValue;
    }

    private static float Advance(bool initialized, float weight, bool alternate, AlsStopLateralSelector settings, float delta)
    {
        var target = alternate ? 1f : 0f;
        var seconds = alternate ? settings.AlternateBlendSeconds : settings.DefaultBlendSeconds;
        // The first relevant update snaps. Subsequent linear blends scale duration by remaining weight.
        return !initialized || seconds == 0 ? target : Mathf.MoveToward(weight, target, delta / seconds);
    }


    public void Compose(bool leftFoot, in AlsStopSelectorState state, NVector4 velocity,
        ReadOnlySpan<AlsPrecisePose> basis, Span<AlsPrecisePose> output) =>
        AlsStopPlantComposer.Compose(basis, _preciseSamples.AsSpan((leftFoot ? 0 : 6) * _bones, 6 * _bones), velocity,
            state.LeftAlternate, state.RightAlternate, _parents, _masks[leftFoot ? 0 : 1],
            _preciseLayerScratch, _preciseRotationScratch, output);

}
