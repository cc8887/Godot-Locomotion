using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsFootReferenceBindingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("v4_movement_source_inputs.json")]
    [InlineData("v4_overlay_source_inputs.json")]
    public void ProductionReferenceAndLegBindingMatchNativeMeshAndSkeleton(string indexName)
    {
        var root = RepositoryRoot.Find();
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var index = File.ReadAllText(Path.Combine(root, "assets/config", indexName));
        using var document = JsonDocument.Parse(index);
        var request = document.RootElement.GetProperty("request");
        var roots = request.GetProperty("rootAssets").EnumerateArray().Select(row =>
            Array.FindIndex(set.Animations, a => a.StableId == row.GetProperty("assetId").GetString())).ToArray();
        var bank = AlsRawAnimationSourceCompiler.Compile(index, set, request.GetProperty("bindingDigest").GetString()!,
            request.GetProperty("players").GetInt32(), request.GetProperty("samples").GetInt32(), roots,
            file => File.ReadAllBytes(Path.Combine(root, "assets/config", file)));
        var skeleton = Assert.Single(bank.Skeletons.ToArray());
        var components = new AlsPrecisePose[skeleton.LogicalBoneCount];
        AlsRefactoredFootAnimationFrame.ToNativeComponents(skeleton.PreciseReferencePose, skeleton.LogicalParents, components);
        var names = skeleton.LogicalBoneNames.ToArray();
        var binding = AlsFootRigCompiler.Compile(AlsFootRigCompilerTests.Source()).Bind(names,
            skeleton.LogicalParents, components, true);
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "tests/Als.Import.Tests/Fixtures/Refactored/foot_reference_binding.json")));
        Assert.Equal(1, native.RootElement.GetProperty("schemaVersion").GetInt32());
        var cases = native.RootElement.GetProperty("cases");
        Assert.Equal(2, cases.GetArrayLength());
        double maximum = 0;
        foreach (var sample in cases.EnumerateArray())
        {
            var nativePositions = new Dictionary<string, AlsDoubleVector>();
            foreach (var bone in sample.GetProperty("bones").EnumerateArray())
            {
                var name = bone.GetProperty("name").GetString()!;
                var position = Vector(bone.GetProperty("initial_global").GetProperty("position"));
                nativePositions.Add(name, position);
                var id = Array.FindIndex(names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                Assert.True(id >= 0, "Missing native reference bone: " + name);
                var error = Math.Sqrt((components[id].Position - position).LengthSquared);
                maximum = Math.Max(maximum, error);
                Assert.InRange(error, 0, 1e-4); // cm; at most one micrometer
            }
            Assert.Equal(7, nativePositions.Count);
            var thigh = nativePositions["thigh_l"]; var calf = nativePositions["calf_l"]; var foot = nativePositions["foot_l"];
            var length = (float)Math.Sqrt((calf - thigh).LengthSquared);
            length = (float)(length + Math.Sqrt((foot - calf).LengthSquared));
            Assert.Equal(length, binding.LegLength);
            Assert.InRange(Math.Abs((float)foot.Z - binding.FootHeight), 0, 1e-4);
        }
        output.WriteLine($"NATIVE_REFERENCE_BINDING_OK bank={indexName} cases=2 bones=7 max_position_cm={maximum:R} leg_length_cm={binding.LegLength:R} foot_height_cm={binding.FootHeight:R}");
    }

    private static AlsDoubleVector Vector(JsonElement values) => new(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble());
}
