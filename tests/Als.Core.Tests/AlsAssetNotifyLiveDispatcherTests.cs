using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Core.Tests;

public sealed class AlsAssetNotifyLiveDispatcherTests
{
    private static AlsAssetNotifyPolicy Policy(int id,int state=-1,int notify=-1)=>new(
        id,id,0,notify,state,id,0,1,AlsAssetNotifyFilterType.None,0,AlsTimelineTickMode.Queued,true,true,true);
    private static AlsAssetNotifyActiveState State(int policy,int handle,int instance=10,
        AlsAssetNotifySourceKind kind=AlsAssetNotifySourceKind.AssetPlayer,uint source=1,bool noMerge=false)=>
        new(new(new(policy,handle,.1f,true,false),kind,source,noMerge,.4f),instance);
    private static AlsAssetNotifyLiveDispatcher<AlsAssetNotifyActiveState> Dispatcher(
        AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState> active,ImmutableArray<AlsAssetNotifyPolicy> policies,int allocator=100)=>
        new(active,policies,s=>s,(s,id)=>s with{InstanceId=id},allocator);
    private static int[] Handles(AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState> active)=>
        active.States.Select(s=>s.Input.Reference.OccurrenceHandleId).ToArray();
    private static readonly AlsAssetNotifyDispatchContext Context=new(AlsAssetNotifyDispatchMode.ForceAllSources,.02f);

    [Fact]
    public void ReplaysAllOriginalNativeLifecycleCasesWithoutCallbackMutation()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","P3","v4_notify_lifecycle_native.json")));
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);var root=doc.RootElement;
        var policies=root.GetProperty("policies").Deserialize<AlsAssetNotifyPolicy[]>(options)!.ToImmutableArray();
        int count=0;
        foreach(var row in root.GetProperty("cases").EnumerateArray())
        {
            var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(s=>s.Input.SourceInstanceId);
            active.Replace(row.GetProperty("before").Deserialize<AlsAssetNotifyActiveState[]>(options)!.ToImmutableArray());
            var dispatcher=Dispatcher(active,policies,row.GetProperty("nextInstanceId").GetInt32());
            var calls=new List<AlsAssetNotifyCallback>();
            var queued=row.GetProperty("queued").Deserialize<AlsAssetNotifyDispatchInput[]>(options)!.Select(i=>new AlsAssetNotifyActiveState(i,0)).ToArray();
            Assert.True(dispatcher.Dispatch(queued,row.GetProperty("context").Deserialize<AlsAssetNotifyDispatchContext>(options),
                (k,s,seconds)=>calls.Add(new(k,s,seconds))));
            Assert.Equal(row.GetProperty("callbacks").Deserialize<AlsAssetNotifyCallback[]>(options)!,calls);
            Assert.Equal(row.GetProperty("expected").Deserialize<AlsAssetNotifyActiveState[]>(options)!,active.States);
            Assert.Equal(row.GetProperty("candidateNextInstanceId").GetInt32(),dispatcher.NextInstanceId);count++;
        }
        Assert.Equal(169,count);
    }

    [Fact]
    public void InstantAndBeginSeeUnmatchedOldInventoryAndTickSeesReplacedInventory()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);
        active.Replace([State(0,0),State(1,1),State(2,2)]);
        var d=Dispatcher(active,[Policy(0,0),Policy(1,1),Policy(2,2),Policy(3,3),Policy(4,notify:4)]);
        var seen=new List<(AlsAssetNotifyCallbackKind Kind,int Handle,int[] Active)>();
        Assert.True(d.Dispatch([State(0,9),State(4,4),State(3,3)],Context,
            (k,s,_)=>seen.Add((k,s.Input.Reference.OccurrenceHandleId,Handles(active)))));
        Assert.Equal(new[]{AlsAssetNotifyCallbackKind.Notify,AlsAssetNotifyCallbackKind.End,AlsAssetNotifyCallbackKind.End,
            AlsAssetNotifyCallbackKind.Begin,AlsAssetNotifyCallbackKind.Tick,AlsAssetNotifyCallbackKind.Tick},seen.Select(s=>s.Kind));
        Assert.Equal(new[]{4,2,1,3,9,3},seen.Select(s=>s.Handle));
        foreach(var s in seen.Take(4))Assert.Equal(new[]{2,1},s.Active);
        foreach(var s in seen.Skip(4))Assert.Equal(new[]{9,3},s.Active);
        Assert.Equal(new[]{10,101},active.States.Select(s=>s.InstanceId));
    }

    [Fact]
    public void BeginClearIsOverwrittenAtNativeArraySwitch()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);active.Replace([State(0,0)]);
        var d=Dispatcher(active,[Policy(0,0),Policy(1,1)]);var ticks=new List<int>();
        Assert.True(d.Dispatch([State(1,1)],Context,(k,s,_)=>
        {if(k==AlsAssetNotifyCallbackKind.Begin)active.Clear();if(k==AlsAssetNotifyCallbackKind.Tick)ticks.Add(s.InstanceId);}));
        Assert.Equal(new[]{100},ticks);Assert.Equal(new[]{1},Handles(active));
    }

    [Fact]
    public void TickClearStopsRemainingTicksAndRetainsEmptyInventory()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);
        var d=Dispatcher(active,[Policy(0,0),Policy(1,1)]);int ticks=0;
        Assert.True(d.Dispatch([State(0,0),State(1,1)],Context,(k,_,_)=>{if(k==AlsAssetNotifyCallbackKind.Tick){ticks++;active.Clear();}}));
        Assert.Equal(1,ticks);Assert.Empty(active.States);
    }

    [Fact]
    public void AppendDuringEndAndTickUsesLiveForwardTraversal()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);active.Replace([State(0,0)]);
        var d=Dispatcher(active,[Policy(0,0),Policy(1,1),Policy(2,2)]);var calls=new List<(AlsAssetNotifyCallbackKind,int)>();
        Assert.True(d.Dispatch([State(1,1)],Context,(k,s,_)=>
        {
            int handle=s.Input.Reference.OccurrenceHandleId;calls.Add((k,handle));
            if(k==AlsAssetNotifyCallbackKind.End&&handle==0)active.Append(State(2,2,12));
            if(k==AlsAssetNotifyCallbackKind.Tick&&handle==1)active.Append(State(2,3,13));
        }));
        Assert.Equal(new[]{(AlsAssetNotifyCallbackKind.End,0),(AlsAssetNotifyCallbackKind.End,2),
            (AlsAssetNotifyCallbackKind.Begin,1),(AlsAssetNotifyCallbackKind.Tick,1),(AlsAssetNotifyCallbackKind.Tick,3)},calls);
        Assert.Equal(new[]{1,3},Handles(active));
    }

    [Fact]
    public void InstantMutationChangesSubsequentReuseAndAllocator()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);active.Replace([State(0,0)]);
        var d=Dispatcher(active,[Policy(0,0),Policy(1,notify:1),Policy(2)]);var ids=new List<int>();
        Assert.True(d.Dispatch([State(2,2),State(1,1),State(0,9)],Context,(k,s,_)=>
        {if(k==AlsAssetNotifyCallbackKind.Notify){ids.Add(s.InstanceId);active.Clear();}}));
        Assert.Equal(new[]{-1,100},ids);Assert.Equal(101,active.States.Single().InstanceId);Assert.Equal(102,d.NextInstanceId);
    }

    [Fact]
    public void NestedDispatchReusesSameInventoryAndAllocator()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);
        var d=Dispatcher(active,[Policy(0,0),Policy(1,1)]);bool nested=false;var ticks=new List<int>();
        Assert.True(d.Dispatch([State(0,0)],Context,(k,s,_)=>
        {
            if(k!=AlsAssetNotifyCallbackKind.Tick)return;ticks.Add(s.Input.Reference.OccurrenceHandleId);
            if(!nested){nested=true;Assert.True(d.Dispatch([State(1,1)],Context,(nk,ns,_)=>
                {if(nk==AlsAssetNotifyCallbackKind.Tick)ticks.Add(ns.Input.Reference.OccurrenceHandleId);}));}
        }));
        Assert.Equal(new[]{0,1},ticks);Assert.Equal(new[]{1},Handles(active));Assert.Equal(102,d.NextInstanceId);
    }

    [Fact]
    public void FilterRetainsLifecycleButSuppressesCallbacksAndEndAllIgnoresStateFilter()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);
        var d=Dispatcher(active,[Policy(0,0)]);int callbacks=0;
        Assert.True(d.Dispatch([State(0,0)],Context,(_,_,_)=>callbacks++,shouldTrigger:_=>false));
        Assert.Equal(0,callbacks);Assert.Single(active.States);
        Assert.True(d.Dispatch([],new(AlsAssetNotifyDispatchMode.EndAll,0),(_,_,_)=>callbacks++,shouldTrigger:_=>false));
        Assert.Equal(1,callbacks);Assert.Empty(active.States);
    }

    [Fact]
    public void InvalidatedEndAbortsBeforeBeginAndTick()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);active.Replace([State(0,0)]);
        var d=Dispatcher(active,[Policy(0,0),Policy(1,1)]);var kinds=new List<AlsAssetNotifyCallbackKind>();
        Assert.False(d.Dispatch([State(1,1)],Context,(k,_,_)=>{kinds.Add(k);active.Clear();}));
        Assert.Equal(new[]{AlsAssetNotifyCallbackKind.End},kinds);Assert.Empty(active.States);Assert.Equal(101,d.NextInstanceId);
    }

    [Fact]
    public void LifetimeGuardStopsBeforeArraySwitchAndInvalidInputHasNoEffects()
    {
        var active=new AlsMontageNotifyStateEndList<AlsAssetNotifyActiveState>(_=>0);var d=Dispatcher(active,[Policy(0,0)]);
        int calls=0;bool alive=true;
        Assert.Throws<ArgumentException>(()=>d.Dispatch([State(0,0) with{Input=State(0,0).Input with{Duration=float.NaN}}],Context,(_,_,_)=>calls++));
        Assert.Equal(100,d.NextInstanceId);Assert.Empty(active.States);Assert.Equal(0,calls);
        Assert.False(d.Dispatch([State(0,0)],Context,(_,_,_)=>{calls++;alive=false;},continueDispatch:()=>alive));
        Assert.Equal(1,calls);Assert.Empty(active.States);
    }
}
