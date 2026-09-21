using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsContactRuntimeSettingsTests
{
    private static string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name + ".json"));
    [Fact]
    public void ActualProjectSettingsAndFortyBodyBindingsAreTransported()
    {
        var json = Read("v4_physics_contact_settings"); using var doc = JsonDocument.Parse(json); var count = 0;
        foreach (var rig in doc.RootElement.GetProperty("rigs").EnumerateArray())
        {
            var definition = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs"), rig.GetProperty("mesh").GetString()!);
            var settings = AlsContactRuntimeSettingsCompiler.Compile(json, definition);
            Assert.Equal(1000, settings.MaxPushOutVelocity); Assert.Equal(1000, settings.RestitutionThreshold);
            Assert.True(settings.EnableInitialDepenetration);
            Assert.All(settings.BodyOverlapVelocities, value => Assert.Equal(-1, value)); count += settings.BodyOverlapVelocities.Length;
            var changed = JsonNode.Parse(json)!; changed["solver"]!["splitImpulse"] = true;
            Assert.Throws<InvalidDataException>(() => AlsContactRuntimeSettingsCompiler.Compile(changed.ToJsonString(), definition));
            Assert.Throws<InvalidDataException>(() => AlsContactRuntimeSettingsCompiler.Compile(json, definition with { PhysicsAsset = "/Wrong" }));
            var bodies = definition.Bodies.ToArray(); bodies[0] = bodies[0] with { Bone = "wrong" };
            Assert.Throws<InvalidDataException>(() => AlsContactRuntimeSettingsCompiler.Compile(json, definition with { Bodies = bodies }));
        }
        Assert.Equal(40, count);
    }
    [Fact]
    public void NativeConstraintSetupAndActivationResolveAllFortyEightPairs()
    {
        using var doc = JsonDocument.Parse(Read("v4_physics_contact_settings")); var rows = doc.RootElement.GetProperty("cases");
        Assert.Equal(48, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            var value = AlsInitialOverlapSettings.Resolve(row.GetProperty("body0").GetSingle(), row.GetProperty("body1").GetSingle());
            Assert.Equal(row.GetProperty("setupVelocity").GetSingle(), value);
            Assert.Equal(row.GetProperty("activatedVelocity").GetSingle(), value);
            Assert.Equal(row.GetProperty("mode").GetInt32() == 0, row.GetProperty("perContact").GetBoolean());
        }
    }
}
