using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredMovementCacheTests
{
    private static readonly Lazy<(AlsRefactoredAnimationCatalog Catalog, AlsRefactoredMovementCacheProfile Profile)> Fixture = new(() =>
    {
        var json = MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string p) => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p));
        var catalog = new AlsRefactoredAnimationCatalog(json, Read);
        var triangles = AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"), json, Read);
        return (catalog, new(catalog, triangles));
    });

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void LeanEnvelopeRetainsHistoryAndRetriesWithoutChangingPose(int hz)
    {
        var profile = Fixture.Value.Profile;
        var runtime = new AlsRefactoredMovementCacheRuntime(profile, profile.Lean.BoneNames, ["PoseMoving", "Untouched"]);
        var clean = new AlsRefactoredMovementCacheRuntime(profile, profile.Lean.BoneNames, ["PoseMoving", "Untouched"]);
        var pose = Enumerable.Repeat(AlsPrecisePose.Identity, profile.Lean.BoneNames.Length).ToArray();
        AlsInertialCurve[] curves = [new(.2f), new(.37f)];
        var expected = .5f;
        for (var frame = 0; frame < hz * 3; frame++)
        {
            var running = frame < hz || frame >= hz * 2 ? 0 : 1;
            var delta = frame % 23 == 0 ? 0 : 1f / hz;
            var reset = frame == hz * 2 + 5;
            var target = Math.Clamp((float)running, .5f, 1);
            if (frame == 0 || reset) expected = target;
            else if ((target - expected) * (target - expected) < 1e-8f) expected = target;
            else expected += (target - expected) * Math.Clamp(delta * (target >= expected ? 20 : 1), 0, 1);
            var lean = new Vector2(MathF.Sin(frame * .13f), MathF.Cos(frame * .07f));
            runtime.Prepare(frame, running, lean, delta, reset);
            runtime.ValidateCommit(frame);
            runtime.Evaluate(frame, pose, curves);
            var before = runtime.Pose.ToArray(); var beforeCurves = runtime.Curves.ToArray(); var filtered = runtime.FilteredLean;
            runtime.Cancel();
            runtime.Prepare(frame, running, lean, delta, reset); runtime.Evaluate(frame, pose, curves);
            clean.Prepare(frame, running, lean, delta, reset); clean.Evaluate(frame, pose, curves);
            Assert.Equal(expected, runtime.Alpha); Assert.Equal(filtered, runtime.FilteredLean);
            Assert.Equal(before, runtime.Pose.ToArray()); Assert.Equal(beforeCurves, runtime.Curves.ToArray());
            Assert.True(runtime.Pose.SequenceEqual(clean.Pose)); Assert.True(runtime.Curves.SequenceEqual(clean.Curves));
            Assert.Equal(new AlsInertialCurve(1), runtime.Curves[0]); Assert.Equal(curves[1], runtime.Curves[1]);
            Assert.Contains(runtime.Pose.ToArray(), p => p != AlsPrecisePose.Identity);
            // A failed reevaluation cannot publish the preceding successful pose.
            Assert.Throws<ArgumentException>(() => runtime.Evaluate(frame, [], curves));
            Assert.Throws<ArgumentException>(() => runtime.ValidateCommit(frame));
            runtime.Evaluate(frame, pose, curves);
            runtime.Commit(frame); clean.Commit(frame);
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void UpdateOnlyFramesPreserveLeanAndAlphaUntilEvaluationResumes(int hz)
    {
        var profile = Fixture.Value.Profile;
        var sparse = new AlsRefactoredMovementCacheRuntime(profile, profile.Lean.BoneNames, ["Untouched"]);
        var full = new AlsRefactoredMovementCacheRuntime(profile, profile.Lean.BoneNames, ["Untouched"]);
        var pose = Enumerable.Repeat(AlsPrecisePose.Identity, profile.Lean.BoneNames.Length).ToArray();
        AlsInertialCurve[] curves = [new(.37f)];
        var evaluations = 0;
        for (var frame = 0; frame < hz * 3; frame++)
        {
            var running = frame < hz ? 1f : 0f;
            var lean = new Vector2(MathF.Sin(frame * .13f), MathF.Cos(frame * .07f));
            var delta = frame % 23 == 0 ? 0 : 1f / hz;
            var reset = frame == hz + 1;
            full.Prepare(frame, running, lean, delta, reset);
            full.Evaluate(frame, pose, curves);
            sparse.Prepare(frame, running, lean, delta, reset);
            var alpha = sparse.Alpha; var filtered = sparse.FilteredLean;
            sparse.Cancel();
            sparse.Prepare(frame, running, lean, delta, reset);
            Assert.Equal(alpha, sparse.Alpha); Assert.Equal(filtered, sparse.FilteredLean);
            Assert.Equal(full.Alpha, sparse.Alpha); Assert.Equal(full.FilteredLean, sparse.FilteredLean);
            Assert.Throws<ArgumentException>(() => sparse.Commit(frame + 1));
            if (frame % 9 == 8 || frame == hz * 3 - 1)
            {
                sparse.Evaluate(frame, pose, curves);
                Assert.True(full.Pose.SequenceEqual(sparse.Pose));
                Assert.True(full.Curves.SequenceEqual(sparse.Curves));
                Assert.Throws<ArgumentException>(() => sparse.Evaluate(frame, [], curves));
                Assert.Throws<ArgumentException>(() => sparse.Commit(frame));
                Assert.Throws<InvalidOperationException>(() => sparse.Pose.ToArray());
                sparse.Evaluate(frame, pose, curves);
                evaluations++;
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => sparse.Pose.ToArray());
                Assert.Throws<InvalidOperationException>(() => sparse.Curves.ToArray());
            }
            sparse.Commit(frame); full.Commit(frame);
            Assert.Throws<InvalidOperationException>(() => sparse.Pose.ToArray());
            Assert.Throws<ArgumentException>(() => sparse.Prepare(frame, running, lean, delta));
        }
        Assert.True(evaluations >= 10);
    }

    [Fact]
    public void OriginalEnvelopeRejectsAlteredBindingsAndNodePolicies()
    {
        var original = Fixture.Value.Catalog.Read(AlsRefactoredRotatePlayers.Blueprint(false));
        foreach (var change in new[] { "base", "curve", "alpha", "time", "sync", "teleport", "asset", "binding", "axis", "callback" })
        {
            var json = JsonNode.Parse(original.GetRawText())!;
            JsonNode Node(int id) => json["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == id)!;
            switch (change)
            {
                case "base": Node(120)["runtime"]!["base"]!["linkId"] = 117; break;
                case "curve": Node(122)["runtime"]!["curveValues"]![0] = 0; break;
                case "alpha": Node(120)["runtime"]!["alphaScaleBiasClamp"]!["interpSpeedDecreasing"] = 20; break;
                case "time": Node(202)["runtime"]!["normalizedTime"] = .5; break;
                case "sync": Node(202)["runtime"]!["method"] = "SyncGroup"; break;
                case "teleport": Node(202)["runtime"]!["bTeleportToNormalizedTime"] = false; break;
                case "asset": Node(202)["runtime"]!["blendSpace"] = "foreign"; break;
                case "binding": json["nativeText"] = json["nativeText"]!.GetValue<string>().Replace("\"PoseState\",\"UnweightedGaitRunningAmount\"", "\"PoseState\",\"GaitRunningAmount\"", StringComparison.Ordinal); break;
                case "axis": json["nativeText"] = json["nativeText"]!.GetValue<string>().Replace("\"LeanState\",\"RightAmount\"", "\"LeanState\",\"ForwardAmount\"", StringComparison.Ordinal); break;
                case "callback": Node(120)["runtime"]!["updateFunction"]!["functionName"] = "Unknown"; break;
            }
            using var doc = JsonDocument.Parse(json.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredMovementCacheProfile.Compile(doc.RootElement));
        }
    }
}
