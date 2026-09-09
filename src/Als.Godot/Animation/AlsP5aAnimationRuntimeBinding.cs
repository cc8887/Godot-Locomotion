using Godot;
using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using CoreOccurrenceEntry = GodotAls.Core.Contracts.AlsP5OccurrenceLayoutEntry;
using CoreOccurrenceKind = GodotAls.Core.Contracts.AlsP5OccurrenceSourceKind;

namespace GodotAls.Animation;

internal readonly record struct AlsP5aPhysicalSlotDescriptor(
    CoreOccurrenceKind SourceKind,
    int SourceBindingIndex,
    int GraphSlotIndex,
    int AnimationId,
    int OccurrenceHandleId,
    int AuthorityGroupId,
    int SyncGroupId,
    int SyncMemberIndex,
    StringName GraphNodeName);

internal sealed class AlsP5aAnimationRuntimeBinding : IDisposable
{
    private readonly AlsP5CoreRuntimeBindingSnapshot _coreBindings;
    private readonly IReadOnlyList<AlsP5aPhysicalSlotDescriptor> _physicalSlots;
    private readonly StringName[] _ownedGraphNodeNames;
    private int _disposed;

    private AlsP5aAnimationRuntimeBinding(
        AlsP5CoreRuntimeBindingSnapshot coreBindings,
        AlsP5aAnimationLibraryStamp stamp,
        AlsP5aPhysicalSlotDescriptor[] physicalSlots,
        StringName[] ownedGraphNodeNames)
    {
        _coreBindings = coreBindings;
        Stamp = stamp;
        _physicalSlots = Array.AsReadOnly(physicalSlots);
        _ownedGraphNodeNames = ownedGraphNodeNames;
    }

    public AlsP5aAnimationLibraryStamp Stamp { get; }

    public IReadOnlyList<AlsP5aPhysicalSlotDescriptor> PhysicalSlots => _physicalSlots;

    public static AlsP5aAnimationRuntimeBinding Compile(
        AlsP5CoreRuntimeBindingSnapshot coreBindings,
        AlsAnimationLibraryBuildResult library)
    {
        ArgumentNullException.ThrowIfNull(coreBindings);
        ArgumentNullException.ThrowIfNull(library);
        ValidateStamp(library.Stamp, coreBindings);
        library.ValidateLiveP5aSkeleton();

        var occurrence = coreBindings.CreateOccurrenceLayoutView();
        AlsP5OccurrenceLayoutContract.Validate(
            occurrence.Version, occurrence.Digest, occurrence.Entries);
        var core = coreBindings.CreateCoreView();
        var graph = coreBindings.CreateGraphBuildView();
        ValidateLibraryResources(library, in graph, in core);
        ValidateNonBankOccurrences(in occurrence, in core);
        return CreatePhysicalSlots(coreBindings, in occurrence, in graph, in core);
    }

    public AlsP5RuntimeBindings CreateCoreView() => _coreBindings.CreateCoreView();

    public AlsP5OccurrenceLayoutView CreateOccurrenceLayoutView() =>
        _coreBindings.CreateOccurrenceLayoutView();

    public AlsP5GraphBuildView CreateGraphBuildView() =>
        _coreBindings.CreateGraphBuildView();

    internal static void ValidateStamp(
        AlsP5aAnimationLibraryStamp stamp,
        AlsP5CoreRuntimeBindingSnapshot coreBindings)
    {
        ArgumentNullException.ThrowIfNull(coreBindings);
        if (!string.Equals(stamp.AnimationSetDefinitionDigest,
                coreBindings.AnimationSetDefinitionDigest, StringComparison.Ordinal) ||
            stamp.LayoutDigest != coreBindings.LayoutDigest ||
            stamp.BindingDigest != coreBindings.Digest ||
            stamp.GraphDigest != coreBindings.GraphDigest)
        {
            throw new InvalidOperationException(
                "The P5A animation library stamp does not match the binding snapshot.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var name in _ownedGraphNodeNames) name.Dispose();
    }

    private static AlsP5aAnimationRuntimeBinding CreatePhysicalSlots(
        AlsP5CoreRuntimeBindingSnapshot snapshot,
        in AlsP5OccurrenceLayoutView occurrence,
        in AlsP5GraphBuildView graph,
        in AlsP5RuntimeBindings core)
    {
        var baseAnimationIds = new[] { graph.StandingIdleAnimationId }
            .Concat(graph.StandingSamples.ToArray().Select(value => value.AnimationId))
            .Append(graph.CrouchingIdleAnimationId)
            .Concat(graph.CrouchingSamples.ToArray().Select(value => value.AnimationId))
            .Append(graph.JumpStartAnimationId)
            .Append(graph.FallLoopAnimationId)
            .Append(graph.LandAnimationId)
            .ToArray();
        if (baseAnimationIds.Length != 22 || graph.Turns.Length != 8 || graph.Rotates.Length != 4)
        {
            throw new InvalidOperationException("The fixed P5A physical bank shape is invalid.");
        }

        var descriptors = new AlsP5aPhysicalSlotDescriptor[
            baseAnimationIds.Length + graph.Turns.Length + graph.Rotates.Length];
        var names = new StringName[descriptors.Length];
        var occurrenceEntries = occurrence.Entries.ToArray();
        var syncOccurrences = core.SyncOccurrences.ToArray();
        try
        {
            var destination = 0;
            AddBank(CoreOccurrenceKind.Base, baseAnimationIds, "P5Base");
            AddBank(CoreOccurrenceKind.Turn,
                graph.Turns.ToArray().Select(value => value.AnimationId).ToArray(), "P5Turn");
            AddBank(CoreOccurrenceKind.Rotate,
                graph.Rotates.ToArray().Select(value => value.AnimationId).ToArray(), "P5Rotate");
            if (descriptors.Count(value => value.SyncGroupId >= 0) != syncOccurrences.Length)
            {
                throw new InvalidOperationException(
                    "The configured P5A Sync occurrences do not close over the Base bank.");
            }
            return new AlsP5aAnimationRuntimeBinding(
                snapshot, new AlsP5aAnimationLibraryStamp(
                    snapshot.AnimationSetDefinitionDigest,
                    snapshot.LayoutDigest,
                    snapshot.Digest,
                    snapshot.GraphDigest),
                descriptors,
                names);

            void AddBank(
                CoreOccurrenceKind sourceKind,
                IReadOnlyList<int> animationIds,
                string prefix)
            {
                for (var sourceIndex = 0; sourceIndex < animationIds.Count; sourceIndex++)
                {
                    var entry = FindOccurrence(
                        occurrenceEntries, sourceKind, sourceIndex, sourceIndex);
                    var syncGroupId = -1;
                    var syncMemberIndex = -1;
                    foreach (ref readonly var sync in syncOccurrences.AsSpan())
                    {
                        if (sync.OccurrenceHandleId != entry.OccurrenceHandleId) continue;
                        if (sourceKind != CoreOccurrenceKind.Base ||
                            sync.AnimationId != animationIds[sourceIndex] || syncGroupId >= 0)
                        {
                            throw new InvalidOperationException(
                                "A P5A Sync occurrence does not identify one Base slot.");
                        }
                        syncGroupId = sync.GroupId;
                        syncMemberIndex = sync.GroupMemberIndex;
                    }

                    names[destination] = new StringName($"{prefix}{sourceIndex}");
                    descriptors[destination] = new AlsP5aPhysicalSlotDescriptor(
                        sourceKind,
                        sourceIndex,
                        sourceIndex,
                        animationIds[sourceIndex],
                        entry.OccurrenceHandleId,
                        entry.AuthorityGroupId,
                        syncGroupId,
                        syncMemberIndex,
                        names[destination]);
                    destination++;
                }
            }
        }
        catch
        {
            foreach (var name in names) name?.Dispose();
            throw;
        }
    }

    private static void ValidateNonBankOccurrences(
        in AlsP5OccurrenceLayoutView occurrence,
        in AlsP5RuntimeBindings core)
    {
        var transition = FindOccurrence(
            occurrence.Entries, CoreOccurrenceKind.Transition, 0, 0);
        if (transition.OccurrenceHandleId != core.DynamicTransition.OccurrenceHandleId ||
            transition.AuthorityGroupId != core.DynamicTransition.AuthorityGroupId)
        {
            throw new InvalidOperationException("The Dynamic Transition occurrence binding changed.");
        }

        for (var index = 0; index < core.ActionDefinitions.Length; index++)
        {
            ref readonly var action = ref core.ActionDefinitions[index];
            var entry = FindOccurrence(
                occurrence.Entries, CoreOccurrenceKind.ActionMontage, index, 0);
            if (action.DefinitionId != index ||
                action.OccurrenceHandleId != entry.OccurrenceHandleId ||
                action.MontageAuthorityGroupId != entry.AuthorityGroupId ||
                action.MontageAuthorityGroupId == action.SequenceAuthorityGroupId)
            {
                throw new InvalidOperationException("An Action Montage occurrence binding changed.");
            }
        }

        for (var index = 0; index < core.ActionSegments.Length; index++)
        {
            ref readonly var segment = ref core.ActionSegments[index];
            var entry = FindOccurrence(
                occurrence.Entries, CoreOccurrenceKind.ActionSequence, index, 0);
            if (segment.OccurrenceHandleId != entry.OccurrenceHandleId ||
                (uint)segment.ActionDefinitionId >= (uint)core.ActionDefinitions.Length ||
                entry.AuthorityGroupId !=
                    core.ActionDefinitions[segment.ActionDefinitionId].SequenceAuthorityGroupId)
            {
                throw new InvalidOperationException("An Action Sequence occurrence binding changed.");
            }
        }
    }

    private static void ValidateLibraryResources(
        AlsAnimationLibraryBuildResult library,
        in AlsP5GraphBuildView graph,
        in AlsP5RuntimeBindings core)
    {
        var expected = new HashSet<int>(graph.AllAnimationIds.ToArray());
        Add(graph.StandingIdleAnimationId);
        Add(graph.CrouchingIdleAnimationId);
        Add(graph.JumpStartAnimationId);
        Add(graph.FallLoopAnimationId);
        Add(graph.LandAnimationId);
        Add(graph.LeanAdditiveBaseAnimationId);
        Add(graph.Aim.DownAnimationId);
        Add(graph.Aim.ForwardAnimationId);
        Add(graph.Aim.UpAnimationId);
        Add(graph.Aim.AdditiveBasePoseAnimationId);
        foreach (ref readonly var value in graph.StandingSamples) Add(value.AnimationId);
        foreach (ref readonly var value in graph.CrouchingSamples) Add(value.AnimationId);
        foreach (ref readonly var value in graph.LeanSamples) Add(value.AnimationId);
        foreach (ref readonly var value in graph.Turns) Add(value.AnimationId);
        foreach (ref readonly var value in graph.Rotates) Add(value.AnimationId);
        Add(core.DynamicTransition.StandingLeft.AnimationId);
        Add(core.DynamicTransition.StandingLeft.AdditiveBaseAnimationId);
        Add(core.DynamicTransition.StandingRight.AnimationId);
        Add(core.DynamicTransition.StandingRight.AdditiveBaseAnimationId);
        Add(core.DynamicTransition.CrouchingLeft.AnimationId);
        Add(core.DynamicTransition.CrouchingLeft.AdditiveBaseAnimationId);
        Add(core.DynamicTransition.CrouchingRight.AnimationId);
        Add(core.DynamicTransition.CrouchingRight.AdditiveBaseAnimationId);
        foreach (ref readonly var member in core.SyncMembers) Add(member.AnimationId);
        foreach (ref readonly var segment in core.ActionSegments) Add(segment.AnimationId);

        if (library.Resources.Count != expected.Count)
        {
            throw new InvalidOperationException("The P5A library resource closure changed.");
        }
        var normalized = graph.NormalizedAnimationIds.ToArray().ToHashSet();
        foreach (var animationId in expected)
        {
            if (!library.Resources.TryGetValue(animationId, out var descriptor) ||
                descriptor.AnimationId != animationId ||
                descriptor.PlayLengthSeconds <= 0f ||
                !float.IsFinite(descriptor.PlayLengthSeconds) ||
                !string.Equals(descriptor.ClipName.ToString(), $"clip_{animationId}",
                    StringComparison.Ordinal) ||
                descriptor.NormalizedTrack != (normalized.Contains(animationId) ? (byte)1 : (byte)0) ||
                !library.Library.HasAnimation(descriptor.ClipName))
            {
                throw new InvalidOperationException(
                    $"The P5A library resource binding is invalid: {animationId}.");
            }
            using var animation = library.Library.GetAnimation(descriptor.ClipName);
            if (animation is null || Math.Abs(animation.Length - descriptor.PlayLengthSeconds) >
                (1.0 / 30.0) + 1e-6)
            {
                throw new InvalidOperationException(
                    $"The P5A library resource duration changed: {animationId}.");
            }
        }

        void Add(int animationId)
        {
            if (animationId < 0) throw new InvalidOperationException("A snapshot animation ID is invalid.");
            expected.Add(animationId);
        }
    }

    private static CoreOccurrenceEntry FindOccurrence(
        ReadOnlySpan<CoreOccurrenceEntry> entries,
        CoreOccurrenceKind sourceKind,
        int sourceBindingIndex,
        int graphSlotIndex)
    {
        var found = false;
        var result = default(CoreOccurrenceEntry);
        foreach (ref readonly var entry in entries)
        {
            if (entry.SourceKind != sourceKind || entry.SourceBindingIndex != sourceBindingIndex)
            {
                continue;
            }
            if (found || entry.GraphSlotIndex != graphSlotIndex)
            {
                throw new InvalidOperationException("A P5A occurrence slot mapping is ambiguous.");
            }
            found = true;
            result = entry;
        }
        return found
            ? result
            : throw new InvalidOperationException("A P5A occurrence slot mapping is missing.");
    }
}
