using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Transitions;

namespace GodotAls.Core.Tests;

public sealed class AlsDynamicTransitionRuntimeTests
{
    [Fact]
    public void ContractsAreSequentialUnmanagedAndPlaybackDefaultIsExplicitlyInactive()
    {
        AssertContract<AlsDynamicTransitionClipBinding>();
        AssertContract<AlsDynamicTransitionBinding>();
        AssertContract<AlsDynamicTransitionInput>();
        AssertContract<AlsDynamicTransitionPlayback>();
        AssertFields<AlsDynamicTransitionPlayback>(
            ("OccurrenceHandleId", typeof(int)), ("AnimationId", typeof(int)),
            ("PlaybackEpoch", typeof(long)), ("PreviousTime", typeof(float)),
            ("CurrentTime", typeof(float)), ("DurationSeconds", typeof(float)),
            ("PlayRate", typeof(float)), ("BlendSeconds", typeof(float)),
            ("FrameEndOffsetSeconds", typeof(double)), ("ContributesThisFrame", typeof(byte)),
            ("CooldownBlockedThisFrame", typeof(byte)), ("ActivatesAtFrameStart", typeof(byte)),
            ("ClosesAfterFrame", typeof(byte)));

        var playback = AlsDynamicTransitionPlayback.CreateDefault();

        Assert.Equal(-1, playback.OccurrenceHandleId);
        Assert.Equal(-1, playback.AnimationId);
        Assert.Equal(0L, playback.PlaybackEpoch);
        AssertPositiveZero(playback.PreviousTime);
        AssertPositiveZero(playback.CurrentTime);
        AssertPositiveZero(playback.DurationSeconds);
        AssertPositiveZero(playback.PlayRate);
        AssertPositiveZero(playback.BlendSeconds);
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(playback.FrameEndOffsetSeconds));
        Assert.Equal((byte)0, playback.ContributesThisFrame);
        Assert.Equal((byte)0, playback.CooldownBlockedThisFrame);
        Assert.Equal((byte)0, playback.ActivatesAtFrameStart);
        Assert.Equal((byte)0, playback.ClosesAfterFrame);
    }

    [Fact]
    public void AllowGateRelevanceAndStrictDistanceAreAppliedInAlsOrder()
    {
        var binding = Binding() with { DistanceMeters = 1f };
        var input = Input(AlsStance.Standing, leftDistance: 2f, rightDistance: 0f);

        AssertNoQueue(binding, input with { AllowTransitions = MathF.BitDecrement(1f - 1e-5f) });
        AssertNoQueue(binding, input with { AllowTransitions = 1f - 1e-5f, LeftRelevant = 0 });
        AssertNoQueue(binding, input with { AllowTransitions = 1f - 1e-5f, LeftTarget = Vector3.UnitX });

        var next = Queue(binding, input with { AllowTransitions = 1f - 1e-5f }, out var selection);

        Assert.Equal((byte)1, next.Queued);
        Assert.Equal(10, next.QueuedAnimationId);
        Assert.Equal(AlsTransitionFoot.Left, next.QueuedFoot);
        Assert.Equal(10, selection.AnimationId);
        Assert.Equal((byte)1, selection.Active);
    }

    [Theory]
    [InlineData(AlsStance.Standing, AlsTransitionFoot.Left, 10)]
    [InlineData(AlsStance.Standing, AlsTransitionFoot.Right, 11)]
    [InlineData(AlsStance.Crouching, AlsTransitionFoot.Left, 12)]
    [InlineData(AlsStance.Crouching, AlsTransitionFoot.Right, 13)]
    public void SelectsTheExactStanceAndFootSlot(
        AlsStance stance,
        AlsTransitionFoot foot,
        int expectedAnimationId)
    {
        var input = foot == AlsTransitionFoot.Left
            ? Input(stance, leftDistance: 1f, rightDistance: 0f)
            : Input(stance, leftDistance: 0f, rightDistance: 1f);

        var next = Queue(Binding(), input, out var selection);

        Assert.Equal(expectedAnimationId, next.QueuedAnimationId);
        Assert.Equal(foot, next.QueuedFoot);
        Assert.Equal(expectedAnimationId, selection.AnimationId);
        Assert.Equal(foot, selection.Foot);
        Assert.Equal(0.2f, selection.BlendSeconds);
        Assert.Equal(2f, selection.PlayRate);
    }

    [Fact]
    public void LargerDoubleSquaredDistanceWinsAndExactTieChoosesLeft()
    {
        var binding = Binding() with { DistanceMeters = 0.25f };

        var right = Queue(binding, Input(AlsStance.Standing, 1f, 2f), out _);
        var tie = Queue(binding, Input(AlsStance.Standing, 2f, 2f), out _);
        var huge = Queue(
            binding,
            new AlsDynamicTransitionInput(
                AlsStance.Standing,
                1f,
                new Vector3(float.MaxValue, 0f, 0f),
                Vector3.Zero,
                1,
                new Vector3(float.MaxValue, 0f, 0f),
                new Vector3(-float.MaxValue, 0f, 0f),
                1),
            out _);

        Assert.Equal(AlsTransitionFoot.Right, right.QueuedFoot);
        Assert.Equal(AlsTransitionFoot.Left, tie.QueuedFoot);
        Assert.Equal(AlsTransitionFoot.Right, huge.QueuedFoot);
    }

    [Fact]
    public void ProductionStyleDuplicateAnimationIdsResolveByFootAndRejectAmbiguousMetadata()
    {
        var legal = Binding() with
        {
            CrouchingLeft = new AlsDynamicTransitionClipBinding(10, 100, 1f),
            CrouchingRight = new AlsDynamicTransitionClipBinding(11, 100, 1.25f),
        };

        var queued = Queue(legal, Input(AlsStance.Crouching, 1f, 0f), out _);
        Assert.Equal(10, queued.QueuedAnimationId);
        Assert.Equal(AlsTransitionFoot.Left, queued.QueuedFoot);
        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            legal, 0.1f, queued, out var active, out _, out var playback, out var failure), failure.ToString());
        Assert.Equal((byte)1, active.Active);
        Assert.Equal(10, active.AnimationId);
        Assert.Equal(AlsTransitionFoot.Left, active.Foot);
        Assert.Equal(1f, playback.DurationSeconds);

        var ambiguous = legal with
        {
            CrouchingLeft = legal.CrouchingLeft with { DurationSeconds = 2f },
        };
        AssertQueueFailure(ambiguous, Input(AlsStance.Crouching, 1f, 0f), 0, queued, AlsP5FailureCode.InvalidBinding);
    }

    [Fact]
    public void EveryBindingSlotAndGlobalFieldIsPreflightedBeforeSelection()
    {
        var valid = Binding();
        var invalid = new[]
        {
            valid with { OccurrenceHandleId = -1 },
            valid with { AuthorityGroupId = -1 },
            valid with { StandingLeft = valid.StandingLeft with { AnimationId = -1 } },
            valid with { StandingRight = valid.StandingRight with { AdditiveBaseAnimationId = -1 } },
            valid with { CrouchingLeft = valid.CrouchingLeft with { DurationSeconds = 0f } },
            valid with { CrouchingRight = valid.CrouchingRight with { DurationSeconds = float.NaN } },
            valid with { CrouchingRight = valid.CrouchingRight with { AdditiveBaseAnimationId = 101 } },
            valid with { DistanceMeters = 0f },
            valid with { DistanceMeters = float.PositiveInfinity },
            valid with { BlendSeconds = -0.1f },
            valid with { BlendSeconds = float.NaN },
            valid with { PlayRate = 0f },
            valid with { PlayRate = float.PositiveInfinity },
            valid with { CooldownFrames = -1 },
        };

        foreach (var binding in invalid)
        {
            AssertQueueFailure(
                binding,
                Input(AlsStance.Standing, 1f, 0f),
                0,
                AlsDynamicTransitionState.CreateDefault(),
                AlsP5FailureCode.InvalidBinding);
        }
    }

    [Fact]
    public void CooldownSnapshotIsConsumedBeforePoisonedProbeInput()
    {
        var current = AlsDynamicTransitionState.CreateDefault();
        current.CooldownFrames = 1;
        var poisoned = new AlsDynamicTransitionInput(
            (AlsStance)byte.MaxValue,
            float.NaN,
            new Vector3(float.NaN),
            new Vector3(float.PositiveInfinity),
            byte.MaxValue,
            new Vector3(float.NegativeInfinity),
            new Vector3(float.NaN),
            byte.MaxValue);

        Assert.True(AlsDynamicTransitionRuntime.TryQueue(
            Binding(), poisoned, 1, current, out var next, out var selection, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        AssertBytesEqual(current, next);
        Assert.Equal(AlsDynamicTransitionQueuedSelection.CreateDefault(), selection);
    }

    [Fact]
    public void InvalidUnblockedProbeAndSnapshotRollbackBitForBit()
    {
        var current = ActiveState(10, AlsTransitionFoot.Left, 3, 0.1f, 0.2f);
        var valid = Input(AlsStance.Standing, 1f, 0f);
        var invalidInputs = new[]
        {
            valid with { Stance = (AlsStance)2 },
            valid with { AllowTransitions = float.NaN },
            valid with { LeftTarget = new Vector3(float.PositiveInfinity, 0f, 0f) },
            valid with { LeftLock = new Vector3(float.NaN, 0f, 0f) },
            valid with { RightTarget = new Vector3(float.NegativeInfinity, 0f, 0f) },
            valid with { RightLock = new Vector3(float.NaN, 0f, 0f) },
            valid with { LeftRelevant = 2 },
            valid with { RightRelevant = 2 },
        };

        foreach (var input in invalidInputs)
        {
            AssertQueueFailure(Binding(), input, 0, current, AlsP5FailureCode.NonFiniteInput);
        }
        AssertQueueFailure(Binding(), valid, 2, current, AlsP5FailureCode.NonFiniteInput);
    }

    [Fact]
    public void SelectionQueuesForNextFramePreservesActiveFootAndArmsConfiguredCooldown()
    {
        var current = ActiveState(10, AlsTransitionFoot.Left, 4, 0.1f, 0.2f);
        var next = Queue(Binding(), Input(AlsStance.Crouching, 0f, 2f), out var selection, current);

        Assert.Equal((byte)1, next.Active);
        Assert.Equal(10, next.AnimationId);
        Assert.Equal(AlsTransitionFoot.Left, next.Foot);
        Assert.Equal((byte)1, next.Queued);
        Assert.Equal(13, next.QueuedAnimationId);
        Assert.Equal(AlsTransitionFoot.Right, next.QueuedFoot);
        Assert.Equal(2, next.CooldownFrames);
        Assert.Equal((byte)1, selection.Active);
    }

    [Fact]
    public void QueueAtNBlocksNPlusOneAndNPlusTwoAndAllowsNPlusThree()
    {
        var binding = Binding() with
        {
            StandingLeft = new AlsDynamicTransitionClipBinding(10, 100, 10f),
            CooldownFrames = 2,
        };
        var state = Queue(binding, Input(AlsStance.Standing, 1f, 0f), out _);

        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            binding, 0.1f, state, out state, out _, out var n1, out var failure), failure.ToString());
        Assert.Equal((byte)1, n1.CooldownBlockedThisFrame);
        Assert.True(AlsDynamicTransitionRuntime.TryQueue(
            binding, Input(AlsStance.Standing, 0f, 2f), n1.CooldownBlockedThisFrame,
            state, out var blockedN1, out var n1Selection, out failure), failure.ToString());
        AssertBytesEqual(state, blockedN1);
        Assert.Equal((byte)0, n1Selection.Active);

        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            binding, 0.1f, blockedN1, out state, out _, out var n2, out failure), failure.ToString());
        Assert.Equal((byte)1, n2.CooldownBlockedThisFrame);
        Assert.Equal(0, state.CooldownFrames);
        Assert.True(AlsDynamicTransitionRuntime.TryQueue(
            binding, Input(AlsStance.Standing, 0f, 2f), n2.CooldownBlockedThisFrame,
            state, out var blockedN2, out var n2Selection, out failure), failure.ToString());
        AssertBytesEqual(state, blockedN2);
        Assert.Equal((byte)0, n2Selection.Active);

        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            binding, 0.1f, blockedN2, out state, out _, out var n3, out failure), failure.ToString());
        Assert.Equal((byte)0, n3.CooldownBlockedThisFrame);
        var allowedN3 = Queue(
            binding,
            Input(AlsStance.Standing, 0f, 2f),
            out var n3Selection,
            state,
            n3.CooldownBlockedThisFrame);
        Assert.Equal((byte)1, allowedN3.Queued);
        Assert.Equal(11, allowedN3.QueuedAnimationId);
        Assert.Equal((byte)1, n3Selection.Active);
    }

    [Fact]
    public void InactiveAdvancePublishesOnlyTheCooldownSnapshotAndDecrementsCandidateState()
    {
        var current = AlsDynamicTransitionState.CreateDefault();
        current.PlaybackEpoch = 7;
        current.CooldownFrames = 1;

        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            Binding(), 0.1f, current, out var next, out var closing, out var playback, out var failure));

        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(7, next.PlaybackEpoch);
        Assert.Equal(0, next.CooldownFrames);
        Assert.Equal(AlsDynamicTransitionPlayback.CreateDefault(), closing);
        Assert.Equal(-1, playback.OccurrenceHandleId);
        Assert.Equal(-1, playback.AnimationId);
        Assert.Equal((byte)1, playback.CooldownBlockedThisFrame);
        Assert.Equal((byte)0, playback.ContributesThisFrame);
        AssertPositiveZero(playback.PreviousTime);
        AssertPositiveZero(playback.CurrentTime);
        Assert.Equal(0L, BitConverter.DoubleToInt64Bits(playback.FrameEndOffsetSeconds));
    }

    [Fact]
    public void PromotionActivatesAtFrameStartAndContinuationAdvancesFiniteInterval()
    {
        var queued = Queue(Binding(), Input(AlsStance.Standing, 1f, 0f), out _);

        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            Binding(), 0.1f, queued, out var active, out var closing, out var first, out var failure), failure.ToString());

        Assert.Equal(AlsDynamicTransitionPlayback.CreateDefault(), closing);
        Assert.Equal((byte)1, active.Active);
        Assert.Equal((byte)0, active.Queued);
        Assert.Equal(10, active.AnimationId);
        Assert.Equal(AlsTransitionFoot.Left, active.Foot);
        Assert.Equal(1L, active.PlaybackEpoch);
        AssertPositiveZero(first.PreviousTime);
        Assert.Equal(0.2f, first.CurrentTime);
        Assert.Equal((double)0.1f, first.FrameEndOffsetSeconds);
        Assert.Equal((byte)1, first.ContributesThisFrame);
        Assert.Equal((byte)1, first.ActivatesAtFrameStart);
        Assert.Equal((byte)0, first.ClosesAfterFrame);

        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            Binding(), 0.1f, active, out var continued, out closing, out var second, out failure), failure.ToString());
        Assert.Equal(AlsDynamicTransitionPlayback.CreateDefault(), closing);
        Assert.Equal(0.2f, second.PreviousTime);
        Assert.Equal(0.4f, second.CurrentTime);
        Assert.Equal((byte)0, second.ActivatesAtFrameStart);
        Assert.Equal(0.2f, continued.PreviousPlaybackTime);
        Assert.Equal(0.4f, continued.PlaybackTime);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(10)]
    public void ReplacementClosesOldZeroWindowBeforeActivatingNewPlayback(int queuedAnimationId)
    {
        var current = ActiveState(10, AlsTransitionFoot.Left, 8, 0.1f, 0.25f);
        current.Queued = 1;
        current.QueuedAnimationId = queuedAnimationId;
        current.QueuedFoot = queuedAnimationId == 10 ? AlsTransitionFoot.Left : AlsTransitionFoot.Right;

        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            Binding(), 0.1f, current, out var next, out var closing, out var playback, out var failure), failure.ToString());

        Assert.Equal(7, closing.OccurrenceHandleId);
        Assert.Equal(10, closing.AnimationId);
        Assert.Equal(8, closing.PlaybackEpoch);
        Assert.Equal(0.25f, closing.PreviousTime);
        Assert.Equal(0.25f, closing.CurrentTime);
        Assert.Equal(0d, closing.FrameEndOffsetSeconds);
        Assert.Equal((byte)0, closing.ContributesThisFrame);
        Assert.Equal((byte)0, closing.ActivatesAtFrameStart);
        Assert.Equal((byte)1, closing.ClosesAfterFrame);
        Assert.Equal(queuedAnimationId, playback.AnimationId);
        Assert.Equal(9, playback.PlaybackEpoch);
        Assert.Equal((byte)1, playback.ContributesThisFrame);
        Assert.Equal((byte)1, playback.ActivatesAtFrameStart);
        Assert.Equal((byte)0, playback.ClosesAfterFrame);
        Assert.Equal(queuedAnimationId, next.AnimationId);
        Assert.Equal(current.QueuedFoot, next.Foot);
        Assert.Equal((byte)0, next.Queued);
    }

    [Fact]
    public void NaturalCompletionClosesAtExactTerminalOffsetAndLeavesLogicalStateInactive()
    {
        var binding = Binding() with
        {
            StandingLeft = new AlsDynamicTransitionClipBinding(10, 100, 0.5f),
        };
        var current = ActiveState(10, AlsTransitionFoot.Left, 3, 0.2f, 0.4f);
        current.CooldownFrames = 1;

        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            binding, 0.2f, current, out var next, out var closing, out var playback, out var failure), failure.ToString());

        Assert.Equal(AlsDynamicTransitionPlayback.CreateDefault(), closing);
        Assert.Equal(7, playback.OccurrenceHandleId);
        Assert.Equal(10, playback.AnimationId);
        Assert.Equal(3, playback.PlaybackEpoch);
        Assert.Equal(0.4f, playback.PreviousTime);
        Assert.Equal(BitConverter.SingleToInt32Bits(0.5f), BitConverter.SingleToInt32Bits(playback.CurrentTime));
        Assert.Equal(((double)0.5f - 0.4f) / 2d, playback.FrameEndOffsetSeconds);
        Assert.Equal((byte)1, playback.ContributesThisFrame);
        Assert.Equal((byte)1, playback.CooldownBlockedThisFrame);
        Assert.Equal((byte)0, playback.ActivatesAtFrameStart);
        Assert.Equal((byte)1, playback.ClosesAfterFrame);
        Assert.Equal((byte)0, next.Active);
        Assert.Equal(-1, next.AnimationId);
        Assert.Equal(AlsTransitionFoot.Left, next.Foot);
        AssertPositiveZero(next.PreviousPlaybackTime);
        AssertPositiveZero(next.PlaybackTime);
        Assert.Equal(3, next.PlaybackEpoch);
        Assert.Equal(0, next.CooldownFrames);
    }

    [Fact]
    public void ExactTerminalAndRoundedInteriorBoundariesRemainCanonical()
    {
        var exactBinding = Binding() with
        {
            StandingLeft = new AlsDynamicTransitionClipBinding(10, 100, 0.5f),
        };
        var exact = ActiveState(10, AlsTransitionFoot.Left, 1, 0.125f, 0.25f);
        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            exactBinding, 0.125f, exact, out var completed, out _, out var terminal, out var failure), failure.ToString());
        Assert.Equal((byte)0, completed.Active);
        Assert.Equal(0.5f, terminal.CurrentTime);
        Assert.Equal((byte)1, terminal.ClosesAfterFrame);

        var interiorBinding = Binding() with { PlayRate = 0.5f };
        var interior = ActiveState(10, AlsTransitionFoot.Left, 2, 0.25f, 0.5f);
        Assert.True(AlsDynamicTransitionRuntime.TryAdvance(
            interiorBinding, MathF.BitDecrement(1f), interior,
            out var advanced, out _, out var playback, out failure), failure.ToString());
        Assert.Equal((byte)1, advanced.Active);
        Assert.Equal(BitConverter.SingleToInt32Bits(MathF.BitDecrement(1f)), BitConverter.SingleToInt32Bits(playback.CurrentTime));
        Assert.True(playback.CurrentTime > playback.PreviousTime);
        Assert.True(playback.CurrentTime < playback.DurationSeconds);
    }

    [Fact]
    public void EpochExhaustionAndUnrepresentablePositiveAdvanceRollbackEverything()
    {
        var exhausted = AlsDynamicTransitionState.CreateDefault();
        exhausted.PlaybackEpoch = long.MaxValue;
        exhausted.Queued = 1;
        exhausted.QueuedAnimationId = 10;
        exhausted.QueuedFoot = AlsTransitionFoot.Left;
        AssertAdvanceFailure(Binding(), 0.1f, exhausted, AlsP5FailureCode.NonFiniteOutput);

        var stalled = ActiveState(10, AlsTransitionFoot.Left, 1, 0.25f, 0.5f);
        AssertAdvanceFailure(Binding() with { PlayRate = 1f }, float.Epsilon, stalled, AlsP5FailureCode.NonFiniteOutput);
    }

    [Fact]
    public void AdvanceValidationOrderAndCanonicalStateFailuresRollbackBitForBit()
    {
        var invalidBinding = Binding() with { DistanceMeters = 0f };
        var invalidState = AlsDynamicTransitionState.CreateDefault();
        invalidState.Active = 2;
        AssertAdvanceFailure(invalidBinding, 0f, invalidState, AlsP5FailureCode.InvalidDeltaTime);
        AssertAdvanceFailure(invalidBinding, 0.1f, invalidState, AlsP5FailureCode.InvalidBinding);

        var valid = Binding();
        var invalidStates = new List<AlsDynamicTransitionState>();

        AddInvalid(static state => state.Active = 2);
        AddInvalid(static state => state.Queued = 2);
        AddInvalid(static state => state.Foot = (AlsTransitionFoot)2);
        AddInvalid(static state => state.QueuedFoot = (AlsTransitionFoot)2);
        AddInvalid(static state => state.PlaybackEpoch = -1);
        AddInvalid(static state => state.CooldownFrames = -1);
        AddInvalid(static state => state.AnimationId = 0);
        AddInvalid(static state => state.PreviousPlaybackTime = -0f);
        AddInvalid(static state => state.PlaybackTime = -0f);
        AddInvalid(static state => state.Foot = AlsTransitionFoot.Right);
        AddInvalid(static state => { state.Active = 1; state.AnimationId = 10; state.PlaybackEpoch = 0; });
        AddInvalid(static state => { state.Active = 1; state.AnimationId = 99; state.PlaybackEpoch = 1; });
        AddInvalid(static state => { state.Active = 1; state.AnimationId = 10; state.PlaybackEpoch = 1; state.PreviousPlaybackTime = 0.3f; state.PlaybackTime = 0.2f; });
        AddInvalid(static state => { state.Active = 1; state.AnimationId = 10; state.PlaybackEpoch = 1; state.PlaybackTime = 1f; });
        AddInvalid(static state => state.QueuedAnimationId = 0);
        AddInvalid(static state => { state.Queued = 1; state.QueuedAnimationId = 99; });

        foreach (var state in invalidStates)
        {
            AssertAdvanceFailure(valid, 0.1f, state, AlsP5FailureCode.InvalidTimeline);
            AssertQueueFailure(valid, Input(AlsStance.Standing, 1f, 0f), 0, state, AlsP5FailureCode.InvalidTimeline);
        }

        void AddInvalid(Action<AlsDynamicTransitionStateBox> mutate)
        {
            var box = new AlsDynamicTransitionStateBox(AlsDynamicTransitionState.CreateDefault());
            mutate(box);
            invalidStates.Add(box.Value);
        }
    }

    private static AlsDynamicTransitionBinding Binding() => new(
        7,
        9,
        new AlsDynamicTransitionClipBinding(10, 100, 1f),
        new AlsDynamicTransitionClipBinding(11, 100, 1.25f),
        new AlsDynamicTransitionClipBinding(12, 100, 1.5f),
        new AlsDynamicTransitionClipBinding(13, 100, 2f),
        0.5f,
        0.2f,
        2f,
        2);

    private static AlsDynamicTransitionInput Input(
        AlsStance stance,
        float leftDistance,
        float rightDistance) => new(
        stance,
        1f,
        new Vector3(leftDistance, 0f, 0f),
        Vector3.Zero,
        leftDistance == 0f ? (byte)0 : (byte)1,
        new Vector3(rightDistance, 0f, 0f),
        Vector3.Zero,
        rightDistance == 0f ? (byte)0 : (byte)1);

    private static AlsDynamicTransitionState ActiveState(
        int animationId,
        AlsTransitionFoot foot,
        long epoch,
        float previous,
        float current)
    {
        var state = AlsDynamicTransitionState.CreateDefault();
        state.AnimationId = animationId;
        state.PlaybackEpoch = epoch;
        state.PreviousPlaybackTime = previous;
        state.PlaybackTime = current;
        state.Foot = foot;
        state.Active = 1;
        return state;
    }

    private static AlsDynamicTransitionState Queue(
        in AlsDynamicTransitionBinding binding,
        in AlsDynamicTransitionInput input,
        out AlsDynamicTransitionQueuedSelection selection,
        AlsDynamicTransitionState? current = null,
        byte cooldownBlockedThisFrame = 0)
    {
        var state = current ?? AlsDynamicTransitionState.CreateDefault();
        Assert.True(AlsDynamicTransitionRuntime.TryQueue(
            binding, input, cooldownBlockedThisFrame, state,
            out var next, out selection, out var failure), failure.ToString());
        Assert.Equal(AlsP5FailureCode.None, failure);
        return next;
    }

    private static void AssertNoQueue(
        in AlsDynamicTransitionBinding binding,
        in AlsDynamicTransitionInput input)
    {
        var current = AlsDynamicTransitionState.CreateDefault();
        current.PlaybackEpoch = 5;
        Assert.True(AlsDynamicTransitionRuntime.TryQueue(
            binding, input, 0, current, out var next, out var selection, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        AssertBytesEqual(current, next);
        Assert.Equal(AlsDynamicTransitionQueuedSelection.CreateDefault(), selection);
    }

    private static void AssertAdvanceFailure(
        in AlsDynamicTransitionBinding binding,
        float delta,
        in AlsDynamicTransitionState current,
        AlsP5FailureCode expectedFailure)
    {
        Assert.False(AlsDynamicTransitionRuntime.TryAdvance(
            binding, delta, current, out var next, out var closing, out var playback, out var failure));
        Assert.Equal(expectedFailure, failure);
        AssertBytesEqual(current, next);
        Assert.Equal(AlsDynamicTransitionPlayback.CreateDefault(), closing);
        Assert.Equal(AlsDynamicTransitionPlayback.CreateDefault(), playback);
    }

    private static void AssertQueueFailure(
        in AlsDynamicTransitionBinding binding,
        in AlsDynamicTransitionInput input,
        byte cooldownBlockedThisFrame,
        in AlsDynamicTransitionState current,
        AlsP5FailureCode expectedFailure)
    {
        Assert.False(AlsDynamicTransitionRuntime.TryQueue(
            binding, input, cooldownBlockedThisFrame, current,
            out var next, out var selection, out var failure));
        Assert.Equal(expectedFailure, failure);
        AssertBytesEqual(current, next);
        Assert.Equal(AlsDynamicTransitionQueuedSelection.CreateDefault(), selection);
    }

    private static void AssertBytesEqual<T>(in T expected, in T actual)
        where T : unmanaged
    {
        var expectedCopy = expected;
        var actualCopy = actual;
        Assert.Equal(
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref expectedCopy, 1)).ToArray(),
            MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref actualCopy, 1)).ToArray());
    }

    private static void AssertContract<T>() where T : struct
    {
        Assert.Equal(LayoutKind.Sequential, typeof(T).StructLayoutAttribute?.Value);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    private static void AssertFields<T>(params (string Name, Type Type)[] expected)
    {
        var actual = typeof(T)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(static field => field.MetadataToken)
            .Select(static field => (Normalize(field.Name), field.FieldType));
        Assert.Equal(expected, actual);
    }

    private static string Normalize(string name) =>
        name.StartsWith('<') ? name[1..name.IndexOf('>')] : name;

    private static void AssertPositiveZero(float value) =>
        Assert.Equal(0, BitConverter.SingleToInt32Bits(value));

    private sealed class AlsDynamicTransitionStateBox(AlsDynamicTransitionState value)
    {
        public AlsDynamicTransitionState Value = value;

        public byte Active { set => Value.Active = value; }
        public byte Queued { set => Value.Queued = value; }
        public AlsTransitionFoot Foot { set => Value.Foot = value; }
        public AlsTransitionFoot QueuedFoot { set => Value.QueuedFoot = value; }
        public long PlaybackEpoch { set => Value.PlaybackEpoch = value; }
        public int CooldownFrames { set => Value.CooldownFrames = value; }
        public int AnimationId { set => Value.AnimationId = value; }
        public int QueuedAnimationId { set => Value.QueuedAnimationId = value; }
        public float PreviousPlaybackTime { set => Value.PreviousPlaybackTime = value; }
        public float PlaybackTime { set => Value.PlaybackTime = value; }
    }
}
