using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraCycleLayerPoseSmoke : Node
{
    public override void _Ready()
    {
        try { LyraCycleLayerSourceSmoke.Run(true); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Cycle layer pose failed: " + error); GetTree().Quit(1); }
    }
}

internal sealed class LyraCycleLayerPoseComparison
{
    private readonly LyraLogicalSourceBank _bank;
    private double _position, _rotation, _scale;
    private int _frames, _curves, _attributes, _probes;
    private readonly bool _rootMotion;
    private readonly bool _orientation;
    private readonly bool _stride;
    private readonly bool _runtime;
    private readonly bool _mainCycleLean;
    private readonly int _expectedFrames;
    private readonly int _expectedAttributes;
    private readonly string? _stage;
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    public LyraCycleLayerPoseComparison(LyraLogicalSourceBank bank, JsonElement native, bool rootMotion = false, bool orientation = false,
        bool stride = false, bool runtime = false, bool mainCycleLean = false, int expectedFrames = 3528, string? stage = null,
        int? expectedAttributes = null)
    {
        _bank = bank;
        _rootMotion = rootMotion;
        _orientation = orientation;
        _stride = stride;
        _runtime = runtime;
        _mainCycleLean = mainCycleLean;
        _expectedFrames = expectedFrames; _stage = stage; _expectedAttributes = expectedAttributes ?? checked(expectedFrames*4);
        Require(native.GetProperty("schemaVersion").GetInt32() == 1 && native.GetProperty("traces").GetArrayLength() == 9 &&
            native.GetProperty("policySha256").GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(
                "res://assets/generated/lyra_als/cycle_layer_pose_policy.json")), "Stale Cycle pose fixture.");
        foreach (var dependency in native.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(
                "res://assets/generated/lyra_als/" + dependency.Name)), "Stale Cycle pose oracle dependency.");
        foreach (var row in native.GetProperty("probes").EnumerateArray())
        {
            var presence = row.GetProperty("presence").GetInt32(); var weight = row.GetProperty("weight").GetSingle();
            var result = LyraLayeredDataBlend.BlendInteger(new(17, (presence & 1) != 0), new(-11, (presence & 2) != 0),
                weight, row.GetProperty("override").GetBoolean());
            var expected = row.GetProperty("output").GetProperty("attributes");
            Require(result.Present == (expected.GetArrayLength() != 0) && (!result.Present || result.Value == expected[0].GetProperty("value").GetInt32()),
                $"Native integer attribute operator mismatch presence={presence} weight={weight:R} override={row.GetProperty("override")}: {result}.");
            var shared = LyraLayeredDataBlend.OverrideCurve(new(17, true, 1), new(-11, true, 2), -1);
            var expectedCurve = row.GetProperty("output").GetProperty("curves").GetProperty("ProbeShared");
            Require(shared.Value == expectedCurve.GetProperty("value").GetSingle() && shared.Flags == expectedCurve.GetProperty("flags").GetUInt32(), "Curve Override scaled/merged the child.");
            Require(LyraLayeredDataBlend.OverrideCurve(new(-7, true), default, -1).Value ==
                row.GetProperty("output").GetProperty("curves").GetProperty("ProbeBaseOnly").GetProperty("value").GetSingle(), "Override removed the base-only curve.");
            Require(LyraLayeredDataBlend.OverrideCurve(default, new(9, true), -1).Value ==
                row.GetProperty("output").GetProperty("curves").GetProperty("ProbeChildOnly").GetProperty("value").GetSingle(), "Override lost the child-only curve.");
            _probes++;
        }
        Require(_probes == 72, "Incomplete native data probes.");
    }
    public void Compare(JsonElement expected, LyraCycleLayerPoseHost host, string label, ReadOnlySpan<AlsPrecisePose> finalPose = default)
    {
        Require(_rootMotion || !host.HasGeneratedRootMotion, "Authored fixture unexpectedly accepted generated RootMotion.");
        Compare(expected, finalPose.IsEmpty ? host.Pose : finalPose, host.Curves, host.Attributes, label);
    }
    public void Compare(JsonElement expected, ReadOnlySpan<AlsPrecisePose> pose, ReadOnlySpan<LyraCurveSample> curves,
        ReadOnlySpan<LyraAttributeSample> attributes, string label)
    {
        var rows = expected.GetProperty("pose");
        Require(rows.GetArrayLength() == 81 && pose.Length == 81, "Incomplete Cycle pose.");
        for (var bone = 0; bone < 81; bone++)
        {
            var value = pose[bone]; var target = LyraLogicalSourceBank.ParsePose(rows[bone]);
            var p = Math.Sqrt((value.Position - target.Position).LengthSquared);
            var a = value.Rotation.Normalized(); var b = target.Rotation.Normalized(); var sign = AlsQuaternion.Dot(a, b) < 0 ? -1 : 1;
            var q = Math.Sqrt((a + b * -sign).LengthSquared); var s = Math.Sqrt((value.Scale - target.Scale).LengthSquared);
            _position = Math.Max(_position, p); _rotation = Math.Max(_rotation, q); _scale = Math.Max(_scale, s);
            Require(p <= 1e-8 && q <= 1e-10 && s <= 1e-12, $"{label}/bone{bone}: positionCm={p:R} quaternion={q:R} scale={s:R}.");
        }
        var curveRows = expected.GetProperty("curves"); var seen = 0;
        for (var id = 0; id < curves.Length; id++)
        {
            var present = curveRows.TryGetProperty(_bank.Curves.Names[id], out var curve); var actual = curves[id];
            Require(actual.Present == present && (!present || BitConverter.SingleToInt32Bits(actual.Value) ==
                BitConverter.SingleToInt32Bits(curve.GetProperty("value").GetSingle()) && actual.Flags == curve.GetProperty("flags").GetUInt32()), label + "/curve " + _bank.Curves.Names[id]);
            if (present) { seen++; _curves++; }
        }
        Require(seen == curveRows.EnumerateObject().Count(), "Unrepresented Cycle curve.");
        var attributeRows = expected.GetProperty("attributes").EnumerateArray().ToArray(); seen = 0;
        for (var id = 0; id < attributes.Length; id++)
        {
            var identity = _bank.Curves.Attributes.Layout[id]; var actual = attributes[id];
            var matches = attributeRows.Where(a => a.GetProperty("name").GetString() == identity.Name &&
                a.GetProperty("bone").GetString()!.Equals(identity.Bone, StringComparison.OrdinalIgnoreCase) &&
                a.GetProperty("type").GetString() == identity.Type && a.GetProperty("namespace").GetString() == identity.Namespace).ToArray();
            Require(actual.Present == (matches.Length == 1) && matches.Length <= 1 &&
                (!actual.Present || actual.Value == matches[0].GetProperty("value").GetInt32()), label + "/attribute " + identity.Name);
            if (actual.Present) { seen++; _attributes++; }
        }
        Require(seen == attributeRows.Length, "Unrepresented Cycle attribute."); _frames++;
    }
    public void Finish()
    {
        Require(_frames == _expectedFrames && _attributes == _expectedAttributes, "Incomplete native locomotion layer pose coverage.");
        GD.Print($"LYRA_CYCLE_LAYER_POSE_GODOT_OK frames={_frames} bones={_frames * 81} curves={_curves} attributes={_attributes} dataProbes={_probes} " +
            $"positionCm={_position:R} quaternion={_rotation:R} scale={_scale:R} retry=true stage={(_stage ?? (_mainCycleLean ? "OriginalMainCycleApplyAdditive" : _runtime ? "OriginalCycleRoot" : _stride ? "AfterStride" : _orientation ? "AfterOrientation" : _rootMotion ? "ProviderPreWarp" : "AuthoredPreWarp"))} generatedRootMotion={_rootMotion} production=false");
    }
}
