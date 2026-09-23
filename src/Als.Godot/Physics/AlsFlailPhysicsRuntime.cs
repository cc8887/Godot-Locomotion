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
            _frame.Prepare(AlsMovementStateInput.Ragdoll,_inputs.PelvisVelocity,true,
                new AlsPoseUpdateContext(identity,1,(float)dt),next);
            _frame.Evaluate(_source,null);
            _frame.ValidateCommit(identity);
            // Validated animation commit cannot fail after the solver publishes.
            // A failed scene step discards the candidate clock and target pose.
            host.StepAnimatedScene(dt,new(0,0,-980),contacts,scene,_inputs,_frame.Pose);
            _frame.Commit(identity); _traversal=next; Steps++;
        }
        catch { _frame.Cancel(); throw; }
    }
    public void Dispose() { _frame.Cancel(); _source.Dispose(); }
}
