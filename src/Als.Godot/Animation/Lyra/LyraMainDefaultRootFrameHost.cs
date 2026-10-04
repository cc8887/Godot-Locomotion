using GodotAls.Core.Contracts;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraMainDefaultRootFrame(LyraMainDefaultRootFrameHost Owner,
    LyraMainUpdateCandidate Macro,AlsFrameIdentity Identity,bool Visited);

// Main's real zero-linked-instance branch, prior to the enclosing final Rig73.
// The self SkeletalControls root has no Result link. Its input, caches, state
// machine, Aiming parameters and all source histories therefore stay unvisited.
// Main selects this branch during ordinary character Unlink.
internal sealed class LyraMainDefaultRootFrameHost
{
    private readonly LyraLinkedLayerCallRoutes _calls;
    private readonly LyraCompositionPoseBuffer _output;
    private readonly uint _character,_generation;
    private LyraMainDefaultRootFrame? _pending;
    private bool _evaluated;
    internal LyraMainGraphStateOwner Main {get;}
    internal LyraMainDefaultRootFrameHost(LyraLocomotionResources resources,
        LyraMainGraphStateOwner main,object characterOwner,uint character=0,uint generation=1,
        LyraLinkedLayerCallRoutes? calls=null)
    {
        if(generation==0||!ReferenceEquals(main.Layout,resources.Catalog.Bank))throw new ArgumentException("Foreign Main layout or invalid identity.");
        Main=main;_character=character;_generation=generation;main.RequireIdle();
        ArgumentNullException.ThrowIfNull(characterOwner);main.ClaimCharacter(characterOwner);
        if(calls is null)
        {
            var contracts=LyraLinkedLayerContracts.Load();var bindings=contracts.CreateBindings();
            calls=new(resources.LayerGraphs,contracts,bindings.Targets,main,new Dictionary<long,LyraItemLayerGraphInstance>());
        }
        _calls=calls;
        _output=new(main.Layout);
    }
    internal LyraMainDefaultRootFrame Prepare(in LyraMainUpdateInput input,float delta,bool visited=true,
        Action<LyraMainUpdateCandidate>? enterRoot=null)
    {
        if(_pending is not null)throw new InvalidOperationException("Default Main frame is pending.");
        try
        {
            var macro=Main.Prepare(this,input with{RootYawMode=Main.Update.Tail.Mode},delta);
            _pending=new(this,macro,new(macro.Observation.Frame,_character,_generation),visited);_evaluated=false;
            enterRoot?.Invoke(macro);
            if(visited)_calls.Update(_calls.Call(LyraLayerHook.FullBody_SkeletalControls),
                [()=>throw new InvalidOperationException("Self default unexpectedly traversed Main input.")],
                (_,_)=>throw new InvalidOperationException("Self default unexpectedly allocated a Linked owner."));
            return _pending;
        }
        catch{Cancel();throw;}
    }
    private void Validate(LyraMainDefaultRootFrame frame)
    {
        if(!ReferenceEquals(frame,_pending)||!ReferenceEquals(frame.Owner,this))throw new InvalidOperationException("Foreign or stale default Main frame.");
        Main.ValidateUntraversed(this,frame.Macro);
    }
    internal LyraLayerPoseInput Evaluate(LyraMainDefaultRootFrame frame)
    {
        Validate(frame);if(!frame.Visited)throw new InvalidOperationException("Unvisited default Main pose.");
        // FPoseContext is fresh at the full-root evaluation boundary. Layer
        // reset itself remains Pose-only, preserving any existing context data.
        Array.Clear(_output.Curves);Array.Clear(_output.Attributes);_output.RootMotion=default;
        _calls.Evaluate(_calls.Call(LyraLayerHook.FullBody_SkeletalControls),
            [_=>throw new InvalidOperationException("Self default unexpectedly evaluated Main input.")],_output,false,
            (_,_,_)=>throw new InvalidOperationException("Self default unexpectedly evaluated a Linked owner."));
        _evaluated=true;return _output.Input;
    }
    internal void ValidateCommit(LyraMainDefaultRootFrame frame,bool updateOnly=false)
    {
        Validate(frame);
        if(updateOnly?_evaluated:(!_evaluated||!frame.Visited))throw new InvalidOperationException("Incomplete default Main transaction.");
    }
    internal void ValidateFrame(LyraMainDefaultRootFrame frame)=>Validate(frame);
    internal void Commit(LyraMainDefaultRootFrame frame,bool updateOnly=false)
    {
        ValidateCommit(frame,updateOnly);
        Main.CommitUntraversed(this,frame.Macro);_pending=null;_evaluated=false;
    }
    internal void Cancel(){Main.Cancel(this);_pending=null;_evaluated=false;}
}
