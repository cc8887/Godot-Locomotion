using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredRotatePlayer(bool Left, int PropertyIndex, string Source, bool DynamicLoop);

/// <summary>Original stance-local RotateInPlace player bindings. State relevance,
/// weights and entry resets remain the responsibility of the stance graph.</summary>
public sealed class AlsRefactoredRotatePlayers
{
    private readonly AlsRefactoredRotatePlayer[] _players;
    public ReadOnlySpan<AlsRefactoredRotatePlayer> Players => _players;
    public string CatalogDigest { get; }
    public static string Blueprint(bool crouching) => $"/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_{(crouching ? "Crouching" : "Standing")}.AB_Als_{(crouching ? "Crouching" : "Standing")}";
    public AlsRefactoredRotatePlayers(AlsRefactoredAnimationCatalog catalog, bool crouching)
    {
        CatalogDigest = catalog.IndexDigest; _players = Compile(catalog.Read(Blueprint(crouching)), crouching);
        foreach (var player in _players) Expect(catalog.Read(player.Source), new { @class = "AnimSequence" });
    }
    internal static AlsRefactoredRotatePlayer[] Compile(JsonElement payload, bool crouching)
    {
        var blueprint = Blueprint(crouching); Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        var nodes = payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray().Where(n =>
            n.GetProperty("class").GetString() == "AnimGraphNode_SequencePlayer" && n.GetProperty("runtime").GetProperty("sequence").GetString()!.Contains("/RotateInPlace/", StringComparison.Ordinal)).ToArray();
        if (nodes.Length != 2) throw new ArgumentException("Incomplete original rotation players.");
        var players = new List<AlsRefactoredRotatePlayer>();
        foreach (var left in new[] { true, false })
        {
            var name = "A_Als_" + (crouching ? "Crouch_" : "") + "Rotate_90_" + (left ? "Left" : "Right");
            var path = "/ALS/ALS/Animations/RotateInPlace/" + name + "." + name;
            var matches = nodes.Where(n => n.GetProperty("runtime").GetProperty("sequence").GetString() == path).ToArray();
            if (matches.Length != 1) throw new ArgumentException("Rotation player source differs.");
            var node = matches[0]; var graphPath = node.GetProperty("graph").GetString()!;
            var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, blueprint, graphPath), true);
            var authored = graph.Named(node.GetProperty("path").GetString()!.Split('.')[^1]);
            if (authored.Kind != "AnimGraphNode_SequencePlayer" || authored.Pins.Values.Any(p => !p.Output && p.Links != "") ||
                !authored.Body.Contains("Sequence=\"/Script/Engine.AnimSequence'" + path + "'\"", StringComparison.Ordinal)) throw new ArgumentException("Unsupported rotation player expression.");
            foreach (var policy in new[] { node.GetProperty("runtime"), node.GetProperty("authoredProperties").GetProperty("Node") })
            {
                Expect(policy, new { sequence = path, groupName = "None", method = "DoNotSync", groupRole = "CanBeLeader", startPosition = 0,
                    playRateBasis = 1, bLoopAnimation = true, bStartFromMatchingPose = false, bOverridePositionWhenJoiningSyncGroupAsLeader = false, bIgnoreForRelevancyTest = false });
                Clamp(policy.GetProperty("playRateScaleBiasClampConstants"));
                foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
            }
            var bindings = Regex.Matches(authored.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m => m.Groups[1].Value + ":" + string.Join(":", Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value))).Order().ToArray();
            var expected = new List<string> { "PlayRate:GetParent:RotateInPlaceState:PlayRate" };
            expected.Add("bLoopAnimation:GetParent:RotateInPlaceState:bRotating" + (left ? "Left" : "Right"));
            if (!bindings.SequenceEqual(expected.Order())) throw new ArgumentException("Rotation player binding differs: " + string.Join(",", bindings));
            players.Add(new(left, node.GetProperty("propertyIndex").GetInt32(), path, true));
        }
        return players.ToArray();
    }
    public AlsRefactoredSourcePlayerDefinition[] Bind(int firstPlayer)
    {
        ValidateFirst(firstPlayer);
        return _players.Select((p, i) => new AlsRefactoredSourcePlayerDefinition(firstPlayer + i, p.Source, -1)).ToArray();
    }
    public AlsRefactoredSourcePlayerInput Input(int firstPlayer, int localPlayer, float rate, bool rotatingLeft, bool rotatingRight, float weight, bool reinitialize = false)
    {
        ValidateFirst(firstPlayer);
        if ((uint)localPlayer >= 2 || !float.IsFinite(rate) || !float.IsFinite(weight) || weight is < 0 or > 1) throw new ArgumentException("Invalid rotation source input.");
        var player = _players[localPlayer];
        return new(firstPlayer + localPlayer, default, rate, weight, reinitialize, Looping: player.DynamicLoop ? player.Left ? rotatingLeft : rotatingRight : null);
    }
    private static void ValidateFirst(int first) { if (first < 0 || first > int.MaxValue - 2) throw new ArgumentOutOfRangeException(nameof(first)); }
}
