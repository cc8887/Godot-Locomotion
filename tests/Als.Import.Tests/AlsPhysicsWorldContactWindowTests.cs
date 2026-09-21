using System.Text.Json;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsWorldContactWindowTests
{
    [Fact]
    public void NativeContactWindowBindsFiltersAndNeverCollidesIgnoringRoot()
    {
        string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name));
        using var document = JsonDocument.Parse(Read("v4_physics_world_contact_window.json"));
        using var filtersDocument = JsonDocument.Parse(Read("v4_physics_simulation_filters.json"));
        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("windowFrames").GetInt32());
        Assert.Equal(filtersDocument.RootElement.GetProperty("sourceSha256").GetString(), root.GetProperty("sourceSha256").GetString());
        Assert.Equal(2, root.GetProperty("cases").GetArrayLength());
        foreach (var row in root.GetProperty("cases").EnumerateArray())
        {
            var mesh = row.GetProperty("setup").GetProperty("mesh").GetString()!;
            var runtimeJson = Read("v4_physics_runtime_shapes.json"); var authoredJson = Read("v4_physics_asset_inputs.json");
            var runtime = AlsRuntimeShapeCompiler.Compile(runtimeJson, authoredJson, mesh);
            var filters = AlsSimulationFilterCompiler.Compile(Read("v4_physics_simulation_filters.json"), runtimeJson, authoredJson, mesh);
            var samples = row.GetProperty("samples"); Assert.Equal(4, samples.GetArrayLength());
            Assert.False(row.TryGetProperty("oneSecondSleepBudget", out _));
            for (var frame = 1; frame <= 3; frame++)
            {
                var sample = samples[frame]; Assert.Equal(frame, sample.GetProperty("frame").GetInt32());
                foreach (var shape in runtime)
                {
                    var native = sample.GetProperty("bodies")[shape.Body].GetProperty("shapeFilters")[shape.NativeIndex];
                    var compiled = filters[(shape.Body, shape.Shape)];
                    Assert.Equal(native.GetProperty("simulation").GetBoolean(), compiled.Simulation);
                    Assert.Equal(Convert.ToUInt64(native.GetProperty("blockChannels").GetString(), 16), compiled.Filter.BlockChannels);
                    Assert.Equal(1UL << native.GetProperty("channel").GetInt32(), compiled.Filter.Channel);
                }
                var contacts = sample.GetProperty("contactsAfterSolve"); Assert.NotEmpty(contacts.EnumerateArray());
                foreach (var contact in contacts.EnumerateArray())
                {
                    Assert.NotEqual("root", contact.GetProperty("body0").GetString());
                    Assert.NotEqual("root", contact.GetProperty("body1").GetString());
                }
            }
        }
    }
}
