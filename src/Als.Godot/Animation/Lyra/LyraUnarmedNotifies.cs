using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

internal enum LyraNotifyKind { FootPlantLeft, FootPlantRight, ContextEffect, TransitionToLocomotion }

internal readonly record struct LyraNotifyEvent(int Index, string Name, string NotifyClass,
    string NotifyStateClass, double TriggerTime, double EndTriggerTime, int Track,
    double TriggerWeightThreshold, double TriggerChance, int FilterType, int FilterLod,
    bool CanBeFilteredViaRequest, bool TriggerOnDedicatedServer, bool TriggerOnFollower,
    bool BranchingPoint, LyraNotifyKind Kind);

internal readonly record struct LyraQueuedNotify(string Slot, LyraNotifyEvent Event,
    bool ReachedEnd, bool ScopeExit = false)
{
    public LyraNotifyKind Kind => Event.Kind;
}

internal readonly record struct LyraNotifySource(LyraNotifyEvent[] Events,
    AlsAssetNotifyDefinition[] Definitions, AlsAssetNotifyPolicy[] Policies, float Length, bool Looping);

internal sealed class LyraUnarmedNotifies
{
    private const string ResourcePath = "res://assets/generated/lyra_als/unarmed_notifies.json";
    private const string AuxiliaryResourcePath = "res://assets/generated/lyra_als/unarmed_aux_notifies.json";
    private const string RemainingResourcePath = "res://assets/generated/lyra_als/unarmed_remaining_notifies.json";
    private const string PistolResourcePath = "res://assets/generated/lyra_als/pistol_notifies.json";
    private const string RifleResourcePath = "res://assets/generated/lyra_als/rifle_notifies.json";
    private const string TransitionClass = "/Game/Characters/Heroes/Mannequin/Animations/AnimNotifies/TransitionToLocomotion.TransitionToLocomotion_C";
    private const string LeftClass = "/Game/Effects/AnimationNotifies/AN_FootPlant_Left.AN_FootPlant_Left_C";
    private const string RightClass = "/Game/Effects/AnimationNotifies/AN_FootPlant_Right.AN_FootPlant_Right_C";
    private const string ContextClass = "/Script/LyraGame.AnimNotify_LyraContextEffects";
    private readonly Dictionary<string, LyraNotifyEvent[]> _events;
    private readonly Dictionary<string, LyraNotifySource> _sources;

    private LyraUnarmedNotifies(Dictionary<string, LyraNotifyEvent[]> events, int eventCount,
        Dictionary<string, LyraNotifySource> sources, int transitionStateCount)
    {
        _events = events;
        _sources = sources;
        EventCount = eventCount;
        TransitionStateCount = transitionStateCount;
    }

    public int EventCount { get; }
    public int TransitionStateCount { get; }

    public bool TransitionToLocomotionActive(string slot, double time) =>
        _events[slot].Any(entry => entry.NotifyStateClass == TransitionClass &&
            time >= entry.TriggerTime && time < entry.EndTriggerTime);

    public IReadOnlyList<LyraNotifyEvent> Events(string slot) => _events[slot];

    public int Collect(string slot, double previousTime, double tickDelta,
        ref uint randomSeed, Span<LyraQueuedNotify> output)
    {
        var source = _sources[slot];
        if (!double.IsFinite(previousTime) || !double.IsFinite(tickDelta) ||
            previousTime < 0 || previousTime > source.Length ||
            tickDelta < -float.MaxValue || tickDelta > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(previousTime));
        Span<AlsAssetNotifyOccurrence> extracted = stackalloc AlsAssetNotifyOccurrence[64];
        if (!AlsTimelineRuntime.TryExtractAssetNotifies(source.Definitions, source.Length,
                (float)previousTime, (float)tickDelta, source.Looping, extracted,
                out var extractedCount, out var failure))
            throw new InvalidOperationException($"Lyra notify extraction failed: {slot}/{failure}.");
        Span<AlsAssetNotifyReference> incoming = stackalloc AlsAssetNotifyReference[64];
        Span<AlsAssetNotifyReference> scratch = stackalloc AlsAssetNotifyReference[64];
        Span<AlsAssetNotifyReference> queued = stackalloc AlsAssetNotifyReference[64];
        var currentTime = (float)(previousTime + tickDelta);
        if (source.Looping)
        {
            currentTime %= source.Length;
            if (currentTime < 0) currentTime += source.Length;
        }
        else currentTime = Math.Clamp(currentTime, 0, source.Length);
        for (var index = 0; index < extractedCount; index++)
            incoming[index] = new(extracted[index].DefinitionIndex, 0,
                currentTime, true, extracted[index].ReachedEnd);
        if (!AlsTimelineRuntime.TryQueueAssetNotifies(source.Policies, [],
                incoming[..extractedCount], new(true, false, 0, 1),
                AlsAssetNotifyQueueMode.Filtered, randomSeed, scratch, queued,
                out var count, out var nextSeed, out failure) || count > output.Length)
            throw new InvalidOperationException($"Lyra notify queue failed: {slot}/{failure}.");
        for (var index = 0; index < count; index++)
            output[index] = new(slot, source.Events[queued[index].PolicyIndex],
                queued[index].ReachedEnd);
        randomSeed = nextSeed;
        return count;
    }

    public static LyraUnarmedNotifies Load(LyraUnarmedCatalog catalog,
        LyraUnarmedAuxCatalog? auxiliary = null, LyraUnarmedRemainingCatalog? remaining = null,
        LyraPistolCatalog? pistol = null, LyraRifleCatalog? rifle = null)
    {
        var events = new Dictionary<string, LyraNotifyEvent[]>(StringComparer.Ordinal);
        var sources = new Dictionary<string, LyraNotifySource>(StringComparer.Ordinal);
        var total = 0;
        var transitions = 0;
        void ReadCatalog(string path, IReadOnlyDictionary<string, LyraClip> clips)
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidOperationException("Unsupported Lyra notify schema.");
            var seen = 0;
            foreach (var row in root.GetProperty("clips").EnumerateArray())
            {
                var slot = row.GetProperty("slot").GetString()!;
                if (!clips.TryGetValue(slot, out var clip) ||
                    row.GetProperty("source").GetString() != clip.SourceObjectPath ||
                    row.GetProperty("target").GetString() != clip.TargetObjectPath ||
                    !row.GetProperty("eventsUnchanged").GetBoolean())
                    throw new InvalidOperationException($"Lyra notify export differs from retarget: {slot}.");
                var source = row.GetProperty("sourceEvents");
                var target = row.GetProperty("targetEvents");
                if (source.GetArrayLength() != target.GetArrayLength())
                    throw new InvalidOperationException($"Lyra notify event count differs: {slot}.");
                var entries = new List<LyraNotifyEvent>();
                foreach (var value in source.EnumerateArray())
                {
                    var entry = new LyraNotifyEvent(value.GetProperty("index").GetInt32(),
                        value.GetProperty("name").GetString()!,
                        value.GetProperty("notifyClass").GetString()!,
                        value.GetProperty("notifyStateClass").GetString()!,
                        value.GetProperty("triggerTime").GetDouble(),
                        value.GetProperty("endTriggerTime").GetDouble(),
                        value.GetProperty("track").GetInt32(),
                        value.GetProperty("triggerWeightThreshold").GetDouble(),
                        value.GetProperty("triggerChance").GetDouble(),
                        value.GetProperty("filterType").GetInt32(),
                        value.GetProperty("filterLod").GetInt32(),
                        value.GetProperty("canBeFilteredViaRequest").GetBoolean(),
                        value.GetProperty("triggerOnDedicatedServer").GetBoolean(),
                        value.GetProperty("triggerOnFollower").GetBoolean(),
                        value.GetProperty("branchingPoint").GetBoolean(),
                        Classify(value.GetProperty("notifyClass").GetString()!,
                            value.GetProperty("notifyStateClass").GetString()!));
                    if (entry.Index != entries.Count || !double.IsFinite(entry.TriggerTime) ||
                        !double.IsFinite(entry.EndTriggerTime) || entry.TriggerTime < -1e-3 ||
                        entry.EndTriggerTime < entry.TriggerTime ||
                        entry.EndTriggerTime > clip.PlayLength + 1e-3 ||
                        !double.IsFinite(entry.TriggerChance) || entry.TriggerChance is < 0 or > 1 ||
                        entry.Track < 0 || entry.FilterLod < 0)
                        throw new InvalidOperationException($"Invalid Lyra notify event: {slot}/{entry.Index}.");
                    if (entry.NotifyStateClass.Length > 0)
                    {
                        if (entry.NotifyStateClass != TransitionClass)
                            throw new NotSupportedException($"Unknown Lyra notify state: {entry.NotifyStateClass}.");
                        transitions++;
                    }
                    entries.Add(entry);
                }
                if (!events.TryAdd(slot, entries.ToArray()))
                    throw new InvalidOperationException($"Duplicate Lyra notify clip: {slot}.");
                var definitions = entries.Select(entry => new AlsAssetNotifyDefinition(entry.Index,
                    (float)entry.TriggerTime, (float)entry.EndTriggerTime)).ToArray();
                var policies = entries.Select(entry => new AlsAssetNotifyPolicy(entry.Index,
                    entry.Index, entry.Track,
                    entry.Kind == LyraNotifyKind.TransitionToLocomotion ? -1 : entry.Index,
                    entry.Kind == LyraNotifyKind.TransitionToLocomotion ? entry.Index : -1,
                    entry.Index, (float)entry.TriggerWeightThreshold, (float)entry.TriggerChance,
                    (AlsAssetNotifyFilterType)entry.FilterType, entry.FilterLod,
                    entry.BranchingPoint ? AlsTimelineTickMode.BranchingPoint : AlsTimelineTickMode.Queued,
                    entry.CanBeFilteredViaRequest, entry.TriggerOnDedicatedServer,
                    entry.TriggerOnFollower)).ToArray();
                if (entries.Any(entry => entry.FilterType is not (0 or 1)) ||
                    !sources.TryAdd(slot, new(entries.ToArray(), definitions, policies,
                        (float)clip.PlayLength, clip.Loop || slot is "idle" or "crouch_idle" or "hipfire_crouch" or
                            "crouch_entry" or "crouch_exit" or "jump_start_loop" or "jump_fall_loop" ||
                            LyraUnarmedTiming.IsCycleSlot(slot))))
                    throw new InvalidOperationException($"Unsupported Lyra notification source: {slot}.");
                total += entries.Count;
                seen++;
            }
            if (seen != clips.Count)
                throw new InvalidOperationException("Lyra notify inventory is incomplete.");
        }
        ReadCatalog(ResourcePath, catalog.Clips);
        if (auxiliary is not null) ReadCatalog(AuxiliaryResourcePath, auxiliary.Clips);
        if (remaining is not null) ReadCatalog(RemainingResourcePath, remaining.Clips);
        if (pistol is not null) ReadCatalog(PistolResourcePath, pistol.Clips);
        if (rifle is not null) ReadCatalog(RifleResourcePath, rifle.Clips);
        var expectedTransitions = 4 + (auxiliary is null ? 0 : 4) +
            (remaining is null ? 0 : 4) + (pistol is null ? 0 : 12) + (rifle is null ? 0 : 12);
        if (transitions != expectedTransitions ||
            events.Count(entry => entry.Value.Any(item => item.NotifyStateClass == TransitionClass)) != expectedTransitions)
            throw new InvalidOperationException("Lyra notify inventory is incomplete.");
        return new LyraUnarmedNotifies(events, total, sources, transitions);
    }

    private static LyraNotifyKind Classify(string notifyClass, string stateClass) =>
        (notifyClass, stateClass) switch
        {
            (LeftClass, "") => LyraNotifyKind.FootPlantLeft,
            (RightClass, "") => LyraNotifyKind.FootPlantRight,
            (ContextClass, "") => LyraNotifyKind.ContextEffect,
            ("", TransitionClass) => LyraNotifyKind.TransitionToLocomotion,
            _ => throw new NotSupportedException($"Unknown Lyra notification: {notifyClass}/{stateClass}."),
        };
}
