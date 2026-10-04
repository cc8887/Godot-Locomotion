using System.Collections.Immutable;
using GodotAls.Core.Animation;

namespace GodotAls.Core.Tests;

public sealed class AlsLinkedLayerBindingsTests
{
    private static AlsLinkedLayerClass Class(string name, params (string Function, string Group)[] functions) =>
        new(name, functions.Select(f => new AlsLinkedLayerFunction(f.Function, f.Group)).ToImmutableArray());
    private static void Link(AlsLinkedLayerBindings bindings, string? name) => bindings.Commit(bindings.PrepareLink(name));

    [Fact]
    public void NamedGroupsShareButUngroupedCallSitesAndCharactersOwnSeparateHistory()
    {
        AlsLinkedLayerCallSite[] calls = [new("a", "Walk"), new("b", "Aim"),
            new("c", "Loose"), new("d", "Loose"), new("e", "Other")];
        var definition = Class("Rifle", ("Walk", "Body"), ("Aim", "Body"), ("Loose", "None"), ("Other", "Face"));
        var first = new AlsLinkedLayerBindings("Main", calls, [definition]);
        var second = new AlsLinkedLayerBindings("Main", calls, [definition]);
        Link(first, "Rifle"); Link(second, "Rifle");
        Assert.Same(first.Target("a"), first.Target("b"));
        Assert.NotEqual(first.Target("a").Instance, first.Target("e").Instance);
        Assert.NotEqual(first.Target("c").Instance, first.Target("d").Instance);
        Assert.NotSame(first.Target("a"), second.Target("a"));
        Assert.Equal(AlsLinkedLayerTargetKind.External, first.Target("a").Kind);
    }

    [Fact]
    public void PartialOverlayAndMatchingUnlinkPreserveOtherBindingsAndRestoreDefaults()
    {
        var defaults = Class("Default", ("Walk", "Base"), ("Aim", "Base"));
        var rifle = Class("Rifle", ("Walk", "Body"), ("Aim", "Body"));
        var aim = Class("AimOnly", ("Aim", "Body"));
        var bindings = new AlsLinkedLayerBindings("Main",
            [new("a", "Walk", "Default"), new("b", "Aim")], [defaults, rifle, aim]);
        Link(bindings, null);
        Assert.Equal("Default", bindings.Target("a").Class);
        Assert.Equal(AlsLinkedLayerTargetKind.Self, bindings.Target("b").Kind);
        Link(bindings, "Rifle"); var walk = bindings.Target("a");
        Link(bindings, "AimOnly");
        Assert.Equal(AlsLinkedLayerTargetKind.Unbound, bindings.Target("a").Kind);
        bindings.Commit(bindings.PrepareUnlink("Rifle"));
        Assert.Equal("Default", bindings.Target("a").Class);
        Assert.Equal("AimOnly", bindings.Target("b").Class);
        bindings.Commit(bindings.PrepareUnlink("AimOnly"));
        Assert.Equal(AlsLinkedLayerTargetKind.Self, bindings.Target("b").Kind);
    }

    [Fact]
    public void RepeatedClassUsesFirstGroupedCallSiteRatherThanCoalescingExistingInstances()
    {
        var rifle = Class("Rifle", ("Walk", "Body"), ("Aim", "Body"));
        var partial = Class("Partial", ("Aim", "Body"));
        var bindings = new AlsLinkedLayerBindings("Main", [new("a", "Walk", "Rifle"), new("b", "Aim", "Partial")], [rifle, partial]);
        Link(bindings, null); var walk = bindings.Target("a"); var aim = bindings.Target("b");
        Link(bindings, "Rifle");
        // Original UE skips the entire bucket if its first target already has
        // the requested class, even when another node has a different default.
        Assert.Same(walk, bindings.Target("a")); Assert.Same(aim, bindings.Target("b"));
    }

    [Fact]
    public void GroupFlagsIncludeCdoAndAllNodesWhileUngroupedFlagsComeFromCallSite()
    {
        var definition = Class("Rifle", ("Walk", "Body"), ("Aim", "Body"), ("Loose", "")) with { ReceiveNotifies = true };
        var bindings = new AlsLinkedLayerBindings("Main",
            [new("a", "Walk"), new("b", "Aim", PropagateNotifies: true), new("c", "Loose")], [definition]);
        Link(bindings, "Rifle");
        Assert.True(bindings.Target("a").ReceiveNotifies); Assert.True(bindings.Target("a").PropagateNotifies);
        Assert.False(bindings.Target("c").ReceiveNotifies); Assert.False(bindings.Target("c").PropagateNotifies);
    }

    [Fact]
    public void CancelAndForeignOrStaleCandidatesCannotChangeCommittedBindings()
    {
        var definition = Class("Rifle", ("Walk", "Body"));
        var bindings = new AlsLinkedLayerBindings("Main", [new("a", "Walk")], [definition]);
        var other = new AlsLinkedLayerBindings("Main", [new("a", "Walk")], [definition]);
        var initial = bindings.Target("a"); var cancelled = bindings.PrepareLink("Rifle");
        Assert.Throws<InvalidOperationException>(() => bindings.PrepareLink("Rifle"));
        bindings.Cancel(cancelled); Assert.Same(initial, bindings.Target("a"));
        var retry = bindings.PrepareLink("Rifle"); Assert.Equal(cancelled.Targets.ToArray(), retry.Targets.ToArray());
        Assert.Throws<InvalidOperationException>(() => bindings.Commit(cancelled));
        Assert.Throws<InvalidOperationException>(() => other.Commit(retry));
        bindings.Commit(retry); Assert.Throws<InvalidOperationException>(() => bindings.Commit(retry));
        var instance = bindings.Target("a"); Link(bindings, "Rifle"); Assert.Same(instance, bindings.Target("a"));
    }

    [Fact]
    public void MissingOrUnimplementedFunctionsDoNotOverrideAndSelfClassDoesNotRecurse()
    {
        var main = Class("Main", ("Walk", "Body"));
        var empty = new AlsLinkedLayerClass("Empty", [new("Walk", "Body", false)]);
        var bindings = new AlsLinkedLayerBindings("Main", [new("a", "Walk")], [main, empty]);
        Link(bindings, "Empty"); Assert.Equal(AlsLinkedLayerTargetKind.Self, bindings.Target("a").Kind);
        Link(bindings, "Main"); Assert.Equal(0, bindings.Target("a").Instance);
        Assert.Throws<ArgumentException>(() => bindings.PrepareLink("Unknown"));
        Link(bindings, "Main");
    }

    [Fact]
    public void SelectingMainRecreatesConfiguredDefaultPerCallInsteadOfRecursingOrLeavingUnbound()
    {
        var main = Class("Main", ("Walk", "Body"));
        var defaults = Class("Default", ("Walk", "Body")) with { ReceiveNotifies = true };
        var bindings = new AlsLinkedLayerBindings("Main",
            [new("a", "Walk", "Default"), new("b", "Walk", "Default")], [main, defaults]);
        Link(bindings, null); Assert.Same(bindings.Target("a"), bindings.Target("b"));
        Link(bindings, "Main");
        Assert.Equal("Default", bindings.Target("a").Class);
        Assert.Equal(AlsLinkedLayerTargetKind.External, bindings.Target("a").Kind);
        Assert.NotEqual(bindings.Target("a").Instance, bindings.Target("b").Instance);
        Assert.False(bindings.Target("a").ReceiveNotifies);
        var previous = bindings.Target("a"); Link(bindings, "Main");
        Assert.NotEqual(previous.Instance, bindings.Target("a").Instance);
    }

    [Fact]
    public void FunctionAndGroupNamesFollowUnrealNameCaseAndDefaultInitializationKeepsClassGroupsSeparate()
    {
        var first = Class("First", ("Walk", "Body"), ("Aim", "BODY"));
        var second = Class("Second", ("Aim", "body"));
        var bindings = new AlsLinkedLayerBindings("Main",
            [new("a", "walk", "First"), new("b", "Aim", "Second"), new("c", "AIM", "First")], [first, second]);
        Link(bindings, null);
        Assert.Same(bindings.Target("a"), bindings.Target("c"));
        Assert.NotEqual(bindings.Target("a").Instance, bindings.Target("b").Instance);
        var firstInstance = bindings.Target("a"); var secondInstance = bindings.Target("b");
        bindings.Commit(bindings.PrepareUnlink("Second"));
        Assert.Same(firstInstance, bindings.Target("a")); Assert.Same(secondInstance, bindings.Target("b"));
    }
}
