using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLocomotionInputCompilerTests
{
    [Fact]
    public void CompilesCharacterInterfaceAndAnimBpPredicateAndMatchesNativeCalls()
    {
        var json = Read();
        var profile = AlsLocomotionInputCompiler.Compile(json);
        Assert.Equal(new AlsStandingMovementSettings(.01f, 1.5f, 0), profile.Movement);
        Assert.Equal(2f, profile.PivotSpeedLimit);
        using var doc = JsonDocument.Parse(json);
        var count = 0;
        foreach (var row in doc.RootElement.GetProperty("shouldMoveNativeCases").EnumerateArray())
        {
            Assert.Equal(row.GetProperty("shouldMove").GetBoolean(), AlsStandingMovementInputModel.ShouldMove(
                row.GetProperty("isMoving").GetBoolean(), row.GetProperty("hasMovementInput").GetBoolean(),
                row.GetProperty("speed").GetSingle() * .01f, profile.Movement));
            count++;
        }
        Assert.Equal(40, count);
        Assert.Equal(profile, AlsLocomotionInputCompiler.Compile(json));
    }

    [Theory]
    [InlineData("ShouldMoveCheck", "K2Node_CommutativeAssociativeBinaryOperator_1", "FunctionReference", "memberName", "BooleanAND")]
    [InlineData("SetEssentialValues", "K2Node_CallFunction_62", "FunctionReference", "memberName", "GetVelocity")]
    [InlineData("SetEssentialValues", "K2Node_CallFunction_9", "FunctionReference", "memberName", "GetActorLocation")]
    [InlineData("SetEssentialValues", "K2Node_CallFunction_23", "FunctionReference", "memberName", "GreaterEqual_DoubleDouble")]
    [InlineData("SetEssentialValues", "K2Node_CallFunction_63", "FunctionReference", "memberParent", "/Script/Engine.Actor")]
    public void RejectsChangedSemantics(string graph, string node, string reference, string field, string value)
    {
        var root = JsonNode.Parse(Read())!;
        Node(root, graph, node)["properties"]![reference]![field] = value;
        Assert.ThrowsAny<ArgumentException>(() => AlsLocomotionInputCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void HorizontalSpeedMustNotIncludeVerticalVelocity()
    {
        var root = JsonNode.Parse(Read())!;
        var pin = Node(root, "SetEssentialValues", "K2Node_CallFunction_5")["pins"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == "A_Z")!;
        pin["value"] = "1.0";
        Assert.Throws<ArgumentException>(() => AlsLocomotionInputCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void RejectsBrokenCharacterToAnimationBinding()
    {
        var root = JsonNode.Parse(Read())!;
        var pin = Node(root, "UpdateCharacterInfo", "K2Node_VariableSet_5")["pins"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == "HasMovementInput")!;
        pin["links"]![0]!["pin"] = "IsMoving";
        Assert.Throws<ArgumentException>(() => AlsLocomotionInputCompiler.Compile(root.ToJsonString()));
    }

    [Fact]
    public void ReadsThresholdFromSourceAndIncludesItInDigest()
    {
        var original = AlsLocomotionInputCompiler.Compile(Read());
        var root = JsonNode.Parse(Read())!;
        Node(root, "ShouldMoveCheck", "K2Node_CallFunction_0")["pins"]!.AsArray()
            .Single(p => p!["name"]!.GetValue<string>() == "B")!["value"] = "170.0";
        var changed = AlsLocomotionInputCompiler.Compile(root.ToJsonString());
        Assert.Equal(1.7f, changed.Movement.ForcedMoveSpeedThreshold, 5);
        Assert.NotEqual(original.Digest, changed.Digest);
    }

    private static JsonNode Node(JsonNode root, string graph, string node) => root["graphs"]!.AsArray()
        .Single(g => g!["name"]!.GetValue<string>() == graph)!["nodes"]!.AsArray()
        .Single(n => n!["name"]!.GetValue<string>() == node)!;
    private static string Read() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "v4_locomotion_inputs.json"));
}
