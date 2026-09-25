using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStopTests
{
    private static AlsRefactoredAnimationCatalog Catalog() => new(MantlingHostFixture.Read("refactored_animation_sources"),
        p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    private static readonly Lazy<AlsRefactoredStopResources> Data = new(() => new(MantlingHostFixture.Read("refactored_stance_machines"),Catalog()));

    [Fact]
    public void FixedPlantFramesUseOriginalSequencesAndIsolatedOutputs()
    {
        var catalog = Catalog(); var leaves = new AlsRefactoredStopEvaluators(catalog,Data.Value);
        Assert.Equal(new[] {4,4,6,4,7,7,21,21,24,24,23,21},leaves.Evaluators.ToArray().Select(d => d.Frame));
        Assert.Equal(79,leaves.BoneNames.Length);
        var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[leaves.CurveNames.Length];
        var expected = new AlsPrecisePose[79];
        foreach (var group in leaves.Evaluators.ToArray().GroupBy(d => d.Source))
        {
            var source = catalog.CompileAbsolutePoseWithCurves(group.Key); var sampler = source.Pose.CreateSampler(source.Curves);
            var sourceCurves = new AlsInertialCurve[source.Curves.Names.Length];
            foreach (var leaf in group)
            {
                // Independent call into the sequence sampler at the authored sampling-frame rate.
                var time = (float)((double)leaf.Frame * source.Pose.Data.FrameRateDenominator / source.Pose.Data.FrameRateNumerator);
                sampler.Sample(time,true,false,false,expected,sourceCurves); leaves.Sample(leaf.PropertyIndex,pose,curves);
                Assert.Equal(expected,pose);
                for (var c = 0; c < sourceCurves.Length; c++)
                    Assert.Equal(sourceCurves[c],curves[Array.FindIndex(leaves.CurveNames.ToArray(),n => n.Equals(source.Curves.Names[c],StringComparison.OrdinalIgnoreCase))]);
                Array.Clear(pose); Array.Clear(curves); leaves.Sample(leaf.PropertyIndex,pose,curves); Assert.Equal(expected,pose);
            }
        }
        Assert.Throws<ArgumentException>(() => leaves.Sample(0,pose,curves));
        Assert.Throws<ArgumentException>(() => leaves.Sample(41,new AlsPrecisePose[1],curves));
    }

    [Theory]
    [InlineData("frame")] [InlineData("source")] [InlineData("loop")] [InlineData("sync")]
    public void ChangedPlantEvaluatorLeavesAreRejected(string change)
    {
        var payload = JsonNode.Parse(Catalog().Read(AlsRefactoredRotatePlayers.Blueprint(false)).GetRawText())!;
        var runtime = payload["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == 41)!["runtime"]!;
        switch(change)
        {
            case "frame": runtime["explicitFrame"] = 5; break;
            case "source": runtime["sequence"] = "wrong"; break;
            case "loop": runtime["bShouldLoop"] = false; break;
            case "sync": runtime["method"] = "SyncGroup"; break;
        }
        using var doc = System.Text.Json.JsonDocument.Parse(payload.ToJsonString());
        Assert.Throws<ArgumentException>(() => AlsRefactoredStopEvaluators.Compile(doc.RootElement,Data.Value));
    }

    [Fact]
    public void OriginalResourcesAndBoundaryPriorityChooseCorrectFoot()
    {
        var r = Data.Value;
        Assert.Equal(new[] {52,50,47,44,31},r.States.ToArray().Select(s => s.RootPropertyIndex));
        Assert.Equal(new[] {18,17,16,15},r.Edges.ToArray().Select(e => e.RulePropertyIndex));
        Assert.Equal(12,r.States.ToArray().Sum(s => s.PlayerPropertyIndices.Length));
        (float Amount,int State)[] cases = [(-2,1),(MathF.BitDecrement(-.5f),1),(-.5f,1),(MathF.BitIncrement(-.5f),3),
            (MathF.BitDecrement(0),3),(0,3),(MathF.BitIncrement(0),4),(MathF.BitDecrement(.5f),4),(.5f,4),(MathF.BitIncrement(.5f),2),(2,2)];
        foreach (var (amount,state) in cases)
        {
            var machine = new AlsRefactoredStopRuntime(r); machine.Prepare(0,amount,.01f);
            Assert.Equal(state,machine.Candidate.State.CurrentState); Assert.Equal(1,machine.Candidate.TransitionCount);
            Assert.Equal(state is 1 or 3 ? "PlayStopLeftTransitionAnimation" : "PlayStopRightTransitionAnimation",machine.StateCallback!.Value.Function);
            var edge = machine.Candidate.GetTransitionIndex(0); Assert.Equal(state,r.Edges[edge].To);
            Assert.Equal(state is 1 or 2 ? 0 : .1f,r.Edges[edge].Seconds);
            machine.Commit(0); machine.Prepare(1,-amount,1);
            Assert.Equal(state,machine.Candidate.State.CurrentState); Assert.Null(machine.StateCallback);
        }
        Assert.Throws<ArgumentException>(() => new AlsRefactoredStopRuntime(r).Prepare(0,float.NaN,.01f));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void OuterStandingDrivesNestedStopEntryAndFadingSourceUpdates(int hz)
    {
        var outer = new AlsRefactoredStandingRuntime(new(MantlingHostFixture.Read("refactored_stance_machines"),Catalog()));
        var inner = new AlsRefactoredStopRuntime(Data.Value); var counter = new AlsGraphTraversalCounter(0,0);
        AlsRefactoredStandingObservation[] clocks = [new(12,0,0,false),new(9,0,0,false)];
        var entries = new List<int>(); var fading = 0;
        for (var frame = 0; frame < hz * 4; frame++)
        {
            var moving = frame % hz < hz / 2;
            outer.Prepare(frame,new(moving,false,false),clocks,1f/hz,updateCounter:counter);
            var update = outer.Candidate; var initializations = Enumerable.Range(0,update.InitializationCount).Select(update.GetInitialization).ToArray();
            var states = Enumerable.Range(0,update.UpdateCount).Select(update.GetUpdate).ToArray();
            var stopSources = states.Where(s => s.State == 2).ToArray(); Assert.True(stopSources.Length <= 1);
            if (stopSources.Length == 1)
            {
                var amount = (frame / hz) switch { 0 => -.75f, 1 => -.25f, 2 => .25f, _ => .75f };
                inner.Prepare(frame,amount,1f/hz,stopSources[0].Weight,initializations.Contains(2),counter);
                var candidate = inner.Candidate; var callback = inner.StateCallback;
                inner.Cancel(); inner.Prepare(frame,amount,1f/hz,stopSources[0].Weight,initializations.Contains(2),counter);
                Assert.Equal(candidate.State,inner.Candidate.State); Assert.Equal(callback,inner.StateCallback);
                if (callback.HasValue) entries.Add(callback.Value.State);
                if (update.State.CurrentState != 2) fading++;
                inner.ValidateCommit(frame); outer.ValidateCommit(frame); inner.Commit(frame);
            }
            outer.Commit(frame); counter = counter.Next((ulong)frame+1);
        }
        Assert.Equal(new[] {1,3,4,2},entries); Assert.True(fading > 0);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RelevanceResetBlendAndCancelRetryPreserveStopSelection(int hz)
    {
        var r = Data.Value; var machine = new AlsRefactoredStopRuntime(r); var counter = new AlsGraphTraversalCounter(0,0);
        var seen = new HashSet<int>(); var blended = 0; var callbacks = 0;
        for (var frame = 0; frame < hz * 4; frame++)
        {
            var phase = frame / (hz / 2); var reset = frame % (hz / 2) == 0;
            if (reset && frame > 0) counter = counter.Next((ulong)frame).Next((ulong)frame);
            var amount = (phase % 4) switch { 0 => -.75f, 1 => -.25f, 2 => .25f, _ => .75f };
            machine.Prepare(frame,amount,1f/hz,frame % 7 == 0 ? 0 : .8f,updateCounter:counter);
            var candidate = machine.Candidate; var callback = machine.StateCallback;
            seen.Add(candidate.State.CurrentState); if (candidate.UpdateCount > 1) blended++;
            if (callback.HasValue) callbacks++;
            Assert.Equal(reset,callback.HasValue);
            machine.Cancel(); machine.Prepare(frame,amount,1f/hz,frame % 7 == 0 ? 0 : .8f,updateCounter:counter);
            Assert.Equal(candidate.State,machine.Candidate.State); Assert.Equal(callback,machine.StateCallback);
            machine.Commit(frame); counter = counter.Next((ulong)frame+1);
        }
        Assert.Equal(4,seen.Count); Assert.Equal(8,callbacks); Assert.True(blended > 0);
    }

    [Theory]
    [InlineData("catalog")] [InlineData("priority")] [InlineData("duration")] [InlineData("notify")]
    [InlineData("automatic")] [InlineData("player")] [InlineData("root")] [InlineData("logic")]
    public void AlteredStopMachineIsRejected(string change)
    {
        var root = JsonNode.Parse(MantlingHostFixture.Read("refactored_stance_machines"))!; var m = root["stances"]![0]!["bakedMachines"]![3]!;
        switch(change)
        {
            case "catalog": root["catalogSha256"] = new string('0',64); break;
            case "priority": m["states"]![0]!["transitions"]![0]!["transitionIndex"] = 1; break;
            case "duration": m["transitions"]![0]!["crossfadeDuration"] = .1; break;
            case "notify": m["transitions"]![0]!["startNotify"] = 1; break;
            case "automatic": m["states"]![0]!["transitions"]![0]!["bAutomaticRemainingTimeRule"] = true; break;
            case "player": m["states"]![3]!["playerNodeIndices"]![0] = 0; break;
            case "root": m["states"]![0]!["stateRootNodeIndex"] = 0; break;
            case "logic": m["transitions"]![0]!["logicType"] = "TLT_Inertialization"; break;
        }
        Assert.Throws<ArgumentException>(() => new AlsRefactoredStopResources(root.ToJsonString(),Catalog()));
    }
}
