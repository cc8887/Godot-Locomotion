using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Import.Compilation;

public sealed record AlsRefactoredSourceOwnerDefinition(uint Owner,
    IReadOnlyList<AlsRefactoredSourcePlayerDefinition> Players, IReadOnlyDictionary<int, string> Groups);
public readonly record struct AlsRefactoredSourceOwnerPlayer(uint Owner, int LocalPlayer);

/// <summary>One outer AnimInstance Sync scope for linked graphs. Collect actual node visits,
/// finish one shared asset tick, then read clocks/poses/notifies. Same-name groups cross owner
/// boundaries; local player IDs never do. No child may advance or commit this physical bank.</summary>
public sealed class AlsRefactoredSourceSyncScope
{
    private readonly uint _character, _generation;
    private readonly Dictionary<uint, (int First, int Count)> _owners = new();
    private readonly AlsRefactoredSourceOwnerPlayer[] _ownerPlayers;
    private readonly AlsRefactoredSourcePlayerDefinition[] _definitions;
    private readonly string[] _groupNames;
    private readonly AlsRefactoredSourcePlayerRuntime _runtime;
    private readonly AlsRefactoredSourceNotifyBinding? _notifies;
    private readonly AlsRefactoredSourcePlayerInput[] _inputs;
    private readonly AlsRefactoredSourcePlayerInput[] _localInputs;
    private readonly HashSet<uint> _viewOwners = new();
    private readonly AlsPoseUpdateContext[] _contexts;
    private readonly AlsRefactoredNotifyPlayerContext[] _notifyContexts;
    private readonly bool[] _registered, _pendingReset, _nextPendingReset;
    private readonly int[][] _orders;
    private readonly int[] _orderCounts = new int[2];
    private int[] _nextOrder;
    private readonly int[] _fullOrder;
    private int _writeIndex, _candidateWriteIndex, _nextOrderCount, _count;
    private AlsPoseUpdateContext _context;
    private bool _begun, _complete, _initialize;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public int PlayerCount => _definitions.Length;
    public string Source(uint owner, int player) => _definitions[PlayerId(owner, player)].Source;
    public ReadOnlySpan<string> GroupNames => _groupNames;
    public ReadOnlySpan<int> EncounteredGroups { get { RequireComplete(); return _nextOrder.AsSpan(0, _nextOrderCount); } }
    public ReadOnlySpan<AlsAssetSyncBatchGroupHistory> Groups { get { RequireComplete(); return _runtime.Groups[.._nextOrderCount]; } }
    public ReadOnlySpan<AlsAssetSyncPlayer> Ticks { get { RequireComplete(); return _runtime.Ticks; } }
    public ReadOnlySpan<AlsAssetSyncSample> ResolvedSamples { get { RequireComplete(); return _runtime.ResolvedSamples; } }
    public ReadOnlySpan<AlsAssetPlayerHistory> Players { get { RequireComplete(); return _runtime.Players; } }
    public ReadOnlySpan<AlsAssetSampleHistory> Samples { get { RequireComplete(); return _runtime.Samples; } }
    public ReadOnlySpan<AlsAssetPlayerTickContext> TickContexts { get { RequireComplete(); return _runtime.TickContexts; } }
    public ReadOnlySpan<AlsPoseUpdateContext> RegistrationContexts { get { RequireComplete(); return _contexts.AsSpan(0, _count); } }

    public AlsRefactoredSourceSyncScope(uint character, uint generation, AlsRefactoredAnimationCatalog catalog,
        AlsRefactoredSyncBank sync, IReadOnlyDictionary<string, AlsRefactoredTriangulationProfile> profiles,
        IEnumerable<AlsRefactoredSourceOwnerDefinition> owners, AlsRefactoredNotifyBank? notifies = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        _character = character; _generation = generation;
        var definitions = new List<AlsRefactoredSourcePlayerDefinition>();
        var identities = new List<AlsRefactoredSourceOwnerPlayer>();
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var owner in owners.OrderBy(o => o.Owner))
        {
            if (owner.Owner == 0 || owner.Players.Count == 0 || _owners.ContainsKey(owner.Owner)) throw new ArgumentException("Invalid source owner.");
            var local = owner.Players.OrderBy(p => p.PlayerId).ToArray(); var first = definitions.Count;
            if (!local.Where(p => p.GroupId >= 0).Select(p => p.GroupId).ToHashSet().SetEquals(owner.Groups.Keys))
                throw new ArgumentException("Incomplete source owner group bindings.");
            for (var i = 0; i < local.Length; i++)
            {
                var d = local[i]; var group = -1;
                if (d.PlayerId != i || d.GroupId < -1) throw new ArgumentException("Invalid local source player layout.");
                if (d.GroupId >= 0)
                {
                    var name = owner.Groups[d.GroupId];
                    if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid source group name.");
                    if (!names.TryGetValue(name, out group)) { group = names.Count; names.Add(name, group); }
                }
                definitions.Add(d with { PlayerId = first + i, GroupId = group }); identities.Add(new(owner.Owner, i));
            }
            _owners.Add(owner.Owner, (first, local.Length));
        }
        _definitions = definitions.ToArray(); _ownerPlayers = identities.ToArray(); _groupNames = names.Keys.ToArray();
        _runtime = new(catalog, sync, profiles, _definitions);
        // There is one physical source bank. Its globally unique player IDs include all linked owners.
        if (notifies is not null) _notifies = new(1, _runtime, notifies);
        _inputs = new AlsRefactoredSourcePlayerInput[_definitions.Length]; _contexts = new AlsPoseUpdateContext[_inputs.Length];
        _localInputs = new AlsRefactoredSourcePlayerInput[_inputs.Length];
        _notifyContexts = new AlsRefactoredNotifyPlayerContext[_inputs.Length]; _registered = new bool[_inputs.Length];
        _pendingReset = new bool[_inputs.Length]; _nextPendingReset = new bool[_inputs.Length];
        _orders = [new int[names.Count], new int[names.Count]]; _nextOrder = new int[names.Count]; _fullOrder = new int[names.Count];
    }
    public int PlayerId(uint owner, int localPlayer)
    {
        if (!_owners.TryGetValue(owner, out var range) || (uint)localPlayer >= (uint)range.Count) throw new ArgumentException("Foreign source player.");
        return range.First + localPlayer;
    }
    internal IAlsRefactoredSourcePlayers CreateView(AlsRefactoredLocomotionSourceOwner owner)
    {
        var id = (uint)owner;
        if (_begun || !_owners.TryGetValue(id, out var range) || !_viewOwners.Add(id)) throw new ArgumentException("Duplicate or foreign source view.");
        return new AlsRefactoredSourcePlayerView(this, _runtime, id, range.Count);
    }
    internal void ValidateFrame(long frame)
    { RequireComplete(); if (frame != _context.Identity.FrameId) throw new ArgumentException("Foreign shared source frame."); _runtime.ValidateCommit(frame); }
    internal void ValidateOwnerInputs(uint owner, long frame, float delta, ReadOnlySpan<AlsRefactoredSourcePlayerInput> inputs)
    {
        RequireCollecting();
        if (frame != _context.Identity.FrameId || delta != _context.Delta) throw new ArgumentException("Foreign source view frame.");
        var range = _owners[owner]; var count = 0;
        for (var i = range.First; i < range.First + range.Count; i++) if (_registered[i]) count++;
        if (count != inputs.Length) throw new ArgumentException("Missing graph node registrations.");
        for (var i = 0; i < inputs.Length; i++)
        {
            var id = PlayerId(owner, inputs[i].PlayerId);
            if (!_registered[id] || _localInputs[id] != inputs[i]) throw new ArgumentException("Graph registration differs from source input.");
            for (var j = 0; j < i; j++) if (inputs[j].PlayerId == inputs[i].PlayerId) throw new ArgumentException("Duplicate graph source input.");
        }
    }
    public AlsRefactoredSourceOwnerPlayer OwnerPlayer(int player) => (uint)player < (uint)_ownerPlayers.Length
        ? _ownerPlayers[player] : throw new ArgumentException("Foreign scope player.");
    public void Begin(in AlsPoseUpdateContext context, bool initialize = false)
    {
        if (_begun || context.Identity.CharacterId != _character || context.Identity.SlotGeneration != _generation ||
            context.Identity.FrameId < 0 || context.UpdateCounter is not { HasUpdated: true } || !context.HasSharedContext ||
            CommittedIdentity.SlotGeneration != 0 && context.Identity.FrameId <= CommittedIdentity.FrameId)
            throw new ArgumentException("Invalid source scope frame.");
        _context = context; _initialize = initialize; _count = 0; Array.Clear(_registered);
        _pendingReset.CopyTo(_nextPendingReset, 0);
        // FAnimSync has two persistent TMaps. Reset/ResetAll clear entries' contents,
        // not their keys/order; ResetAll only puts the write index back at zero.
        _candidateWriteIndex = initialize ? 0 : _writeIndex;
        _orders[_candidateWriteIndex].CopyTo(_nextOrder, 0); _nextOrderCount = _orderCounts[_candidateWriteIndex];
        _begun = true; _complete = false;
    }
    public void ReinitializeOwner(uint owner)
    {
        RequireCollecting();
        if (!_owners.TryGetValue(owner, out var range)) throw new ArgumentException("Foreign source owner reset.");
        for (var i = range.First; i < range.First + range.Count; i++)
            if (_registered[i]) throw new InvalidOperationException("Owner initialization must precede node visits.");
        _nextPendingReset.AsSpan(range.First, range.Count).Fill(true);
    }
    /// <summary>Call at the actual node visit, before deferred child-cache traversal. Do not
    /// group visits by owner afterwards: native ungrouped order and group insertion order matter.</summary>
    public void Enqueue(uint owner, in AlsRefactoredSourcePlayerInput input, in AlsPoseUpdateContext context, bool scopeFiltered = false)
    {
        RequireCollecting(); var id = PlayerId(owner, input.PlayerId);
        if (_registered[id] || context.Identity != _context.Identity || context.UpdateCounter != _context.UpdateCounter ||
            context.Delta != _context.Delta || !context.HasSharedContext || input.Weight != context.Weight ||
            !float.IsFinite(input.PlayRate) || !float.IsFinite(input.StartPosition) || !float.IsFinite(input.BlendInput.X) ||
            !float.IsFinite(input.BlendInput.Y) || input.Weight > 1)
            throw new ArgumentException("Duplicate or foreign source node visit.");
        var mapped = input with { PlayerId = id };
        if (_nextPendingReset[id]) mapped = mapped with { Reinitialize = true,
            StartPosition = input.Reinitialize ? input.StartPosition : _definitions[id].StartPosition };
        _localInputs[id] = input; _inputs[_count] = mapped; _contexts[_count] = context;
        _notifyContexts[_count++] = new(id, context.IsActive, scopeFiltered); _registered[id] = true; _nextPendingReset[id] = false;
        var group = _definitions[id].GroupId;
        if (group >= 0 && !_nextOrder.AsSpan(0, _nextOrderCount).Contains(group)) _nextOrder[_nextOrderCount++] = group;
    }
    public void Complete()
    {
        RequireCollecting();
        _nextOrder.AsSpan(0, _nextOrderCount).CopyTo(_fullOrder); var count = _nextOrderCount;
        // Reserve never-encountered groups as empty trailing storage. They neither tick nor
        // appear in the scope's visible map, and are reordered on their first actual visit.
        for (var group = 0; group < _groupNames.Length; group++)
            if (!_nextOrder.AsSpan(0, _nextOrderCount).Contains(group)) _fullOrder[count++] = group;
        try { _runtime.Prepare(_context.Identity.FrameId, _inputs.AsSpan(0, _count), _context.Delta, _initialize, _fullOrder); _complete = true; }
        catch { Discard(); throw; }
    }
    public int BuildNotifyTicks(Span<AlsRefactoredSourceNotifyTick> output)
    {
        RequireComplete();
        return (_notifies ?? throw new InvalidOperationException("No original notify bank.")).BuildTicks(_context.Identity.FrameId,
            _notifyContexts.AsSpan(0, _count), output);
    }
    public int Extract(in AlsRefactoredSourceNotifyTick tick, Span<AlsAssetNotifyOccurrence> output)
    { RequireComplete(); return (_notifies ?? throw new InvalidOperationException("No original notify bank.")).Extract(tick, output); }
    public void Evaluate(uint owner, int player) { RequireComplete(); _runtime.Evaluate(_context.Identity.FrameId, PlayerId(owner, player)); }
    public ReadOnlySpan<AlsPrecisePose> Pose(uint owner, int player) { RequireComplete(); return _runtime.Pose(PlayerId(owner, player)); }
    public ReadOnlySpan<AlsInertialCurve> Curves(uint owner, int player) { RequireComplete(); return _runtime.Curves(PlayerId(owner, player)); }
    public void ValidateCommit(in AlsFrameIdentity identity)
    {
        RequireComplete(); if (identity != _context.Identity) throw new ArgumentException("Foreign source scope commit.");
        _runtime.ValidateCommit(identity.FrameId);
    }
    public void Commit(in AlsFrameIdentity identity)
    {
        ValidateCommit(identity); _runtime.Commit(identity.FrameId); _nextPendingReset.CopyTo(_pendingReset, 0);
        (_orders[_candidateWriteIndex], _nextOrder) = (_nextOrder, _orders[_candidateWriteIndex]);
        _orderCounts[_candidateWriteIndex] = _nextOrderCount; _writeIndex = 1 - _candidateWriteIndex;
        CommittedIdentity = identity; _begun = _complete = false;
    }
    public void Discard() { _runtime.Cancel(); _begun = _complete = false; }
    private void RequireCollecting() { if (!_begun || _complete) throw new InvalidOperationException("No collecting source scope."); }
    private void RequireComplete() { if (!_begun || !_complete) throw new InvalidOperationException("Source scope has not completed its shared tick."); }
}
