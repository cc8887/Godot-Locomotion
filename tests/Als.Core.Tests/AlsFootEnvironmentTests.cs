using System.Text.Json;
using GodotAls.Core.Locomotion;
using Xunit.Abstractions;
using M = System.Math;

namespace GodotAls.Core.Tests;

public sealed class AlsFootEnvironmentTests(ITestOutputHelper output)
{
    private static JsonDocument Read() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "FootIk", "native_foot_environment.json")));

    [Fact]
    public void ActualSpringNodeMatchesInitializationForcesAndPelvisDoubleWeighting()
    {
        using var document = Read();
        double resultError = 0, velocityError = 0, graphError = 0;
        var frames = 0; var resets = 0;
        foreach (var sample in document.RootElement.GetProperty("springs").EnumerateArray())
        {
            var state = default(AlsRigSpringState); var graphState = default(AlsRigSpringState);
            var mode = sample.GetProperty("mode").GetInt32();
            foreach (var row in sample.GetProperty("frames").EnumerateArray())
            {
                if (B(row, "reset")) { state = graphState = default; resets++; }
                var input = new AlsRigSpringInput(row.GetProperty("dt").GetDouble(), F(row, "target"), F(row, "current"),
                    F(row, "strength"), F(row, "force"), F(row, "damping"), F(row, "targetVelocity"), B(row, "useCurrent"), B(row, "initializeFromTarget"));
                var next = AlsRigSpringModel.Evaluate(state, input);
                Assert.Equal(next, AlsRigSpringModel.Evaluate(state, input));
                state = next;
                resultError = M.Max(resultError, M.Abs(state.Result - F(row, "result")));
                velocityError = M.Max(velocityError, M.Abs(state.Velocity - F(row, "velocity")));
                Assert.Equal(F(row, "previousTarget"), state.PreviousTarget);
                Assert.Equal(B(row, "previousValid"), state.PreviousValid);
                if (mode == 0)
                {
                    var result = AlsPelvisRigModel.Evaluate(graphState, new(-30, 40, 2, 1), input.DeltaTime,
                        F(row, "left"), F(row, "right"), F(row, "amount"), new(2, 3, 100));
                    graphState = result.Spring;
                    graphError = M.Max(graphError, M.Abs(result.Offset - F(row, "pelvisOffset")));
                    Assert.Equal(2, result.Location.X); Assert.Equal(3, result.Location.Y);
                    Assert.Equal(100 + (double)result.Offset, result.Location.Z);
                }
                frames++;
            }
        }
        output.WriteLine($"springFrames={frames} retries={frames} resets={resets} resultError={resultError:R} velocityError={velocityError:R} pelvisError={graphError:R}");
        Assert.Equal(1440, frames); Assert.Equal(12, resets);
        Assert.InRange(resultError, 0, .0002); Assert.InRange(velocityError, 0, .003); Assert.InRange(graphError, 0, .0002);
    }

    [Fact]
    public void ActualFootTraceMatchesCollisionObservationsAndComponentSpaceSlopeCorrection()
    {
        using var document = Read();
        double rayError = 0, offsetError = 0, normalError = 0;
        var rows = 0; var blocking = 0; var accepted = 0; var rejected = 0;
        foreach (var row in document.RootElement.GetProperty("traces").EnumerateArray())
        {
            var d = new AlsFootTraceRigDefinition(50, 80, 13.5f, F(row, "walkableAngle"));
            var space = P(row.GetProperty("toWorld"));
            var target = V(row.GetProperty("target"));
            var hit = new AlsFootTraceRigHit(B(row, "blocking"), V(row.GetProperty("impact")), V(row.GetProperty("normal")));
            var ray = AlsFootTraceRigModel.Trace(d, target, space);
            // Foot Z does not move the ray vertically: the native trace is
            // anchored to VM Z=+50/-80, even when target Z is animated.
            Assert.Equal(ray, AlsFootTraceRigModel.Trace(d, target with { Z = -700 }, space));
            var result = AlsFootTraceRigModel.Evaluate(d, B(row, "enabled"), hit, space);
            rayError = M.Max(rayError, M.Max(Distance(ray.Start, V(row.GetProperty("start"))), Distance(ray.End, V(row.GetProperty("end")))));
            offsetError = M.Max(offsetError, M.Abs(result.OffsetZ - F(row, "offsetZ")));
            normalError = M.Max(normalError, Distance(result.OffsetNormal, V(row.GetProperty("offsetNormal"))));
            if (hit.Blocking) blocking++;
            if (result.Walkable) accepted++; else rejected++;
            rows++;
        }
        output.WriteLine($"traces={rows} blocking={blocking} walkable={accepted} rejected={rejected} rayErrorCm={rayError:R} offsetErrorCm={offsetError:R} normalError={normalError:R}");
        Assert.Equal(48, rows); Assert.True(blocking >= 24); Assert.True(accepted >= 8); Assert.True(rejected >= 8);
        Assert.InRange(rayError, 0, 1e-10); Assert.InRange(offsetError, 0, .00001); Assert.InRange(normalError, 0, 1e-12);
    }

    [Fact]
    public void ZeroDeltaColdSpringInitializesWithoutMakingPreviousTargetValid()
    {
        var input = new AlsRigSpringInput(0, 15, -5, 2, 0, 1, 0, false, true);
        var first = AlsRigSpringModel.Evaluate(default, input);
        Assert.Equal(15, first.Result); Assert.False(first.PreviousValid);
        var second = AlsRigSpringModel.Evaluate(first, input with { Target = 30 });
        Assert.Equal(30, second.Result); Assert.False(second.PreviousValid);
        var external = AlsRigSpringModel.Evaluate(default, input with { UseCurrent = true, InitializeFromTarget = false });
        Assert.Equal(-5, external.Result);
    }

    private static float F(JsonElement e, string name) => e.GetProperty(name).GetSingle();
    private static bool B(JsonElement e, string name) => e.GetProperty(name).GetBoolean();
    private static AlsDoubleVector V(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
    private static AlsPrecisePose P(JsonElement p)
    { var q = p.GetProperty("q"); return new(V(p.GetProperty("p")), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()), V(p.GetProperty("s"))); }
    private static double Distance(AlsDoubleVector a, AlsDoubleVector b) => M.Sqrt((a - b).LengthSquared);
}
