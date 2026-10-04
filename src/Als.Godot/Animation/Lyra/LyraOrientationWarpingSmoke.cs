using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraOrientationWarpingSmoke : Node
{
    public override void _Ready()
    {
        try { LyraCycleLayerSourceSmoke.Run(true, true, true); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Orientation warping failed: " + error); GetTree().Quit(1); }
    }
}

internal sealed class LyraOrientationComparison
{
    private int _frames, _present;
    private double _position, _rotation, _scale;
    private readonly bool _stride;
    private readonly bool _runtime;
    private readonly int _expectedRootPresent;
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    public LyraOrientationComparison(JsonElement native, bool stride = false, bool runtime = false,
        string runtimeRequests = "cycle_runtime_requests.json", int expectedRootPresent = 3255)
    {
        _stride = stride;
        _runtime = runtime;
        _expectedRootPresent = expectedRootPresent;
        var prefix = stride ? "stride" : "orientation";
        const string root = "res://assets/generated/lyra_als/";
        Require(native.GetProperty("schemaVersion").GetInt32() == 1, "Invalid Warp fixture schema.");
        if (runtime)
            Require(native.GetProperty("contractSha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + "cycle_layer_graph.json")) &&
                native.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + runtimeRequests)), "Stale original Cycle fixture.");
        else
            Require(native.GetProperty("policySha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + prefix + "_policy.json")) &&
                native.GetProperty("requestsSha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + prefix + "_requests.json")), "Stale Warp fixture.");
        foreach (var dependency in native.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dependency.Name)), "Changed Orientation dependency.");
    }
    public static AlsOrientationWarpingInput Input(JsonElement row)
    {
        var direction = row.GetProperty("direction"); var rotation = row.GetProperty("relativeRotation");
        var relative = new AlsQuaternion(rotation[0].GetDouble(), rotation[1].GetDouble(), rotation[2].GetDouble(), rotation[3].GetDouble());
        return new(row.GetProperty("delta").GetSingle(), row.GetProperty("angle").GetSingle(),
            new(direction[0].GetDouble(), direction[1].GetDouble(), direction[2].GetDouble()),
            new(default, relative, AlsDoubleVector.One), relative, row.GetProperty("alpha").GetSingle(),
            row.GetProperty("weight").GetSingle(), row.GetProperty("counter").GetInt64(), row.GetProperty("reinitialize").GetBoolean());
    }
    public void Compare(JsonElement expected, LyraCycleLayerPoseHost host, string label)
    {
        var present = expected.TryGetProperty("rootMotion", out var root);
        Require(host.RootMotion.Present == present, label + "/warped root presence");
        if (present)
        {
            Require(root.GetProperty("name").GetString() == "RootMotionDelta" && root.GetProperty("bone").GetString() == "root" &&
                root.GetProperty("namespace").GetString() == "bone" && root.GetProperty("type").GetString() == "/Script/Engine.TransformAnimationAttribute", "Wrong warped attribute identity.");
            var actual = host.RootMotion.Value; var target = LyraLogicalSourceBank.ParsePose(root);
            var p = Math.Sqrt((actual.Position - target.Position).LengthSquared);
            var q = Math.Sqrt((actual.Rotation + target.Rotation * (AlsQuaternion.Dot(actual.Rotation, target.Rotation) < 0 ? 1 : -1)).LengthSquared);
            var s = Math.Sqrt((actual.Scale - target.Scale).LengthSquared);
            _position = Math.Max(_position, p); _rotation = Math.Max(_rotation, q); _scale = Math.Max(_scale, s);
            Require(p <= 1e-8 && q <= 1e-10 && s <= 1e-12, $"{label}/warpedRoot positionCm={p:R} quaternion={q:R} scale={s:R}"); _present++;
        }
        _frames++;
    }
    public void Finish()
    {
        Require(_frames == 3528 && _present == _expectedRootPresent, "Incomplete Orientation output coverage.");
        GD.Print($"{(_runtime ? "LYRA_CYCLE_RUNTIME_GODOT_OK" : _stride ? "LYRA_STRIDE_GODOT_OK" : "LYRA_ORIENTATION_GODOT_OK")} frames={_frames} bones={_frames * 81} rootPresent={_present} " +
            $"rootPositionCm={_position:R} rootQuaternion={_rotation:R} rootScale={_scale:R} retry=true stage={(_runtime ? "OriginalCycleRoot" : _stride ? "AfterStride" : "AfterOrientation")} production=false wholeMain=false");
    }
}
