using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public interface IAlsMontagePoseSource
{
    // Must sample the additive delta for additive assets, including its authored
    // base pose and curve subtraction. Sampling never advances an instance.
    void Sample(in AlsMontageEvaluation entry, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves);
}

// The optimized ALS reference and the current original Lyra weapon reference
// produce different float rounding. Select the captured native arithmetic at
// the owner boundary; do not silently change already verified ALS instances.
public enum AlsMontageWeightNormalization { SharedReciprocal, IndividualDivision }

// UE SlotEvaluatePose without blend profiles: mix non-additives, then apply
// local/mesh additive deltas in montage evaluation order. Exclusive scratch owner.
public sealed class AlsMontageSlotPose
{
    private readonly AlsPrecisePose[] _reference, _sample, _result;
    private readonly AlsInertialCurve[] _sampleCurves, _resultCurves;
    private readonly int[] _parents;
    private readonly AlsQuaternion[] _rotations;
    private readonly AlsMontageWeightNormalization _normalization;
    private bool _busy;
    public AlsMontageSlotPose(ReadOnlySpan<AlsPrecisePose> reference, ReadOnlySpan<int> parents, int curveCount,
        AlsMontageWeightNormalization normalization = AlsMontageWeightNormalization.SharedReciprocal)
    {
        if (reference.IsEmpty || parents.Length != reference.Length || curveCount < 0 || (uint)normalization > 1) throw new ArgumentException("Invalid montage pose layout.");
        for (var bone = 0; bone < parents.Length; bone++)
            if (parents[bone] < -1 || parents[bone] >= bone) throw new ArgumentException("Montage poses require parent-first bones.");
        foreach (var pose in reference) pose.Validate();
        _reference = reference.ToArray(); _parents = parents.ToArray(); _sample = new AlsPrecisePose[reference.Length]; _result = new AlsPrecisePose[reference.Length];
        _sampleCurves = new AlsInertialCurve[curveCount]; _resultCurves = new AlsInertialCurve[curveCount]; _rotations = new AlsQuaternion[reference.Length * 2];
        _normalization = normalization;
    }
    public void Evaluate(AlsMontageFrame frame, in AlsFrameIdentity identity, AlsMontageSlot slot,
        ReadOnlySpan<AlsPrecisePose> source, ReadOnlySpan<AlsInertialCurve> sourceCurves,
        Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves, IAlsMontagePoseSource sampler)
    {
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(sampler);
        if (_busy || identity.SlotGeneration == 0 || frame.Identity != identity || !slot.IsValid ||
            output.Length != _reference.Length || curves.Length != _resultCurves.Length ||
            !source.IsEmpty && source.Length != output.Length || !sourceCurves.IsEmpty && sourceCurves.Length != curves.Length)
            throw new ArgumentException("Stale montage frame or invalid pose buffers.");
        var weights = frame.SlotWeights(slot);
        if (weights.SourceWeight > AlsPoseBlender.WeightThreshold && (source.Length != output.Length || sourceCurves.Length != curves.Length))
            throw new ArgumentException("A relevant montage source cannot be replaced by a reference pose.");
        Validate(source, sourceCurves);
        foreach (var entry in frame.Evaluations)
            if (entry.Slot == slot && ((uint)entry.AdditiveType > 2 || !float.IsFinite(entry.Weight) || entry.Weight is < 0 or > 1))
                throw new ArgumentException("Invalid montage evaluation policy.");
        _busy = true;
        try
        {
            if (weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold) { source.CopyTo(output); sourceCurves.CopyTo(curves); return; }
            var denominator = weights.TotalNodeWeight > 1 + AlsPoseBlender.WeightThreshold ? weights.TotalNodeWeight : 1;
            var weightScale = 1f / denominator;
            float Weight(float value) => _normalization == AlsMontageWeightNormalization.IndividualDivision ? value / denominator : value * weightScale;
            var first = true; var nonAdditiveCount = 0; _resultCurves.AsSpan().Clear();
            foreach (var entry in frame.Evaluations)
            {
                if (entry.Slot != slot || entry.AdditiveType != 0) continue;
                nonAdditiveCount++;
                Sample(entry); var weight = Weight(entry.Weight);
                for (var bone = 0; bone < output.Length; bone++)
                    _result[bone] = first ? AlsPrecisePoseBlender.Scale(_sample[bone], weight) : AlsPrecisePoseBlender.Accumulate(_result[bone], _sample[bone], weight);
                for (var c = 0; c < curves.Length; c++)
                    _resultCurves[c] = first ? AlsStandingCycleCurves.Scale(_sampleCurves[c], weight) : AlsStandingCycleCurves.Accumulate(_resultCurves[c], _sampleCurves[c], weight);
                first = false;
            }
            if (first)
            {
                if (weights.SourceWeight > AlsPoseBlender.WeightThreshold) { source.CopyTo(_result); sourceCurves.CopyTo(_resultCurves); }
                else _reference.CopyTo(_result, 0);
            }
            else
            {
                // UE BlendPosesTogetherIndirect only normalizes with two or more
                // poses. Preserve raw-key rotation length in its single-pose path.
                var hasSource = weights.SourceWeight > AlsPoseBlender.WeightThreshold;
                for (var bone = 0; bone < output.Length; bone++)
                {
                    if (hasSource) _result[bone] = AlsPrecisePoseBlender.Accumulate(_result[bone], source[bone], weights.SourceWeight);
                    if (hasSource || nonAdditiveCount > 1) _result[bone] = _result[bone].Normalized();
                }
                if (hasSource)
                    for (var c = 0; c < curves.Length; c++) _resultCurves[c] = AlsStandingCycleCurves.Accumulate(_resultCurves[c], sourceCurves[c], weights.SourceWeight);
            }
            foreach (var entry in frame.Evaluations)
            {
                if (entry.Slot != slot || entry.AdditiveType == 0) continue;
                Sample(entry); var weight = Weight(entry.Weight);
                if (entry.AdditiveType == 2) AlsPrecisePoseBlender.MeshApply(_result, _sample, _parents, _rotations, _result, weight);
                else for (var bone = 0; bone < output.Length; bone++) _result[bone] = AlsPrecisePoseBlender.LocalApply(_result[bone], _sample[bone], weight);
                AlsLayeringCurves.Apply(_resultCurves, _sampleCurves, weight, _resultCurves);
            }
            _result.CopyTo(output); _resultCurves.CopyTo(curves);
        }
        finally { _busy = false; }
        void Sample(in AlsMontageEvaluation entry)
        {
            sampler.Sample(entry, _sample, _sampleCurves); Validate(_sample, _sampleCurves);
        }
    }
    private static void Validate(ReadOnlySpan<AlsPrecisePose> poses, ReadOnlySpan<AlsInertialCurve> curves)
    {
        foreach (var pose in poses) pose.Validate();
        foreach (var curve in curves) if (curve.Present && !float.IsFinite(curve.Value)) throw new ArgumentException("Non-finite montage curve.");
    }
}
