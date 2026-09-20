using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsOverlayClockTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsOverlaySourceProfile> Sources = new(() => AlsOverlaySourceCompiler.Compile(
        Read("v4_layering_inputs.json"), Read("v4_overlay_inputs.json"), Set.Value));
    private static readonly Lazy<AlsOverlayClockDefinition> Definition = new(() => Compile());
    private static string Read(string name) => AlsAimPoseCompilerTests.Read(name);
    private static AlsOverlayClockDefinition Compile(string? json = null) => AlsOverlaySyncCompiler.Compile(
        json ?? Read("v4_overlay_sync_inputs.json"), Read("v4_overlay_inputs.json"), Sources.Value, Set.Value);

    [Fact]
    public void NativeMetadataRetainsAllOccurrencesAndSharedMarkerSymbols()
    {
        var d = Definition.Value; Assert.Equal(148, d.Sources.Length); Assert.Equal(29, d.Sequences.Length);
        Assert.Equal(6, d.Markers.Length); Assert.Equal(26, d.Sources.ToArray().Count(p => !p.Evaluator));
        Assert.Equal(3, d.Sources.ToArray().Count(p => p.Role == AlsAssetSyncRole.AlwaysFollower));
        var names = Set.Value.Animations.SelectMany(a => a.SyncMarkers).Select(m => m.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var sequence in d.Sequences)
        {
            var imported = Set.Value.Animations[sequence.AnimationId];
            for (var i = 0; i < sequence.MarkerCount; i++)
                Assert.Equal(Array.IndexOf(names, imported.SyncMarkers[i].Name) + 1, d.Markers[sequence.MarkerStart + i].Symbol);
        }
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("missing")]
    [InlineData("length")]
    [InlineData("marker")]
    public void RejectsStaleTimingOrMarkerMetadata(string change)
    {
        var json = JsonNode.Parse(Read("v4_overlay_sync_inputs.json"))!;
        if (change == "digest") json["overlaySha256"] = "foreign";
        if (change == "missing") json["assets"]!.AsArray().RemoveAt(0);
        if (change == "length") json["assets"]![0]!["length"] = 42;
        if (change == "marker") json["assets"]!.AsArray().First(a => a!["markers"]!.AsArray().Count > 0)!["markers"]![0]!["time"] = .12345f;
        Assert.Throws<ArgumentException>(() => Compile(json.ToJsonString()));
    }

    [Fact]
    public void CandidateCancellationRestoresClocksAndInitializationAndRejectsForeignFrames()
    {
        var owner = new AlsOverlaySourceClock(Definition.Value, 7, 2);
        var source = Definition.Value.Sources.ToArray().First(p => !p.Evaluator && p.PlayRate != 0).Source;
        owner.Begin(new(1, 7, 2), 1, .1f, 0); owner.Initialize(source, 1);
        owner.Update(new(source, 1, 1, false, false)); owner.TickStandalone(); var expected = owner.Times[source];
        Assert.NotEqual(0, expected); owner.Cancel(); Assert.Equal(0, owner.Times[source]);
        owner.Begin(new(1, 7, 2), 1, .1f, 0); owner.Initialize(source, 1);
        owner.Update(new(source, 1, 1, false, false)); owner.TickStandalone(); Assert.Equal(expected, owner.Times[source]); owner.Commit();
        Assert.Throws<ArgumentException>(() => owner.Begin(new(2, 8, 2), 2, .1f, 0));
        Assert.Throws<ArgumentException>(() => owner.Begin(new(1, 7, 2), 1, .1f, 0));
        owner.Begin(new(2, 7, 2), 2, .1f, 0); owner.Initialize(source, 2);
        owner.Update(new(source, 2, 1, false, false)); owner.TickStandalone(); owner.Cancel(); Assert.Equal(expected, owner.Times[source]);
        owner.Begin(new(2, 7, 2), 2, .1f, 0); owner.Update(new(source, 1, 1, false, false)); owner.TickStandalone();
        Assert.True(owner.Times[source] > expected); owner.Commit();
    }

    [Fact]
    public void TeleportEvaluatorsClampExplicitPinsAndNeverEnterSyncOrNotifyTraversal()
    {
        var owner = new AlsOverlaySourceClock(Definition.Value, 7, 2);
        var source = Definition.Value.Sources.ToArray().First(p => p.AimSweep);
        owner.Begin(new(1, 7, 2), 1, 10, 100); owner.Initialize(source.Source, 1);
        owner.Update(new(source.Source, 1, 0, true, true)); owner.TickStandalone();
        Assert.Equal(Definition.Value.Sequences[source.SequenceIndex].DurationSeconds, owner.Times[source.Source]);
        Assert.Equal(0, owner.CollectedPlayers.Length); Assert.Equal(0, owner.TickContexts.Length); owner.Commit();
        owner.Begin(new(2, 7, 2), 2, 10, -1); owner.Update(new(source.Source, 1, 1, false, false)); owner.TickStandalone();
        Assert.Equal(0, owner.Times[source.Source]); owner.Commit();
    }

    [Fact]
    public void ZeroWeightInactivePlayersStillTickAndRetainNotifyContextForTheFrameOwner()
    {
        var owner = new AlsOverlaySourceClock(Definition.Value, 7, 2);
        var source = Definition.Value.Sources.ToArray().First(p => !p.Evaluator && p.PlayRate != 0).Source;
        owner.Begin(new(1, 7, 2), 1, .1f, 0); owner.Initialize(source, 1);
        owner.Update(new(source, 1, 0, true, true)); owner.TickStandalone();
        Assert.NotEqual(0, owner.Times[source]); Assert.True(owner.PlayerUpdates[0].Inactive);
        Assert.True(owner.CollectedPlayers[0].RequestedInertialization); Assert.Equal(0, owner.CollectedPlayers[0].Weight); owner.Commit();
    }

    [Fact]
    public void FourIndependentOwnersAndTenThousandHotTransactionsHaveNoAllocations()
    {
        var d = Definition.Value; var ids = d.Sources.ToArray().Where(p => !p.Evaluator).Select(p => p.Source).ToArray();
        var owners = Enumerable.Range(0, 4).Select(i => new AlsOverlaySourceClock(d, (uint)i, 2)).ToArray();
        Parallel.For(0, 4, i => { for (var serial = 1; serial <= 200; serial++) Tick(owners[i], (uint)i, serial); });
        for (var i = 1; i < 4; i++) Assert.True(owners[0].Times.SequenceEqual(owners[i].Times));
        // Parallel.For need not execute on this measuring thread. Warm this
        // thread too, as the existing pose-owner allocation checks do.
        for (var serial = 201; serial <= 700; serial++) Tick(owners[0], 0, serial);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var serial = 701; serial <= 10700; serial++) Tick(owners[0], 0, serial);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Tick(AlsOverlaySourceClock owner, uint character, int serial)
        {
            owner.Begin(new(serial, character, 2), serial, 1f / 60, .5);
            foreach (var id in ids)
            { if (serial == 1) owner.Initialize(id, 1); owner.Update(new(id, 1, 1, false, false)); }
            owner.TickStandalone(); owner.Commit();
        }
    }
}
