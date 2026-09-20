using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class OverlaySourceSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var definition = AlsMovementGraphDefinition.Load(set,
            AlsLocomotionProfileCompiler.Compile(Read("assets/config/p4_cycle_locomotion_profile.json"), set));
        var profile = definition.OverlaySources; var bank = definition.OverlayRawSources;
        var names = bank.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray())
            .Append("__not_authored__").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var skeleton = bank.GetSkeleton(profile.SkeletonId);
        Require(skeleton.LogicalBoneCount == 79 && bank.Sources.Length == 36, "Incomplete Overlay source dependency closure.");
        var shared = 0;
        foreach (var source in bank.Sources)
        {
            var existing = definition.RawSources.Sources.ToArray().FirstOrDefault(s => s.PoseData.Identity.AnimationId == source.PoseData.Identity.AnimationId);
            if (existing is null) continue;
            Require(ReferenceEquals(source, existing), "Shared Overlay resource was duplicated."); shared++;
        }
        Require(shared > 0 && shared < bank.Sources.Length && ReferenceEquals(skeleton, definition.RawSources.GetSkeleton(profile.SkeletonId)),
            "Overlay requires partial resource sharing with the complete logical skeleton.");
        using var oracle = JsonDocument.Parse(Read("tests/Als.Core.Tests/Fixtures/P3/v4_overlay_node_pose_native.json"));
        var root = oracle.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("source").GetString() ==
            "UE GetAnimationPose; authored Overlay evaluator pins; caller-owned player seconds", "Wrong Overlay node oracle provenance.");
        Hash("assets/config/v4_overlay_source_inputs.json", root.GetProperty("sourceIndexSha256").GetString()!);
        Hash("assets/config/v4_layering_inputs.json", root.GetProperty("layeringSha256").GetString()!);
        var sampler = new AlsPreciseOverlayAnimationSourceSampler(profile, bank, set, names);
        var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[names.Length];
        var retry = new AlsPrecisePose[79]; var retryCurves = new AlsInertialCurve[names.Length];
        var lookup = profile.Players.ToArray().ToDictionary(p => p.CompiledIndex);
        var seen = new int[148]; var count = 0; var pError = 0.0; var qError = 0.0; var cError = 0f;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var source = lookup[row.GetProperty("compiledIndex").GetInt32()];
            Require(source.NodePath == row.GetProperty("node").GetString() && set.Animations[source.AnimationId].ObjectPath == row.GetProperty("source").GetString(),
                "Native Overlay source occurrence differs.");
            var seconds = row.GetProperty("playerSeconds").GetDouble(); var sweep = row.GetProperty("aimSweepTime").GetDouble();
            sampler.Sample(source.Id, seconds, sweep, pose, curves);
            var native = row.GetProperty("pose");
            Require(native.GetProperty("names").EnumerateArray().Select(n => n.GetString()).SequenceEqual(skeleton.LogicalBoneNames.ToArray(), StringComparer.OrdinalIgnoreCase),
                "Native Overlay bone layout differs.");
            var expected = native.GetProperty("pose"); Require(expected.GetArrayLength() == 79, "Incomplete Overlay pose.");
            for (var bone = 0; bone < 79; bone++)
            {
                var atom = ConvertNative(expected[bone]); var actual = pose[bone];
                var p = Math.Sqrt((atom.Position - actual.Position).LengthSquared);
                var q = Math.Sqrt(Math.Min((atom.Rotation + actual.Rotation * -1).LengthSquared, (atom.Rotation + actual.Rotation).LengthSquared));
                var s = Math.Sqrt((atom.Scale - actual.Scale).LengthSquared);
                pError = Math.Max(pError, p); qError = Math.Max(qError, q);
                Require(p <= .00002 && q <= 2e-10 && s <= 2e-10, $"Overlay node={source.Id} bone={bone} p={p:R} q={q:R} s={s:R}");
            }
            var nativeCurves = native.GetProperty("curves").EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetSingle(), StringComparer.OrdinalIgnoreCase);
            Require(nativeCurves.Count == curves.Count(c => c.Present), "Overlay curve presence count differs.");
            for (var c = 0; c < names.Length; c++)
            {
                var present = nativeCurves.TryGetValue(names[c], out var value);
                Require(present == curves[c].Present, "Overlay curve presence differs: " + names[c]);
                if (!present) continue;
                var error = MathF.Abs(value - curves[c].Value); cError = MathF.Max(cError, error);
                Require(error <= .00002f, "Overlay curve value differs: " + names[c]);
            }
            sampler.Sample(source.Id, seconds, sweep, retry, retryCurves);
            Require(pose.SequenceEqual(retry) && curves.SequenceEqual(retryCurves), "Overlay source retry changed pose/curves.");
            seen[source.Id]++; count++;
        }
        Require(count == 444 && seen.All(n => n == 3), "Incomplete Overlay occurrence coverage.");
        for (var i = 0; i < 1000; i++) sampler.Sample(i % 148, i % 120 / 60.0, i % 101 / 100.0, pose, curves);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) sampler.Sample(i % 148, i % 120 / 60.0, i % 101 / 100.0, pose, curves);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, "Overlay hot source sampling allocated memory.");
        // Interleave every occurrence across four independent scratch owners.
        var samplers = Enumerable.Range(0, 4).Select(_ => new AlsPreciseOverlayAnimationSourceSampler(profile, bank, set, names)).ToArray();
        var outputs = Enumerable.Range(0, 4).Select(_ => new AlsPrecisePose[148 * 79]).ToArray();
        var curveOutputs = Enumerable.Range(0, 4).Select(_ => new AlsInertialCurve[148 * names.Length]).ToArray();
        Parallel.For(0, 4, owner =>
        {
            for (var i = 0; i < 148; i++)
                samplers[owner].Sample(i, .617, .731, outputs[owner].AsSpan(i * 79, 79), curveOutputs[owner].AsSpan(i * names.Length, names.Length));
        });
        for (var i = 1; i < 4; i++) Require(outputs[0].SequenceEqual(outputs[i]) && curveOutputs[0].SequenceEqual(curveOutputs[i]), "Parallel Overlay owners differ.");
        GD.Print($"OVERLAY_SOURCE_NATIVE_OK precision=double nodes=148 roots=29 assets=36 shared_assets={shared} poses={count} bones={count * 79} position_error={pError:R} quaternion_error={qError:R} curve_error={cError:R} retry=all_samples owners=4 parallel_nodes=592 hot_samples=10000 allocated_bytes={allocated} source_clocks=caller final_demo=pending");
    }
    private static void Hash(string path, string expected) => Require(Convert.ToHexString(SHA256.HashData(
        Godot.FileAccess.GetFileAsBytes("res://" + path))).Equals(expected, StringComparison.OrdinalIgnoreCase), "Overlay oracle binding differs: " + path);
    private static AlsPrecisePose ConvertNative(JsonElement value)
    {
        var p = value.GetProperty("position"); var q = value.GetProperty("rotation"); var s = value.GetProperty("scale");
        return new(new(p[0].GetDouble() * .01, -p[1].GetDouble() * .01, p[2].GetDouble() * .01),
            new(-q[0].GetDouble(), q[1].GetDouble(), -q[2].GetDouble(), q[3].GetDouble()), new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
    }
    private static string Read(string path) => Godot.FileAccess.GetFileAsString("res://" + path);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
