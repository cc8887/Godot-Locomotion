using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCharacterMovementRuntimeTests
{
    internal static string Read(string file) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets/config", file));
    internal static AlsCharacterMovementRuntime Compile(string? source = null) => AlsCharacterMovementCompiler.CompileRuntime(
        source ?? Read("v4_character_movement_runtime.json"), AlsCharacterMovementCompiler.Compile(
            Read("v4_character_movement_inputs.json"), Read("v4_character_rotation_inputs.json")));

    [Fact]
    public void GaitEligibilityMatchesAllNativeBlueprintBoundaries()
    {
        var runtime = Compile();
        using var document = JsonDocument.Parse(Read("v4_character_movement_runtime.json"));
        var cases = document.RootElement.GetProperty("gaitVerification"); Assert.Equal(1260, cases.GetArrayLength());
        foreach (var row in cases.EnumerateArray())
        {
            var mode = (AlsRotationMode)row.GetProperty("mode").GetInt32(); var amount = row.GetProperty("amount").GetDouble();
            var hasInput = row.GetProperty("hasInput").GetBoolean();
            var yaw = row.GetProperty("inputYaw").GetDouble() - row.GetProperty("controlYaw").GetDouble();
            Assert.True(row.GetProperty("canSprint").GetBoolean() == runtime.CanSprint(mode, amount, hasInput, yaw), row.ToString());
            Assert.Equal((AlsGait)row.GetProperty("allowed").GetInt32(), runtime.AllowedGait((AlsGait)row.GetProperty("desired").GetInt32(),
                (AlsStance)row.GetProperty("stance").GetInt32(), mode, amount, hasInput, yaw));
        }
        Assert.False(runtime.CanSprint(AlsRotationMode.VelocityDirection, (double).9f, true, 0));
        Assert.True(runtime.CanSprint(AlsRotationMode.VelocityDirection, .9, true, 0));
    }

    [Fact]
    public void ContinuousVelocityAndNextSettingsMatchNativeCmcThenCharacter()
    {
        var runtime = Compile();
        using var document = JsonDocument.Parse(Read("v4_character_movement_runtime.json"));
        var frames = 0;
        foreach (var trace in document.RootElement.GetProperty("traces").EnumerateArray())
        {
            var hz = trace.GetProperty("hz").GetInt32(); var state = runtime.InitialState; var velocity = AlsDoubleVector.Zero;
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var label = $"hz={hz} frame={row.GetProperty("frame")}";
                var stance = (AlsStance)row.GetProperty("stance").GetInt32(); var amount = row.GetProperty("amount").GetDouble();
                var angle = row.GetProperty("angle").GetDouble(); var radians = angle * (System.Math.PI / 180);
                var input = new AlsDoubleVector(System.Math.Cos(radians) * amount, System.Math.Sin(radians) * amount, 0);
                var step = runtime.Integrate(state, velocity, input, stance, true, 1f / hz);
                var expected = V(row.GetProperty("velocity"));
                Assert.True((step.Velocity - expected).LengthSquared < .0001, label + " velocity " + (step.Velocity - expected));
                var next = runtime.UpdateCharacter(state, System.Math.Sqrt(step.Velocity.LengthSquared), step, stance,
                    (AlsRotationMode)row.GetProperty("mode").GetInt32(), (AlsGait)row.GetProperty("desired").GetInt32(), angle, true, false, 1f / hz);
                Assert.Equal((AlsGait)row.GetProperty("allowed").GetInt32(), next.AllowedGait);
                var after = row.GetProperty("after");
                // Independently check parameter evaluation at the same native speed;
                // steep curves amplify the small closed-loop velocity difference.
                // The real continuous history below still uses our own velocity.
                var paired = runtime.UpdateCharacter(state, System.Math.Sqrt(expected.LengthSquared), step, stance,
                    (AlsRotationMode)row.GetProperty("mode").GetInt32(), (AlsGait)row.GetProperty("desired").GetInt32(), angle, true, false, 1f / hz);
                Assert.True(System.Math.Abs(paired.Values.MaxAcceleration - after.GetProperty("max_acceleration").GetSingle()) <= .001, label + " paired acceleration");
                Assert.True(System.Math.Abs(paired.Values.BrakingDeceleration - after.GetProperty("braking_deceleration_walking").GetSingle()) <= .001, label + " paired braking");
                Assert.True(System.Math.Abs(paired.Values.GroundFriction - after.GetProperty("ground_friction").GetSingle()) <= .00001, label + " paired friction");
                Assert.Equal(after.GetProperty("max_walk_speed").GetSingle(), next.Values.MaxWalkSpeed);
                Assert.Equal(after.GetProperty("max_walk_speed_crouched").GetSingle(), next.Values.MaxCrouchedSpeed);
                state = next; velocity = step.Velocity; frames++;
            }
        }
        Assert.Equal(840, frames);
    }

    [Theory]
    [InlineData("order")] [InlineData("delay")] [InlineData("input")] [InlineData("angle")]
    public void RejectsChangedExecutionSemantics(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_character_movement_runtime.json"))!;
        if (mutation == "order") root["settings"]!["tick_before_owner"] = false;
        else
        {
            var name = mutation == "delay" ? "EventGraph" : "CanSprint";
            var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == name)!;
            graph["nativeText"] = graph["nativeText"]!.GetValue<string>().Replace(mutation == "delay" ? "RetriggerableDelay" : mutation == "input" ?
                "Greater_DoubleDouble" : "Less_DoubleDouble", "ChangedFunction", StringComparison.Ordinal);
        }
        Assert.ThrowsAny<Exception>(() => Compile(root.ToJsonString()));
    }
    private static AlsDoubleVector V(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());

    [Fact]
    public void LandingReadsCachedInputAndRetriggersReset()
    {
        var runtime = Compile(); var initial = runtime.InitialState;
        var moving = runtime.Integrate(initial, default, new(1, 0, 0), AlsStance.Standing, true, .2f);
        var landed = runtime.UpdateCharacter(initial, 0, moving, AlsStance.Standing, AlsRotationMode.LookingDirection,
            AlsGait.Running, 0, true, true, .2f);
        Assert.Equal(3, landed.BrakingFrictionFactor); // Cached HasMovementInput was false.
        Assert.Equal(.3f, landed.LandResetRemaining);
        var idle = runtime.Integrate(landed, default, default, AlsStance.Standing, true, .1f);
        var repeated = runtime.UpdateCharacter(landed, 0, idle, AlsStance.Standing, AlsRotationMode.LookingDirection,
            AlsGait.Running, 0, true, true, .1f);
        Assert.Equal(.5f, repeated.BrakingFrictionFactor); // Cached input was true despite this frame being idle.
        Assert.Equal(.4f, repeated.LandResetRemaining);
        var expired = runtime.UpdateCharacter(repeated, 0, idle, AlsStance.Standing, AlsRotationMode.LookingDirection,
            AlsGait.Running, 0, true, false, .4f);
        Assert.Equal(0, expired.BrakingFrictionFactor); Assert.Equal(0, expired.LandResetRemaining);
    }

    [Fact]
    public void ContinuousRuntimeUpdatesAllocateNoManagedMemory()
    {
        var runtime = Compile(); var state = runtime.InitialState; var velocity = AlsDoubleVector.Zero;
        for (var i = 0; i < 1000; i++) Advance(i);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Advance(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        Assert.True(velocity.IsFinite);
        void Advance(int frame)
        {
            var step = runtime.Integrate(state, velocity, new(frame % 120 < 60 ? 1 : -1, 0, 0), AlsStance.Standing, true, 1f / 60);
            velocity = step.Velocity;
            state = runtime.UpdateCharacter(state, System.Math.Sqrt(velocity.LengthSquared), step, AlsStance.Standing,
                AlsRotationMode.LookingDirection, AlsGait.Sprinting, frame % 120 < 60 ? 0 : 180, true, frame % 50 == 0, 1f / 60);
        }
    }
}
