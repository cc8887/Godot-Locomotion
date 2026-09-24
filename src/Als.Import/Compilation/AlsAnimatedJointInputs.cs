using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

// Single-owner adapter bound to one island. Reads committed solver targets and
// pelvis velocity on each call; a failed Step cannot leave a separate target
// history ahead of the solver. Definition MUST contain original authored frames.
public sealed class AlsAnimatedJointInputs
{
    private readonly AlsNativeMotorPose _pose;
    private readonly AlsRagdollMotorInputs _motors;
    private readonly AlsPrecisePose[] _locals;
    private readonly AlsQuaternion[] _targets;
    private readonly AlsIslandAngularDrive[] _drives;
    private readonly int _pelvis;
    public AlsJointIsland Island { get; }
    public AlsDoubleVector PelvisVelocity => new(Island.BodyAt(_pelvis).Velocity.Linear);
    public AlsDoubleVector PelvisPosition => Island.BodyAt(_pelvis).Actor.Position;

    public AlsAnimatedJointInputs(AlsRagdollPhysicsDefinition authored, ReadOnlySpan<AlsPhysicsJointSettings> settings,
        ReadOnlySpan<string> names, ReadOnlySpan<int> parents, AlsJointIsland island, float stiffnessScale, float dampingScale)
    {
        if (island.BodyCount < authored.Bodies.Length || island.JointCount != authored.Joints.Length)
            throw new ArgumentException("Animated island topology differs.");
        for (var i=0;i<authored.Joints.Length;i++)
        {
            var actual=island.JointDefinitionAt(i); var expected=authored.Joints[i];
            if (actual.Parent!=expected.ParentBody || actual.Child!=expected.ChildBody)
                throw new ArgumentException("Animated island joint order differs.");
        }
        _pelvis=Array.FindIndex(authored.Bodies,b=>b.Bone.Equals("pelvis",StringComparison.OrdinalIgnoreCase));
        if (_pelvis<0 || island.BodyDefinitionAt(_pelvis).InverseMass.Mass<=0)
            throw new ArgumentException("Animated island requires a dynamic pelvis.");
        Island=island;
        _pose=new(authored,names,parents); _motors=new(authored,settings,stiffnessScale,dampingScale);
        _locals=new AlsPrecisePose[authored.Bones.Length]; _targets=new AlsQuaternion[authored.Joints.Length];
        _drives=new AlsIslandAngularDrive[_motors.OutputCount];
    }

    // Returned memory is owned by this adapter and valid until its next Prepare.
    public ReadOnlySpan<AlsIslandAngularDrive> Prepare(ReadOnlySpan<AlsLocalPose> committedFlail)
    {
        _pose.Convert(committedFlail,_locals);
        return Evaluate();
    }
    public ReadOnlySpan<AlsIslandAngularDrive> Prepare(ReadOnlySpan<AlsPrecisePose> committedFlail)
    {
        _pose.Convert(committedFlail,_locals);
        return Evaluate();
    }
    private ReadOnlySpan<AlsIslandAngularDrive> Evaluate()
    {
        for(var i=0;i<_targets.Length;i++) _targets[i]=Island.JointDefinitionAt(i).Angular.DriveTarget;
        _motors.Evaluate(_locals,_targets,PelvisVelocity,_drives);
        return _drives;
    }
}
