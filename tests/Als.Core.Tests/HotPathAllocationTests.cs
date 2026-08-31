using System.Runtime.CompilerServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Exchange;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Pose;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class HotPathAllocationTests
{
    [Fact]
    public void EventAndExchangeHotPathDoesNotAllocateAfterWarmup()
    {
        var exchange = new AlsFrameExchange();

        Run(exchange, 100);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(exchange, 10_000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void FullLocomotionEvaluateDoesNotAllocateAfterWarmup()
    {
        var input = P3TestInput.Grounded(
            velocity: new System.Numerics.Vector3(2f, 0f, -4f),
            acceleration: new System.Numerics.Vector3(1f, 0f, -2f),
            rotationMode: AlsRotationMode.VelocityDirection,
            characterYaw: 0.25f);
        var allocated = MeasureConvenienceEvaluate(input);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ExplicitLocomotionEvaluateDoesNotAllocateAfterWarmup()
    {
        var input = P3TestInput.Grounded(
            velocity: new System.Numerics.Vector3(2f, 0f, -4f),
            acceleration: new System.Numerics.Vector3(1f, 0f, -2f),
            rotationMode: AlsRotationMode.VelocityDirection,
            characterYaw: 0.25f);
        var resolved = AlsLocomotionCommandResolver.Resolve(input.Command, input.Stance);

        var allocated = MeasureExplicitEvaluate(input, resolved);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ViewPoseEvaluateDoesNotAllocateAfterWarmup()
    {
        var input = P3TestInput.Grounded(
            rotationMode: AlsRotationMode.Aiming,
            characterYaw: 0.25f,
            viewYaw: 0.4f,
            aimYaw: 0.5f) with
        {
            CharacterTransform = System.Numerics.Matrix4x4.CreateRotationY(0.25f),
        };

        var allocated = MeasureViewPoseEvaluate(input);

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TurnRotateSelectAndFinalizeDoesNotAllocateAfterWarmup()
    {
        var input = P3TestInput.Grounded(
            rotationMode: AlsRotationMode.Aiming,
            characterYaw: 0f,
            aimYaw: 1.4f);
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = 1;
        state.LocomotionState = AlsLocomotionState.Grounded;
        state.ViewPose = new AlsViewPoseState(1.4f, 0f, 8f, 0.25f, 0.5f, 0f, 0f);
        var view = new AlsViewPoseOutput(1.4f, 0f, 0.25f, 0.5f, 0.75f, 0f);
        var settings = AlsTurnRotateSettings.CreateReference();

        for (var index = 0; index < 10_000; index++)
        {
            RunTurnRotate(settings, input, view, ref state);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            RunTurnRotate(settings, input, view, ref state);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void FootPlacementEvaluateDoesNotAllocateAfterWarmup()
    {
        var leftHit = new AlsFootHit(
            1, 1, new System.Numerics.Vector3(-0.2f, 0f, 0f),
            System.Numerics.Vector3.UnitY, -1, System.Numerics.Vector3.Zero,
            System.Numerics.Quaternion.Identity, 10, System.Numerics.Vector3.Zero);
        var rightHit = leftHit with
        {
            Position = new System.Numerics.Vector3(0.2f, 0f, 0f),
            ColliderId = 11,
        };
        var input = AlsFrameInput.CreateDefault(new AlsFrameIdentity(1, 0, 1), 1f / 60f) with
        {
            CharacterTransform = System.Numerics.Matrix4x4.Identity,
            Floor = new AlsFloorSample(
                1, System.Numerics.Vector3.UnitY, -1,
                System.Numerics.Matrix4x4.Identity, System.Numerics.Vector3.Zero),
            LeftFootHit = leftHit,
            RightFootHit = rightHit,
            CurrentDriveMode = AlsDriveMode.MotorDriven,
        };

        var allocated = MeasureFootPlacementEvaluate(input);

        Assert.Equal(0, allocated);
    }

    private static void Run(AlsFrameExchange exchange, int iterations)
    {
        var buffer = new AlsEventBuffer();

        for (var index = 0; index < iterations; index++)
        {
            var identity = new AlsFrameIdentity(index, 0, 1);
            var input = AlsFrameInput.CreateDefault(identity, 1f / 60f);

            exchange.PublishInput(input);
            exchange.TryReadInput(identity, out _);

            var result = AlsFrameResult.CreateDefault(identity);
            result.TypedEvents.TryAdd(
                new AlsAnimationEvent(
                    1, -1, -1, -1, 0, 0, 0, 0, 0,
                    0.25f, 1f, AlsTimelineEventKind.Generic,
                    AlsAnimationEventPhase.Trigger, default));

            exchange.PublishResult(result);
            exchange.TryConsumeResult(identity, out _);

            buffer.TryAdd(
                new AlsAnimationEvent(
                    2, -1, -1, -1, 0, 0, 0, 0, 0,
                    0.5f, 1f, AlsTimelineEventKind.Generic,
                    AlsAnimationEventPhase.Trigger, default));
            buffer.Clear();
        }
    }

    [Fact]
    public void P5BuffersDoNotAllocateAfterWarmup()
    {
        var events = new AlsEventBuffer();
        var outcomes = new AlsActionOutcomeBuffer();
        var animationEvent = new AlsAnimationEvent(
            1, 2, -1, 3, 1, 0, 0, 0, 0,
            0.25f, 1f, AlsTimelineEventKind.Generic,
            AlsAnimationEventPhase.Trigger, default);
        var outcome = new AlsActionOutcome(1, 2, 1, AlsActionResultCode.Accepted);

        for (var index = 0; index < 100; index++)
        {
            ExerciseP5Buffers(ref events, ref outcomes, animationEvent, outcome);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            ExerciseP5Buffers(ref events, ref outcomes, animationEvent, outcome);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void WarmEvaluationAllocatesZeroBytes()
    {
        AlsCurveKey[] keys =
        [
            new(0f, 1f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new(1f, 3f, 0f, 0f, AlsCurveInterpolationMode.Linear),
            new(0f, -1f, 0f, 0f, AlsCurveInterpolationMode.Constant),
        ];
        AlsCurveBinding[] bindings =
        [
            new(17, 0, 2, 1f, 1, 0),
            new(31, 2, 1, 1f, 1, 0),
        ];
        AlsCurveBlendSample[] samples =
        [
            new(0, 0, 0.25f, 0.75f),
            new(1, 0, 0.25f, 0.25f),
        ];

        ExerciseCurveRuntime(keys, bindings, samples, 100, out _, out _);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ExerciseCurveRuntime(keys, bindings, samples, 10_000, out var finalCycle, out var checksum);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(1_250L, finalCycle);
        Assert.True(float.IsFinite(checksum));
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ExerciseCurveRuntime(
        AlsCurveKey[] keys,
        AlsCurveBinding[] bindings,
        AlsCurveBlendSample[] samples,
        int iterations,
        out long finalCycle,
        out float checksum)
    {
        var cycle = 0L;
        var time = 0f;
        var sum = 0f;
        for (var index = 0; index < iterations; index++)
        {
            if (!AlsCurveRuntime.TrySample(
                    bindings[0], keys, 0, 0.75f, out var sampled, out var failure) ||
                failure != AlsP5FailureCode.None ||
                !AlsCurveRuntime.TryBlend(
                    bindings, keys, samples, out var blended, out failure) ||
                failure != AlsP5FailureCode.None ||
                !AlsCurveRuntime.TryBlendAdditiveToDefault(
                    1f, 0f, 2f, bindings, keys, samples, out var additive, out failure) ||
                failure != AlsP5FailureCode.None ||
                !AlsCurveRuntime.TryAdvancePlaybackTime(
                    1f, 1, cycle, time, 0.125f, 1f,
                    out cycle, out time, out failure) ||
                failure != AlsP5FailureCode.None)
            {
                throw new InvalidOperationException("Curve runtime allocation probe failed.");
            }

            sum += sampled + blended + additive + time;
        }

        finalCycle = cycle;
        checksum = sum;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void ExerciseP5Buffers(
        ref AlsEventBuffer events,
        ref AlsActionOutcomeBuffer outcomes,
        in AlsAnimationEvent animationEvent,
        in AlsActionOutcome outcome)
    {
        for (var eventIndex = 0; eventIndex < AlsEventBuffer.Capacity; eventIndex++)
        {
            if (!events.TryAdd(animationEvent with { EventId = eventIndex }))
            {
                throw new InvalidOperationException("Event buffer fill failed.");
            }
        }

        if (events[0].EventId != 0 ||
            events[AlsEventBuffer.Capacity - 1].EventId != AlsEventBuffer.Capacity - 1 ||
            events.TryAdd(animationEvent))
        {
            throw new InvalidOperationException("Event buffer read or overflow contract failed.");
        }

        var eventCopy = events;
        events.Clear();
        if (eventCopy.Count != AlsEventBuffer.Capacity || eventCopy[1].EventId != 1)
        {
            throw new InvalidOperationException("Event buffer copy contract failed.");
        }

        if (!outcomes.TryAdd(outcome) ||
            !outcomes.TryAdd(outcome with { RequestId = outcome.RequestId + 1 }) ||
            outcomes[0] != outcome ||
            outcomes.TryAdd(outcome))
        {
            throw new InvalidOperationException("Outcome buffer contract failed.");
        }

        var outcomeCopy = outcomes;
        outcomes.Clear();
        if (outcomeCopy.Count != AlsActionOutcomeBuffer.Capacity || outcomeCopy[1].RequestId != outcome.RequestId + 1)
        {
            throw new InvalidOperationException("Outcome buffer copy contract failed.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureConvenienceEvaluate(AlsFrameInput input)
    {
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        for (var index = 0; index < 10_000; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                ref state,
                ref result,
                P3TestSettings.Reference);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                ref state,
                ref result,
                P3TestSettings.Reference);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureExplicitEvaluate(
        AlsFrameInput input,
        AlsResolvedLocomotionCommand resolved)
    {
        var state = new AlsRuntimeState();
        var result = new AlsFrameResult();

        for (var index = 0; index < 10_000; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                resolved,
                ref state,
                ref result,
                P3TestSettings.Reference);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            AlsLocomotionModel.Evaluate(
                input,
                resolved,
                ref state,
                ref result,
                P3TestSettings.Reference);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureViewPoseEvaluate(AlsFrameInput input)
    {
        var state = new AlsRuntimeState { Initialized = 1 };
        var settings = AlsViewPoseSettings.CreateDefault();

        for (var index = 0; index < 10_000; index++)
        {
            if (!AlsViewPoseModel.TryEvaluate(
                    settings, input, state, out state, out _, out _))
            {
                throw new InvalidOperationException("View pose warmup failed.");
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            if (!AlsViewPoseModel.TryEvaluate(
                    settings, input, state, out state, out _, out _))
            {
                throw new InvalidOperationException("View pose measurement failed.");
            }
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static long MeasureFootPlacementEvaluate(AlsFrameInput input)
    {
        var state = AlsRuntimeState.CreateDefault();
        state.Initialized = 1;
        state.LocomotionState = AlsLocomotionState.Grounded;
        state.LeftFootProbeOrigin = new System.Numerics.Vector3(-0.2f, 0.13f, 0f);
        state.RightFootProbeOrigin = new System.Numerics.Vector3(0.2f, 0.13f, 0f);
        var settings = AlsFootPlacementSettings.CreateReference();

        for (var index = 0; index < 10_000; index++)
        {
            if (!AlsFootPlacementModel.TryEvaluate(
                    settings, input, 1f, 1f, 1f, 1f, state,
                    out state, out _, out _))
            {
                throw new InvalidOperationException("Foot placement warmup failed.");
            }
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            if (!AlsFootPlacementModel.TryEvaluate(
                    settings, input, 1f, 1f, 1f, 1f, state,
                    out state, out _, out _))
            {
                throw new InvalidOperationException("Foot placement measurement failed.");
            }
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void RunTurnRotate(
        in AlsTurnRotateSettings settings,
        in AlsFrameInput input,
        in AlsViewPoseOutput view,
        ref AlsRuntimeState state)
    {
        if (!AlsTurnRotateModel.TrySelectAndAdvance(
                settings, input, view, state, out state, out var selection, out _) ||
            !AlsTurnRotateModel.TryFinalizeYaw(selection, -1f, -0.5f, out _, out _))
        {
            throw new InvalidOperationException("Turn/rotate hot path failed.");
        }
    }
}
