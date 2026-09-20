using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsFootIkInputCompiler
{
    public static AlsFootIkInputModel Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var pelvis = AlsPelvisIkInputCompiler.Compile(json);
        var threshold = CompileLock(new(root, "SetFootLocking"));
        CompileLockOffsets(new(root, "SetFootLockOffsets"));
        var reset = CompileReset(new(root, "ResetIKOffsets"));
        var offset = CompileOffsets(new(root, "SetFootOffsets"));
        CompileUpdate(new(root, "UpdateFootIK"));
        var defaults = new G(root, "UpdateFootIK");
        return new(new(threshold), offset, new(reset), pelvis, new(
            new(defaults.Default("FootLock_L_Alpha"), defaults.VectorDefault("FootLock_L_Location"), defaults.RotationDefault("FootLock_L_Rotation")),
            new(defaults.Default("FootLock_R_Alpha"), defaults.VectorDefault("FootLock_R_Location"), defaults.RotationDefault("FootLock_R_Rotation")),
            new(defaults.VectorDefault("FootOffset_L_Location"), defaults.RotationDefault("FootOffset_L_Rotation")),
            new(defaults.VectorDefault("FootOffset_R_Location"), defaults.RotationDefault("FootOffset_R_Rotation")), pelvis.InitialState));
    }

    private static double CompileLock(G g)
    {
        g.Member("K2Node_FunctionEntry_0", "K2Node_FunctionEntry", "SetFootLocking");
        g.Flow("K2Node_IfThenElse_2", "K2Node_FunctionEntry_0", "then");
        g.Math("K2Node_CallFunction_2", "Greater_DoubleDouble"); g.Literal("K2Node_CallFunction_2", "B", "0.0");
        g.Link("K2Node_IfThenElse_2", "Condition", "K2Node_CallFunction_2", "ReturnValue");
        g.Member("K2Node_CallFunction_9", "K2Node_CallFunction", "GetCurveValue"); g.Self("K2Node_CallFunction_9");
        g.Link("K2Node_CallFunction_2", "A", "K2Node_CallFunction_9", "ReturnValue");
        g.Link("K2Node_CallFunction_9", "CurveName", "K2Node_FunctionEntry_0", "Enable_FootIK_Curve");
        g.Sequence("K2Node_ExecutionSequence_1", 4); g.Flow("K2Node_ExecutionSequence_1", "K2Node_IfThenElse_2", "then");
        g.Flow("K2Node_VariableSet_6", "K2Node_ExecutionSequence_1", "then_0");
        g.Variable("K2Node_VariableSet_6", "FootLockCurveValue", true);
        g.Member("K2Node_CallFunction_6", "K2Node_CallFunction", "GetCurveValue"); g.Self("K2Node_CallFunction_6");
        g.Read("K2Node_CallFunction_6", "CurveName", "K2Node_VariableGet_1", "FootLockCurve");
        g.Link("K2Node_VariableSet_6", "FootLockCurveValue", "K2Node_CallFunction_6", "ReturnValue");
        g.Flow("K2Node_IfThenElse_0", "K2Node_ExecutionSequence_1", "then_1");
        g.Operator("K2Node_CommutativeAssociativeBinaryOperator_0", "BooleanOR");
        g.Link("K2Node_IfThenElse_0", "Condition", "K2Node_CommutativeAssociativeBinaryOperator_0", "ReturnValue");
        g.Link("K2Node_CommutativeAssociativeBinaryOperator_0", "A", "K2Node_CallFunction_7", "ReturnValue");
        g.Link("K2Node_CommutativeAssociativeBinaryOperator_0", "B", "K2Node_CallFunction_8", "ReturnValue");
        g.Math("K2Node_CallFunction_7", "GreaterEqual_DoubleDouble"); g.Math("K2Node_CallFunction_8", "Less_DoubleDouble");
        g.Read("K2Node_CallFunction_7", "A", "K2Node_VariableGet_9", "FootLockCurveValue");
        g.Read("K2Node_CallFunction_8", "A", "K2Node_VariableGet_9", "FootLockCurveValue");
        g.Read("K2Node_CallFunction_8", "B", "K2Node_VariableGet_8", "CurrentFootLockAlpha");
        var threshold = g.Number("K2Node_CallFunction_7", "B");
        g.Flow("K2Node_VariableSetRef_0", "K2Node_IfThenElse_0", "then");
        g.Reference("K2Node_VariableSetRef_0", "K2Node_VariableGet_10", "CurrentFootLockAlpha");
        g.Read("K2Node_VariableSetRef_0", "Value", "K2Node_VariableGet_4", "FootLockCurveValue");
        g.Flow("K2Node_IfThenElse_7", "K2Node_ExecutionSequence_1", "then_2");
        g.Math("K2Node_CallFunction_14", "GreaterEqual_DoubleDouble");
        Require(g.Number("K2Node_CallFunction_14", "B") == threshold, "Foot capture/update thresholds differ.");
        g.Link("K2Node_IfThenElse_7", "Condition", "K2Node_CallFunction_14", "ReturnValue");
        g.Read("K2Node_CallFunction_14", "A", "K2Node_VariableGet_16", "CurrentFootLockAlpha");
        g.Flow("K2Node_VariableSetRef_2", "K2Node_IfThenElse_7", "then");
        g.Flow("K2Node_VariableSetRef_1", "K2Node_VariableSetRef_2", "then");
        g.Reference("K2Node_VariableSetRef_2", "K2Node_VariableGet_17", "CurrentFootLockLocation");
        g.Reference("K2Node_VariableSetRef_1", "K2Node_VariableGet_18", "CurrentFootLockRotation");
        g.Link("K2Node_VariableSetRef_2", "Value", "K2Node_CallFunction_11", "ReturnValue_Location");
        g.Link("K2Node_VariableSetRef_1", "Value", "K2Node_CallFunction_11", "ReturnValue_Rotation");
        g.Member("K2Node_CallFunction_11", "K2Node_CallFunction", "GetSocketTransform");
        g.Member("K2Node_CallFunction_10", "K2Node_CallFunction", "GetOwningComponent"); g.Self("K2Node_CallFunction_10");
        g.Link("K2Node_CallFunction_11", "self", "K2Node_CallFunction_10", "ReturnValue");
        g.Read("K2Node_CallFunction_11", "InSocketName", "K2Node_VariableGet_15", "IKFootBone");
        g.Literal("K2Node_CallFunction_11", "TransformSpace", "RTS_Component");
        g.Flow("K2Node_IfThenElse_5", "K2Node_ExecutionSequence_1", "then_3");
        g.Math("K2Node_CallFunction_15", "Greater_DoubleDouble"); g.Literal("K2Node_CallFunction_15", "B", "0.0");
        g.Link("K2Node_IfThenElse_5", "Condition", "K2Node_CallFunction_15", "ReturnValue");
        g.Read("K2Node_CallFunction_15", "A", "K2Node_VariableGet_7", "CurrentFootLockAlpha");
        g.Member("K2Node_CallFunction_13", "K2Node_CallFunction", "SetFootLockOffsets"); g.Self("K2Node_CallFunction_13");
        g.Flow("K2Node_CallFunction_13", "K2Node_IfThenElse_5", "then");
        g.Read("K2Node_CallFunction_13", "LocalLocation", "K2Node_VariableGet_12", "CurrentFootLockLocation");
        g.Read("K2Node_CallFunction_13", "LocalRotation", "K2Node_VariableGet_13", "CurrentFootLockRotation");
        return threshold;
    }

    private static void CompileLockOffsets(G g)
    {
        g.LocalZero("RotationDifference");
        g.Sequence("K2Node_ExecutionSequence_0", 4); g.Flow("K2Node_ExecutionSequence_0", "K2Node_FunctionEntry_0", "then");
        g.Flow("K2Node_IfThenElse_0", "K2Node_ExecutionSequence_0", "then_0");
        g.Member("K2Node_CallFunction_0", "K2Node_CallFunction", "IsMovingOnGround");
        g.Link("K2Node_IfThenElse_0", "Condition", "K2Node_CallFunction_0", "ReturnValue");
        g.Flow("K2Node_VariableSet_0", "K2Node_IfThenElse_0", "then"); g.Variable("K2Node_VariableSet_0", "RotationDifference", true);
        g.Math("K2Node_CallFunction_2", "NormalizedDeltaRotator");
        g.Link("K2Node_VariableSet_0", "RotationDifference", "K2Node_CallFunction_2", "ReturnValue");
        g.Member("K2Node_CallFunction_3", "K2Node_CallFunction", "K2_GetActorRotation");
        g.Member("K2Node_CallFunction_1", "K2Node_CallFunction", "GetLastUpdateRotation");
        g.Link("K2Node_CallFunction_2", "A", "K2Node_CallFunction_3", "ReturnValue");
        g.Link("K2Node_CallFunction_2", "B", "K2Node_CallFunction_1", "ReturnValue");
        g.Flow("K2Node_VariableSet_1", "K2Node_ExecutionSequence_0", "then_1"); g.Variable("K2Node_VariableSet_1", "LocationDifference", true);
        g.Math("K2Node_CallFunction_6", "LessLess_VectorRotator"); g.Math("K2Node_CallFunction_4", "Multiply_VectorFloat");
        g.Link("K2Node_VariableSet_1", "LocationDifference", "K2Node_CallFunction_6", "ReturnValue");
        g.Link("K2Node_CallFunction_6", "A", "K2Node_CallFunction_4", "ReturnValue");
        g.Read("K2Node_CallFunction_4", "A", "K2Node_VariableGet_5", "Velocity", global: true);
        g.Member("K2Node_CallFunction_5", "K2Node_CallFunction", "GetWorldDeltaSeconds");
        g.Link("K2Node_CallFunction_4", "B", "K2Node_CallFunction_5", "ReturnValue");
        g.Member("K2Node_CallFunction_8", "K2Node_CallFunction", "K2_GetComponentRotation");
        g.Member("K2Node_CallFunction_7", "K2Node_CallFunction", "GetOwningComponent"); g.Self("K2Node_CallFunction_7");
        g.Link("K2Node_CallFunction_6", "B", "K2Node_CallFunction_8", "ReturnValue");
        g.Link("K2Node_CallFunction_8", "self", "K2Node_CallFunction_7", "ReturnValue");
        g.Flow("K2Node_VariableSetRef_0", "K2Node_ExecutionSequence_0", "then_2");
        g.Reference("K2Node_VariableSetRef_0", "K2Node_VariableGet_6", "LocalLocation");
        g.Math("K2Node_CallFunction_9", "Subtract_VectorVector"); g.Math("K2Node_CallFunction_10", "RotateAngleAxis");
        g.Link("K2Node_VariableSetRef_0", "Value", "K2Node_CallFunction_10", "ReturnValue");
        g.Link("K2Node_CallFunction_10", "InVect", "K2Node_CallFunction_9", "ReturnValue");
        g.Read("K2Node_CallFunction_9", "A", "K2Node_VariableGet_9", "LocalLocation");
        g.Read("K2Node_CallFunction_9", "B", "K2Node_VariableGet_7", "LocationDifference");
        g.Read("K2Node_CallFunction_10", "AngleDeg", "K2Node_VariableGet_8", "RotationDifference", "RotationDifference_Yaw");
        g.Literal("K2Node_CallFunction_10", "Axis", "0, 0,-1.000000");
        g.Flow("K2Node_VariableSetRef_1", "K2Node_ExecutionSequence_0", "then_3");
        g.Reference("K2Node_VariableSetRef_1", "K2Node_VariableGet_11", "LocalRotation");
        g.Math("K2Node_CallFunction_12", "NormalizedDeltaRotator");
        g.Link("K2Node_VariableSetRef_1", "Value", "K2Node_CallFunction_12", "ReturnValue");
        g.Read("K2Node_CallFunction_12", "A", "K2Node_VariableGet_12", "LocalRotation");
        g.Read("K2Node_CallFunction_12", "B", "K2Node_VariableGet_10", "RotationDifference");
    }

    private static float CompileReset(G g)
    {
        g.Sequence("K2Node_ExecutionSequence_0", 2); g.Flow("K2Node_ExecutionSequence_0", "K2Node_FunctionEntry_0", "then");
        g.Flow("K2Node_VariableSet_4", "K2Node_ExecutionSequence_0", "then_0");
        g.Flow("K2Node_VariableSet_0", "K2Node_VariableSet_4", "then");
        g.Flow("K2Node_VariableSet_6", "K2Node_ExecutionSequence_0", "then_1");
        g.Flow("K2Node_VariableSet_5", "K2Node_VariableSet_6", "then");
        var speed = Reset("K2Node_VariableSet_4", "FootOffset_L_Location", "K2Node_CallFunction_0", "VInterpTo", "K2Node_VariableGet_2");
        Require(Reset("K2Node_VariableSet_0", "FootLock_R_Location", "K2Node_CallFunction_25", "VInterpTo", "K2Node_VariableGet_31") == speed &&
            Reset("K2Node_VariableSet_6", "FootOffset_L_Rotation", "K2Node_CallFunction_2", "RInterpTo", "K2Node_VariableGet_3") == speed &&
            Reset("K2Node_VariableSet_5", "FootOffset_L_Rotation", "K2Node_CallFunction_16", "RInterpTo", "K2Node_VariableGet_11") == speed,
            "Changed original asymmetric IK reset rates.");
        return speed;
        float Reset(string setter, string property, string call, string function, string delta)
        {
            g.Variable(setter, property, true, true); g.Math(call, function);
            g.Link(setter, property, call, "ReturnValue"); g.Link(call, "Current", setter, "Output_Get");
            g.Literal(call, "Target", "0, 0, 0"); g.Read(call, "DeltaTime", delta, "DeltaTimeX", global: true);
            return (float)g.Number(call, "InterpSpeed");
        }
    }

    private static AlsFootOffsetInputModel CompileOffsets(G g)
    {
        g.LocalZero("TargetRotationOffset");
        g.Flow("K2Node_IfThenElse_2", "K2Node_FunctionEntry_0", "then");
        g.Math("K2Node_CallFunction_2", "Greater_DoubleDouble"); g.Literal("K2Node_CallFunction_2", "B", "0.0");
        g.Link("K2Node_IfThenElse_2", "Condition", "K2Node_CallFunction_2", "ReturnValue");
        g.Member("K2Node_CallFunction_9", "K2Node_CallFunction", "GetCurveValue"); g.Self("K2Node_CallFunction_9");
        g.Link("K2Node_CallFunction_2", "A", "K2Node_CallFunction_9", "ReturnValue");
        g.Link("K2Node_CallFunction_9", "CurveName", "K2Node_FunctionEntry_0", "Enable_FootIK_Curve");
        g.Sequence("K2Node_ExecutionSequence_0", 3); g.Flow("K2Node_ExecutionSequence_0", "K2Node_IfThenElse_2", "then");
        g.Flow("K2Node_VariableSet_0", "K2Node_ExecutionSequence_0", "then_0");
        g.Variable("K2Node_VariableSet_0", "IKFootFloorLocation", true);
        g.Math("K2Node_CallFunction_3", "MakeVector"); g.Link("K2Node_VariableSet_0", "IKFootFloorLocation", "K2Node_CallFunction_3", "ReturnValue");
        g.Link("K2Node_CallFunction_3", "X", "K2Node_CallFunction_4", "ReturnValue_X");
        g.Link("K2Node_CallFunction_3", "Y", "K2Node_CallFunction_4", "ReturnValue_Y");
        g.Link("K2Node_CallFunction_3", "Z", "K2Node_CallFunction_27", "ReturnValue_Z");
        g.Member("K2Node_CallFunction_4", "K2Node_CallFunction", "GetSocketLocation");
        g.Member("K2Node_CallFunction_27", "K2Node_CallFunction", "GetSocketLocation");
        g.Read("K2Node_CallFunction_4", "InSocketName", "K2Node_VariableGet_0", "IKFootBone");
        g.Read("K2Node_CallFunction_27", "InSocketName", "K2Node_VariableGet_1", "RootBone");
        g.Flow("K2Node_CallFunction_0", "K2Node_VariableSet_0", "then");
        g.Member("K2Node_CallFunction_0", "K2Node_CallFunction", "LineTraceSingle");
        g.Literal("K2Node_CallFunction_0", "TraceChannel", "TraceTypeQuery1");
        g.Literal("K2Node_CallFunction_0", "bTraceComplex", "false"); g.Literal("K2Node_CallFunction_0", "bIgnoreSelf", "true");
        g.Link("K2Node_CallFunction_0", "Start", "K2Node_CommutativeAssociativeBinaryOperator_5", "ReturnValue");
        g.Link("K2Node_CallFunction_0", "End", "K2Node_CallFunction_20", "ReturnValue");
        g.Operator("K2Node_CommutativeAssociativeBinaryOperator_5", "Add_VectorVector");
        g.Math("K2Node_CallFunction_20", "Subtract_VectorVector");
        foreach(var (node, variable, property) in new[] {("K2Node_CommutativeAssociativeBinaryOperator_5","K2Node_VariableGet_10","IK_TraceDistanceAboveFoot"),
                     ("K2Node_CallFunction_20","K2Node_VariableGet_15","IK_TraceDistanceBelowFoot")})
        {
            g.Link(node, "A", "K2Node_VariableSet_0", "Output_Get"); g.Literal(node, "B_X", "0.0"); g.Literal(node, "B_Y", "0.0");
            g.Read(node, "B_Z", variable, property, global: true);
        }
        g.Flow("K2Node_IfThenElse_0", "K2Node_CallFunction_0", "then");
        g.Member("K2Node_CallFunction_8", "K2Node_CallFunction", "IsWalkable");
        g.Link("K2Node_IfThenElse_0", "Condition", "K2Node_CallFunction_8", "ReturnValue");
        g.Link("K2Node_CallFunction_8", "Hit", "K2Node_CallFunction_0", "OutHit");
        g.Member("K2Node_CallFunction_26", "K2Node_CallFunction", "BreakHitResult");
        g.Link("K2Node_CallFunction_26", "Hit", "K2Node_CallFunction_0", "OutHit");
        g.Flow("K2Node_VariableSet_1", "K2Node_IfThenElse_0", "then"); g.Variable("K2Node_VariableSet_1", "ImpactPoint", true);
        g.Flow("K2Node_VariableSet_5", "K2Node_VariableSet_1", "then"); g.Variable("K2Node_VariableSet_5", "ImpactNormal", true);
        g.Link("K2Node_VariableSet_1", "ImpactPoint", "K2Node_CallFunction_26", "ImpactPoint");
        g.Link("K2Node_VariableSet_5", "ImpactNormal", "K2Node_CallFunction_26", "ImpactNormal");
        g.Flow("K2Node_VariableSetRef_2", "K2Node_VariableSet_5", "then");
        g.Reference("K2Node_VariableSetRef_2", "K2Node_VariableGet_18", "CurrentLocationTarget");
        g.Link("K2Node_VariableSetRef_2", "Value", "K2Node_CallFunction_17", "ReturnValue"); g.Math("K2Node_CallFunction_17", "Subtract_VectorVector");
        g.Operator("K2Node_CommutativeAssociativeBinaryOperator_2", "Add_VectorVector");
        g.Operator("K2Node_CommutativeAssociativeBinaryOperator_4", "Add_VectorVector");
        g.Link("K2Node_CallFunction_17", "A", "K2Node_CommutativeAssociativeBinaryOperator_2", "ReturnValue");
        g.Link("K2Node_CallFunction_17", "B", "K2Node_CommutativeAssociativeBinaryOperator_4", "ReturnValue");
        g.Read("K2Node_CommutativeAssociativeBinaryOperator_2", "A", "K2Node_VariableGet_4", "ImpactPoint");
        g.Link("K2Node_CommutativeAssociativeBinaryOperator_2", "B", "K2Node_CallFunction_10", "ReturnValue");
        g.Read("K2Node_CommutativeAssociativeBinaryOperator_4", "A", "K2Node_VariableGet_16", "IKFootFloorLocation");
        g.Link("K2Node_CommutativeAssociativeBinaryOperator_4", "B", "K2Node_CallFunction_18", "ReturnValue");
        g.Math("K2Node_CallFunction_10", "Multiply_VectorFloat"); g.Math("K2Node_CallFunction_18", "Multiply_VectorFloat");
        g.Read("K2Node_CallFunction_10", "A", "K2Node_VariableGet_5", "ImpactNormal"); g.Literal("K2Node_CallFunction_18", "A", "0, 0,1.000000");
        g.Read("K2Node_CallFunction_10", "B", "K2Node_VariableGet_6", "FootHeight", global: true);
        g.Read("K2Node_CallFunction_18", "B", "K2Node_VariableGet_17", "FootHeight", global: true);
        g.Flow("K2Node_VariableSet_4", "K2Node_VariableSetRef_2", "then"); g.Variable("K2Node_VariableSet_4", "TargetRotationOffset", true);
        g.Math("K2Node_CallFunction_14", "MakeRotator"); g.Link("K2Node_VariableSet_4", "TargetRotationOffset", "K2Node_CallFunction_14", "ReturnValue");
        g.Link("K2Node_CallFunction_14", "Roll", "K2Node_CallFunction_12", "ReturnValue"); g.Literal("K2Node_CallFunction_14", "Yaw", "0.0");
        g.Link("K2Node_CallFunction_14", "Pitch", "K2Node_CommutativeAssociativeBinaryOperator_3", "ReturnValue");
        g.Operator("K2Node_CommutativeAssociativeBinaryOperator_3", "Multiply_DoubleDouble");
        g.Literal("K2Node_CommutativeAssociativeBinaryOperator_3", "B", "-1.000000");
        g.Link("K2Node_CommutativeAssociativeBinaryOperator_3", "A", "K2Node_CallFunction_11", "ReturnValue");
        foreach(var (node, axis) in new[] {("K2Node_CallFunction_11","X"),("K2Node_CallFunction_12","Y")})
        {
            g.Math(node, "DegAtan2"); g.Read(node, "Y", "K2Node_VariableGet_7", "ImpactNormal", "ImpactNormal_" + axis);
            g.Read(node, "X", "K2Node_VariableGet_7", "ImpactNormal", "ImpactNormal_Z");
        }
        g.Flow("K2Node_IfThenElse_3", "K2Node_ExecutionSequence_0", "then_1");
        g.Math("K2Node_CallFunction_21", "Greater_DoubleDouble");
        g.Link("K2Node_IfThenElse_3", "Condition", "K2Node_CallFunction_21", "ReturnValue");
        g.Read("K2Node_CallFunction_21", "A", "K2Node_VariableGet_8", "CurrentLocationOffset", "CurrentLocationOffset_Z");
        g.Read("K2Node_CallFunction_21", "B", "K2Node_VariableGet_19", "CurrentLocationTarget", "CurrentLocationTarget_Z");
        g.Flow("K2Node_VariableSetRef_3", "K2Node_IfThenElse_3", "then");
        g.Flow("K2Node_VariableSetRef_0", "K2Node_IfThenElse_3", "else");
        g.Flow("K2Node_VariableSetRef_1", "K2Node_ExecutionSequence_0", "then_2");
        var down = Interp("K2Node_VariableSetRef_3", "K2Node_VariableGet_33", "CurrentLocationOffset", "K2Node_CallFunction_25", "VInterpTo", "K2Node_VariableGet_30", "K2Node_VariableGet_32", "CurrentLocationTarget", "K2Node_VariableGet_31");
        var up = Interp("K2Node_VariableSetRef_0", "K2Node_VariableGet_9", "CurrentLocationOffset", "K2Node_CallFunction_22", "VInterpTo", "K2Node_VariableGet_29", "K2Node_VariableGet_28", "CurrentLocationTarget", "K2Node_VariableGet_23");
        var rotation = Interp("K2Node_VariableSetRef_1", "K2Node_VariableGet_14", "CurrentRotationOffset", "K2Node_CallFunction_16", "RInterpTo", "K2Node_VariableGet_13", "K2Node_VariableGet_12", "TargetRotationOffset", "K2Node_VariableGet_11");
        g.Flow("K2Node_VariableSetRef_5", "K2Node_IfThenElse_2", "else"); g.Flow("K2Node_VariableSetRef_4", "K2Node_VariableSetRef_5", "then");
        g.Reference("K2Node_VariableSetRef_5", "K2Node_VariableGet_34", "CurrentLocationOffset"); g.Literal("K2Node_VariableSetRef_5", "Value", "");
        g.Reference("K2Node_VariableSetRef_4", "K2Node_VariableGet_35", "CurrentRotationOffset"); g.Literal("K2Node_VariableSetRef_4", "Value", "");
        return new(g.Default("FootHeight"), g.Default("IK_TraceDistanceAboveFoot"), g.Default("IK_TraceDistanceBelowFoot"), down, up, rotation);
        float Interp(string setter, string variable, string property, string call, string function, string current, string target, string targetProperty, string delta)
        {
            g.Reference(setter, variable, property); g.Math(call, function); g.Link(setter, "Value", call, "ReturnValue");
            g.Read(call, "Current", current, property); g.Read(call, "Target", target, targetProperty);
            g.Read(call, "DeltaTime", delta, "DeltaTimeX", global: true); return (float)g.Number(call, "InterpSpeed");
        }
    }

    private static void CompileUpdate(G g)
    {
        g.LocalZero("FootOffset_L_Target"); g.LocalZero("FootOffset_R_Target");
        g.Flow("K2Node_CallFunction_3", "K2Node_FunctionEntry_0", "then");
        g.Flow("K2Node_CallFunction_0", "K2Node_CallFunction_3", "then");
        g.Flow("K2Node_SwitchEnum_0", "K2Node_CallFunction_0", "then");
        g.Read("K2Node_SwitchEnum_0", "Selection", "K2Node_VariableGet_6", "MovementState", global: true);
        g.MovementBranches();
        g.Flow("K2Node_CallFunction_5", "K2Node_Knot_3", "OutputPin", reroutes: false);
        g.Flow("K2Node_CallFunction_4", "K2Node_CallFunction_5", "then");
        g.Flow("K2Node_CallFunction_6", "K2Node_CallFunction_4", "then");
        g.Flow("K2Node_CallFunction_1", "K2Node_SwitchEnum_0", "NewEnumerator2");
        g.Flow("K2Node_CallFunction_7", "K2Node_CallFunction_1", "then");
        foreach(var (node, side, alpha, location, rotation) in new[] {
                    ("K2Node_CallFunction_3","L","K2Node_VariableGet_3","K2Node_VariableGet_4","K2Node_VariableGet_5"),
                    ("K2Node_CallFunction_0","R","K2Node_VariableGet_0","K2Node_VariableGet_1","K2Node_VariableGet_2")})
        {
            g.Member(node, "K2Node_CallFunction", "SetFootLocking"); g.Self(node);
            g.Literal(node, "Enable_FootIK_Curve", "Enable_FootIK_" + side); g.Literal(node, "FootLockCurve", "FootLock_" + side);
            g.Literal(node, "IKFootBone", "ik_foot_" + side.ToLowerInvariant());
            g.Read(node, "CurrentFootLockAlpha", alpha, "FootLock_" + side + "_Alpha", global: true);
            g.Read(node, "CurrentFootLockLocation", location, "FootLock_" + side + "_Location", global: true);
            g.Read(node, "CurrentFootLockRotation", rotation, "FootLock_" + side + "_Rotation", global: true);
        }
        foreach(var (node, side, target, location, rotation) in new[] {
                    ("K2Node_CallFunction_5","L","K2Node_VariableGet_7","K2Node_VariableGet_15","K2Node_VariableGet_11"),
                    ("K2Node_CallFunction_4","R","K2Node_VariableGet_14","K2Node_VariableGet_16","K2Node_VariableGet_9")})
        {
            g.Member(node, "K2Node_CallFunction", "SetFootOffsets"); g.Self(node);
            g.Literal(node, "Enable_FootIK_Curve", "Enable_FootIK_" + side); g.Literal(node, "IKFootBone", "ik_foot_" + side.ToLowerInvariant());
            g.Literal(node, "RootBone", "root");
            g.Read(node, "CurrentLocationTarget", target, "FootOffset_" + side + "_Target");
            g.Read(node, "CurrentLocationOffset", location, "FootOffset_" + side + "_Location", global: true);
            g.Read(node, "CurrentRotationOffset", rotation, "FootOffset_" + side + "_Rotation", global: true);
        }
        g.Member("K2Node_CallFunction_6", "K2Node_CallFunction", "SetPelvisIKOffset"); g.Self("K2Node_CallFunction_6");
        g.Member("K2Node_CallFunction_1", "K2Node_CallFunction", "SetPelvisIKOffset"); g.Self("K2Node_CallFunction_1");
        g.Read("K2Node_CallFunction_6", "FootOffset_L_Target", "K2Node_VariableGet_12", "FootOffset_L_Target");
        g.Read("K2Node_CallFunction_6", "FootOffset_R_Target", "K2Node_VariableGet_13", "FootOffset_R_Target");
        g.Literal("K2Node_CallFunction_1", "FootOffset_L_Target", "0, 0, 0"); g.Literal("K2Node_CallFunction_1", "FootOffset_R_Target", "0, 0, 0");
        g.Member("K2Node_CallFunction_7", "K2Node_CallFunction", "ResetIKOffsets"); g.Self("K2Node_CallFunction_7");
    }

    private sealed class G
    {
        private readonly Graph _graph; private readonly string _name, _defaults;
        public G(JsonElement root, string name)
        {
            _name = name; _defaults = root.GetProperty("defaultsText").GetString()!;
            var source = root.GetProperty("source").GetString();
            _graph = new(root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("path").GetString() == source + ":" + name).GetProperty("nativeText").GetString()!, true);
            Member("K2Node_FunctionEntry_0", "K2Node_FunctionEntry", name);
        }
        public void Member(string name, string kind, string member)
        { var n = _graph.Named(name); Require(n.Kind == kind && n.Member == member, "Changed foot function node: " + name); }
        public void Self(string name) => _graph.Self(_graph.Named(name));
        public void Variable(string name, string member, bool setter = false, bool global = false)
        {
            Member(name, setter ? "K2Node_VariableSet" : "K2Node_VariableGet", member);
            if(global) Self(name);
            else Require(_graph.Named(name).Body.Contains("MemberScope=\"" + _name + "\"", StringComparison.Ordinal), "Foot local property became global.");
        }
        public void Read(string target, string pin, string variable, string property, string? output = null, bool global = false)
        { Variable(variable, property, global: global); Link(target, pin, variable, output ?? property); }
        public void Reference(string target, string variable, string property)
        { Require(_graph.Named(target).Kind == "K2Node_VariableSetRef", "Changed foot reference write."); Read(target, "Target", variable, property); }
        public void Link(string target, string pin, string source, string output) => _graph.Link(_graph.Named(target), pin, _graph.Named(source), output);
        public void Flow(string target, string source, string output, bool reroutes = true)
        {
            var (node, pin) = reroutes ? _graph.FollowReroutes(_graph.Named(target), "execute") : _graph.Follow(_graph.Named(target), "execute");
            Require(node.Name == source && pin.Name == output, "Changed foot execution order.");
        }
        public void Sequence(string name, int outputs)
        { var n = _graph.Named(name); Require(n.Kind == "K2Node_ExecutionSequence" && n.Pins.Values.Count(p => p.Output) == outputs, "Changed foot sequence branches."); }
        public void Math(string name, string member) => _graph.Function(_graph.Named(name), member, "Engine.KismetMathLibrary");
        public void Operator(string name, string member)
        {
            Member(name, "K2Node_CommutativeAssociativeBinaryOperator", member);
            Require(_graph.Named(name).Body.Contains("/Script/Engine.KismetMathLibrary'", StringComparison.Ordinal), "Foreign foot math operator.");
        }
        public void Literal(string node, string pin, string expected) => Require(_graph.Literal(_graph.Named(node), pin) == expected, "Changed foot literal: " + node + "." + pin);
        public double Number(string node, string pin) => double.Parse(_graph.Literal(_graph.Named(node), pin), CultureInfo.InvariantCulture);
        private string DefaultText(string name)
        {
            var matches = Regex.Matches(_defaults, @"(?m)^   " + name + @"=([^\r\n]+)");
            Require(matches.Count == 1, "Missing foot default: " + name);
            return matches[0].Groups[1].Value;
        }
        public double Default(string name)
        {
            var value = double.Parse(DefaultText(name), CultureInfo.InvariantCulture);
            Require(double.IsFinite(value), "Nonfinite foot default."); return value;
        }
        public AlsDoubleVector VectorDefault(string name) => new(Part(name,"X"), Part(name,"Y"), Part(name,"Z"));
        public AlsAimingRotation RotationDefault(string name) => new(Part(name,"Pitch"), Part(name,"Yaw"), Part(name,"Roll"));
        private double Part(string name, string component)
        {
            var match = Regex.Match(DefaultText(name), @"[\(,]" + component + @"=([^,\)]+)");
            Require(match.Success, "Missing foot default component.");
            var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            Require(double.IsFinite(value), "Nonfinite foot default."); return value;
        }
        public void LocalZero(string variable)
        {
            var rows = Regex.Matches(_graph.Named("K2Node_FunctionEntry_0").Body, @"(?m)^      LocalVariables\([^)]+\)=[^\r\n]+")
                .Where(m => m.Value.Contains("VarName=\"" + variable + "\"", StringComparison.Ordinal)).ToArray();
            Require(rows.Length == 1 && Regex.Match(rows[0].Value, "DefaultValue=\"([^\"]*)\"").Groups[1].Value == "", "Changed zero-initialized foot local.");
        }
        public void MovementBranches()
        {
            var node = _graph.Named("K2Node_SwitchEnum_0");
            Require(node.Kind == "K2Node_SwitchEnum" && node.Body.Contains("ALS_MovementState.ALS_MovementState'", StringComparison.Ordinal), "Changed Foot IK movement enum.");
            _graph.Links(_graph.Named("K2Node_Knot_3"), "InputPin", (node,"NewEnumerator0"), (node,"NewEnumerator1"), (node,"NewEnumerator4"));
            Require(node.Pins.Values.Single(p => p.Name == "NewEnumerator3").Links == "", "Ragdoll unexpectedly updates offsets.");
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
