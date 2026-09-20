using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsJumpStateProfile(AlsJumpStateDefinition Runtime, AlsJumpPoseProfile Pose, int ParentMachineNodeIndex);

public static class AlsJumpStateCompiler
{
    public static AlsJumpStateProfile Compile(string graphJson, string cacheJson, AlsLocomotionSourceProfile sources, AlsAnimationSetDefinition set)
    {
        var pose = AlsJumpPoseCompiler.Compile(graphJson, sources, set);
        var machines = AlsGroundedMachineCompiler.CompileMovement(graphJson);
        using var graphDocument = JsonDocument.Parse(graphJson); using var cacheDocument = JsonDocument.Parse(cacheJson);
        var root = graphDocument.RootElement; var cache = cacheDocument.RootElement;
        Require(cache.GetProperty("inventorySchemaVersion").GetInt32() == 1 && Text(cache, "source") == Text(root, "source"),
            "Jump inventory provenance differs.");
        var count = cache.GetProperty("compiledPropertyCount").GetInt32();
        var inventory = cache.GetProperty("compiledNodeInventory").EnumerateArray().ToDictionary(n => Text(n, "path"));
        Require(count > 0 && inventory.Count == count, "Incomplete Jump node inventory.");
        var own = Index(machines.Jump!, "AnimGraphNode_StateMachine");
        var parent = Index(machines.Movement!, "AnimGraphNode_StateMachine");
        foreach (var id in pose.PlayerIds)
        {
            var player = sources.Players[id]; var native = inventory[player.SourceNode];
            Require(Text(native, "class") == "AnimGraphNode_SequencePlayer" && native.GetProperty("compiledNodeIndex").GetInt32() == player.CompiledNodeIndex &&
                native.GetProperty("compiledNodeIndex").GetInt32() + native.GetProperty("propertyIndex").GetInt32() == count - 1,
                "Jump source node identity differs.");
        }
        var ids = pose.PlayerIds;
        return new(new(pose.Machine, own, ids[0], ids[1], ids[2], ids[3], ids[4], ids[5]), pose, parent);

        int Index(AlsGroundedMachineProfile machine, string expectedClass)
        {
            var path = machine.SourcePath[..machine.SourcePath.LastIndexOf('.')]; var native = inventory[path];
            var index = native.GetProperty("compiledNodeIndex").GetInt32(); var property = native.GetProperty("propertyIndex").GetInt32();
            Require(Text(native, "class") == expectedClass && index >= 0 && property >= 0 && index + property == count - 1 &&
                Text(native.GetProperty("properties"), "EditorStateMachineGraph") == machine.SourcePath, "Jump machine node identity differs.");
            return index;
        }
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new FormatException("Expected text.");
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
