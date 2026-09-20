using GodotAls.Core.Contracts;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Compilation;

public static partial class AlsP5OccurrenceLayoutCompiler
{
    /// <summary>Standing source replacement plus the still-physical legacy idle/crouch/air,
    /// Turn/Rotate banks and action lanes. Unbound native sources remain explicit, not certified.
    /// SourceBindingIndex is the compiled node index; GraphSlotIndex is its sample index.</summary>
    public static AlsP5OccurrenceLayout CompileSourceAware(AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose, AlsP5aAnimationRuntimeProfile p5a,
        AlsLocomotionSourceProfile sources, AlsP5SourceInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(locomotion); ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(p5a); ArgumentNullException.ThrowIfNull(sources); ArgumentNullException.ThrowIfNull(inventory);
        if (locomotion.SkeletonId != sources.SkeletonId || pose.SkeletonId != sources.SkeletonId ||
            inventory.SourceStamp != sources.RuntimeStamp || inventory.AnimationSetDefinitionDigest != sources.AnimationSetDefinitionDigest ||
            locomotion.StandingWalkRun.Length != 6 || locomotion.StandingSamples.Length != 13 || locomotion.CrouchingSamples.Length != 4 ||
            pose.Turns.Length != 8 || pose.Rotates.Length != 4)
            throw new ArgumentException("Source-aware layout requires consistent source inventory and current physical graph profiles.");
        var players = sources.RuntimePlayers; var samples = sources.RuntimeSamples;
        var nativeNodes = inventory.Nodes.Where(n => n.Kind == AlsP5InventoryNodeKind.AssetPlayer).ToArray();
        var bound = nativeNodes.Where(n => n.LocomotionPlayerId >= 0).ToDictionary(n => n.LocomotionPlayerId);
        if (bound.Count != players.Length) throw new ArgumentException("Native source coverage differs from the source snapshot.");
        foreach (var player in players)
            if (!bound.TryGetValue(player.PlayerId, out var node) || node.CompiledNodeIndex != player.CompiledNodeIndex || node.SampleCount != player.SampleCount)
                throw new ArgumentException("Native source identity differs from the source snapshot.");

        var cycles = players.Where(p => p.Domain == AlsLocomotionSourceDomain.Cycle && p.InputX == AlsSourceAxisInput.StrideBlend).ToArray();
        var matched = new HashSet<int>();
        foreach (var direction in locomotion.StandingWalkRun)
        {
            int[] clips = [direction.WalkPoseId, direction.WalkId, direction.RunPoseId, direction.RunId];
            var matches = cycles.Where(p => clips.Order().SequenceEqual(samples.AsSpan(p.SampleStart, p.SampleCount).ToArray()
                .Select(s => s.AnimationId).Order())).ToArray();
            if (matches.Length != 1 || !matched.Add(matches[0].PlayerId))
                throw new ArgumentException("Standing profile does not match the six compiled source BlendSpaces.");
        }
        var sprint = locomotion.StandingSamples.MaxBy(p => p.Y)!.AnimationId;
        var replacedClips = locomotion.StandingWalkRun.SelectMany(p => new[] { p.WalkId, p.RunId }).Append(sprint).Order();
        if (!replacedClips.SequenceEqual(locomotion.StandingSamples.Select(p => p.AnimationId).Order()))
            throw new ArgumentException("Standing physical samples differ from the sources replacing them.");
        if (!players.Any(p => p.Domain == AlsLocomotionSourceDomain.Cycle && p.Kind == AlsLocomotionSourceKind.Sequence &&
                samples[p.SampleStart].AnimationId == sprint)) throw new ArgumentException("Standing Sprint source is missing.");

        var entries = new List<AlsP5OccurrenceLayoutEntry>();
        var baseIds = BaseAnimationSlots(locomotion);
        var standingEnd = 1 + locomotion.StandingSamples.Length;
        var baseSlot = 0;
        for (var binding = 0; binding < baseIds.Length; binding++)
            if (binding == 0 || binding >= standingEnd)
                AddEntry(entries, AlsP5OccurrenceSourceKind.Base, binding, baseSlot++, 0);

        var mappings = new AlsP5SourceOccurrenceMapping[samples.Length];
        var authority = 1;
        foreach (var player in players)
        {
            var kind = player.Kind == AlsLocomotionSourceKind.TeleportEvaluator
                ? AlsP5OccurrenceSourceKind.SourceEvaluator : AlsP5OccurrenceSourceKind.SourceSample;
            for (var n = 0; n < player.SampleCount; n++)
            {
                var sample = samples[player.SampleStart + n]; var handle = entries.Count;
                AddEntry(entries, kind, player.CompiledNodeIndex, sample.SourceIndex, authority);
                mappings[sample.SampleId] = new(player.PlayerId, sample.SampleId, player.CompiledNodeIndex,
                    sample.SourceIndex, sample.AnimationId, handle);
            }
            authority++;
        }
        AddBank(entries, AlsP5OccurrenceSourceKind.Turn, pose.Turns.Length, 0, 2);
        AddBank(entries, AlsP5OccurrenceSourceKind.Rotate, pose.Rotates.Length, 0, 2);
        AddEntry(entries, AlsP5OccurrenceSourceKind.Transition, 0, 0, authority++);
        AddActionEntries(entries, p5a, authority);

        // These mappings describe only retained legacy sources. New group membership remains
        // player-owned in SourceView, never expanded into BlendSpace sample group members.
        var legacySync = new List<AlsP5SyncOccurrenceMapping>();
        foreach (var group in p5a.SyncGroups.OrderBy(g => g.GroupId))
        foreach (var member in group.Members.OrderBy(m => m.GroupMemberIndex))
        {
            var retained = entries.Where(e => e.SourceKind == AlsP5OccurrenceSourceKind.Base && baseIds[e.SourceBindingIndex] == member.AnimationId).ToArray();
            if (retained.Length == 0 && baseIds.AsSpan(1, standingEnd - 1).Contains(member.AnimationId)) continue;
            if (retained.Length != 1) throw new ArgumentException("Legacy Sync member has no unique retained physical source.");
            legacySync.Add(new(group.GroupId, member.GroupMemberIndex, member.AnimationId, retained[0].OccurrenceHandleId));
        }
        var version = AlsP5OccurrenceLayoutContract.SourceGraphVersion;
        var array = entries.ToArray(); Validate(version, array);
        var layout = new AlsP5OccurrenceLayout(version, ComputeDigest(version, array), array, legacySync.ToArray())
        {
            SourceStamp = sources.RuntimeStamp,
            SourceMappings = mappings,
            UnboundNativeSourceIndices = nativeNodes.Where(n => n.LocomotionPlayerId < 0).Select(n => n.CompiledNodeIndex).Order().ToArray()
        };
        AlsP5SourceOccurrenceContract.Validate(layout.CreateSourceView(), sources.CreateCoreView());
        return layout;
    }

    public static void ValidateSourceAware(AlsP5OccurrenceLayout layout, AlsLocomotionAnimationProfile locomotion,
        AlsPoseAnimationProfile pose, AlsP5aAnimationRuntimeProfile p5a, AlsLocomotionSourceProfile sources, AlsP5SourceInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var expected = CompileSourceAware(locomotion, pose, p5a, sources, inventory);
        if (layout.Version != expected.Version || layout.Digest != expected.Digest || layout.SourceStamp != expected.SourceStamp ||
            !layout.Entries.SequenceEqual(expected.Entries) || !layout.SourceMappings.SequenceEqual(expected.SourceMappings) ||
            !layout.SyncMappings.SequenceEqual(expected.SyncMappings) || !layout.UnboundNativeSourceIndices.SequenceEqual(expected.UnboundNativeSourceIndices))
            throw new ArgumentException("Source-aware physical layout differs from its profiles or native coverage.");
    }
}
