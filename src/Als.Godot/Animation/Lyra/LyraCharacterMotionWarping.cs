using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraWarpTargetBinding(AlsMotionWarpingTarget Target,Node3D? Follow);
internal sealed record LyraWarpTargetCommand(string Name,LyraWarpTargetBinding? Binding);
internal sealed record LyraCharacterWarpCandidate(AlsMotionWarpingCandidate Core,
    Dictionary<string,LyraWarpTargetBinding> Bindings,AlsMotionWarpingContext Context,
    AlsPrecisePose Actor,AlsPrecisePose Component,AlsPrecisePose VisualRoot);

// History is published with the irreversible capsule move. A later animation
// retry must keep this receipt, including modifier start locations and targets.
internal sealed class LyraCharacterMotionWarping
{
    private readonly CharacterBody3D _body;
    private readonly Node3D _component;
    private readonly AlsMotionWarpingRuntime _runtime;
    private Dictionary<string,LyraWarpTargetBinding> _bindings=new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LyraWarpTargetCommand> _commands=[];
    private bool _disable;
    public AlsPrecisePose BaseOffset {get;}
    public LyraCharacterWarpCandidate? Last {get;private set;}
    public int Frames {get;private set;}
    public System.Collections.Immutable.ImmutableArray<AlsMotionWarpingModifier> Modifiers=>_runtime.Committed;
    public LyraCharacterMotionWarping(CharacterBody3D body,Node3D component,LyraMotionWarpingProfile profile,
        IAlsMotionWarpingRootSource source,uint character,uint generation)
    {
        _body=body;_component=component;_runtime=new(character,generation,profile.Windows,source);
        BaseOffset=AlsPrecisePose.Relative(LyraGodotRigCollision.NativeTransform(component.GlobalTransform),LyraGodotRigCollision.NativeTransform(body.GlobalTransform));
        _=VisualRoot(LyraGodotRigCollision.NativeTransform(body.GlobalTransform));
    }
    public void Set(AlsMotionWarpingTarget target,Node3D? follow=null)
    {
        if(string.IsNullOrWhiteSpace(target.Name))throw new ArgumentException("Warp target needs a name.");
        target.Transform.Validate();
        if(follow is not null&&(!GodotObject.IsInstanceValid(follow)||!follow.IsInsideTree()||follow.IsQueuedForDeletion()))throw new ArgumentException("Warp target component is not live.");
        _commands.Add(new(target.Name,new(target,follow)));
    }
    public void Remove(string name)
    {if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Warp target needs a name.");_commands.Add(new(name,null));}
    public void DisableExisting()=>_disable=true;
    private AlsPrecisePose VisualRoot(AlsPrecisePose actor)
    {
        var shapes=_body.GetChildren().OfType<CollisionShape3D>().Where(s=>!s.Disabled).ToArray();
        if(shapes.Length!=1||shapes[0].Shape is not CapsuleShape3D capsule||shapes[0].Position!=Vector3.Zero||shapes[0].Rotation!=Vector3.Zero||
            shapes[0].Scale!=Vector3.One||_body.GlobalBasis.Y.Normalized().Dot(Vector3.Up)<.99999f)
            throw new NotSupportedException("MotionWarping requires this character's centered upright capsule.");
        float halfHeight=capsule.Height*.5f*_body.GlobalBasis.Y.Length()*100;
        return new(actor.Position-new AlsDoubleVector(0,0,1).Rotate(actor.Rotation)*halfHeight,actor.Rotation,AlsDoubleVector.One);
    }
    public LyraCharacterWarpCandidate Prepare(AlsFrameIdentity id,LyraRootMotionAttribute raw,AlsMotionWarpingContext context)
    {
        var bindings=new Dictionary<string,LyraWarpTargetBinding>(_bindings,StringComparer.OrdinalIgnoreCase);
        var requests=new List<AlsMotionWarpingTargetRequest>();
        foreach(var c in _commands)
        {
            if(c.Binding is {} binding)bindings[c.Name]=binding;
            else{bindings.Remove(c.Name);requests.Add(new(AlsMotionWarpingTargetOperation.Remove,new(c.Name,AlsPrecisePose.Identity)));}
        }
        foreach(var pair in bindings.ToArray())
        {
            var binding=pair.Value;var target=binding.Target;
            if(binding.Follow is {} node)
            {
                if(!GodotObject.IsInstanceValid(node)||!node.IsInsideTree()||node.IsQueuedForDeletion())
                {bindings.Remove(pair.Key);requests.Add(new(AlsMotionWarpingTargetOperation.Remove,target));continue;}
                target=target with{Transform=LyraGodotRigCollision.NativeTransform(node.GlobalTransform)};
            }
            requests.Add(new(AlsMotionWarpingTargetOperation.Set,target));
        }
        var actor=LyraGodotRigCollision.NativeTransform(_body.GlobalTransform);var component=LyraGodotRigCollision.NativeTransform(_component.GlobalTransform);
        var visual=VisualRoot(actor);
        var core=_runtime.Begin(id,raw.Present,raw.Present?raw.Value:AlsPrecisePose.Identity,context,actor,visual,BaseOffset,requests.ToArray(),_disable);
        return new(core,bindings,context,actor,component,visual);
    }
    public void ValidateCommit(LyraCharacterWarpCandidate c)=>_runtime.ValidateCommit(c.Core);
    public void Commit(LyraCharacterWarpCandidate c)
    {_runtime.Commit(c.Core);_bindings=c.Bindings;_commands.Clear();_disable=false;Last=c;Frames++;}
    public void Cancel()=>_runtime.Cancel();
}
