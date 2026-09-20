using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public static class AlsYawOffsetCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";

    public static AlsGroundedControlInputModel CompileGlobalControl(string movementJson, string yawJson, string controlJson)
    {
        AlsMovementUpdateGateCompiler.Validate(movementJson);
        var yaw = Compile(yawJson);
        using var document = JsonDocument.Parse(controlJson); var root = document.RootElement;
        Require(Text(root, "source") == Source && root.GetProperty("schemaVersion").GetInt32() == 1, "Wrong global control source.");
        var graphs = root.GetProperty("graphs").EnumerateArray().ToArray(); Require(graphs.Length == 2, "Wrong control graph set.");
        Graph Read(string name) => new(Text(graphs.Single(g => Text(g, "path") == Source + ":" + name), "nativeText"));
        var rotation = Read("UpdateRotationValues"); ValidateRotation(rotation);
        var setter = rotation.One("K2Node_VariableSet", "MovementDirection"); rotation.Self(setter);
        var function = rotation.One("K2Node_CallFunction", "CalculateMovementDirection"); rotation.Self(function);
        rotation.Link(setter, "MovementDirection", function, "ReturnValues");
        var (sequence, branch) = rotation.FollowReroutes(setter, "execute");
        Require(sequence.Kind == "K2Node_ExecutionSequence" && branch.Name == "then_0", "Movement direction must precede yaw writes.");
        ValidateMovementDirection(Read("CalculateMovementDirection"));
        var values = root.GetProperty("defaults"); Require(values.EnumerateObject().Count() == 6, "Missing control defaults.");
        var direction = values.GetProperty("MovementDirection").GetInt32(); Require(direction is >= 0 and <= 3, "Invalid initial direction.");
        var idle = new AlsGroundedIdleControl(values.GetProperty("Rotate_L").GetBoolean(), values.GetProperty("Rotate_R").GetBoolean(),
            NumberValue("RotateRate"), NumberValue("RotationScale"), NumberValue("ElapsedDelayTime"));
        Require(idle.ElapsedDelayTime >= 0, "Negative initial turn delay.");
        return new(yaw, new(default, default, (AlsMovementDirection)direction, default, idle));
        float NumberValue(string name)
        { var value = values.GetProperty(name).GetSingle(); Require(float.IsFinite(value), "Non-finite control default."); return value; }
    }

    private static void ValidateMovementDirection(Graph graph)
    {
        var gait = graph.One("K2Node_VariableGet", "Gait"); graph.Self(gait);
        var mode = graph.One("K2Node_VariableGet", "RotationMode"); graph.Self(mode);
        var gaitSwitch = graph.Named("K2Node_SwitchEnum_1"); var modeSwitch = graph.Named("K2Node_SwitchEnum_0");
        Require(gaitSwitch.Kind == "K2Node_SwitchEnum" && modeSwitch.Kind == "K2Node_SwitchEnum" &&
            gaitSwitch.Body.Contains("ALS_Gait.ALS_Gait'", StringComparison.Ordinal) &&
            modeSwitch.Body.Contains("ALS_RotationMode.ALS_RotationMode'", StringComparison.Ordinal), "Wrong control enum switches.");
        graph.Link(gaitSwitch, "execute", graph.One("K2Node_FunctionEntry", "CalculateMovementDirection"), "then");
        graph.Link(gaitSwitch, "Selection", gait, "Gait"); graph.Link(modeSwitch, "Selection", mode, "RotationMode");
        var knot = graph.Named("K2Node_Knot_1"); Require(knot.Kind == "K2Node_Knot", "Wrong gait reroute.");
        graph.Link(modeSwitch, "execute", knot, "OutputPin"); graph.Links(knot, "InputPin", (gaitSwitch, "NewEnumerator0"), (gaitSwitch, "NewEnumerator1"));
        var sprint = graph.Named("K2Node_FunctionResult_3"); var velocity = graph.Named("K2Node_FunctionResult_5");
        graph.Link(sprint, "execute", gaitSwitch, "NewEnumerator2"); graph.Link(velocity, "execute", modeSwitch, "NewEnumerator0");
        Require(graph.Literal(sprint, "ReturnValues") == "NewEnumerator0" && graph.Literal(velocity, "ReturnValues") == "NewEnumerator0",
            "Sprint or VelocityDirection no longer returns Forward.");
        var result = graph.Named("K2Node_FunctionResult_1"); graph.Links(result, "execute", (modeSwitch, "NewEnumerator1"), (modeSwitch, "NewEnumerator3"));
        var quadrant = graph.One("K2Node_CallFunction", "CalculateQuadrant"); graph.Self(quadrant);
        graph.Link(result, "ReturnValues", quadrant, "ReturnValue");
        var current = graph.One("K2Node_VariableGet", "MovementDirection"); graph.Self(current); graph.Link(quadrant, "Current", current, "MovementDirection");
        foreach (var (name, expected) in new[] { ("FR-Threshold", 70f), ("FL-Threshold", -70f), ("BR-Threshold", 110f), ("BL-Threshold", -110f), ("Buffer", 5f) })
            Require(Number(graph.Literal(quadrant, name)) == expected, "Movement quadrant threshold differs from the native verified model.");
        var delta = graph.One("K2Node_CallFunction", "NormalizedDeltaRotator"); graph.Function(delta, "NormalizedDeltaRotator", "Engine.KismetMathLibrary");
        graph.Link(quadrant, "Angle", delta, "ReturnValue_Yaw");
        var rotate = graph.One("K2Node_CallFunction", "Conv_VectorToRotator"); graph.Function(rotate, "Conv_VectorToRotator", "Engine.KismetMathLibrary");
        graph.Link(delta, "A", rotate, "ReturnValue");
        var actual = graph.One("K2Node_VariableGet", "Velocity"); graph.Self(actual); graph.Link(rotate, "InVec", actual, "Velocity");
        var aim = graph.One("K2Node_VariableGet", "AimingRotation"); graph.Self(aim); graph.Link(delta, "B", aim, "AimingRotation");
    }

    public static AlsYawOffset Compile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "source") == Source &&
            Text(root, "rotationGraphPath") == Source + ":UpdateRotationValues" &&
            Text(root, "updateGraphPath") == Source + ":UpdateGraph", "Wrong native yaw source.");
        Require(root.GetProperty("yawDefaults").GetArrayLength() == 4 &&
            root.GetProperty("yawDefaults").EnumerateArray().All(v => v.GetSingle() == 0), "Unsupported yaw defaults.");
        ValidateRotation(new(Text(root, "rotationGraphText")));
        ValidateUpdate(new(Text(root, "updateGraphText")));
        ValidateMoveMacro(new(Text(root, "moveMacroText")));
        Require(Regex.IsMatch(Text(root, "movementEnumText"), @"\(""NewEnumerator1"", NSLOCTEXT\([^)]*, ""Grounded""\)\)"),
            "MovementState grounded enumerator differs.");
        var curves = root.GetProperty("curves");
        Require(curves.GetArrayLength() == 2, "Expected both native CurveVectors.");
        var keys = new Vector2[4][];
        for (var curve = 0; curve < 2; curve++)
        {
            var data = curves[curve]; var name = curve == 0 ? "YawOffset_FB" : "YawOffset_LR";
            Require(Text(data, "name") == name && Text(data, "path") ==
                "/Game/AdvancedLocomotionV4/Data/Curves/AnimationBlendCurves/" + name + "." + name,
                "Wrong yaw CurveVector asset.");
            var native = Text(data, "nativeText");
            var channels = Regex.Matches(native, @"(?m)^\s*FloatCurves\((\d+)\)=\(Keys=\((.*?)\),PreInfinityExtrap=(\w+),PostInfinityExtrap=(\w+)\)\r?$");
            Require(channels.Count == 2, "Unexpected native yaw channel count or properties.");
            for (var axis = 0; axis < 2; axis++)
            {
                var channel = channels[axis];
                Require(channel.Groups[1].Value == axis.ToString(CultureInfo.InvariantCulture) &&
                    channel.Groups[3].Value == "RCCE_CycleWithOffset" && channel.Groups[4].Value == "RCCE_CycleWithOffset",
                    "Unexpected yaw axis or infinity mode.");
                var values = new List<Vector2>();
                foreach (Match key in Regex.Matches(channel.Groups[2].Value, @"\(([^()]*)\)"))
                {
                    var fields = key.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Split('=', 2)).ToDictionary(f => f[0], f => f[1], StringComparer.Ordinal);
                    foreach (var field in fields.Keys)
                        Require(field is "Time" or "Value" or "ArriveTangent" or "LeaveTangent" or "ArriveTangentWeight" or
                            "LeaveTangentWeight" or "InterpMode" or "TangentMode" or "TangentWeightMode", "Unknown native rich-curve key field.");
                    Require(fields.GetValueOrDefault("InterpMode", "RCIM_Linear") == "RCIM_Linear",
                        "Yaw interpolation changed; a linear runtime cannot consume this asset.");
                    values.Add(new(Number(fields.GetValueOrDefault("Time", "0")), Number(fields.GetValueOrDefault("Value", "0"))));
                }
                keys[curve * 2 + axis] = values.ToArray();
            }
        }
        var profile = new AlsYawOffset(keys);
        // Native GetVectorValue samples are validation evidence, never a replacement key table.
        for (var curve = 0; curve < 2; curve++)
        {
            var samples = curves[curve].GetProperty("verification");
            Require(samples.GetArrayLength() == 1441, "Incomplete native yaw verification domain.");
            for (var i = 0; i < samples.GetArrayLength(); i++)
            {
                var sample = samples[i]; var angle = sample.GetProperty("angle").GetSingle();
                Require(angle == -180 + i * .25f, "Native yaw verification angles differ.");
                var value = profile.Sample(angle);
                Require(MathF.Abs(value[curve * 2] - sample.GetProperty("x").GetSingle()) <= .00002f &&
                    MathF.Abs(value[curve * 2 + 1] - sample.GetProperty("y").GetSingle()) <= .00002f &&
                    sample.GetProperty("z").GetSingle() == 0, "Yaw key evaluation differs from the native CurveVector.");
            }
        }
        return profile;
    }

    private static void ValidateRotation(Graph graph)
    {
        string[] variables = ["FYaw", "BYaw", "LYaw", "RYaw"];
        string[] vectors = ["YawOffset_FB", "YawOffset_FB", "YawOffset_LR", "YawOffset_LR"];
        var setters = variables.Select(v => graph.One("K2Node_VariableSet", v)).ToArray();
        Node? sharedDelta = null;
        for (var axis = 0; axis < 4; axis++)
        {
            var setter = setters[axis]; graph.Self(setter);
            var (sample, component) = graph.Follow(setter, variables[axis]);
            graph.Function(sample, "GetVectorValue", "Engine.CurveVector");
            Require(component.Name == (axis % 2 == 0 ? "ReturnValue_X" : "ReturnValue_Y"), "Wrong CurveVector output component.");
            var (variable, variablePin) = graph.Follow(sample, "self");
            Require(variable.Kind == "K2Node_VariableGet" && variable.Member == vectors[axis] && variablePin.Name == vectors[axis],
                "Wrong CurveVector getter."); graph.Self(variable);
            var (delta, yaw) = graph.Follow(sample, "InTime");
            graph.Function(delta, "NormalizedDeltaRotator", "Engine.KismetMathLibrary");
            Require(yaw.Name == "ReturnValue_Yaw" && (sharedDelta is null || sharedDelta == delta), "Wrong normalized yaw pin.");
            sharedDelta = delta;
            if (axis > 0) graph.Link(setter, "execute", setters[axis - 1], "then");
        }
        var (velocityRotation, rotationPin) = graph.Follow(sharedDelta!, "A");
        graph.Function(velocityRotation, "Conv_VectorToRotator", "Engine.KismetMathLibrary");
        Require(rotationPin.Name == "ReturnValue", "Wrong velocity rotator output.");
        var (velocity, velocityPin) = graph.Follow(velocityRotation, "InVec");
        Require(velocity.Kind == "K2Node_VariableGet" && velocity.Member == "Velocity" && velocityPin.Name == "Velocity", "Wrong velocity input.");
        graph.Self(velocity);
        var (control, controlPin) = graph.Follow(sharedDelta!, "B");
        graph.Function(control, "GetControlRotation", "Engine.Pawn");
        Require(controlPin.Name == "ReturnValue", "Wrong control rotation output.");
        var (character, characterPin) = graph.Follow(control, "self");
        Require(character.Kind == "K2Node_VariableGet" && character.Member == "Character" && characterPin.Name == "Character", "Wrong control rotation owner.");
        graph.Self(character);
        var (sequence, sequencePin) = graph.FollowReroutes(setters[0], "execute");
        Require(sequence.Kind == "K2Node_ExecutionSequence" && sequencePin.Name == "then_1", "Yaw write execution order differs.");
        var entry = graph.One("K2Node_FunctionEntry", "UpdateRotationValues");
        graph.Link(sequence, "execute", entry, "then");
    }

    private static void ValidateUpdate(Graph graph)
    {
        var yaw = graph.One("K2Node_CallFunction", "UpdateRotationValues"); graph.Self(yaw);
        var movement = graph.One("K2Node_CallFunction", "UpdateMovementValues"); graph.Self(movement);
        graph.Link(yaw, "execute", movement, "then");
        var (macro, branch) = graph.Follow(movement, "execute");
        Require(macro.Kind == "K2Node_MacroInstance" && branch.Name == "WhileTrue" && macro.Body.Contains(
            "ALS_MacroLibrary.ALS_MacroLibrary:ML_DoWhile(TrueFalse)'", StringComparison.Ordinal), "Wrong moving update gate.");
        var shouldMove = graph.One("K2Node_VariableSet", "ShouldMove"); graph.Self(shouldMove);
        graph.Link(macro, "Condition", shouldMove, "Output_Get"); graph.Link(macro, "execute", shouldMove, "then");
        var check = graph.One("K2Node_CallFunction", "ShouldMoveCheck"); graph.Self(check);
        graph.Link(shouldMove, "ShouldMove", check, "Return Value");
        var (stateSwitch, statePin) = graph.FollowReroutes(shouldMove, "execute");
        Require(stateSwitch.Kind == "K2Node_SwitchEnum" && statePin.Name == "NewEnumerator1" && stateSwitch.Body.Contains(
            "ALS_MovementState.ALS_MovementState'", StringComparison.Ordinal), "Wrong grounded update gate.");
        var state = graph.One("K2Node_VariableGet", "MovementState"); graph.Self(state);
        graph.Link(stateSwitch, "Selection", state, "MovementState");
    }

    private static void ValidateMoveMacro(Graph graph)
    {
        var output = graph.WithPin("K2Node_Tunnel", "WhileTrue", false);
        var input = graph.WithPin("K2Node_Tunnel", "Condition", true);
        var (sequence, pin) = graph.FollowReroutes(output, "WhileTrue");
        Require(sequence.Kind == "K2Node_ExecutionSequence" && pin.Name == "then_1", "WhileTrue is no longer an unconditional true-branch continuation.");
        var (branch, truePin) = graph.Follow(sequence, "execute");
        Require(branch.Kind == "K2Node_IfThenElse" && truePin.Name == "then", "Wrong WhileTrue branch.");
        graph.Link(branch, "execute", input, "execute"); graph.Link(branch, "Condition", input, "Condition");
    }

    internal sealed record Pin(string Id, string Name, bool Output, string Links);
    internal sealed record Node(string Name, string Kind, string Member, string Body, Dictionary<string, Pin> Pins);
    internal sealed class Graph
    {
        private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
        public IEnumerable<Node> Nodes => _nodes.Values;
        public Graph(string text, bool directProperties = false)
        {
            var classes = Regex.Matches(text, @"(?m)^   Begin Object Class=/Script/\w+\.(\w+) Name=""([^""]+)""")
                .ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value, StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(text, @"(?ms)^   Begin Object Name=""([^""]+)""[^\r\n]*\r?\n(.*?)^   End Object"))
            {
                var name = match.Groups[1].Value; var body = match.Groups[2].Value;
                var member = Regex.Match(body, (directProperties ? @"(?m)^      " : "") + @"(?:VariableReference|FunctionReference)=\([^\r\n]*MemberName=""([^""]+)""").Groups[1].Value;
                var pins = new Dictionary<string, Pin>(StringComparer.Ordinal);
                foreach (Match line in Regex.Matches(body, (directProperties ? @"(?m)^      " : "") + @"CustomProperties Pin [^\r\n]+"))
                {
                    var pin = line.Value; var id = Regex.Match(pin, @"PinId=(\w+)").Groups[1].Value;
                    pins.Add(id, new(id, Regex.Match(pin, @"PinName=""([^""]+)""").Groups[1].Value,
                        pin.Contains("Direction=\"EGPD_Output\"", StringComparison.Ordinal), Regex.Match(pin, @"LinkedTo=\(([^)]*)\)").Groups[1].Value));
                }
                _nodes.Add(name, new(name, classes[name], member, body, pins));
            }
            Require(_nodes.Count > 0, "Empty native graph.");
        }
        public Node One(string kind, string member) => _nodes.Values.Single(n => n.Kind == kind && n.Member == member);
        public Node Named(string name) => _nodes[name];
        public void Links(Node node, string name, params (Node Node, string Pin)[] expected)
        {
            var pin = node.Pins.Values.Single(p => p.Name == name);
            var targets = Regex.Matches(pin.Links, @"(\w+) (\w+)").Select(m => m.Groups[1].Value + " " + m.Groups[2].Value).ToArray();
            var wanted = expected.Select(e => e.Node.Name + " " + e.Node.Pins.Values.Single(p => p.Name == e.Pin).Id).ToArray();
            Require(!pin.Output && targets.Length == wanted.Length && targets.Order().SequenceEqual(wanted.Order()), "Native control branch links differ.");
        }
        public string Literal(Node node, string name)
        {
            var pin = node.Pins.Values.Single(p => p.Name == name); Require(!pin.Output && pin.Links == "", "Connected native control literal.");
            var line = Regex.Matches(node.Body, @"CustomProperties Pin [^\r\n]+").Single(m => m.Value.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal)).Value;
            return Regex.Match(line, @"(?:^|,)DefaultValue=""([^""]*)""").Groups[1].Value;
        }
        public Node WithPin(string kind, string name, bool output) => _nodes.Values.Single(n => n.Kind == kind &&
            n.Pins.Values.Any(p => p.Name == name && p.Output == output));
        public void Self(Node node) => Require(node.Body.Contains("bSelfContext=True", StringComparison.Ordinal) &&
            !node.Body.Contains("MemberParent=", StringComparison.Ordinal) &&
            node.Pins.Values.Where(p => p.Name == "self").All(p => p.Links == ""), "Wrong self variable/function owner.");
        public void Function(Node node, string member, string owner) => Require(node.Kind == "K2Node_CallFunction" &&
            node.Member == member && node.Body.Contains("MemberParent=\"/Script/CoreUObject.Class'/Script/" + owner + "'\"", StringComparison.Ordinal),
            "Wrong native yaw function.");
        public (Node, Pin) Follow(Node node, string input)
        {
            var pin = node.Pins.Values.Single(p => p.Name == input && !p.Output);
            var links = Regex.Matches(pin.Links, @"(\w+) (\w+),"); Require(links.Count == 1, "Missing/ambiguous native input link.");
            var link = links[0]; var other = _nodes[link.Groups[1].Value]; var output = other.Pins[link.Groups[2].Value];
            Require(output.Output && output.Links.Contains(node.Name + " " + pin.Id + ",", StringComparison.Ordinal), "Native link is not reciprocal.");
            return (other, output);
        }
        public (Node, Pin) FollowReroutes(Node node, string input)
        {
            var (other, pin) = Follow(node, input); var count = 0;
            while (other.Kind == "K2Node_Knot")
            {
                Require(++count < 32 && pin.Name == "OutputPin", "Invalid reroute chain.");
                (other, pin) = Follow(other, "InputPin");
            }
            return (other, pin);
        }
        public void Link(Node node, string input, Node expected, string output)
        {
            var (other, pin) = Follow(node, input);
            Require(other == expected && pin.Name == output, "Native yaw connection differs.");
        }
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
}
