using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDirectionMovingNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 30)] [InlineData(false, 60)] [InlineData(false, 120)]
    [InlineData(true, 30)] [InlineData(true, 60)] [InlineData(true, 120)]
    public void OriginalMovingCacheClocksPosesAndCurvesMatchContinuousRuntime(bool crouching, int hz)
    {
        var kind = crouching ? "Crouching" : "Standing";
        var index = MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", path));
        var catalog = new AlsRefactoredAnimationCatalog(index, Read);
        var triangles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), index, Read);
        var resources = new AlsRefactoredDirectionResources(MantlingHostFixture.Read("refactored_stance_machines"), catalog, crouching);
        var sourceProfile = new AlsRefactoredDirectionSourceProfile(catalog, new(catalog, resources));
        var poseProfile = new AlsRefactoredDirectionPose(catalog, sourceProfile, triangles);
        var sampler = poseProfile.CreateSampler(); var machine = new AlsRefactoredDirectionRuntime(resources);
        var source = new AlsRefactoredDirectionSourceRuntime(sourceProfile, 0);
        var bank = new AlsRefactoredSyncBank(MantlingHostFixture.Read("refactored_sync_inputs"), catalog);
        var players = new AlsRefactoredSourcePlayerRuntime(catalog, bank, triangles, sourceProfile.Players.Bind(0,
            new Dictionary<string, int> { ["Movement"] = 0, ["Run Start"] = 1, ["First Pivot"] = 2, ["Second Pivot"] = 3 }));
        using var doc = JsonDocument.Parse(MantlingHostFixture.Read("refactored_direction_sources_" + kind)); var root = doc.RootElement;
        Assert.Equal(AlsRefactoredRotatePlayers.Blueprint(crouching), root.GetProperty("source").GetString());
        foreach (var name in new[] { "refactored_animation_sources", "refactored_stance_machines", "refactored_sync_inputs", "refactored_triangulation_inputs" })
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(name)))), root.GetProperty("resourceHashes").GetProperty(name).GetString()!.ToUpperInvariant());
        Assert.Equal(poseProfile.BoneNames.ToArray(), root.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray());
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("name").GetString() == hz + "hz");
        var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[poseProfile.CurveNames.Length];
        var counter = new AlsGraphTraversalCounter(0, 0); var initialization = counter;
        var maxPosition = 0d; var maxRotation = 0d; var maxScale = 0d; var maxCurve = 0f; var maxTime = 0f; var maxWeight = 0f;
        var frame = 0; var movingFrames = 0; var stacks = 0; var ticks = 0; var caches = 0;
        var seenPlayers = new HashSet<int>(); var seenStates = new HashSet<int>();
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input = row.GetProperty("input"); var grounded = input.GetProperty("groundedState"); var direction = grounded.GetProperty("MovementDirection");
            var velocity = grounded.GetProperty("VelocityBlend"); var yaw = grounded.GetProperty("RotationYawOffsets"); var standing = input.GetProperty("standingState");
            float F(JsonElement n, string key) => n.GetProperty(key).GetSingle();
            var delta = F(input, "delta"); var reset = input.GetProperty("reset").GetBoolean();
            if (reset) initialization = initialization.Next((ulong)frame);
            machine.Prepare(frame, new(direction.GetProperty("bForward").GetBoolean(), direction.GetProperty("bBackward").GetBoolean(), direction.GetProperty("bLeft").GetBoolean(), direction.GetProperty("bRight").GetBoolean(),
                F(grounded, "HipsDirectionLockAmount"), F(input.GetProperty("feetState"), "FeetCrossingAmount")), delta, reinitialize: reset, updateCounter: counter);
            var context = new AlsPoseUpdateContext(new(frame, 5, 1), 1, delta).WithUpdateCounter(counter);
            source.Prepare(machine, context, initialization,
                new(F(velocity, "ForwardAmount"), F(velocity, "BackwardAmount"), F(velocity, "LeftAmount"), F(velocity, "RightAmount")),
                new(F(standing, "PlayRate"), F(input.GetProperty("crouchingState"), "PlayRate"), F(standing, "StrideBlendAmount"), F(standing, "WalkRunBlendAmount")),
                new(input.GetProperty("gait").GetString()!, F(standing, "SprintBlockAmount"), F(standing, "SprintAccelerationAmount")), reset,
                new(F(yaw, "ForwardAngle"), F(yaw, "BackwardAngle"), F(yaw, "LeftAngle"), F(yaw, "RightAngle")));
            players.Prepare(frame, source.SourceInputs, delta, reset); sampler.Sample(frame, machine, source, players, pose, curves);
            Assert.Equal(row.GetProperty("current").GetInt32(), machine.Candidate.State.CurrentState);
            seenStates.Add(machine.Candidate.State.CurrentState); movingFrames += source.SourceInputs.Length > 0 ? 1 : 0;
            stacks += machine.Candidate.State.Transitions.Count > 1 ? 1 : 0; ticks += source.SourceInputs.Length; caches += sampler.CacheEvaluations;
            var nativePlayers = row.GetProperty("sourcePlayers").EnumerateArray().ToDictionary(p => p.GetProperty("propertyIndex").GetInt32());
            var nativeCaches = row.GetProperty("sourceCaches").EnumerateArray().ToDictionary(p => p.GetProperty("propertyIndex").GetInt32());
            foreach (var tick in source.SourceInputs)
            {
                var property = sourceProfile.Players.Players[tick.PlayerId].PropertyIndex; seenPlayers.Add(property);
                var actual = players.Players.ToArray().Single(p => p.PlayerId == tick.PlayerId);
                var time = MathF.Abs(actual.Time - F(nativePlayers[property], "time")); var weight = MathF.Abs(tick.Weight - F(nativePlayers[property], "weight"));
                maxTime = MathF.Max(maxTime, time); maxWeight = MathF.Max(maxWeight, weight);
                Assert.True(time <= 2e-6f && weight <= 2e-6f, $"{kind} {hz} frame={frame} property={property} time={actual.Time}/{F(nativePlayers[property], "time")} diff={time} weight={weight}");
            }
            foreach (var cache in source.CacheUpdates)
            {
                var difference = MathF.Abs(cache.Context.Weight - F(nativeCaches[cache.PropertyIndex], "weight")); maxWeight = MathF.Max(maxWeight, difference);
                Assert.True(difference <= 2e-6f, $"{kind} {hz} frame={frame} cache={cache.PropertyIndex} weight={difference}");
            }
            for (var b = 0; b < 79; b++)
            {
                var native = row.GetProperty("pose")[b]; var p = native.GetProperty("position"); var s = native.GetProperty("scale"); var r = native.GetProperty("rotation");
                var q = new AlsQuaternion(r[0].GetDouble(), r[1].GetDouble(), r[2].GetDouble(), r[3].GetDouble()); var actual = pose[b].Rotation;
                if (AlsQuaternion.Dot(actual, q) < 0) actual = -actual;
                var dq = Math.Max(Math.Max(Math.Abs(actual.X - q.X), Math.Abs(actual.Y - q.Y)), Math.Max(Math.Abs(actual.Z - q.Z), Math.Abs(actual.W - q.W)));
                maxRotation = Math.Max(maxRotation, dq);
                Assert.True(dq <= 2e-6, $"{kind} {hz} frame={frame} bone={poseProfile.BoneNames[b]} Q={dq}");
                for (var axis = 0; axis < 3; axis++)
                {
                    var dp = Math.Abs(pose[b].Position[axis] - p[axis].GetDouble()); var ds = Math.Abs(pose[b].Scale[axis] - s[axis].GetDouble());
                    maxPosition = Math.Max(maxPosition, dp); maxScale = Math.Max(maxScale, ds);
                    Assert.True(dp <= 1e-4 && ds <= 2e-6, $"{kind} {hz} frame={frame} bone={poseProfile.BoneNames[b]} axis={axis} P={dp} S={ds}");
                }
            }
            var nativeCurves = row.GetProperty("curves");
            for (var c = 0; c < curves.Length; c++)
            {
                var present = nativeCurves.TryGetProperty(poseProfile.CurveNames[c], out var value);
                Assert.True(present == curves[c].Present, $"{kind} {hz} frame={frame} curve={poseProfile.CurveNames[c]} presence");
                if (!present) continue;
                var difference = MathF.Abs(value.GetSingle() - curves[c].Value); maxCurve = MathF.Max(maxCurve, difference);
                Assert.True(difference <= 2e-6f, $"{kind} {hz} frame={frame} curve={poseProfile.CurveNames[c]} diff={difference}");
            }
            Assert.Equal(curves.Count(c => c.Present), nativeCurves.EnumerateObject().Count());
            if(reset||frame%17==0)
            {
                var priorPlayers=players.Players.ToArray();var priorSamples=players.Samples.ToArray();var priorGroups=players.Groups.ToArray();
                var priorPose=pose.ToArray();var priorCurves=curves.ToArray();
                players.Cancel();players.Prepare(frame,source.SourceInputs,delta,reset);
                sampler.Sample(frame,machine,source,players,pose,curves);
                Assert.Equal(priorPlayers,players.Players.ToArray());Assert.Equal(priorSamples,players.Samples.ToArray());Assert.Equal(priorGroups,players.Groups.ToArray());
                Assert.Equal(priorPose,pose);Assert.Equal(priorCurves,curves);
            }
            players.Commit(frame); source.Commit(frame); machine.Commit(frame++); counter = counter.Next((ulong)frame);
        }
        Assert.Equal(hz * 4, frame); Assert.True(movingFrames > hz * 3); Assert.True(stacks > 0); Assert.Equal(6, seenStates.Count);
        Assert.Equal(crouching ? 6 : 8, seenPlayers.Count); Assert.True(caches > movingFrames);
        output.WriteLine($"{kind} {hz}: frames={frame} bones={frame * 79} moving={movingFrames} stacks={stacks} ticks={ticks} caches={caches} maxP={maxPosition:R} Q={maxRotation:R} S={maxScale:R} C={maxCurve:R} time={maxTime:R} weight={maxWeight:R}");
    }
}
