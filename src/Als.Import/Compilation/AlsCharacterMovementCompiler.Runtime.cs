using System.Globalization;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static partial class AlsCharacterMovementCompiler
{
    public static AlsCharacterMovementRuntime CompileRuntime(string text, AlsCharacterMovementModel model)
    {
        using var document = JsonDocument.Parse(text); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "source") == Source, "Foreign movement runtime.");
        var settings = root.GetProperty("settings");
        Require(settings.GetProperty("tick_before_owner").GetBoolean(), "CMC must tick before the character parameter update.");
        var sprint = new Graph(GraphText(root, "CanSprint"));
        var allowed = new Graph(GraphText(root, "GetAllowedGait"));
        ValidateAllowedGait(allowed, sprint);
        var land = new Graph(GraphText(root, "EventGraph"));
        var hasInput = land.Named("K2Node_IfThenElse_2");
        land.Link(hasInput, "Condition", land.Named("K2Node_VariableGet_9"), "HasMovementInput");
        land.Link(hasInput, "execute", land.Named("K2Node_IfThenElse_3"), "else");
        Require(land.Named("K2Node_VariableGet_9").Member == "HasMovementInput", "Landing friction no longer reads cached input.");
        var withInput = land.Named("K2Node_VariableSet_2"); var idle = land.Named("K2Node_VariableSet_1");
        var reset = land.Named("K2Node_VariableSet_0"); var delay = land.Named("K2Node_CallFunction_17");
        foreach (var setter in new[] { withInput, idle, reset })
        {
            Require(setter.Member == "BrakingFrictionFactor" && setter.Body.Contains("/Script/Engine.CharacterMovementComponent", StringComparison.Ordinal), "Foreign landing friction setter.");
            var (component, pin) = land.Follow(setter, "self");
            Require(component.Member == "CharacterMovement" && pin.Name == "CharacterMovement", "Landing friction writes another component."); land.Self(component);
        }
        land.Link(withInput, "execute", hasInput, "then"); land.Link(idle, "execute", hasInput, "else");
        land.Function(delay, "RetriggerableDelay", "Engine.KismetSystemLibrary");
        land.Links(delay, "execute", (withInput, "then"), (idle, "then")); land.Link(reset, "execute", delay, "then");
        Require(Parse(land.Literal(reset, "BrakingFrictionFactor")) == 0, "Landing reset factor changed.");
        var initial = root.GetProperty("initial");
        return new(model, new(new(Number(initial, "max_walk_speed"), Number(initial, "max_walk_speed_crouched"),
            Number(initial, "max_acceleration"), Number(initial, "braking_deceleration_walking"), Number(initial, "ground_friction")),
            (double)Parse(sprint.Literal(sprint.Named("K2Node_CallFunction_1"), "B")),
            double.Parse(sprint.Literal(sprint.Named("K2Node_CallFunction_8"), "B"), CultureInfo.InvariantCulture),
            Parse(land.Literal(withInput, "BrakingFrictionFactor")), Parse(land.Literal(idle, "BrakingFrictionFactor")), Parse(land.Literal(delay, "Duration")),
            Number(settings, "air_control"), Number(settings, "air_control_boost_multiplier"), Number(settings, "air_control_boost_velocity_threshold"),
            Number(settings, "falling_lateral_friction"), Number(settings, "braking_deceleration_falling")));
    }

    private static void ValidateAllowedGait(Graph g, Graph s)
    {
        Edge(g, "K2Node_SwitchEnum_0", "execute", "K2Node_FunctionEntry_0", "then");
        Selection(g, "K2Node_SwitchEnum_0", "K2Node_VariableGet_0", "Stance", "ALS_Stance");
        Edge(g, "K2Node_SwitchEnum_1", "execute", "K2Node_SwitchEnum_0", "NewEnumerator0");
        Selection(g, "K2Node_SwitchEnum_1", "K2Node_VariableGet_1", "RotationMode", "ALS_RotationMode");
        Selection(g, "K2Node_SwitchEnum_2", "K2Node_VariableGet_4", "DesiredGait", "ALS_Gait");
        Selection(g, "K2Node_SwitchEnum_3", "K2Node_VariableGet_3", "DesiredGait", "ALS_Gait");
        g.Links(g.Named("K2Node_SwitchEnum_2"), "execute", (g.Named("K2Node_SwitchEnum_0"), "NewEnumerator1"), (g.Named("K2Node_SwitchEnum_1"), "NewEnumerator3"));
        Edge(g, "K2Node_SwitchEnum_3", "execute", "K2Node_Knot_0", "OutputPin");
        g.Links(g.Named("K2Node_Knot_0"), "InputPin", (g.Named("K2Node_SwitchEnum_1"), "NewEnumerator0"), (g.Named("K2Node_SwitchEnum_1"), "NewEnumerator1"));
        foreach (var (node, predecessor, pin, value) in new[] {
            ("K2Node_FunctionResult_0", "K2Node_SwitchEnum_3", "NewEnumerator0", "NewEnumerator0"),
            ("K2Node_FunctionResult_2", "K2Node_SwitchEnum_3", "NewEnumerator1", "NewEnumerator1"),
            ("K2Node_FunctionResult_6", "K2Node_IfThenElse_0", "then", "NewEnumerator2"),
            ("K2Node_FunctionResult_7", "K2Node_IfThenElse_0", "else", "NewEnumerator1"),
            ("K2Node_FunctionResult_10", "K2Node_SwitchEnum_2", "NewEnumerator0", "NewEnumerator0") })
        { Edge(g, node, "execute", predecessor, pin); Require(g.Literal(g.Named(node), "AllowedGait") == value, "Changed allowed gait result."); }
        g.Links(g.Named("K2Node_FunctionResult_11"), "execute", (g.Named("K2Node_SwitchEnum_2"), "NewEnumerator1"), (g.Named("K2Node_SwitchEnum_2"), "NewEnumerator2"));
        Require(g.Literal(g.Named("K2Node_FunctionResult_11"), "AllowedGait") == "NewEnumerator1", "Crouched/aiming gait changed.");
        Edge(g, "K2Node_IfThenElse_0", "execute", "K2Node_SwitchEnum_3", "NewEnumerator2");
        Edge(g, "K2Node_IfThenElse_0", "Condition", "K2Node_CallFunction_2", "CanSprint");
        Require(g.Named("K2Node_CallFunction_2").Member == "CanSprint", "Foreign sprint eligibility."); g.Self(g.Named("K2Node_CallFunction_2"));
        Selection(s, "K2Node_SwitchEnum_0", "K2Node_VariableGet_6", "RotationMode", "ALS_RotationMode");
        Edge(s, "K2Node_IfThenElse_1", "execute", "K2Node_FunctionEntry_0", "then");
        Edge(s, "K2Node_IfThenElse_1", "Condition", "K2Node_VariableGet_3", "HasMovementInput");
        Require(s.Named("K2Node_VariableGet_3").Member == "HasMovementInput", "Sprint input guard changed.");
        Edge(s, "K2Node_SwitchEnum_0", "execute", "K2Node_IfThenElse_1", "then");
        foreach (var (result, predecessor, output) in new[] { ("K2Node_FunctionResult_2", "K2Node_IfThenElse_1", "else"), ("K2Node_FunctionResult_0", "K2Node_SwitchEnum_0", "NewEnumerator3") })
        { Edge(s, result, "execute", predecessor, output); Require(s.Literal(s.Named(result), "CanSprint") == "false", "Sprint false guard changed."); }
        foreach (var (result, branch, math) in new[] { ("K2Node_FunctionResult_1", "NewEnumerator0", "K2Node_CallFunction_1"), ("K2Node_FunctionResult_3", "NewEnumerator1", "K2Node_CommutativeAssociativeBinaryOperator_0") })
        {
            var (node, pin) = s.FollowReroutes(s.Named(result), "execute"); Require(node == s.Named("K2Node_SwitchEnum_0") && pin.Name == branch, "Wrong sprint mode branch.");
            Edge(s, result, "CanSprint", math, "ReturnValue");
        }
        foreach (var (node, variable) in new[] { ("K2Node_CallFunction_1", "K2Node_VariableGet_0"), ("K2Node_CallFunction_9", "K2Node_VariableGet_2") })
        { s.Function(s.Named(node), "Greater_DoubleDouble", "Engine.KismetMathLibrary"); Edge(s, node, "A", variable, "MovementInputAmount"); Require(s.Named(variable).Member == "MovementInputAmount", "Sprint input amount changed."); }
        Require(s.Literal(s.Named("K2Node_CallFunction_1"), "B") == s.Literal(s.Named("K2Node_CallFunction_9"), "B"), "Sprint thresholds disagree.");
        Require(s.Named("K2Node_CommutativeAssociativeBinaryOperator_0").Member == "BooleanAND", "Sprint conditions no longer combine with AND.");
        Edge(s, "K2Node_CommutativeAssociativeBinaryOperator_0", "A", "K2Node_CallFunction_9", "ReturnValue");
        Edge(s, "K2Node_CommutativeAssociativeBinaryOperator_0", "B", "K2Node_CallFunction_8", "ReturnValue");
        s.Function(s.Named("K2Node_CallFunction_8"), "Less_DoubleDouble", "Engine.KismetMathLibrary");
        s.Function(s.Named("K2Node_CallFunction_11"), "Abs", "Engine.KismetMathLibrary");
        s.Function(s.Named("K2Node_CallFunction_5"), "NormalizedDeltaRotator", "Engine.KismetMathLibrary");
        s.Function(s.Named("K2Node_CallFunction_4"), "Conv_VectorToRotator", "Engine.KismetMathLibrary");
        s.Function(s.Named("K2Node_CallFunction_3"), "GetCurrentAcceleration", "Engine.CharacterMovementComponent");
        Edge(s, "K2Node_CallFunction_8", "A", "K2Node_CallFunction_11", "ReturnValue");
        Edge(s, "K2Node_CallFunction_11", "A", "K2Node_CallFunction_5", "ReturnValue_Yaw");
        Edge(s, "K2Node_CallFunction_5", "A", "K2Node_CallFunction_4", "ReturnValue");
        Edge(s, "K2Node_CallFunction_5", "B", "K2Node_CallFunction_6", "ReturnValue");
        Edge(s, "K2Node_CallFunction_4", "InVec", "K2Node_CallFunction_3", "ReturnValue");
        Edge(s, "K2Node_CallFunction_3", "self", "K2Node_VariableGet_4", "CharacterMovement");
        Require(s.Named("K2Node_CallFunction_6").Member == "GetControlRotation", "Sprint control yaw source changed."); s.Self(s.Named("K2Node_CallFunction_6"));
    }

    private static void Edge(Graph graph, string node, string pin, string source, string output) => graph.Link(graph.Named(node), pin, graph.Named(source), output);
    private static void Selection(Graph graph, string node, string variable, string field, string enumName)
    {
        Require(graph.Named(node).Kind == "K2Node_SwitchEnum" && graph.Named(node).Body.Contains(enumName + "." + enumName, StringComparison.Ordinal) && graph.Named(variable).Member == field, "Foreign movement selector.");
        graph.Self(graph.Named(variable)); Edge(graph, node, "Selection", variable, field);
    }
}
