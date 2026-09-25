using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDirectionNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false,30)]
    [InlineData(false,60)]
    [InlineData(false,120)]
    [InlineData(true,30)]
    [InlineData(true,60)]
    [InlineData(true,120)]
    public void OriginalMachineMatchesContinuousStatesStacksAndNotifies(bool crouching,int hz)
    {
        var kind=crouching?"Crouching":"Standing";
        var catalog=new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        var resources=new AlsRefactoredDirectionResources(MantlingHostFixture.Read("refactored_stance_machines"),catalog,crouching);
        var runtime=new AlsRefactoredDirectionRuntime(resources);
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_direction_trace_"+kind));var root=doc.RootElement;
        Assert.Equal(AlsRefactoredRotatePlayers.Blueprint(crouching),root.GetProperty("source").GetString());
        foreach(var name in new[]{"refactored_animation_sources","refactored_stance_machines"})
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(name)))),root.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        if(!crouching)Assert.Equal("ActivatePivot",root.GetProperty("notifyDefinitions")[0].GetProperty("name").GetString());
        var trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==hz+"hz");
        var frame=0;var stacked=0;var notifies=0;var states=new HashSet<int>();var edges=new HashSet<int>();
        float maxTime=0,maxAlpha=0,maxWeight=0;
        foreach(var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input=row.GetProperty("input");var grounded=input.GetProperty("groundedState");var direction=grounded.GetProperty("MovementDirection");
            var value=new AlsRefactoredDirectionInput(direction.GetProperty("bForward").GetBoolean(),direction.GetProperty("bBackward").GetBoolean(),
                direction.GetProperty("bLeft").GetBoolean(),direction.GetProperty("bRight").GetBoolean(),grounded.GetProperty("HipsDirectionLockAmount").GetSingle(),
                input.GetProperty("feetState").GetProperty("FeetCrossingAmount").GetSingle());
            var delta=input.GetProperty("delta").GetSingle();var reset=input.GetProperty("reset").GetBoolean();
            runtime.Prepare(frame,value,delta,reinitialize:reset);var next=runtime.Candidate;var state=next.State;var context=$"{kind}/{hz}/{frame}";
            Assert.True(state.CurrentState==row.GetProperty("current").GetInt32(),context+" state");states.Add(state.CurrentState);
            maxTime=MathF.Max(maxTime,MathF.Abs(state.ElapsedSeconds-row.GetProperty("elapsed").GetSingle()));
            var native=row.GetProperty("transitions");Assert.True(state.Transitions.Count==native.GetArrayLength(),context+" stack length");
            if(state.Transitions.Count>1)stacked++;
            for(var i=0;i<state.Transitions.Count;i++)
            {
                var actual=state.Transitions.GetTransition(i);var edge=native[i];
                Assert.Equal(edge.GetProperty("from").GetInt32(),actual.From);Assert.Equal(edge.GetProperty("to").GetInt32(),actual.To);
                Assert.Equal(new[]{state.GetActiveEdge(i)},edge.GetProperty("edges").EnumerateArray().Select(e=>e.GetInt32()));edges.Add(state.GetActiveEdge(i));
                maxTime=MathF.Max(maxTime,MathF.Abs(actual.Elapsed-edge.GetProperty("elapsed").GetSingle()));
                maxTime=MathF.Max(maxTime,MathF.Abs(actual.Duration-edge.GetProperty("duration").GetSingle()));
                maxAlpha=MathF.Max(maxAlpha,MathF.Abs(actual.Alpha-edge.GetProperty("alpha").GetSingle()));
            }
            for(var s=0;s<6;s++)maxWeight=MathF.Max(maxWeight,MathF.Abs(AlsTransitionStack.Weight(state.Transitions,s)-row.GetProperty("weights")[s].GetSingle()));
            var updates=row.GetProperty("updates").EnumerateArray().Select(v=>v.GetInt32()).ToList();
            if(native.GetArrayLength()==0&&!updates.Contains(state.CurrentState))updates.Add(state.CurrentState);
            Assert.True(updates.SequenceEqual(Enumerable.Range(0,next.UpdateCount).Select(i=>next.GetUpdate(i).State)),context+" update order");
            Assert.True(row.GetProperty("notifies").EnumerateArray().Select(n=>n.GetInt32()).SequenceEqual(
                Enumerable.Range(0,next.EventCount).Select(i=>next.GetEvent(i).NotifyIndex)),context+" notify order");notifies+=next.EventCount;
            Assert.True(maxTime<=2e-6f&&maxAlpha<=2e-6f&&maxWeight<=2e-6f,$"{context} time={maxTime:R} alpha={maxAlpha:R} weight={maxWeight:R}");
            if(frame%17==0)
            {
                runtime.Cancel();runtime.Prepare(frame,value,delta,reinitialize:reset);
                Assert.Equal(state.CurrentState,runtime.Candidate.State.CurrentState);
                Assert.Equal(next.EventCount,runtime.Candidate.EventCount);
            }
            runtime.Commit(frame++);
        }
        Assert.Equal(hz*4+189,frame);Assert.Equal(6,states.Count);Assert.True(stacked>0);Assert.Equal(Enumerable.Range(0,24),edges.Order());
        Assert.Equal(crouching,notifies==0);
        output.WriteLine($"{kind}/{hz} frames={frame} stacked={stacked} edges={edges.Count} notifies={notifies} time={maxTime:R} alpha={maxAlpha:R} weight={maxWeight:R}");
    }
}
