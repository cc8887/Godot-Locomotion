using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using System.Text.Json.Nodes;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredDirectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalDirectionRulesPreserveCurveAndStateWeightGates(bool crouching)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var profile = new AlsRefactoredDirectionResources(MantlingHostFixture.Read("refactored_stance_machines"), catalog, crouching);
        Assert.Equal(6, profile.States.Length); Assert.Equal(24, profile.Edges.Length);
        Assert.Equal(8, profile.Edges.ToArray().Select(e => e.RulePropertyIndex).Distinct().Count());
        Assert.Equal(crouching ? 0 : 6, profile.Edges.ToArray().Count(e => e.StartNotify == 0));
        Assert.Equal(crouching ? 0 : 7, profile.Edges.ToArray().Count(e => e.Blend == AlsTransitionBlend.QuadraticInOut));
        var rules = profile.Edges.ToArray().Select(e => e.Rule).Distinct().ToArray(); Assert.Equal(8, rules.Length);
        Assert.Equal(new[] { 3, 5 }, rules.Where(r => r.Kind == AlsRefactoredDirectionRuleKind.Unlock).Select(r => r.RequiredFullState).Order());
        foreach (var hips in new[] { -.6f, -.5f, MathF.BitIncrement(-.5f), 0, MathF.BitDecrement(.5f), .5f, .6f })
        foreach (var feet in new[] { -.1f, 0, MathF.BitIncrement(0) })
        foreach (var full in new[] { 0, MathF.BitDecrement(1), 1 })
        foreach (var mask in Enumerable.Range(0, 16))
        {
            var input = new AlsRefactoredDirectionInput((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, hips, feet);
            float[] weights = [full, full, full, full, full, full];
            foreach (var rule in rules)
            {
                var expected = rule.Kind switch
                {
                    AlsRefactoredDirectionRuleKind.Forward => input.Forward,
                    AlsRefactoredDirectionRuleKind.Backward => input.Backward,
                    AlsRefactoredDirectionRuleKind.Left => input.Left,
                    AlsRefactoredDirectionRuleKind.Right => input.Right,
                    AlsRefactoredDirectionRuleKind.HipsLeft => hips <= -.5f && feet <= 0,
                    AlsRefactoredDirectionRuleKind.HipsRight => hips >= .5f && feet <= 0,
                    _ => hips > -.5f && hips < .5f && feet <= 0 && full == 1
                };
                Assert.Equal(expected, rule.Evaluate(input, weights));
            }
        }
        foreach (var rule in rules.Where(r => r.Kind == AlsRefactoredDirectionRuleKind.Unlock))
        {
            float[] weights = [1,1,1,1,1,1]; weights[rule.RequiredFullState] = .99f;
            Assert.False(rule.Evaluate(default, weights)); weights[rule.RequiredFullState] = 1;
            Assert.True(rule.Evaluate(default, weights));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceDriftCannotDropExitPriorityNotificationsOrBoneProfiles(bool crouching)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var original = MantlingHostFixture.Read("refactored_stance_machines");
        foreach (var change in new[] { "duration", "mode", "notify", "rule", "priority", "profile", "catalog" })
        {
            var json = JsonNode.Parse(original)!;
            var stance = json["stances"]![crouching ? 1 : 0]!;
            var machine = stance["bakedMachines"]![0]!;
            var edge = machine["transitions"]![0]!;
            if (change == "duration") edge["crossfadeDuration"] = .1;
            if (change == "mode") edge["blendMode"] = "Linear";
            if (change == "notify") edge["startNotify"] = 5;
            if (change == "rule") machine["states"]![0]!["transitions"]![0]!["bAutomaticRemainingTimeRule"] = true;
            if (change == "priority") machine["states"]![0]!["transitions"]![0]!["transitionIndex"] = 1;
            if (change == "profile") stance["blendProfiles"]![0]!["mode"] = 0;
            if (change == "catalog") json["catalogSha256"] = "foreign";
            Assert.Throws<ArgumentException>(() => new AlsRefactoredDirectionResources(json.ToJsonString(), catalog, crouching));
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void QuadraticDirectionBlendAdvancesInSharedTransitionStack(int hz)
    {
        Assert.Equal(.125f, AlsTransitionStack.Alpha(.25f, AlsTransitionBlend.QuadraticInOut));
        Assert.Equal(.875f, AlsTransitionStack.Alpha(.75f, AlsTransitionBlend.QuadraticInOut));
        var state = AlsTransitionStack.Start(AlsTransitionStack.Initialize(0), 1, .75f, AlsTransitionBlend.QuadraticInOut);
        var previous = 0f;
        for (var i = 0; i < hz; i++)
        {
            state = AlsTransitionStack.Advance(state, 1f / hz);
            var weight = AlsTransitionStack.Weight(state, 1);
            Assert.InRange(weight, previous, 1); previous = weight;
        }
        Assert.Equal(1, previous); Assert.Equal(0, state.Count);
    }
}
