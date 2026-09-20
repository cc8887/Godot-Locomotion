using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Tests;

public sealed class AlsAnimationFailureRecoveryTests
{
    [Fact]
    public void RepeatedRollbackRetainsInputAndOnlySuccessfulNextCommitConsumesLatch()
    {
        var recovery = new AlsAnimationFailureRecovery();
        recovery.RecordRolledBackFailure(Id(13), Id(12));
        recovery.RecordRolledBackFailure(Id(13), Id(12));
        Assert.Equal(2, recovery.FailedAttempts);
        Assert.True(recovery.RequiresCancellation(Id(13)));
        Assert.Throws<InvalidOperationException>(() => recovery.Commit(Id(12)));
        Assert.Throws<InvalidOperationException>(() => recovery.Commit(Id(14)));
        Assert.True(recovery.Pending);
        recovery.Commit(Id(13)); Assert.False(recovery.Pending);
        Assert.False(recovery.RequiresCancellation(Id(14)));
    }

    [Fact]
    public void PersistentFailureHasBoundedRetriesAndFailedRestorationBlocksImmediately()
    {
        var recovery = new AlsAnimationFailureRecovery();
        for (var i = 0; i < AlsAnimationFailureRecovery.MaximumAutomaticRetries; i++)
        { recovery.RecordRolledBackFailure(Id(13), Id(12)); Assert.True(recovery.CanRetry); }
        recovery.RecordRolledBackFailure(Id(13), Id(12));
        Assert.False(recovery.CanRetry); Assert.True(recovery.Pending);
        var restoreFailure = new AlsAnimationFailureRecovery();
        restoreFailure.RecordRolledBackFailure(Id(13), Id(12)); restoreFailure.Block();
        Assert.False(restoreFailure.CanRetry); Assert.True(restoreFailure.Pending);
    }

    [Fact]
    public void ForeignOrSkippedFrameCannotArmOrReadRecovery()
    {
        var recovery = new AlsAnimationFailureRecovery();
        Assert.Throws<InvalidOperationException>(() => recovery.RecordRolledBackFailure(Id(14), Id(12)));
        Assert.Throws<InvalidOperationException>(() => recovery.RecordRolledBackFailure(new(13, 1, 2), Id(12)));
        recovery.RecordRolledBackFailure(Id(13), Id(12));
        Assert.Throws<InvalidOperationException>(() => recovery.RequiresCancellation(new(13, 2, 1)));
        Assert.Throws<InvalidOperationException>(() => recovery.RecordRolledBackFailure(Id(14), Id(13)));
        Assert.Equal(Id(13), recovery.PendingIdentity);
    }
    private static AlsFrameIdentity Id(long frame) => new(frame, 1, 1);
}
