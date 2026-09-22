using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsWorldJointObservationTests
{
    private static JsonDocument Read(string name) => JsonDocument.Parse(File.ReadAllText(
        AlsFootRigCompilerTests.PathInRepository("assets/config/" + name + ".json")));

    [Fact]
    public void CapturedSolverInputsMatchIndependentWorldJointOrderAndComFrames()
    {
        using var world = Read("v4_physics_world_joint_window");
        using var coupled = Read("v4_physics_first_steps_coupled_reference");
        var checkedJoints = 0;
        foreach (var fixture in coupled.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = fixture.GetProperty("capture").GetProperty("input");
            var row = world.RootElement.GetProperty("cases").EnumerateArray().Single(c =>
                c.GetProperty("setup").GetProperty("mesh").GetString() == input.GetProperty("mesh").GetString());
            Assert.Equal(D(row, "dtUsed"), D(input, "dt"));
            var sample = row.GetProperty("samples")[input.GetProperty("frame").GetInt32() + 1];
            var steps = sample.GetProperty("stepObservations"); Assert.Equal(4, steps.GetArrayLength());
            Assert.Equal("postSolve", steps[3].GetProperty("stage").GetString());
            var pre = steps.EnumerateArray().Single(s => s.GetProperty("stage").GetString() == "preSolve");
            EqualJson(pre.GetProperty("jointSolverSettings"), input.GetProperty("solverSettings"));
            var native = pre.GetProperty("joints").EnumerateArray().Where(j => B(j, "inGraph") && !B(j, "graphSleeping"))
                .OrderBy(j => j.GetProperty("graphOrder").GetInt32()).ToArray();
            var captured = input.GetProperty("joints"); Assert.Equal(native.Length, captured.GetArrayLength());
            var bodies = pre.GetProperty("bodies");
            for (var i = 0; i < native.Length; i++)
            {
                var a = native[i]; var b = captured[i];
                CheckEnd("parent"); CheckEnd("child");
                // Chaos Sanitize clears limits on axes that are not Limited.
                // Compare every setting after only this documented normalization;
                // neither allowlists arbitrary errors nor rewrites source fixtures.
                EqualJson(a.GetProperty("settings"), SanitizeUnusedLimits(b.GetProperty("jointSettings")));
                checkedJoints++;
                void CheckEnd(string end)
                {
                    var body = input.GetProperty("bodies")[b.GetProperty(end).GetInt32()];
                    Assert.Equal(a.GetProperty(end).GetString(), body.GetProperty("name").GetString());
                    var state = bodies.EnumerateArray().Single(s => s.GetProperty("name").GetString() == body.GetProperty("name").GetString());
                    var actor = Pose(a.GetProperty(end + "ActorFrame"));
                    var calculated = state.GetProperty("objectState").GetInt32() == 4
                        ? AlsPrecisePose.Relative(actor, Pose(state.GetProperty("massLocal"))) : actor;
                    Assert.Equal(Pose(a.GetProperty(end + "ComFrame")), calculated);
                    Assert.Equal(calculated, Pose(b.GetProperty(end + "Frame")));
                }
            }
        }
        Assert.Equal(114, checkedJoints);
    }

    [Fact]
    public void FirstStepNativeContainerReplayReproducesIndependentWorldPostSolveVelocitiesExactly()
    {
        using var world = Read("v4_physics_world_joint_window");
        using var coupled = Read("v4_physics_first_steps_coupled_reference");
        var checkedBodies = 0;
        foreach (var fixture in coupled.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = fixture.GetProperty("capture").GetProperty("input");
            if (input.GetProperty("frame").GetInt32() != 0) continue;
            var row = world.RootElement.GetProperty("cases").EnumerateArray().Single(c =>
                c.GetProperty("setup").GetProperty("mesh").GetString() == input.GetProperty("mesh").GetString());
            var states = row.GetProperty("samples")[1].GetProperty("stepObservations")[3].GetProperty("bodies");
            var samples = fixture.GetProperty("nativeSamples");
            var replay = samples[samples.GetArrayLength() - 1].GetProperty("bodies");
            for (var i = 0; i < states.GetArrayLength(); i++)
            {
                Assert.Equal(states[i].GetProperty("name").GetString(), input.GetProperty("bodies")[i].GetProperty("name").GetString());
                Assert.Equal(SingleVector(states[i], "v"), SingleVector(replay[i], "v"));
                Assert.Equal(SingleVector(states[i], "w"), SingleVector(replay[i], "w"));
                checkedBodies++;
            }
        }
        Assert.Equal(40, checkedBodies);
    }

    private static Vector3 SingleVector(JsonElement e, string name)
    { var v = e.GetProperty(name); return new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()); }

    private static JsonElement SanitizeUnusedLimits(JsonElement settings)
    {
        var node = JsonNode.Parse(settings.GetRawText())!;
        if (settings.GetProperty("LinearMotionTypes").EnumerateArray().All(x => x.GetInt32() != 1)) node["LinearLimit"] = 0;
        for (var i = 0; i < 3; i++)
            if (settings.GetProperty("AngularMotionTypes")[i].GetInt32() != 1) node["AngularLimits"]![i] = 0;
        return JsonSerializer.SerializeToElement(node);
    }

    private static void EqualJson(JsonElement a, JsonElement b)
    {
        Assert.Equal(a.ValueKind, b.ValueKind);
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                Assert.Equal(a.EnumerateObject().Select(p => p.Name).Order(), b.EnumerateObject().Select(p => p.Name).Order());
                foreach (var p in a.EnumerateObject()) EqualJson(p.Value, b.GetProperty(p.Name));
                break;
            case JsonValueKind.Array:
                Assert.Equal(a.GetArrayLength(), b.GetArrayLength());
                for (var i = 0; i < a.GetArrayLength(); i++) EqualJson(a[i], b[i]);
                break;
            case JsonValueKind.Number: Assert.Equal(a.GetDouble(), b.GetDouble()); break;
            default: Assert.Equal(a.GetRawText(), b.GetRawText()); break;
        }
    }
}
