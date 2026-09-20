using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;
using GodotAls.Core.Events;
using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public sealed record AlsLocomotionSourcePlayer(int PlayerId, string SourceNode, int CompiledNodeIndex,
    AlsLocomotionSourceKind Kind, AlsLocomotionSourceDomain Domain, int SyncGroupId,
    float StartPosition, float DefaultPlayRate, float PlayRateBasis, string PlayRateInput, bool Loop,
    int SampleStart, int SampleCount, int DetailSlot, string InputX, string InputY,
    AlsSourceLoopInput LoopInput = AlsSourceLoopInput.Constant);

public sealed record AlsLocomotionSourceSample(int SampleId, int PlayerId, int SourceIndex, int AnimationId,
    float X, float Y, float Z, float SampleRateScale, float AssetRateScale, float DurationSeconds,
    int AdditiveBaseAnimationId);

/// <summary>Source-local identities, not yet rebased into the full P5 physical occurrence layout.
/// BlendSpace players own group membership; samples retain separate sampling/notify identities.</summary>
public sealed class AlsLocomotionSourceProfile
{
    private readonly AlsLocomotionSourcePlayer[] _players;
    private readonly AlsLocomotionSourceSample[] _samples;
    private readonly string[] _groups;
    private readonly AlsLocomotionSourcePlayerBinding[] _runtimePlayers;
    private readonly AlsLocomotionSourceSampleBinding[] _runtimeSamples;
    private readonly AlsLocomotionSourceSyncBinding[] _runtimeSyncPlayers;
    private readonly AlsAssetSyncSequence[] _syncSequences;
    private readonly AlsAssetSyncMarker[] _syncMarkers;
    private readonly string[] _markerSymbols;
    private readonly int[] _groupIds;
    private readonly AlsLocomotionNotifyMetadata _notifies;
    public int SkeletonId { get; }
    public string Digest { get; }
    public string SourceGraphDigest { get; }
    public string AnimationSetDefinitionDigest { get; }
    public AlsStandingSprintProfile Sprint { get; }
    public AlsLocomotionSourceStamp RuntimeStamp { get; }
    public AlsLocomotionSourcePlayer[] Players => _players.ToArray();
    public AlsLocomotionSourceSample[] Samples => _samples.ToArray();
    public string[] SyncGroups => _groups.ToArray();
    public AlsLocomotionSourcePlayerBinding[] RuntimePlayers => _runtimePlayers.ToArray();
    public AlsLocomotionSourceSampleBinding[] RuntimeSamples => _runtimeSamples.ToArray();
    public AlsLocomotionSourceSyncBinding[] RuntimeSyncPlayers => _runtimeSyncPlayers.ToArray();
    public AlsAssetSyncSequence[] SyncSequences => _syncSequences.ToArray();
    public AlsAssetSyncMarker[] SyncMarkers => _syncMarkers.ToArray();
    public string[] MarkerSymbols => _markerSymbols.ToArray();
    public string[] NotifyObjects => _notifies.Objects.ToArray();
    public string[] NotifyNames => _notifies.Names.ToArray();

    public AlsLocomotionSourceView CreateCoreView() => new(RuntimeStamp, _runtimePlayers, _runtimeSamples,
        _runtimeSyncPlayers, _groupIds, _syncSequences, _syncMarkers,
        _notifies.Ranges, _notifies.Definitions, _notifies.Policies);

    internal AlsLocomotionSourceProfile(int skeleton, string sourceDigest, string definitionDigest,
        AlsLocomotionSourcePlayer[] players, AlsLocomotionSourceSample[] samples, string[] groups, AlsLocomotionSyncMetadata sync,
        AlsLocomotionNotifyMetadata notifies, AlsStandingSprintProfile sprint, string additionalSourceDigest = "")
    {
        _notifies = new(notifies.Ranges.ToArray(), notifies.Definitions.ToArray(), notifies.Policies.ToArray(),
            notifies.Objects.ToArray(), notifies.Names.ToArray());
        SkeletonId = skeleton; SourceGraphDigest = sourceDigest; AnimationSetDefinitionDigest = definitionDigest;
        Sprint = sprint;
        _players = players.ToArray(); _samples = samples.ToArray(); _groups = groups.ToArray();
        _groupIds = Enumerable.Range(0, groups.Length).ToArray();
        _runtimeSyncPlayers = sync.Players.ToArray(); _syncSequences = sync.Sequences.ToArray();
        _syncMarkers = sync.Markers.ToArray(); _markerSymbols = sync.Symbols.ToArray();
        var sequenceIndices = _syncSequences.Select((s, i) => (s.AnimationId, Index: i)).ToDictionary(s => s.AnimationId, s => s.Index);
        _runtimePlayers = players.Select(p => new AlsLocomotionSourcePlayerBinding(p.PlayerId, p.CompiledNodeIndex,
            p.Kind, p.Domain, p.SyncGroupId, p.StartPosition, p.DefaultPlayRate, p.PlayRateBasis,
            p.PlayRateInput == "" ? AlsSourceRateInput.Constant : p.PlayRateInput == "StandingPlayRate" ? AlsSourceRateInput.StandingPlayRate :
                p.PlayRateInput == "RotateRate" ? AlsSourceRateInput.RotateRate :
                p.PlayRateInput == "CrouchingPlayRate" ? AlsSourceRateInput.CrouchingPlayRate :
                p.PlayRateInput == "JumpPlayRate" ? AlsSourceRateInput.JumpPlayRate :
                p.PlayRateInput == "FlailRate" ? AlsSourceRateInput.FlailRate :
                throw new ArgumentException("Unsupported runtime rate input."), p.Loop,
            p.SampleStart, p.SampleCount, p.DetailSlot, Axis(p.InputX), Axis(p.InputY), p.LoopInput)).ToArray();
        _runtimeSamples = samples.Select(s => new AlsLocomotionSourceSampleBinding(s.SampleId, s.PlayerId, s.SourceIndex,
            s.AnimationId, s.X, s.Y, s.Z, s.SampleRateScale, s.AssetRateScale, s.DurationSeconds, s.AdditiveBaseAnimationId, sequenceIndices[s.AnimationId])).ToArray();
        Digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { Version = 3, SkeletonId, SourceGraphDigest, definitionDigest, Players, Samples, SyncGroups, RuntimePlayers, RuntimeSamples,
                RuntimeSyncPlayers, SyncSequences, SyncMarkers, MarkerSymbols, Notifies = _notifies }))).ToLowerInvariant();
        if (additionalSourceDigest.Length != 0)
            Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Digest + "\nadditional_sources\n" + additionalSourceDigest))).ToLowerInvariant();
        var digestBytes = Convert.FromHexString(Digest);
        RuntimeStamp = new(AlsLocomotionSourceView.CurrentVersion, skeleton,
            BinaryPrimitives.ReadUInt64LittleEndian(digestBytes), BinaryPrimitives.ReadUInt64LittleEndian(digestBytes.AsSpan(8)),
            BinaryPrimitives.ReadUInt64LittleEndian(digestBytes.AsSpan(16)), BinaryPrimitives.ReadUInt64LittleEndian(digestBytes.AsSpan(24)));
    }

    private static AlsSourceAxisInput Axis(string input) => input switch
    {
        "" => AlsSourceAxisInput.None,
        "StrideBlend" => AlsSourceAxisInput.StrideBlend,
        "WalkRunBlend" => AlsSourceAxisInput.WalkRunBlend,
        _ when input.StartsWith("LeanAmount_LR_", StringComparison.Ordinal) => AlsSourceAxisInput.LeanLeftRight,
        _ when input.StartsWith("LeanAmount_FB_", StringComparison.Ordinal) => AlsSourceAxisInput.LeanForwardBack,
        _ => throw new ArgumentException("Unsupported runtime axis input."),
    };
}

public static class AlsLocomotionSourceCompiler
{
    private const string Blueprint = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_AnimBP.ALS_AnimBP";
    private const string CyclePath = Blueprint + ":BaseLayer.AnimGraphNode_StateMachine_1.(N) Locomotion Cycles.AnimStateNode_0.(N) Locomotion Cycles";

    public static AlsLocomotionSourceProfile Compile(string json, AlsAnimationSetDefinition set, int skeletonId) => CompileSources(json, set, skeletonId, false);

    public static AlsLocomotionSourceProfile CompileWithJump(string json, AlsAnimationSetDefinition set, int skeletonId) => CompileSources(json, set, skeletonId, true);

    public static AlsLocomotionSourceProfile CompileWithMovement(string json, AlsAnimationSetDefinition set, int skeletonId) => CompileSources(json, set, skeletonId, true, true);

    private static AlsLocomotionSourceProfile CompileSources(string json, AlsAnimationSetDefinition set, int skeletonId, bool includeJump, bool includeMovement = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(set);
        if ((uint)skeletonId >= set.Skeletons.Length) throw new ArgumentOutOfRangeException(nameof(skeletonId));
        try
        {
            Require(AlsAnimationSetPayload.ComputeDefinitionDigest(set) == set.DefinitionDigest, "Stale animation-set definition digest.");
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            RejectDuplicates(root);
            Require(root.GetProperty("schemaVersion").GetInt32() == 1 && Text(root, "source") == Blueprint &&
                Text(root, "scope") == "Main Grounded with Standing and Crouching source dependencies", "Incomplete source graph scope.");
            var detail = AlsLocomotionDetailCompiler.Compile(json, set, skeletonId);
            var stop = AlsStopPoseProfileCompiler.Compile(json, set, skeletonId);
            var graphs = root.GetProperty("graphs").EnumerateArray().ToDictionary(g => Text(g, "path"), StringComparer.Ordinal);
            var nodes = graphs[CyclePath].GetProperty("nodes").EnumerateArray().ToDictionary(n => Text(n, "name"), StringComparer.Ordinal);
            var allNodes = graphs.SelectMany(g => g.Value.GetProperty("nodes").EnumerateArray()
                .Select(n => (Path: g.Key + "." + Text(n, "name"), Node: n))).ToDictionary(n => n.Path, n => n.Node, StringComparer.Ordinal);
            var cycleNodes = nodes.Values.Where(n => Text(n, "class") is "AnimGraphNode_BlendSpacePlayer" or "AnimGraphNode_SequencePlayer").ToArray();
            Require(cycleNodes.Length == 9 && cycleNodes.Count(n => Text(n, "class") == "AnimGraphNode_BlendSpacePlayer") == 7,
                "Expected six WalkRun players, Lean, Sprint and Sprint Impulse.");
            var baked = One(root.GetProperty("bakedMachines").EnumerateArray().Where(m => Text(m, "machineName") == "(N) Locomotion Cycles"));
            var state = One(baked.GetProperty("states").EnumerateArray());
            Require(baked.GetProperty("initialState").GetInt32() == 0 && baked.GetProperty("transitions").GetArrayLength() == 0 &&
                Text(state, "stateName") == "(N) Locomotion Cycles", "Unexpected Cycle machine topology.");
            var order = state.GetProperty("playerNodeIndices").EnumerateArray().Select(n => n.GetInt32()).ToArray();
            Require(order.Length == 9 && order.Distinct().Count() == 9, "Invalid baked Cycle player order.");
            var byIndex = cycleNodes.ToDictionary(n => n.GetProperty("compiledNodeIndex").GetInt32());
            Require(order.All(byIndex.ContainsKey), "Baked Cycle identities differ from the graph.");
            var detailPlayers = detail.States.SelectMany(s => s.Players).ToArray();
            var groups = cycleNodes.Select(n => Text(Data(n), "groupName")).Concat(detailPlayers.Select(p => p.SyncGroup))
                .Where(g => g != "None").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            Require(groups.SequenceEqual(new[] { "Locomotion", "Pivot 1", "Pivot 2", "Run Start" }), "Unexpected source Sync groups.");
            var jumpSources = includeJump ? AlsJumpSourceCompiler.Compile(root, set, skeletonId) : [];
            var movementSources = includeMovement ? AlsMovementSourceCompiler.Compile(root, set, skeletonId) : [];
            if (includeJump)
            {
                _ = AlsGroundedMachineCompiler.CompileMovement(json);
                // Append to preserve existing Grounded group identities.
                groups = groups.Concat(new[] { "Jump", "Flail" }).ToArray();
            }
            if (includeMovement) groups = groups.Concat(new[] { "Fall", "Land" }).ToArray();
            var players = new List<AlsLocomotionSourcePlayer>();
            var samples = new List<AlsLocomotionSourceSample>();
            var compiledIds = new HashSet<int>();
            var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
            var blendNames = new HashSet<string>(StringComparer.Ordinal);
            var sequenceNames = new HashSet<string>(StringComparer.Ordinal);
            var assetRates = new Dictionary<int, float>();
            foreach (var index in order)
            {
                var node = byIndex[index]; var data = Data(node);
                var path = CyclePath + "." + Text(node, "name");
                var start = Number(data, "startPosition"); var rate = Number(data, "playRate");
                var groupName = Text(data, "groupName"); var grouped = groupName != "None";
                Require(Text(data, "groupRole") == "CanBeLeader" &&
                    Text(data, "method") == (grouped ? "SyncGroup" : "DoNotSync") &&
                    !data.GetProperty("bOverridePositionWhenJoiningSyncGroupAsLeader").GetBoolean() &&
                    !data.GetProperty("bIgnoreForRelevancyTest").GetBoolean(), "Unsupported Cycle sync policy.");
                var sampleStart = samples.Count;
                if (Text(node, "class") == "AnimGraphNode_BlendSpacePlayer")
                {
                    var native = node.GetProperty("runtimePlayer");
                    var blend = One(set.BlendSpaces.Where(b => b.ObjectPath == Text(native, "assetObjectPath")));
                    Require(blendNames.Add(blend.Name) && Text(data, "blendSpace") == blend.ObjectPath &&
                        Text(native, "groupName") == groupName && native.GetProperty("groupRole").GetInt32() == 0 &&
                        native.GetProperty("groupMethod").GetInt32() == (grouped ? 1 : 0) &&
                        Number(native, "startPosition") == start && Number(native, "playRate") == rate &&
                        native.GetProperty("loop").GetBoolean() && data.GetProperty("bLoop").GetBoolean() &&
                        native.GetProperty("resetOnAssetChange").GetBoolean() && data.GetProperty("bResetPlayTimeWhenBlendSpaceChanges").GetBoolean() &&
                        !native.GetProperty("evaluator").GetBoolean() && start >= 0 && start <= 1 &&
                        Text(native, "NotifyTriggerMode") == "HighestWeightedAnimation" &&
                        native.GetProperty("bAllowMarkerBasedSync").GetBoolean() && native.GetProperty("bInterpolateUsingGrid").GetBoolean() &&
                        Number(native, "TargetWeightInterpolationSpeedPerSec") == 0 && Text(native, "AxisToScaleAnimation") == "BSA_None",
                        "Unsupported or inconsistent native BlendSpace settings.");
                    var lean = blend.Name == "ALS_N_Lean";
                    Require(grouped != lean && (lean || blend.Name.StartsWith("ALS_N_WalkRun_", StringComparison.Ordinal)), "Unexpected Cycle BlendSpace.");
                    var nativeSamples = native.GetProperty("samples").EnumerateArray().ToArray();
                    Require(nativeSamples.Length == blend.Samples.Length && nativeSamples.Length == (lean ? 5 : 4), "Incomplete BlendSpace samples.");
                    for (var sampleIndex = 0; sampleIndex < nativeSamples.Length; sampleIndex++)
                    {
                        var sample = nativeSamples[sampleIndex]; var imported = blend.Samples[sampleIndex];
                        var animation = Animation(Text(sample, "animation"));
                        var xyz = sample.GetProperty("sampleValue");
                        var x = Number(xyz, "x"); var y = Number(xyz, "y"); var z = Number(xyz, "z");
                        var sampleRate = Number(sample, "rateScale"); var assetRate = Number(sample, "assetRateScale");
                        Require(sample.GetProperty("sourceIndex").GetInt32() == sampleIndex && imported.AnimationId == animation.Id &&
                            imported.SampleValue.Length == 3 && imported.SampleValue[0] == x && imported.SampleValue[1] == y && imported.SampleValue[2] == z &&
                            sampleRate == imported.RateScale && sampleRate > 0 && assetRate > 0 &&
                            Number(sample, "playLength") == animation.PlayLength && !sample.GetProperty("bMirror").GetBoolean() &&
                            !sample.GetProperty("bUseSingleFrameForBlending").GetBoolean() && sample.GetProperty("frameIndexToSample").GetInt32() == 0,
                            "Native sample order, animation, coordinates or rate differ from the imported asset.");
                        AddSample(animation, sampleIndex, x, y, z, sampleRate, assetRate);
                    }
                    AddPlayer(node, path, AlsLocomotionSourceKind.BlendSpace, AlsLocomotionSourceDomain.Cycle,
                        groupName, start, rate, 1, lean ? "" : Variable(node, "PlayRate", "StandingPlayRate"), true, sampleStart, -1,
                        Variable(node, "X", lean ? "LeanAmount" : "StrideBlend"), Variable(node, "Y", lean ? "LeanAmount" : "WalkRunBlend"));
                }
                else
                {
                    var animation = Animation(Text(data, "sequence"));
                    Require(sequenceNames.Add(animation.Name) && animation.Name is "ALS_N_Sprint_F" or "ALS_N_Sprint_F_Impulse" &&
                        Text(node, "assetObjectPath") == animation.ObjectPath && data.GetProperty("bLoopAnimation").GetBoolean() &&
                        !data.GetProperty("bStartFromMatchingPose").GetBoolean() && start >= 0 && start <= animation.PlayLength && grouped,
                        "Unsupported Cycle sequence source.");
                    var basis = Number(data, "playRateBasis");
                    Require(basis > 0, "Invalid sequence play-rate basis.");
                    var clamp = data.GetProperty("playRateScaleBiasClampConstants");
                    Require(!clamp.GetProperty("bMapRange").GetBoolean() && !clamp.GetProperty("bClampResult").GetBoolean() &&
                        !clamp.GetProperty("bInterpResult").GetBoolean() && Number(clamp, "scale") == 1 && Number(clamp, "bias") == 0,
                        "Unsupported sequence rate transform.");
                    AddSample(animation, 0, 0, 0, 0, 1, Number(node, "assetRateScale"));
                    AddPlayer(node, path, AlsLocomotionSourceKind.Sequence, AlsLocomotionSourceDomain.Cycle,
                        groupName, start, rate, basis, Variable(node, "PlayRate", "StandingPlayRate"), true, sampleStart, -1, "", "");
                }
            }
            Require(blendNames.SetEquals(new[] { "ALS_N_WalkRun_F", "ALS_N_WalkRun_B", "ALS_N_WalkRun_FL", "ALS_N_WalkRun_BL",
                "ALS_N_WalkRun_FR", "ALS_N_WalkRun_BR", "ALS_N_Lean" }) && sequenceNames.Count == 2, "Cycle source closure is incomplete.");
            for (var slot = 0; slot < detailPlayers.Length; slot++)
            {
                var player = detailPlayers[slot]; var node = allNodes[player.SourceNode]; var sampleStart = samples.Count;
                AddSample(set.Animations[player.AnimationId], 0, 0, 0, 0, 1, player.AssetRateScale);
                AddPlayer(node, player.SourceNode, AlsLocomotionSourceKind.Sequence, AlsLocomotionSourceDomain.Detail,
                    player.SyncGroup, player.StartSeconds, player.PlayRate, 1, "", false, sampleStart, slot, "", "");
            }
            foreach (var sample in stop.Left.Samples.Concat(stop.Right.Samples))
            {
                var sampleStart = samples.Count;
                AddSample(set.Animations[sample.AnimationId], 0, 0, 0, 0, 1, 1);
                AddPlayer(allNodes[sample.SourceNode], sample.SourceNode, AlsLocomotionSourceKind.TeleportEvaluator,
                    AlsLocomotionSourceDomain.Stop, "None", sample.TimeSeconds, 0, 1, "",
                    allNodes[sample.SourceNode].GetProperty("properties").GetProperty("Node").GetProperty("bShouldLoop").GetBoolean(),
                    sampleStart, -1, "", "");
            }
            foreach (var source in AlsStandingSourceCompiler.Compile(root, set, skeletonId))
            {
                var sampleStart = samples.Count;
                AddSample(set.Animations[source.AnimationId], 0, 0, 0, 0, 1, source.AssetRate);
                AddPlayer(allNodes[source.SourceNode], source.SourceNode, source.Kind, AlsLocomotionSourceDomain.Standing,
                    "None", source.Time, source.Rate, source.RateBasis,
                    source.Kind == AlsLocomotionSourceKind.Sequence ? "RotateRate" : "", source.Loop,
                    sampleStart, -1, "", "", source.LoopInput);
            }
            foreach (var source in AlsMainGroundedSourceCompiler.Compile(root, set, skeletonId))
            {
                var sampleStart = samples.Count;
                AddSample(set.Animations[source.AnimationId], 0, 0, 0, 0, 1, source.AssetRate);
                AddPlayer(allNodes[source.SourceNode], source.SourceNode, source.Kind, AlsLocomotionSourceDomain.MainGrounded,
                    "None", source.Time, source.Rate, source.RateBasis, "", source.Loop, sampleStart, -1, "", "");
            }
            foreach (var source in AlsCrouchingSourceCompiler.Compile(root, set, skeletonId))
            {
                var sampleStart = samples.Count;
                for (var index = 0; index < source.Samples.Length; index++)
                {
                    var sample = source.Samples[index];
                    AddSample(set.Animations[sample.AnimationId], index, sample.X, sample.Y, sample.Z, sample.Rate, sample.AssetRate);
                }
                AddPlayer(allNodes[source.Path], source.Path, source.Kind, AlsLocomotionSourceDomain.Crouching,
                    source.Group, source.Time, source.Rate, source.Basis, source.RateInput, source.Loop,
                    sampleStart, -1, source.X, source.Y, source.LoopInput);
            }
            foreach (var source in jumpSources)
            {
                var sampleStart = samples.Count;
                AddSample(set.Animations[source.AnimationId], 0, 0, 0, 0, 1, source.AssetRate);
                AddPlayer(allNodes[source.Path], source.Path, AlsLocomotionSourceKind.Sequence, AlsLocomotionSourceDomain.Jump,
                    source.Group, source.Time, source.Rate, source.Basis, source.RateInput, source.Loop, sampleStart, -1, "", "");
            }
            foreach (var source in movementSources)
            {
                var sampleStart = samples.Count;
                for (var i = 0; i < source.Samples.Length; i++)
                {
                    var sample = source.Samples[i];
                    AddSample(set.Animations[sample.AnimationId], i, sample.X, sample.Y, sample.Z, sample.Rate, sample.AssetRate);
                }
                AddPlayer(allNodes[source.Path], source.Path, source.Kind, AlsLocomotionSourceDomain.MainMovement,
                    source.Group, source.Time, source.Rate, source.Basis, "", source.Loop, sampleStart, -1, source.X, source.Y);
            }
            var playerArray = players.ToArray(); var sampleArray = samples.ToArray();
            var sync = AlsLocomotionSourceSyncCompiler.Compile(root, set, playerArray, sampleArray,
                includeJump ? MovementAssetClosure(root) : null);
            AlsCycleDirectionGraphCompiler.Validate(root);
            var notifies = AlsLocomotionSourceNotifyCompiler.Compile(root, set);
            return new(skeletonId, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),
                set.DefinitionDigest, playerArray, sampleArray, groups, sync, notifies, AlsStandingSprintCompiler.Compile(root));

            AlsAnimationDefinition Animation(string path)
            {
                var value = One(set.Animations.Where(a => a.ObjectPath == path));
                Require(value.SkeletonId == skeletonId && (uint)value.Id < set.Animations.Length &&
                    set.Animations[value.Id].ObjectPath == path && float.IsFinite(value.PlayLength) && value.PlayLength > 0,
                    "Invalid animation closure or skeleton.");
                return value;
            }
            void AddSample(AlsAnimationDefinition animation, int index, float x, float y, float z, float sampleRate, float assetRate)
            {
                Require(float.IsFinite(assetRate) && assetRate > 0, "Invalid asset rate scale.");
                Require(!assetRates.TryGetValue(animation.Id, out var previousRate) || previousRate == assetRate,
                    "The same animation asset has conflicting rate metadata across source instances.");
                assetRates[animation.Id] = assetRate;
                samples.Add(new(samples.Count, players.Count, index, animation.Id, x, y, z, sampleRate, assetRate,
                    animation.PlayLength, animation.AdditiveBasePoseAnimationId));
            }
            void AddPlayer(JsonElement node, string path, AlsLocomotionSourceKind kind, AlsLocomotionSourceDomain domain,
                string group, float start, float rate, float basis, string rateInput, bool loop, int sampleStart, int detailSlot, string x, string y,
                AlsSourceLoopInput loopInput = AlsSourceLoopInput.Constant)
            {
                var compiled = node.GetProperty("compiledNodeIndex").GetInt32();
                Require(compiled >= 0 && compiledIds.Add(compiled) && sourcePaths.Add(path), "Aliased source player identity.");
                players.Add(new(players.Count, path, compiled, kind, domain, group == "None" ? -1 : Array.IndexOf(groups, group),
                    start, rate, basis, rateInput, loop, sampleStart, samples.Count - sampleStart, detailSlot, x, y, loopInput));
            }
            string Variable(JsonElement node, string pinName, string expected)
            {
                var pin = One(node.GetProperty("pins").EnumerateArray().Where(p => Text(p, "name") == pinName));
                Require(Text(pin, "direction") == "input", "Wrong player input direction.");
                var link = One(pin.GetProperty("links").EnumerateArray());
                var source = nodes[Text(link, "node")];
                Require(Text(source, "class") == "K2Node_VariableGet" &&
                    Text(source.GetProperty("properties").GetProperty("VariableReference"), "memberName") == expected &&
                    (expected == "LeanAmount" ? Text(link, "pin").StartsWith(pinName == "X" ? "LeanAmount_LR_" : "LeanAmount_FB_", StringComparison.Ordinal) : Text(link, "pin") == expected),
                    "Player input is disconnected or reads a different variable.");
                return Text(link, "pin");
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        { throw new AlsCompilationException([new AlsValidationIssue("ALSSOURCE001", null, "$", "Invalid locomotion source graph: " + error.Message)]); }
    }

    private static JsonElement Data(JsonElement node) => node.GetProperty("properties").GetProperty("Node");
    private static HashSet<string> MovementAssetClosure(JsonElement root)
    {
        var assets = new HashSet<string>(StringComparer.Ordinal);
        var prefix = Blueprint + ":BaseLayer.AnimGraphNode_StateMachine_9.Main Movement States.";
        var players = root.GetProperty("graphs").EnumerateArray().Where(g => Text(g, "path").StartsWith(prefix, StringComparison.Ordinal))
            .SelectMany(g => g.GetProperty("nodes").EnumerateArray()).Where(n => Text(n, "class") is
                "AnimGraphNode_SequencePlayer" or "AnimGraphNode_SequenceEvaluator" or "AnimGraphNode_BlendSpacePlayer").ToArray();
        Require(players.Length == 19 && players.Select(n => n.GetProperty("compiledNodeIndex").GetInt32()).Distinct().Count() == 19,
            "Incomplete Main Movement asset declarations.");
        foreach (var node in players)
        {
            if (Text(node, "class") == "AnimGraphNode_BlendSpacePlayer")
                foreach (var sample in node.GetProperty("runtimePlayer").GetProperty("samples").EnumerateArray()) assets.Add(Text(sample, "animation"));
            else assets.Add(Text(Data(node), "sequence"));
        }
        return assets;
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() ?? throw new InvalidOperationException("Expected text.");
    private static float Number(JsonElement value, string name)
    {
        var number = value.GetProperty(name).GetSingle();
        Require(float.IsFinite(number), "Non-finite source value."); return number;
    }
    private static T One<T>(IEnumerable<T> values) => values.Single();
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { Require(keys.Add(property.Name), "Duplicate JSON property."); RejectDuplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
}
