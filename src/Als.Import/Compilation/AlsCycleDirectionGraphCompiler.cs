using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

internal static class AlsCycleDirectionGraphCompiler
{
    public static void Validate(JsonElement root)
    {
        const string path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP:(N) CycleBlending";
        string[] names = ["Move F", "Move B", "Move LF", "Move LB", "Move RF", "Move RB"];
        string[] caches = ["(N) F Movement", "(N) B Movement", "(N) LF Movement", "(N) LB Movement", "(N) RF Movement", "(N) RB Movement"];
        string[] axes = ["F", "B", "L", "R"];
        foreach (var state in Enumerable.Range(0, 6))
        {
            var graph = root.GetProperty("graphs").EnumerateArray().Single(g => Text(g, "name") == names[state] &&
                Text(g, "path").StartsWith(path + ".", StringComparison.Ordinal));
            var nodes = graph.GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"));
            var blend = nodes.Values.Single(n => Text(n, "class") == "AnimGraphNode_MultiWayBlend");
            var data = blend.GetProperty("properties").GetProperty("Node");
            var scale = data.GetProperty("alphaScaleBias");
            Require(data.GetProperty("poses").GetArrayLength() == 4 && !data.GetProperty("bAdditiveNode").GetBoolean() &&
                data.GetProperty("bNormalizeAlpha").GetBoolean() && scale.GetProperty("scale").GetSingle() == 1 &&
                scale.GetProperty("bias").GetSingle() == 0, "Directional MultiWayBlend policy differs.");
            for (var axis = 0; axis < 4; axis++)
            {
                var vector = System.Numerics.Vector4.Zero; vector[axis] = 1;
                var direction = Enumerable.Range(0, 6).Single(d => AlsStandingCycle.DirectionWeight((AlsCycleDirection)state, d, vector) == 1);
                var read = Linked($"Poses_{axis}", "AnimGraphNode_UseCachedPose", out var posePin);
                Require(posePin == "Pose" && Text(read.GetProperty("properties"), "NameOfCache") == caches[direction],
                    "Directional state reads a different cached source.");
                var getter = Linked($"DesiredAlphas_{axis}", "K2Node_VariableGet", out var weightPin);
                var variable = getter.GetProperty("properties").GetProperty("VariableReference");
                Require(Text(variable, "memberName") == "VelocityBlend" && variable.GetProperty("bSelfContext").GetBoolean() &&
                    weightPin.StartsWith($"VelocityBlend_{axes[axis]}_", StringComparison.Ordinal), "Directional weight input differs.");
            }
            JsonElement Linked(string pinName, string nodeClass, out string outputPin)
            {
                var pin = blend.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == pinName && Text(p, "direction") == "input");
                var link = pin.GetProperty("links").EnumerateArray().Single();
                var node = nodes[Text(link, "node")];
                Require(Text(node, "class") == nodeClass, "Unsupported directional pose path.");
                outputPin = Text(link, "pin"); return node;
            }
        }
    }

    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool condition, string reason) { if (!condition) throw new FormatException(reason); }
}
