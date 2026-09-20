using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class PreciseRawSourceSmoke : Node
{
    private delegate AlsRawPoseKeySelection Sample(double time, bool retarget, bool extract, bool ignore,
        Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves);
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private static void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var definition = AlsMovementGraphDefinition.Load(set, AlsLocomotionProfileCompiler.Compile(Read("assets/config/p4_cycle_locomotion_profile.json"), set));
        var stop = OS.GetCmdlineUserArgs().Contains("--stop-source");
        var bank = stop ? definition.StopRawSources : definition.OverlayRawSources;
        var additive = OS.GetCmdlineUserArgs().Contains("--additive");
        var aliases = OS.GetCmdlineUserArgs().Contains("--refactored-source-curves");
        var sources = bank.Sources.ToArray().Where(s => !additive || s.Policy.AdditiveType != AlsRawAnimationAdditiveType.None).ToArray();
        var names = bank.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray()).Append("__not_authored__")
            .Concat(aliases ? AlsRefactoredV4SourceCurves.TargetNames : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        using var fixture = JsonDocument.Parse(Read(stop ? "tests/Als.Import.Tests/Fixtures/Stop/" +
            (additive ? "native_additive_poses.json" : "native_source_poses.json") : "tests/Als.Core.Tests/Fixtures/P3/" +
            (additive ? "v4_overlay_additive_source_pose_native.json" : "v4_overlay_source_pose_native.json")));
        Require(fixture.RootElement.GetProperty("schemaVersion").GetInt32() == 1, "Unknown precise native fixture schema.");
        if (!additive)
        {
            Require(fixture.RootElement.GetProperty("source").GetString() == "UAnimSequence.GetBonePose(forceRaw=true); non-additive source evaluation", "Wrong raw native evaluation.");
            var request = fixture.RootElement.GetProperty("request");
            Require(request.GetProperty("definitionDigest").GetString() == bank.DefinitionDigest && request.GetProperty("bindingDigest").GetString() == bank.BindingDigest,
                "Raw native fixture binding differs.");
        }
        else Require(fixture.RootElement.GetProperty("sourceIndexSha256").GetString()!.Equals(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Godot.FileAccess.GetFileAsBytes("res://assets/config/" +
                (stop ? "v4_stop_source_inputs.json" : "v4_overlay_source_inputs.json")))),
            StringComparison.OrdinalIgnoreCase), "Additive native fixture targets a different source bank.");
        var assets = fixture.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Require(assets.Length == sources.Length && bank.Sources.Length == (stop ? 3 : 36), "Precise raw source closure differs.");
        var count = 0; var boneCount = 0; var pError = 0.0; var qError = 0.0; var sError = 0.0; var cError = 0f;
        var aliasValues = 0; var aliasNonzero = 0; var aliasNegative = 0;
        foreach (var source in sources)
        {
            var skeleton = bank.GetSkeleton(source.PoseData.Identity.SkeletonId);
            Sample sample = additive ? new AlsPreciseAnimationPoseSourceSampler(source, bank, set, names).Sample :
                new AlsPreciseRawAnimationSourceSampler(source, skeleton, set, names).Sample;
            var pose = new AlsPrecisePose[79]; var retry = new AlsPrecisePose[79];
            var curves = new AlsInertialCurve[names.Length]; var retryCurves = new AlsInertialCurve[names.Length];
            var asset = assets.Single(a => a.GetProperty("assetId").GetString() == source.PoseData.Identity.AssetId);
            Require(asset.GetProperty("source").GetString() == source.PoseData.Identity.AssetPath, "Foreign precise native source.");
            foreach (var row in asset.GetProperty("samples").EnumerateArray())
            {
                Require(row.GetProperty("names").EnumerateArray().Select(n => n.GetString()).SequenceEqual(skeleton.LogicalBoneNames.ToArray()), "Foreign precise bone layout.");
                var seconds = row.GetProperty("timeSeconds").GetDouble(); var retarget = row.GetProperty("shouldRetarget").GetBoolean();
                if (additive) Require(row.GetProperty("evaluatedAdditive").GetBoolean(), "Native additive evaluation was skipped.");
                var extract = row.GetProperty("extractRootMotion").GetBoolean(); var ignore = row.GetProperty("ignoreRootLock").GetBoolean();
                sample(seconds, retarget, extract, ignore, pose, curves);
                sample(seconds, retarget, extract, ignore, retry, retryCurves);
                Require(pose.SequenceEqual(retry) && curves.SequenceEqual(retryCurves), "Precise source replay changed.");
                var expected = row.GetProperty("pose"); Require(expected.GetArrayLength() == 79, "Incomplete native precise pose.");
                for (var bone = 0; bone < 79; bone++)
                {
                    var native = Native(expected[bone]); var actual = pose[bone];
                    var p = Math.Sqrt((native.Position - actual.Position).LengthSquared);
                    var q = Distance(native.Rotation, actual.Rotation); var s = Math.Sqrt((native.Scale - actual.Scale).LengthSquared);
                    pError = Math.Max(pError, p); qError = Math.Max(qError, q); sError = Math.Max(sError, s);
                    Require(p <= .00002 && q <= 2e-10 && s <= 2e-10,
                        $"Precise raw mismatch asset={source.PoseData.Identity.AssetPath} time={seconds:R} bone={skeleton.LogicalBoneNames[bone]} p={p:R} q={q:R} s={s:R}.");
                    boneCount++;
                }
                var expectedCurves = row.GetProperty("curves").EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetSingle(), StringComparer.OrdinalIgnoreCase);
                if (aliases)
                    foreach (var (original, target) in new[] { ("Mask_LandPrediction", "GroundPredictionBlock"), ("FootLock_L", "FootLeftLock"), ("FootLock_R", "FootRightLock") })
                        if (expectedCurves.TryGetValue(original, out var authored))
                        {
                            expectedCurves.Add(target, authored); aliasValues++;
                            if (authored != 0) aliasNonzero++;
                            if (authored < 0) aliasNegative++;
                        }
                Require(curves.Count(c => c.Present) == expectedCurves.Count, "Precise raw curve presence count differs.");
                for (var i = 0; i < names.Length; i++)
                {
                    var present = expectedCurves.TryGetValue(names[i], out var value); var error = MathF.Abs(curves[i].Value - value);
                    Require(curves[i].Present == present && (!present || error <= .00002f), "Precise raw curve differs.");
                    if (present) cError = MathF.Max(cError, error);
                }
                count++;
            }
        }
        Require(count == (stop ? additive ? 104 : 124 : additive ? 676 : 848), "Incomplete precise source coverage.");
        if (aliases)
        {
            Require(aliasValues > 0 && aliasNonzero > 0, "Native fixture never exercised precise source aliases.");
            GD.Print($"REFACTORED_SOURCE_CURVES_NATIVE_OK precision=double additive={additive} values={aliasValues} nonzero={aliasNonzero} negative={aliasNegative} expected=native_original_curves missing=absent");
        }
        GD.Print($"PRECISE_SOURCE_OK stage={(additive ? "animation_pose" : "raw_bone_pose")} sources={sources.Length} poses={count} bones={boneCount} retries={count} position_error={pError:R} quaternion_error={qError:R} scale_error={sError:R} curve_error={cError:R} clocks=caller_supplied");
    }
    private static AlsPrecisePose Native(JsonElement atom)
    {
        var p = atom.GetProperty("position"); var q = atom.GetProperty("rotation"); var s = atom.GetProperty("scale");
        return new(new(p[0].GetDouble() * .01, -p[1].GetDouble() * .01, p[2].GetDouble() * .01),
            new(-q[0].GetDouble(), q[1].GetDouble(), -q[2].GetDouble(), q[3].GetDouble()),
            new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
    }
    private static double Distance(AlsQuaternion a, AlsQuaternion b)
    {
        var minus = a + b * -1; var plus = a + b;
        return Math.Sqrt(Math.Min(minus.LengthSquared, plus.LengthSquared));
    }
    private static string Read(string path) => Godot.FileAccess.GetFileAsString("res://" + path);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
