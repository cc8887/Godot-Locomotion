using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementDetailsPoseTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog;
        public readonly AlsRefactoredMovementDetailsPoseGraph Graph;
        public readonly AlsRefactoredMovementCacheProfile Movement;
        public readonly AlsRefactoredSyncBank Bank;
        public readonly IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> Triangles;
        public readonly AlsPrecisePose[] Base;
        public Fixture()
        {
            var index = MantlingHostFixture.Read("refactored_animation_sources");
            byte[] Read(string p) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p));
            Catalog = new(index, Read); Graph = new(Catalog, new(MantlingHostFixture.Read("refactored_stance_machines"), Catalog));
            Bank = new(MantlingHostFixture.Read("refactored_sync_inputs"), Catalog);
            Triangles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), index, Read);
            Movement = new(Catalog, Triangles);
            Base = Catalog.CompileAbsolutePoseWithCurves("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose").Pose.ReferencePose.ToArray();
        }
        public AlsRefactoredSourcePlayerRuntime Players(int first)
        {
            var defs = Graph.Players.Bind(first, new Dictionary<string, int> { ["Movement"] = 0, ["Run Start"] = 1, ["First Pivot"] = 2, ["Second Pivot"] = 3 });
            return new(Catalog, Bank, Triangles, Enumerable.Range(0, first).Select(i => new AlsRefactoredSourcePlayerDefinition(i, defs[0].Source, -1)).Concat(defs).ToArray());
        }
    }
    private static readonly Lazy<Fixture> Data = new(() => new());

    [Fact]
    public void SingleDirectionUsesActualClipTimeAndForeignFramesCannotWriteOutput()
    {
        var f = Data.Value; const int frame = 0;
        var machine = new AlsRefactoredMovementDetailsRuntime(f.Graph.Resources);
        var update = new AlsRefactoredMovementDetailsSourceRuntime(f.Graph, 0); var players = f.Players(0);
        var movement = new AlsRefactoredMovementCacheRuntime(f.Movement, f.Movement.Lean.BoneNames, ["Untouched"]);
        var profile = new AlsRefactoredMovementDetailsPose(f.Catalog, f.Graph, f.Movement, movement.CurveNames);
        var sampler = profile.CreateSampler();
        var pose = Enumerable.Repeat(AlsPrecisePose.Identity, profile.BoneNames.Length).ToArray();
        var curves = Enumerable.Repeat(new AlsInertialCurve(-123), profile.CurveNames.Length).ToArray();
        var counter = new AlsGraphTraversalCounter(0, 0); var input = new AlsRefactoredMovementDetailsInput("Als.Gait.Running", 1, 1, 1, false);
        machine.Prepare(frame, input, update.CommittedObservations, .037f, updateCounter: counter);
        update.Prepare(machine, new AlsPoseUpdateContext(new(frame, 2, 1), 1, .037f).WithUpdateCounter(counter), Vector4.UnitX);
        Assert.Equal(1, machine.Candidate.State.CurrentState); Assert.Equal(0, machine.Candidate.State.Transitions.Count);
        players.Prepare(frame, update.SourceInputs, .037f);
        movement.Prepare(frame, 1, new(.3f, -.6f), .037f); movement.Evaluate(frame, f.Base, [new(.37f)]);
        Assert.Throws<ArgumentException>(() => sampler.Sample(frame, machine, update, players, movement, pose, curves));
        Assert.All(curves, c => Assert.Equal(-123, c.Value)); // Source-time capture is mandatory.
        update.CaptureSourceTimes(frame, players);
        var foreign = new AlsRefactoredMovementDetailsRuntime(f.Graph.Resources);
        foreign.Prepare(frame, input, update.CommittedObservations, .037f, updateCounter: counter);
        Assert.Throws<ArgumentException>(() => sampler.Sample(frame, foreign, update, players, movement, pose, curves));
        Assert.Throws<ArgumentException>(() => sampler.Sample(frame + 1, machine, update, players, movement, pose, curves));
        Assert.All(curves, c => Assert.Equal(-123, c.Value));
        sampler.Sample(frame, machine, update, players, movement, pose, curves);
        Assert.Equal(1, sampler.PlayerEvaluations); Assert.Equal(1, sampler.StateEvaluations);
        var tick = Assert.Single(update.SourceInputs.ToArray());
        var time = Assert.Single(players.Players.ToArray(), p => p.PlayerId == tick.PlayerId).Time;
        var clip = f.Catalog.CompileAdditivePose(players.Source(tick.PlayerId));
        var additive = new AlsPrecisePose[pose.Length]; var additiveCurves = new AlsInertialCurve[clip.CurveNames.Length];
        clip.CreateSampler().Sample(time, additive, additiveCurves);
        // Explicit transform composition, independent of the graph blend helper.
        for (var b = 0; b < pose.Length; b++)
        {
            Assert.Equal(movement.Pose[b].Position + additive[b].Position, pose[b].Position);
            Assert.Equal(movement.Pose[b].Scale * (AlsDoubleVector.One + additive[b].Scale), pose[b].Scale);
            var expected = (additive[b].Rotation * movement.Pose[b].Rotation).Normalized();
            Assert.True(Math.Abs(AlsQuaternion.Dot(expected, pose[b].Rotation) - 1) < 1e-12);
        }
        for (var c = 0; c < curves.Length; c++)
        {
            var name = profile.CurveNames[c];
            var m = Array.FindIndex(movement.CurveNames.ToArray(), n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            var a = Array.FindIndex(clip.CurveNames.ToArray(), n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            var basis = m < 0 ? default : movement.Curves[m]; var delta = a < 0 ? default : additiveCurves[a];
            Assert.Equal(basis.Present || delta.Present, curves[c].Present);
            if (curves[c].Present) Assert.Equal((basis.Present ? basis.Value : 0) + (delta.Present ? delta.Value : 0), curves[c].Value);
        }
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void SixStatesComposeRealAdditivesOverMovementWithRollback(int hz)
    {
        var f = Data.Value; const int first = 3;
        var machine = new AlsRefactoredMovementDetailsRuntime(f.Graph.Resources);
        var update = new AlsRefactoredMovementDetailsSourceRuntime(f.Graph, first); var players = f.Players(first);
        var movement = new AlsRefactoredMovementCacheRuntime(f.Movement, f.Movement.Lean.BoneNames, ["Untouched"]);
        var profile = new AlsRefactoredMovementDetailsPose(f.Catalog, f.Graph, f.Movement, movement.CurveNames);
        var sampler = profile.CreateSampler(); var independent = profile.CreateSampler();
        var pose = new AlsPrecisePose[profile.BoneNames.Length]; var curves = new AlsInertialCurve[profile.CurveNames.Length];
        var otherPose = new AlsPrecisePose[pose.Length]; var otherCurves = new AlsInertialCurve[curves.Length];
        var counter = new AlsGraphTraversalCounter(0, 0); var stage = 0; var states = new HashSet<int>(); var stacks = 0; var changed = 0; var empty = 0;
        int[] target = [5,2,0,1,2,3,4,3,2,0,2];
        for (var frame = 0; frame < hz * 6; frame++)
        {
            var input = stage is 2 or 9 ? new AlsRefactoredMovementDetailsInput("", 1, 0, 1, false) :
                new AlsRefactoredMovementDetailsInput("Als.Gait.Running", stage == 10 ? .5f : 1, 1, stage == 0 ? .5f : 1, stage is 5 or 6 or 7);
            var delta = frame % 31 == 0 ? 0 : 1f / hz;
            var context = new AlsPoseUpdateContext(new(frame, 11, 1), .8f, delta).WithUpdateCounter(counter).WithInertialization(119, true);
            var velocity = frame % 17 == 0 ? Vector4.Zero : frame % 7 == 0 ? Vector4.UnitX : new Vector4(.1f, .2f, .3f, .4f);
            var lean = new Vector2(MathF.Sin(frame * .13f), MathF.Cos(frame * .07f));
            void Prepare()
            {
                machine.Prepare(frame, input, update.CommittedObservations, delta, context.Weight, updateCounter: counter);
                update.Prepare(machine, context, velocity); players.Prepare(frame, update.SourceInputs, delta); update.CaptureSourceTimes(frame, players);
                movement.Prepare(frame, input.UnweightedGaitRunningAmount, lean, delta);
                movement.Evaluate(frame, f.Base, [new(.37f)]);
            }
            Prepare(); sampler.Sample(frame, machine, update, players, movement, pose, curves);
            var expectedPose = pose.ToArray(); var expectedCurves = curves.ToArray();
            var playerCount = sampler.PlayerEvaluations; var stateCount = sampler.StateEvaluations;
            machine.Cancel(); update.Cancel(); players.Cancel(); movement.Cancel();
            Prepare(); sampler.Sample(frame, machine, update, players, movement, pose, curves);
            independent.Sample(frame, machine, update, players, movement, otherPose, otherCurves);
            Assert.Equal(expectedPose, pose); Assert.Equal(expectedCurves, curves); Assert.Equal(pose, otherPose); Assert.Equal(curves, otherCurves);
            Assert.Equal(playerCount, sampler.PlayerEvaluations); Assert.Equal(stateCount, sampler.StateEvaluations);
            Assert.Equal(new AlsInertialCurve(.37f), curves[0]);
            Assert.Equal(new AlsInertialCurve(1), curves[Array.IndexOf(profile.CurveNames.ToArray(), "PoseMoving")]);
            foreach (var p in pose) p.Validate();
            var current = machine.Candidate.State.CurrentState; states.Add(current); stacks += machine.Candidate.State.Transitions.Count > 0 ? 1 : 0;
            if (velocity == Vector4.Zero)
            {
                Assert.Equal(0, sampler.PlayerEvaluations);
                for (var b = 0; b < pose.Length; b++)
                {
                    Assert.True((pose[b].Position - movement.Pose[b].Position).LengthSquared < 1e-20);
                    Assert.True(Math.Abs(AlsQuaternion.Dot(pose[b].Rotation, movement.Pose[b].Rotation) - 1) < 1e-12);
                    Assert.True((pose[b].Scale - movement.Pose[b].Scale).LengthSquared < 1e-20);
                }
                empty++;
            }
            if (!pose.AsSpan().SequenceEqual(movement.Pose)) changed++;
            if (machine.Candidate.State.Transitions.Count == 0 && current is 0 or 2) Assert.True(pose.AsSpan().SequenceEqual(movement.Pose));
            // Failed output layout leaves caller buffers unchanged.
            Assert.Throws<ArgumentException>(() => sampler.Sample(frame, machine, update, players, movement, [], curves));
            Assert.Equal(expectedCurves, curves);
            machine.ValidateCommit(frame); update.ValidateCommit(frame); players.ValidateCommit(frame); movement.ValidateCommit(frame);
            machine.Commit(frame); update.Commit(frame); players.Commit(frame); movement.Commit(frame);
            if (current == target[stage]) stage = (stage + 1) % target.Length;
            counter = counter.Next((ulong)frame + 1);
        }
        Assert.Equal(6, states.Count); Assert.True(stacks > 0); Assert.True(changed > 0); Assert.True(empty > 0);
    }
}
