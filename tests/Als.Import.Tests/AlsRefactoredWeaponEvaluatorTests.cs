using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponEvaluatorTests
{
    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow, 13)]
    [InlineData(AlsRefactoredWeaponKind.Rifle, 16)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded, 11)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded, 12)]
    public void OriginalLeavesSampleFixedFramesAndPitchWithoutClocks(AlsRefactoredWeaponKind kind, int count)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var source = new AlsRefactoredWeaponSourceProfile(catalog,
            new(catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, kind)));
        var profile = new AlsRefactoredWeaponEvaluators(catalog, source);
        Assert.Equal(count, profile.Evaluators.Length); Assert.Equal(2, profile.Evaluators.ToArray().Count(d => d.Pitch));
        Assert.Equal(79, profile.BoneNames.Length);
        var leaves = profile.Evaluators.ToArray(); var names = profile.CurveNames.ToArray();
        Parallel.For(0, 3, worker =>
        {
            var sampler = profile.CreateSampler(); var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[names.Length];
            foreach (var leaf in leaves)
            {
                foreach (var pitch in new[] { 0f, .1234567f, .5f, .999f, 1f })
                {
                    var expected = new AlsPrecisePose[79]; AlsInertialCurve[] expectedCurves; string[] sourceNames;
                    if (leaf.Pitch)
                    {
                        var aim = catalog.CompileAdditivePose(leaf.Source); sourceNames = aim.CurveNames.ToArray();
                        expectedCurves = new AlsInertialCurve[sourceNames.Length]; aim.CreateSampler().Sample(pitch, expected, expectedCurves);
                    }
                    else
                    {
                        var absolute = catalog.CompileAbsolutePoseWithCurves(leaf.Source); sourceNames = absolute.Curves.Names.ToArray();
                        expectedCurves = new AlsInertialCurve[sourceNames.Length];
                        var time = (float)((double)leaf.Frame * absolute.Pose.Data.FrameRateDenominator / absolute.Pose.Data.FrameRateNumerator);
                        absolute.Pose.CreateSampler(absolute.Curves).Sample(time, true, false, false, expected, expectedCurves);
                    }
                    sampler.Sample(leaf.PropertyIndex, pitch, pose, curves);
                    Assert.Equal(expected, pose);
                    for (var i = 0; i < names.Length; i++)
                    {
                        var c = Array.FindIndex(sourceNames, n => n.Equals(names[i], StringComparison.OrdinalIgnoreCase));
                        Assert.Equal(c < 0 ? default : expectedCurves[c], curves[i]);
                    }
                    var before = pose.ToArray(); var beforeCurves = curves.ToArray();
                    Assert.Throws<ArgumentException>(() => sampler.Sample(leaf.PropertyIndex, float.NaN, pose, curves));
                    Assert.Throws<ArgumentException>(() => sampler.Sample(-1, pitch, pose, curves));
                    Assert.Equal(before, pose); Assert.Equal(beforeCurves, curves);
                }
            }
        });

        var payload = catalog.Read(AlsRefactoredWeaponMachineResources.Blueprint(kind));
        foreach (var field in new[] { "method", "sequence", "explicitFrame", "bTeleportToExplicitTime" })
        {
            var json = JsonNode.Parse(payload.GetRawText())!;
            var leaf = leaves.First(d => !d.Pitch);
            var runtime = json["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == leaf.PropertyIndex)!["runtime"]!;
            runtime[field] = field switch { "explicitFrame" => JsonValue.Create(999), "bTeleportToExplicitTime" => JsonValue.Create(false), _ => JsonValue.Create("Unsupported") };
            using var changed = JsonDocument.Parse(json.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredWeaponEvaluators.Compile(changed.RootElement, source));
        }
        var bindingJson = JsonNode.Parse(payload.GetRawText())!;
        bindingJson["nativeText"] = payload.GetProperty("nativeText").GetString()!.Replace("\"PitchAmount\"", "\"YawAmount\"", StringComparison.Ordinal);
        using var wrongBinding = JsonDocument.Parse(bindingJson.ToJsonString());
        Assert.Throws<ArgumentException>(() => AlsRefactoredWeaponEvaluators.Compile(wrongBinding.RootElement, source));
    }
}
