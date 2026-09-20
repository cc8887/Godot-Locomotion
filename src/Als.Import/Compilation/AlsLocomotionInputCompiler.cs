using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsLocomotionInputProfile(AlsStandingMovementSettings Movement, float PivotSpeedLimit, string Digest);

public static class AlsLocomotionInputCompiler
{
    private const string Anim = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string Character = "/Game/AdvancedLocomotionV4/Blueprints/CharacterLogic/ALS_Base_CharacterBP.ALS_Base_CharacterBP";

    public static AlsLocomotionInputProfile Compile(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        RejectDuplicates(root);
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("inputSchemaVersion").GetInt32() == 1 &&
            Text(root, "source") == Anim && Text(root, "characterSource") == Character, "Wrong locomotion input source.");
        var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"), StringComparer.Ordinal);
        var essential = Nodes(Character + ":SetEssentialValues");
        var moving = Set(essential, "IsMoving");
        var amount = Set(essential, "MovementInputAmount");
        var hasInput = Set(essential, "HasMovementInput");
        var speed = Set(essential, "Speed");
        var movingThreshold = Threshold(essential, moving, "IsMoving");
        var inputThreshold = Threshold(essential, hasInput, "HasMovementInput");
        Require(Expr(essential, moving, "IsMoving") == $"gt(Speed,{Number(movingThreshold)})" &&
            Expr(essential, hasInput, "HasMovementInput") == $"gt(MovementInputAmount,{Number(inputThreshold)})",
            "Unexpected essential-value predicates.");
        Require(Expr(essential, speed, "Speed") == "length(GetVelocity.ReturnValue_X,GetVelocity.ReturnValue_Y,0)",
            "Speed must use the unfiltered horizontal velocity.");
        Require(Expr(essential, amount, "MovementInputAmount") == "divide(length(GetCurrentAcceleration),GetMaxAcceleration)",
            "MovementInputAmount must come from input acceleration, not velocity differentiation.");
        Require(Link(Pin(moving, "execute")) == (Text(speed, "name"), "then") &&
            Link(Pin(hasInput, "execute")) == (Text(amount, "name"), "then"), "Essential-value update order differs.");

        var getters = Nodes(Character + ":BPI_Get_EssentialValues");
        var getterResult = One(getters.Values.Where(n => Text(n, "class") == "K2Node_FunctionResult"));
        foreach (var name in new[] { "Speed", "IsMoving", "HasMovementInput", "MovementInputAmount" })
            Require(Expr(getters, getterResult, name) == name, "Character interface changes an essential value.");
        Require(Expr(getters, getterResult, "MovementInput") == "GetCurrentAcceleration" &&
            Expr(getters, getterResult, "Velocity") == "GetVelocity", "Character interface source differs.");

        var update = Nodes(Anim + ":UpdateCharacterInfo");
        var message = One(update.Values.Where(n => Text(n, "class") == "K2Node_Message" &&
            Text(n.GetProperty("properties").GetProperty("FunctionReference"), "memberName") == "BPI_Get_EssentialValues"));
        Require(Text(message.GetProperty("properties").GetProperty("FunctionReference"), "memberParent") ==
            "/Game/AdvancedLocomotionV4/Blueprints/Interfaces/ALS_Character_BPI.ALS_Character_BPI_C", "Wrong essential interface owner.");
        foreach (var name in new[] { "Speed", "IsMoving", "HasMovementInput", "MovementInputAmount", "MovementInput", "Velocity" })
            Require(Link(Pin(Set(update, name), name)) == (Text(message, "name"), name), "AnimBP input is not the character interface value.");

        var check = Nodes(Anim + ":ShouldMoveCheck");
        var checkResult = One(check.Values.Where(n => Text(n, "class") == "K2Node_FunctionResult"));
        var forceCompare = One(check.Values.Where(n => Text(n, "class") == "K2Node_CallFunction" &&
            Text(n.GetProperty("properties").GetProperty("FunctionReference"), "memberName") == "Greater_DoubleDouble"));
        var forceThreshold = Constant(Pin(forceCompare, "B"));
        Require(Expr(check, checkResult, "Return Value") == $"or(and(IsMoving,HasMovementInput),gt(Speed,{Number(forceThreshold)}))",
            "Unsupported ShouldMove predicate.");
        var settings = new AlsStandingMovementSettings(movingThreshold * .01f, forceThreshold * .01f, inputThreshold);
        _ = AlsStandingMovementInputModel.ShouldMove(false, false, 0, settings);
        var pivotLimit = root.GetProperty("triggerPivotSpeedLimit").GetSingle();
        Require(float.IsFinite(pivotLimit) && pivotLimit >= 0, "Invalid authored Pivot speed limit.");
        return new(settings, pivotLimit * .01f, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));

        Dictionary<string, JsonElement> Nodes(string path) => graphs[path].GetProperty("nodes").EnumerateArray()
            .ToDictionary(n => Text(n, "name"), StringComparer.Ordinal);
    }

    private static string Expr(Dictionary<string, JsonElement> nodes, JsonElement node, string pinName, int depth = 0)
    {
        Require(depth < 24, "Cyclic input expression.");
        var pin = Pin(node, pinName);
        Require(Text(pin, "direction") == "input", "Expected input pin.");
        if (pin.GetProperty("links").GetArrayLength() == 0) return Number(Constant(pin));
        var (nodeName, output) = Link(pin);
        var source = nodes[nodeName];
        var kind = Text(source, "class");
        var properties = source.GetProperty("properties");
        if (kind is "K2Node_VariableGet" or "K2Node_VariableSet")
        {
            var reference = properties.GetProperty("VariableReference");
            var name = Text(reference, "memberName");
            Require(reference.GetProperty("bSelfContext").GetBoolean() && Text(reference, "memberParent") == "" &&
                output == (kind == "K2Node_VariableSet" ? "Output_Get" : name), "Unexpected variable owner/output.");
            return name;
        }
        Require(kind is "K2Node_CallFunction" or "K2Node_CommutativeAssociativeBinaryOperator", "Unsupported input expression node.");
        var function = properties.GetProperty("FunctionReference");
        var functionName = Text(function, "memberName");
        string Read(string name) => Expr(nodes, source, name, depth + 1);
        if (functionName == "GetVelocity")
        {
            Require(function.GetProperty("bSelfContext").GetBoolean() && Text(function, "memberParent") == "" &&
                Pin(source, "self").GetProperty("links").GetArrayLength() == 0 &&
                output is "ReturnValue" or "ReturnValue_X" or "ReturnValue_Y", "Unexpected velocity source.");
            return output == "ReturnValue" ? "GetVelocity" : "GetVelocity." + output;
        }
        Require(output == "ReturnValue", "Wrong input function output.");
        if (functionName is "GetCurrentAcceleration" or "GetMaxAcceleration")
        {
            Require(Text(function, "memberParent") == "/Script/Engine.CharacterMovementComponent" && Read("self") == "CharacterMovement",
                "Wrong movement component input source.");
            return functionName;
        }
        Require(Text(function, "memberParent") == "/Script/Engine.KismetMathLibrary", "Wrong math function owner.");
        if (functionName == "VSize")
            return Pin(source, "A").GetProperty("links").GetArrayLength() > 0 ? $"length({Read("A")})" :
                $"length({Read("A_X")},{Read("A_Y")},{Read("A_Z")})";
        var operation = functionName switch
        {
            "Greater_DoubleDouble" => "gt", "Divide_DoubleDouble" => "divide",
            "BooleanAND" => "and", "BooleanOR" => "or",
            _ => throw new ArgumentException("Unsupported input function: " + functionName),
        };
        Require(source.GetProperty("pins").EnumerateArray().Where(p => Text(p, "direction") == "input")
            .All(p => Text(p, "name") is "self" or "A" or "B"), "Additional input operands are not supported.");
        return $"{operation}({Read("A")},{Read("B")})";
    }

    private static float Threshold(Dictionary<string, JsonElement> nodes, JsonElement set, string name)
    {
        var (source, output) = Link(Pin(set, name));
        Require(output == "ReturnValue", "Unexpected threshold output.");
        return Constant(Pin(nodes[source], "B"));
    }
    private static JsonElement Set(Dictionary<string, JsonElement> nodes, string name) => One(nodes.Values.Where(n =>
        Text(n, "class") == "K2Node_VariableSet" && Text(n.GetProperty("properties").GetProperty("VariableReference"), "memberName") == name));
    private static JsonElement Pin(JsonElement node, string name) => One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == name));
    private static (string Node, string Pin) Link(JsonElement pin)
    {
        var link = One(pin.GetProperty("links").EnumerateArray());
        return (Text(link, "node"), Text(link, "pin"));
    }
    private static float Constant(JsonElement pin)
    {
        Require(pin.GetProperty("links").GetArrayLength() == 0, "Expected authored constant.");
        var value = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
        Require(float.IsFinite(value), "Non-finite input constant.");
        return value;
    }
    private static JsonElement One(IEnumerable<JsonElement> values)
    {
        var array = values.ToArray();
        Require(array.Length == 1, "Missing or ambiguous input source.");
        return array[0];
    }
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool value, string message) { if (!value) throw new ArgumentException(message); }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), "Duplicate input JSON property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }
}
