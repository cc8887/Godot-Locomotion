using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

/// <summary>Main-thread presentation of a completed native mantle pose. Virtual bones stay
/// in the source evaluator; this binding writes only the visual skin's physical bones.
/// The caller must suspend the normal animation writer before applying a pose.</summary>
internal sealed class AlsMantlingVisualBinding
{
    private readonly Skeleton3D _skeleton;
    private readonly AlsMantlingPoseSource _source;
    private readonly int[] _visual;
    private readonly StringName[] _names;
    private readonly int[] _parents;
    private readonly Transform3D[] _scratch;
    internal AlsMantlingVisualBinding(Skeleton3D skeleton,AlsMantlingPoseSource source)
    {
        Main();_skeleton=skeleton;_source=source;
        if(!GodotObject.IsInstanceValid(skeleton)||!skeleton.IsInsideTree()||skeleton.GetBoneCount()!=source.Data.PhysicalBoneCount)
            throw new ArgumentException("Mantle presentation needs the complete physical skin skeleton.");
        var count=skeleton.GetBoneCount();_visual=new int[count];_names=new StringName[count];_parents=new int[count];_scratch=new Transform3D[count];
        var used=new HashSet<int>();
        for(var i=0;i<count;i++)
        {
            var logical=source.Data.PhysicalToLogical[i];var bone=skeleton.FindBone(source.BoneNames[logical]);
            if(bone<0||!used.Add(bone))throw new ArgumentException("Mantle physical bone is missing or ambiguous.");
            _visual[i]=bone;_names[i]=skeleton.GetBoneName(bone);_parents[i]=skeleton.GetBoneParent(bone);
        }
        for(var i=0;i<count;i++)
        {
            var logical=source.Data.PhysicalToLogical[i];var parent=source.Parents[logical];
            var expectedParent=parent<0?-1:_visual[source.Data.LogicalToPhysical[parent]];
            if(_parents[i]!=expectedParent)throw new ArgumentException("Mantle physical hierarchy differs from the visual skin.");
            var expected=ToFbx(source.ReferencePose[logical]);var rest=skeleton.GetBoneRest(_visual[i]);
            if(rest.Origin.DistanceTo(expected.Origin)>.0002f||
                rest.Basis.Scale.DistanceTo(expected.Basis.Scale)>.0002f||
                1-MathF.Abs(rest.Basis.GetRotationQuaternion().Dot(expected.Basis.GetRotationQuaternion()))>1e-5f)
                throw new ArgumentException("Mantle visual rest pose requires an explicit retarget: "+_names[i]);
        }
    }
    internal void Apply(ReadOnlySpan<AlsPrecisePose> pose)
    {
        Main();
        if(!GodotObject.IsInstanceValid(_skeleton)||!_skeleton.IsInsideTree()||_skeleton.GetBoneCount()!=_visual.Length||pose.Length!=_source.Data.LogicalBoneCount)
            throw new InvalidOperationException("Mantle visual binding expired or pose layout changed.");
        // Complete validation/conversion before any scene write.
        for(var i=0;i<_visual.Length;i++)
        {
            if(_skeleton.GetBoneName(_visual[i])!=_names[i]||_skeleton.GetBoneParent(_visual[i])!=_parents[i])
                throw new InvalidOperationException("Mantle visual skeleton changed.");
            _scratch[i]=ToFbx(pose[_source.Data.PhysicalToLogical[i]]);
        }
        for(var i=0;i<_visual.Length;i++)
        {
            var value=_scratch[i];_skeleton.SetBonePosePosition(_visual[i],value.Origin);
            var original=pose[_source.Data.PhysicalToLogical[i]];var q=original.Rotation;var scale=original.Scale;
            _skeleton.SetBonePoseRotation(_visual[i],new Quaternion((float)-q.X,(float)q.Y,(float)-q.Z,(float)q.W).Normalized());
            _skeleton.SetBonePoseScale(_visual[i],new((float)scale.X,(float)scale.Y,(float)scale.Z));
        }
    }
    internal static Transform3D ToFbx(in AlsPrecisePose pose)
    {
        pose.Validate();var p=pose.Position;var q=pose.Rotation;var s=pose.Scale;
        var rotation=new Quaternion((float)-q.X,(float)q.Y,(float)-q.Z,(float)q.W).Normalized();
        var basis=new Basis(rotation);
        var result=new Transform3D(new Basis(basis.X*(float)s.X,basis.Y*(float)s.Y,basis.Z*(float)s.Z),
            new((float)(p.X*.01),(float)(-p.Y*.01),(float)(p.Z*.01)));
        if(!result.IsFinite())throw new ArgumentException("Mantle visual transform overflow.");return result;
    }
    private static void Main(){if(!GodotThread.IsMainThread())throw new InvalidOperationException("Mantle visual pose writes belong to Main.");}
}
