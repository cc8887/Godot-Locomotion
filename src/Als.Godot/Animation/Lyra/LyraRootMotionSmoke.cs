using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraRootMotionSmoke : Node
{
    public override void _Ready()
    {
        try { LyraCycleLayerSourceSmoke.Run(true, true); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Root motion failed: " + error); GetTree().Quit(1); }
    }
}

internal sealed class LyraRootMotionComparison
{
    private int _ranges, _probes, _frames, _present, _identity;
    private double _position, _rotation, _scale;
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    public LyraRootMotionComparison(LyraLogicalSourceBank bank, JsonElement native)
    {
        const string root = "res://assets/generated/lyra_als/";
        Require(native.GetProperty("schemaVersion").GetInt32() == 1 && native.GetProperty("rootPolicySha256").GetString() ==
            LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + "root_motion_policy.json")), "Stale root policy fixture.");
        foreach (var dependency in native.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root + dependency.Name)), "Stale root oracle dependency.");
        var slots = native.GetProperty("slots").EnumerateArray().Select(s => s.GetString()!).ToArray();
        var samplers = slots.Select(s => new AlsRawRootMotionIntervalSampler(bank.Get(s).Data, bank.Reference[0], bank.Get(s).NormalizedRootMotionScale)).ToArray();
        var requests = native.GetProperty("requests"); var rows = native.GetProperty("root").GetProperty("rows");
        Require(slots.Length == 189 && requests.GetArrayLength() == 3024 && rows.GetArrayLength() == 3024, "Incomplete root source ranges.");
        for (var index = 0; index < requests.GetArrayLength(); index++)
        {
            var request = requests[index]; var expected = rows[index]; var id = request.GetProperty("sequence").GetInt32();
            Require(expected.GetProperty("sequence").GetInt32() == id && expected.GetProperty("case").GetInt32() == request.GetProperty("case").GetInt32(), "Root range order changed.");
            var raw = samplers[id].SampleRoot(request.GetProperty("sampleTime").GetDouble());
            ComparePose(raw, expected.GetProperty("rawSample"), slots[id] + "/rawRoot");
            ComparePose(raw, expected.GetProperty("rootSample"), slots[id] + "/providerRoot");
            var actual = samplers[id].Extract(request.GetProperty("previous").GetSingle(), request.GetProperty("delta").GetSingle(), request.GetProperty("looping").GetBoolean());
            ComparePose(actual, expected.GetProperty("extracted"), slots[id] + "/range" + request.GetProperty("case"));
            var present = bank.Get(slots[id]).EnableRootMotion;
            Require(present == expected.GetProperty("present").GetBoolean(), "Provider presence disagrees with source metadata.");
            ComparePose(present ? actual : AlsPrecisePose.Identity, expected.GetProperty("provided"), slots[id] + "/provided");
            _ranges++;
        }
        foreach (var probe in native.GetProperty("probes").EnumerateArray())
        {
            var presence = probe.GetProperty("presence").GetInt32(); var weight = probe.GetProperty("weight").GetSingle();
            var first = new AlsPrecisePose(new(17, -7, 3), new(0, 0, Math.Sin(.3), Math.Cos(.3)), new(1.2, .8, 1.5));
            var second = new AlsPrecisePose(new(-11, 9, -5), new(Math.Sin(.45), 0, 0, -Math.Cos(.45)), new(.7, 1.3, .9));
            var actual = LyraRootMotionAttribute.Blend(new(first, (presence & 1) != 0), new(second, (presence & 2) != 0),
                weight, probe.GetProperty("override").GetBoolean());
            CompareAttribute(actual, probe.GetProperty("output"), $"transformProbe/{presence}/{weight:R}/{probe.GetProperty("override")}"); _probes++;
        }
        Require(_probes == 72, "Missing transform attribute probes.");
    }
    private void ComparePose(AlsPrecisePose actual, JsonElement expected, string label)
    {
        var target = LyraLogicalSourceBank.ParsePose(expected);
        var p = Math.Sqrt((actual.Position - target.Position).LengthSquared);
        var a = actual.Rotation; var b = target.Rotation; var sign = AlsQuaternion.Dot(a, b) < 0 ? -1 : 1;
        var q = Math.Sqrt((a + b * -sign).LengthSquared); var s = Math.Sqrt((actual.Scale - target.Scale).LengthSquared);
        _position = Math.Max(_position, p); _rotation = Math.Max(_rotation, q); _scale = Math.Max(_scale, s);
        Require(p <= 1e-8 && q <= 1e-10 && s <= 1e-12, $"{label}: positionCm={p:R} quaternion={q:R} scale={s:R}");
    }
    private void CompareAttribute(LyraRootMotionAttribute actual, JsonElement expected, string label)
    {
        var present = expected.TryGetProperty("rootMotion", out var root);
        Require(actual.Present == present, label + "/presence");
        if (!present) return;
        Require(root.GetProperty("name").GetString() == "RootMotionDelta" && root.GetProperty("bone").GetString() == "root" &&
            root.GetProperty("namespace").GetString() == "bone" && root.GetProperty("type").GetString() == "/Script/Engine.TransformAnimationAttribute", "Wrong generated attribute identity.");
        ComparePose(actual.Value, root, label);
    }
    public void Compare(JsonElement expected, LyraCycleLayerPoseHost host, string label)
    {
        CompareAttribute(host.RootMotion, expected, label);
        Require(host.HasGeneratedRootMotion == host.RootMotion.Present, "Root availability diverged from pose availability.");
        if (host.RootMotion.Present)
        {
            _present++;
            if (host.RootMotion.Value.Position.LengthSquared == 0 && host.RootMotion.Value.Rotation == AlsQuaternion.Identity) _identity++;
        }
        _frames++;
    }
    public void Finish()
    {
        Require(_ranges == 3024 && _probes == 72 && _frames == 3528 && _present > 0 && _identity > 0, "Incomplete generated RootMotion coverage.");
        GD.Print($"LYRA_ROOT_MOTION_GODOT_OK sequences=189 ranges={_ranges} probes={_probes} frames={_frames} present={_present} identity={_identity} " +
            $"positionCm={_position:R} quaternion={_rotation:R} scale={_scale:R} retry=true stage=ProviderPreWarp production=false");
    }
}
