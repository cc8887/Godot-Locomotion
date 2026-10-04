using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Animation;

namespace GodotAls.Import.Tests;

public sealed class AlsAnimationLayerContractTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(directory.FullName, "GodotALS.csproj")))
                directory = directory.Parent ?? throw new InvalidOperationException("Missing repository root.");
            return directory.FullName;
        }
    }
    private static byte[] Original => File.ReadAllBytes(Path.Combine(Root, "assets/generated/lyra_als/linked_layer_contracts.json"));
    private static byte[] Inventory => File.ReadAllBytes(Path.Combine(Root, "assets/generated/lyra_als/linked_layer_inventory.json"));
    private static string Interface => "/Game/Characters/Heroes/Mannequin/Animations/LinkedLayers/ALI_ItemAnimLayers.ALI_ItemAnimLayers_C";

    [Fact]
    public void OriginalCompiledAbiAndOwnershipSurviveGenericImport()
    {
        var result = AlsAnimationLayerContractCompiler.Compile(Original, Inventory);
        Assert.Equal(11, result.Classes.Length);
        Assert.Equal(164, result.Classes.Sum(c => c.Functions.Length));
        Assert.Equal(14, result.Class(Interface).Functions.Length);
        foreach (var provider in result.Classes.Where(c => c.ClassPath != Interface && c.Calls.Length == 0))
        {
            result.ValidateImplementation(Interface, provider.ClassPath);
            var aim = provider.Function("FullBody_Aiming");
            Assert.Equal(new[] { "PreAimPose" }, aim.InputPoses);
            Assert.All(aim.Parameters, p => Assert.True(p.ClassBound && p.Type == AlsAnimationLayerScalarType.Double));
        }
        var main = result.Classes.Single(c => c.Calls.Length != 0);
        var bindings = result.CreateBindings(main.ClassPath);
        var unarmed = result.Classes.Single(c => c.ClassPath.Contains("ABP_UnarmedAnimLayers.", StringComparison.Ordinal));
        bindings.Commit(bindings.PrepareLink(unarmed.ClassPath));
        Assert.Single(bindings.Targets.Select(t => t.Instance).Distinct());
        Assert.All(bindings.Targets, t => Assert.Equal(unarmed.ClassPath, t.Class));
    }

    [Fact]
    public void GeneratedProductionSourceIsExactAndRetainsPublishedOrdinals()
    {
        var catalog = AlsAnimationLayerContractCompiler.Compile(Original, Inventory);
        var ids = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllBytes(Path.Combine(Root,
            "tools/contracts/lyra-layer-function-ids.json")))!;
        var generated = AlsAnimationLayerCodeGenerator.Generate(catalog, Interface,
            new("GodotAls.Animation.Lyra", "Lyra", "LyraLayerPoseInput"), ids);
        Assert.Equal(File.ReadAllText(Path.Combine(Root,
            "src/Als.Godot/Animation/Lyra/LyraGeneratedLayerContract.g.cs")), generated);
        Assert.Equal(0, ids["FullBody_Aiming"]); Assert.Equal(13, ids["LeftHandPose_OverrideState"]);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Original)).ToLowerInvariant(), catalog.SourceSha256);
    }

    [Theory]
    [InlineData("duplicate-function")][InlineData("duplicate-node")][InlineData("duplicate-alias")]
    [InlineData("duplicate-parameter")][InlineData("binding")][InlineData("unknown-type")]
    [InlineData("input-pose")][InlineData("inventory")][InlineData("signature-type")][InlineData("null-group")]
    public void RejectsMalformedOrIncompatibleCompiledAbi(string mutation)
    {
        var root = JsonNode.Parse(Original)!;
        var classes = root["classes"]!;
        var provider = classes["unarmed"]!;
        var aiming = provider["functions"]!.AsArray().Single(f => f!["name"]!.GetValue<string>() == "FullBody_Aiming")!;
        switch (mutation)
        {
            case "duplicate-function": provider["functions"]!.AsArray().Add(aiming.DeepClone()); break;
            case "duplicate-node": classes["main"]!["linkedNodes"]!.AsArray().Add(classes["main"]!["linkedNodes"]![0]!.DeepClone()); break;
            case "duplicate-alias":
                var raw = System.Text.Encoding.UTF8.GetString(Original).Replace("\"classes\":{", "\"classes\":{\"interface\":" + classes["interface"]!.ToJsonString() + ",", StringComparison.Ordinal);
                Assert.Throws<InvalidOperationException>(() => AlsAnimationLayerContractCompiler.Compile(System.Text.Encoding.UTF8.GetBytes(raw), Inventory)); return;
            case "duplicate-parameter": aiming["inputProperties"]!.AsArray().Add(aiming["inputProperties"]![0]!.DeepClone()); break;
            case "binding": aiming["inputProperties"]![0]!["classType"] = "float"; break;
            case "unknown-type": aiming["inputProperties"]![0]!["functionType"] = "FTransform"; aiming["inputProperties"]![0]!["classType"] = "FTransform"; break;
            case "input-pose": classes["main"]!["linkedNodes"]![0]!["inputPoses"]!.AsArray().Add("Unexpected"); break;
            case "inventory": root["inventorySha256"] = new string('0', 64); break;
            case "signature-type": aiming["inputProperties"]![0]!["functionType"] = "float"; aiming["inputProperties"]![0]!["classType"] = "float"; break;
            case "null-group": aiming["group"] = null; break;
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(root.ToJsonString());
        Assert.Throws<InvalidOperationException>(() =>
        {
            var compiled = AlsAnimationLayerContractCompiler.Compile(bytes, Inventory);
            compiled.ValidateImplementation(Interface, provider["class"]!.GetValue<string>());
        });
    }

    [Fact]
    public void PartialImplementationCanChooseItsOwnInstanceGroup()
    {
        var root = JsonNode.Parse(Original)!;
        var provider = root["classes"]!["unarmed"]!;
        var functions = provider["functions"]!.AsArray();
        functions.Single(f => f!["name"]!.GetValue<string>() == "FullBody_Aiming")!["group"] = "IndependentAim";
        functions.Single(f => f!["name"]!.GetValue<string>() == "FullBody_CycleState")!["implemented"] = false;
        var compiled = AlsAnimationLayerContractCompiler.Compile(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()), Inventory);
        compiled.ValidateImplementation(Interface, provider["class"]!.GetValue<string>());
        Assert.Equal("IndependentAim", compiled.Class(provider["class"]!.GetValue<string>()).Function("FullBody_Aiming").Group);
    }
}
