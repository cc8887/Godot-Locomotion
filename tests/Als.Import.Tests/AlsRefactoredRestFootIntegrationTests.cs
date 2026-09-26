using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredRestFootIntegrationTests
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void BothStancesConsumeFeetThroughSharedQueueAndCancelPlaybackTogether(int hz)
    {
        var p = AlsRefactoredLocomotionHostTests.Data.Value;
        var owner = p.CreateRuntime(4, 1); var clean = p.CreateRuntime(4, 1);
        var counter = new AlsGraphTraversalCounter(0, 0);
        var played = new HashSet<string>(); var requests = 0;
        for (var f = 0; f < hz * 4; f++)
        {
            var id = new AlsFrameIdentity(f, 4, 1);
            var context = new AlsPoseUpdateContext(id, 1, 1f / hz).WithUpdateCounter(counter);
            var crouch = f >= hz * 2; var right = f / hz % 2 == 1;
            var input = AlsRefactoredStandingHostTests.Input(0, hz);
            input = input with { Rest = input.Rest with { Stance = crouch ? AlsRefactoredRestStance.Crouching : AlsRefactoredRestStance.Standing },
                QuickStop = input.QuickStop with { Crouching = crouch } };
            var feet = new AlsFootTransitionFeedback(id, f == 0 ? default : new(f - 1, 4, 1), 1,
                right ? 0 : 1, right ? 1 : 0, new(20, 0, 0), default, new(20, 0, 0), default);
            void Prepare(AlsRefactoredLocomotionHost h)
            {
                h.BeginGlobal(context, new(input, AlsRefactoredLocomotionMode.Grounded, false, false, false, 0,
                    crouch ? 0 : 1, crouch ? 1 : 0), new(0, 0), f == 0);
                h.Actions.UpdateFeet(feet); h.Prepare(context, f == 0);
                h.Evaluate(AlsPrecisePose.Identity); h.PostUpdate();
                Assert.Throws<InvalidOperationException>(() => h.Actions.UpdateFeet(feet));
            }
            Prepare(owner); var pose = owner.Pose.ToArray(); var actions = owner.Actions.Candidate.ToArray();
            var count = owner.Actions.DynamicRequestCount; var queue = owner.Actions.QueueState;
            owner.Discard(); Prepare(owner); Prepare(clean);
            Assert.Equal(count, owner.Actions.DynamicRequestCount); Assert.Equal(queue, owner.Actions.QueueState);
            Assert.Equal(pose, owner.Pose.ToArray()); Assert.Equal(actions, owner.Actions.Candidate.ToArray());
            Assert.Equal(clean.Pose.ToArray(), pose); Assert.Equal(clean.Actions.Candidate.ToArray(), actions);
            requests += count;
            foreach (var action in actions) played.Add(p.Actions.SourcePath(action.AnimationId));
            owner.Commit(id); clean.Commit(id); counter = counter.Next((ulong)f + 1);
        }
        Assert.True(requests > 4);
        foreach (var crouch in new[] { false, true }) foreach (var left in new[] { false, true })
            Assert.Contains(p.Actions.Standing.Montages.Settings.DynamicSequence(crouch, left), played);
    }
}
