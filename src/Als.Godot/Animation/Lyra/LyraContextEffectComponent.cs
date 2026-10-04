using System.Collections.Immutable;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraContextEffectsHit(bool Hit,AlsDoubleVector Point,AlsDoubleVector Normal,int Surface,
    ulong Collider,int Shape,Transform3D ColliderTransform);
internal readonly record struct LyraContextEffectsMessage(LyraNotifyCallback Callback,string Bone,string Effect,string Animation,
    AlsDoubleVector Start,AlsDoubleVector End,LyraContextEffectsHit Hit,AlsDoubleVector Location,AlsDoubleVector Rotation,
    ImmutableArray<string> Contexts,AlsDoubleVector Scale,float Volume,float Pitch);
internal readonly record struct LyraContextEffectsDelivery(LyraContextEffectsMessage Message,ImmutableArray<string> Contexts,LyraContextEffectSelection Selection);
internal interface ILyraContextEffectsReceiver
{void AnimMotionEffect(in LyraContextEffectsMessage message,LyraContextEffectsCatalog catalog,bool libraryLoaded);}

// Counterpart of the actor component interface. The typed message retains full
// hit, attach and original payload data. Audio resources are selected but their
// playback is deferred; the original DefaultSkin library has no Niagara rows.
public partial class LyraContextEffectComponent:Node,ILyraContextEffectsReceiver
{
    [Signal] public delegate void ContextEffectSelectedEventHandler(string effect,string bone,int surface,int audioCount,int vfxCount);
    public bool ConvertPhysicalSurfaceToContext {get;set;}=true;
    internal ImmutableArray<string> CurrentContexts {get;private set;}=[];
    internal ImmutableArray<LyraContextEffectsDelivery> LastDeliveries {get;private set;}=[];
    internal event Action<LyraContextEffectsDelivery>? Selected;
    public long DeliveredMessages {get;private set;}
    internal void UpdateContexts(ImmutableArray<string> contexts,LyraContextEffectsCatalog catalog)
    {catalog.ValidateContexts(contexts);CurrentContexts=contexts;}
    internal void BeginDispatch()=>LastDeliveries=[];
    void ILyraContextEffectsReceiver.AnimMotionEffect(in LyraContextEffectsMessage message,LyraContextEffectsCatalog catalog,bool libraryLoaded)
    {
        if(!GodotThread.IsMainThread()||!IsInsideTree()||IsQueuedForDeletion())throw new InvalidOperationException("ContextEffects receiver expired.");
        var tags=message.Contexts;
        foreach(var tag in CurrentContexts)if(!tags.Contains(tag))tags=tags.Add(tag);
        if(ConvertPhysicalSurfaceToContext&&message.Hit.Surface>=0&&catalog.Surface(message.Hit.Surface) is {} surface&&!tags.Contains(surface))tags=tags.Add(surface);
        var selection=libraryLoaded?catalog.Select(message.Effect,tags):new LyraContextEffectSelection([],[]);
        var delivery=new LyraContextEffectsDelivery(message,tags,selection);DeliveredMessages++;LastDeliveries=LastDeliveries.Add(delivery);
        Selected?.Invoke(delivery);
        EmitSignal(SignalName.ContextEffectSelected,message.Effect,message.Bone,message.Hit.Surface,selection.Audio.Length,selection.Vfx.Length);
    }
}
