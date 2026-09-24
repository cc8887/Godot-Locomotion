using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsCameraGraphCompilerTests
{
    [Fact]
    public void CompilesEffectivePinsCachesAndAllSixLookTransitions()
    {
        var graph = AlsCameraGraphCompiler.Compile(Read());
        var action = Assert.IsType<AlsCameraSelectPose>(graph.Root);
        Assert.Equal("LocomotionAction", action.Input);
        Assert.Equal(new[] { .4f, .2f, .2f, 1f }, action.Times);
        Assert.Equal("Cubic", action.Blend);
        var ragdoll = Assert.IsType<AlsCameraModifyCurves>(action.Children[3]);
        Assert.Equal(-320, ragdoll.Values["CameraOffsetX"]);
        Assert.Equal(.05f, ragdoll.Values["RotationLag"]);
        Assert.IsType<AlsCameraReferencePose>(ragdoll.Source);
        var view = Assert.IsType<AlsCameraPoseCache>(action.Children[0]);
        Assert.Equal("View Mode", view.Name);
        Assert.Equal("HermiteCubic", Assert.IsType<AlsCameraSelectPose>(view.Source).Blend);
        Assert.Same(view, Assert.IsType<AlsCameraModifyCurves>(action.Children[1]).Source);
        Assert.Same(view, Assert.IsType<AlsCameraModifyCurves>(action.Children[2]).Source);
        var states = graph.Nodes.Values.OfType<AlsCameraLookStates>().Single();
        Assert.Equal("AnimStateNode_0", states.Entry);
        Assert.Equal(3, states.States.Count); Assert.Equal(6, states.Transitions.Count);
        Assert.Equal(2, states.Transitions.Count(t => t.Seconds == .35f && t.Curve.EndsWith("CF_Als_CameraBlend_Quick", StringComparison.Ordinal)));
        Assert.Equal(4, states.Transitions.Count(t => t.Seconds == 1f));
        var velocityRule = states.Transitions.First(t => t.To == "AnimStateNode_0").Condition;
        Assert.Equal("Or", velocityRule.Operation);
        Assert.Equal("Als.RotationMode.VelocityDirection", velocityRule.A!.Value);
        Assert.Equal("Not", velocityRule.B!.Operation);
        Assert.Equal("Valid", velocityRule.B.A!.Operation);
        var shoulder = graph.Nodes.Values.OfType<AlsCameraSelectPose>().Single(n => n.Boolean);
        Assert.Equal("bRightShoulder", shoulder.Input); Assert.Equal(new[] { .5f, .5f }, shoulder.Times);
        Assert.Equal(1, Assert.IsType<AlsCameraModifyCurves>(shoulder.Children[0]).Values["CameraOffsetY"]);
        Assert.Equal(-1, Assert.IsType<AlsCameraModifyCurves>(shoulder.Children[1]).Values["CameraOffsetY"]);
        Assert.All(graph.Nodes.Values.OfType<AlsCameraSelectPose>().Where(n => n.Input == "Gait"), n => Assert.True(n.ResetChildOnActivate));
    }

    [Theory]
    [InlineData("foreign_selector")]
    [InlineData("negative_time")]
    [InlineData("unsupported_modifier")]
    [InlineData("missing_curve")]
    [InlineData("foreign_condition")]
    [InlineData("dynamic_curve")]
    [InlineData("child_update")]
    [InlineData("negative_transition")]
    [InlineData("missing_blend")]
    [InlineData("foreign_blend")]
    [InlineData("conflicting_blend")]
    public void RejectsUnsupportedCameraChanges(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var text = root["graphs"]![0]!["nativeText"]!.GetValue<string>();
        if (mutation == "foreign_selector") text = text.Replace("PathAsText=\"ViewMode\",PropertyPath=(\"ViewMode\")", "PathAsText=\"Unknown\",PropertyPath=(\"Unknown\")");
        if (mutation == "negative_time") text = text.Replace("DefaultValue=\"0.400000\"", "DefaultValue=\"-0.400000\"");
        if (mutation == "unsupported_modifier") text = text.Replace("ApplyMode=Scale", "ApplyMode=WeightedMovingAverage");
        if (mutation == "missing_curve") root["assets"]!.AsArray().RemoveAt(4);
        if (mutation == "foreign_condition") text = text.Replace("GameplayTags.BlueprintGameplayTagLibrary", "Foreign.BlueprintGameplayTagLibrary");
        if (mutation == "dynamic_curve") text = text.Replace("ApplyMode=Scale", "ApplyMode=Scale,Alpha=0.5");
        if (mutation == "child_update") text = text.Replace("ChildUpateMode=ResetChildOnActivate", "ChildUpateMode=Unknown");
        if (mutation == "negative_transition") text = text.Replace("CrossfadeDuration=0.350000", "CrossfadeDuration=-0.350000");
        if (mutation == "missing_blend") root["blendNodes"]!.AsArray().RemoveAt(0);
        if (mutation == "foreign_blend") root["blendNodes"]!.AsArray().Add(new JsonObject { ["path"] = "foreign", ["blendType"] = "LINEAR" });
        if (mutation == "conflicting_blend") text = text.Replace("BlendType=Cubic", "BlendType=Linear");
        root["graphs"]![0]!["nativeText"] = text;
        Assert.Throws<ArgumentException>(() => AlsCameraGraphCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void CustomBlendCurvesMatchNativeSamples()
    {
        var json = Read(); var graph = AlsCameraGraphCompiler.Compile(json); var root = JsonNode.Parse(json)!;
        var samples = 0;
        foreach (var asset in root["assets"]!.AsArray().Where(a => a!["class"]!.GetValue<string>() == "/Script/Engine.CurveFloat"))
        {
            var curve = graph.Curves[asset!["path"]!.GetValue<string>()];
            foreach (var sample in asset["samples"]!.AsArray())
            {
                // FAlphaBlend::AlphaToBlendOption clamps the raw CurveFloat
                // sample. Quick overshoots 1 near its endpoint by design.
                var expected = System.Math.Clamp(sample!["value"]!.GetValue<float>(), 0, 1);
                Assert.InRange(System.Math.Abs(curve.Sample(sample["time"]!.GetValue<float>()) - expected), 0, 2e-6f);
                samples++;
            }
            Assert.Equal(0, curve.Sample(-1)); Assert.Equal(1, curve.Sample(2));
        }
        Assert.Equal(202, samples);
    }
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "refactored_camera_inputs.json"));
}
