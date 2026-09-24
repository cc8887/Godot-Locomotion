using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredTransitionMontageTests
{
    private sealed class Fixture
    {
        public AlsRefactoredAnimationCatalog Catalog { get; } = new(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        public AlsRefactoredWeaponNotifyProfile[] Profiles { get; }
        public Dictionary<string, int> Ids { get; }
        public AlsRefactoredTransitionMontages Bindings { get; }
        public Fixture()
        {
            Profiles = Enum.GetValues<AlsRefactoredWeaponKind>().Select(k => new AlsRefactoredWeaponNotifyProfile(Catalog,
                new(Catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), Catalog, k)))).ToArray();
            Ids = Profiles.SelectMany(p => p.Bindings.ToArray()).Select(b => b.Sequence).Distinct().Order().Select((p, i) => (p, i))
                .ToDictionary(v => v.p, v => 400 + v.i);
            Bindings = new(Catalog, MantlingHostFixture.Read("refactored_slot_inventory"), Profiles, Ids, 77);
        }
        public AlsMontageRuntime Owner() => new([], sequences: Bindings.Assets.ToArray().Concat(new[]
        {
            new AlsSequenceMontageAsset(900, AlsTurnSlot.Standing, 77, 5, 0),
            new AlsSequenceMontageAsset(901, AlsMontageSlot.Head, 88, 5, 0),
            new AlsSequenceMontageAsset(902, AlsTurnSlot.Crouching, 77, 5, 0)
        }).ToArray());
    }
    private static readonly Lazy<Fixture> Data = new(() => new());
    private static AlsFrameIdentity Id(long frame) => new(frame, 5, 1);

    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)]
    [InlineData(AlsRefactoredWeaponKind.Rifle)]
    public void RealNotifyRequestsCreateSeparateMontagesReplaceGroundedGroupAndPreserveFrozenPose(AlsRefactoredWeaponKind kind)
    {
        var data = Data.Value; var profile = data.Profiles[(int)kind]; var machine = profile.Machine.CreateRuntime(); var notifies = profile.CreateRuntime(5, 1);
        var aim = new AlsRefactoredWeaponRuleInput("Als.RotationMode.Aiming", "", "", false, true);
        machine.Prepare(0, aim, .1f); machine.Commit(0);
        machine.Prepare(1, aim with { RotationMode = "" }, 3); machine.Commit(1);
        machine.Prepare(2, aim, .01f); notifies.Prepare(Id(2), machine, "Als.Stance.Standing", false);
        Assert.Equal(2, notifies.Commands.Length);
        var owner = data.Owner(); owner.Begin(Id(1), .1f);
        Assert.True(owner.PlaySequence(new(900, AlsTurnSlot.Standing, 1, 0, .2f, .7f)));
        Assert.True(owner.PlaySequence(new(901, AlsMontageSlot.Head, 1, 0, .2f, .8f))); owner.Commit(Id(1));
        owner.Begin(Id(2), .3f); var frozen = owner.Evaluation.ToArray();
        data.Bindings.Play(owner, Id(2), notifies.Commands);
        Assert.Equal(frozen, owner.Evaluation.ToArray()); Assert.Equal(4, owner.Candidate.Length);
        var before = owner.Candidate.ToArray();
        Assert.Equal(4, before.Select(v => v.InstanceId).Distinct().Count());
        Assert.Equal(0, before[0].Blend.DesiredWeight); Assert.Equal(1, before[1].Blend.DesiredWeight);
        Assert.Equal(0, before[2].Blend.DesiredWeight); Assert.Equal(1, before[3].Blend.DesiredWeight);
        Assert.Equal(notifies.Commands[0].Binding.PlayRate, before[2].PlayRate);
        Assert.Equal(notifies.Commands[1].Binding.PlayRate, before[3].PlayRate);
        Assert.All(before.Skip(2), v => { Assert.Equal(.3f, v.Position); Assert.Equal(2, v.AdditiveType); Assert.Equal(AlsMontageSlot.Transition, v.Slot); });
        data.Bindings.StopTransitionAndTurnInPlace(owner, Id(2), .05f);
        Assert.Equal(before[0], owner.Candidate[0]); Assert.Equal(before[1], owner.Candidate[1]); Assert.Equal(before[2], owner.Candidate[2]);
        Assert.Equal(.05f, owner.Candidate[3].BlendTime); var stopped = owner.Candidate.ToArray();
        owner.Discard(); owner.Begin(Id(2), .3f);
        data.Bindings.Play(owner, Id(2), notifies.Commands); data.Bindings.StopTransitionAndTurnInPlace(owner, Id(2), .05f);
        Assert.Equal(stopped, owner.Candidate.ToArray());
        owner.ValidateCommit(Id(2)); notifies.ValidateCommit(Id(2)); machine.ValidateCommit(2);
        owner.Commit(Id(2)); notifies.Commit(Id(2)); machine.Commit(2);
        owner.Begin(Id(3), .4f);
        Assert.Single(owner.Candidate.ToArray(), v => v.Blend.DesiredWeight > 0); Assert.Equal(901, owner.Candidate.ToArray().Single(v => v.Blend.DesiredWeight > 0).AnimationId);
        Assert.True(owner.PlaySequence(new(902, AlsTurnSlot.Crouching, 1, 0, .2f, .65f)));
        data.Bindings.StopTransitionAndTurnInPlace(owner, Id(3));
        Assert.Equal(.65f, owner.Candidate[^1].BlendTime);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ContinuousPhysicalPlaybackAndLateDiscardAreDeterministic(int hz)
    {
        var data = Data.Value; var owner = data.Owner(); var binding = data.Profiles[1].Bindings[0];
        for (var frame = 0; frame < hz * 4; frame++)
        {
            var id = Id(frame); owner.Begin(id, 1f / hz);
            void Requests()
            {
                if (frame is 0 or 4 or 9) data.Bindings.Play(owner, id, [new(id, 0, binding), new(id, 1, binding)]);
            }
            Requests(); var first = owner.Candidate.ToArray(); var evaluations = owner.Evaluation.ToArray();
            owner.Discard(); owner.Begin(id, 1f / hz); Requests();
            Assert.Equal(first, owner.Candidate.ToArray()); Assert.Equal(evaluations, owner.Evaluation.ToArray());
            Assert.All(owner.Candidate.ToArray(), v => { Assert.InRange(v.Position, 0, v.Duration); Assert.Equal(77, v.GroupId); });
            owner.Commit(id);
        }
        Assert.Empty(owner.Committed.ToArray());
    }

    [Fact]
    public void InvalidTailBankOrGroupCannotPartiallyPlay()
    {
        var data = Data.Value; var owner = data.Owner(); owner.Begin(Id(1), .1f);
        var binding = data.Profiles[0].Bindings[0];
        Assert.Throws<ArgumentException>(() => data.Bindings.Play(owner, Id(1), [new(Id(1), 0, binding), new(Id(1), 1, binding with { PlayRate = 99 })]));
        Assert.Empty(owner.Candidate.ToArray());
        var wrongBank = new AlsMontageRuntime([], sequences: data.Bindings.Assets.ToArray().Select(a => a with { GroupId = 78 }).ToArray()); wrongBank.Begin(Id(1), .1f);
        Assert.Throws<ArgumentException>(() => data.Bindings.Play(wrongBank, Id(1), [new(Id(1), 0, binding)]));
        Assert.Empty(wrongBank.Candidate.ToArray());
        var root = JsonNode.Parse(MantlingHostFixture.Read("refactored_slot_inventory"))!;
        root["skeletons"]!["/ALS/ALS/Character/SK_Als.SK_Als"]!["groups"]![1]!["slots"]![0] = "Foreign";
        Assert.Throws<ArgumentException>(() => new AlsRefactoredTransitionMontages(data.Catalog, root.ToJsonString(), data.Profiles, data.Ids, 77));
        Assert.Equal(1, data.Bindings.NativeGroupIndex); Assert.Equal(77, data.Bindings.HostGroupId);
        Assert.Throws<ArgumentException>(() => new AlsRefactoredTransitionMontages(data.Catalog, MantlingHostFixture.Read("refactored_slot_inventory"),
            data.Profiles, data.Ids.ToDictionary(p => p.Key, p => 1), 77));
    }
}
