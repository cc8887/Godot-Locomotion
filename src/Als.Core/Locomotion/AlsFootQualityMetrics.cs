using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// Samples must come from the final skinned mesh, in the supporting surface's
// local centimeters. Contact denotes an animation/geometry window, not force.
public readonly record struct AlsFootQualitySample(AlsFrameIdentity Identity, long SurfaceId, bool Contact,
    AlsDoubleVector SoleCentroid, double MinimumSupportHeight, double KneeBendRadians);

public readonly record struct AlsFootQualityMetrics(AlsFootQualitySample Previous, AlsDoubleVector Anchor,
    long ContactFrames, long ConsecutiveSamples, double SquaredPlanarDisplacement,
    double MaximumPlanarSpeed, double MaximumAnchorDrift, double MaximumPenetration,
    double MaximumKneeDeltaRadians)
{
    public long MaximumKneeDeltaFrame { get; init; }
    public double RmsPlanarDisplacement => ConsecutiveSamples==0 ? 0 : System.Math.Sqrt(SquaredPlanarDisplacement/ConsecutiveSamples);
    public AlsFootQualityMetrics Advance(in AlsFootQualitySample sample, double delta)
    {
        if(sample.Identity.SlotGeneration==0||!sample.SoleCentroid.IsFinite||!double.IsFinite(sample.MinimumSupportHeight)||
            !double.IsFinite(sample.KneeBendRadians)||sample.KneeBendRadians<0||sample.KneeBendRadians>System.Math.PI||
            !double.IsFinite(delta)||delta<=0||Previous.Identity!=default&&
            (sample.Identity.CharacterId!=Previous.Identity.CharacterId||sample.Identity.SlotGeneration!=Previous.Identity.SlotGeneration||
                sample.Identity.FrameId<=Previous.Identity.FrameId))throw new ArgumentException("Invalid final-mesh foot quality sample.");
        var continuous=sample.Contact&&Previous.Contact&&sample.SurfaceId==Previous.SurfaceId&&
            sample.Identity.FrameId==Previous.Identity.FrameId+1;
        var anchor=continuous?Anchor:sample.SoleCentroid;
        var next=this with {Previous=sample,Anchor=anchor};
        if(!sample.Contact)return next;
        next=next with {ContactFrames=ContactFrames+1,
            MaximumPenetration=System.Math.Max(MaximumPenetration,System.Math.Max(0,-sample.MinimumSupportHeight))};
        if(!continuous)return next;
        var displacement=sample.SoleCentroid-Previous.SoleCentroid;var drift=sample.SoleCentroid-anchor;
        var d2=displacement.X*displacement.X+displacement.Z*displacement.Z;
        var kneeDelta=System.Math.Abs(sample.KneeBendRadians-Previous.KneeBendRadians);
        return next with {ConsecutiveSamples=ConsecutiveSamples+1,SquaredPlanarDisplacement=SquaredPlanarDisplacement+d2,
            MaximumPlanarSpeed=System.Math.Max(MaximumPlanarSpeed,System.Math.Sqrt(d2)/delta),
            MaximumAnchorDrift=System.Math.Max(MaximumAnchorDrift,System.Math.Sqrt(drift.X*drift.X+drift.Z*drift.Z)),
            MaximumKneeDeltaRadians=System.Math.Max(MaximumKneeDeltaRadians,kneeDelta),
            MaximumKneeDeltaFrame=kneeDelta>MaximumKneeDeltaRadians?sample.Identity.FrameId:MaximumKneeDeltaFrame};
    }
}
