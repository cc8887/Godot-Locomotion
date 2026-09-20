using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsFootEnvironmentDefinition(AlsPelvisRigDefinition Pelvis, float TraceUpward, float TraceDownward,
    float WalkableAngle, double EnableThreshold, string FootHeightVariable, string LeftIkCurve, string RightIkCurve);

// Contract for the three exported environment functions, not the complete rig.
// Topology checks cover implementations, pin types, links and injected variables;
// authored numeric settings are read separately into the immutable definition.
public static class AlsFootEnvironmentCompiler
{
    public static AlsFootEnvironmentDefinition Compile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            root.GetProperty("source").GetString() == "/ALS/ALS/Character/CR_Als.CR_Als", "Unexpected foot environment source/schema.");
        var pelvis = new Graph(root.GetProperty("pelvis"), "3DBA0E1B9EBE838D5FD42DED27ACF5D0707F0443AC1F496EA722BA423B3F1F88");
        var offsets = new Graph(root.GetProperty("offsets"), "36FAAC2F19AB13CE93E600926B3FA7446E29227D74AC94E8151809F9A29001D2");
        var trace = new Graph(root.GetProperty("traceOffsets"), "FC8ACD602240F364F280FD84BC36A9AA1D226C65A7A224326F0E46C52243C09D");
        pelvis.Value("SetTranslation_1", "Space", "GlobalSpace");
        pelvis.Value("SetTranslation_1", "Item.Name", "pelvis");
        pelvis.Value("SetTranslation_1", "Item.Type", "Bone");
        pelvis.Value("SetTranslation_1", "bPropagateToChildren", "True");
        Require(pelvis.Number("SetTranslation_1", "Weight") == 1, "Changed pelvis write weight.");
        pelvis.Value("GetTransform_3", "Item.Name", "pelvis");
        pelvis.Value("GetTransform_3", "Space", "GlobalSpace");
        pelvis.Value("GetTransform_3", "bInitial", "False", nativeDefault: "False");
        const string spring = "SpringInterpV2_1_1";
        pelvis.Value(spring, "bUseCurrentInput", "false");
        pelvis.Value(spring, "bInitializeFromTarget", "true");
        Require(pelvis.Number(spring, "Force", 0) == 0 && pelvis.Number(spring, "TargetVelocityAmount") == 0,
            "Unsupported pelvis spring force/target velocity.");
        var minimum = pelvis.Number("Clamp", "Minimum"); var maximum = pelvis.Number("Clamp", "Maximum");
        var strength = Float(pelvis.Number(spring, "Strength")); var damping = Float(pelvis.Number(spring, "CriticalDamping"));
        Require(minimum <= maximum && strength >= 0 && damping >= 0, "Invalid pelvis spring limits/settings.");
        offsets.Value("GetCurveValue", "Curve", "FootLeftIk");
        offsets.Value("GetCurveValue_1", "Curve", "FootRightIk");
        const string unit = "AlsRigUnit_FootOffset";
        trace.Value(unit, "TraceChannel", "ECC_Visibility");
        // FootHeight is injected from a rig variable. Its displayed pin default
        // is not the runtime value and must not become a hardcoded asset setting.
        trace.Value(unit, "FootHeight.RigVMInjectionInfo_0.VariableNode.Variable", "FootHeight");
        var upward = Float(trace.Number(unit, "TraceDistanceUpward"));
        var downward = Float(trace.Number(unit, "TraceDistanceDownward"));
        var angle = Float(trace.Number(unit, "WalkableFloorAngle"));
        var threshold = trace.Number("GreaterEqual", "B");
        Require(upward >= 0 && downward >= 0 && angle is >= 0 and <= 90 && threshold > 0,
            "Invalid foot trace configuration.");
        return new(new(minimum, maximum, strength, damping), upward, downward, angle, threshold,
            "FootHeight", "FootLeftIk", "FootRightIk");
    }

    private static float Float(double value)
    {
        Require(float.IsFinite((float)value), "Foot setting exceeds float range.");
        return (float)value;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    internal sealed class Graph
    {
        private readonly JsonElement _nodes;
        public Graph(JsonElement nodes, string expected)
        {
            _nodes = nodes;
            var entries = new List<string>();
            foreach (var node in nodes.EnumerateArray())
            {
                var name = node.GetProperty("name").GetString()!; var kind = node.GetProperty("class").GetString()!;
                var properties = Lines(node);
                if (kind.EndsWith("RigVMLink", StringComparison.Ordinal))
                    entries.Add(string.Join("|", properties.Where(p => p.StartsWith("SourcePinPath=", StringComparison.Ordinal) || p.StartsWith("TargetPinPath=", StringComparison.Ordinal))));
                else
                {
                    entries.Add(name + "|" + kind + "|" + string.Join("|", properties.Where(p => p.StartsWith("ResolvedFunctionName=", StringComparison.Ordinal) || p.StartsWith("ScriptStructPath=", StringComparison.Ordinal))));
                    foreach (var pin in node.GetProperty("pins").EnumerateArray())
                    {
                        var pinName = pin.GetProperty("name").GetString()!;
                        entries.Add(name + "|" + pinName + "|" + string.Join("|", Lines(pin).Where(p =>
                            p.StartsWith("CPPType=", StringComparison.Ordinal) ||
                            p.StartsWith("CPPTypeObjectPath=", StringComparison.Ordinal) ||
                            p.StartsWith("Direction=", StringComparison.Ordinal))));
                        if (pinName == "Variable" || pinName.EndsWith(".Variable", StringComparison.Ordinal))
                            entries.Add(name + "|" + pinName + "|" + string.Join("|", Lines(pin).Where(p => p.StartsWith("DefaultValue=", StringComparison.Ordinal))));
                    }
                }
            }
            entries.Sort(StringComparer.Ordinal);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", entries))));
            Require(hash == expected, "Foot environment topology/variable binding changed: " + hash);
        }
        private string? Read(string nodeName, string pinName)
        {
            var node = _nodes.EnumerateArray().Single(n => n.GetProperty("name").GetString() == nodeName);
            var pin = node.GetProperty("pins").EnumerateArray().Single(p => p.GetProperty("name").GetString() == pinName);
            return Lines(pin).Where(p => p.StartsWith("DefaultValue=\"", StringComparison.Ordinal)).Select(p => p["DefaultValue=\"".Length..^1]).SingleOrDefault();
        }
        public void Value(string node, string pin, string expected, string? nativeDefault = null) =>
            Require((Read(node, pin) ?? nativeDefault) == expected, "Changed rig pin: " + node + "." + pin);
        public void Reference(string nodeName, string function)
        {
            var node = _nodes.EnumerateArray().Single(n => n.GetProperty("name").GetString() == nodeName);
            var header = Lines(node).Single(p => p.StartsWith("ReferencedFunctionHeader=", StringComparison.Ordinal));
            Require(header.Contains("LibraryNodePath=\"/ALS/ALS/Character/CR_Als.CR_Als:RigVMFunctionLibrary." + function + "\"", StringComparison.Ordinal) &&
                header.Contains("HostObject=\"/ALS/ALS/Character/CR_Als.CR_Als_C\"", StringComparison.Ordinal),
                "Changed rig function reference: " + nodeName);
        }
        public double Number(string node, string pin, double? nativeDefault = null)
        {
            var raw = Read(node, pin);
            if (raw is null && nativeDefault is { } fallback) return fallback;
            Require(double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value),
                "Missing/nonfinite rig parameter: " + node + "." + pin);
            return value;
        }
        private static string[] Lines(JsonElement item) => item.GetProperty("properties").EnumerateArray().Select(p => p.GetString()!).ToArray();
    }
}
