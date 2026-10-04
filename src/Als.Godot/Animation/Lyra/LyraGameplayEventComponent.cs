using System.Collections.Immutable;
using Godot;

namespace GodotAls.Animation.Lyra;

// SendGameplayEventToActor supplies an event tag separately from its payload.
// These original two Blueprints pass the default FGameplayEventData: even its
// EventTag remains invalid (FName text "None"). Animation provenance is not
// inserted into payload.
internal readonly record struct LyraGameplayEvent(string Tag,string PayloadEventTag="None",float Magnitude=0);

// Actor-owned event receiver. Abilities may subscribe through the Godot signal;
// this component does not invent damage, ammunition or a full GAS runtime.
public partial class LyraGameplayEventComponent:Node
{
    [Signal] public delegate void GameplayEventReceivedEventHandler(string eventTag,string payloadEventTag,float magnitude);
    public long MeleeEvents {get;private set;}
    public long ReloadEvents {get;private set;}
    internal ImmutableArray<LyraGameplayEvent> LastEvents {get;private set;}=[];
    internal void BeginDispatch()=>LastEvents=[];
    internal void Send(in LyraGameplayEvent message)
    {
        if(!GodotThread.IsMainThread()||!IsInsideTree()||IsQueuedForDeletion())
            throw new InvalidOperationException("Gameplay event receiver is not live on the main thread.");
        switch(message.Tag)
        {
            case "GameplayEvent.MeleeHit":MeleeEvents++;break;
            case "GameplayEvent.ReloadDone":ReloadEvents++;break;
            default:throw new NotSupportedException("Unbound original gameplay event.");
        }
        LastEvents=LastEvents.Add(message);
        EmitSignal(SignalName.GameplayEventReceived,message.Tag,message.PayloadEventTag,message.Magnitude);
    }
}
