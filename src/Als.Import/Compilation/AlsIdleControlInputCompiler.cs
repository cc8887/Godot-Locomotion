using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsIdleControlInputCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    public static AlsIdleControlInputModel Compile(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("source").GetString() == Source, "Wrong idle source.");
        Graph Read(string name)
        {
            var item = root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("name").GetString() == name);
            Require(item.GetProperty("path").GetString() == Source + ":" + name, "Wrong idle graph path.");
            return new(item.GetProperty("nativeText").GetString()!);
        }
        ValidatePermission(Read("CanRotateInPlace"), "CanRotateInPlace", "BooleanOR(Eq(RotationMode,NewEnumerator3),Eq(ViewMode,NewEnumerator1))");
        var turnGate = Read("CanTurnInPlace");
        ValidatePermission(turnGate, "CanTurnInPlace", "BooleanAND(Eq(RotationMode,NewEnumerator1),Eq(ViewMode,NewEnumerator0),Greater_DoubleDouble(Curve(Enable_Transition),0.99))");
        ValidatePermission(Read("CanDynamicTransition"), "CanDynamicTransition", "EqualEqual_DoubleDouble(Curve(Enable_Transition),1)");
        ValidateRotate(Read("RotateInPlaceCheck")); ValidateTurn(Read("TurnInPlaceCheck")); ValidateUpdate(Read("UpdateGraph"));
        ValidateAimingAngle(Read("UpdateAimingValues")); ValidateCharacterRate(root);
        var defaults = root.GetProperty("defaultsText").GetString()!;
        float Value(string name)
        {
            var matches = Regex.Matches(defaults, @"(?m)^   " + Regex.Escape(name) + @"=([^\r\n]+)");
            Require(matches.Count == 1, "Missing idle default: " + name);
            var value = float.Parse(matches[0].Groups[1].Value, CultureInfo.InvariantCulture);
            Require(float.IsFinite(value), "Non-finite idle default."); return value;
        }
        var settings = new AlsIdleControlSettings(Value("RotateMinThreshold"), Value("RotateMaxThreshold"), Value("AimYawRateMinRange"),
            Value("AimYawRateMaxRange"), Value("MinPlayRate"), Value("MaxPlayRate"), Value("TurnCheckMinAngle"), Value("AimYawRateLimit"),
            Value("MinAngleDelay"), Value("MaxAngleDelay"), .99f);
        Require(settings.RotateMin < 0 && settings.RotateMax > 0 && settings.RateMin < settings.RateMax &&
            settings.PlayRateMin > 0 && settings.PlayRateMax >= settings.PlayRateMin && settings.TurnMinAngle is >= 0 and < 180 &&
            settings.TurnRateLimit >= 0 && settings.MinAngleDelay >= 0 && settings.MaxAngleDelay >= 0, "Invalid idle settings.");
        return new(settings);
    }

    private static void ValidatePermission(Graph graph, string entry, string expected)
    {
        var result = graph.WithPin("K2Node_FunctionResult", "ReturnValue", false);
        graph.Link(result, "execute", graph.One("K2Node_FunctionEntry", entry), "then");
        Expression(graph, result, "ReturnValue", expected);
    }
    private static void ValidateCharacterRate(JsonElement root)
    {
        const string character = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP";
        Require(root.GetProperty("characterSource").GetString() == character && root.GetProperty("previousAimYawDefault").GetDouble() == 0,
            "Wrong character aim history default.");
        var essential = new Graph(root.GetProperty("essentialValuesText").GetString()!);
        Expression(essential, Setter(essential, "AimYawRate"), "AimYawRate", "Abs(Divide_DoubleDouble(Subtract_DoubleDouble(ControlYaw,PreviousAimYaw),WorldDelta))");
        var cacheData = root.GetProperty("characterHistoryGraphs").EnumerateArray().Single(g => g.GetProperty("path").GetString() == character + ":CacheValues");
        var cache = new Graph(cacheData.GetProperty("nativeText").GetString()!);
        var setter = Setter(cache, "PreviousAimYaw"); Expression(cache, setter, "PreviousAimYaw", "ControlYaw");
        var sequence = cache.Named("K2Node_ExecutionSequence_0"); Require(sequence.Kind == "K2Node_ExecutionSequence", "Wrong character cache order.");
        cache.Link(sequence, "execute", cache.One("K2Node_FunctionEntry", "CacheValues"), "then");
        Reroute(cache, setter, "execute", sequence, "then_1"); Empty(setter, "then");
        var tickData = root.GetProperty("characterHistoryGraphs").EnumerateArray().Single(g => g.GetProperty("path").GetString() == character + ":TickGraph");
        var tick = new Graph(tickData.GetProperty("nativeText").GetString()!);
        var tickSequence = tick.Named("K2Node_ExecutionSequence_0"); Require(tickSequence.Kind == "K2Node_ExecutionSequence", "Wrong character tick sequence.");
        var update = tick.One("K2Node_CallFunction", "SetEssentialValues"); var save = tick.One("K2Node_CallFunction", "CacheValues");
        tick.Self(update); tick.Self(save); tick.Link(update, "execute", tickSequence, "then_0"); tick.Link(save, "execute", tickSequence, "then_1");
    }
    private static void ValidateAimingAngle(Graph g)
    {
        var setter = Setter(g, "AimingAngle"); var (make, vector) = g.Follow(setter, "AimingAngle");
        g.Function(make, "MakeVector2D", "Engine.KismetMathLibrary"); Require(vector.Name == "ReturnValue", "Wrong aiming vector.");
        var (delta, yaw) = g.Follow(make, "X"); g.Function(delta, "NormalizedDeltaRotator", "Engine.KismetMathLibrary");
        Require(yaw.Name == "ReturnValue_Yaw", "Idle angle no longer uses yaw.");
        var (aim, aimPin) = g.Follow(delta, "A"); g.Self(aim);
        Require(aim.Kind == "K2Node_VariableGet" && aim.Member == "AimingRotation" && aimPin.Name == "AimingRotation", "Wrong aiming rotation.");
        var (actor, rotation) = g.Follow(delta, "B"); g.Function(actor, "K2_GetActorRotation", "Engine.Actor");
        Require(rotation.Name == "ReturnValue", "Wrong actor rotation component.");
        var (owner, character) = g.Follow(actor, "self"); g.Self(owner);
        Require(owner.Member == "Character" && owner.Kind == "K2Node_VariableGet" && character.Name == "Character", "Wrong actor rotation owner.");
    }
    private static void ValidateRotate(Graph g)
    {
        var left = Setter(g, "Rotate_L"); var right = Setter(g, "Rotate_R"); var rate = Setter(g, "RotateRate");
        Expression(g, left, "Rotate_L", "Less_DoubleDouble(AimingAngle_X,RotateMinThreshold)");
        Expression(g, right, "Rotate_R", "Greater_DoubleDouble(AimingAngle_X,RotateMaxThreshold)");
        Expression(g, rate, "RotateRate", "MapRangeClamped(AimYawRate,AimYawRateMinRange,AimYawRateMaxRange,MinPlayRate,MaxPlayRate)");
        var sequence = g.Named("K2Node_ExecutionSequence_0"); Require(sequence.Kind == "K2Node_ExecutionSequence", "Wrong rotate sequence.");
        g.Link(sequence, "execute", g.One("K2Node_FunctionEntry", "RotateInPlaceCheck"), "then");
        Reroute(g, left, "execute", sequence, "then_0"); g.Link(right, "execute", left, "then");
        var branch = g.Named("K2Node_IfThenElse_3"); Require(branch.Kind == "K2Node_IfThenElse", "Wrong rotate branch.");
        Reroute(g, branch, "execute", sequence, "then_1"); Expression(g, branch, "Condition", "BooleanOR(Rotate_L,Rotate_R)");
        g.Link(rate, "execute", branch, "then");
        Empty(right, "then"); Empty(rate, "then"); Empty(branch, "else");
    }
    private static void ValidateTurn(Graph g)
    {
        var check = g.Named("K2Node_IfThenElse_0"); var delay = g.Named("K2Node_IfThenElse_3");
        Require(check.Kind == "K2Node_IfThenElse" && delay.Kind == "K2Node_IfThenElse", "Wrong turn branches.");
        g.Link(check, "execute", g.One("K2Node_FunctionEntry", "TurnInPlaceCheck"), "then");
        Expression(g, check, "Condition", "BooleanAND(Greater_DoubleDouble(Abs(AimingAngle_X),TurnCheckMinAngle),Less_DoubleDouble(AimYawRate,AimYawRateLimit))");
        var increment = Setter(g, "ElapsedDelayTime", "K2Node_VariableSet_9"); var reset = Setter(g, "ElapsedDelayTime", "K2Node_VariableSet_0");
        g.Link(increment, "execute", check, "then"); g.Link(reset, "execute", check, "else");
        Expression(g, increment, "ElapsedDelayTime", "Add_DoubleDouble(ElapsedDelayTime,DeltaTimeX)");
        Expression(g, reset, "ElapsedDelayTime", "0");
        g.Link(g.Named("K2Node_FunctionResult_6"), "execute", reset, "then");
        g.Link(delay, "execute", increment, "then");
        Expression(g, delay, "Condition", "Greater_DoubleDouble(ElapsedDelayTime,MapRangeClamped(Abs(AimingAngle_X),TurnCheckMinAngle,180,MinAngleDelay,MaxAngleDelay))");
        var turn = g.One("K2Node_CallFunction", "TurnInPlace"); g.Self(turn); g.Link(turn, "execute", delay, "then");
        Expression(g, turn, "TargetRotation_Yaw", "AimingRotation_Yaw");
        foreach (var (pin, value) in new[] { ("TargetRotation_Roll", "0"), ("TargetRotation_Pitch", "0"), ("PlayRateScale", "1"), ("StartTime", "0"), ("OverrideCurrent", "false") })
            Expression(g, turn, pin, value);
        Empty(delay, "else"); Empty(turn, "then");
    }
    private static void ValidateUpdate(Graph g)
    {
        var sequence = g.Named("K2Node_ExecutionSequence_2"); Require(sequence.Kind == "K2Node_ExecutionSequence", "Wrong idle sequence.");
        var macro = g.Named("K2Node_MacroInstance_1");
        Require(macro.Kind == "K2Node_MacroInstance" && macro.Body.Contains("ALS_MacroLibrary.ALS_MacroLibrary:ML_DoWhile(TrueFalse)'", StringComparison.Ordinal), "Wrong idle macro.");
        g.Link(sequence, "execute", macro, "WhileFalse");
        foreach (var (index, branchId, permission, method) in new[] {
            (0, 1, "CanRotateInPlace", "RotateInPlaceCheck"), (1, 2, "CanTurnInPlace", "TurnInPlaceCheck"),
            (2, 0, "CanDynamicTransition", "DynamicTransitionCheck") })
        {
            var branch = g.Named("K2Node_IfThenElse_" + branchId); Require(branch.Kind == "K2Node_IfThenElse", "Wrong idle update branch.");
            Reroute(g, branch, "execute", sequence, "then_" + index);
            var can = g.One("K2Node_CallFunction", permission); g.Self(can); g.Link(branch, "Condition", can, "ReturnValue");
            var call = g.One("K2Node_CallFunction", method); g.Self(call); g.Link(call, "execute", branch, "then"); Empty(call, "then");
            if (index == 0)
            {
                var left = Setter(g, "Rotate_L", "K2Node_VariableSet_5"); var right = Setter(g, "Rotate_R", "K2Node_VariableSet_7");
                g.Link(left, "execute", branch, "else"); g.Link(right, "execute", left, "then");
                Expression(g, left, "Rotate_L", "false"); Expression(g, right, "Rotate_R", "false"); Empty(right, "then");
            }
            else if (index == 1)
            {
                var reset = Setter(g, "ElapsedDelayTime", "K2Node_VariableSet_8");
                g.Link(reset, "execute", branch, "else"); Expression(g, reset, "ElapsedDelayTime", "0"); Empty(reset, "then");
            }
            else Empty(branch, "else");
        }
    }
    private static Node Setter(Graph g, string member, string? name = null)
    {
        var node = name is null ? g.One("K2Node_VariableSet", member) : g.Named(name);
        Require(node.Kind == "K2Node_VariableSet" && node.Member == member, "Wrong idle setter."); g.Self(node); return node;
    }
    private static void Reroute(Graph g, Node node, string input, Node expected, string output)
    {
        var (other, pin) = g.FollowReroutes(node, input); Require(other == expected && pin.Name == output, "Wrong idle sequence order.");
    }
    private static void Empty(Node node, string name) => Require(node.Pins.Values.Single(p => p.Name == name).Links == "", "Unexpected idle continuation.");
    private static void Expression(Graph g, Node node, string input, string expected) =>
        Require(Input(g, node, input, 0) == expected, "Idle expression differs: " + node.Name + "." + input);
    private static string Input(Graph g, Node node, string input, int depth)
    {
        Require(depth < 32, "Cyclic idle expression.");
        var pin = node.Pins.Values.Single(p => p.Name == input && !p.Output);
        if (pin.Links == "")
        {
            var value = g.Literal(node, input);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number.ToString("G", CultureInfo.InvariantCulture) : value;
        }
        var (other, output) = g.FollowReroutes(node, input);
        if (other.Kind == "K2Node_CallFunction" && other.Member == "GetControlRotation")
        { g.Self(other); Require(output.Name == "ReturnValue_Yaw", "Wrong character control component."); return "ControlYaw"; }
        if (other.Kind == "K2Node_CallFunction" && other.Member == "GetWorldDeltaSeconds")
        {
            g.Function(other, "GetWorldDeltaSeconds", "Engine.GameplayStatics");
            Require(output.Name == "ReturnValue" && g.Literal(other, "WorldContextObject") == "", "Wrong character delta context."); return "WorldDelta";
        }
        if (other.Kind == "K2Node_VariableGet")
        {
            g.Self(other); Require(output.Name == other.Member || output.Name == "AimingAngle_X" && other.Member == "AimingAngle" ||
                output.Name == "AimingRotation_Yaw" && other.Member == "AimingRotation", "Wrong idle variable component."); return output.Name;
        }
        if (other.Kind == "K2Node_EnumEquality")
        {
            Require(output.Name == "ReturnValue", "Wrong enum result.");
            var a = Input(g, other, "A", depth + 1); var b = Input(g, other, "B", depth + 1);
            Require(a is "RotationMode" or "ViewMode" && other.Body.Contains("ALS_" + (a == "RotationMode" ? "RotationMode" : "ViewMode") + ".ALS_" + a + "'", StringComparison.Ordinal), "Wrong idle enum domain.");
            return "Eq(" + a + "," + b + ")";
        }
        Require(output.Name == "ReturnValue" && other.Kind is "K2Node_CallFunction" or "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported idle expression node.");
        if (other.Member == "GetCurveValue")
        { g.Self(other); return "Curve(" + g.Literal(other, "CurveName") + ")"; }
        Require(other.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/Engine.KismetMathLibrary'\"", StringComparison.Ordinal), "Wrong idle math owner.");
        string[] args = other.Member switch
        {
            "BooleanAND" or "BooleanOR" => other.Pins.Values.Where(p => !p.Output && p.Name != "self").Select(p => p.Name).Order(StringComparer.Ordinal).ToArray(),
            "Less_DoubleDouble" or "Greater_DoubleDouble" or "EqualEqual_DoubleDouble" or "Add_DoubleDouble" or "Subtract_DoubleDouble" or "Divide_DoubleDouble" => ["A", "B"],
            "Abs" => ["A"], "MapRangeClamped" => ["Value", "InRangeA", "InRangeB", "OutRangeA", "OutRangeB"],
            _ => throw new ArgumentException("Unsupported idle math: " + other.Member),
        };
        return other.Member + "(" + string.Join(",", args.Select(arg => Input(g, other, arg, depth + 1))) + ")";
    }
    private static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
}
