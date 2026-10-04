using System.Collections.Immutable;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMainSlotVisit(string Kind,int Id,AlsPoseUpdateContext Context);

// The five real Slot update boundaries, including the deferred split. This
// owner consumes the frozen physical bank; it never advances a second clock.
internal sealed class LyraMainSlotsTraversal:ILyraMontageNotifySlots
{
    private readonly LyraMainSlotUpdateOwner _slots;
    private readonly List<LyraMainSlotVisit> _visits=[];
    private AlsFrameIdentity _identity;private float _dynamicAlpha;private bool _pending,_resolved;
    public AlsPoseUpdateContext? FullBodySource {get;private set;}
    public AlsPoseUpdateContext? AimingSource {get;private set;}
    public AlsPoseUpdateContext? AdditivesSource {get;private set;}
    public ImmutableArray<LyraMainSlotVisit> Visits=>_visits.ToImmutableArray();
    public LyraMainSlotsTraversal(AlsMontageRuntime runtime){_slots=new(runtime);}
    public void Begin(AlsMontageFrame frame,in AlsPoseUpdateContext context,bool visited,bool initialize,float dynamicAlpha)
    {
        if(_pending||frame.Identity!=context.Identity||!float.IsFinite(dynamicAlpha))
            throw new InvalidOperationException("Invalid Main Slot graph candidate.");
        _slots.Begin(frame,initialize);_identity=context.Identity;_dynamicAlpha=Math.Clamp(dynamicAlpha,0,1);
        _pending=true;_resolved=false;_visits.Clear();FullBodySource=AimingSource=AdditivesSource=null;
        if(!visited)return;
        try
        {
            FullBodySource=Slot(4,context);
            if(FullBodySource is {} full)
            {
                AimingSource=Slot(3,full);
                if(AimingSource is {} aiming)Record("aiming",6,aiming);
                AdditivesSource=full.WithWeight(full.Weight*.65f);Record("leaf",5,AdditivesSource.Value);
            }
        }
        catch{Cancel();throw;}
    }
    private void Record(string kind,int id,in AlsPoseUpdateContext context)=>_visits.Add(new(kind,id,context));
    private AlsPoseUpdateContext? Slot(int slot,in AlsPoseUpdateContext context)
    {
        Record("slot",slot,context);var source=_slots.Update(slot,context);
        if(!source.Updated)return null;Record("source",slot,source.Context);return source.Context;
    }
    internal void ValidateResolve(AlsFrameIdentity identity)
    {
        if(!_pending||_resolved||identity!=_identity)throw new InvalidOperationException("Foreign or repeated Main Slot traversal.");
        _slots.ValidateCommit(identity);_resolved=true;
    }
    internal void CompleteUnvisited(AlsFrameIdentity identity)
    {
        if(_visits.Count!=0||FullBodySource is not null||AimingSource is not null||AdditivesSource is not null)
            throw new InvalidOperationException("Unvisited Main Slots contain graph traversal.");
        ValidateResolve(identity);
    }
    internal void UpdateCachedSource(int node,in AlsPoseUpdateContext context,AlsPoseCacheTraversal traversal)
    {
        if(!_pending||!_resolved||context.Identity!=_identity)throw new InvalidOperationException("Foreign Main Slot cache context.");
        Record("cache",node,context);
        if(node==181)traversal.Use(77,context);
        else if(node==78&&Slot(2,context) is {} split)
        {
            // Main's split visits the upper branch first. Its root mask is
            // zero; base and dynamic additive retain the complete modifier.
            if(Slot(0,split.WithWeight(split.Weight,0)) is {} upper)traversal.Use(82,upper);
            traversal.Use(80,split);
            if(_dynamicAlpha>AlsPoseBlender.WeightThreshold&&Slot(1,split.WithWeight(split.Weight*_dynamicAlpha)) is {} reference)
                Record("leaf",79,reference);
        }
        else if(node==83)Record("leaf",83,context);
    }
    public AlsSlotWeights Weights(int slot)=>_slots.Weights(slot);
    public ushort NotifyRelevantMask=>_slots.NotifyRelevantMask;
    public ushort CurrentNotifyRelevantMask=>_slots.CurrentNotifyRelevantMask;
    public void ValidateCommit(AlsFrameIdentity identity)
    {if(!_pending||!_resolved)throw new InvalidOperationException("Incomplete Main Slot graph.");_slots.ValidateCommit(identity);}
    public void Commit(AlsFrameIdentity identity){ValidateCommit(identity);_slots.Commit(identity);_pending=false;}
    public void Cancel(){_slots.Cancel();_pending=_resolved=false;FullBodySource=AimingSource=AdditivesSource=null;_visits.Clear();}
}
