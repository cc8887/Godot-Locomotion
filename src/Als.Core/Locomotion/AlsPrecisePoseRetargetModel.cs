namespace GodotAls.Core.Locomotion;

/// <summary>Tracked-bone, non-baked raw retarget pass on one skeleton.
/// Source and target references must already use the same skeleton/bone basis.
/// Compatible-skeleton remapping is a separate operation, not inferred here.</summary>
public sealed class AlsPrecisePoseRetargetModel
{
    private readonly record struct Entry(int Bone,int Mode,AlsPrecisePose Source,AlsPrecisePose Target,
        bool Orient,AlsQuaternion Rotation,float Scale);
    private readonly Entry[] _entries;
    private readonly int _count;
    private readonly double _units;
    public AlsPrecisePoseRetargetModel(ReadOnlySpan<int> mapping,ReadOnlySpan<int> modes,
        ReadOnlySpan<bool> presence,ReadOnlySpan<AlsPrecisePose> target,ReadOnlySpan<AlsPrecisePose> source,
        double unitsToCentimeters=1)
    {
        if(mapping.IsEmpty||presence.Length!=mapping.Length||target.Length!=mapping.Length||modes.IsEmpty||
            !source.IsEmpty && source.Length<modes.Length)
            throw new ArgumentException("Incomplete raw retarget references.");
        if(!double.IsFinite(unitsToCentimeters)||unitsToCentimeters<=0) throw new ArgumentException("Invalid retarget units.");
        _units=unitsToCentimeters;
        _count=mapping.Length;var seen=new bool[modes.Length];var entries=new List<Entry>();
        foreach(var pose in source) pose.Validate();
        for(var bone=0;bone<mapping.Length;bone++)
        {
            target[bone].Validate();var physical=mapping[bone];if(physical==-1) continue;
            if((uint)physical>=(uint)modes.Length||seen[physical]||modes[physical] is <0 or >4)
                throw new ArgumentException("Invalid retarget mapping or mode.");
            seen[physical]=true;
            if(!presence[bone]||modes[physical]==0||source.IsEmpty) continue;
            var a=source[physical];var b=target[bone];var orient=false;var q=AlsQuaternion.Identity;float scale=1;
            if(modes[physical] is 2 or 4)
            {
                var aLength=(float)System.Math.Sqrt((a.Position*_units).LengthSquared);
                var bLength=(float)System.Math.Sqrt((b.Position*_units).LengthSquared);
                if(!float.IsFinite(aLength)||!float.IsFinite(bLength)) throw new ArgumentException("Retarget length overflow.");
                if(modes[physical]==2) { if(aLength>1e-4f) scale=bLength/aLength; }
                else if(!(a.Position-b.Position).NearlyZero(.001f/_units)&&MathF.Abs(aLength*bLength)>1e-8f)
                {
                    orient=true;scale=bLength/aLength;
                    q=Between(a.Position*(_units/aLength),b.Position*(_units/bLength));
                }
                if(!float.IsFinite(scale)) throw new ArgumentException("Retarget scale overflow.");
            }
            entries.Add(new(bone,modes[physical],a,b,orient,q,scale));
        }
        if(seen.Any(v=>!v)) throw new ArgumentException("Incomplete physical retarget mapping.");
        _entries=entries.ToArray();
    }
    public void Apply(Span<AlsPrecisePose> pose,bool shouldRetarget)
    {
        if(pose.Length!=_count) throw new ArgumentException("Retarget output layout differs.");
        foreach(var atom in pose) atom.Validate();
        if(!shouldRetarget) return;
        foreach(var entry in _entries) pose[entry.Bone]=ApplyEntry(pose[entry.Bone],entry);
    }
    public void Apply(Span<AlsLocalPose> pose,bool shouldRetarget)
    {
        if(pose.Length!=_count) throw new ArgumentException("Retarget output layout differs.");
        foreach(var atom in pose) AlsRawRootLock.Validate(atom);
        if(!shouldRetarget) return;
        foreach(var entry in _entries) pose[entry.Bone]=ApplyEntry(new(pose[entry.Bone]),entry).ToSingle();
    }
    private AlsPrecisePose ApplyEntry(in AlsPrecisePose pose,in Entry e)=>e.Mode switch
    {
        1=>pose with {Position=e.Target.Position},
        2=>pose with {Position=pose.Position*e.Scale},
        3=>new(pose.Position+(e.Target.Position-e.Source.Position),
            (pose.Rotation*e.Source.Rotation.Conjugate()*e.Target.Rotation).Normalized(),
            pose.Scale*(e.Target.Scale*new AlsDoubleVector(Reciprocal(e.Source.Scale.X),Reciprocal(e.Source.Scale.Y),Reciprocal(e.Source.Scale.Z)))),
        4 when e.Orient=>pose with {Position=(pose.Position-e.Source.Position).NearlyZero(.001f/_units)
            ? e.Target.Position : pose.Position.Rotate(e.Rotation)*e.Scale},
        _=>pose
    };
    private static double Reciprocal(double value)=>System.Math.Abs(value)<=1e-8f ? 0 : 1/value;
    private static AlsQuaternion Between(AlsDoubleVector a,AlsDoubleVector b)
    {
        var w=1+AlsDoubleVector.Dot(a,b);AlsDoubleVector xyz;
        if(w>=1e-6f) xyz=AlsDoubleVector.Cross(a,b);
        else
        {
            w=0;
            var basis=System.Math.Abs(a.X)>System.Math.Abs(a.Y)&&System.Math.Abs(a.X)>System.Math.Abs(a.Z)
                ? new AlsDoubleVector(0,1,0) : new AlsDoubleVector(-1,0,0);
            xyz=AlsDoubleVector.Cross(a,basis);
        }
        return new AlsQuaternion(xyz.X,xyz.Y,xyz.Z,w).Normalized();
    }
}
