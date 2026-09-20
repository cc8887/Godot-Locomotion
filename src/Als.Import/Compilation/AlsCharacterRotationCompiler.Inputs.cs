using System.Text.Json;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static partial class AlsCharacterRotationCompiler
{
    private static void ValidateMappedSpeed(JsonElement root)
    {
        var g = Graph(root, "GetMappedSpeed");
        Edges(g, ("K2Node_FunctionResult_0", "ReturnValue", "K2Node_Select_0", "ReturnValue"),
            ("K2Node_Select_0", "Index", "K2Node_CallFunction_4", "ReturnValue"),
            ("K2Node_Select_0", "Option 1", "K2Node_CallFunction_5", "ReturnValue"),
            ("K2Node_Select_0", "Option 0", "K2Node_Select_3", "ReturnValue"),
            ("K2Node_Select_3", "Index", "K2Node_CallFunction_2", "ReturnValue"),
            ("K2Node_Select_3", "Option 1", "K2Node_CallFunction_6", "ReturnValue"),
            ("K2Node_Select_3", "Option 0", "K2Node_CallFunction_10", "ReturnValue"),
            ("K2Node_VariableSet_0", "execute", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_VariableSet_1", "execute", "K2Node_VariableSet_0", "then"),
            ("K2Node_VariableSet_2", "execute", "K2Node_VariableSet_1", "then"),
            ("K2Node_FunctionResult_0", "execute", "K2Node_VariableSet_2", "then"),
            ("K2Node_BreakStruct_5", "MovementSettings", "K2Node_VariableGet_7", "CurrentMovementSettings"));
        Variable(g, "K2Node_VariableGet_7", "CurrentMovementSettings");
        foreach (var (setter, field) in new[] { ("K2Node_VariableSet_0", "WalkSpeed"), ("K2Node_VariableSet_1", "RunSpeed"), ("K2Node_VariableSet_2", "SprintSpeed") })
        {
            Member(g, setter, "Loc" + field);
            var (source, pin) = g.Follow(g.Named(setter), "Loc" + field);
            Require(source == g.Named("K2Node_BreakStruct_5") && pin.Name.StartsWith(field + "_", StringComparison.Ordinal), "Mapped speed is not read from current settings.");
        }
        foreach (var (node, speedNode, thresholdNode, threshold) in new[] {
            ("K2Node_CallFunction_2", "K2Node_VariableGet_6", "K2Node_VariableGet_8", "LocWalkSpeed"),
            ("K2Node_CallFunction_4", "K2Node_VariableGet_3", "K2Node_VariableGet_9", "LocRunSpeed") })
        {
            Member(g, node, "Greater_DoubleDouble"); Member(g, speedNode, "Speed"); Member(g, thresholdNode, threshold);
            Edge(g, node, "A", speedNode, "Speed"); Edge(g, node, "B", thresholdNode, threshold);
        }
        foreach (var (node, from, to, lowNode, lowName, highNode, highName) in new[] {
            ("K2Node_CallFunction_10", "0.0", "1.000000", "", "", "K2Node_VariableGet_13", "LocWalkSpeed"),
            ("K2Node_CallFunction_6", "1.000000", "2.000000", "K2Node_VariableGet_10", "LocWalkSpeed", "K2Node_VariableGet_14", "LocRunSpeed"),
            ("K2Node_CallFunction_5", "2.000000", "3.000000", "K2Node_VariableGet_11", "LocRunSpeed", "K2Node_VariableGet_12", "LocSprintSpeed") })
        {
            Member(g, node, "MapRangeClamped"); Edge(g, node, "Value", "K2Node_VariableGet_5", "Speed");
            Literal(g, node, "OutRangeA", from); Literal(g, node, "OutRangeB", to);
            if (lowNode == "") Literal(g, node, "InRangeA", "0.0");
            else { Member(g, lowNode, lowName); Edge(g, node, "InRangeA", lowNode, lowName); }
            Member(g, highNode, highName); Edge(g, node, "InRangeB", highNode, highName);
        }
        Variable(g, "K2Node_VariableGet_5", "Speed");
    }

    private static void ValidateMovementSelection(JsonElement root)
    {
        var g = Graph(root, "GetTargetMovementSettings");
        Switch(g, "K2Node_SwitchEnum_0", "ALS_RotationMode", "K2Node_VariableGet_2", "RotationMode");
        Edge(g, "K2Node_SwitchEnum_0", "execute", "K2Node_FunctionEntry_0", "then");
        foreach (var (selector, variable, pin, mode, standing, crouching) in new[] {
            ("K2Node_SwitchEnum_1", "K2Node_VariableGet_1", "NewEnumerator0", "VelocityDirection", "K2Node_FunctionResult_6", "K2Node_FunctionResult_5"),
            ("K2Node_SwitchEnum_2", "K2Node_VariableGet_3", "NewEnumerator1", "LookingDirection", "K2Node_FunctionResult_2", "K2Node_FunctionResult_4"),
            ("K2Node_SwitchEnum_3", "K2Node_VariableGet_4", "NewEnumerator3", "Aiming", "K2Node_FunctionResult_0", "K2Node_FunctionResult_3") })
        {
            Switch(g, selector, "ALS_Stance", variable, "Stance"); Edge(g, selector, "execute", "K2Node_SwitchEnum_0", pin);
            foreach (var (result, stance, stancePin) in new[] { (standing, "Standing", "NewEnumerator0"), (crouching, "Crouching", "NewEnumerator1") })
            {
                Edge(g, result, "execute", selector, stancePin);
                var (source, output) = g.Follow(g.Named(result), "MovementSettings");
                Require(source.Member == "MovementData" && output.Name.StartsWith("MovementData_" + mode + "_", StringComparison.Ordinal) &&
                    output.Name.Contains("_" + stance + "_", StringComparison.Ordinal), "Wrong stance/mode movement settings.");
                g.Self(source);
            }
        }
        var binding = Graph(root, "SetMovementModel", true);
        Member(binding, "K2Node_VariableSet_1", "MovementData"); Variable(binding, "K2Node_VariableGet_6", "MovementModel");
        Edges(binding, ("K2Node_GetDataTableRow_0", "execute", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_GetDataTableRow_0", "RowName", "K2Node_VariableGet_6", "MovementModel_RowName"),
            ("K2Node_GetDataTableRow_0", "DataTable", "K2Node_VariableGet_6", "MovementModel_DataTable"),
            ("K2Node_VariableSet_1", "execute", "K2Node_GetDataTableRow_0", "then"),
            ("K2Node_VariableSet_1", "MovementData", "K2Node_GetDataTableRow_0", "ReturnValue"));
    }

    private static void ValidateHistory(JsonElement root)
    {
        ValidateEssentialRotationInputs(root);
        ValidateActualGaitAndMovementOrder(root);
        var begin = Graph(root, "On Begin Play", true);
        SelfCall(begin, "K2Node_CallFunction_2", "OnGaitChanged");
        Variable(begin, "K2Node_VariableGet_2", "DesiredGait");
        Edge(begin, "K2Node_CallFunction_2", "NewActualGait", "K2Node_VariableGet_2", "DesiredGait");
        Edge(begin, "K2Node_CallFunction_2", "execute", "K2Node_ExecutionSequence_0", "then_3");
        SequenceEntry(begin, "K2Node_ExecutionSequence_0");
        ReroutedEdge(begin, "K2Node_VariableSet_5", "execute", "K2Node_ExecutionSequence_0", "then_4");
        Member(begin, "K2Node_CallFunction_9", "K2_GetActorRotation");
        foreach (var (node, name) in new[] { ("K2Node_VariableSet_5", "TargetRotation"), ("K2Node_VariableSet_0", "LastVelocityRotation"), ("K2Node_VariableSet_1", "LastMovementInputRotation") })
        { Member(begin, node, name); Edge(begin, node, name, "K2Node_CallFunction_9", "ReturnValue"); }
        Edge(begin, "K2Node_VariableSet_0", "execute", "K2Node_VariableSet_5", "then");
        Edge(begin, "K2Node_VariableSet_1", "execute", "K2Node_VariableSet_0", "then");
        Require(Text(root, "defaultsText").Contains("InAirRotation=(Pitch=0.000000,Yaw=0.000000,Roll=0.000000)", StringComparison.Ordinal), "Changed default air rotation.");
        var changed = Graph(root, "OnMovementStateChanged");
        Switch(changed, "K2Node_SwitchEnum_0", "ALS_MovementState", "K2Node_VariableGet_3", "MovementState");
        var assignment = changed.Named("K2Node_MacroInstance_1");
        Require(assignment.Kind == "K2Node_MacroInstance" && assignment.Body.Contains(
            "MacroGraph=\"/Script/Engine.EdGraph'/Game/AdvancedLocomotionV4/Blueprints/Libraries/ALS_MacroLibrary.ALS_MacroLibrary:ML_SetPreviousAndNewValue'\"",
            StringComparison.Ordinal), "Movement-state event uses a different assignment macro.");
        Variable(changed, "K2Node_VariableGet_1", "MovementState");
        ScopedVariable(changed, "K2Node_VariableGet_0", "PreviousMovementState", "OnMovementStateChanged");
        Edges(changed, ("K2Node_MacroInstance_1", "execute", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_MacroInstance_1", "NewValue", "K2Node_FunctionEntry_0", "NewMovementState"),
            ("K2Node_MacroInstance_1", "NewTarget", "K2Node_VariableGet_1", "MovementState"),
            ("K2Node_MacroInstance_1", "PreviousTarget", "K2Node_VariableGet_0", "PreviousMovementState"),
            ("K2Node_SwitchEnum_0", "execute", "K2Node_MacroInstance_1", "then"));
        Member(changed, "K2Node_VariableSet_0", "InAirRotation"); Member(changed, "K2Node_CallFunction_0", "K2_GetActorRotation");
        Switch(changed, "K2Node_SwitchEnum_2", "ALS_MovementAction", "K2Node_VariableGet_4", "MovementAction");
        Edges(changed, ("K2Node_SwitchEnum_2", "execute", "K2Node_SwitchEnum_0", "NewEnumerator2"),
            ("K2Node_VariableSet_0", "execute", "K2Node_SwitchEnum_2", "NewEnumerator4"),
            ("K2Node_VariableSet_0", "InAirRotation", "K2Node_CallFunction_0", "ReturnValue"));
        var values = Graph(root, "SetEssentialValues");
        foreach (var (setter, field, branch, read, member) in new[] {
            ("K2Node_VariableSet_4", "LastVelocityRotation", "K2Node_IfThenElse_1", "K2Node_CallFunction_11", "K2Node_CallFunction_4"),
            ("K2Node_VariableSet_6", "LastMovementInputRotation", "K2Node_IfThenElse_0", "K2Node_CallFunction_18", "K2Node_CallFunction_24") })
        {
            Member(values, setter, field); Member(values, read, "Conv_VectorToRotator");
            Member(values, member, field == "LastVelocityRotation" ? "GetVelocity" : "GetCurrentAcceleration");
            Edges(values, (setter, "execute", branch, "then"), (setter, field, read, "ReturnValue"), (read, "InVec", member, "ReturnValue"));
        }
        Edges(values, ("K2Node_IfThenElse_1", "Condition", "K2Node_VariableSet_3", "Output_Get"),
            ("K2Node_IfThenElse_0", "Condition", "K2Node_VariableSet_2", "Output_Get"));
        Member(values, "K2Node_VariableSet_3", "IsMoving"); Member(values, "K2Node_VariableSet_2", "HasMovementInput");
        var tick = Graph(root, "TickGraph");
        Switch(tick, "K2Node_SwitchEnum_2", "ALS_MovementState", "K2Node_VariableGet_38", "MovementState");
        SelfCall(tick, "K2Node_CallFunction_1", "CacheValues");
        Edge(tick, "K2Node_CallFunction_1", "execute", "K2Node_ExecutionSequence_0", "then_1");
        foreach (var (node, member) in new[] { ("K2Node_CallFunction_10", "SetEssentialValues"), ("K2Node_CallFunction_4", "UpdateCharacterMovement"),
            ("K2Node_CallFunction_3", "UpdateGroudedRotation"), ("K2Node_CallFunction_0", "UpdateInAirRotation") }) SelfCall(tick, node, member);
        Edges(tick, ("K2Node_CallFunction_10", "execute", "K2Node_ExecutionSequence_0", "then_0"),
            ("K2Node_SwitchEnum_2", "execute", "K2Node_CallFunction_10", "then"),
            ("K2Node_CallFunction_4", "execute", "K2Node_SwitchEnum_2", "NewEnumerator1"),
            ("K2Node_CallFunction_3", "execute", "K2Node_CallFunction_4", "then"),
            ("K2Node_CallFunction_0", "execute", "K2Node_SwitchEnum_2", "NewEnumerator2"));
        var cache = Graph(root, "CacheValues");
        SequenceEntry(cache, "K2Node_ExecutionSequence_0");
        SelfCall(cache, "K2Node_CallFunction_1", "GetControlRotation");
        cache.Self(Member(cache, "K2Node_VariableSet_2", "PreviousAimYaw"));
        Edge(cache, "K2Node_VariableSet_2", "PreviousAimYaw", "K2Node_CallFunction_1", "ReturnValue_Yaw");
        ReroutedEdge(cache, "K2Node_VariableSet_2", "execute", "K2Node_ExecutionSequence_0", "then_1");
        Require(Text(root, "defaultsText").Contains("PreviousAimYaw=0.000000", StringComparison.Ordinal),
            "Changed initial control-yaw history.");
    }

    private static void ValidateEssentialRotationInputs(JsonElement root)
    {
        var g = Graph(root, "SetEssentialValues");
        SequenceEntry(g, "K2Node_ExecutionSequence_0");
        foreach (var (setter, field, sequencePin) in new[] {
            ("K2Node_VariableSet_8", "Speed", "then_1"),
            ("K2Node_VariableSet_19", "MovementInputAmount", "then_2"),
            ("K2Node_VariableSet_11", "AimYawRate", "then_3") })
        {
            g.Self(Member(g, setter, field));
            ReroutedEdge(g, setter, "execute", "K2Node_ExecutionSequence_0", sequencePin);
        }
        foreach (var (node, function) in new[] {
            ("K2Node_CallFunction_5", "VSize"), ("K2Node_CallFunction_26", "VSize"),
            ("K2Node_CallFunction_67", "Divide_DoubleDouble"), ("K2Node_CallFunction_22", "Greater_DoubleDouble"),
            ("K2Node_CallFunction_21", "Abs"), ("K2Node_CallFunction_48", "Divide_DoubleDouble"),
            ("K2Node_CallFunction_46", "Subtract_DoubleDouble") })
            g.Function(g.Named(node), function, "Engine.KismetMathLibrary");
        g.Function(g.Named("K2Node_CallFunction_3"), "GetWorldDeltaSeconds", "Engine.GameplayStatics");
        foreach (var (node, function) in new[] { ("K2Node_CallFunction_9", "GetVelocity"),
            ("K2Node_CallFunction_4", "GetVelocity"), ("K2Node_CallFunction_42", "GetControlRotation") }) SelfCall(g, node, function);
        foreach (var (node, function, variable) in new[] {
            ("K2Node_CallFunction_62", "GetCurrentAcceleration", "K2Node_VariableGet_10"),
            ("K2Node_CallFunction_63", "GetMaxAcceleration", "K2Node_VariableGet_10"),
            ("K2Node_CallFunction_24", "GetCurrentAcceleration", "K2Node_VariableGet_0") })
        {
            g.Function(g.Named(node), function, "Engine.CharacterMovementComponent");
            Variable(g, variable, "CharacterMovement"); Edge(g, node, "self", variable, "CharacterMovement");
        }
        Variable(g, "K2Node_VariableGet_12", "PreviousAimYaw");
        Edges(g,
            ("K2Node_VariableSet_8", "Speed", "K2Node_CallFunction_5", "ReturnValue"),
            ("K2Node_CallFunction_5", "A_X", "K2Node_CallFunction_9", "ReturnValue_X"),
            ("K2Node_CallFunction_5", "A_Y", "K2Node_CallFunction_9", "ReturnValue_Y"),
            ("K2Node_VariableSet_3", "execute", "K2Node_VariableSet_8", "then"),
            ("K2Node_IfThenElse_1", "execute", "K2Node_VariableSet_3", "then"),
            ("K2Node_VariableSet_19", "MovementInputAmount", "K2Node_CallFunction_67", "ReturnValue"),
            ("K2Node_CallFunction_67", "A", "K2Node_CallFunction_26", "ReturnValue"),
            ("K2Node_CallFunction_67", "B", "K2Node_CallFunction_63", "ReturnValue"),
            ("K2Node_CallFunction_26", "A", "K2Node_CallFunction_62", "ReturnValue"),
            ("K2Node_VariableSet_2", "execute", "K2Node_VariableSet_19", "then"),
            ("K2Node_VariableSet_2", "HasMovementInput", "K2Node_CallFunction_22", "ReturnValue"),
            ("K2Node_CallFunction_22", "A", "K2Node_VariableSet_19", "Output_Get"),
            ("K2Node_IfThenElse_0", "execute", "K2Node_VariableSet_2", "then"),
            ("K2Node_VariableSet_11", "AimYawRate", "K2Node_CallFunction_21", "ReturnValue"),
            ("K2Node_CallFunction_21", "A", "K2Node_CallFunction_48", "ReturnValue"),
            ("K2Node_CallFunction_48", "A", "K2Node_CallFunction_46", "ReturnValue"),
            ("K2Node_CallFunction_48", "B", "K2Node_CallFunction_3", "ReturnValue"),
            ("K2Node_CallFunction_46", "A", "K2Node_CallFunction_42", "ReturnValue_Yaw"),
            ("K2Node_CallFunction_46", "B", "K2Node_VariableGet_12", "PreviousAimYaw"));
        Literal(g, "K2Node_CallFunction_5", "A_Z", "0.0");
        Literal(g, "K2Node_CallFunction_22", "B", "0.0");
    }

    private static void ValidateActualGaitAndMovementOrder(JsonElement root)
    {
        var g = Graph(root, "GetActualGait");
        Variable(g, "K2Node_VariableGet_5", "CurrentMovementSettings");
        Edge(g, "K2Node_BreakStruct_1", "MovementSettings", "K2Node_VariableGet_5", "CurrentMovementSettings");
        foreach (var (setter, field, predecessor, pin) in new[] {
            ("K2Node_VariableSet_1", "WalkSpeed", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_VariableSet_2", "RunSpeed", "K2Node_VariableSet_1", "then"),
            ("K2Node_VariableSet_0", "SprintSpeed", "K2Node_VariableSet_2", "then") })
        {
            ScopedVariable(g, setter, "Local" + field, "GetActualGait", setter: true);
            Edge(g, setter, "execute", predecessor, pin);
            var (source, output) = g.Follow(g.Named(setter), "Local" + field);
            Require(source == g.Named("K2Node_BreakStruct_1") && output.Name.StartsWith(field + "_", StringComparison.Ordinal),
                "Actual gait does not read the current movement speed settings.");
        }
        foreach (var (comparison, speed, addition, threshold, field) in new[] {
            ("K2Node_CallFunction_0", "K2Node_VariableGet_1", "K2Node_CommutativeAssociativeBinaryOperator_2", "K2Node_VariableGet_2", "LocalRunSpeed"),
            ("K2Node_CallFunction_6", "K2Node_VariableGet_6", "K2Node_CommutativeAssociativeBinaryOperator_0", "K2Node_VariableGet_7", "LocalWalkSpeed") })
        {
            g.Function(g.Named(comparison), "GreaterEqual_DoubleDouble", "Engine.KismetMathLibrary");
            Member(g, addition, "Add_DoubleDouble");
            Variable(g, speed, "Speed"); ScopedVariable(g, threshold, field, "GetActualGait");
            Edge(g, comparison, "A", speed, "Speed"); Edge(g, comparison, "B", addition, "ReturnValue");
            Edge(g, addition, "A", threshold, field); Literal(g, addition, "B", "10.000000");
        }
        // AllowedGait is a function parameter, not a character member. Keep its
        // local owner while validating the actual enum selection and result paths.
        LocalGaitSwitch(g, "K2Node_SwitchEnum_0", "K2Node_VariableGet_3", "AllowedGait", "GetActualGait");
        Edges(g, ("K2Node_IfThenElse_0", "execute", "K2Node_VariableSet_0", "then"),
            ("K2Node_IfThenElse_0", "Condition", "K2Node_CallFunction_0", "ReturnValue"),
            ("K2Node_IfThenElse_2", "execute", "K2Node_IfThenElse_0", "else"),
            ("K2Node_IfThenElse_2", "Condition", "K2Node_CallFunction_6", "ReturnValue"),
            ("K2Node_SwitchEnum_0", "execute", "K2Node_IfThenElse_0", "then"),
            ("K2Node_FunctionResult_0", "execute", "K2Node_IfThenElse_2", "else"),
            ("K2Node_FunctionResult_4", "execute", "K2Node_IfThenElse_2", "then"),
            ("K2Node_FunctionResult_1", "execute", "K2Node_SwitchEnum_0", "NewEnumerator2"));
        g.Links(g.Named("K2Node_FunctionResult_5"), "execute", (g.Named("K2Node_SwitchEnum_0"), "NewEnumerator0"),
            (g.Named("K2Node_SwitchEnum_0"), "NewEnumerator1"));
        foreach (var (node, result) in new[] { ("K2Node_FunctionResult_0", "NewEnumerator0"),
            ("K2Node_FunctionResult_4", "NewEnumerator1"), ("K2Node_FunctionResult_5", "NewEnumerator1"),
            ("K2Node_FunctionResult_1", "NewEnumerator2") }) Literal(g, node, "ActualGait", result);

        var update = Graph(root, "UpdateCharacterMovement");
        SequenceEntry(update, "K2Node_ExecutionSequence_0");
        foreach (var (node, field, sequence) in new[] { ("K2Node_VariableSet_0", "AllowedGait", "then_0"),
            ("K2Node_VariableSet_4", "ActualGait", "then_1") })
        {
            ScopedVariable(update, node, field, "UpdateCharacterMovement", setter: true);
            ReroutedEdge(update, node, "execute", "K2Node_ExecutionSequence_0", sequence);
        }
        foreach (var (node, member) in new[] { ("K2Node_CallFunction_1", "GetAllowedGait"),
            ("K2Node_CallFunction_3", "GetActualGait"), ("K2Node_CallFunction_0", "BPI_Set_Gait"),
            ("K2Node_CallFunction_6", "UpdateDynamicMovementSettings") }) SelfCall(update, node, member);
        foreach (var (node, field) in new[] { ("K2Node_VariableGet_7", "AllowedGait"), ("K2Node_VariableGet_8", "AllowedGait"),
            ("K2Node_VariableGet_4", "ActualGait"), ("K2Node_VariableGet_5", "ActualGait") })
            ScopedVariable(update, node, field, "UpdateCharacterMovement");
        Variable(update, "K2Node_VariableGet_3", "Gait");
        Require(update.Named("K2Node_EnumInequality_0").Kind == "K2Node_EnumInequality", "Actual gait comparison changed.");
        ReroutedEdge(update, "K2Node_CallFunction_6", "execute", "K2Node_ExecutionSequence_0", "then_2");
        Edges(update, ("K2Node_VariableSet_0", "AllowedGait", "K2Node_CallFunction_1", "AllowedGait"),
            ("K2Node_VariableSet_4", "ActualGait", "K2Node_CallFunction_3", "ActualGait"),
            ("K2Node_CallFunction_3", "AllowedGait", "K2Node_VariableGet_7", "AllowedGait"),
            ("K2Node_CallFunction_6", "AllowedGait", "K2Node_VariableGet_8", "AllowedGait"),
            ("K2Node_IfThenElse_0", "execute", "K2Node_VariableSet_4", "then"),
            ("K2Node_IfThenElse_0", "Condition", "K2Node_EnumInequality_0", "ReturnValue"),
            ("K2Node_EnumInequality_0", "A", "K2Node_VariableGet_5", "ActualGait"),
            ("K2Node_EnumInequality_0", "B", "K2Node_VariableGet_3", "Gait"),
            ("K2Node_CallFunction_0", "execute", "K2Node_IfThenElse_0", "then"),
            ("K2Node_CallFunction_0", "NewGait", "K2Node_VariableGet_4", "ActualGait"));
        // This is the update invoked by sequence then_2, after GetActualGait.
        // Verify the real assignment, without claiming that this compiler ports
        // the separate acceleration, braking and friction consumers below it.
        var dynamic = Graph(root, "UpdateDynamicMovementSettings");
        SelfCall(dynamic, "K2Node_CallFunction_0", "GetTargetMovementSettings");
        dynamic.Self(Member(dynamic, "K2Node_VariableSet_5", "CurrentMovementSettings"));
        Edges(dynamic, ("K2Node_VariableSet_5", "execute", "K2Node_FunctionEntry_0", "then"),
            ("K2Node_VariableSet_5", "CurrentMovementSettings", "K2Node_CallFunction_0", "MovementSettings"),
            ("K2Node_ExecutionSequence_0", "execute", "K2Node_VariableSet_5", "then"));
    }

    private static void SequenceEntry(Graph graph, string node)
    {
        Require(graph.Named(node).Kind == "K2Node_ExecutionSequence", "Character input update is not an execution sequence.");
        Edge(graph, node, "execute", "K2Node_FunctionEntry_0", "then");
    }
    private static void ReroutedEdge(Graph graph, string node, string input, string source, string output)
    {
        var (other, pin) = graph.FollowReroutes(graph.Named(node), input);
        Require(other == graph.Named(source) && pin.Name == output, "Character input execution order changed.");
    }
    private static void ScopedVariable(Graph graph, string node, string member, string scope, bool setter = false)
    {
        var value = Member(graph, node, member);
        Require(value.Kind == (setter ? "K2Node_VariableSet" : "K2Node_VariableGet") &&
            value.Body.Contains("MemberScope=\"" + scope + "\"", StringComparison.Ordinal), "Character input local variable owner changed.");
    }
    private static void LocalGaitSwitch(Graph graph, string node, string variable, string member, string scope)
    {
        var value = graph.Named(node);
        Require(value.Kind == "K2Node_SwitchEnum" && value.Body.Contains(
            "Enum=\"/Script/Engine.UserDefinedEnum'/Game/AdvancedLocomotionV4/Data/Enums/ALS_Gait.ALS_Gait'\"", StringComparison.Ordinal),
            "Actual gait switch enum changed.");
        ScopedVariable(graph, variable, member, scope); Edge(graph, node, "Selection", variable, member);
    }
}
