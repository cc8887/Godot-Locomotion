using GodotAls.Core.Actions;

namespace GodotAls.Core.Tests;

public sealed class AlsMontageEventQueueTests
{
    private static AlsMontageEvent Event(AlsMontageEventKind kind,long instance=1) =>
        new(kind,instance,4,8,kind==AlsMontageEventKind.BlendingOut,"Other",true);
    [Fact]
    public void EachNativeContainerCopiesItsDelegatesAndGlobalBroadcastReadsCurrentBindings()
    {
        var queue=new AlsMontageEventQueue();var calls=new List<string>();var old=new object();var next=new object();
        queue.BindGlobal(AlsMontageEventKind.Ended,old,_=>calls.Add("old-global"));
        queue.BeginQueueing();
        queue.Emit(Event(AlsMontageEventKind.Ended),_=>{
            calls.Add("captured-end");queue.RemoveGlobal(AlsMontageEventKind.Ended,old);
            queue.BindGlobal(AlsMontageEventKind.Ended,next,_=>calls.Add("new-global"));
        });
        queue.Emit(Event(AlsMontageEventKind.SectionChanged),_=>calls.Add("section"));
        queue.Emit(Event(AlsMontageEventKind.BlendedIn),_=>calls.Add("in"));
        queue.Emit(Event(AlsMontageEventKind.BlendingOut),_=>calls.Add("out"));
        Assert.Empty(calls);queue.Dispatch();
        Assert.Equal(["out","in","section","captured-end","new-global"],calls);
        Assert.False(queue.IsQueuing);
    }
    [Fact]
    public void CallbackEventsTriggerSynchronouslyWhenDispatchTurnsOffQueueing()
    {
        var queue=new AlsMontageEventQueue();var calls=new List<string>();queue.BeginQueueing();
        queue.Emit(Event(AlsMontageEventKind.BlendingOut),_=>{
            calls.Add("out-start");Assert.False(queue.IsQueuing);
            queue.Emit(Event(AlsMontageEventKind.Ended),_=>calls.Add("nested-end"));
            calls.Add("out-return");
        });
        queue.Emit(Event(AlsMontageEventKind.Ended,2),_=>calls.Add("queued-end"));queue.Dispatch();
        Assert.Equal(["out-start","nested-end","out-return","queued-end"],calls);
    }
    [Fact]
    public void ReenteringAdvanceKeepsSameContainerAdditionsAndVisitsLaterContainers()
    {
        var queue=new AlsMontageEventQueue();var calls=new List<string>();queue.BeginQueueing();
        queue.Emit(Event(AlsMontageEventKind.BlendingOut),_=>{
            calls.Add("out");queue.BeginQueueing();
            queue.Emit(Event(AlsMontageEventKind.BlendingOut,2),_=>calls.Add("next-out"));
            queue.Emit(Event(AlsMontageEventKind.Ended,2),_=>calls.Add("new-end"));
        });
        queue.Emit(Event(AlsMontageEventKind.Ended),_=>calls.Add("old-end"));
        queue.Dispatch();Assert.Equal(["out","old-end","new-end"],calls);
        Assert.True(queue.IsQueuing);Assert.Single(queue.Queued(AlsMontageEventKind.BlendingOut));
        queue.Dispatch();Assert.Equal(["out","old-end","new-end","next-out"],calls);
    }
}
