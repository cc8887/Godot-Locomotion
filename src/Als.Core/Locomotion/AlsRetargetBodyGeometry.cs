namespace GodotAls.Core.Locomotion;

public readonly record struct AlsRetargetBodyInfluence(int Bone, AlsDoubleVector LocalPosition, float Weight);
public readonly record struct AlsRetargetBodyPoint(int FirstInfluence, int InfluenceCount);
public readonly record struct AlsRetargetContactEnvironment(bool Enabled, AlsPrecisePose LedgeToComponent,
    double ClearanceCm = .2)
{
    internal void Validate()
    {
        if (!Enabled) return;
        LedgeToComponent.Validate();
        if (!double.IsFinite(ClearanceCm) || ClearanceCm < 0 ||
            LedgeToComponent.Scale.X <= 0 || LedgeToComponent.Scale.Y <= 0 || LedgeToComponent.Scale.Z <= 0)
            throw new ArgumentException("Invalid retarget body contact environment.");
    }
}

// Immutable bind data for vertices that translate with the contact root but
// are not rotated by any subsequent limb IK. The host reads the imported mesh;
// the Core model independently checks this structural restriction.
public sealed class AlsRetargetBodyGeometry
{
    private readonly AlsRetargetBodyPoint[] _points;
    private readonly AlsRetargetBodyInfluence[] _influences;
    private readonly double[] _weightSums;
    public int BoneCount { get; }
    public int PointCount => _points.Length;

    public AlsRetargetBodyGeometry(int boneCount, ReadOnlySpan<AlsRetargetBodyPoint> points,
        ReadOnlySpan<AlsRetargetBodyInfluence> influences)
    {
        if (boneCount <= 0 || points.IsEmpty) throw new ArgumentException("Empty retarget body geometry.");
        BoneCount=boneCount; _points=points.ToArray(); _influences=influences.ToArray();
        _weightSums=new double[points.Length];
        for(var i=0;i<points.Length;i++)
        {
            var point=points[i];
            if(point.FirstInfluence<0 || point.InfluenceCount is <1 or >8 ||
                point.FirstInfluence>influences.Length-point.InfluenceCount)
                throw new ArgumentException("Invalid retarget body influence range.");
            double sum=0;
            foreach(var influence in influences.Slice(point.FirstInfluence,point.InfluenceCount))
            {
                if((uint)influence.Bone>=boneCount || !influence.LocalPosition.IsFinite ||
                    !float.IsFinite(influence.Weight) || influence.Weight<=0)
                    throw new ArgumentException("Invalid retarget body influence.");
                sum+=influence.Weight;
            }
            if(System.Math.Abs(sum-1)>.001)throw new ArgumentException("Unnormalized retarget body point.");
            _weightSums[i]=sum;
        }
    }

    internal void ValidateTranslationSupport(ReadOnlySpan<int> parents,int contactRoot,
        ReadOnlySpan<AlsRetargetLimb> limbs,ReadOnlySpan<AlsRetargetLimb> conditionalLimbs)
    {
        if(parents.Length!=BoneCount || contactRoot<0)throw new ArgumentException("Body geometry requires its contact-root skeleton.");
        foreach(var influence in _influences)
        {
            var descendant=false;
            for(var bone=influence.Bone;bone>=0;bone=parents[bone])
            {
                foreach(var limb in limbs)if(bone==limb.Upper)throw new ArgumentException("Body point is affected by limb IK.");
                foreach(var limb in conditionalLimbs)if(bone==limb.Upper)throw new ArgumentException("Body point is affected by contact IK.");
                descendant|=bone==contactRoot;
            }
            if(!descendant)throw new ArgumentException("Body point is outside the contact-root subtree.");
        }
    }

    internal void BuildRequirements(ReadOnlySpan<AlsPrecisePose> components,
        in AlsRetargetContactEnvironment environment,Span<double> requiredUp,Span<double> requiredOut)
    {
        if(components.Length!=BoneCount || requiredUp.Length!=PointCount || requiredOut.Length!=PointCount)
            throw new ArgumentException("Invalid body geometry pose buffers.");
        var ledge=environment.LedgeToComponent;
        var inverse=ledge.Rotation.Conjugate();
        for(var i=0;i<_points.Length;i++)
        {
            var point=_points[i];var position=AlsDoubleVector.Zero;
            foreach(var influence in _influences.AsSpan(point.FirstInfluence,point.InfluenceCount))
            {
                var bone=components[influence.Bone];
                position+=((influence.LocalPosition*bone.Scale).Rotate(bone.Rotation)+bone.Position)*influence.Weight;
            }
            // Both planes pass through the ledge origin. Positive pose scales
            // do not change these planes; distances stay component centimeters.
            var local=(position-ledge.Position).Rotate(inverse);
            // Imported weights are kept byte-for-byte. Use their actual sum,
            // rather than assume rounding made every vertex sum exactly one.
            requiredUp[i]=(environment.ClearanceCm-local.Z)/_weightSums[i];
            requiredOut[i]=(environment.ClearanceCm+local.Y)/_weightSums[i];
            if(!double.IsFinite(requiredUp[i]) || !double.IsFinite(requiredOut[i]))
                throw new ArgumentException("Nonfinite skinned body geometry.");
        }
    }

    internal double ClearanceResidual(int point,double upGap,double outGap) =>
        System.Math.Min(upGap,outGap)*_weightSums[point];
}
