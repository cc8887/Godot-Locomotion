using System.Globalization;
using System.Text.Json;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsCharacterAnimationBridgeSettings(float ComponentTeleportDistance,
    float RotationReferenceHz, float RotationDeadZone);

public static class AlsCharacterAnimationBridgeCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP";
    public static AlsCharacterAnimationBridgeSettings Compile(string text)
    {
        using var json = JsonDocument.Parse(text); var root = json.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("source").GetString() == Source,
            "Wrong character animation bridge source.");
        var distance = root.GetProperty("teleportDistanceThresholdCm").GetSingle();
        Require(float.IsFinite(distance) && distance >= 0, "Invalid character component teleport distance.");
        var item = root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("name").GetString() == "UpdateGroudedRotation");
        Require(item.GetProperty("path").GetString() == Source + ":UpdateGroudedRotation", "Wrong grounded rotation graph.");
        var graph = new Graph(item.GetProperty("nativeText").GetString()!);
        var add = graph.One("K2Node_CallFunction", "K2_AddActorWorldRotation");
        var (multiply, _) = graph.Follow(add, "DeltaRotation_Yaw");
        Require(multiply.Member == "Multiply_DoubleDouble", "RotationAmount no longer scales a yaw delta.");
        var (curve, _) = graph.Follow(multiply, "A");
        Require(curve.Member == "GetAnimCurveValue" && graph.Literal(curve, "CurveName") == "RotationAmount", "Wrong actor rotation curve.");
        graph.Self(curve);
        var (scale, _) = graph.Follow(multiply, "B");
        Require(scale.Member == "Divide_DoubleDouble", "Missing frame-rate scaling.");
        var (delta, _) = graph.Follow(scale, "A");
        Require(delta.Member == "GetWorldDeltaSeconds", "Actor rotation uses a different time source.");
        var (period, _) = graph.Follow(scale, "B");
        Require(period.Member == "Divide_DoubleDouble", "Missing reference frame period.");
        var numerator = Number(graph, period, "A"); var hz = Number(graph, period, "B");
        Require(numerator == 1 && hz > 0, "Invalid reference frame period.");
        var (branch, branchPin) = graph.Follow(add, "execute");
        Require(branch.Kind == "K2Node_IfThenElse" && branchPin.Name == "then", "Rotation delta is not conditionally applied.");
        var (greater, _) = graph.Follow(branch, "Condition");
        Require(greater.Member == "Greater_DoubleDouble", "Rotation dead zone changed.");
        var deadZone = Number(graph, greater, "B");
        var (absolute, _) = graph.Follow(greater, "A");
        Require(absolute.Member == "Abs" && deadZone >= 0, "Invalid rotation dead zone.");
        graph.Link(absolute, "A", curve, "ReturnValue");
        return new(distance * .01f, hz, deadZone);
    }
    private static float Number(Graph graph, Node node, string pin)
    {
        var value = float.Parse(graph.Literal(node, pin), CultureInfo.InvariantCulture);
        Require(float.IsFinite(value), "Non-finite character bridge literal."); return value;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Character animation bridge: " + message); }
}
