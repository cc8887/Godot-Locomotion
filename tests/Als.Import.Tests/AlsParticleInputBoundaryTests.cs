using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsParticleInputBoundaryTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void CreationAndResetMatchIndependentlyStoredNativeWorldInputs()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository(
            "assets/config/v4_physics_world_contact_window.json")));
        var checkedBodies = 0; var roundedRotations = 0;
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var inputs = row.GetProperty("setup").GetProperty("bodies").EnumerateArray().ToArray();
            var native = row.GetProperty("samples")[0].GetProperty("bodies");
            var states = inputs.Select(b => new AlsIslandBodyState(Pose(b.GetProperty("actor")),
                new(V(b, "v").ToSingle(), V(b, "w").ToSingle()))).ToArray();
            var bodies = inputs.Select(b => new AlsIslandBody(AlsPrecisePose.Identity,
                B(b, "dynamic") ? new(1, AlsDoubleVector.One) : default)).ToArray();
            var island = new AlsJointIsland(bodies, [], states);
            Check();
            island.StepForceFree(1d / 60); island.Reset(states); Check();
            void Check()
            {
                for (var i = 0; i < states.Length; i++)
                {
                    var expected = native[i]; var stored = island.BodyAt(i);
                    Assert.Equal(inputs[i].GetProperty("name").GetString(), expected.GetProperty("name").GetString());
                    Assert.Equal(Pose(expected.GetProperty("world")), stored.Actor);
                    Assert.Equal(V(expected, "linearVelocity").ToSingle(), stored.Velocity.Linear);
                    Assert.Equal(V(expected, "angularVelocity").ToSingle(), stored.Velocity.Angular);
                    Assert.Equal(states[i].Actor.Position, stored.Actor.Position);
                    if (states[i].Actor.Rotation != stored.Actor.Rotation) roundedRotations++;
                    checkedBodies++;
                }
            }
        }
        Assert.Equal(80, checkedBodies); Assert.True(roundedRotations > 40);
        output.WriteLine($"NATIVE_PARTICLE_INPUT storedBodies={checkedBodies} roundedRotations={roundedRotations}");
    }
}
