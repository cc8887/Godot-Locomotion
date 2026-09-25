using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredStopEvaluatorUpdate(int PropertyIndex,AlsPoseUpdateContext Context);

/// <summary>Original Stop state traversal. Emits distinct cache66 readers and
/// fixed evaluators; the enclosing scheduler owns the single deferred cache update.</summary>
public sealed class AlsRefactoredStopSourceRuntime
{
    private AlsOverlayBlendListState[] _selectors=new AlsOverlayBlendListState[4],_next=new AlsOverlayBlendListState[4];
    private readonly AlsRefactoredMovementCacheRead[] _reads=new AlsRefactoredMovementCacheRead[5];
    private readonly int[] _initialReads=new int[4];
    private readonly AlsRefactoredStopEvaluatorUpdate[] _evaluators=new AlsRefactoredStopEvaluatorUpdate[12];
    private int _readCount,_initialCount,_evaluatorCount;
    private AlsRefactoredStopRuntime? _owner;
    private AlsFrameIdentity _identity,_committed;
    private bool _prepared,_hasCommitted;
    private Vector4 _weights;
    public AlsRefactoredStopPoseGraph Profile { get; }
    public Vector4 DirectionWeights=>_prepared?_weights:throw new InvalidOperationException("No Stop source candidate.");
    public ReadOnlySpan<AlsRefactoredMovementCacheRead> CacheReads=>_prepared?_reads.AsSpan(0,_readCount):throw new InvalidOperationException("No Stop source candidate.");
    public ReadOnlySpan<int> CacheInitializationReads=>_prepared?_initialReads.AsSpan(0,_initialCount):throw new InvalidOperationException("No Stop source candidate.");
    public ReadOnlySpan<AlsRefactoredStopEvaluatorUpdate> EvaluatorUpdates=>_prepared?_evaluators.AsSpan(0,_evaluatorCount):throw new InvalidOperationException("No Stop source candidate.");
    public AlsRefactoredStopSourceRuntime(AlsRefactoredStopPoseGraph profile)=>Profile=profile;
    public Vector4 SelectorWeights(int state,bool right)
    {
        if(!_prepared||state is <3 or >4)throw new ArgumentException("Invalid Stop selector query.");
        return _next[(state-3)*2+(right?1:0)].Weights;
    }
    public void Prepare(AlsRefactoredStopRuntime machine,in AlsPoseUpdateContext context,Vector4 velocity,AlsRefactoredHipsDirection hips,bool initializeInstance=false)
    {
        var frame=context.Identity.FrameId;
        if(_prepared||!ReferenceEquals(machine.Resources,Profile.Resources)||_owner is not null&&!ReferenceEquals(_owner,machine)||
            context.UpdateCounter is not {HasUpdated:true}||!context.HasSharedContext||!Enum.IsDefined(hips)||
            _hasCommitted&&(context.Identity.CharacterId!=_committed.CharacterId||context.Identity.SlotGeneration!=_committed.SlotGeneration||frame<=_committed.FrameId))
            throw new ArgumentException("Invalid Stop source owner/context.");
        machine.ValidateCommit(frame);var update=machine.Candidate;
        if(update.State.LastUpdateCounter!=context.UpdateCounter||update.State.RecordedWeight!=context.Weight||initializeInstance&&!update.Reinitialized)
            throw new ArgumentException("Stop machine/context differs.");
        _weights=AlsOverlayPoseWeights.MultiWay(velocity,4);
        if(initializeInstance)Array.Clear(_next);else _selectors.CopyTo(_next,0);
        _readCount=_initialCount=_evaluatorCount=0;
        for(var i=0;i<update.InitializationCount;i++)
        {
            var state=update.GetInitialization(i);_initialReads[_initialCount++]=Profile.States[state].Read;
            if(state>=3){_next[(state-3)*2]=default;_next[(state-3)*2+1]=default;}
        }
        for(var i=0;i<update.UpdateCount;i++)
        {
            var child=update.GetUpdate(i);var state=Profile.States[child.State];
            var path=context.WithWeight(child.Weight).WithState(Profile.Resources.MachinePropertyIndex,child.State,child.InertializationSync);
            if(child.State>=3)
            {
                // LayeredBoneBlend updates its layer first. The root is outside
                // either leg mask, hence layer root-motion weight is zero.
                var layer=path.WithWeight(path.Weight,0);
                for(var channel=0;channel<4;channel++)
                {
                    var weight=_weights[channel];if(weight<=AlsPoseBlender.WeightThreshold)continue;
                    var source=layer.WithWeight(layer.Weight*weight);
                    if(channel<2){Emit(state.Players[channel],source);continue;}
                    var right=channel==3;var slot=(child.State-3)*2+channel-2;
                    var selected=hips==(right?AlsRefactoredHipsDirection.RightBackward:AlsRefactoredHipsDirection.LeftBackward)?1:0;
                    var blend=AlsOverlayPoseWeights.BlendList(_next[slot],selected,right?[0,.1f]:[0,0],AlsTransitionBlend.Linear,false,false,context.Delta);
                    _next[slot]=blend.State;
                    void Leaf(int local,float w)
                    {
                        var leaf=source.WithWeight(source.Weight*w);if(local!=selected)leaf=leaf.AsInactive();
                        Emit(state.Players[2+(channel-2)*2+local],leaf);
                    }
                    if(blend.ZeroWeightPreviousChild>=0)Leaf(blend.ZeroWeightPreviousChild,0);
                    for(var local=0;local<2;local++)if(blend.State.Weights[local]>AlsPoseBlender.WeightThreshold)Leaf(local,blend.State.Weights[local]);
                }
            }
            // Cache weight is the whole state weight, never the sum of leaf weights.
            _reads[_readCount++]=new(state.Read,Profile.MovementCache,path);
        }
        _identity=context.Identity;_owner??=machine;_prepared=true;
    }
    private void Emit(int property,AlsPoseUpdateContext context)=>_evaluators[_evaluatorCount++]=new(property,context);
    public void ValidateCommit(long frame)
    {if(!_prepared||frame!=_identity.FrameId)throw new ArgumentException("Invalid Stop source commit.");}
    public void ValidateTraversal(long frame,AlsRefactoredStopRuntime machine)
    {
        ValidateCommit(frame);machine.ValidateCommit(frame);
        if(!ReferenceEquals(machine,_owner))throw new ArgumentException("Foreign Stop machine owner.");
    }
    public void Commit(long frame)
    {ValidateCommit(frame);(_selectors,_next)=(_next,_selectors);_committed=_identity;_hasCommitted=true;Cancel();}
    public void Cancel()=>_prepared=false;
}
