using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMainLeanSampleState(AlsBlendSpaceSampleWeight Weight, AlsAssetSampleHistory Clock);
internal sealed record LyraMainLeanNodeState(bool Initialized, float Pin, float CachedWeight, float Time,
    float DeltaPrevious, float Delta, ImmutableArray<LyraMainLeanSampleState> Samples,
    AlsAssetMarkerRecord Marker=default,float FilterOutput=0,int CachedTriangle=-1,bool HasBeenFullWeight=false);
internal sealed record LyraMainLeanSourceCandidate(ImmutableArray<LyraMainLeanNodeState> Prepared,
    ImmutableArray<bool> Active, ImmutableArray<int> Order, float Delta);
internal sealed record LyraMainLeanSyncInputs(LyraMainLeanSourceCandidate Candidate,
    ImmutableArray<LyraMainLeanNodeState> Weighted, AlsAssetSyncPlayer[] Players, AlsAssetSyncSample[] Samples);
internal sealed record LyraMainLeanResolved(LyraMainLeanSyncInputs Inputs, ImmutableArray<LyraMainLeanNodeState> States);

// The three original Main occurrences share immutable resources, never clocks.
// Prepare registers source work; weight smoothing occurs at common Sync; only
// the enclosing character transaction may commit the resolved state.
internal sealed class LyraMainLeanSourceHost
{
    private readonly LyraLogicalSourceSampler[][] _samplers;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly int _playerBase, _assetId, _sequenceBase;
    private readonly long _epoch;
    private LyraMainLeanSourceCandidate? _pending;
    private LyraMainLeanSyncInputs? _inputs;
    private LyraMainLeanResolved? _resolved;
    private readonly AlsPrecisePose[][] _scratch = Enumerable.Range(0, 3).Select(_ => new AlsPrecisePose[81]).ToArray();
    private readonly int[] _evaluating = new int[3];
    public ImmutableArray<LyraMainLeanNodeState> States { get; private set; }
    public LyraMainLeanSourceHost(LyraLogicalSourceBank bank, AlsAssetSyncSequence[] sequences,
        int playerBase, int assetId, int sequenceBase, long epoch)
    {
        if (bank.MainLeanCatalogSha256 is null || playerBase < 0 || playerBase > (int.MaxValue - 9) / 3 ||
            assetId < 0 || epoch <= 0 || sequenceBase < 0 || sequenceBase > sequences.Length - 3)
            throw new ArgumentException("Invalid Main Lean owner/source binding.");
        _sequences = sequences; _playerBase = playerBase; _assetId = assetId; _sequenceBase = sequenceBase; _epoch = epoch;
        var nodes = LyraSourceNodeCatalog.Load().ForClass(LyraRuntimeGraphCatalog.MainClass).Nodes;
        foreach (var index in new[] { 22, 16, 12 })
        {
            var node = nodes[index];
            if (node.Kind != LyraSourceKind.BlendSpacePlayer || node.Method != LyraSourceSyncMethod.DoNotSync ||
                !node.Looping || !node.IgnoreRelevancy || node.OverridePositionWhenJoining ||
                node.Settings.GetProperty("playRate").GetSingle() != 1 || node.Settings.GetProperty("startPosition").GetSingle() != 0 ||
                !node.Settings.GetProperty("resetOnAssetChange").GetBoolean() || node.Settings.GetProperty("evaluator").GetBoolean() ||
                node.Settings.GetProperty("teleport").GetBoolean())
                throw new NotSupportedException("Changed original Main Lean source occurrence policy.");
        }
        var inventoryBytes = Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "inventory.json");
        using var inventory = JsonDocument.Parse(inventoryBytes);
        if (inventory.RootElement.GetProperty("weightSpeed").GetSingle() != 3)
            throw new NotSupportedException("Changed original Main Lean smoothing speed.");
        using var behavior = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "behavior.json"));
        var policy = behavior.RootElement;
        if (policy.GetProperty("schemaVersion").GetInt32() != 1 ||
            policy.GetProperty("catalogSha256").GetString() != bank.MainLeanCatalogSha256 ||
            policy.GetProperty("inventorySha256").GetString() != LyraLogicalSourceBank.Sha(inventoryBytes) ||
            !policy.GetProperty("weightEaseInOut").GetBoolean() || !policy.GetProperty("legacySampleLength").GetBoolean() ||
            !policy.GetProperty("allowMarkerBasedSync").GetBoolean() ||
            policy.GetProperty("notifyMode").GetString() != "<NotifyTriggerMode.HIGHEST_WEIGHTED_ANIMATION: 1>" ||
            policy.GetProperty("perBoneBlendMode").GetString() != "<BlendSpacePerBoneBlendMode.MANUAL_PER_BONE_OVERRIDE: 0>" ||
            policy.GetProperty("manualPerBoneOverrideCount").GetInt32() != 0 || policy.GetProperty("allowMeshSpaceBlending").GetBoolean() ||
            policy.GetProperty("matchSyncPhases").GetBoolean() || policy.GetProperty("axisFilters").EnumerateArray().Any(a => a.GetProperty("time").GetSingle() != 0))
            throw new NotSupportedException("Changed original Main Lean runtime policy.");
        _ = new LyraMainLeanBlendSpace(bank); // Validates authored sample/topology policy.
        var slots = new[] { "main_lean_center", "main_lean_left", "main_lean_right" };
        using var playback = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "runtime_policies.json"));
        var playbackPolicy = playback.RootElement;
        if (playbackPolicy.GetProperty("schemaVersion").GetInt32() != 1 ||
            playbackPolicy.GetProperty("catalogSha256").GetString() != bank.MainLeanCatalogSha256 ||
            playbackPolicy.GetProperty("sourceNodesSha256").GetString() != LyraLogicalSourceBank.Sha(
                Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/source_nodes.json")) ||
            playbackPolicy.GetProperty("sequences").GetArrayLength() != 3)
            throw new InvalidOperationException("Stale Main Lean playback policy.");
        _samplers = Enumerable.Range(0, 3).Select(_ => slots.Select(bank.CreateSampler).ToArray()).ToArray();
        for (var i = 0; i < slots.Length; i++)
        {
            var source = bank.Get(slots[i]); var sequence = sequences[sequenceBase + i];
            var original = playbackPolicy.GetProperty("sequences")[i];
            if (sequence.AnimationId != source.Data.Identity.AnimationId || sequence.DurationSeconds != (float)source.Data.PlayLength ||
                original.GetProperty("source").GetString() != source.Source || original.GetProperty("target").GetString() != source.Data.Identity.AssetPath ||
                original.GetProperty("sourceRate").GetSingle() != 1 || original.GetProperty("targetRate").GetSingle() != sequence.RateScale ||
                original.GetProperty("sourceMarkerCount").GetInt32() != 0 || original.GetProperty("targetMarkerCount").GetInt32() != sequence.MarkerCount ||
                original.GetProperty("playLength").GetSingle() != (float)source.Data.PlayLength || sequence.RateScale != 1 || sequence.MarkerCount != 0)
                throw new InvalidOperationException("Main Lean sequence identity/policy differs.");
        }
        States = Enumerable.Range(0, 3).Select(_ => new LyraMainLeanNodeState(false, 0, 0, 0, 0, 0, [],Marker:AlsAssetMarkerRecord.Invalid)).ToImmutableArray();
    }
    private static LyraMainLeanNodeState InitializeSource(LyraMainLeanNodeState state,double angle)
        =>state with{Initialized=true,Time=0,Pin=(float)angle,Samples=[],FilterOutput=0,
            Marker=state.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false},HasBeenFullWeight=false};
    internal bool InitializeSourceNode(int node,double angle)
    {
        if(_pending is not null)throw new InvalidOperationException("Main Lean phase initialization needs an idle source.");
        if(!double.IsFinite(angle)||!float.IsFinite((float)angle))throw new ArgumentException("Nonfinite Main Lean initialization pin.");
        var index=Array.IndexOf(new[]{22,16,12},node);if(index<0)return false;
        States=States.SetItem(index,InitializeSource(States[index],angle));return true;
    }
    public LyraMainLeanSourceCandidate Prepare(double angle, float delta, ReadOnlySpan<float> weights,
        ReadOnlySpan<bool> active, ReadOnlySpan<bool> initialize, ReadOnlySpan<int> order)
    {
        if (_pending is not null) throw new InvalidOperationException("Main Lean frame is already pending.");
        if (!double.IsFinite(angle) || !float.IsFinite((float)angle) || !float.IsFinite(delta) || delta < 0 ||
            weights.Length != 3 || active.Length != 3 || initialize.Length != 3 || order.Length != 3)
            throw new ArgumentException("Incomplete Main Lean frame.");
        for (var i = 0; i < 3; i++)
            if (!float.IsFinite(weights[i]) || weights[i] < 0 || order[i] is < 0 or > 2 || order[..i].Contains(order[i]))
                throw new ArgumentException("Invalid Main Lean weight/traversal order.");
        var nodes = States.ToArray();
        for (var i = 0; i < 3; i++)
        {
            var state = nodes[i];
            if (!state.Initialized || initialize[i])
                state = InitializeSource(state,angle);
            if (active[i]) state = state with { Pin = (float)angle, CachedWeight = weights[i],
                HasBeenFullWeight=state.HasBeenFullWeight||weights[i]>=1-1e-5f };
            nodes[i] = state;
        }
        return _pending = new(nodes.ToImmutableArray(), active.ToArray().ToImmutableArray(), order.ToArray().ToImmutableArray(), delta);
    }
    public LyraMainLeanSyncInputs CollectAtCommonSync(LyraMainLeanSourceCandidate candidate, int sampleStart = 0)
    {
        if (!ReferenceEquals(candidate, _pending) || _inputs is not null || sampleStart < 0)
            throw new InvalidOperationException("Rejected stale/repeated Main Lean collection.");
        var weighted = candidate.Prepared.ToArray();
        var players = new List<AlsAssetSyncPlayer>(); var samples = new List<AlsAssetSyncSample>();
        Span<AlsAimGridVertex> target = stackalloc AlsAimGridVertex[2];
        Span<AlsBlendSpaceSampleWeight> oldWeights = stackalloc AlsBlendSpaceSampleWeight[3];
        Span<AlsBlendSpaceSampleWeight> targetWeights = stackalloc AlsBlendSpaceSampleWeight[2];
        Span<AlsBlendSpaceSampleWeight> smooth = stackalloc AlsBlendSpaceSampleWeight[6];
        foreach (var index in candidate.Order)
        {
            if (!candidate.Active[index]) continue;
            var node = candidate.Prepared[index]; var count = LyraMainLeanBlendSpace.StaticWeights(node.Pin, target);
            for (var i = 0; i < node.Samples.Length; i++) oldWeights[i] = node.Samples[i].Weight;
            for (var i = 0; i < count; i++) targetWeights[i] = new(target[i].Sample, target[i].Weight);
            var smoothCount = AlsBlendSpaceWeightSmoothing.Evaluate(oldWeights[..node.Samples.Length], targetWeights[..count], candidate.Delta, 3, true, smooth);
            var start = checked(sampleStart + samples.Count);
            var stateSamples = ImmutableArray.CreateBuilder<LyraMainLeanSampleState>(smoothCount);
            for (var i = 0; i < smoothCount; i++)
            {
                var sample = smooth[i]; var sampleId = checked((_playerBase + index) * 3 + sample.SampleId);
                samples.Add(new(sampleId, _sequenceBase + sample.SampleId, sample.Weight));
                stateSamples.Add(new(sample, new(sampleId, _sequences[_sequenceBase + sample.SampleId].AnimationId,
                    0, 0, AlsAssetMarkerRecord.Invalid, 0, 0)));
            }
            weighted[index] = node with { Samples = stateSamples.MoveToImmutable(),FilterOutput=node.Pin };
            players.Add(new(_playerBase + index, _assetId, _epoch, AlsAssetSyncKind.BlendSpace,
                node.Time, 1, node.CachedWeight, start, smoothCount, 0, LegacyLength: true));
        }
        return _inputs = new(candidate, weighted.ToImmutableArray(), players.ToArray(), samples.ToArray());
    }
    public LyraMainLeanResolved Resolve(LyraMainLeanSyncInputs inputs, ReadOnlySpan<AlsAssetPlayerHistory> players,
        ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        if (!ReferenceEquals(inputs, _inputs) || !ReferenceEquals(inputs.Candidate, _pending) || _resolved is not null)
            throw new InvalidOperationException("Rejected stale/repeated Main Lean Sync result.");
        var nodes = inputs.Weighted.ToArray();
        for (var index = 0; index < 3; index++)
        {
            var found = -1;
            for (var i = 0; i < players.Length; i++) if (players[i].PlayerId == _playerBase + index)
            { if (found >= 0) throw new InvalidOperationException("Duplicate Main Lean clock."); found = i; }
            if ((found >= 0) != inputs.Candidate.Active[index]) throw new InvalidOperationException("Missing/hidden Main Lean clock.");
            if (found < 0) continue;
            var clock = players[found]; var node = nodes[index];
            if (clock.AssetId != _assetId || clock.Epoch != _epoch || clock.SampleCount != node.Samples.Length ||
                !float.IsFinite(clock.Time) || clock.Time is < 0 or > 1 || !float.IsFinite(clock.DeltaPrevious) ||
                !float.IsFinite(clock.Delta) || clock.SampleStart < 0 || clock.SampleStart > samples.Length - clock.SampleCount)
                throw new InvalidOperationException("Foreign/nonfinite Main Lean clock.");
            var resolvedSamples = ImmutableArray.CreateBuilder<LyraMainLeanSampleState>(node.Samples.Length);
            for (var i = 0; i < node.Samples.Length; i++)
            {
                var expected = node.Samples[i]; var value = samples[clock.SampleStart + i];
                var duration = _sequences[_sequenceBase + expected.Weight.SampleId].DurationSeconds;
                if (value.SampleId != expected.Clock.SampleId || value.AnimationId != expected.Clock.AnimationId ||
                    !float.IsFinite(value.Time) || value.Time < 0 || value.Time > duration ||
                    !float.IsFinite(value.PreviousTime) || !float.IsFinite(value.DeltaPrevious) || !float.IsFinite(value.Delta))
                    throw new InvalidOperationException("Foreign/nonfinite Main Lean sample.");
                resolvedSamples.Add(expected with { Clock = value });
            }
            nodes[index] = node with { Time = clock.Time, DeltaPrevious = clock.DeltaPrevious, Delta = clock.Delta, Samples = resolvedSamples.MoveToImmutable() };
        }
        return _resolved = new(inputs, nodes.ToImmutableArray());
    }
    public void Evaluate(LyraMainLeanResolved resolved, int node, Span<AlsPrecisePose> output)
    {
        if (!ReferenceEquals(resolved, _resolved) || node is < 0 or > 2 || !resolved.Inputs.Candidate.Active[node] || output.Length != 81)
            throw new InvalidOperationException("Main Lean pose requires its active synchronized occurrence.");
        if (Interlocked.CompareExchange(ref _evaluating[node], 1, 0) != 0)
            throw new InvalidOperationException("Main Lean occurrence is already being evaluated.");
        try
        {
            var samples = resolved.States[node].Samples; var scratch = _scratch[node];
            for (var i = 0; i < samples.Length; i++)
            {
                var sample = samples[i]; _samplers[node][sample.Weight.SampleId].Sample(sample.Clock.Time, scratch);
                var weight = Math.Clamp(sample.Weight.Weight, 0, 1);
                for (var bone = 0; bone < output.Length; bone++)
                    output[bone] = i == 0 ? AlsPrecisePoseBlender.Scale(scratch[bone], weight)
                        : AlsPrecisePoseBlender.Accumulate(output[bone], scratch[bone], weight);
            }
            // BlendPosesTogether normalizes multi-sample blends, followed by
            // UBlendSpace's unconditional normalization.
            if(samples.Length>1)
                for (var bone = 0; bone < output.Length; bone++) output[bone] = output[bone].Normalized();
            for (var bone = 0; bone < output.Length; bone++) output[bone] = output[bone].Normalized();
        }
        finally { Volatile.Write(ref _evaluating[node], 0); }
    }
    public void Commit(LyraMainLeanResolved resolved)
    {
        ValidateCommit(resolved);
        States = resolved.States; _pending = null; _inputs = null; _resolved = null;
    }
    public void ValidateCommit(LyraMainLeanResolved resolved)
    {
        if (!ReferenceEquals(resolved, _resolved) || _evaluating.Any(v => v != 0))
            throw new InvalidOperationException("Rejected stale or evaluating Main Lean commit.");
    }
    public void Cancel() { _pending = null; _inputs = null; _resolved = null; }
}
