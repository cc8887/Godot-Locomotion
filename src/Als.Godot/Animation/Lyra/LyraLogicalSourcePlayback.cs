using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

// Complete source poses, including control channels, share the current demo's
// crossfade. The selected player time comes from the existing motion host.
// Retired occurrences advance at the AnimationPlayer's existing rate (one).
// Original UE graph inertialization/Sync traversal is still a separate gate.
internal sealed class LyraLogicalSourcePlayback
{
    private sealed class Retired(LyraLogicalSourceSampler sampler, string slot, double time,
        float weight, double duration)
    {
        public readonly LyraLogicalSourceSampler Sampler = sampler;
        public readonly string Slot = slot;
        public double Time = time, Remaining = duration;
        public readonly float Weight = weight;
        public readonly double Duration = duration;
    }
    private readonly LyraLogicalSourceBank _bank;
    private readonly Func<string, bool> _loop;
    private readonly Func<string, double> _length;
    private readonly List<Retired> _retired = [];
    private readonly AlsPrecisePose[] _scratch = new AlsPrecisePose[81];
    private LyraLogicalSourceSampler? _current;
    private string _slot = "";
    private double _time;
    private float _weight = 1;

    public LyraLogicalSourcePlayback(LyraLogicalSourceBank bank, Func<string, bool> loop,
        Func<string, double> length) { _bank = bank; _loop = loop; _length = length; }
    public int SampledFrames { get; private set; }
    public int BlendedFrames { get; private set; }
    public string Slot => _slot;
    public double Time => _time;
    public int ActiveOccurrences => (_current is null ? 0 : 1) + _retired.Count;

    public void Select(string slot, double blend, bool restart = false)
    {
        if (!double.IsFinite(blend) || blend < 0) throw new ArgumentOutOfRangeException(nameof(blend));
        if (_slot == slot && !restart) return;
        var candidate = _bank.CreateSampler(slot);
        if (blend == 0) _retired.Clear();
        else if (_current is not null && _weight > AlsPoseBlender.WeightThreshold)
            _retired.Add(new(_current, _slot, _time, _weight, blend));
        _current = candidate; _slot = slot; _time = 0;
        _weight = _retired.Count == 0 ? 1 : Math.Clamp(1 - _retired.Sum(v => v.Weight * (float)(v.Remaining / v.Duration)), 0, 1);
    }

    public void Sample(double selectedTime, double delta, Span<AlsPrecisePose> output)
    {
        if (output.Length != 81 || _current is null || !double.IsFinite(selectedTime) || selectedTime < 0 ||
            !double.IsFinite(delta) || delta < 0) throw new ArgumentException("Invalid logical source frame.");
        var total = 0f;
        for (var index = _retired.Count - 1; index >= 0; index--)
        {
            var player = _retired[index]; player.Remaining = Math.Max(0, player.Remaining - delta);
            player.Time = AdvanceTime(player.Slot, player.Time + delta);
            var weight = player.Weight * (float)(player.Remaining / player.Duration);
            if (weight <= AlsPoseBlender.WeightThreshold) { _retired.RemoveAt(index); continue; }
            total += weight;
        }
        _time = selectedTime; _weight = Math.Clamp(1 - total, 0, 1);
        SampleInPlace(_current, selectedTime, output);
        if (_retired.Count != 0)
        {
            for (var bone = 0; bone < 81; bone++) output[bone] = AlsPrecisePoseBlender.Scale(output[bone], _weight);
            foreach (var player in _retired)
            {
                SampleInPlace(player.Sampler, player.Time, _scratch);
                var weight = player.Weight * (float)(player.Remaining / player.Duration);
                for (var bone = 0; bone < 81; bone++) output[bone] = AlsPrecisePoseBlender.Accumulate(output[bone], _scratch[bone], weight);
            }
            for (var bone = 0; bone < 81; bone++) output[bone] = output[bone].Normalized();
            BlendedFrames++;
        }
        SampledFrames++;
    }

    private double AdvanceTime(string slot, double time)
    {
        var length = _length(slot);
        if (!double.IsFinite(length) || length <= 0) throw new InvalidOperationException("Invalid logical source length.");
        return _loop(slot) ? time % length : Math.Min(time, length);
    }
    private void SampleInPlace(LyraLogicalSourceSampler sampler, double time, Span<AlsPrecisePose> output)
    {
        sampler.Sample(time, output);
        // The existing demo consumes movement through the capsule and keeps the
        // animation root at reference before RotateRootBone.
        output[0] = _bank.Reference[0];
    }
}
