using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Sync;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public readonly record struct AlsRefactoredMovementPlayer(int PropertyIndex, string Source, string Group,
    float Start, bool Loop, float Rate, float RateBasis, string RateBinding, bool BlendSpace);
public readonly record struct AlsRefactoredMovementPlayerInput(float StandingRate, float CrouchingRate, float Stride, float WalkRun);

/// <summary>State-local movement player identities, excluding the separately
/// compiled RotateInPlace players. No state weights or callbacks are invented.</summary>
public sealed class AlsRefactoredMovementPlayers
{
    private readonly AlsRefactoredMovementPlayer[] _players;
    public ReadOnlySpan<AlsRefactoredMovementPlayer> Players => _players;
    public string CatalogDigest { get; }
    public AlsRefactoredMovementPlayers(AlsRefactoredAnimationCatalog catalog, bool crouching)
    {
        CatalogDigest = catalog.IndexDigest;
        _players = Compile(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(crouching)), crouching);
        foreach (var p in _players) Expect(catalog.Read(p.Source), new { @class = p.BlendSpace ? "BlendSpace" : "AnimSequence" });
    }
    internal static AlsRefactoredMovementPlayer[] Compile(JsonElement payload, bool crouching)
    {
        var blueprint = AlsRefactoredRotatePlayers.Blueprint(crouching); Expect(payload, new { source = blueprint, @class = "AnimBlueprint" });
        var players = new List<AlsRefactoredMovementPlayer>();
        foreach (var node in payload.GetProperty("compiled").GetProperty("nodes").EnumerateArray())
        {
            var kind = node.GetProperty("class").GetString();
            if (kind is not ("AnimGraphNode_SequencePlayer" or "AnimGraphNode_BlendSpacePlayer")) continue;
            var blend = kind == "AnimGraphNode_BlendSpacePlayer"; var runtime = node.GetProperty("runtime");
            var path = runtime.GetProperty(blend ? "blendSpace" : "sequence").GetString()!;
            if (path.Contains("/RotateInPlace/", StringComparison.Ordinal)) continue;
            if (!path.StartsWith("/ALS/ALS/Animations/Grounded/", StringComparison.Ordinal)) throw new ArgumentException("Unexpected movement source.");
            var graph = new AlsYawOffsetCompiler.Graph(AlsNativeNestedGraph.Extract(payload.GetProperty("nativeText").GetString()!, blueprint, node.GetProperty("graph").GetString()!), true);
            var authored = graph.Named(node.GetProperty("path").GetString()!.Split('.')[^1]);
            if (authored.Kind != kind || authored.Pins.Values.Any(p => !p.Output && p.Links != "")) throw new ArgumentException("Unsupported movement expression.");
            var group = runtime.GetProperty("groupName").GetString()!;
            if (group is not ("Movement" or "Run Start" or "First Pivot" or "Second Pivot")) throw new ArgumentException("Unexpected movement sync group.");
            var start = runtime.GetProperty("startPosition").GetSingle(); var loop = runtime.GetProperty(blend ? "bLoop" : "bLoopAnimation").GetBoolean();
            var rate = runtime.GetProperty("playRate").GetSingle();
            var basis = blend ? 1 : runtime.GetProperty("playRateBasis").GetSingle();
            foreach (var policy in new[] { runtime, node.GetProperty("authoredProperties").GetProperty("Node") })
            {
                Expect(policy, new { groupName = group, groupRole = "CanBeLeader", method = "SyncGroup", bOverridePositionWhenJoiningSyncGroupAsLeader = false, bIgnoreForRelevancyTest = false });
                if (policy.GetProperty(blend ? "blendSpace" : "sequence").GetString() != path || policy.GetProperty(blend ? "bLoop" : "bLoopAnimation").GetBoolean() != loop) throw new ArgumentException("Movement policies differ.");
                if (blend) Expect(policy, new { bResetPlayTimeWhenBlendSpaceChanges = true });
                else { Expect(policy, new { bStartFromMatchingPose = false }); if (policy.GetProperty("playRateBasis").GetSingle() != basis) throw new ArgumentException("Movement rate basis differs."); Clamp(policy.GetProperty("playRateScaleBiasClampConstants")); }
                foreach (var cb in new[] { "initialUpdateFunction", "becomeRelevantFunction", "updateFunction" }) Expect(policy.GetProperty(cb), new { functionName = "None" });
            }
            // Exposed literal pins override the editor Node's default value.
            // Compare as UE floats, not JSON doubles produced by serialization.
            var authoredStart = authored.Pins.Values.Any(p => p.Name == "StartPosition" && !p.Output)
                ? float.Parse(graph.Literal(authored, "StartPosition"), CultureInfo.InvariantCulture)
                : node.GetProperty("authoredProperties").GetProperty("Node").GetProperty("startPosition").GetSingle();
            if (authoredStart != start) throw new ArgumentException("Movement start position differs from its authored input.");
            var assetToken = (blend ? "BlendSpace=\"/Script/Engine.BlendSpace'" : "Sequence=\"/Script/Engine.AnimSequence'") + path + "'\"";
            if (!authored.Body.Contains(assetToken, StringComparison.Ordinal)) throw new ArgumentException("Authored movement resource differs.");
            var bindings = Regex.Matches(authored.Body, "PropertyName=\"([^\"]+)\".*?PropertyPath=\\(([^)]*)\\).*?bIsBound=True")
                .Select(m => (Name: m.Groups[1].Value, Path: string.Join(".", Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Select(v => v.Groups[1].Value)))).ToArray();
            if (bindings.Select(b => b.Name).Distinct().Count() != bindings.Length) throw new ArgumentException("Duplicate movement binding.");
            var rateBinding = bindings.SingleOrDefault(b => b.Name == "PlayRate").Path ?? "";
            foreach (var binding in bindings)
                if (!(binding.Name == "PlayRate" && binding.Path == $"GetParent.{(crouching ? "CrouchingState" : "StandingState")}.PlayRate" ||
                    blend && !crouching && (binding.Name == "X" && binding.Path == "GetParent.StandingState.StrideBlendAmount" || binding.Name == "Y" && binding.Path == "GetParent.StandingState.WalkRunBlendAmount")))
                    throw new ArgumentException("Unknown movement player binding.");
            if (blend && (crouching || bindings.Length != 3 || rateBinding == "")) throw new ArgumentException("Incomplete movement BlendSpace input.");
            if (rateBinding == "" && authored.Pins.Values.Any(p => p.Name == "PlayRate" && !p.Output)) rate = float.Parse(graph.Literal(authored, "PlayRate"), CultureInfo.InvariantCulture);
            if (!float.IsFinite(rate) || !float.IsFinite(basis) || basis <= 0 || !float.IsFinite(start) || start < 0) throw new ArgumentException("Invalid movement player constants.");
            players.Add(new(node.GetProperty("propertyIndex").GetInt32(), path, group, start, loop, rate, basis, rateBinding, blend));
        }
        if (players.Count != (crouching ? 6 : 24) || players.Select(p => p.PropertyIndex).Distinct().Count() != players.Count) throw new ArgumentException("Incomplete movement playback identities.");
        return players.OrderBy(p => p.PropertyIndex).ToArray();
    }
    public AlsRefactoredSourcePlayerDefinition[] Bind(int first, IReadOnlyDictionary<string, int> groups)
    {
        ValidateFirst(first); var names = _players.Select(p => p.Group).Distinct().ToArray();
        if (names.Any(n => !groups.TryGetValue(n, out var id) || id < 0) || names.Select(n => groups[n]).Distinct().Count() != names.Length) throw new ArgumentException("Invalid movement host groups.");
        return _players.Select((p, i) => new AlsRefactoredSourcePlayerDefinition(first + i, p.Source, groups[p.Group], p.Start, p.Loop, AlsAssetSyncRole.CanBeLeader)).ToArray();
    }
    public AlsRefactoredSourcePlayerInput Input(int first, int local, AlsRefactoredMovementPlayerInput input, float weight, bool reinitialize = false)
    {
        ValidateFirst(first);
        if ((uint)local >= (uint)_players.Length || !float.IsFinite(input.StandingRate) || !float.IsFinite(input.CrouchingRate) || !float.IsFinite(input.Stride) || !float.IsFinite(input.WalkRun) || !float.IsFinite(weight) || weight is < 0 or > 1) throw new ArgumentException("Invalid movement tick input.");
        var p = _players[local]; var rate = p.RateBinding == "" ? p.Rate : p.RateBinding.Contains("CrouchingState", StringComparison.Ordinal) ? input.CrouchingRate : input.StandingRate;
        return new(first + local, p.BlendSpace ? new Vector2(input.Stride, input.WalkRun) : default, rate / p.RateBasis, weight, reinitialize, p.Start);
    }
    private void ValidateFirst(int first) { if (first < 0 || first > int.MaxValue - _players.Length) throw new ArgumentOutOfRangeException(nameof(first)); }
}
