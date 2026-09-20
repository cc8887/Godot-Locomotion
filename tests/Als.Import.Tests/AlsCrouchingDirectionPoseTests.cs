using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCrouchingDirectionPoseTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsLocomotionSourceProfile> Sources = new(() => AlsLocomotionSourceCompiler.Compile(Read("v4_grounded_dependencies.json"), Set.Value,
        AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), Set.Value).SkeletonId));
    private static readonly Lazy<AlsCrouchingDirectionPoseProfile> Profile = new(() => Compile());
    private static readonly Lazy<AlsGroundedMachineDefinition> Machine = new(() => AlsGroundedMachineCompiler.CompileGrounded(Read("v4_grounded_dependencies.json")).CrouchingDirection!.Runtime);

    [Fact]
    public void CompilesActualSixSourceMappingsCacheOrderAndYawWrites()
    {
        var profile = Profile.Value;
        Assert.Equal(new[] { 49, 50, 55, 51, 52, 53 }, profile.PlayerIds.ToArray());
        Assert.Equal(new[] { 2, 5, 0, 1, 3, 4 }, profile.CacheUpdateOrder.ToArray());
        Assert.Equal(new[] { new AlsCrouchingDirectionPoseRow(0, 1, 2, 4, 0), new(0, 1, 3, 5, 1),
            new(0, 1, 3, 4, 3), new(0, 1, 2, 5, 3), new(0, 1, 2, 5, 2), new(0, 1, 3, 4, 2) }, profile.Rows.ToArray());
        Assert.Equal(15, profile.ProfileBones.ToArray().Count(x => x));
        Assert.Equal(79, profile.LogicalProfileBones.Length);
        Assert.Equal(15, profile.LogicalProfileBones.ToArray().Count(x => x));
        Assert.DoesNotContain(true, profile.LogicalProfileBones[68..].ToArray());
    }

    [Fact]
    public void CurvesDistinguishAbsentFromAuthoredZeroAndIgnoreUnvisitedDirections()
    {
        var model = Machine.Value; var rows = Profile.Value.Rows.ToArray();
        var input = new AlsGroundedRuleInput(true, false, false, AlsStance.Crouching, true, false, 1, 0);
        var state = AlsGroundedStateMachine.Update(model, default, input, new AlsGroundedAutomaticTime[6], 1, .01f, 0).State;
        var caches = new AlsInertialCurve[6];
        Assert.False(AlsCrouchingDirectionPose.CurveWithPresence(rows, state, caches, Vector4.UnitX, Vector4.Zero, false).Present);
        caches[1] = new(7); // Backward source is not visited by forward velocity.
        Assert.False(AlsCrouchingDirectionPose.CurveWithPresence(rows, state, caches, Vector4.UnitX, Vector4.Zero, false).Present);
        caches[0] = new(0);
        Assert.Equal(new AlsInertialCurve(0), AlsCrouchingDirectionPose.CurveWithPresence(rows, state, caches, Vector4.UnitX, Vector4.Zero, false));
        Assert.False(AlsCrouchingDirectionPose.CurveWithPresence(rows, state, caches, Vector4.Zero, Vector4.Zero, false).Present);
        Assert.Equal(new AlsInertialCurve(0), AlsCrouchingDirectionPose.CurveWithPresence(rows, state, caches, Vector4.Zero, Vector4.Zero, true));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void EvaluatesNativeDirectionStackProfilesAndYawWithoutChangingCandidate(int hz)
    {
        var model = Machine.Value; var rows = Profile.Value.Rows.ToArray();
        var times = new AlsGroundedAutomaticTime[6]; var rest = new[] { Pose(-1), Pose(-1) };
        var clips = Enumerable.Range(0, 12).Select(i => Pose(i / 2 * 100)).ToArray();
        var scratch = new AlsLocalPose[12]; var output = new AlsLocalPose[2]; var retry = new AlsLocalPose[2];
        var mask = new[] { false, true }; var values = new float[] { 0, 100, 200, 300, 400, 500 };
        var input = new AlsGroundedRuleInput(true, false, false, AlsStance.Crouching, true, false, 1, 0)
            { MovementDirection = AlsMovementDirection.Left, FeetCrossing = 1 };
        var state = AlsGroundedStateMachine.Update(model, default, input, times, 1, 1f / hz, 0).State;
        Assert.Equal(4, state.CurrentState);
        input = input with { FeetCrossing = 0 };
        var profiled = false;
        for (var frame = 1; frame <= hz; frame++)
        {
            var next = AlsGroundedStateMachine.Update(model, state, input, times, 1, 1f / hz, frame);
            AlsCrouchingDirectionPose.Compose(rows, model, next.State, clips, rest, mask, Vector4.UnitZ, scratch, output);
            AlsCrouchingDirectionPose.Compose(rows, model, next.State, clips, rest, mask, Vector4.UnitZ, scratch, retry);
            Assert.Equal(output, retry);
            var curve = AlsCrouchingDirectionPose.Curve(rows, next.State, values, Vector4.UnitZ, new(10, 20, 30, 40), false);
            Assert.Equal(output[0].Position.X, curve, 4);
            Assert.Equal(30, AlsCrouchingDirectionPose.Curve(rows, next.State, values, Vector4.UnitZ, new(10, 20, 30, 40), true), 4);
            if (next.State.Transitions.Count > 0)
            {
                var alpha = next.State.Transitions.Latest.Alpha;
                Assert.Equal(200 * (1 - alpha) + 300 * alpha, output[0].Position.X);
                var legWeights = AlsGroundedPoseBlend.WeightFactor(alpha, 2, true);
                Assert.Equal(200 * legWeights.Y + 300 * legWeights.X, output[1].Position.X);
                profiled |= output[1].Position.X > output[0].Position.X;
            }
            state = next.State;
        }
        Assert.True(profiled); Assert.Equal(5, state.CurrentState);
        Assert.Equal(300, output[0].Position.X); Assert.Equal(300, output[1].Position.X);
    }

    [Fact]
    public void EmptyVelocityUsesReferencePoseButStillWritesDirectionYaw()
    {
        var model = Machine.Value; var rows = Profile.Value.Rows.ToArray(); var times = new AlsGroundedAutomaticTime[6];
        var input = new AlsGroundedRuleInput(true, false, false, AlsStance.Crouching, true, false, 1, 0);
        var state = AlsGroundedStateMachine.Update(model, default, input, times, 1, .01f, 0).State;
        var output = new AlsLocalPose[1]; var scratch = new AlsLocalPose[6]; var rest = new[] { Pose(-1) };
        var clips = Enumerable.Repeat(Pose(42), 6).ToArray();
        AlsCrouchingDirectionPose.Compose(rows, model, state, clips, rest, new[] { false }, Vector4.Zero, scratch, output);
        Assert.Equal(rest, output);
        Assert.Equal(0, AlsCrouchingDirectionPose.Curve(rows, state, new float[] { 1, 2, 3, 4, 5, 6 }, Vector4.Zero, new(10, 20, 30, 40), false));
        Assert.Equal(10, AlsCrouchingDirectionPose.Curve(rows, state, new float[6], Vector4.Zero, new(10, 20, 30, 40), true));
    }

    [Fact]
    public void ZeroElapsedTransitionRetainsNativePerEntryMinimumWeights()
    {
        var model = Machine.Value; var rows = Profile.Value.Rows.ToArray(); var times = new AlsGroundedAutomaticTime[6];
        var input = new AlsGroundedRuleInput(true, false, false, AlsStance.Crouching, true, false, 1, 0)
            { MovementDirection = AlsMovementDirection.Left, FeetCrossing = 1 };
        var state = AlsGroundedStateMachine.Update(model, default, input, times, 1, .01f, 0).State;
        state = AlsGroundedStateMachine.Update(model, state, input with { FeetCrossing = 0 }, times, 1, 0, 1).State;
        Assert.Equal(0, state.Transitions.Latest.Alpha);
        var output = new AlsLocalPose[2]; var scratch = new AlsLocalPose[12];
        AlsCrouchingDirectionPose.Compose(rows, model, state, Enumerable.Range(0, 12).Select(i => Pose(i / 2 * 100)).ToArray(),
            new[] { Pose(0), Pose(0) }, new[] { false, true }, Vector4.UnitZ, scratch, output);
        var reciprocal = 1 / (.00001f + .5f); var incoming = .00001f * reciprocal; var outgoing = .5f * reciprocal;
        Assert.Equal(200, output[0].Position.X);
        Assert.Equal(200 * outgoing + 300 * incoming, output[1].Position.X);
        Assert.True(output[1].Position.X > output[0].Position.X);
    }

    [Fact]
    public void RepeatedActiveStateAndPoseEvaluationAllocatesNothing()
    {
        var model = Machine.Value; var rows = Profile.Value.Rows.ToArray(); var times = new AlsGroundedAutomaticTime[6];
        var input = new AlsGroundedRuleInput(true, false, false, AlsStance.Crouching, true, false, 1, 0)
            { MovementDirection = AlsMovementDirection.Left, FeetCrossing = 1 };
        var state = AlsGroundedStateMachine.Update(model, default, input, times, 1, .01f, 0).State;
        input = input with { FeetCrossing = 0 };
        var rest = new[] { Pose(0) }; var clips = Enumerable.Range(0, 6).Select(i => Pose(i)).ToArray();
        var mask = new[] { true }; var scratch = new AlsLocalPose[6]; var output = new AlsLocalPose[1]; var values = new float[6];
        long allocated = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < 200; i++) Run();
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++) Run();
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
        });
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30))); Assert.Null(failure); Assert.Equal(0, allocated);
        void Run()
        {
            var candidate = AlsGroundedStateMachine.Update(model, state, input, times, 1, .01f, 1).State;
            AlsCrouchingDirectionPose.Compose(rows, model, candidate, clips, rest, mask, Vector4.UnitZ, scratch, output);
            AlsCrouchingDirectionPose.Curve(rows, candidate, values, Vector4.UnitZ, Vector4.Zero, false);
        }
    }

    [Theory]
    [InlineData("cache-owner")] [InlineData("cache-order")] [InlineData("cache-property")]
    [InlineData("cache-name")] [InlineData("weight-pin")] [InlineData("yaw")] [InlineData("modifier")]
    [InlineData("source-alias")] [InlineData("profile")] [InlineData("extra-node")]
    public void RejectsUnsupportedPoseOrCompiledCacheWiring(string mutation)
    {
        var graph = JsonNode.Parse(Read("v4_grounded_dependencies.json"))!;
        var cache = JsonNode.Parse(Read("v4_pose_cache_graph.json"))!;
        var curves = JsonNode.Parse(Read("v4_locomotion_curves.json"))!;
        var content = graph["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(".CLF_Directional States.AnimStateNode_0.Move F"))!;
        var nodes = content["nodes"]!.AsArray();
        var reader = nodes.First(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_UseCachedPose")!;
        var native = cache["compiledNodeInventory"]!.AsArray().Single(n => n!["path"]!.GetValue<string>() == content["path"]!.GetValue<string>() + "." + reader["name"]!.GetValue<string>())!;
        var modify = nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_ModifyCurve")!;
        switch (mutation)
        {
            case "cache-owner": native["cacheSourcePropertyIndex"] = -1; break;
            case "cache-order": var order = cache["orderedSavedPoseNodes"]!.AsArray().Single(n => n!["root"]!.GetValue<string>() == "(CLF) CycleBlending")!["compiledNodeIndices"]!;
                order[0] = order[1]!.DeepClone(); break;
            case "cache-property": native["propertyIndex"] = 0; break;
            case "cache-name": reader["properties"]!["NameOfCache"] = "(N) F Movement"; break;
            case "weight-pin": var blend = nodes.Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_MultiWayBlend")!;
                Pin(blend, "DesiredAlphas_0")["links"] = Pin(blend, "DesiredAlphas_1")["links"]!.DeepClone(); break;
            case "yaw": Pin(modify, "CurveValues_0")["links"]![0]!["pin"] = "Other"; break;
            case "modifier": modify["properties"]!["Node"]!["applyMode"] = "Add"; break;
            case "source-alias": var cycle = graph["graphs"]!.AsArray().Single(g => g!["path"]!.GetValue<string>().EndsWith(".(CLF) Locomotion Cycles.AnimStateNode_0.(CLF) Locomotion Cycles"))!;
                var call = cycle["nodes"]!.AsArray().Single(n => n!["class"]!.GetValue<string>() == "AnimGraphNode_LinkedAnimLayer")!;
                Pin(call, "LF")["links"] = Pin(call, "LB")["links"]!.DeepClone(); break;
            case "profile": curves["blendProfile"]!["entries"]![0]!["scale"] = 5; break;
            case "extra-node": var extra = reader.DeepClone(); extra["name"] = "Disconnected"; nodes.Add(extra); break;
        }
        Assert.Throws<FormatException>(() => Compile(graph.ToJsonString(), cache.ToJsonString(), curves.ToJsonString()));
    }

    private static AlsLocalPose Pose(float x) => new(new(x, 0, 0), Quaternion.Identity, Vector3.One);
    private static AlsCrouchingDirectionPoseProfile Compile(string? graph = null, string? cache = null, string? curves = null) =>
        AlsCrouchingDirectionPoseCompiler.Compile(graph ?? Read("v4_grounded_dependencies.json"), cache ?? Read("v4_pose_cache_graph.json"),
            curves ?? Read("v4_locomotion_curves.json"), Sources.Value, Set.Value);
    private static string Read(string name) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
    private static JsonNode Pin(JsonNode node, string name) => node["pins"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == name)!;
}
