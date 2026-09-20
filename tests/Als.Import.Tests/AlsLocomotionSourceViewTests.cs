using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsLocomotionSourceViewTests
{
    [Theory]
    [InlineData(1.5f, true, false)]
    [InlineData(0f, false, true)]
    [InlineData(-1f, true, true)]
    [InlineData(.25f, false, false)]
    public void RotationInputsOverrideNodeDefaultsAndRemainIndependentOfStandingRate(float rate, bool left, bool right)
    {
        var view = Compile().CreateCoreView();
        var players = view.Players.ToArray();
        var rotations = players.Where(p => p.LoopInput != AlsSourceLoopInput.Constant).ToArray();
        var updates = rotations.Select((p, i) => new AlsLocomotionSourceUpdate(p.PlayerId, 1, .5f, .8f, i, 1)).ToArray();
        var samples = rotations.Select(p => new AlsLocomotionSampleUpdate(p.SampleStart, 1, 1)).ToArray();
        foreach (var p in rotations) players[p.PlayerId] = p with { DefaultPlayRate = 7, PlayRateBasis = 2 };
        var changed = new AlsLocomotionSourceView(view.Stamp, players, view.Samples, view.SyncPlayers,
            view.GroupIds, view.Sequences, view.Markers, view.NotifyRanges, view.NotifyDefinitions, view.NotifyPolicies);
        Assert.Equal(4, rotations.Length);
        var output = new AlsAssetSyncPlayer[4]; var sampleOutput = new AlsAssetSyncSample[4]; var groups = new int[4];
        Assert.True(AlsLocomotionSourceRuntime.TryBuildTicks(changed, view.Stamp, updates, samples, 3.75f,
            output, sampleOutput, groups, out var failure, new(rate, left, right)), failure.ToString());
        Assert.All(output, p => Assert.Equal(rate / 2, p.PlayRate));
        Assert.Equal(new[] { left, right, left, right }, output.Select(p => p.Looping));
        Assert.Equal(new[] { -1, -1, -1, -1 }, groups);
        Assert.Equal(rotations.Select(p => p.PlayerId), output.Select(p => p.PlayerId));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("domain")]
    [InlineData("loop-policy")]
    [InlineData("rate-policy")]
    [InlineData("group")]
    public void InvalidLateRotationInputCannotPublishAnEarlierValidTick(string mutation)
    {
        var view = Compile().CreateCoreView(); var players = view.Players.ToArray();
        var rotation = players.Single(p => p.Domain == AlsLocomotionSourceDomain.Standing && p.LoopInput == AlsSourceLoopInput.RotateLeft);
        var ordinary = players.Single(p => p.Domain == AlsLocomotionSourceDomain.Cycle && p.Kind == AlsLocomotionSourceKind.Sequence && p.PlayRateBasis == 1);
        AlsSourceRotationInput? input = new(1, true, false);
        if (mutation == "missing") input = null;
        if (mutation == "nan") input = new(float.NaN, true, false);
        if (mutation == "infinity") input = new(float.PositiveInfinity, true, false);
        if (mutation == "domain") rotation = rotation with { Domain = AlsLocomotionSourceDomain.Cycle };
        if (mutation == "loop-policy") rotation = rotation with { LoopInput = AlsSourceLoopInput.Constant };
        if (mutation == "rate-policy") rotation = rotation with { PlayRateInput = AlsSourceRateInput.Constant };
        if (mutation == "group") rotation = rotation with { SyncGroupId = 0 };
        players[rotation.PlayerId] = rotation;
        var changed = new AlsLocomotionSourceView(view.Stamp, players, view.Samples, view.SyncPlayers,
            view.GroupIds, view.Sequences, view.Markers, view.NotifyRanges, view.NotifyDefinitions, view.NotifyPolicies);
        var output = new AlsAssetSyncPlayer[2]; var sampleOutput = new AlsAssetSyncSample[2]; int[] groups = [91, 92];
        Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(changed, view.Stamp,
            [new(ordinary.PlayerId, 1, .1f, 1, 0, 1), new(rotation.PlayerId, 1, .1f, 1, 1, 1)],
            [new(ordinary.SampleStart, 1, 1), new(rotation.SampleStart, 1, 1)], 1,
            output, sampleOutput, groups, out var failure, input));
        Assert.Equal(mutation is "nan" or "infinity" ? AlsP5FailureCode.NonFiniteInput : AlsP5FailureCode.InvalidBinding, failure);
        Assert.All(output, p => Assert.Equal(default, p)); Assert.All(sampleOutput, s => Assert.Equal(default, s));
        Assert.Equal(new[] { 91, 92 }, groups);
    }

    [Fact]
    public void CoreViewContainsAllCompiledTablesAndTheFullDigest()
    {
        var sources = Compile(); var view = sources.CreateCoreView();
        Assert.True(view.Stamp.IsValid); Assert.Equal(sources.RuntimeStamp, view.Stamp);
        Assert.Equal(sources.SkeletonId, view.Stamp.SkeletonId);
        Assert.Equal(56, view.Players.Length); Assert.Equal(82, view.Samples.Length);
        Assert.True(view.Players.SequenceEqual(sources.RuntimePlayers));
        Assert.True(view.Samples.SequenceEqual(sources.RuntimeSamples));
        Assert.True(view.SyncPlayers.SequenceEqual(sources.RuntimeSyncPlayers));
        Assert.True(view.Sequences.SequenceEqual(sources.SyncSequences));
        Assert.True(view.Markers.SequenceEqual(sources.SyncMarkers));
        Assert.Equal(new[] { 0, 1, 2, 3 }, view.GroupIds.ToArray());
        Span<byte> digest = stackalloc byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(digest, view.Stamp.Digest0);
        BinaryPrimitives.WriteUInt64LittleEndian(digest[8..], view.Stamp.Digest1);
        BinaryPrimitives.WriteUInt64LittleEndian(digest[16..], view.Stamp.Digest2);
        BinaryPrimitives.WriteUInt64LittleEndian(digest[24..], view.Stamp.Digest3);
        Assert.Equal(sources.Digest, Convert.ToHexString(digest).ToLowerInvariant());
        Assert.Equal(P3RepositoryFixtures.LoadAnimationSet().DefinitionDigest, sources.AnimationSetDefinitionDigest);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionSourceStamp>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionSourceUpdate>());
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<AlsLocomotionSampleUpdate>());
    }

    [Fact]
    public void BorrowedViewIsUnaffectedByMutatingPublicDefensiveCopies()
    {
        var sources = Compile(); var view = sources.CreateCoreView();
        var player = view.Players[0]; var sample = view.Samples[0]; var sync = view.SyncPlayers[0];
        var sequence = view.Sequences[0]; var marker = view.Markers[0];
        sources.RuntimePlayers[0] = default; sources.RuntimeSamples[0] = default; sources.RuntimeSyncPlayers[0] = default;
        sources.SyncSequences[0] = default; sources.SyncMarkers[0] = default; sources.SyncGroups[0] = "foreign";
        Assert.Equal(player, view.Players[0]); Assert.Equal(sample, view.Samples[0]); Assert.Equal(sync, view.SyncPlayers[0]);
        Assert.Equal(sequence, view.Sequences[0]); Assert.Equal(marker, view.Markers[0]); Assert.Equal(0, view.GroupIds[0]);
        Assert.Equal(view.Stamp, sources.CreateCoreView().Stamp);
    }

    [Fact]
    public void SourceProvenanceDriftRejectsOldSnapshotIdentity()
    {
        var original = Compile(); var changed = Compile(" "); var view = changed.CreateCoreView();
        Assert.NotEqual(original.RuntimeStamp, changed.RuntimeStamp);
        Assert.True(original.CreateCoreView().Players.SequenceEqual(view.Players));
        Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(view, original.RuntimeStamp, [], [], 1, [], [], [], out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
    }

    [Fact]
    public void StopEvaluatorsRemainInViewButCannotMasqueradeAsTimedPlayers()
    {
        var sources = Compile(); var view = sources.CreateCoreView();
        var output = new AlsAssetSyncPlayer[1]; var sampleOutput = new AlsAssetSyncSample[1]; var groups = new int[1];
        var rejected = 0;
        foreach (var player in view.Players)
        {
            if (player.Kind != AlsLocomotionSourceKind.TeleportEvaluator) continue;
            Assert.False(AlsLocomotionSourceRuntime.TryBuildTicks(view, view.Stamp,
                [new(player.PlayerId, 1, player.StartPosition, 1, 0, 1)], [new(player.SampleStart, 1, 1)], 1,
                output, sampleOutput, groups, out var failure));
            Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
            Assert.Equal(default, output[0]); Assert.Equal(default, sampleOutput[0]);
            rejected++;
        }
        Assert.Equal(18, rejected);
    }

    [Fact]
    public void RepeatedViewCreationAllocatesNothing()
    {
        var sources = Compile();
        for (var i = 0; i < 1000; i++) Consume(sources);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Consume(sources);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Consume(AlsLocomotionSourceProfile source)
    {
        var view = source.CreateCoreView();
        if (view.Stamp != source.RuntimeStamp || view.Players.Length != 56 || view.Samples.Length != 82 ||
            view.SyncPlayers.Length != 56 || view.GroupIds.Length != 4 || view.Sequences.IsEmpty || view.Markers.IsEmpty)
            throw new InvalidOperationException();
    }

    private static AlsLocomotionSourceProfile Compile(string suffix = "")
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var profile = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        return AlsLocomotionSourceCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "v4_locomotion_source_graph.json")) + suffix, set, profile.SkeletonId);
    }
}
