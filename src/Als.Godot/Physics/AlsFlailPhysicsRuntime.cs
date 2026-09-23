using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Scene-owned isolated Flail/physics transaction used by contact acceptance.
// Ordinary character gameplay must use its existing shared animation owner.
internal sealed class AlsFlailPhysicsRuntime : IDisposable
{
    private readonly AlsRagdollFrameRuntime _frame;
    private readonly AlsRagdollAnimationSource _source;
    private readonly AlsAnimatedJointInputs _inputs;
    private readonly uint _character;
    private AlsAnimationGraphFrame _traversal;
    internal long Steps { get; private set; }
    internal float Time => _frame.Committed.Time;
    private AlsDoubleVector _inputVelocity;
    private float _inputRate;
    internal object CaptureSample() => new {
        frame=Steps,time=Time,rate=_inputRate,spring=AlsRagdollMotorInputs.SpringFromPelvisVelocity(_inputVelocity),
        pelvisVelocity=new[]{_inputVelocity.X,_inputVelocity.Y,_inputVelocity.Z},
        motors=Enumerable.Range(0,_inputs.Island.JointCount).Select(i=> {
            var a=_inputs.Island.JointDefinitionAt(i).Angular; var q=a.DriveTarget;
            return new { index=i,target=new[]{q.X,q.Y,q.Z,q.W},
                stiffness=new[]{a.X.DriveStiffness,a.Y.DriveStiffness,a.Z.DriveStiffness} }; }).ToArray(),
        bodies=Enumerable.Range(0,_inputs.Island.BodyCount).Select(i=> {
            var b=_inputs.Island.BodyAt(i);var p=b.Actor.Position;var q=b.Actor.Rotation;var v=b.Velocity.Linear;var w=b.Velocity.Angular;
            return new { index=i,position=new[]{p.X,p.Y,p.Z},rotation=new[]{q.X,q.Y,q.Z,q.W},v=new[]{v.X,v.Y,v.Z},w=new[]{w.X,w.Y,w.Z} }; }).ToArray()
    };

    internal AlsFlailPhysicsRuntime(AlsMovementGraphDefinition graph, AlsAnimationSetDefinition set,
        AlsRagdollPhysicsDefinition authored, AlsPhysicsJointSettings[] settings, AlsJointIsland island, uint character)
    {
        _character=character;
        var skeleton=graph.RagdollRawSources.GetSkeleton(graph.RagdollPose.SkeletonId);
        _inputs=new(authored,settings,skeleton.LogicalBoneNames,skeleton.LogicalParents,island,1.5f,1.5f);
        _frame=new(graph.RagdollFrame,new(graph.RagdollPose.SnapshotName,"ALS_Mesh",character,1,
            skeleton.RawBoneNames,skeleton.LogicalToPhysical,skeleton.ReferencePose),character,1,skeleton.LogicalBoneCount,0);
        _source=new(graph,set,[]);
    }

    internal void Step(double dt, AlsCoreJointHost host, AlsWorldContacts contacts, AlsSceneContactSet scene)
    {
        var identity=new AlsFrameIdentity(Steps+1,_character,1);
        var next=_traversal.Next(identity,checked((ulong)identity.FrameId));
        try
        {
            var velocity=_inputs.PelvisVelocity;
            _frame.Prepare(AlsMovementStateInput.Ragdoll,velocity,true,
                new AlsPoseUpdateContext(identity,1,(float)dt),next);
            _frame.Evaluate(_source,null);
            _frame.ValidateCommit(identity);
            var rate=(float)_frame.Candidate.FlailRate;
            // Validated animation commit cannot fail after the solver publishes.
            // A failed scene step discards the candidate clock and target pose.
            host.StepAnimatedScene(dt,new(0,0,-980),contacts,scene,_inputs,_frame.PreciseFlailPose);
            _frame.Commit(identity); _traversal=next; Steps++;
            _inputVelocity=velocity; _inputRate=rate;
        }
        catch { _frame.Cancel(); throw; }
    }
    public void Dispose() { _frame.Cancel(); _source.Dispose(); }
}
