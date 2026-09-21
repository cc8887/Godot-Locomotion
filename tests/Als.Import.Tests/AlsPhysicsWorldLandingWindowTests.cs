using System.Text.Json;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsWorldLandingWindowTests
{
    [Fact]
    public void InternalWorldContactsUseMarshalledOverlapSettings()
    {
        string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name));
        using var doc = JsonDocument.Parse(Read("v4_physics_world_landing_window.json"));
        var root = doc.RootElement;
        Assert.Equal(11, root.GetProperty("windowStartFrame").GetInt32());
        Assert.Equal(10, root.GetProperty("windowFrames").GetInt32());
        Assert.Equal(2, root.GetProperty("cases").GetArrayLength());
        var contactsChecked = 0; var rebuiltFoot = false;
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            var mesh = row.GetProperty("setup").GetProperty("mesh").GetString()!;
            var definition = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs.json"), mesh);
            var settings = AlsContactRuntimeSettingsCompiler.Compile(Read("v4_physics_contact_settings.json"), definition);
            var bodies = definition.Bodies.ToDictionary(b => b.Bone, b => b.Index);
            var samples = row.GetProperty("samples"); Assert.Equal(11, samples.GetArrayLength());
            for (var offset = 1; offset <= 10; offset++)
            {
                var sample = samples[offset]; Assert.Equal(11 + offset, sample.GetProperty("frame").GetInt32());
                Assert.Equal(0, sample.GetProperty("collisionSolverType").GetInt32());
                foreach (var contact in sample.GetProperty("contactsAfterSolve").EnumerateArray())
                {
                    float Velocity(string bone) => bodies.TryGetValue(bone, out var index) ? settings.BodyOverlapVelocities[index] : 0;
                    var bone0 = contact.GetProperty("body0").GetString()!; var bone1 = contact.GetProperty("body1").GetString()!;
                    Assert.Equal(AlsInitialOverlapSettings.Resolve(Velocity(bone0), Velocity(bone1)),
                        contact.GetProperty("depenetrationVelocity").GetSingle());
                    for (var endpoint = 0; endpoint < 2; endpoint++)
                    {
                        var bone = contact.GetProperty("body" + endpoint).GetString()!;
                        var count = bodies.TryGetValue(bone, out var index) ? definition.Bodies[index].Shapes.Length : 1;
                        Assert.InRange(contact.GetProperty("shape" + endpoint).GetInt32(), 0, count - 1);
                    }
                    contactsChecked++;
                    if (mesh.EndsWith("AnimMan", StringComparison.Ordinal) && offset == 4 && bone0 == "foot_l" && bone1 == "environment_0")
                    {
                        rebuiltFoot = true; Assert.False(contact.GetProperty("restored").GetBoolean());
                        var points = contact.GetProperty("points"); Assert.Equal(3, points.GetArrayLength());
                        Assert.True(points[0].GetProperty("initialContact").GetBoolean());
                        Assert.False(points[1].GetProperty("initialContact").GetBoolean());
                        Assert.True(points[2].GetProperty("initialContact").GetBoolean());
                        foreach (var point in points.EnumerateArray()) Assert.Equal(0, point.GetProperty("initialPhi").GetSingle());
                    }
                }
            }
        }
        Assert.True(rebuiltFoot); Assert.True(contactsChecked > 100);
    }
}
