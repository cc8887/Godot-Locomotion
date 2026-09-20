using System.Buffers.Binary;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsP5OccurrenceLayoutCompilerTests
{
    [Fact]
    public void RejectsCycleProfileInsteadOfSilentlyDroppingItsNewSources()
    {
        var (_, pose, p5a) = CompileProfiles();
        var cycle = AlsLocomotionProfileCompiler.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "p4_cycle_locomotion_profile.json")), P3RepositoryFixtures.LoadAnimationSet());
        Assert.Equal(6, cycle.StandingWalkRun.Length);
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Compile(cycle, pose, p5a));
    }

    [Fact]
    public void PhysicalTurnAndRotateBanksHaveDistinctHandlesWithSharedAuthority()
    {
        var (locomotion, pose, p5a) = CompileProfiles();
        var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);
        Assert.Equal(2, layout.Version);
        Assert.Equal(49, layout.Entries.Length);
        foreach (var (kind, count) in new[]
        {
            (AlsP5OccurrenceSourceKind.Turn, 8),
            (AlsP5OccurrenceSourceKind.Rotate, 4),
        })
        {
            for (var binding = 0; binding < count; binding++)
            {
                var copies = layout.Entries.Where(value =>
                    value.SourceKind == kind && value.SourceBindingIndex == binding).ToArray();
                Assert.Equal(2, copies.Length);
                Assert.Equal(new[] { binding, count + binding }, copies.Select(value => value.GraphSlotIndex));
                Assert.NotEqual(copies[0].OccurrenceHandleId, copies[1].OccurrenceHandleId);
                Assert.All(copies, value => Assert.Equal(0, value.AuthorityGroupId));
            }
        }
        Assert.Equal(17, layout.SyncMappings.Length);
        Assert.All(layout.SyncMappings, value => Assert.InRange(value.OccurrenceHandleId, 0, 21));
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(1, layout.Entries));
    }

    [Fact]
    public void AllocatesExactContiguousSourceAndAuthorityOrderWithSyncMappings()
    {
        var (locomotion, pose, p5a) = CompileProfiles();
        var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);

        Assert.Equal(2, layout.Version);
        Assert.NotEqual(0UL, layout.Digest);
        Assert.Equal(49, layout.Entries.Length);
        var expectedEntries = new List<AlsP5OccurrenceLayoutEntry>();
        AddExpectedBank(AlsP5OccurrenceSourceKind.Base, 22, 0);
        AddExpectedBank(AlsP5OccurrenceSourceKind.Turn, 8, 0, 2);
        AddExpectedBank(AlsP5OccurrenceSourceKind.Rotate, 4, 0, 2);
        expectedEntries.Add(new(AlsP5OccurrenceSourceKind.Transition, 0, 0, 46, 1));
        expectedEntries.Add(new(AlsP5OccurrenceSourceKind.ActionMontage, 0, 0, 47, 2));
        expectedEntries.Add(new(AlsP5OccurrenceSourceKind.ActionSequence, 0, 0, 48, 3));
        Assert.Equal(expectedEntries, layout.Entries);
        Assert.Equal(Enumerable.Range(0, layout.Entries.Length),
            layout.Entries.Select(value => value.OccurrenceHandleId));
        Assert.Collection(layout.Entries.GroupBy(value => value.SourceKind),
            group => Assert.Equal((AlsP5OccurrenceSourceKind.Base, 22), (group.Key, group.Count())),
            group => Assert.Equal((AlsP5OccurrenceSourceKind.Turn, 16), (group.Key, group.Count())),
            group => Assert.Equal((AlsP5OccurrenceSourceKind.Rotate, 8), (group.Key, group.Count())),
            group => Assert.Equal((AlsP5OccurrenceSourceKind.Transition, 1), (group.Key, group.Count())),
            group => Assert.Equal((AlsP5OccurrenceSourceKind.ActionMontage, 1), (group.Key, group.Count())),
            group => Assert.Equal((AlsP5OccurrenceSourceKind.ActionSequence, 1), (group.Key, group.Count())));
        Assert.All(layout.Entries.Take(46), value => Assert.Equal(0, value.AuthorityGroupId));
        Assert.Equal(1, layout.Entries[46].AuthorityGroupId);
        Assert.Equal(2, layout.Entries[47].AuthorityGroupId);
        Assert.Equal(3, layout.Entries[48].AuthorityGroupId);
        Assert.Equal(new[] { 0, 1, 2, 3 }, layout.Entries.Select(value => value.AuthorityGroupId).Distinct());
        Assert.Equal(17, layout.SyncMappings.Length);
        var expectedBaseAnimations = BaseAnimationSlots(locomotion);
        Assert.Equal(22, expectedBaseAnimations.Length);
        Assert.All(layout.SyncMappings, mapping =>
        {
            Assert.Equal(0, mapping.SyncGroupId);
            var entry = layout.Entries[mapping.OccurrenceHandleId];
            Assert.Equal(AlsP5OccurrenceSourceKind.Base, entry.SourceKind);
            Assert.Equal(expectedBaseAnimations[entry.SourceBindingIndex], mapping.AnimationId);
            Assert.Equal(p5a.SyncGroups[0].Members.Single(value => value.AnimationId == mapping.AnimationId).GroupMemberIndex,
                mapping.GroupMemberIndex);
        });
        Assert.Equal(p5a.SyncGroups[0].Members.Select(value => value.AnimationId),
            layout.SyncMappings.OrderBy(value => value.GroupMemberIndex).Select(value => value.AnimationId));

        void AddExpectedBank(AlsP5OccurrenceSourceKind kind, int count, int authority, int bankCount = 1)
        {
            for (var index = 0; index < count * bankCount; index++)
            {
                expectedEntries.Add(new(kind, index % count, index, expectedEntries.Count, authority));
            }
        }
    }

    [Fact]
    public void UsesProfileSlotOrderRatherThanAnimationIdentityOrTransientCurrentOutgoingState()
    {
        var (locomotion, pose, p5a) = CompileProfiles();
        var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);
        var baseEntries = layout.Entries.Where(value => value.SourceKind == AlsP5OccurrenceSourceKind.Base).ToArray();

        Assert.Equal(Enumerable.Range(0, 22), baseEntries.Select(value => value.SourceBindingIndex));
        Assert.Equal(Enumerable.Range(0, 22), baseEntries.Select(value => value.GraphSlotIndex));
        var expected = BaseAnimationSlots(locomotion);
        Assert.Equal(new[]
        {
            locomotion.StandingIdleAnimationId,
            locomotion.StandingSamples[0].AnimationId,
            locomotion.CrouchingIdleAnimationId,
            locomotion.JumpStartAnimationId,
            locomotion.FallLoopAnimationId,
            locomotion.LandAnimationId,
        }, new[] { expected[0], expected[1], expected[14], expected[19], expected[20], expected[21] });
        Assert.Equal(Enumerable.Range(0, 16), layout.Entries
            .Where(value => value.SourceKind == AlsP5OccurrenceSourceKind.Turn)
            .Select(value => value.GraphSlotIndex));
        Assert.Equal(Enumerable.Range(0, 8), layout.Entries
            .Where(value => value.SourceKind == AlsP5OccurrenceSourceKind.Rotate)
            .Select(value => value.GraphSlotIndex));
        Assert.Single(layout.Entries, value => value.SourceKind == AlsP5OccurrenceSourceKind.Transition);
    }

    [Fact]
    public void DigestIsExactLittleEndianFnvAndEveryEntryFieldParticipates()
    {
        var (locomotion, pose, p5a) = CompileProfiles();
        var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);
        Assert.Equal(ManualDigest(layout.Version, layout.Entries), layout.Digest);
        Assert.Equal(layout.Digest,
            AlsP5OccurrenceLayoutCompiler.ComputeDigest(layout.Version, layout.Entries));

        var original = layout.Entries;
        for (var field = 0; field < 5; field++)
        {
            var changed = original.ToArray();
            var entry = changed[0];
            changed[0] = field switch
            {
                0 => entry with { SourceKind = AlsP5OccurrenceSourceKind.Turn },
                1 => entry with { SourceBindingIndex = entry.SourceBindingIndex + 1 },
                2 => entry with { GraphSlotIndex = entry.GraphSlotIndex + 1 },
                3 => entry with { OccurrenceHandleId = entry.OccurrenceHandleId + 1 },
                _ => entry with { AuthorityGroupId = entry.AuthorityGroupId + 1 },
            };
            Assert.NotEqual(layout.Digest, ManualDigest(layout.Version, changed));
            Assert.NotEqual(layout.Digest,
                AlsP5OccurrenceLayoutCompiler.ComputeDigest(layout.Version, changed));
        }
    }

    [Fact]
    public void RejectsDuplicateKeysHandlesAndNoncontiguousOrNegativeRanges()
    {
        var valid = new[]
        {
            new AlsP5OccurrenceLayoutEntry(AlsP5OccurrenceSourceKind.Base, 0, 0, 0, 0),
            new AlsP5OccurrenceLayoutEntry(AlsP5OccurrenceSourceKind.Transition, 0, 0, 1, 1),
        };
        AlsP5OccurrenceLayoutCompiler.Validate(2, valid);
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(2,
            [valid[0], valid[0] with { OccurrenceHandleId = 1, AuthorityGroupId = 1 }]));
        AlsP5OccurrenceLayoutCompiler.Validate(2,
            [valid[0], valid[0] with { GraphSlotIndex = 1, OccurrenceHandleId = 1, AuthorityGroupId = 1 }]);
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(2,
            [valid[0], valid[1] with { OccurrenceHandleId = 0 }]));
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(2,
            [valid[0], valid[1] with { OccurrenceHandleId = 2 }]));
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(2,
            [valid[0], valid[1] with { AuthorityGroupId = 2 }]));
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(2,
            [valid[0] with { GraphSlotIndex = -1 }, valid[1]]));
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(2,
            [valid[0] with { OccurrenceHandleId = -1 }, valid[1]]));
        Assert.Throws<ArgumentException>(() => AlsP5OccurrenceLayoutCompiler.Validate(2,
            [valid[0] with { AuthorityGroupId = -1 }, valid[1]]));
    }

    [Fact]
    public void ReturnedArraysAreDefensiveAndRecompileIsByteAndDigestStable()
    {
        var (locomotion, pose, p5a) = CompileProfiles();
        var first = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);
        var entries = first.Entries;
        entries[0] = entries[0] with { OccurrenceHandleId = 999 };
        var mappings = first.SyncMappings;
        mappings[0] = mappings[0] with { OccurrenceHandleId = 999 };
        var second = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);

        Assert.Equal(0, first.Entries[0].OccurrenceHandleId);
        Assert.NotEqual(999, first.SyncMappings[0].OccurrenceHandleId);
        Assert.Equal(first.Entries, second.Entries);
        Assert.Equal(first.SyncMappings, second.SyncMappings);
        Assert.Equal(first.Digest, second.Digest);
    }

    [Fact]
    public void MultipleActionsUseDensePairedAuthoritiesAndOnePhysicalActionGraphSlot()
    {
        var (locomotion, pose, _) = CompileProfiles();
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot.Find(), "assets", "config", "p5a_animation_runtime.json")))!.AsObject();
        var second = root["actions"]![0]!.DeepClone();
        second!["name"] = "RollAlternate";
        root["actions"]!.AsArray().Add(second);
        var p5a = AlsP5aAnimationRuntimeProfileCompiler.Compile(
            root.ToJsonString(), P3RepositoryFixtures.LoadAnimationSet());

        var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);

        Assert.Equal(51, layout.Entries.Length);
        Assert.Equal(Enumerable.Range(0, 6), layout.Entries
            .Select(value => value.AuthorityGroupId).Distinct().Order());
        Assert.All(layout.Entries.Where(value => value.SourceKind is
            AlsP5OccurrenceSourceKind.ActionMontage or AlsP5OccurrenceSourceKind.ActionSequence),
            value => Assert.Equal(0, value.GraphSlotIndex));
        Assert.Equal(new[] { 0, 1 }, layout.Entries
            .Where(value => value.SourceKind == AlsP5OccurrenceSourceKind.ActionMontage)
            .Select(value => value.SourceBindingIndex));
        Assert.Equal(new[] { 0, 1 }, layout.Entries
            .Where(value => value.SourceKind == AlsP5OccurrenceSourceKind.ActionSequence)
            .Select(value => value.SourceBindingIndex));
        Assert.Equal(new[] { 2, 4 }, layout.Entries
            .Where(value => value.SourceKind == AlsP5OccurrenceSourceKind.ActionMontage)
            .Select(value => value.AuthorityGroupId));
        Assert.Equal(new[] { 3, 5 }, layout.Entries
            .Where(value => value.SourceKind == AlsP5OccurrenceSourceKind.ActionSequence)
            .Select(value => value.AuthorityGroupId));
    }

    [Fact]
    public void MultipleSegmentsOfOneActionUseDistinctHandlesAndOneSequenceAuthority()
    {
        var (locomotion, pose, p5a) = CompileProfiles();
        var first = p5a.SegmentBindings[0];
        var expanded = p5a with
        {
            SegmentBindings =
            [
                first,
                first with { SegmentId = 1, MontageStartTime = first.MontageEndTime },
            ],
        };

        var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, expanded);
        var sequenceEntries = layout.Entries.Where(value =>
            value.SourceKind == AlsP5OccurrenceSourceKind.ActionSequence).ToArray();

        Assert.Equal(2, sequenceEntries.Length);
        Assert.Equal(new[] { 48, 49 }, sequenceEntries.Select(value => value.OccurrenceHandleId));
        Assert.Equal(new[] { 0, 1 }, sequenceEntries.Select(value => value.SourceBindingIndex));
        Assert.All(sequenceEntries, value => Assert.Equal(3, value.AuthorityGroupId));
    }

    private static (AlsLocomotionAnimationProfile, AlsPoseAnimationProfile, AlsP5aAnimationRuntimeProfile)
        CompileProfiles()
    {
        var root = RepositoryRoot.Find();
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        var pose = AlsPoseProfileCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "assets", "config", "p4_pose_profile.json")),
            set, locomotion);
        var p5a = AlsP5aAnimationRuntimeProfileCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "assets", "config", "p5a_animation_runtime.json")), set);
        return (locomotion, pose, p5a);
    }

    private static ulong ManualDigest(int version, IReadOnlyList<AlsP5OccurrenceLayoutEntry> entries)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        Add(version);
        Add(entries.Count);
        foreach (var entry in entries)
        {
            AddByte((byte)entry.SourceKind);
            Add(entry.SourceBindingIndex);
            Add(entry.GraphSlotIndex);
            Add(entry.OccurrenceHandleId);
            Add(entry.AuthorityGroupId);
        }
        return hash;

        void Add(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            foreach (var item in bytes)
            {
                hash ^= item;
                hash *= prime;
            }
        }

        void AddByte(byte value)
        {
            hash ^= value;
            hash *= prime;
        }
    }

    private static int[] BaseAnimationSlots(AlsLocomotionAnimationProfile locomotion) =>
    [
        locomotion.StandingIdleAnimationId,
        .. locomotion.StandingSamples.Select(value => value.AnimationId),
        locomotion.CrouchingIdleAnimationId,
        .. locomotion.CrouchingSamples.Select(value => value.AnimationId),
        locomotion.JumpStartAnimationId,
        locomotion.FallLoopAnimationId,
        locomotion.LandAnimationId,
    ];
}
