using System.Text.Json;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using GodotAls.Import.Runtime;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsCameraGraphNativeTests(ITestOutputHelper output)
{
    [Fact]
    public void ContinuousGraphMatchesIndependentNativeEvaluation()
    {
        var directory = Path.Combine(RepositoryRoot.Find(), "assets", "config");
        var graph = AlsCameraGraphCompiler.Compile(File.ReadAllText(Path.Combine(directory, "refactored_camera_inputs.json")));
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "refactored_camera_graph_reference.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("/ALS/ALSCamera/AB_Als_Camera.AB_Als_Camera", root.GetProperty("source").GetString());
        Assert.Equal(7, root.GetProperty("traces").GetArrayLength());
        var frames = 0; var values = 0; var maximum = 0f; var failures = new List<string>();
        foreach (var trace in root.GetProperty("traces").EnumerateArray())
        {
            var runtime = new AlsCameraGraphRuntime(graph);
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var input = row.GetProperty("input");
                var frame = row.GetProperty("serial").GetInt32();
                var actual = runtime.Prepare(frame, input.GetProperty("delta").GetSingle(), new AlsCameraGraphInput(
                    Tag("RotationMode"), Tag("Stance"), Tag("Gait"), Tag("ViewMode"), Tag("LocomotionAction"),
                    input.GetProperty("RightShoulder").GetBoolean()));
                var expected = row.GetProperty("curves").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle());
                Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
                foreach (var pair in expected)
                {
                    var difference = MathF.Abs(actual[pair.Key] - pair.Value);
                    maximum = MathF.Max(maximum, difference); values++;
                    // Existing T3D rich keys have six decimal digits; budget
                    // 1e-3 UE cm for offsets, 3e-6 for dimensionless/lag curves.
                    var tolerance = pair.Key.Contains("Offset", StringComparison.Ordinal) ? .001f : .000003f;
                    if (difference > tolerance && failures.Count < 20)
                        failures.Add($"{trace.GetProperty("name").GetString()}:{frame} {pair.Key} native={pair.Value:R} actual={actual[pair.Key]:R} difference={difference:R}");
                }
                runtime.Commit(); frames++;
                string Tag(string name) => input.GetProperty(name).GetString()!;
            }
        }
        output.WriteLine($"Frames={frames} values={values} maxAbsolute={maximum:R}");
        Assert.Equal(3114, frames);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
