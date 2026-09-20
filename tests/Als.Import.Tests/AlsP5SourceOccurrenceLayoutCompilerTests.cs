using GodotAls.Core.Contracts;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using Kind = GodotAls.Import.Compilation.AlsP5OccurrenceSourceKind;

namespace GodotAls.Import.Tests;

public sealed class AlsP5SourceOccurrenceLayoutCompilerTests
{
    [Fact]
    public void AllocatesCurrentPhysicalCompositionWithoutEquatingInventoryCountsToSlots()
    {
        var f = Fixture.Create(); var layout = f.Compile(); var entries = layout.Entries;
        var expectedCount = 2 + f.Locomotion.CrouchingSamples.Length + 3 + f.Sources.RuntimeSamples.Length +
            2 * f.Pose.Turns.Length + 2 * f.Pose.Rotates.Length + 1 + f.P5.Actions.Length + f.P5.SegmentBindings.Length;
        Assert.Equal(3, layout.Version); Assert.Equal(expectedCount, entries.Length); Assert.Equal(118, entries.Length);
        Assert.Equal(Enumerable.Range(0, entries.Length), entries.Select(e => e.OccurrenceHandleId));
        Assert.Equal(9, entries.Count(e => e.SourceKind == Kind.Base));
        Assert.Equal(new[] { 0, 14, 15, 16, 17, 18, 19, 20, 21 }, entries.Where(e => e.SourceKind == Kind.Base).Select(e => e.SourceBindingIndex));
        Assert.Equal(Enumerable.Range(0, 9), entries.Where(e => e.SourceKind == Kind.Base).Select(e => e.GraphSlotIndex));
        Assert.Equal(64, entries.Count(e => e.SourceKind == Kind.SourceSample));
        Assert.Equal(18, entries.Count(e => e.SourceKind == Kind.SourceEvaluator));
        Assert.Equal(16, entries.Count(e => e.SourceKind == Kind.Turn)); Assert.Equal(8, entries.Count(e => e.SourceKind == Kind.Rotate));
        Assert.Equal(82, layout.SourceMappings.Length); Assert.Equal(177, layout.UnboundNativeSourceIndices.Length);
        Assert.Equal(f.Inventory.Nodes.Where(n => n.Kind == AlsP5InventoryNodeKind.AssetPlayer && n.LocomotionPlayerId < 0)
            .Select(n => n.CompiledNodeIndex).Order(), layout.UnboundNativeSourceIndices);
        Assert.Equal(4, layout.SyncMappings.Length);
        Assert.All(layout.SyncMappings, m => Assert.Equal(Kind.Base, entries[m.OccurrenceHandleId].SourceKind));
        var sourceView = layout.CreateSourceView();
        AlsP5SourceOccurrenceContract.Validate(sourceView, f.Sources.CreateCoreView());
        f.Validate(layout);
    }

    [Fact]
    public void SharedAnimationAndSyncGroupDoNotCollapseSourceOwnership()
    {
        var f = Fixture.Create(); var layout = f.Compile(); var entries = layout.Entries;
        var source = f.Sources.CreateCoreView();
        foreach (var map in layout.SourceMappings)
        {
            var sample = source.Samples[map.SampleId]; var player = source.Players[map.PlayerId]; var entry = entries[map.OccurrenceHandleId];
            Assert.Equal(player.CompiledNodeIndex, entry.SourceBindingIndex);
            Assert.Equal(sample.SourceIndex, entry.GraphSlotIndex); Assert.Equal(sample.AnimationId, map.AnimationId);
            Assert.NotEqual(0, entry.AuthorityGroupId);
        }
        var mappedPlayers = layout.SourceMappings.GroupBy(m => m.PlayerId).ToArray();
        Assert.Equal(56, mappedPlayers.Length);
        Assert.All(mappedPlayers, group => Assert.Single(group.Select(m => entries[m.OccurrenceHandleId].AuthorityGroupId).Distinct()));
        Assert.Equal(56, mappedPlayers.Select(group => entries[group.First().OccurrenceHandleId].AuthorityGroupId).Distinct().Count());
        var duplicateAsset = layout.SourceMappings.GroupBy(m => m.AnimationId).First(group => group.Select(m => m.PlayerId).Distinct().Count() >= 3).ToArray();
        Assert.Equal(duplicateAsset.Length, duplicateAsset.Select(m => m.OccurrenceHandleId).Distinct().Count());
        Assert.Equal(duplicateAsset.Length, duplicateAsset.Select(m => entries[m.OccurrenceHandleId].AuthorityGroupId).Distinct().Count());
        foreach (var kind in new[] { Kind.Turn, Kind.Rotate })
        foreach (var banks in entries.Where(e => e.SourceKind == kind).GroupBy(e => e.SourceBindingIndex))
        {
            Assert.Equal(2, banks.Count()); Assert.Equal(2, banks.Select(e => e.OccurrenceHandleId).Distinct().Count());
            Assert.All(banks, entry => Assert.Equal(0, entry.AuthorityGroupId));
        }
    }

    [Fact]
    public void CacheReadsNeverAllocateSourceOccurrencesAndUnboundCoverageRemainsExplicit()
    {
        var f = Fixture.Create(); var layout = f.Compile();
        var cacheIds = f.Inventory.Nodes.Where(n => n.Kind is AlsP5InventoryNodeKind.CacheRead or AlsP5InventoryNodeKind.CacheWrite)
            .Select(n => n.CompiledNodeIndex).ToHashSet();
        Assert.DoesNotContain(layout.Entries.Where(e => e.SourceKind is Kind.SourceSample or Kind.SourceEvaluator), e => cacheIds.Contains(e.SourceBindingIndex));
        var bound = layout.SourceMappings.Select(m => m.CompiledNodeIndex).Distinct().ToHashSet();
        Assert.DoesNotContain(layout.UnboundNativeSourceIndices, bound.Contains);
        Assert.Equal(f.Inventory.Nodes.Count(n => n.Kind == AlsP5InventoryNodeKind.AssetPlayer), bound.Count + layout.UnboundNativeSourceIndices.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ExactSourceValidationRejectsEveryLayoutAndCoverageDrift(int mutation)
    {
        var f = Fixture.Create(); var layout = f.Compile(); var entries = layout.Entries; var maps = layout.SourceMappings;
        switch (mutation)
        {
            case 0: layout = layout with { Version = 2 }; break;
            case 1: layout = layout with { Digest = layout.Digest ^ 1 }; break;
            case 2: layout = layout with { SourceStamp = layout.SourceStamp with { Digest3 = layout.SourceStamp.Digest3 ^ 1 } }; break;
            case 3: entries[9] = entries[9] with { GraphSlotIndex = 55 }; layout = layout with { Entries = entries }; break;
            case 4: maps[0] = maps[0] with { PlayerId = 1 }; layout = layout with { SourceMappings = maps }; break;
            case 5: maps[0] = maps[0] with { AnimationId = 999 }; layout = layout with { SourceMappings = maps }; break;
            case 6: layout = layout with { SourceMappings = maps[..^1] }; break;
            case 7: layout = layout with { SyncMappings = [] }; break;
            case 8: layout = layout with { UnboundNativeSourceIndices = [] }; break;
        }
        Assert.Throws<ArgumentException>(() => f.Validate(layout));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CoreMappingValidationRejectsAliasingEvenWithRecomputedEntryDigest(int mutation)
    {
        var f = Fixture.Create(); var layout = f.Compile(); var entries = layout.Entries; var maps = layout.SourceMappings;
        var first = maps[0]; var secondPlayer = maps.First(m => m.PlayerId != first.PlayerId);
        if (mutation == 0) maps[0] = first with { OccurrenceHandleId = maps[1].OccurrenceHandleId };
        if (mutation == 1) entries[first.OccurrenceHandleId] = entries[first.OccurrenceHandleId] with { AuthorityGroupId = entries[secondPlayer.OccurrenceHandleId].AuthorityGroupId };
        if (mutation == 2) entries[first.OccurrenceHandleId] = entries[first.OccurrenceHandleId] with { AuthorityGroupId = 0 };
        if (mutation == 3) entries[first.OccurrenceHandleId] = entries[first.OccurrenceHandleId] with { SourceKind = Kind.SourceEvaluator };
        layout = layout with { Entries = entries, SourceMappings = maps, Digest = AlsP5OccurrenceLayoutCompiler.ComputeDigest(3, entries) };
        var rejected = false;
        try { AlsP5SourceOccurrenceContract.Validate(layout.CreateSourceView(), f.Sources.CreateCoreView()); }
        catch (ArgumentException) { rejected = true; }
        Assert.True(rejected);
    }

    [Fact]
    public void ViewsRemainImmutableAndRecordCopiesRefreshTheirCoreEntryTable()
    {
        var f = Fixture.Create(); var layout = f.Compile(); var original = layout.CreateCoreView();
        var entries = layout.Entries; var maps = layout.SourceMappings; var unbound = layout.UnboundNativeSourceIndices;
        entries[0] = entries[0] with { GraphSlotIndex = 88 }; maps[0] = default; unbound[0] = -1;
        Assert.NotEqual(entries[0].GraphSlotIndex, original.Entries[0].GraphSlotIndex);
        Assert.NotEqual(default, layout.SourceMappings[0]); Assert.True(layout.UnboundNativeSourceIndices[0] >= 0);
        var changed = layout with { Entries = entries };
        Assert.Equal(88, changed.CreateCoreView().Entries[0].GraphSlotIndex);
        Assert.NotEqual(88, layout.CreateCoreView().Entries[0].GraphSlotIndex);
        var stamp = layout.SourceStamp;
        for (var i = 0; i < 1000; i++) Consume(layout, stamp);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Consume(layout, stamp);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void LegacyAllocatorAndRuntimeCompilerCannotSilentlyConsumeNewLayout()
    {
        var f = Fixture.Create(); var layout = f.Compile();
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Compile(f.Locomotion, f.Pose, f.P5));
        Assert.Throws<ArgumentException>(() => AlsP5CoreRuntimeBindingCompiler.Compile(
            P3RepositoryFixtures.LoadAnimationSet(), f.Locomotion, f.Pose, f.P5, layout));
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(2, layout.Entries));
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutContract.Validate(2, layout.Digest, layout.CreateCoreView().Entries));
    }

    [Fact]
    public void RejectsInventoryFromAnotherSourceSnapshot()
    {
        var f = Fixture.Create(); var set = P3RepositoryFixtures.LoadAnimationSet();
        var changed = AlsLocomotionSourceCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "v4_locomotion_source_graph.json")) + " ", set, f.Locomotion.SkeletonId);
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.CompileSourceAware(f.Locomotion, f.Pose, f.P5, changed, f.Inventory));
    }

    [Fact]
    public void RejectsStandingClipReplacementEvenWithoutLegacySyncMembership()
    {
        var f = Fixture.Create(); var samples = f.Locomotion.StandingSamples.ToArray();
        samples[0] = samples[0] with { AnimationId = f.Locomotion.StandingIdleAnimationId };
        var changed = f.Locomotion with { StandingSamples = samples };
        var noLegacySync = f.P5 with { SyncGroups = [] };
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.CompileSourceAware(
            changed, f.Pose, noLegacySync, f.Sources, f.Inventory));
    }

    [Fact]
    public void AdditionalActionSegmentUsesAllocatorWithoutChangingSourceMapping()
    {
        var f = Fixture.Create(); var original = f.Compile(); var first = f.P5.SegmentBindings[0];
        var actions = f.P5 with { SegmentBindings = [first, first with { SegmentId = 1, MontageStartTime = first.MontageEndTime }] };
        var expanded = AlsP5OccurrenceLayoutCompiler.CompileSourceAware(f.Locomotion, f.Pose, actions, f.Sources, f.Inventory);
        Assert.Equal(original.Entries.Length + 1, expanded.Entries.Length);
        Assert.Equal(original.SourceMappings, expanded.SourceMappings);
        var segments = expanded.Entries.Where(e => e.SourceKind == Kind.ActionSequence).ToArray();
        Assert.Equal(2, segments.Length); Assert.Equal(segments[0].AuthorityGroupId, segments[1].AuthorityGroupId);
        Assert.NotEqual(segments[0].OccurrenceHandleId, segments[1].OccurrenceHandleId);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining |
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static void Consume(AlsP5OccurrenceLayout layout, AlsLocomotionSourceStamp expected)
    {
        var view = layout.CreateSourceView(); var core = layout.CreateCoreView();
        if (view.SourceStamp != expected || view.Digest != core.Digest || view.Mappings.Length != 82 || view.Entries.Length != core.Entries.Length)
            throw new InvalidOperationException();
    }

    private sealed record Fixture(AlsLocomotionAnimationProfile Locomotion, AlsPoseAnimationProfile Pose,
        AlsP5aAnimationRuntimeProfile P5, AlsLocomotionSourceProfile Sources, AlsP5SourceInventory Inventory)
    {
        public static Fixture Create()
        {
            var set = P3RepositoryFixtures.LoadAnimationSet(); var root = Path.Combine(RepositoryRoot.Find(), "assets", "config");
            var locomotion = AlsLocomotionProfileCompiler.Compile(File.ReadAllText(Path.Combine(root, "p4_cycle_locomotion_profile.json")), set);
            var pose = AlsPoseProfileCompiler.Compile(File.ReadAllText(Path.Combine(root, "p4_pose_profile.json")), set, locomotion);
            var p5 = AlsP5aAnimationRuntimeProfileCompiler.Compile(File.ReadAllText(Path.Combine(root, "p5a_animation_runtime.json")), set);
            var sources = AlsLocomotionSourceCompiler.Compile(File.ReadAllText(Path.Combine(root, "v4_locomotion_source_graph.json")), set, locomotion.SkeletonId);
            var inventory = AlsP5SourceInventoryCompiler.Compile(File.ReadAllText(Path.Combine(root, "v4_anim_graph_inventory.json")), set, sources);
            return new(locomotion, pose, p5, sources, inventory);
        }
        public AlsP5OccurrenceLayout Compile() => AlsP5OccurrenceLayoutCompiler.CompileSourceAware(Locomotion, Pose, P5, Sources, Inventory);
        public void Validate(AlsP5OccurrenceLayout layout) => AlsP5OccurrenceLayoutCompiler.ValidateSourceAware(layout, Locomotion, Pose, P5, Sources, Inventory);
    }
}
