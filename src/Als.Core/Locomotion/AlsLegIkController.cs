using System.Collections.Immutable;
using M = System.Math;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsLegIkDefinition(int Foot,int Target);
public readonly record struct AlsLegIkBendHistory(AlsDoubleVector Real,AlsDoubleVector Base);

// The original Lyra LegIK configuration: two bones, no softening/rotation
// limit, Y foot forward, Z hinge, knee twist correction and no twist curve.
// History is returned as a candidate; the enclosing owner controls publication.
public sealed class AlsLegIkController
{
    private readonly AlsComponentPose _pose;
    private readonly int[][] _chains;
    private readonly AlsLegIkDefinition[] _definitions;
    private readonly int _bones;
    private readonly float _precision;
    public AlsLegIkController(ReadOnlySpan<int> parents,ReadOnlySpan<AlsLegIkDefinition> definitions,float precision=.01f)
    {
        if(definitions.IsEmpty||!float.IsFinite(precision)||precision<=0)throw new ArgumentException("Invalid LegIK configuration.");
        for(var bone=0;bone<parents.Length;bone++)if(parents[bone]<-1||parents[bone]>=bone)throw new ArgumentException("LegIK needs a parent-first hierarchy.");
        _definitions=definitions.ToArray();_bones=parents.Length;_pose=new(parents);_precision=precision;
        _chains=new int[definitions.Length][];
        for(var i=0;i<definitions.Length;i++)
        {
            var d=definitions[i];if((uint)d.Foot>=parents.Length||(uint)d.Target>=parents.Length)throw new ArgumentException("Invalid LegIK bones.");
            var knee=parents[d.Foot];var hip=knee<0?-1:parents[knee];if(hip<0)throw new ArgumentException("LegIK needs a three-joint limb.");
            _chains[i]=[d.Foot,knee,hip];
        }
        if(_chains.SelectMany(c=>c).Distinct().Count()!=3*definitions.Length)throw new ArgumentException("Overlapping LegIK limbs.");
    }
    public ImmutableArray<AlsLegIkBendHistory> Evaluate(ReadOnlySpan<AlsPrecisePose> input,float alpha,
        ImmutableArray<AlsLegIkBendHistory> history,Span<AlsPrecisePose> output)
    {
        if(input.Length!=_bones||output.Length!=_bones||input.Overlaps(output)||!float.IsFinite(alpha)||alpha is <0 or >1||history.IsDefault||history.Length!=_chains.Length||
            history.Any(h=>!h.Real.IsFinite||!h.Base.IsFinite))throw new ArgumentException("Invalid LegIK frame.");
        foreach(var p in input)p.Validate(.001);
        if(alpha<=AlsPoseBlender.WeightThreshold){input.CopyTo(output);return history;}
        _pose.Begin(input);var next=Evaluate(_pose,alpha,history);_pose.Export(output);return next;
    }
    internal ImmutableArray<AlsLegIkBendHistory> Evaluate(AlsComponentPose pose,float alpha,ImmutableArray<AlsLegIkBendHistory> history)
    {
        if(alpha<=AlsPoseBlender.WeightThreshold)return history;
        var next=history.ToArray();
        var changes=new List<(int Bone,AlsPrecisePose Pose)>();
        Span<AlsPrecisePose> leg=stackalloc AlsPrecisePose[3];Span<AlsDoubleVector> links=stackalloc AlsDoubleVector[3];
        // All limbs read the same component pose, before any output is applied.
        for(var limb=0;limb<_chains.Length;limb++)
        {
            var chain=_chains[limb];
            for(var i=0;i<3;i++)leg[i]=pose.Component(chain[i]);
            var goal=pose.Component(_definitions[limb].Target);var hip=leg[2].Position;
            var changed=Rotate(leg,SafeNormal(leg[0].Position-hip),SafeNormal(goal.Position-hip));
            if(!(leg[0].Position-goal.Position).NearlyZero(_precision))
            {
                for(var i=0;i<3;i++)links[i]=leg[i].Position;
                var lower=M.Sqrt((links[0]-links[1]).LengthSquared);var upper=M.Sqrt((links[1]-links[2]).LengthSquared);
                var delta=goal.Position-links[2];
                if(delta.LengthSquared>=(upper+lower)*(upper+lower))
                {
                    var direction=SafeNormal(delta);links[1]=links[2]+direction*upper;links[0]=links[1]+direction*lower;
                }
                else
                {
                    links[0]=goal.Position;var length=M.Sqrt(delta.LengthSquared);var denominator=2*upper*length;
                    var cosine=M.Abs(denominator)>1e-8?(upper*upper+length*length-lower*lower)/denominator:0;
                    var angle=M.Acos(M.Clamp(cosine,-1,1));
                    var direction=M.Abs(length)>1e-8?delta*(1/length):default;
                    var knee=links[1]-links[2];var projected=links[2]+direction*AlsDoubleVector.Dot(knee,direction);
                    var bend=SafeNormal(links[1]-projected,(double)1e-4f);
                    var hinge=new AlsDoubleVector(0,0,1).Rotate(leg[2].Rotation);
                    if(hinge!=default&&direction!=default&&M.Abs(upper)>1e-8)
                    {
                        var kneeDirection=knee*(1/upper);var dot=AlsDoubleVector.Dot(kneeDirection,direction);
                        if(bend!=default&&dot<.99)next[limb]=new(bend,AlsDoubleVector.Cross(hinge,direction));
                        else if(next[limb].Real!=default)
                            bend=next[limb].Real.Rotate(AlsComponentQuaternion.Between(next[limb].Base,AlsDoubleVector.Cross(hinge,direction)));
                    }
                    links[1]=links[2]+(direction*cosine+bend*M.Sin(angle))*upper;
                }
                for(var i=1;i>=0;i--)
                {
                    var rotation=AlsComponentQuaternion.Between(SafeNormal(leg[i].Position-leg[i+1].Position),SafeNormal(links[i]-links[i+1]));
                    leg[i+1]=leg[i+1] with{Rotation=rotation*leg[i+1].Rotation};
                }
                for(var i=1;i>=0;i--)leg[i]=leg[i] with{Position=links[i]};changed=true;
            }
            var axis=SafeNormal(goal.Position-leg[2].Position);
            var fk=new AlsDoubleVector(0,1,0).Rotate(leg[0].Rotation);var ik=new AlsDoubleVector(0,1,0).Rotate(goal.Rotation);
            fk=AlsDoubleVector.Cross(AlsDoubleVector.Cross(axis,fk),axis);ik=AlsDoubleVector.Cross(AlsDoubleVector.Cross(axis,ik),axis);
            changed|=Rotate(leg,fk,ik);
            if(changed||!QuatEquals(leg[0].Rotation,goal.Rotation))
            {
                leg[0]=leg[0] with{Rotation=goal.Rotation};
                for(var i=0;i<3;i++)changes.Add((chain[i],leg[i]));
            }
        }
        if(changes.Count>0)
        {
            var sorted=changes.OrderBy(c=>c.Bone).ToArray();
            pose.Apply(sorted.Select(c=>c.Bone).ToArray(),sorted.Select(c=>c.Pose).ToArray(),alpha);
        }
        return next.ToImmutableArray();
    }
    private static bool Rotate(Span<AlsPrecisePose> leg,AlsDoubleVector initial,AlsDoubleVector target)
    {
        if(initial==default||(initial-target).NearlyZero((double)1e-4f))return false;
        var rotation=AlsComponentQuaternion.Between(initial,target);if(QuatEquals(rotation,AlsQuaternion.Identity,(double)1e-8f))return false;
        var hip=leg[2].Position;
        for(var i=0;i<3;i++)leg[i]=leg[i] with{Rotation=rotation*leg[i].Rotation,Position=hip+(leg[i].Position-hip).Rotate(rotation)};
        return true;
    }
    private static AlsDoubleVector SafeNormal(AlsDoubleVector v,double tolerance=(double)1e-8f)
    {var squared=v.LengthSquared;return squared==1?v:squared<tolerance?default:v*(1/M.Sqrt(squared));}
    private static bool QuatEquals(AlsQuaternion a,AlsQuaternion b,double t=(double)1e-4f)
    {
        return M.Abs(a.X-b.X)<=t&&M.Abs(a.Y-b.Y)<=t&&M.Abs(a.Z-b.Z)<=t&&M.Abs(a.W-b.W)<=t||
            M.Abs(a.X+b.X)<=t&&M.Abs(a.Y+b.Y)<=t&&M.Abs(a.Z+b.Z)<=t&&M.Abs(a.W+b.W)<=t;
    }
}
