using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsAimPoseCompilerTests
{
    internal static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config", name));
    internal static AlsAimPoseDefinition Model() => AlsAimPoseCompiler.Compile(Read("v4_layering_inputs.json"), Read("v4_aim_pose_inputs.json"));

    [Fact]
    public void CompilesCompleteNativeNestedGraphAndCurvePolicies()
    {
        var model = Model();
        Assert.Equal(3, model.Machines.Length); Assert.Equal(7, model.Evaluators.Length); Assert.Equal(4, model.Curves.Length);
        Assert.Equal(19, model.Machines.ToArray().Sum(m => m.Edges.Length));
        Assert.Equal(9, model.Machines.ToArray().Sum(m => m.States.Length));
        Assert.Equal("No Input", model.Machines[1].States[0].Name);
        Assert.Equal("No Offset", model.Machines[2].States[0].Name);
        Assert.Equal(AlsTransitionBlend.HermiteCubic, model.Machines[0].Edges[0].Blend);
        Assert.True(model.Machines[0].Edges[0].Curve >= 0); // Assigned but unused by this non-custom mode.
        Assert.Equal(4, model.Machines[2].Edges.ToArray().Count(e => e.Duration == 0));
        Assert.Equal(4, model.Machines[2].Edges.ToArray().Count(e => e.Rule.Kind == AlsAimRuleKind.YawRangeStateNotFull));
        Assert.Equal(7, model.Evaluators.ToArray().Select(e => e.CompiledIndex).Distinct().Count());
        Assert.Equal(10, model.Machines.ToArray().Sum(m => m.Edges.ToArray().Count(e => e.HeadProfile)));
        Assert.Equal(79, model.Head.BoneNames.Length);
        for (var bone = 0; bone < model.Head.BoneNames.Length; bone++)
        {
            var weights = model.Head.Weights(bone, .5f);
            var head = model.Head.BoneNames[bone] is "neck_01" or "head";
            Assert.Equal(head ? .8f : .5f, weights.X, 6);
            Assert.Equal(head ? .2f : .5f, weights.Y, 6);
        }
    }

    [Fact]
    public void CameraRulesRetainDeadBandsHistoryGatesAndElapsedDelay()
    {
        var model = Model(); var camera = model.Machines[2];
        var ranges = camera.Edges.ToArray().Select(e => e.Rule).Where(r => r.Kind == AlsAimRuleKind.YawRange).Distinct().ToArray();
        Assert.Equal(3, ranges.Length);
        foreach (var rule in ranges)
        {
            Assert.True(rule.Matches(AlsRotationMode.Aiming, false, rule.Minimum, 0, []));
            Assert.True(rule.Matches(AlsRotationMode.Aiming, false, rule.Maximum, 0, []));
            Assert.False(rule.Matches(AlsRotationMode.Aiming, false, MathF.BitDecrement(rule.Minimum), 0, []));
            Assert.False(rule.Matches(AlsRotationMode.Aiming, false, MathF.BitIncrement(rule.Maximum), 0, []));
            Assert.False(rule.Matches(AlsRotationMode.Aiming, false, 127, 0, []));
            Assert.False(rule.Matches(AlsRotationMode.Aiming, false, -127, 0, []));
        }
        foreach (var rule in camera.Edges.ToArray().Select(e => e.Rule).Where(r => r.Kind == AlsAimRuleKind.YawRangeStateNotFull))
        {
            var weights = new float[5]; var yaw = (rule.Minimum + rule.Maximum) / 2;
            weights[rule.WeightState] = 1;
            Assert.False(rule.Matches(AlsRotationMode.Aiming, false, yaw, 0, weights));
            weights[rule.WeightState] = MathF.BitDecrement(1);
            Assert.True(rule.Matches(AlsRotationMode.Aiming, false, yaw, 0, weights));
        }
        var elapsed = camera.Edges.ToArray().First(e => e.Rule.Kind == AlsAimRuleKind.Elapsed).Rule;
        Assert.False(elapsed.Matches(AlsRotationMode.Aiming, false, 160, 2, []));
        Assert.True(elapsed.Matches(AlsRotationMode.Aiming, false, 160, MathF.BitIncrement(2), []));
    }

    [Fact]
    public void EvaluatorsUsePitchAndSeparateSecondsFromNormalizedTime()
    {
        var model = Model();
        var input = new AlsAimingInputState(new(1, 4, 1), default, default, new(160, 35), default, .2, .3, .1, .9, .95);
        foreach (var evaluator in model.Evaluators)
        {
            var state = model.Machines[evaluator.Machine].States[evaluator.State].Name;
            var expected = state switch
            {
                "No Input" => new AlsAimEvaluatorInput(0, .5f, false),
                "Has Input" => new(0, .3f, false),
                "No Offset" => new(0, .5f, true),
                "SwitchSidesBlendPose" => new(35, .5f, true),
                "Looking Left and Back" => new(35, .1f, true),
                "Looking Right and Back" => new(35, .9f, true),
                "Looking Forwards" => new(35, .95f, true),
                _ => throw new InvalidOperationException(state)
            };
            Assert.Equal(expected, evaluator.Evaluate(input));
        }
    }

    [Fact]
    public void InterruptedAimTransitionsKeepTheirOwnNativeCurvesAndLifetime()
    {
        var model = Model();
        // Out reaches 1 halfway through its duration; In is still near its
        // delayed start. Native FAlphaBlend retires Out at that endpoint.
        var curves = model.Curves.ToArray();
        Func<int, float, float> sample = (edge, time) => curves[edge == 0 ? 1 : 0].Sample(time);
        var state = AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, 1, AlsTransitionBlend.Custom);
        state = AlsTransitionStack.AdvancePerTransition(state, .25f, out _, sample);
        state = AlsTransitionStack.Start(state, 2, 1, AlsTransitionBlend.Custom);
        state = AlsTransitionStack.AdvancePerTransition(state, .25f, out var observed, sample);
        var oracle = JsonNode.Parse(Read("v4_aim_pose_inputs.json"))!["curves"]!.AsArray();
        var outgoing = oracle.Single(c => c!["path"]!.GetValue<string>().EndsWith("AO_SwitchSidesOut.AO_SwitchSidesOut", StringComparison.Ordinal))!;
        var incoming = oracle.Single(c => c!["path"]!.GetValue<string>().EndsWith("AO_SwitchSidesIn.AO_SwitchSidesIn", StringComparison.Ordinal))!;
        Assert.Equal(1, state.Count); Assert.Equal(2, observed.Count);
        Assert.Equal(outgoing["verification"]![100]!["value"]!.GetValue<float>(), observed.GetTransition(0).Alpha, 6);
        Assert.Equal(incoming["verification"]![50]!["value"]!.GetValue<float>(), observed.GetTransition(1).Alpha, 6);
        Assert.Equal(1, observed.GetTransition(0).Alpha);
        Assert.True(observed.GetTransition(0).Remaining > 0);
        Assert.True(observed.GetTransition(0).Complete);
        Assert.False(observed.GetTransition(1).Complete);
        Assert.NotEqual(observed.GetTransition(0).Alpha, observed.GetTransition(1).Alpha);
        var prior = state;
        // Cleanup shifts the surviving edge to index zero; it still owns In.
        Func<int, float, float> survivor = (_, time) => curves[0].Sample(time);
        var first = AlsTransitionStack.AdvancePerTransition(prior, .5f, out _, survivor);
        var retry = AlsTransitionStack.AdvancePerTransition(prior, .5f, out _, survivor);
        Assert.Equal(1, first.Count); Assert.Equal(first.GetTransition(0), retry.GetTransition(0));
        Assert.Equal(incoming["verification"]![150]!["value"]!.GetValue<float>(), first.GetTransition(0).Alpha, 6);
        Assert.Equal(1, prior.Count);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("initial")]
    [InlineData("duration")]
    [InlineData("blend")]
    [InlineData("delegate")]
    [InlineData("weight_gate")]
    [InlineData("range")]
    [InlineData("pitch_axis")]
    [InlineData("center")]
    [InlineData("curve_value")]
    [InlineData("curve_sample")]
    [InlineData("head_mode")]
    [InlineData("head_scale")]
    [InlineData("head_oracle")]
    [InlineData("priority")]
    public void RejectsChangedConsumedSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_aim_pose_inputs.json"))!;
        var machine = root["bakedMachines"]!.AsArray().Single(m => m!["machineName"]!.GetValue<string>() == "Look Towards Camera States")!;
        switch (mutation)
        {
            case "source": root["source"] = "Other"; break;
            case "initial": machine["initialState"] = 1; break;
            case "duration": machine["transitions"]![0]!["crossfadeDuration"] = 0; break;
            case "blend": machine["transitions"]![0]!["blendMode"] = "HermiteCubic"; break;
            case "delegate": machine["states"]![0]!["transitions"]![0]!["canTakeDelegateIndex"] = -1; break;
            case "curve_value": root["curves"]![0]!["curve"]!["keys"]![0]!["value"] = .5; break;
            case "curve_sample": root["curves"]![0]!["verification"]![0]!["value"] = .5; break;
            case "head_mode": root["blendProfiles"]![0]!["mode"] = 0; break;
            case "head_scale": root["blendProfiles"]![0]!["bones"]!.AsArray().Single(b => b!["name"]!.GetValue<string>() == "head")!["scale"] = 1; break;
            case "head_oracle": root["blendProfiles"]![0]!["nativeCases"]![16]!["incoming"]![0] = .5; break;
            case "priority":
                root["editorStateNodes"]!.AsArray().Single(n => n!["path"]!.GetValue<string>().EndsWith("States.AnimStateTransitionNode_62", StringComparison.Ordinal))!["properties"]!["PriorityOrder"] = 0;
                break;
            default:
                var suffix = mutation switch { "weight_gate" => "AnimStateTransitionNode_77.Transition", "range" => "AnimStateTransitionNode_67.Transition",
                    "pitch_axis" => "AnimStateNode_2.Looking Left and Back", _ => "AnimStateNode_1.No Input" };
                var graph = root["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(suffix, StringComparison.Ordinal))!;
                var text = graph["nativeText"]!.GetValue<string>();
                var (from, to) = mutation switch
                {
                    "weight_gate" => ("MemberName=\"NotEqual_DoubleDouble\"", "MemberName=\"EqualEqual_DoubleDouble\""),
                    "range" => ("DefaultValue=\"-130.000000\"", "DefaultValue=\"-120.000000\""),
                    "pitch_axis" => ("PinName=\"SmoothedAimingAngle_Y\"", "PinName=\"SmoothedAimingAngle_Z\""),
                    _ => ("DefaultValue=\"0.500000\"", "DefaultValue=\"0.250000\"")
                };
                Assert.Contains(from, text); graph["nativeText"] = text.Replace(from, to, StringComparison.Ordinal); break;
        }
        Assert.ThrowsAny<Exception>(() => AlsAimPoseCompiler.Compile(Read("v4_layering_inputs.json"), root.ToJsonString()));
    }
}
