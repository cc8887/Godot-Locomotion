using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDirectionPoseTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog;
        public readonly AlsRefactoredDirectionSourceProfile Source;
        public readonly AlsRefactoredDirectionPose Pose;
        public readonly AlsRefactoredSyncBank Bank;
        public readonly IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> Triangles;
        public Fixture(bool crouching)
        {
            var json = MantlingHostFixture.Read("refactored_animation_sources");
            byte[] Read(string p) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p));
            Catalog = new(json, Read); Triangles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), json, Read);
            Source = new(Catalog, new(Catalog, new(MantlingHostFixture.Read("refactored_stance_machines"), Catalog, crouching)));
            Pose = new(Catalog, Source, Triangles); Bank = new(MantlingHostFixture.Read("refactored_sync_inputs"), Catalog);
        }
        public AlsRefactoredSourcePlayerRuntime Players() => new(Catalog, Bank, Triangles, Source.Players.Bind(0,
            new Dictionary<string, int> { ["Movement"] = 0, ["Run Start"] = 1, ["First Pivot"] = 2, ["Second Pivot"] = 3 }));
    }
    private static readonly Lazy<Fixture> Standing = new(() => new(false)), Crouching = new(() => new(true));

    [Fact]
    public void ForwardMixedOutputUsesThreeRealSourcesAndOneSharedBaseEvaluation()
    {
        var f = Standing.Value; var players = f.Players(); var forward = f.Source.Forward!;
        var machine = new AlsRefactoredDirectionRuntime(f.Source.Graph.Resources); var update = new AlsRefactoredDirectionSourceRuntime(f.Source, 0);
        var sampler = f.Pose.CreateSampler(); var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[f.Pose.CurveNames.Length];
        var initialization = new AlsGraphTraversalCounter(0, 0);
        for (var frame = 0; frame < 2; frame++)
        {
            var counter = new AlsGraphTraversalCounter((short)frame, (ulong)frame); var delta = frame == 0 ? 0 : .1f;
            machine.Prepare(frame, new(true, false, false, false, 0, 0), delta, updateCounter: counter);
            update.Prepare(machine, new AlsPoseUpdateContext(new(frame, 1, 1), 1, delta).WithUpdateCounter(counter), initialization,
                new(1, 0, 0, 0), new(1, 1, .7f, .8f), new(frame == 0 ? "Als.Gait.Running" : "Als.Gait.Sprinting", .3f, .125f));
            players.Prepare(frame, update.SourceInputs, delta); sampler.Sample(frame, machine, update, players, pose, curves);
            if (frame == 1)
            {
                Assert.Equal(.5f, update.ForwardWeights.Gait.Y, 6); Assert.Equal(.5f, update.ForwardWeights.Acceleration);
                Assert.Equal(2, sampler.CacheEvaluations); Assert.Equal(3, sampler.PlayerEvaluations);
                var basis = players.Pose(forward.BasePlayer); var sprint = players.Pose(forward.SprintPlayer); var acceleration = players.Pose(forward.AccelerationPlayer);
                for (var b = 0; b < 79; b++)
                {
                    // Independent positional composition: .3 Base + .7 *
                    // (.5 Base + .5 * (.5 Sprint + .5 Acceleration)).
                    var expected = basis[b].Position * .65 + sprint[b].Position * .175 + acceleration[b].Position * .175;
                    for (var axis = 0; axis < 3; axis++) Assert.InRange(Math.Abs(pose[b].Position[axis] - expected[axis]), 0, 1e-4);
                }
            }
            players.Commit(frame); update.Commit(frame); machine.Commit(frame);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleChannelUsesActualSourcePoseAndModifiesOnlyYaw(bool crouching)
    {
        var f = (crouching ? Crouching : Standing).Value; var players = f.Players();
        var machine = new AlsRefactoredDirectionRuntime(f.Source.Graph.Resources); var update = new AlsRefactoredDirectionSourceRuntime(f.Source, 0);
        var sampler = f.Pose.CreateSampler(); var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[f.Pose.CurveNames.Length];
        for (var frame = 0; frame < 3; frame++)
        {
            var counter = new AlsGraphTraversalCounter((short)frame, (ulong)frame);
            machine.Prepare(frame, new(true, false, false, false, 0, 0), .01f, reinitialize: true, updateCounter: counter);
            update.Prepare(machine, new GodotAls.Core.Locomotion.AlsPoseUpdateContext(new(frame, 1, 1), 1, .01f).WithUpdateCounter(counter), counter,
                new(1, 0, 0, 0), new(1, 1, .7f, .8f), new("Als.Gait.Sprinting", frame == 0 ? 1 : 0, frame == 2 ? 1 : 0), true, new(13, -17, 23, -29));
            players.Prepare(frame, update.SourceInputs, .01f); sampler.Sample(frame, machine, update, players, pose, curves);
            var player = Assert.Single(update.SourceInputs.ToArray()).PlayerId;
            players.Evaluate(frame, player);
            var expected = players.Pose(player);
            for (var b = 0; b < pose.Length; b++)
            {
                Assert.InRange((pose[b].Position - expected[b].Position).LengthSquared, 0, 1e-20);
                Assert.InRange(Math.Abs(Math.Abs(AlsQuaternion.Dot(pose[b].Rotation, expected[b].Rotation)) - 1), 0, 1e-12);
            }
            for (var c = 0; c < curves.Length; c++)
            {
                if (f.Pose.CurveNames[c] == "RotationYawOffset") { Assert.True(curves[c].Present); Assert.Equal(13, curves[c].Value); }
                else
                {
                    var sourceIndex = players.CurveNames(player).IndexOf(f.Pose.CurveNames[c]);
                    Assert.Equal(sourceIndex < 0 ? default : players.Curves(player)[sourceIndex], curves[c]);
                }
            }
            Assert.Equal(1, sampler.PlayerEvaluations);
            players.Commit(frame); update.Commit(frame); machine.Commit(frame);
        }
    }

    [Theory]
    [InlineData(false, 30)] [InlineData(false, 60)] [InlineData(false, 120)]
    [InlineData(true, 30)] [InlineData(true, 60)] [InlineData(true, 120)]
    public void ContinuousPoseAndCurveOutputRetriesWithoutResamplingSharedCaches(bool crouching, int hz)
    {
        var f = (crouching ? Crouching : Standing).Value; var players = f.Players();
        var machine = new AlsRefactoredDirectionRuntime(f.Source.Graph.Resources); var update = new AlsRefactoredDirectionSourceRuntime(f.Source, 0);
        var sampler = f.Pose.CreateSampler(); var worker = f.Pose.CreateSampler();
        var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[f.Pose.CurveNames.Length];
        var retryPose = new AlsPrecisePose[79]; var retryCurves = new AlsInertialCurve[curves.Length];
        var counter = new AlsGraphTraversalCounter(0, 0); var initialization = counter;
        var stacked = 0; var cached = 0;
        for (var frame = 0; frame < hz * 2; frame++)
        {
            var delta = frame % 19 == 0 ? 0 : 1f / hz; var reset = frame == hz;
            if (reset) initialization = initialization.Next((ulong)frame);
            var direction = frame / (hz / 4) % 4;
            machine.Prepare(frame, new(direction == 0, direction == 1, direction == 2, direction == 3, frame % 13 < 5 ? .5f : -.5f, 0), delta, reinitialize: reset, updateCounter: counter);
            var context = new GodotAls.Core.Locomotion.AlsPoseUpdateContext(new(frame, 11, 1), 1, delta).WithUpdateCounter(counter);
            var velocity = frame % 17 == 0 ? Vector4.Zero : new(.2f, .3f, .25f, .25f);
            var movement = new AlsRefactoredMovementPlayerInput(1.2f, .9f, .7f, .8f);
            var forward = new AlsRefactoredForwardInput(frame % hz < hz / 2 ? "Als.Gait.Sprinting" : "Als.Gait.Running", frame % 11 == 0 ? 1 : .3f, frame % 7 / 6f);
            var yaw = new Vector4(10 + frame, -20, 30, -40);
            update.Prepare(machine, context, initialization, velocity, movement, forward, reset, yaw);
            players.Prepare(frame, update.SourceInputs, delta); sampler.Sample(frame, machine, update, players, pose, curves);
            Assert.InRange(sampler.CacheEvaluations, 0, update.CacheUpdates.Length);
            Assert.InRange(sampler.PlayerEvaluations, 0, update.SourceInputs.Length);
            cached += update.CacheReadCount > sampler.CacheEvaluations ? 1 : 0;
            stacked += machine.Candidate.State.Transitions.Count > 1 ? 1 : 0;
            foreach (var p in pose) p.Validate(1e-10);
            foreach (var c in curves) Assert.True(float.IsFinite(c.Value));
            var expectedYaw = 0f;
            for (var s = 0; s < 6; s++) expectedYaw += AlsTransitionStack.Weight(machine.Candidate.State.Transitions, s) * yaw[s < 2 ? s : s < 4 ? 3 : 2];
            Assert.InRange(MathF.Abs(expectedYaw - curves[f.Pose.CurveNames.IndexOf("RotationYawOffset")].Value), 0, 1e-4f);
            worker.Sample(frame, machine, update, players, retryPose, retryCurves); Assert.Equal(pose, retryPose); Assert.Equal(curves, retryCurves);
            update.Cancel(); players.Cancel();
            update.Prepare(machine, context, initialization, velocity, movement, forward, reset, yaw);
            players.Prepare(frame, update.SourceInputs, delta); sampler.Sample(frame, machine, update, players, retryPose, retryCurves);
            Assert.Equal(pose, retryPose); Assert.Equal(curves, retryCurves);
            // A stale or foreign owner cannot publish partially computed data.
            if (frame == 0)
            {
                var foreign = new AlsRefactoredDirectionRuntime(f.Source.Graph.Resources);
                Assert.Throws<ArgumentException>(() => sampler.Sample(frame, foreign, update, players, retryPose, retryCurves));
                Assert.Equal(pose, retryPose); Assert.Equal(curves, retryCurves);
            }
            players.Commit(frame); update.Commit(frame); machine.Commit(frame); counter = counter.Next((ulong)frame + 1);
        }
        Assert.True(stacked > 0); Assert.True(cached > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingNativeZeroVelocityTracesMatchReferencePoseAndCurvePresence(bool crouching)
    {
        var f = (crouching ? Crouching : Standing).Value;
        using var doc = JsonDocument.Parse(MantlingHostFixture.Read("refactored_direction_trace_" + (crouching ? "Crouching" : "Standing")));
        foreach (var name in new[] { "refactored_animation_sources", "refactored_stance_machines" })
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(name)))), doc.RootElement.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        Assert.Equal(f.Pose.BoneNames.ToArray(), doc.RootElement.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray());
        foreach (var trace in doc.RootElement.GetProperty("traces").EnumerateArray())
        {
            var players = f.Players(); var machine = new AlsRefactoredDirectionRuntime(f.Source.Graph.Resources);
            var update = new AlsRefactoredDirectionSourceRuntime(f.Source, 0); var sampler = f.Pose.CreateSampler();
            var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[f.Pose.CurveNames.Length];
            long frame = 0; var counter = new AlsGraphTraversalCounter(0, 0); var initialization = counter;
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var input = row.GetProperty("input"); var grounded = input.GetProperty("groundedState"); var direction = grounded.GetProperty("MovementDirection");
                Assert.False(grounded.TryGetProperty("VelocityBlend", out _));
                var delta = input.GetProperty("delta").GetSingle(); var reset = input.GetProperty("reset").GetBoolean();
                if (reset) initialization = initialization.Next((ulong)frame);
                machine.Prepare(frame, new(direction.GetProperty("bForward").GetBoolean(), direction.GetProperty("bBackward").GetBoolean(), direction.GetProperty("bLeft").GetBoolean(), direction.GetProperty("bRight").GetBoolean(), grounded.GetProperty("HipsDirectionLockAmount").GetSingle(), input.GetProperty("feetState").GetProperty("FeetCrossingAmount").GetSingle()), delta, reinitialize: reset, updateCounter: counter);
                update.Prepare(machine, new GodotAls.Core.Locomotion.AlsPoseUpdateContext(new(frame, 1, 1), 1, delta).WithUpdateCounter(counter), initialization, Vector4.Zero, new(1, 1, 1, 1), new("", 0, 0), reset);
                players.Prepare(frame, update.SourceInputs, delta); sampler.Sample(frame, machine, update, players, pose, curves);
                Assert.Equal(0, sampler.CacheEvaluations); Assert.Equal(0, sampler.PlayerEvaluations);
                for (var b = 0; b < 79; b++)
                {
                    var native = row.GetProperty("pose")[b]; var position = native.GetProperty("position"); var rotation = native.GetProperty("rotation"); var scale = native.GetProperty("scale");
                    for (var axis = 0; axis < 3; axis++)
                    {
                        Assert.InRange(Math.Abs(pose[b].Position[axis] - position[axis].GetDouble()), 0, 1e-4);
                        Assert.InRange(Math.Abs(pose[b].Scale[axis] - scale[axis].GetDouble()), 0, 2e-6);
                    }
                    var q = new AlsQuaternion(rotation[0].GetDouble(), rotation[1].GetDouble(), rotation[2].GetDouble(), rotation[3].GetDouble());
                    var actual = pose[b].Rotation;
                    if (AlsQuaternion.Dot(actual, q) < 0) actual = -actual;
                    Assert.InRange(Math.Abs(actual.X - q.X), 0, 2e-6);
                    Assert.InRange(Math.Abs(actual.Y - q.Y), 0, 2e-6);
                    Assert.InRange(Math.Abs(actual.Z - q.Z), 0, 2e-6);
                    Assert.InRange(Math.Abs(actual.W - q.W), 0, 2e-6);
                }
                var nativeCurves = row.GetProperty("curves");
                for (var c = 0; c < curves.Length; c++)
                {
                    var present = nativeCurves.TryGetProperty(f.Pose.CurveNames[c], out var value);
                    Assert.Equal(present, curves[c].Present); if (present) Assert.InRange(MathF.Abs(value.GetSingle() - curves[c].Value), 0, 2e-6f);
                }
                Assert.Equal(curves.Count(c => c.Present), nativeCurves.EnumerateObject().Count());
                players.Commit(frame); update.Commit(frame); machine.Commit(frame++); counter = counter.Next((ulong)frame);
            }
        }
    }
}
