using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Exact eight graph-local producers. Callers bind only the sites they own and
// place them before enclosing blends; this is not a V4 name remapping.
public static class AlsRefactoredPoseCurveCompiler
{
    private const string Prefix = "/ALS/ALS/Character/AnimationInstances/";
    private const string Air = "AB_Als_Locomotion.AB_Als_Locomotion:AnimGraph.AnimGraphNode_StateMachine_9.Locomotion States.";
    private static readonly (AlsRefactoredPoseCurveSite Site, string Path, string Hash)[] Expected =
    [
        (AlsRefactoredPoseCurveSite.Grounded, "AB_Als_Grounded.AB_Als_Grounded:AnimGraph.AnimGraphNode_ModifyCurve_6", "F65D9BC965030F99C1ED918150D8B877EBAAA6B58117567C2350E6B15BF49F83"),
        (AlsRefactoredPoseCurveSite.FallPose, Air + "AnimStateNode_1.Fall.AnimGraphNode_ModifyCurve_0", "296AA8931BC83C60792FF2C75445023FC3028FCB57A8489AFEA57DFB97F5AE9E"),
        (AlsRefactoredPoseCurveSite.FallFeet, Air + "AnimStateNode_1.Fall.AnimGraphNode_ModifyCurve_3", "894246AB7B0FBA5148906FBF236CD418F0B3C12A178C5F94C406D6CC3CE04810"),
        (AlsRefactoredPoseCurveSite.JumpPose, Air + "AnimStateNode_2.Jump.AnimGraphNode_ModifyCurve_0", "DDDB32CCBBFE5C332D4E4BB7FE890ECAA0B47A686A1E44D82AC20934379433B2"),
        (AlsRefactoredPoseCurveSite.JumpFeet, Air + "AnimStateNode_2.Jump.AnimGraphNode_ModifyCurve_3", "BE8EA73E503C66B1D6696519FFE6766C8B04927E3B296F94B01B9DD85E72BEE9"),
        (AlsRefactoredPoseCurveSite.Land, Air + "AnimStateNode_5.Land.AnimGraphNode_ModifyCurve_0", "8E36500D03D5C2D05DC04A2450088C371E34417422109A5D4E5FC2EC2B7290D7"),
        (AlsRefactoredPoseCurveSite.StandingMovement, "Stances/AB_Als_Standing.AB_Als_Standing:AnimGraph.AnimGraphNode_ModifyCurve_1", "EEAEB9B083B731FA8A546F734A9C9EC5351ADBE570CB16D4035A90D6DC5DABF3"),
        (AlsRefactoredPoseCurveSite.CrouchingMovement, "Stances/AB_Als_Crouching.AB_Als_Crouching:AnimGraph.AnimGraphNode_ModifyCurve_1", "FD812C83DF0C279A6F18DF9935D5E71EC27E387B7FB33422C5BEBAFA7B1CE1B0"),
    ];

    public static AlsRefactoredPoseCurveWrite[] Compile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("writers").GetArrayLength() != Expected.Length)
            throw new InvalidDataException("Unexpected Refactored pose curve schema/count.");
        var writers = root.GetProperty("writers").EnumerateArray().ToDictionary(w => Text(w, "path"));
        var output = new AlsRefactoredPoseCurveWrite[Expected.Length];
        foreach (var expected in Expected)
        {
            var path = Prefix + expected.Path;
            if (!writers.TryGetValue(path, out var writer)) throw new InvalidDataException("Missing pose curve site: " + path);
            var node = writer.GetProperty("properties").EnumerateArray().Select(p => p.GetString()!).Where(p => p.StartsWith("Node=", StringComparison.Ordinal)).ToArray();
            var bindings = writer.GetProperty("bindings").EnumerateArray().SelectMany(b => b.GetProperty("properties").EnumerateArray())
                .Select(p => p.GetString()!).Where(p => p.StartsWith("PropertyBindings=", StringComparison.Ordinal)).ToArray();
            var lines = new List<string> { path, Text(writer, "class") };
            lines.AddRange(node); lines.AddRange(bindings);
            lines.AddRange(writer.GetProperty("pins").EnumerateArray().Select(p =>
                Text(p, "name") + "|" + Text(p, "id") + "|" + Text(p, "links") + "|" + (p.GetProperty("value").GetString() ?? "<null>")));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
            if (hash != expected.Hash) throw new InvalidDataException("Pose curve producer/input/placement changed: " + path);
            var names = Regex.Matches(Regex.Match(node.Single(), "CurveNames=\\(([^)]*)\\)").Groups[1].Value, "\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToArray();
            var values = new float[names.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var pin = writer.GetProperty("pins").EnumerateArray().Single(p => Text(p, "name") == "CurveValues_" + i);
                values[i] = float.Parse(Text(pin, "value"), CultureInfo.InvariantCulture);
            }
            var prediction = expected.Site is AlsRefactoredPoseCurveSite.FallFeet or AlsRefactoredPoseCurveSite.JumpFeet;
            if (prediction && (bindings.Length != 1 || !bindings[0].Contains("PropertyPath=(\"GetParent\",\"InAirState\",\"GroundPredictionAmount\")", StringComparison.Ordinal)) ||
                !prediction && bindings.Length != 0) throw new InvalidDataException("Changed pose curve alpha binding.");
            output[(int)expected.Site] = new(expected.Site, path, names, values, prediction);
        }
        return output;
    }
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;
}
