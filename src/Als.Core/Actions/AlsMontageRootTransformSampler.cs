using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMontageRootSegment(float TrackStart, float AnimationStart, float AnimationEnd,
    float SegmentRate, float SequenceRate, int LoopCount, AlsRawRootMotionSampler Source);

// UAlsMontageUtility's absolute-root path, for the first authored slot only.
// The importer supplies actual raw sequence samplers, never the oracle samples.
public sealed class AlsMontageRootTransformSampler
{
    private readonly Segment[] _segments;
    private readonly record struct Segment(float Start,float End,float AnimationStart,float AnimationEnd,
        float Rate,int Loops,AlsRawRootMotionSampler Source);
    public AlsMontageRootTransformSampler(ReadOnlySpan<AlsMontageRootSegment> firstSlotSegments)
    {
        if(firstSlotSegments.IsEmpty) throw new ArgumentException("Mantle root sampling needs a first-slot sequence segment.");
        _segments=new Segment[firstSlotSegments.Length];
        for(var i=0;i<_segments.Length;i++)
        {
            var s=firstSlotSegments[i];
            if(s.Source is null || !float.IsFinite(s.TrackStart) || s.TrackStart<0 ||
                !float.IsFinite(s.AnimationStart) || s.AnimationStart<0 || !float.IsFinite(s.AnimationEnd) ||
                s.AnimationEnd<=s.AnimationStart || !float.IsFinite(s.SegmentRate) || !float.IsFinite(s.SequenceRate) || s.LoopCount<1)
                throw new ArgumentException("Invalid mantle root segment.");
            var rate=s.SegmentRate*s.SequenceRate;
            if(MathF.Abs(rate)<=1e-8f) rate=1;
            var end=s.TrackStart+(s.LoopCount*(s.AnimationEnd-s.AnimationStart))/MathF.Abs(rate);
            if(!float.IsFinite(rate) || !float.IsFinite(end) || end<=s.TrackStart) throw new ArgumentException("Invalid mantle segment timing.");
            _segments[i]=new(s.TrackStart,end,s.AnimationStart,s.AnimationEnd,rate,s.LoopCount,s.Source);
        }
    }
    public AlsPrecisePose Sample(float montageTime)
    {
        if(!float.IsFinite(montageTime)) throw new ArgumentException("Nonfinite mantle sample time.");
        var lookup=MathF.Max(0,montageTime); var index=_segments.Length-1;
        for(var i=_segments.Length-1;i>=0;i--)
            if(lookup>=_segments[i].Start && lookup<=_segments[i].End) { index=i;break; }
        var s=_segments[index]; var length=s.AnimationEnd-s.AnimationStart;
        // Preserve ConvertTrackPosToAnimPos, including its reverse/loop math.
        // Lookup clamps negative times; conversion deliberately uses the original.
        var unwrapped=(montageTime-s.Start)*s.Rate;
        var loops=MathF.Min(MathF.Floor(MathF.Abs(unwrapped)/length),s.Loops-1);
        var point=s.Rate>=0?s.AnimationStart:s.AnimationEnd;
        var position=point+(unwrapped-loops*length);
        if(!float.IsFinite(position)) throw new ArgumentException("Mantle root sample time overflow.");
        return s.Source.SampleAbsolute(position);
    }
    public AlsPrecisePose SampleLast()
    {
        // Native ExtractLastRootTransformFromMontage samples the final sequence
        // at Segment.GetEndPos(), without ConvertTrackPosToAnimPos. Do not merge
        // this with Sample(montageLength): trimmed/rate-scaled segments can differ.
        var last=_segments[^1]; return last.Source.SampleAbsolute(last.End);
    }
}
