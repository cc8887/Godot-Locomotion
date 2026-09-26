using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredSourceHostTests
{
    [Fact]
    public void LocalViewsCannotTickOrPublishTheSharedBank()
    {
        var profile = AlsRefactoredLocomotionHostTests.Data.Value;
        var scope = profile.CreateSourceScope(19, 1);
        var view = scope.CreateView(AlsRefactoredLocomotionSourceOwner.StandingMovement);
        Assert.Throws<ArgumentException>(() => scope.CreateView(AlsRefactoredLocomotionSourceOwner.StandingMovement));
        var context = new AlsPoseUpdateContext(new(0, 19, 1), 1, 1f / 60).WithUpdateCounter(new(0, 0));
        var input = new AlsRefactoredSourcePlayerInput(0, default, 1, 1);
        scope.Begin(context); view.BeginRegistration(true); view.Register(input, context);
        Assert.Throws<ArgumentException>(() => view.Prepare(0, [], context.Delta));
        Assert.Throws<ArgumentException>(() => view.Prepare(0, [input with { PlayRate = 2 }], context.Delta));
        view.Prepare(0, [input], context.Delta);
        Assert.Throws<InvalidOperationException>(() => view.Evaluate(0, 0));
        Assert.Throws<InvalidOperationException>(() => view.Commit(0));
        scope.Complete(); view.Evaluate(0, 0);
        Assert.Equal(scope.Players[0].Time, view.Players[0].Time);
        Assert.Equal(scope.Pose(1, 0).ToArray(), view.Pose(0).ToArray());
        view.Commit(0);
        Assert.Equal(default, scope.CommittedIdentity);
        Assert.Throws<ArgumentException>(() => view.Evaluate(0, 0));
        scope.Discard(); scope.Begin(context); view.BeginRegistration(true); view.Register(input, context);
        view.Prepare(0, [input], context.Delta); scope.Complete();
        Assert.Equal(1, view.Players[0].Epoch); view.Commit(0); scope.Commit(context.Identity);
        Assert.Equal(context.Identity, scope.CommittedIdentity);
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void ProductionHostSharesStanceGroupAndRollsBackAllSourceClocks(int hz)
    {
        var profile = AlsRefactoredLocomotionHostTests.Data.Value;
        var host = profile.CreateRuntime(19, 1); var clean = profile.CreateRuntime(19, 1);
        var counter = new AlsGraphTraversalCounter(0, 0);
        var overlap = 0; var airFrames = 0; var hiddenFrames = 0; var directBeforeCached = 0;
        var leaders = new HashSet<uint>(); var previousStanding = 1f; var previousCrouching = 0f;
        for (var f = 0; f < hz * 8; f++)
        {
            var t = f / (float)hz; var crouch = (int)t % 2 == 1;
            var air = t is >= 4 and < 5.5f; var hidden = t is >= 6 and < 6.2f;
            var ground = AlsRefactoredStandingHostTests.Input(hz * 2, hz);
            ground = ground with
            {
                Movement = ground.Movement with { PendingUpdate = f == 0, Velocity = new(170, 100, air ? -350 : 0) },
                Rest = ground.Rest with { PendingUpdate = f == 0, Stance = crouch ? AlsRefactoredRestStance.Crouching : AlsRefactoredRestStance.Standing },
                QuickStop = ground.QuickStop with { Crouching = crouch }
            };
            var input = new AlsRefactoredLocomotionHostInput(ground,
                air ? AlsRefactoredLocomotionMode.InAir : AlsRefactoredLocomotionMode.Grounded,
                f == hz * 4, true, false, 0, previousStanding, previousCrouching);
            var id = new AlsFrameIdentity(f, 19, 1);
            var context = new AlsPoseUpdateContext(id, 1, 1f / hz).WithUpdateCounter(counter);
            void Prepare(AlsRefactoredLocomotionHost h)
            {
                h.BeginGlobal(context, input, new(0, 0), f == 0);
                if (!hidden) { h.Prepare(context, f == 0); h.Evaluate(AlsPrecisePose.Identity); }
                h.PostUpdate();
            }
            Prepare(host);
            var players = host.Sources.Players.ToArray(); var samples = host.Sources.Samples.ToArray();
            var ticks = host.Sources.Ticks.ToArray(); var groups = host.Sources.Groups.ToArray();
            var order = host.Sources.TickContexts.ToArray(); var paths = host.Sources.RegistrationContexts.ToArray();
            Assert.Equal(ticks.Length, paths.Length);
            for (var i = 0; i < ticks.Length; i++)
            {
                Assert.Equal(ticks[i].Weight, paths[i].Weight);
                Assert.Equal(ticks[i].RequestedInertialization, paths[i].InertializationSync);
                Assert.Equal(id, paths[i].Identity);
            }
            var movement = groups.SingleOrDefault(g => host.Sources.GroupNames[g.Group.GroupId] == "Movement");
            if (movement.PlayerCount > 0)
            {
                var owners = players.AsSpan(movement.PlayerStart, movement.PlayerCount).ToArray()
                    .Select(p => host.Sources.OwnerPlayer(p.PlayerId).Owner).Distinct().ToArray();
                if (owners.Length == 2) { overlap++; Assert.Contains(1u, owners); Assert.Contains(3u, owners); }
                leaders.Add(host.Sources.OwnerPlayer(movement.Group.LeaderPlayerId).Owner);
            }
            if (ticks.Any(p => host.Sources.OwnerPlayer(p.PlayerId).Owner == 6)) airFrames++;
            if (hidden) { hiddenFrames++; Assert.Empty(players); }
            var firstDirect = Array.FindIndex(ticks, p => host.Sources.OwnerPlayer(p.PlayerId).Owner == 6);
            var firstCached = Array.FindIndex(ticks, p => host.Sources.OwnerPlayer(p.PlayerId).Owner is 1 or 3);
            if (firstDirect >= 0 && firstCached >= 0) { Assert.True(firstDirect < firstCached); directBeforeCached++; }
            var pose = hidden ? [] : host.Pose.ToArray(); var curves = hidden ? [] : host.Curves.ToArray();
            host.Discard(); Prepare(host); Prepare(clean);
            Assert.Equal(players, host.Sources.Players.ToArray()); Assert.Equal(players, clean.Sources.Players.ToArray());
            Assert.Equal(samples, host.Sources.Samples.ToArray()); Assert.Equal(groups, host.Sources.Groups.ToArray());
            Assert.Equal(order, host.Sources.TickContexts.ToArray()); Assert.Equal(paths, host.Sources.RegistrationContexts.ToArray());
            if (!hidden)
            {
                Assert.Equal(pose, host.Pose.ToArray()); Assert.Equal(pose, clean.Pose.ToArray());
                Assert.Equal(curves, host.Curves.ToArray());
                previousStanding = curves[profile.Pose.CurveNames.IndexOf("PoseStanding")].Value;
                previousCrouching = curves[profile.Pose.CurveNames.IndexOf("PoseCrouching")].Value;
            }
            host.Commit(id); clean.Commit(id); Assert.Equal(id, host.Sources.CommittedIdentity);
            counter = counter.Next((ulong)f + 1);
        }
        Assert.True(overlap > 0, "No shared Standing/Crouching Movement membership.");
        Assert.Equal(new uint[] { 1, 3 }, leaders.Order());
        Assert.True(airFrames > 0 && hiddenFrames > 0 && directBeforeCached > 0);
    }
}
