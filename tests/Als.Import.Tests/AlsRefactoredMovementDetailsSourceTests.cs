using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementDetailsSourceTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog;
        public readonly AlsRefactoredMovementDetailsPoseGraph Graph;
        public readonly AlsRefactoredSyncBank Bank;
        public readonly IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> Triangles;
        public Fixture()
        {
            var index = MantlingHostFixture.Read("refactored_animation_sources");
            byte[] Read(string p) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p));
            Catalog = new(index, Read); Graph = new(Catalog, new(MantlingHostFixture.Read("refactored_stance_machines"), Catalog));
            Bank = new(MantlingHostFixture.Read("refactored_sync_inputs"), Catalog);
            Triangles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), index, Read);
        }
        public AlsRefactoredSourcePlayerRuntime Players(int first = 0)
        {
            var definitions = Graph.Players.Bind(first, new Dictionary<string, int> { ["Movement"] = 0, ["Run Start"] = 1, ["First Pivot"] = 2, ["Second Pivot"] = 3 });
            return new(Catalog, Bank, Triangles, Enumerable.Range(0, first).Select(i => new AlsRefactoredSourcePlayerDefinition(i, definitions[0].Source, -1)).Concat(definitions).ToArray());
        }
    }
    private static readonly Lazy<Fixture> Data = new(() => new());
    private static AlsPoseUpdateContext Context(long frame, AlsGraphTraversalCounter counter, float delta) =>
        new AlsPoseUpdateContext(new(frame, 7, 1), .8f, delta, .35f).WithUpdateCounter(counter).WithState(999, 2).WithInertialization(119, true);
    private static AlsRefactoredMovementDetailsInput Run(bool pivot = false, float weight = 1) => new("Als.Gait.Running", 1, 1, weight, pivot);

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RealSharedClocksDriveAutomaticExitsWithRollbackAndAllSixteenSources(int hz)
    {
        var f = Data.Value; const int first = 7;
        var machine = new AlsRefactoredMovementDetailsRuntime(f.Graph.Resources); var source = new AlsRefactoredMovementDetailsSourceRuntime(f.Graph, first); var players = f.Players(first);
        var counter = new AlsGraphTraversalCounter(0, 0); var stage = 0; var seen = new HashSet<int>(); var resets = 0; var mixed = 0; var automatic = 0; var inertialTicks = 0; var zeroTicks = 0;
        int[] target = [5,2,0,1,2,3,4,3,2,0,2];
        for (var frame = 0; frame < hz * 6; frame++)
        {
            var input = stage is 2 or 9 ? new AlsRefactoredMovementDetailsInput("", 1, 0, 1, false) :
                Run(stage is 5 or 6 or 7, stage == 0 ? .5f : 1) with { GroundedAmount = stage == 10 ? .5f : 1 };
            var delta = frame % 31 == 0 ? 0 : 1f / hz; var context = Context(frame, counter, delta);
            if (frame % 17 == 0) context = context.AsInactive();
            // Keep the state traversed with zero outer weight while its local
            // channels are relevant, as happens in an old transition branch.
            if (frame == 1) context = context.WithWeight(0);
            var velocity = frame % 37 == 0 ? Vector4.Zero : new Vector4(.1f, .2f, .3f, .4f);
            var prior = source.CommittedObservations.ToArray(); var from = machine.CommittedState.CurrentState;
            machine.Prepare(frame, input, source.CommittedObservations, delta, context.Weight, updateCounter: counter);
            source.Prepare(machine, context, velocity);
            var ticks = source.SourceInputs.ToArray(); var contexts = source.SourceContexts.ToArray(); var reads = source.CacheReads.ToArray(); var commands = source.CallbackCommands.ToArray();
            var initial = source.CacheInitializationReads.ToArray();
            Assert.Equal(machine.Candidate.UpdateCount, reads.Length); Assert.Equal(machine.Candidate.InitializationCount, initial.Length);
            Assert.All(reads, read => { Assert.Equal(67, read.CachePropertyIndex); Assert.Equal(119, read.Context.SkippedUpdateHandler); Assert.Equal(2, read.Context.StateCount); Assert.Equal(.35f, read.Context.RootMotionWeight); Assert.Equal(context.IsActive, read.Context.IsActive); });
            Assert.All(contexts, path => { Assert.Equal(new AlsActiveAnimationState(999, 2), path.GetState(0)); Assert.Equal(117, path.GetState(1).MachineNodeIndex); Assert.Equal(119, path.InertializationRequester); });
            mixed += reads.Length > 1 ? 1 : 0;
            for (var i = 0; i < ticks.Length; i++) { Assert.Equal(context.IsActive, contexts[i].IsActive); Assert.Equal(contexts[i].InertializationSync, ticks[i].RequestedInertialization); inertialTicks += ticks[i].RequestedInertialization ? 1 : 0; zeroTicks += ticks[i].Weight == 0 ? 1 : 0; }
            foreach (var command in commands) { Assert.Equal(AlsRefactoredStanceFunction.ResetPivot, command.Function); resets++; }
            players.Prepare(frame, ticks, delta);
            foreach (var tick in ticks) { players.Evaluate(frame, tick.PlayerId); seen.Add(tick.PlayerId); Assert.Equal(79, players.Pose(tick.PlayerId).Length); }
            source.CaptureSourceTimes(frame, players); var observations = source.CandidateObservations.ToArray(); var histories = players.Players.ToArray();
            source.Cancel(); players.Cancel(); Assert.Equal(prior, source.CommittedObservations.ToArray());
            source.Prepare(machine, context, velocity);
            Assert.Equal(ticks, source.SourceInputs.ToArray()); Assert.Equal(contexts, source.SourceContexts.ToArray()); Assert.Equal(reads, source.CacheReads.ToArray());
            Assert.Equal(commands, source.CallbackCommands.ToArray()); Assert.Equal(initial, source.CacheInitializationReads.ToArray());
            players.Prepare(frame, source.SourceInputs, delta); source.CaptureSourceTimes(frame, players);
            Assert.Equal(histories, players.Players.ToArray()); Assert.Equal(observations, source.CandidateObservations.ToArray());
            var current = machine.Candidate.State.CurrentState;
            if (from is 1 or 3 or 4 or 5 && current == 2 && machine.Candidate.TransitionCount > 0)
            {
                var selected = machine.SelectedPlayerProperties[from]; Assert.True(selected >= 0);
                var obs = Assert.Single(prior, p => p.PropertyIndex == selected);
                var asset = Assert.Single(f.Graph.Resources.TimingPlayers.ToArray(), p => p.PropertyIndex == selected);
                Assert.Equal(asset.Length, obs.Time); automatic++;
            }
            source.ValidateCommit(frame); players.ValidateCommit(frame); machine.ValidateCommit(frame);
            source.Commit(frame); players.Commit(frame); machine.Commit(frame);
            if (current == target[stage]) stage = (stage + 1) % target.Length;
            counter = counter.Next((ulong)frame + 1);
        }
        Assert.Equal(16, seen.Count); Assert.True(resets >= 3, "Missing ResetPivot coverage."); Assert.True(mixed > 0, "Missing overlapping cache reads.");
        Assert.True(automatic >= 3, "Missing real-clock automatic exits."); Assert.True(inertialTicks > 0, "Missing inertial source contexts."); Assert.True(zeroTicks > 0, "Missing zero outer weight source updates.");
    }

    [Fact]
    public void HiddenChannelsKeepPendingInitializationAndInstanceResetIsAtomic()
    {
        var f = Data.Value; var machine = new AlsRefactoredMovementDetailsRuntime(f.Graph.Resources);
        var source = new AlsRefactoredMovementDetailsSourceRuntime(f.Graph, 0); var players = f.Players(); var counter = new AlsGraphTraversalCounter(0, 0);
        void Prepare(int frame, Vector4 velocity, bool reset = false)
        {
            machine.Prepare(frame, Run(), source.CommittedObservations, .01f, .8f, reset, counter);
            source.Prepare(machine, Context(frame, counter, .01f), velocity, reset);
        }
        void Commit(int frame) { players.Prepare(frame, source.SourceInputs, .01f); source.CaptureSourceTimes(frame, players); source.Commit(frame); players.Commit(frame); machine.Commit(frame); counter = counter.Next((ulong)frame + 1); }
        Prepare(0, Vector4.Zero); Assert.Empty(source.SourceInputs.ToArray()); Assert.Equal(new[] { 107,110 }, source.CacheInitializationReads.ToArray()); Commit(0);
        Prepare(1, new(1, 0, 0, 0)); Assert.True(Assert.Single(source.SourceInputs.ToArray()).Reinitialize); Commit(1);
        Prepare(2, new(1, 0, 0, 0)); Assert.False(Assert.Single(source.SourceInputs.ToArray()).Reinitialize); Commit(2);
        Prepare(3, new(0, 1, 0, 0)); Assert.True(Assert.Single(source.SourceInputs.ToArray()).Reinitialize); Commit(3);
        var before = source.CommittedObservations.ToArray(); Prepare(4, Vector4.Zero, true);
        Assert.Throws<ArgumentException>(() => source.Commit(4)); source.Cancel(); machine.Cancel(); Assert.Equal(before, source.CommittedObservations.ToArray());
        Prepare(4, new(1, 0, 0, 0)); Assert.False(Assert.Single(source.SourceInputs.ToArray()).Reinitialize); source.Cancel(); machine.Cancel();
        Prepare(4, Vector4.Zero, true); players.Prepare(4, source.SourceInputs, .01f, true); source.CaptureSourceTimes(4, players);
        Assert.All(source.CandidateObservations.ToArray(), o => Assert.Equal(0, o.CachedWeight)); source.Commit(4); players.Commit(4); machine.Commit(4);
    }

    [Fact]
    public void MissingOrWrongSharedTicksCannotPublishObservations()
    {
        var f = Data.Value; var machine = new AlsRefactoredMovementDetailsRuntime(f.Graph.Resources); var source = new AlsRefactoredMovementDetailsSourceRuntime(f.Graph, 0);
        var players = f.Players(); var counter = new AlsGraphTraversalCounter(0, 0); var before = source.CommittedObservations.ToArray();
        machine.Prepare(0, Run(), before, .01f, .8f, updateCounter: counter);
        source.Prepare(machine, Context(0, counter, .01f), new(1, 0, 0, 0)); var input = source.SourceInputs.ToArray();
        players.Prepare(0, [], .01f); Assert.Throws<ArgumentException>(() => source.CaptureSourceTimes(0, players)); players.Cancel();
        players.Prepare(0, [input[0] with { Weight = .1f }], .01f); Assert.Throws<ArgumentException>(() => source.CaptureSourceTimes(0, players)); players.Cancel();
        players.Prepare(0, [input[0] with { RequestedInertialization = false }], .01f); Assert.Throws<ArgumentException>(() => source.CaptureSourceTimes(0, players)); players.Cancel();
        Assert.Throws<ArgumentException>(() => source.Commit(0)); Assert.Equal(before, source.CommittedObservations.ToArray());
        players.Prepare(0, input, .01f); source.CaptureSourceTimes(0, players); Assert.Throws<ArgumentException>(() => source.Commit(1));
        var otherPlayers = f.Players(); otherPlayers.Prepare(0, input, .01f);
        Assert.Throws<ArgumentException>(() => source.CaptureSourceTimes(0, otherPlayers)); Assert.Throws<ArgumentException>(() => source.Commit(0));
        source.CaptureSourceTimes(0, players); otherPlayers.Cancel();
        source.Cancel(); players.Cancel(); Assert.Equal(before, source.CommittedObservations.ToArray());
        var foreign = new AlsRefactoredMovementDetailsRuntime(f.Graph.Resources); foreign.Prepare(0, Run(), before, .01f, .8f, updateCounter: counter);
        Assert.Throws<ArgumentException>(() => source.Prepare(foreign, Context(0, counter, .01f), Vector4.One));
    }
}
