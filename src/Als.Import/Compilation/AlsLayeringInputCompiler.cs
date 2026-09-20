using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsLayeringInputCompiler
{
    private const string Source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string Compact = "GetAnimCurve_Compact";

    public static AlsLayeringInputModel Compile(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("source").GetString() == Source,
            "Unexpected layering input source.");
        ValidateCompactCurveReader(ReadGraph(root, Compact));
        var graph = ReadGraph(root, "UpdateLayerValues");
        var entry = graph.One("K2Node_FunctionEntry", "UpdateLayerValues");
        var sequence = graph.One("K2Node_ExecutionSequence", "");
        graph.Link(sequence, "execute", entry, "then");
        Require(sequence.Pins.Values.Count(p => p.Output && p.Name.StartsWith("then_", StringComparison.Ordinal)) == 6,
            "Layering update execution branch count changed.");
        string[][] branches = [
            ["Enable_AimOffset"], ["BasePose_N", "BasePose_CLF"],
            ["Spine_Add", "Head_Add", "Arm_L_Add", "Arm_R_Add"], ["Hand_R", "Hand_L"],
            ["Enable_HandIK_L", "Enable_HandIK_R"], ["Arm_L_LS", "Arm_L_MS", "Arm_R_LS", "Arm_R_MS"],
        ];
        for (var branch = 0; branch < branches.Length; branch++)
        {
            Node previous = sequence;
            for (var index = 0; index < branches[branch].Length; index++)
            {
                var setter = Setter(graph, branches[branch][index]);
                var (from, pin) = graph.FollowReroutes(setter, "execute");
                Require(from == previous && pin.Name == (index == 0 ? "then_" + branch : "then"),
                    "Layering assignments execute in a different order.");
                previous = setter;
            }
            Require(previous.Pins.Values.Single(p => p.Name == "then" && p.Output).Links.Length == 0,
                "Unexpected execution after a layering assignment branch.");
        }

        var names = new string[AlsLayeringInputModel.CurveCount];
        var aim = FunctionInput(graph, Setter(graph, "Enable_AimOffset"), "Enable_AimOffset", "Lerp");
        Literal(graph, aim, "A", "1.000000"); Literal(graph, aim, "B", "0.0");
        names[(int)AlsLayeringCurve.AimOffsetMask] = CurveInput(graph, aim, "Alpha", "Mask_AimOffset");
        foreach (var (variable, curve, role) in new[] {
            ("BasePose_N", "BasePose_N", AlsLayeringCurve.BasePoseNormal),
            ("BasePose_CLF", "BasePose_CLF", AlsLayeringCurve.BasePoseCrouching),
            ("Spine_Add", "Layering_Spine_Add", AlsLayeringCurve.SpineAdditive),
            ("Head_Add", "Layering_Head_Add", AlsLayeringCurve.HeadAdditive),
            ("Arm_L_Add", "Layering_Arm_L_Add", AlsLayeringCurve.LeftArmAdditive),
            ("Arm_R_Add", "Layering_Arm_R_Add", AlsLayeringCurve.RightArmAdditive),
            ("Hand_L", "Layering_Hand_L", AlsLayeringCurve.LeftHand),
            ("Hand_R", "Layering_Hand_R", AlsLayeringCurve.RightHand),
            ("Arm_L_LS", "Layering_Arm_L_LS", AlsLayeringCurve.LeftArmLocalSpace),
            ("Arm_R_LS", "Layering_Arm_R_LS", AlsLayeringCurve.RightArmLocalSpace),
        }) names[(int)role] = CurveInput(graph, Setter(graph, variable), variable, curve);
        foreach (var (side, enable, layer) in new[] {
            ("L", AlsLayeringCurve.LeftHandIk, AlsLayeringCurve.LeftArmLayer),
            ("R", AlsLayeringCurve.RightHandIk, AlsLayeringCurve.RightArmLayer),
        })
        {
            var variable = "Enable_HandIK_" + side;
            var lerp = FunctionInput(graph, Setter(graph, variable), variable, "Lerp");
            Literal(graph, lerp, "A", "0.0");
            names[(int)enable] = CurveInput(graph, lerp, "B", variable);
            names[(int)layer] = CurveInput(graph, lerp, "Alpha", "Layering_Arm_" + side);
            var mesh = "Arm_" + side + "_MS";
            var integerToDouble = FunctionInput(graph, Setter(graph, mesh), mesh, "Conv_IntToDouble");
            var subtraction = FunctionInput(graph, integerToDouble, "InInt", "Subtract_IntInt");
            Literal(graph, subtraction, "A", "1");
            var floor = FunctionInput(graph, subtraction, "B", "FFloor");
            graph.Link(floor, "A", Setter(graph, "Arm_" + side + "_LS"), "Output_Get");
        }
        var model = new AlsLayeringInputModel(names);
        ValidateNativeFloor(root, model);
        return model;
    }

    private static void ValidateNativeFloor(JsonElement root, AlsLayeringInputModel model)
    {
        float[] inputs = [-float.MaxValue, -2147483904f, -2147483648f, -2.1f, -1, -.1f, 0, .99f, 1, 1.1f, 2,
            2147483520f, 2147483648f, float.MaxValue];
        var samples = root.GetProperty("floorVerification");
        Require(samples.GetArrayLength() == inputs.Length, "Incomplete native layering Floor verification.");
        string[] names = [model.CurveNames[(int)AlsLayeringCurve.LeftArmLocalSpace]];
        Span<AlsInertialCurve> curves = stackalloc AlsInertialCurve[1];
        var index = 0;
        foreach (var sample in samples.EnumerateArray())
        {
            var value = sample.GetProperty("curveValue").GetDouble();
            Require(value == (double)inputs[index++], "Native layering Floor verification domain changed.");
            curves[0] = new((float)value);
            var output = model.Evaluate(new AlsFrameIdentity(2, 0, 1), new AlsFrameIdentity(1, 0, 1), names, curves);
            Require(output.LeftArmMeshSpace == unchecked(1 - sample.GetProperty("floorValue").GetInt32()),
                "Layering mesh weight differs from the native integer Floor result.");
        }
    }

    private static void ValidateCompactCurveReader(Graph graph)
    {
        var input = graph.WithPin("K2Node_Tunnel", "Name", true);
        var output = graph.WithPin("K2Node_Tunnel", "Value", false);
        PinType(input, "Name", "name"); PinType(output, "Value", "real", "double");
        var (read, value) = graph.Follow(output, "Value");
        Require(read.Kind == "K2Node_CallFunction" && read.Member == "GetCurveValue" && value.Name == "ReturnValue",
            "Compact curve reader is not an unmodified AnimInstance.GetCurveValue.");
        graph.Self(read);
        PinType(read, "CurveName", "name"); PinType(read, "ReturnValue", "real", "float");
        graph.Link(read, "CurveName", input, "Name");
    }

    private static string CurveInput(Graph graph, Node node, string pin, string expected)
    {
        var (reader, output) = graph.Follow(node, pin);
        Require(reader.Kind == "K2Node_MacroInstance" && output.Name == "Value" &&
            (reader.Body.Contains("MacroGraph=\"/Script/Engine.EdGraph'ALS_AnimBP:" + Compact + "'\"", StringComparison.Ordinal) ||
             reader.Body.Contains("MacroGraph=\"/Script/Engine.EdGraph'" + Source + ":" + Compact + "'\"", StringComparison.Ordinal)) &&
            (reader.Body.Contains("GraphBlueprint=\"/Script/Engine.AnimBlueprint'ALS_AnimBP'\"", StringComparison.Ordinal) ||
             reader.Body.Contains("GraphBlueprint=\"/Script/Engine.AnimBlueprint'" + Source + "'\"", StringComparison.Ordinal)),
            "Layering curve reader belongs to another macro or animation instance.");
        PinType(reader, "Name", "name"); PinType(reader, "Value", "real", "double");
        var name = graph.Literal(reader, "Name");
        Require(name == expected, "Layering consumes a different curve: " + expected);
        return name;
    }
    private static Node FunctionInput(Graph graph, Node node, string input, string function)
    {
        var (source, output) = graph.Follow(node, input);
        graph.Function(source, function, "Engine.KismetMathLibrary");
        Require(output.Name == "ReturnValue", "Layering formula uses a different function output.");
        PinType(source, "ReturnValue", function is "FFloor" or "Subtract_IntInt" ? "int" : "real",
            function is "FFloor" or "Subtract_IntInt" ? "" : "double");
        foreach (var pin in function switch
        {
            "Lerp" => new[] { "A", "B", "Alpha" }, "FFloor" => ["A"],
            "Subtract_IntInt" => ["A", "B"], "Conv_IntToDouble" => ["InInt"],
            _ => throw new ArgumentException("Unsupported layering input function."),
        }) PinType(source, pin, function is "Subtract_IntInt" or "Conv_IntToDouble" ? "int" : "real",
            function is "Subtract_IntInt" or "Conv_IntToDouble" ? "" : "double");
        return source;
    }
    private static Node Setter(Graph graph, string variable)
    {
        var node = graph.One("K2Node_VariableSet", variable); graph.Self(node);
        PinType(node, variable, "real", "double"); PinType(node, "Output_Get", "real", "double");
        return node;
    }
    private static void PinType(Node node, string name, string category, string subcategory = "")
    {
        var pin = node.Pins.Values.Single(p => p.Name == name);
        var line = Regex.Matches(node.Body, @"CustomProperties Pin [^\r\n]+")
            .Single(m => m.Value.Contains("PinId=" + pin.Id + ",", StringComparison.Ordinal)).Value;
        Require(line.Contains("PinType.PinCategory=\"" + category + "\"", StringComparison.Ordinal) &&
            line.Contains("PinType.PinSubCategory=\"" + subcategory + "\"", StringComparison.Ordinal),
            "Layering numeric type or precision changed.");
    }
    private static Graph ReadGraph(JsonElement root, string name)
    {
        var graph = root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("path").GetString() == Source + ":" + name);
        Require(graph.GetProperty("name").GetString() == name, "Layering graph identity changed.");
        return new(graph.GetProperty("nativeText").GetString()!);
    }
    private static void Literal(Graph graph, Node node, string pin, string expected) =>
        Require(graph.Literal(node, pin) == expected, "Layering formula literal changed.");
    private static void Require(bool condition, string message)
    { if (!condition) throw new ArgumentException("Layering input: " + message); }
}
