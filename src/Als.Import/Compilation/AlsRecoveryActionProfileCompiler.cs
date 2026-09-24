using System.Text.Json.Nodes;

namespace GodotAls.Import.Compilation;

// Extend the frozen P5A profile without renumbering its existing action IDs or
// rewriting its historical reference inputs. The result is ONE action bank.
public static class AlsRecoveryActionProfileCompiler
{
    public static AlsP5aAnimationRuntimeProfile Compile(string baseProfile, string additionalActions,
        AlsAnimationSetDefinition set)
    {
        var root = JsonNode.Parse(baseProfile)!.AsObject();
        var actions = root["actions"]!.AsArray();
        foreach (var action in JsonNode.Parse(additionalActions)!.AsArray()) actions.Add(action!.DeepClone());
        return AlsP5aAnimationRuntimeProfileCompiler.Compile(root.ToJsonString(), set);
    }
}
