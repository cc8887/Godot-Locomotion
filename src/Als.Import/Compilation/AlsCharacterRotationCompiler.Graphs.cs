using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static partial class AlsCharacterRotationCompiler
{
    private static AlsCharacterRotationSettings ValidateGraphs(JsonElement root, AlsCharacterAnimationBridgeSettings bridge)
    {
        ValidateEnums(root); ValidateSmooth(root); ValidateMappedSpeed(root); ValidateMovementSelection(root);
        ValidateHistory(root); ValidateLimit(root);
        var ground = Graph(root, "UpdateGroudedRotation");
        Switch(ground, "K2Node_SwitchEnum_0", "ALS_MovementAction", "K2Node_VariableGet_9", "MovementAction");
        Switch(ground, "K2Node_SwitchEnum_2", "ALS_RotationMode", "K2Node_VariableGet_1", "RotationMode");
        Switch(ground, "K2Node_SwitchEnum_1", "ALS_RotationMode", "K2Node_VariableGet_2", "RotationMode");
        Switch(ground, "K2Node_SwitchEnum_4", "ALS_ViewMode", "K2Node_VariableGet_7", "ViewMode");
        Switch(ground, "K2Node_SwitchEnum_5", "ALS_Gait", "K2Node_VariableGet_5", "Gait");
        Edges(ground,
            ("K2Node_SwitchEnum_0", "execute", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_IfThenElse_5", "execute", "K2Node_SwitchEnum_0", "NewEnumerator4"),
            ("K2Node_IfThenElse_5", "Condition", "K2Node_CallFunction_16", "ReturnValue"),
            ("K2Node_SwitchEnum_2", "execute", "K2Node_IfThenElse_5", "then"),
            ("K2Node_SwitchEnum_4", "execute", "K2Node_IfThenElse_5", "else"),
            ("K2Node_SwitchEnum_1", "execute", "K2Node_SwitchEnum_4", "NewEnumerator0"),
            ("K2Node_SwitchEnum_5", "execute", "K2Node_SwitchEnum_2", "NewEnumerator1"),
            ("K2Node_Knot_0", "InputPin", "K2Node_SwitchEnum_2", "NewEnumerator0"),
            ("K2Node_Knot_4", "InputPin", "K2Node_SwitchEnum_2", "NewEnumerator3"),
            ("K2Node_CallFunction_15", "execute", "K2Node_Knot_0", "OutputPin"),
            ("K2Node_CallFunction_27", "execute", "K2Node_Knot_1", "OutputPin"),
            ("K2Node_CallFunction_32", "execute", "K2Node_SwitchEnum_5", "NewEnumerator2"),
            ("K2Node_CallFunction_22", "execute", "K2Node_Knot_4", "OutputPin"),
            ("K2Node_IfThenElse_2", "execute", "K2Node_SwitchEnum_0", "NewEnumerator2"),
            ("K2Node_IfThenElse_2", "Condition", "K2Node_VariableGet_11", "HasMovementInput"),
            ("K2Node_CallFunction_17", "execute", "K2Node_IfThenElse_2", "then"),
            ("K2Node_CallFunction_15", "Target_Yaw", "K2Node_VariableGet_8", "LastVelocityRotation_Yaw"),
            ("K2Node_CallFunction_32", "Target_Yaw", "K2Node_VariableGet_3", "LastVelocityRotation_Yaw"),
            ("K2Node_CallFunction_17", "Target_Yaw", "K2Node_VariableGet_10", "LastMovementInputRotation_Yaw"),
            ("K2Node_CallFunction_22", "Target_Yaw", "K2Node_CallFunction_23", "ReturnValue_Yaw"),
            ("K2Node_CallFunction_27", "Target_Yaw", "K2Node_CommutativeAssociativeBinaryOperator_2", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_2", "A", "K2Node_CallFunction_28", "ReturnValue_Yaw"),
            ("K2Node_CommutativeAssociativeBinaryOperator_2", "B", "K2Node_CallFunction_30", "ReturnValue"),
            ("K2Node_IfThenElse_0", "execute", "K2Node_Knot_2", "OutputPin"),
            ("K2Node_VariableSet_0", "execute", "K2Node_CallFunction_2", "then"),
            ("K2Node_VariableSet_0", "TargetRotation", "K2Node_CallFunction_18", "ReturnValue"));
        ground.Links(ground.Named("K2Node_Knot_1"), "InputPin", (ground.Named("K2Node_SwitchEnum_5"), "NewEnumerator0"), (ground.Named("K2Node_SwitchEnum_5"), "NewEnumerator1"));
        ground.Links(ground.Named("K2Node_CallFunction_26"), "execute", (ground.Named("K2Node_SwitchEnum_4"), "NewEnumerator1"), (ground.Named("K2Node_SwitchEnum_1"), "NewEnumerator3"));
        ground.Links(ground.Named("K2Node_Knot_3"), "InputPin", (ground.Named("K2Node_SwitchEnum_1"), "NewEnumerator0"), (ground.Named("K2Node_SwitchEnum_1"), "NewEnumerator1"));
        ground.Links(ground.Named("K2Node_Knot_2"), "InputPin", (ground.Named("K2Node_CallFunction_26"), "then"), (ground.Named("K2Node_Knot_3"), "OutputPin"));
        SelfCall(ground, "K2Node_CallFunction_16", "CanUpdateMovingRotation");
        SelfCall(ground, "K2Node_CallFunction_26", "LimitRotation");
        SelfCall(ground, "K2Node_CallFunction_30", "GetAnimCurveValue"); Literal(ground, "K2Node_CallFunction_30", "CurveName", "YawOffset");
        Member(ground, "K2Node_CommutativeAssociativeBinaryOperator_2", "Add_DoubleDouble");
        foreach (var name in new[] { "K2Node_CallFunction_23", "K2Node_CallFunction_28" }) Member(ground, name, "GetControlRotation");
        Member(ground, "K2Node_CallFunction_18", "K2_GetActorRotation"); Member(ground, "K2Node_VariableSet_0", "TargetRotation");
        foreach (var pair in new[] { ("K2Node_VariableGet_8", "LastVelocityRotation"), ("K2Node_VariableGet_3", "LastVelocityRotation"),
            ("K2Node_VariableGet_10", "LastMovementInputRotation"), ("K2Node_VariableGet_11", "HasMovementInput") }) Variable(ground, pair.Item1, pair.Item2);
        foreach (var pair in new[] { ("K2Node_CallFunction_15", "K2Node_CallFunction_14"), ("K2Node_CallFunction_27", "K2Node_CallFunction_24"), ("K2Node_CallFunction_32", "K2Node_CallFunction_33") })
        { SelfCall(ground, pair.Item2, "CalculateGroundedRotationRate"); Edge(ground, pair.Item1, "ActorInterpSpeed(Smooth)", pair.Item2, "ReturnValue"); }
        foreach (var name in new[] { "K2Node_CallFunction_15", "K2Node_CallFunction_27", "K2Node_CallFunction_32", "K2Node_CallFunction_22", "K2Node_CallFunction_17" }) SmoothCall(ground, name);
        Literal(ground, "K2Node_CallFunction_17", "TargetInterpSpeed(Const)", "0.0");
        var looking = Number(ground, "K2Node_CallFunction_27", "TargetInterpSpeed(Const)");
        Require(looking == Number(ground, "K2Node_CallFunction_32", "TargetInterpSpeed(Const)"), "Looking sprint target interpolation differs.");

        var gate = Graph(root, "CanUpdateMovingRotation");
        Member(gate, "K2Node_CommutativeAssociativeBinaryOperator_0", "BooleanAND");
        Member(gate, "K2Node_CommutativeAssociativeBinaryOperator_1", "BooleanOR");
        Member(gate, "K2Node_CommutativeAssociativeBinaryOperator_2", "BooleanAND");
        Member(gate, "K2Node_CallFunction_1", "Not_PreBool"); Member(gate, "K2Node_CallFunction_0", "Greater_DoubleDouble");
        Member(gate, "K2Node_CallFunction_13", "HasAnyRootMotion");
        Variable(gate, "K2Node_VariableGet_1", "HasMovementInput"); Variable(gate, "K2Node_VariableGet_2", "IsMoving"); Variable(gate, "K2Node_VariableGet_3", "Speed");
        Edges(gate, ("K2Node_FunctionResult_3", "ReturnValue", "K2Node_CommutativeAssociativeBinaryOperator_2", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_2", "A", "K2Node_CommutativeAssociativeBinaryOperator_1", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_2", "B", "K2Node_CallFunction_1", "ReturnValue"),
            ("K2Node_CallFunction_1", "A", "K2Node_CallFunction_13", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_1", "A", "K2Node_CommutativeAssociativeBinaryOperator_0", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_1", "B", "K2Node_CallFunction_0", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_0", "A", "K2Node_VariableGet_2", "IsMoving"),
            ("K2Node_CommutativeAssociativeBinaryOperator_0", "B", "K2Node_VariableGet_1", "HasMovementInput"),
            ("K2Node_CallFunction_0", "A", "K2Node_VariableGet_3", "Speed"));
        var essentials = Graph(root, "SetEssentialValues");
        Member(essentials, "K2Node_CallFunction_23", "Greater_DoubleDouble");
        Edge(essentials, "K2Node_VariableSet_3", "IsMoving", "K2Node_CallFunction_23", "ReturnValue");
        Edge(essentials, "K2Node_CallFunction_23", "A", "K2Node_VariableSet_8", "Output_Get");
        Member(essentials, "K2Node_VariableSet_8", "Speed");

        var rate = Graph(root, "CalculateGroundedRotationRate");
        Member(rate, "K2Node_CommutativeAssociativeBinaryOperator_0", "Multiply_DoubleDouble");
        Member(rate, "K2Node_CallFunction_10", "MapRangeClamped"); Member(rate, "K2Node_CallFunction_3", "GetFloatValue");
        SelfCall(rate, "K2Node_CallFunction_2", "GetMappedSpeed");
        Variable(rate, "K2Node_VariableGet_10", "AimYawRate"); Variable(rate, "K2Node_VariableGet_1", "CurrentMovementSettings");
        Edges(rate, ("K2Node_FunctionResult_0", "ReturnValue", "K2Node_CommutativeAssociativeBinaryOperator_0", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_0", "A", "K2Node_CallFunction_3", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_0", "B", "K2Node_CallFunction_10", "ReturnValue"),
            ("K2Node_CallFunction_10", "Value", "K2Node_VariableGet_10", "AimYawRate"),
            ("K2Node_CallFunction_3", "InTime", "K2Node_CallFunction_2", "ReturnValue"),
            ("K2Node_CallFunction_3", "self", "K2Node_BreakStruct_2", "RotationRateCurve_52_73FA146B4B31FF205DE8E1BBFA8800F6"),
            ("K2Node_BreakStruct_2", "MovementSettings", "K2Node_VariableGet_1", "CurrentMovementSettings"));

        var air = Graph(root, "UpdateInAirRotation");
        Switch(air, "K2Node_SwitchEnum_3", "ALS_RotationMode", "K2Node_VariableGet_9", "RotationMode");
        air.Links(air.Named("K2Node_Knot_0"), "InputPin", (air.Named("K2Node_SwitchEnum_3"), "NewEnumerator0"), (air.Named("K2Node_SwitchEnum_3"), "NewEnumerator1"));
        Edges(air, ("K2Node_SwitchEnum_3", "execute", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_Knot_1", "InputPin", "K2Node_Knot_0", "OutputPin"),
            ("K2Node_CallFunction_3", "execute", "K2Node_Knot_1", "OutputPin"),
            ("K2Node_Knot_2", "InputPin", "K2Node_SwitchEnum_3", "NewEnumerator3"),
            ("K2Node_CallFunction_1", "execute", "K2Node_Knot_2", "OutputPin"),
            ("K2Node_CallFunction_3", "Target_Yaw", "K2Node_VariableGet_6", "InAirRotation_Yaw"),
            ("K2Node_CallFunction_1", "Target_Yaw", "K2Node_CallFunction_7", "ReturnValue_Yaw"),
            ("K2Node_VariableSet_3", "execute", "K2Node_CallFunction_1", "then"),
            ("K2Node_VariableSet_3", "InAirRotation", "K2Node_CallFunction_6", "ReturnValue"));
        Variable(air, "K2Node_VariableGet_6", "InAirRotation"); Member(air, "K2Node_VariableSet_3", "InAirRotation");
        Member(air, "K2Node_CallFunction_7", "GetControlRotation"); Member(air, "K2Node_CallFunction_6", "K2_GetActorRotation");
        foreach (var name in new[] { "K2Node_CallFunction_1", "K2Node_CallFunction_3" })
        { SmoothCall(air, name); Literal(air, name, "TargetInterpSpeed(Const)", "0.0"); }
        return new(Number(essentials, "K2Node_CallFunction_23", "B") * .01f, Number(gate, "K2Node_CallFunction_0", "B") * .01f,
            Number(ground, "K2Node_CallFunction_15", "TargetInterpSpeed(Const)"), looking,
            Number(ground, "K2Node_CallFunction_22", "TargetInterpSpeed(Const)"), Number(ground, "K2Node_CallFunction_22", "ActorInterpSpeed(Smooth)"),
            Number(ground, "K2Node_CallFunction_17", "ActorInterpSpeed(Smooth)"), Number(air, "K2Node_CallFunction_3", "ActorInterpSpeed(Smooth)"),
            Number(air, "K2Node_CallFunction_1", "ActorInterpSpeed(Smooth)"),
            Number(ground, "K2Node_CallFunction_26", "AimYawMin"), Number(ground, "K2Node_CallFunction_26", "AimYawMax"),
            Number(ground, "K2Node_CallFunction_26", "InterpSpeed"), bridge.RotationReferenceHz, bridge.RotationDeadZone,
            Number(rate, "K2Node_CallFunction_10", "InRangeA"), Number(rate, "K2Node_CallFunction_10", "InRangeB"),
            Number(rate, "K2Node_CallFunction_10", "OutRangeA"), Number(rate, "K2Node_CallFunction_10", "OutRangeB"));
    }

    private static void ValidateSmooth(JsonElement root)
    {
        var g = Graph(root, "SmoothCharacterRotation");
        foreach (var pair in new[] { ("K2Node_CallFunction_7", "RInterpTo_Constant"), ("K2Node_CallFunction_8", "RInterpTo"),
            ("K2Node_CallFunction_9", "K2_GetActorRotation"), ("K2Node_CallFunction_0", "K2_SetActorRotation"),
            ("K2Node_CallFunction_1", "GetWorldDeltaSeconds"), ("K2Node_CallFunction_3", "GetWorldDeltaSeconds"), ("K2Node_VariableSet_0", "TargetRotation") }) Member(g, pair.Item1, pair.Item2);
        foreach (var pair in new[] { ("K2Node_VariableGet_2", "TargetRotation"), ("K2Node_VariableGet_8", "TargetRotation"),
            ("K2Node_VariableGet_5", "Target"), ("K2Node_VariableGet_10", "TargetInterpSpeed(Const)"), ("K2Node_VariableGet_11", "ActorInterpSpeed(Smooth)") })
            Require(g.Named(pair.Item1).Member == pair.Item2, "Wrong smooth rotation input variable.");
        Edges(g, ("K2Node_VariableSet_0", "execute", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_VariableSet_0", "TargetRotation", "K2Node_CallFunction_7", "ReturnValue"),
            ("K2Node_CallFunction_0", "execute", "K2Node_VariableSet_0", "then"),
            ("K2Node_CallFunction_0", "NewRotation", "K2Node_CallFunction_8", "ReturnValue"),
            ("K2Node_CallFunction_7", "Current", "K2Node_VariableGet_2", "TargetRotation"),
            ("K2Node_CallFunction_7", "Target", "K2Node_VariableGet_5", "Target"),
            ("K2Node_CallFunction_7", "InterpSpeed", "K2Node_VariableGet_10", "TargetInterpSpeed(Const)"),
            ("K2Node_CallFunction_7", "DeltaTime", "K2Node_CallFunction_1", "ReturnValue"),
            ("K2Node_CallFunction_8", "Current", "K2Node_CallFunction_9", "ReturnValue"),
            ("K2Node_CallFunction_8", "Target", "K2Node_VariableGet_8", "TargetRotation"),
            ("K2Node_CallFunction_8", "InterpSpeed", "K2Node_VariableGet_11", "ActorInterpSpeed(Smooth)"),
            ("K2Node_CallFunction_8", "DeltaTime", "K2Node_CallFunction_3", "ReturnValue"));
        Literal(g, "K2Node_CallFunction_0", "bTeleportPhysics", "false");
    }

    private static void ValidateLimit(JsonElement root)
    {
        var g = Graph(root, "LimitRotation");
        foreach (var pair in new[] { ("K2Node_CallFunction_28", "NormalizedDeltaRotator"), ("K2Node_CallFunction_2", "InRange_FloatFloat"),
            ("K2Node_CallFunction_8", "GetControlRotation"), ("K2Node_CallFunction_38", "GetControlRotation"), ("K2Node_CallFunction_30", "K2_GetActorRotation"),
            ("K2Node_CallFunction_6", "Greater_DoubleDouble"), ("K2Node_CallFunction_7", "SelectFloat"),
            ("K2Node_CommutativeAssociativeBinaryOperator_0", "Add_DoubleDouble"), ("K2Node_CommutativeAssociativeBinaryOperator_1", "Add_DoubleDouble") }) Member(g, pair.Item1, pair.Item2);
        Edges(g, ("K2Node_CallFunction_28", "A", "K2Node_CallFunction_8", "ReturnValue"),
            ("K2Node_CallFunction_28", "B", "K2Node_CallFunction_30", "ReturnValue"),
            ("K2Node_CallFunction_2", "Value", "K2Node_CallFunction_28", "ReturnValue_Yaw"),
            ("K2Node_CallFunction_2", "Min", "K2Node_FunctionEntry_0", "AimYawMin"),
            ("K2Node_CallFunction_2", "Max", "K2Node_FunctionEntry_0", "AimYawMax"),
            ("K2Node_IfThenElse_2", "execute", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_IfThenElse_2", "Condition", "K2Node_CallFunction_2", "ReturnValue"),
            ("K2Node_CallFunction_3", "execute", "K2Node_IfThenElse_2", "else"),
            ("K2Node_CallFunction_3", "Target_Yaw", "K2Node_CallFunction_7", "ReturnValue"),
            ("K2Node_CallFunction_3", "ActorInterpSpeed(Smooth)", "K2Node_VariableGet_0", "InterpSpeed"),
            ("K2Node_CallFunction_6", "A", "K2Node_CallFunction_28", "ReturnValue_Yaw"),
            ("K2Node_CallFunction_7", "bPickA", "K2Node_CallFunction_6", "ReturnValue"),
            ("K2Node_CallFunction_7", "A", "K2Node_CommutativeAssociativeBinaryOperator_1", "ReturnValue"),
            ("K2Node_CallFunction_7", "B", "K2Node_CommutativeAssociativeBinaryOperator_0", "ReturnValue"),
            ("K2Node_CommutativeAssociativeBinaryOperator_1", "A", "K2Node_CallFunction_38", "ReturnValue_Yaw"),
            ("K2Node_CommutativeAssociativeBinaryOperator_1", "B", "K2Node_FunctionEntry_0", "AimYawMin"),
            ("K2Node_CommutativeAssociativeBinaryOperator_0", "A", "K2Node_CallFunction_38", "ReturnValue_Yaw"),
            ("K2Node_CommutativeAssociativeBinaryOperator_0", "B", "K2Node_FunctionEntry_0", "AimYawMax"));
        SmoothCall(g, "K2Node_CallFunction_3"); Literal(g, "K2Node_CallFunction_3", "TargetInterpSpeed(Const)", "0.0");
        Literal(g, "K2Node_CallFunction_6", "B", "0.0"); Literal(g, "K2Node_CallFunction_2", "InclusiveMin", "true"); Literal(g, "K2Node_CallFunction_2", "InclusiveMax", "true");
    }

    private static void SelfCall(Graph g, string node, string member) => g.Self(Member(g, node, member));
    private static void SmoothCall(Graph g, string node)
    { SelfCall(g, node, "SmoothCharacterRotation"); Literal(g, node, "Target_Pitch", "0.0"); Literal(g, node, "Target_Roll", "0.0"); }
    private static void Edges(Graph g, params (string Node, string Pin, string Source, string Output)[] links)
    { foreach (var link in links) Edge(g, link.Node, link.Pin, link.Source, link.Output); }
    private static void Switch(Graph g, string node, string name, string variable, string member)
    {
        Require(g.Named(node).Kind == "K2Node_SwitchEnum" && g.Named(node).Body.Contains("Enum=\"/Script/Engine.UserDefinedEnum'/Game/AdvancedLocomotionV4/Data/Enums/" + name + "." + name + "'\"", StringComparison.Ordinal), "Foreign rotation switch enum.");
        Variable(g, variable, member); Edge(g, node, "Selection", variable, member);
    }
    private static void ValidateEnums(JsonElement root)
    {
        foreach (var (name, entries) in new[] {
            ("ALS_RotationMode", "0:VelocityDirection,1:LookingDirection,3:Aiming"), ("ALS_Stance", "0:Standing,1:Crouching"),
            ("ALS_Gait", "0:Walking,1:Running,2:Sprinting"), ("ALS_ViewMode", "0:ThirdPerson,1:FirstPerson"),
            ("ALS_MovementAction", "0:LowMantle,1:HighMantle,2:Rolling,3:GettingUp,4:None"),
            ("ALS_MovementState", "0:None,1:Grounded,2:In Air,3:Ragdoll,4:Mantling") })
        {
            var path = "/Game/AdvancedLocomotionV4/Data/Enums/" + name + "." + name;
            var entry = root.GetProperty("enums").EnumerateArray().Single(e => Text(e, "path") == path);
            var names = Regex.Matches(Text(entry, "nativeText"), "\\(\"NewEnumerator([0-9]+)\", NSLOCTEXT\\(\"[^\"]*\", \"[^\"]*\", \"([^\"]+)\"\\)\\)")
                .Select(m => m.Groups[1].Value + ":" + m.Groups[2].Value);
            Require(string.Join(",", names) == entries, "Native rotation enum mapping changed.");
        }
    }
}
