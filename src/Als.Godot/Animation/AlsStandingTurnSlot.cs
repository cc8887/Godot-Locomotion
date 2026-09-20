using GodotAls.Core.Locomotion;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Legacy callers supply two controller banks; mapped frames supply the complete
// dynamic montage evaluation bank with an identity checked before pose sampling.
internal readonly record struct AlsStandingTurnSlotInput(int AnimationA, int AnimationB,
    float TimeASeconds, float TimeBSeconds, float BankBlend, float Amount,
    AlsMontageFrame? MontageFrame = null, AlsFrameIdentity MontageIdentity = default,
    AlsMontageSlot Slot = default, float RotationScale = 1);

internal sealed class AlsStandingTurnSlot : IDisposable
{
    private readonly Dictionary<int, AlsMovementAnimationSource> _clips;
    private readonly AlsLocalPose[] _a, _b;
    private readonly AlsPrecisePose[] _aPrecise, _bPrecise;

    public AlsStandingTurnSlot(AlsAnimationLibraryBuildResult library, AlsAnimationSetDefinition set,
        AlsPoseAnimationProfile? profile, ReadOnlySpan<int> actionAnimations = default)
    {
        // Keep crouching assets available while the existing turn channel fades across stance changes.
        var ids = (profile?.Turns.Select(t => t.AnimationId) ?? []).Concat(actionAnimations.ToArray()).Distinct().ToArray();
        if (ids.Any(id => set.Animations[id].AdditiveType != 0))
            throw new InvalidOperationException("The existing turn channel requires non-additive slot poses.");
        var poseSources = library.MovementSources(set, ids.Length > 0 ? set.Animations[ids[0]].SkeletonId : 0);
        _clips = ids.ToDictionary(id => id, id => poseSources.Create(id));
        _aPrecise = new AlsPrecisePose[poseSources.BoneCount]; _bPrecise = new AlsPrecisePose[_aPrecise.Length];
        _a = new AlsLocalPose[poseSources.BoneCount]; _b = new AlsLocalPose[_a.Length];
    }

    public void Compose(in AlsStandingTurnSlotInput input, ReadOnlySpan<AlsLocalPose> rest, Span<AlsLocalPose> source)
    {
        Validate(input);
        if (input.MontageFrame is not null)
        {
            var weights = input.MontageFrame.SlotWeights(input.Slot);
            if (weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold) return;
            source.CopyTo(_b); var first = true;
            foreach (var entry in input.MontageFrame.Evaluations)
            {
                if (entry.Slot != input.Slot) continue;
                var clip = _clips[entry.AnimationId]; clip.SampleSourceSeconds(rest, entry.Position, (float)clip.Length, _a);
                var weight = entry.Weight / MathF.Max(weights.TotalNodeWeight, 1);
                for (var bone = 0; bone < source.Length; bone++)
                    source[bone] = first ? AlsPoseBlender.Scale(_a[bone], weight) : AlsPoseBlender.Accumulate(source[bone], _a[bone], weight);
                first = false;
            }
            var montageSourceWeight = weights.SourceWeight > AlsPoseBlender.WeightThreshold ? weights.SourceWeight : 0;
            for (var bone = 0; bone < source.Length; bone++)
                source[bone] = AlsPoseBlender.Normalize(AlsPoseBlender.Accumulate(source[bone], _b[bone], montageSourceWeight));
            return;
        }
        if (input.Amount <= AlsPoseBlender.WeightThreshold) return;
        var a = _clips[input.AnimationA]; var b = _clips[input.AnimationB];
        a.SampleSourceSeconds(rest, input.TimeASeconds, (float)a.Length, _a);
        b.SampleSourceSeconds(rest, input.TimeBSeconds, (float)b.Length, _b);
        // Slot blends montage poses first, followed by its source. Normalize once after accumulation.
        var weightA = input.Amount * (1 - input.BankBlend); var weightB = input.Amount * input.BankBlend;
        var sourceWeight = 1 - input.Amount > AlsPoseBlender.WeightThreshold ? 1 - input.Amount : 0;
        for (var bone = 0; bone < source.Length; bone++)
            source[bone] = AlsPoseBlender.Normalize(AlsPoseBlender.Accumulate(
                AlsPoseBlender.Accumulate(AlsPoseBlender.Scale(_a[bone], weightA), _b[bone], weightB), source[bone], sourceWeight));
    }

    public float Curve(in AlsStandingTurnSlotInput input, string name, float source)
    {
        if (input.MontageFrame is not null)
        {
            Validate(input); var weights = input.MontageFrame.SlotWeights(input.Slot);
            if (weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold) return source;
            var value = 0f;
            foreach (var entry in input.MontageFrame.Evaluations)
                if (entry.Slot == input.Slot) value += Sample(entry.AnimationId, entry.Position, name) * entry.Weight / MathF.Max(weights.TotalNodeWeight, 1);
            return value + (weights.SourceWeight > AlsPoseBlender.WeightThreshold ? source * weights.SourceWeight : 0);
        }
        if (input.Amount <= AlsPoseBlender.WeightThreshold) return source;
        var a = Sample(input.AnimationA, input.TimeASeconds, name); var b = Sample(input.AnimationB, input.TimeBSeconds, name);
        var sourceWeight = 1 - input.Amount > AlsPoseBlender.WeightThreshold ? 1 - input.Amount : 0;
        return (a * (1 - input.BankBlend) + b * input.BankBlend) * input.Amount + source * sourceWeight;
    }

    public AlsInertialCurve CurveWithPresence(in AlsStandingTurnSlotInput input, string name, in AlsInertialCurve source)
    {
        Validate(input);
        if (input.MontageFrame is null)
        {
            if (input.Amount <= AlsPoseBlender.WeightThreshold) return source;
            var mixed = AlsStandingCycleCurves.Lerp(_clips[input.AnimationA].Curve(input.TimeASeconds, name),
                _clips[input.AnimationB].Curve(input.TimeBSeconds, name), input.BankBlend);
            var sourceWeight = 1 - input.Amount > AlsPoseBlender.WeightThreshold ? 1 - input.Amount : 0;
            return AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(mixed, input.Amount), source, sourceWeight);
        }
        var weights = input.MontageFrame.SlotWeights(input.Slot);
        if (weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold) return source;
        var value = weights.SourceWeight > AlsPoseBlender.WeightThreshold
            ? AlsStandingCycleCurves.Scale(source, weights.SourceWeight) : default;
        foreach (var entry in input.MontageFrame.Evaluations)
            if (entry.Slot == input.Slot)
                value = AlsStandingCycleCurves.Accumulate(value, _clips[entry.AnimationId].Curve(entry.Position, name),
                    entry.Weight / MathF.Max(weights.TotalNodeWeight, 1));
        return value;
    }

    public void Validate(in AlsStandingTurnSlotInput input)
    {
        if (input.MontageFrame is not null)
        {
            if (input.MontageIdentity.SlotGeneration == 0 || input.MontageIdentity != input.MontageFrame.Identity || input.Slot.Id is < 0 or > 3 ||
                !float.IsFinite(input.RotationScale)) throw new ArgumentException("Stale or invalid montage pose bank.");
            foreach (var entry in input.MontageFrame.Evaluations)
                if (entry.Slot == input.Slot && (entry.AdditiveType != 0 || !_clips.TryGetValue(entry.AnimationId, out var clip) || !float.IsFinite(entry.Position) || entry.Position < 0 ||
                    entry.Position > (float)clip.Length || !float.IsFinite(entry.Weight) || entry.Weight is <= 0 or > 1))
                    throw new ArgumentException("Invalid montage evaluation contribution.");
            return;
        }
        if (!float.IsFinite(input.Amount) || input.Amount is < 0 or > 1 || !float.IsFinite(input.BankBlend) || input.BankBlend is < 0 or > 1 ||
            !float.IsFinite(input.TimeASeconds) || input.TimeASeconds < 0 || !float.IsFinite(input.TimeBSeconds) || input.TimeBSeconds < 0 ||
            input.Amount > AlsPoseBlender.WeightThreshold && (!_clips.TryGetValue(input.AnimationA, out var a) || !_clips.TryGetValue(input.AnimationB, out var b) ||
                input.TimeASeconds > (float)a.Length || input.TimeBSeconds > (float)b.Length))
            throw new ArgumentException("Invalid Standing turn slot input.");
    }

    private float Sample(int id, float seconds, string name) => _clips[id].Curve(seconds, name).Value;
    public void Dispose() { foreach (var clip in _clips.Values) clip.Dispose(); }

    public void Compose(in AlsStandingTurnSlotInput input, ReadOnlySpan<AlsPrecisePose> rest, Span<AlsPrecisePose> source)
    {
        Validate(input);
        if (input.MontageFrame is not null)
        {
            var weights = input.MontageFrame.SlotWeights(input.Slot);
            if (weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold) return;
            source.CopyTo(_bPrecise); var first = true;
            foreach (var entry in input.MontageFrame.Evaluations)
            {
                if (entry.Slot != input.Slot) continue;
                var clip = _clips[entry.AnimationId]; clip.SampleSourceSeconds(rest, entry.Position, (float)clip.Length, _aPrecise);
                var weight = entry.Weight / MathF.Max(weights.TotalNodeWeight, 1);
                for (var bone = 0; bone < source.Length; bone++)
                    source[bone] = first ? AlsPrecisePoseBlender.Scale(_aPrecise[bone], weight) : AlsPrecisePoseBlender.Accumulate(source[bone], _aPrecise[bone], weight);
                first = false;
            }
            var montageSourceWeight = weights.SourceWeight > AlsPoseBlender.WeightThreshold ? weights.SourceWeight : 0;
            for (var bone = 0; bone < source.Length; bone++)
                source[bone] = AlsPrecisePoseBlender.Normalize(AlsPrecisePoseBlender.Accumulate(source[bone], _bPrecise[bone], montageSourceWeight));
            return;
        }
        if (input.Amount <= AlsPoseBlender.WeightThreshold) return;
        var a = _clips[input.AnimationA]; var b = _clips[input.AnimationB];
        a.SampleSourceSeconds(rest, input.TimeASeconds, (float)a.Length, _aPrecise);
        b.SampleSourceSeconds(rest, input.TimeBSeconds, (float)b.Length, _bPrecise);
        // Slot blends montage poses first, followed by its source. Normalize once after accumulation.
        var weightA = input.Amount * (1 - input.BankBlend); var weightB = input.Amount * input.BankBlend;
        var sourceWeight = 1 - input.Amount > AlsPoseBlender.WeightThreshold ? 1 - input.Amount : 0;
        for (var bone = 0; bone < source.Length; bone++)
            source[bone] = AlsPrecisePoseBlender.Normalize(AlsPrecisePoseBlender.Accumulate(
                AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(_aPrecise[bone], weightA), _bPrecise[bone], weightB), source[bone], sourceWeight));
    }
}
