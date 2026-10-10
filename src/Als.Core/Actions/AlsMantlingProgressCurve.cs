using GodotAls.Core.Curves;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMantlingProgressSample(float Planar, float Lift);

/// <summary>Immutable gameplay progress, in montage seconds and centimeters.
/// Controls target interpolation without extracting skeletal root motion.
/// The importer builds these buffers once from the asset's trajectory channels.</summary>
public sealed class AlsMantlingProgressCurve
{
    private readonly float[] _times, _planar, _lift, _remainingLift;
    public float Length => _times[^1];
    public float NominalHeight => _lift[^1] - _lift[0];

    public AlsMantlingProgressCurve(ReadOnlySpan<float> times, ReadOnlySpan<float> planar, ReadOnlySpan<float> lift)
    {
        if (times.Length < 2 || times.Length != planar.Length || times.Length != lift.Length || times[0] != 0)
            throw new ArgumentException("Incomplete mantle progress buffers.");
        _times=times.ToArray();_planar=planar.ToArray();_lift=lift.ToArray();_remainingLift=new float[times.Length];
        for (var i=0;i<times.Length;i++)
        {
            if (!float.IsFinite(times[i]) || !float.IsFinite(planar[i]) || !float.IsFinite(lift[i]) ||
                i>0 && (times[i]<=times[i-1] || planar[i]<_planar[i-1]-.001f || lift[i]<_lift[i-1]-.001f))
                throw new ArgumentException("Mantle progress must have finite, increasing time and nondecreasing distance.");
            // Sub-millimeter codec noise must not leave the distance buffer unsorted.
            if(i>0){_planar[i]=MathF.Max(_planar[i-1],planar[i]);_lift[i]=MathF.Max(_lift[i-1],lift[i]);}
        }
        if(!float.IsFinite(NominalHeight)||NominalHeight<=1e-4f||!float.IsFinite(_planar[^1]-_planar[0]))
            throw new ArgumentException("Mantle progress needs finite distance ranges and a positive lift.");
        for(var i=0;i<times.Length;i++)_remainingLift[i]=_lift[i]-_lift[^1];
    }

    // Height matching chooses the entry pose. The existing character clock
    // drives movement and pose thereafter; plateaus must not freeze playback.
    public float MatchStartTime(float height)
    {
        if(!float.IsFinite(height)||height<=0)throw new ArgumentException("Invalid mantle target height.");
        return System.Math.Clamp(AlsDistanceMatching.MatchToTarget(height,_times,_remainingLift),0,Length);
    }

    public AlsMantlingProgressSample Sample(float time,float startTime)
        =>Sample(time,startTime,startTime);

    public AlsMantlingProgressSample Sample(float time,float startTime,float liftStartTime)
    {
        if(!float.IsFinite(time)||!float.IsFinite(startTime)||startTime<0||startTime>Length||
            !float.IsFinite(liftStartTime)||liftStartTime<startTime||liftStartTime>Length)
            throw new ArgumentException("Invalid mantle progress time.");
        var lift=Rebase(_lift,liftStartTime);var range=_planar[^1]-Value(_planar,startTime);
        return new(range<=1e-4f ? lift : Rebase(_planar,startTime),lift);
        float Rebase(float[] values,float originTime)
        {
            var start=Value(values,originTime);var remaining=values[^1]-start;
            return remaining<=1e-4f ? time>=Length ? 1 : 0 : System.Math.Clamp((Value(values,time)-start)/remaining,0,1);
        }
    }

    public AlsMantlingProgressSample Sample(float time, float startTime, float liftStartTime,
        float? targetHeight, float? preparationEndTime)
    {
        var sample = Sample(time, startTime, liftStartTime);
        if (targetHeight is null && preparationEndTime is null) return sample;
        if (targetHeight is not { } height || preparationEndTime is not { } ready ||
            !float.IsFinite(height) || height <= 0 || !float.IsFinite(ready) || ready < startTime || ready > Length)
            throw new ArgumentException("Invalid mantle height preparation.");
        var available = _lift[^1] - Value(_lift, liftStartTime);
        if (height <= available) return sample;
        // A nominal 1 m motion still needs its extra height before gripping a
        // taller ledge. Scaling the entire lift postpones that compensation
        // until landing and sends the approach hand through the front.
        var preparation = ready > startTime ? System.Math.Clamp((time-startTime)/(ready-startTime), 0, 1)
            : time > startTime ? 1 : 0;
        return sample with { Lift = (available*sample.Lift+(height-available)*preparation)/height };
    }

    private float Value(float[] values,float time)
    {
        if(time<=0)return values[0];if(time>=Length)return values[^1];
        var end=Array.BinarySearch(_times,time);
        if(end>=0)return values[end];end=~end;
        var alpha=(time-_times[end-1])/(_times[end]-_times[end-1]);
        return values[end-1]+(values[end]-values[end-1])*alpha;
    }
}
