using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using static GodotAls.Import.Compilation.AlsOverlayTransitionCompiler;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public static class AlsStopTransitionCompiler
{
    public static AlsSequenceMontageAsset[] CompileAssets(string skeletonJson, AlsAnimationSetDefinition set, AlsStopTransitionDefinition definition)
    {
        using var document = JsonDocument.Parse(skeletonJson);
        var text = document.RootElement.GetProperty("skeletonText").GetString()!;
        var groups = Regex.Matches(text, @"(?m)^   SlotGroups\((\d+)\)=\(([^\r\n]+)\)").Cast<Match>()
            .Where(m => Regex.Match(m.Groups[2].Value, @"SlotNames=\(([^)]+)\)").Groups[1].Value.Split(',').Contains("\"Grounded Slot\"")).ToArray();
        if (groups.Length != 1) throw new ArgumentException("Stop has no unique native Grounded Slot group.");
        var group = int.Parse(groups[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return definition.Bindings.ToArray().Select(b =>
        {
            var asset = set.Animations[b.AnimationId];
            if (set.Skeletons[asset.SkeletonId].ObjectPath != document.RootElement.GetProperty("skeleton").GetString() ||
                asset.AdditiveType != b.AdditiveType || asset.RootMotionEnabled)
                throw new ArgumentException("Stop montage asset policy differs.");
            return new AlsSequenceMontageAsset(asset.Id, definition.Slot, group, asset.PlayLength, asset.AdditiveType);
        }).ToArray();
    }

    public static string MergeNotifyMetadata(string overlay, string stop)
    {
        var result = JsonNode.Parse(overlay)!.AsObject(); var extra = JsonNode.Parse(stop)!.AsObject();
        foreach (var field in new[] { "schemaVersion", "source", "notifySchemaVersion" })
            if (!JsonNode.DeepEquals(result[field], extra[field])) throw new ArgumentException("Dynamic notify metadata owners differ.");
        var rows = result["syncAssets"]!.AsArray();
        foreach (var row in extra["syncAssets"]!.AsArray()) rows.Add(row!.DeepClone());
        if (rows.Select(r => r!["path"]!.GetValue<string>()).Distinct().Count() != rows.Count)
            throw new ArgumentException("Duplicate dynamic sequence notify owner.");
        return result.ToJsonString();
    }

    public static AlsStopTransitionDefinition Compile(string json, AlsAnimationSetDefinition set, AlsGroundedMachinesProfile machines)
    {
        const string source = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("source").GetString() != source ||
            machines.Stop.Runtime.Kind != AlsGroundedMachineKind.Stop)
            throw new ArgumentException("Foreign Stop notify graph.");
        var row = root.GetProperty("graphs").EnumerateArray().Single(g => g.GetProperty("name").GetString() == "EventGraph");
        if (row.GetProperty("path").GetString() != source + ":EventGraph") throw new ArgumentException("Foreign Stop event owner.");
        var graph = new Graph(row.GetProperty("nativeText").GetString()!, true);
        CheckPlayer(graph);
        var bindings = new AlsStopTransitionBinding[2];
        for (var side = 0; side < 2; side++)
        {
            var name = side == 0 ? "->N Stop L" : "->N Stop R";
            var id = machines.Stop.Runtime.States[3 + side].StartNotify;
            if (id < 0 || machines.Stop.Runtime.States[5 + side].StartNotify != id || machines.NotifyNames[id] != name)
                throw new ArgumentException("Lock/Plant generated notifications differ from their authored consumer.");
            var entry = Event(graph, "K2Node_Event", "AnimNotify_" + name);
            var play = Next(graph, entry, "then"); graph.Self(play);
            if (play.Kind != "K2Node_CallFunction" || play.Member != "PlayTransition")
                throw new ArgumentException("Stop notification must directly play its transition without an Overlay gate.");
            Unlinked(play, "then");
            var path = ObjectLiteral(graph, play, Parameter(play, "Animation"));
            var asset = set.Animations.Single(a => a.ObjectPath == path);
            var expected = source[..source.LastIndexOf('/')] + "/AnimationExamples/Base/Transitions/ALS_N_Stop_" +
                (side == 0 ? "L" : "R") + "_Down";
            if (asset.ObjectPath != expected + "." + expected[(expected.LastIndexOf('/') + 1)..] ||
                asset.RootMotionEnabled || asset.AdditiveType != 2)
                throw new ArgumentException("Wrong Stop resource, additive type or root motion policy.");
            var start = Scalar(graph, play, Parameter(play, "StartTime"));
            if (start >= asset.PlayLength) throw new ArgumentException("Stop starts beyond its source duration.");
            bindings[side] = new(id, name, asset.Id, asset.AdditiveType,
                Scalar(graph, play, Parameter(play, "BlendInTime")), Scalar(graph, play, Parameter(play, "BlendOutTime")),
                Scalar(graph, play, Parameter(play, "PlayRate")), start);
        }
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json + "\n" + machines.Digest + "\n" + set.DefinitionDigest)));
        return new(digest, bindings);
    }
}
