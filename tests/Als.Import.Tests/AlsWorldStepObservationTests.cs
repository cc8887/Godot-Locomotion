using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsWorldStepObservationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void ActualWorldIntegrationMatchesPredictionFromItsIndependentInputs()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_world_step_window.json")));
        var authored = File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/v4_physics_asset_inputs.json"));
        var checkedBodies = 0; double maxP = 0, maxQ = 0; float maxV = 0, maxW = 0;
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var setup = row.GetProperty("setup"); var definition = AlsPhysicsAssetCompiler.Compile(authored, setup.GetProperty("mesh").GetString()!);
            foreach (var sample in row.GetProperty("samples").EnumerateArray().Skip(1))
            {
                var steps = sample.GetProperty("stepObservations"); Assert.Equal(3, steps.GetArrayLength());
                Assert.Equal("preIntegrate", steps[0].GetProperty("stage").GetString());
                Assert.Equal("postIntegrate", steps[1].GetProperty("stage").GetString());
                Assert.Equal("preSolve", steps[2].GetProperty("stage").GetString());
                foreach (var step in steps.EnumerateArray()) Assert.Equal(D(row, "dtUsed"), D(step, "dt"));
                var before = steps[0].GetProperty("bodies"); var after = steps[1].GetProperty("bodies");
                var solve = steps[2].GetProperty("bodies"); Assert.Equal(definition.Bodies.Length, before.GetArrayLength());
                for (var i = 0; i < before.GetArrayLength(); i++)
                {
                    var a = before[i]; var b = after[i]; var asset = definition.Bodies[i];
                    Assert.Equal(asset.Bone, a.GetProperty("name").GetString());
                    Assert.Equal(asset.MassLocal, Pose(a.GetProperty("massLocal")));
                    if (!B(setup.GetProperty("bodies")[i], "dynamic")) continue;
                    Assert.Equal(4, a.GetProperty("objectState").GetInt32());
                    var acceleration = B(setup.GetProperty("bodies")[i], "gravity") ? V(setup, "gravity") : default;
                    Assert.Equal(acceleration, V(b, "acceleration"));
                    Assert.Equal(AlsDoubleVector.Zero, V(a, "angularAcceleration"));
                    Assert.False(B(asset.Defaults, "bGyroscopicTorqueEnabled"));
                    Assert.Equal(D(asset.Defaults, "linearDamping"), D(a, "linearDamping"));
                    Assert.Equal(D(asset.Defaults, "angularDamping"), D(a, "angularDamping"));
                    var predicted = AlsRigidBodyIntegration.Predict(Pose(a.GetProperty("initialActor")), asset.MassLocal,
                        new(V(a, "v").ToSingle(), V(a, "w").ToSingle()), D(a, "linearDamping"), D(a, "angularDamping"), D(row, "dtUsed"),
                        new(acceleration, V(a, "angularAcceleration"), V(a, "linearImpulseVelocity"), V(a, "angularImpulseVelocity")));
                    var expected = Pose(b.GetProperty("predictedCom"));
                    maxP = Math.Max(maxP, Math.Sqrt((predicted.MassPose.Position - expected.Position).LengthSquared));
                    var q = predicted.MassPose.Rotation + -expected.Rotation;
                    maxQ = Math.Max(maxQ, Math.Max(Math.Max(Math.Abs(q.X), Math.Abs(q.Y)), Math.Max(Math.Abs(q.Z), Math.Abs(q.W))));
                    maxV = Math.Max(maxV, Vector3.Distance(predicted.Velocity.Linear, V(b, "v").ToSingle()));
                    maxW = Math.Max(maxW, Vector3.Distance(predicted.Velocity.Angular, V(b, "w").ToSingle()));
                    // Native updates inertia conditioning between these hooks;
                    // only integration state is expected to remain unchanged.
                    foreach (var field in new[] { "initialActor", "predictedActor", "initialCom", "predictedCom", "v", "w" })
                        Assert.Equal(b.GetProperty(field).GetRawText(), solve[i].GetProperty(field).GetRawText());
                    checkedBodies++;
                }
            }
        }
        output.WriteLine($"NATIVE_WORLD_INTEGRATION bodies={checkedBodies} max_p={maxP:R} max_q={maxQ:R} max_v={maxV:R} max_w={maxW:R}");
        Assert.Equal(114, checkedBodies); Assert.InRange(maxP, 0, 1e-11); Assert.InRange(maxQ, 0, 1e-12);
        Assert.Equal(0, maxV); Assert.Equal(0, maxW);
    }
}
