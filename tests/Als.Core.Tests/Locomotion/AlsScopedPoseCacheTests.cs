using GodotAls.Core.Locomotion;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsScopedPoseCacheTests
{
    [Fact]
    public void TheExistingEmptyAlsCacheDefinitionStillAllowsNestedScopes()
    {
        var definition = new AlsPoseCacheDefinition(1, [], []);
        var committed = new AlsPoseCacheEvaluation(definition, 1, 0);
        var candidate = new AlsPoseCacheEvaluation(definition, 1, 0);
        candidate.BeginCandidate(new AlsFrameIdentity(1, 1, 1), committed);
        var outer = candidate.PushScope(); var inner = candidate.PushScope();
        candidate.PopScope(inner); candidate.PopScope(outer);
        Assert.Equal(0, candidate.SourceEvaluations); Assert.False(candidate.IsFaulted);
    }

    private static void Complete(AlsScopedPoseCache cache, AlsCachedPoseScope scope, int node)
    {
        cache.CompleteEvaluation(scope, node);
        cache.EndEvaluation(scope, node, failed: false);
    }

    [Fact]
    public void NestedPayloadsAreIsolatedAndOuterPayloadSurvives()
    {
        var cache = new AlsScopedPoseCache(2); var payload = new string[4];
        var outer = cache.PushScope();
        Assert.True(cache.BeginEvaluation(outer, 1, counterMatches: true));
        payload[cache.Slot(outer, 1)] = "outer bones, curves, attributes and root";
        Complete(cache, outer, 1);
        var inner = cache.PushScope();
        Assert.Throws<InvalidOperationException>(() => cache.RequireReadable(outer, 1));
        Assert.True(cache.BeginEvaluation(inner, 1, counterMatches: true));
        payload[cache.Slot(inner, 1)] = "inner payload"; Complete(cache, inner, 1);
        cache.PopScope(inner);
        Assert.False(cache.BeginEvaluation(outer, 1, counterMatches: true));
        Assert.Equal("outer bones, curves, attributes and root", payload[cache.Slot(outer, 1)]);
        Assert.Equal(2, cache.SourceEvaluations); cache.PopScope(outer);
    }

    [Fact]
    public void CounterMismatchAndNewScopeBothRequireAnotherSourceEvaluation()
    {
        var cache = new AlsScopedPoseCache(1); var scope = cache.PushScope();
        Assert.True(cache.BeginEvaluation(scope, 0, true)); Complete(cache, scope, 0);
        Assert.False(cache.BeginEvaluation(scope, 0, true));
        Assert.True(cache.BeginEvaluation(scope, 0, false)); Complete(cache, scope, 0);
        cache.PopScope(scope); var next = cache.PushScope();
        Assert.True(cache.BeginEvaluation(next, 0, true)); Complete(cache, next, 0);
        Assert.Equal(3, cache.Evaluations(0)); cache.PopScope(next);
    }

    [Fact]
    public void FailedInnerSourceBlocksOuterPublicationAndCanBeUnwound()
    {
        var cache = new AlsScopedPoseCache(2); var outer = cache.PushScope();
        cache.BeginEvaluation(outer, 0, true); Complete(cache, outer, 0);
        var inner = cache.PushScope(); cache.BeginEvaluation(inner, 1, false);
        cache.EndEvaluation(inner, 1, failed: true); cache.PopScope(inner);
        Assert.Throws<InvalidOperationException>(() => cache.RequireReadable(outer, 0));
        Assert.Throws<InvalidOperationException>(() => cache.PushScope());
        cache.PopScope(outer); cache.Reset();
        var retry = cache.PushScope(); Assert.True(cache.BeginEvaluation(retry, 0, true));
        Complete(cache, retry, 0); Assert.Equal(1, cache.SourceEvaluations); cache.PopScope(retry);
    }

    [Fact]
    public void FinallyCanReleaseAnAncestorWhenASourceLeavesAnInnerScopeOpen()
    {
        var cache = new AlsScopedPoseCache(1); var outer = cache.PushScope();
        cache.BeginEvaluation(outer, 0, false); var inner = cache.PushScope();
        Assert.Throws<InvalidOperationException>(() => cache.CompleteEvaluation(outer, 0));
        cache.EndEvaluation(outer, 0, failed: true);
        cache.PopScope(inner); cache.PopScope(outer); cache.Reset();
        Assert.False(cache.IsFaulted); Assert.Equal(0, cache.SourceEvaluations);
    }

    [Fact]
    public void SwallowingRecursionCannotPublishThePartiallyEvaluatedPose()
    {
        var cache = new AlsScopedPoseCache(1); var scope = cache.PushScope();
        cache.BeginEvaluation(scope, 0, false);
        Assert.Throws<InvalidOperationException>(() => cache.BeginEvaluation(scope, 0, true));
        Assert.Throws<InvalidOperationException>(() => cache.CompleteEvaluation(scope, 0));
        cache.EndEvaluation(scope, 0, failed: true);
        Assert.Throws<InvalidOperationException>(() => cache.RequireReadable(scope, 0));
        Assert.Equal(0, cache.SourceEvaluations); cache.PopScope(scope);
    }

    [Fact]
    public void ASourceCannotCloseResetOrExposeItsScopeBeforeFinally()
    {
        var cache = new AlsScopedPoseCache(1); var scope = cache.PushScope();
        cache.BeginEvaluation(scope, 0, false);
        Assert.Throws<InvalidOperationException>(() => cache.PopScope(scope));
        Assert.Throws<InvalidOperationException>(() => cache.Reset());
        Assert.Throws<InvalidOperationException>(() => cache.RequireReadable(scope, 0));
        cache.CompleteEvaluation(scope, 0);
        Assert.Throws<InvalidOperationException>(() => cache.PopScope(scope));
        Assert.Throws<InvalidOperationException>(() => cache.CompleteEvaluation(scope, 0));
        cache.EndEvaluation(scope, 0, false); cache.RequireReadable(scope, 0); cache.PopScope(scope);
    }

    [Fact]
    public void ForeignForgedAndPreviousCandidateScopesAreRejected()
    {
        var cache = new AlsScopedPoseCache(1); var foreign = new AlsScopedPoseCache(1);
        var scope = cache.PushScope(); var other = foreign.PushScope();
        Assert.Throws<InvalidOperationException>(() => cache.BeginEvaluation(other, 0, true));
        Assert.Throws<InvalidOperationException>(() => cache.ValidateScope(scope with { Serial = scope.Serial + 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.BeginEvaluation(scope, -1, true));
        Assert.Equal(0, cache.SourceEvaluations);
        cache.PopScope(scope); cache.Reset(); var next = cache.PushScope();
        Assert.Throws<InvalidOperationException>(() => cache.RequireReadable(scope, 0));
        Assert.True(next.Serial > scope.Serial); cache.PopScope(next); foreign.PopScope(other);
    }

    [Fact]
    public void AnUncompletedSourceSealsTheScopeButCanStillBeClosed()
    {
        var cache = new AlsScopedPoseCache(1); var scope = cache.PushScope();
        cache.BeginEvaluation(scope, 0, false);
        Assert.Throws<InvalidOperationException>(() => cache.EndEvaluation(scope, 0, failed: false));
        Assert.True(cache.IsFaulted); cache.PopScope(scope); cache.Reset();
        Assert.False(cache.IsFaulted);
    }
}
