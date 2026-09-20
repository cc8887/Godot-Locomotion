using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public sealed record AlsRagdollPoseProfile(int AnimationId, int SkeletonId, int PlayerNodeIndex,
    string PlayerPath, string SnapshotName, string BindingDigest, AlsRagdollAnimationInput Input);

public static class AlsRagdollPoseCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string Machine = Source + ":AnimGraph.AnimGraphNode_StateMachine_10.Ragdoll States";
    public static AlsRagdollPoseProfile Compile(string inputJson, string layeringJson, AlsAnimationSetDefinition set)
    {
        using var inputDocument = JsonDocument.Parse(inputJson); var input = inputDocument.RootElement;
        using var layerDocument = JsonDocument.Parse(layeringJson); var layer = layerDocument.RootElement;
        Require(Text(input, "source") == Source && Text(layer, "source") == Source && input.GetProperty("schemaVersion").GetInt32() == 1,
            "Foreign Ragdoll animation input source.");
        var graphs = input.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"));
        var flail = GraphAt(Machine + ".AnimStateNode_0.In Ragdoll");
        var player = flail.Named("AnimGraphNode_SequencePlayer_0");
        flail.Link(flail.Named("AnimGraphNode_StateResult_0"), "Result", player, "Pose");
        var rate = flail.One("K2Node_VariableGet", "FlailRate"); flail.Self(rate); flail.Link(player, "PlayRate", rate, "FlailRate");
        var playerPath = Machine + ".AnimStateNode_0.In Ragdoll." + player.Name;
        var inventory = layer.GetProperty("compiledNodeInventory").EnumerateArray().Single(n => Text(n, "path") == playerPath);
        var policy = inventory.GetProperty("properties").GetProperty("Node");
        Require(Text(inventory, "class") == player.Kind && Text(policy, "method") == "DoNotSync" && Text(policy, "groupName") == "None" &&
            policy.GetProperty("bLoopAnimation").GetBoolean() && !policy.GetProperty("bStartFromMatchingPose").GetBoolean() &&
            policy.GetProperty("startPosition").GetDouble() == 0 && policy.GetProperty("playRateBasis").GetDouble() == 1,
            "Unsupported original Ragdoll player policy.");
        foreach (var callback in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" })
            Require(Text(policy.GetProperty(callback), "className") == "None" && Text(policy.GetProperty(callback), "functionName") == "None", "Unsupported Ragdoll player callback.");
        var scale = policy.GetProperty("playRateScaleBiasClampConstants");
        Require(!scale.GetProperty("bMapRange").GetBoolean() && !scale.GetProperty("bClampResult").GetBoolean() &&
            !scale.GetProperty("bInterpResult").GetBoolean() && scale.GetProperty("scale").GetDouble() == 1 && scale.GetProperty("bias").GetDouble() == 0,
            "Changed Ragdoll source rate transform.");
        var animation = set.Animations.Single(a => a.ObjectPath == Text(policy, "sequence"));
        Require(animation.ObjectPath.EndsWith("/ALS_Flail.ALS_Flail", StringComparison.Ordinal), "Changed Ragdoll animation source.");
        var snapshotGraph = GraphAt(Machine + ".AnimStateNode_1.Blend Out Pose");
        var snapshot = snapshotGraph.Named("AnimGraphNode_PoseSnapshot_0");
        var snapshotPolicy = layer.GetProperty("compiledNodeInventory").EnumerateArray().Single(n =>
            Text(n, "path") == Machine + ".AnimStateNode_1.Blend Out Pose." + snapshot.Name).GetProperty("properties").GetProperty("Node");
        Require(Text(snapshotPolicy, "mode") == "NamedSnapshot", "Changed Ragdoll snapshot source mode.");
        snapshotGraph.Link(snapshotGraph.Named("AnimGraphNode_StateResult_0"), "Result", snapshot, "Pose");
        var snapshotName = snapshotGraph.Literal(snapshot, "SnapshotName");
        Require(snapshotName == "RagdollPose", "Changed original Ragdoll snapshot query.");
        var endGraph = new Graph(Text(input.GetProperty("characterGraphs").EnumerateArray().Single(g => Text(g, "name") == "RagdollEnd"), "nativeText"), true);
        var save = endGraph.One("K2Node_CallFunction", "SavePoseSnapshot"); endGraph.Function(save, "SavePoseSnapshot", "Engine.AnimInstance");
        Require(endGraph.Literal(save, "SnapshotName") == snapshotName, "RagdollEnd saves a different named pose.");
        var mainInstance = endGraph.Named("K2Node_VariableGet_2"); endGraph.Self(mainInstance);
        Require(mainInstance.Member == "MainAnimInstance", "Snapshot is not saved on the main animation instance.");
        var sequence = endGraph.Named("K2Node_ExecutionSequence_0");
        endGraph.Link(sequence, "execute", endGraph.One("K2Node_FunctionEntry", "RagdollEnd"), "then");
        endGraph.Link(mainInstance, "execute", sequence, "then_0");
        endGraph.Link(save, "execute", mainInstance, "then"); endGraph.Link(save, "self", mainInstance, "MainAnimInstance");

        var update = GraphAt(Source + ":UpdateRagdollValues");
        var setter = update.One("K2Node_VariableSet", "FlailRate"); update.Self(setter);
        var map = update.One("K2Node_CallFunction", "MapRangeClamped"); update.Function(map, "MapRangeClamped", "Engine.KismetMathLibrary");
        var size = update.One("K2Node_CallFunction", "VSize"); update.Function(size, "VSize", "Engine.KismetMathLibrary");
        var velocity = update.One("K2Node_CallFunction", "GetPhysicsLinearVelocity"); update.Function(velocity, "GetPhysicsLinearVelocity", "Engine.PrimitiveComponent");
        var owner = update.One("K2Node_CallFunction", "GetOwningComponent"); update.Self(owner);
        update.Link(setter, "FlailRate", map, "ReturnValue"); update.Link(map, "Value", size, "ReturnValue");
        update.Link(size, "A", velocity, "ReturnValue"); update.Link(velocity, "self", owner, "ReturnValue");
        update.Link(setter, "execute", velocity, "then");
        update.Link(velocity, "execute", update.One("K2Node_FunctionEntry", "UpdateRagdollValues"), "then");
        var bone = update.Literal(velocity, "BoneName"); Require(bone == "root", "Changed physics velocity bone.");
        var model = new AlsRagdollAnimationInput(bone, Number("InRangeA"), Number("InRangeB"), Number("OutRangeA"), Number("OutRangeB"));
        _ = model.FlailRate(default);
        return new(animation.Id, animation.SkeletonId, inventory.GetProperty("compiledNodeIndex").GetInt32(), playerPath, snapshotName,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(playerPath + "|" + animation.StableId))), model);
        Graph GraphAt(string path) => new(Text(graphs[path], "nativeText"), true);
        double Number(string name)
        {
            if (!double.TryParse(update.Literal(map, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                throw new InvalidDataException("Invalid FlailRate mapping endpoint.");
            return number;
        }
    }
    private static string Text(JsonElement p, string name) => p.GetProperty(name).GetString() ?? throw new InvalidDataException(name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
