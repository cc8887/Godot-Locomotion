using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;

namespace GodotAls.Core.Tests;

public sealed class AlsCurveRuntimeTests
{
    [Fact]
    public void CurveContractsFreezeEnumAbiAndFieldLayout()
    {
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(AlsCurveInterpolationMode)));
        Assert.Equal(
            ["Constant", "Linear", "Cubic"],
            Enum.GetNames<AlsCurveInterpolationMode>());
        Assert.Equal(
            [0, 1, 2],
            Enum.GetValues<AlsCurveInterpolationMode>().Select(static value => (int)value));

        AssertContract<AlsCurveKey>();
        AssertContract<AlsCurveBinding>();
        AssertContract<AlsCurveBlendSample>();

        AssertPropertyOrder<AlsCurveKey>(
            "TimeSeconds", "Value", "ArriveTangent", "LeaveTangent", "Interpolation");
        AssertPropertyOrder<AlsCurveBinding>(
            "CurveId", "KeyOffset", "KeyCount", "DurationSeconds", "Required", "Loop");
        AssertPropertyOrder<AlsCurveBlendSample>(
            "BindingIndex", "Cycle", "TimeSeconds", "Weight");

        AssertStorageFieldOrder<AlsCurveKey>(
            ("TimeSeconds", typeof(float)), ("Value", typeof(float)),
            ("ArriveTangent", typeof(float)), ("LeaveTangent", typeof(float)),
            ("Interpolation", typeof(AlsCurveInterpolationMode)));
        AssertStorageFieldOrder<AlsCurveBinding>(
            ("CurveId", typeof(int)), ("KeyOffset", typeof(int)),
            ("KeyCount", typeof(int)), ("DurationSeconds", typeof(float)),
            ("Required", typeof(byte)), ("Loop", typeof(byte)));
        AssertStorageFieldOrder<AlsCurveBlendSample>(
            ("BindingIndex", typeof(int)), ("Cycle", typeof(long)),
            ("TimeSeconds", typeof(float)), ("Weight", typeof(float)));
    }

    [Fact]
    public void SamplesConstantLinearAndCubicSegments()
    {
        AlsCurveKey[] keys =
        [
            Key(0f, 2f, interpolation: AlsCurveInterpolationMode.Constant),
            Key(2f, 8f, interpolation: AlsCurveInterpolationMode.Linear),
            Key(0f, -2f, interpolation: AlsCurveInterpolationMode.Linear),
            Key(2f, 2f, interpolation: AlsCurveInterpolationMode.Linear),
            Key(1f, 1f, leaveTangent: 4f, interpolation: AlsCurveInterpolationMode.Cubic),
            Key(3f, 5f, arriveTangent: -2f, interpolation: AlsCurveInterpolationMode.Linear),
            Key(0f, 0f, leaveTangent: 4f, interpolation: AlsCurveInterpolationMode.Cubic),
            Key(2f, 1f, arriveTangent: -2f, interpolation: AlsCurveInterpolationMode.Linear),
        ];

        AssertSample(Present(30, 0, 2), keys, 1f, 2f);
        AssertSample(Present(70, 2, 2), keys, 1f, 0f);
        AssertSample(Present(110, 4, 2), keys, 2f, 4.5f);
        AssertSample(Present(130, 6, 2), keys, 1f, 2f);
    }

    [Fact]
    public void ExactKeysAndEndpointClampsPreserveSignedZeroBits()
    {
        AlsCurveKey[] keys =
        [
            Key(-2f, -0f, interpolation: AlsCurveInterpolationMode.Linear),
            Key(-1f, 1f, interpolation: AlsCurveInterpolationMode.Linear),
        ];
        var binding = Present(9, 0, 2);

        AssertSample(binding, keys, -4f, -0f);
        AssertSample(binding, keys, -2f, -0f);
        AssertSample(binding, keys, -1.5f, 0.5f);
        AssertSample(binding, keys, -1f, 1f);
        AssertSample(binding, keys, 4f, 1f);
    }

    [Fact]
    public void ClampsNonLoopAndNormalizesLoopTime()
    {
        AlsCurveKey[] keys =
        [
            Key(0f, 1f, interpolation: AlsCurveInterpolationMode.Linear),
            Key(2f, 5f, interpolation: AlsCurveInterpolationMode.Linear),
        ];
        var nonLoop = Present(1, 0, 2, duration: 2f);
        var loop = Present(1, 0, 2, duration: 2f, loop: 1);

        AssertSample(nonLoop, keys, -10f, 1f);
        AssertSample(nonLoop, keys, 10f, 5f);

        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            2f, 1, 4, 1.5f, 1f, 1f,
            out var nextCycle, out var nextTime, out var failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        Assert.Equal(5L, nextCycle);
        AssertFloatBits(0.5f, nextTime);
        Assert.True(AlsCurveRuntime.TrySample(
            loop, keys, nextCycle, nextTime, out var value, out failure));
        Assert.Equal(AlsP5FailureCode.None, failure);
        AssertFloatBits(2f, value);

        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            2f, 1, 9, 1.5f, 0.5f, 1f,
            out nextCycle, out nextTime, out failure));
        Assert.Equal(10L, nextCycle);
        AssertPositiveZero(nextTime);
        Assert.Equal(AlsP5FailureCode.None, failure);

        Assert.False(AlsCurveRuntime.TrySample(
            loop, keys, 10, 2f, out value, out failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
    }

    [Fact]
    public void SplitsLargeLoopTimeIntoInt64Cycle()
    {
        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            0.25f, 1, 7, 0.125f, 1_000_000f, 2f,
            out var cycle, out var time, out var failure));

        Assert.Equal(8_000_007L, cycle);
        AssertFloatBits(0.125f, time);
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Theory]
    [InlineData(0x38D1B717, 0x38D1B716, 0x47D1B717)]
    [InlineData(0x358637BD, 0x358637BC, 0x448637BD)]
    public void PlaybackTimeDoesNotRoundAnExactCycleRemainderIntoAnExtraCycle(
        int durationBits,
        int currentTimeBits,
        int deltaBits)
    {
        var duration = BitConverter.Int32BitsToSingle(durationBits);
        var currentTime = BitConverter.Int32BitsToSingle(currentTimeBits);
        var delta = BitConverter.Int32BitsToSingle(deltaBits);

        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            duration, 1, 0, currentTime, delta, 1f,
            out var cycle, out var time, out var failure));
        Assert.Equal(1_073_741_824L, cycle);
        Assert.Equal(currentTimeBits, BitConverter.SingleToInt32Bits(time));
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Fact]
    public void PlaybackTimePreservesExactInt64QuotientAndLowRemainderAboveTwoToThe53()
    {
        const long expectedCycles = 1_249_512_265_072_881_006L;
        var duration = BitConverter.Int32BitsToSingle(0x3843467A);
        var currentTime = BitConverter.Int32BitsToSingle(0x3823DACD);
        var delta = BitConverter.Int32BitsToSingle(0x484298A1);
        var playRate = BitConverter.Int32BitsToSingle(0x4D8B3535);

        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            duration, 1, 0, currentTime, delta, playRate,
            out var cycle, out var time, out var failure));
        Assert.Equal(expectedCycles, cycle);
        Assert.Equal(0x38112061, BitConverter.SingleToInt32Bits(time));
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Fact]
    public void PlaybackTimeKeepsOddQuotientBitsAndChecksFinalInt64Addition()
    {
        const long expectedCycles = 6_148_914_691_236_517_205L;
        const long exactLongMaxStart = 3_074_457_345_618_258_602L;
        const float powerOfTwo32 = 4_294_967_296f;

        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            3f, 1, 0, 0f, powerOfTwo32, powerOfTwo32,
            out var cycle, out var time, out var failure));
        Assert.Equal(expectedCycles, cycle);
        AssertFloatBits(1f, time);
        Assert.Equal(AlsP5FailureCode.None, failure);

        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            3f, 1, exactLongMaxStart, 0f, powerOfTwo32, powerOfTwo32,
            out cycle, out time, out failure));
        Assert.Equal(long.MaxValue, cycle);
        AssertFloatBits(1f, time);
        Assert.Equal(AlsP5FailureCode.None, failure);

        Assert.False(AlsCurveRuntime.TryAdvancePlaybackTime(
            3f, 1, exactLongMaxStart + 1, 0f, powerOfTwo32, powerOfTwo32,
            out cycle, out time, out failure));
        Assert.Equal(0L, cycle);
        AssertPositiveZero(time);
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
    }

    [Fact]
    public void PlaybackTimeSplitsTinyDurationWithoutLosingTheCycleCount()
    {
        const float powerOfTwo62 = 4_611_686_018_427_387_904f;

        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            float.Epsilon, 1, 0, 0f, float.Epsilon, powerOfTwo62,
            out var cycle, out var time, out var failure));
        Assert.Equal(4_611_686_018_427_387_904L, cycle);
        AssertPositiveZero(time);
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Fact]
    public void PlaybackTimeHandlesZeroDurationAndFloatRemainderCarry()
    {
        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            0f, 0, 0, 0f, float.MaxValue, float.MaxValue,
            out var cycle, out var time, out var failure));
        Assert.Equal(0L, cycle);
        AssertPositiveZero(time);
        Assert.Equal(AlsP5FailureCode.None, failure);

        Assert.True(AlsCurveRuntime.TryAdvancePlaybackTime(
            0.1f, 1, 0, 0f, 0.048f, 6.25f,
            out cycle, out time, out failure));
        Assert.Equal(3L, cycle);
        AssertPositiveZero(time);
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Fact]
    public void PlaybackTimeRejectsInvalidInputsTransactionally()
    {
        var cases = new (float Duration, byte Loop, long Cycle, float Time, float Delta, float Rate, AlsP5FailureCode Failure)[]
        {
            (-1f, 0, 0, 0f, 0f, 1f, AlsP5FailureCode.InvalidBinding),
            (float.NaN, 0, 0, 0f, 0f, 1f, AlsP5FailureCode.InvalidBinding),
            (0f, 1, 0, 0f, 0f, 1f, AlsP5FailureCode.InvalidBinding),
            (1f, 2, 0, 0f, 0f, 1f, AlsP5FailureCode.InvalidBinding),
            (1f, 0, 0, 0f, -0.1f, 1f, AlsP5FailureCode.InvalidDeltaTime),
            (1f, 0, 0, 0f, float.NaN, 1f, AlsP5FailureCode.InvalidDeltaTime),
            (1f, 0, 0, 0f, float.PositiveInfinity, 1f, AlsP5FailureCode.InvalidDeltaTime),
            (1f, 0, 0, 0f, 0f, -0.1f, AlsP5FailureCode.NonFiniteInput),
            (1f, 0, 0, 0f, 0f, float.NaN, AlsP5FailureCode.NonFiniteInput),
            (1f, 0, 0, float.NaN, 0f, 1f, AlsP5FailureCode.NonFiniteInput),
            (1f, 0, 1, 0f, 0f, 1f, AlsP5FailureCode.InvalidTimeline),
            (1f, 0, 0, -0.1f, 0f, 1f, AlsP5FailureCode.InvalidTimeline),
            (1f, 0, 0, 1.1f, 0f, 1f, AlsP5FailureCode.InvalidTimeline),
            (1f, 1, -1, 0f, 0f, 1f, AlsP5FailureCode.InvalidTimeline),
            (1f, 1, 0, -0.1f, 0f, 1f, AlsP5FailureCode.InvalidTimeline),
            (1f, 1, 0, 1f, 0f, 1f, AlsP5FailureCode.InvalidTimeline),
        };

        foreach (var item in cases)
        {
            Assert.False(AlsCurveRuntime.TryAdvancePlaybackTime(
                item.Duration, item.Loop, item.Cycle, item.Time, item.Delta, item.Rate,
                out var nextCycle, out var nextTime, out var failure));
            Assert.Equal(0L, nextCycle);
            AssertPositiveZero(nextTime);
            Assert.Equal(item.Failure, failure);
        }
    }

    [Fact]
    public void PlaybackTimeRejectsInt64OverflowEdges()
    {
        Assert.False(AlsCurveRuntime.TryAdvancePlaybackTime(
            1f, 1, long.MaxValue, 0f, 1f, 1f,
            out var cycle, out var time, out var failure));
        Assert.Equal(0L, cycle);
        AssertPositiveZero(time);
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);

        Assert.False(AlsCurveRuntime.TryAdvancePlaybackTime(
            float.Epsilon, 1, 0, 0f, float.MaxValue, float.MaxValue,
            out cycle, out time, out failure));
        Assert.Equal(0L, cycle);
        AssertPositiveZero(time);
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);

        Assert.False(AlsCurveRuntime.TryAdvancePlaybackTime(
            1f, 1, long.MinValue, 0f, 0f, 1f,
            out cycle, out time, out failure));
        Assert.Equal(0L, cycle);
        AssertPositiveZero(time);
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
    }

    [Fact]
    public void RequiredEmptyCurveFailsButOptionalContributesZero()
    {
        var required = new AlsCurveBinding(4, 0, 0, 0f, 1, 0);
        Assert.False(AlsCurveRuntime.TrySample(
            required, [], 0, 0f, out var value, out var failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);

        var optional = Missing(0);
        Assert.True(AlsCurveRuntime.TrySample(
            optional, [], 0, 0f, out value, out failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.None, failure);

        AlsCurveKey[] keys = [Key(0f, 6f)];
        AlsCurveBinding[] bindings = [Present(42, 0, 1), Missing(1)];
        AlsCurveBlendSample[] samples =
        [
            new(0, 0, 0f, 1f),
            new(1, 0, 0f, 1f),
        ];
        Assert.True(AlsCurveRuntime.TryBlend(
            bindings, keys, samples, out value, out failure));
        AssertFloatBits(3f, value);
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Fact]
    public void MissingOptionalCurveStillValidatesTimelinePolicy()
    {
        var optionalLoop = Missing(0, duration: 1f, loop: 1);

        Assert.True(AlsCurveRuntime.TrySample(
            optionalLoop, [], 7, 0.5f, out var value, out var failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.None, failure);

        Assert.False(AlsCurveRuntime.TrySample(
            optionalLoop, [], 7, 1f, out value, out failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
    }

    [Fact]
    public void RejectsOverflowingAndMalformedBindingWindows()
    {
        AlsCurveKey[] keys = [Key(0f, 1f)];
        AlsCurveBinding[] invalid =
        [
            new(1, -1, 1, 0f, 1, 0),
            new(1, 0, -1, 0f, 1, 0),
            new(1, int.MaxValue, 1, 0f, 1, 0),
            new(1, 1, int.MaxValue, 0f, 1, 0),
            new(-1, 0, 1, 0f, 1, 0),
            new(-2, 0, 0, 0f, 0, 0),
            new(0, 0, 0, 0f, 0, 0),
            new(-1, 0, 0, 0f, 1, 0),
            new(-1, 0, 0, 0f, 2, 0),
            new(-1, 0, 0, 0f, 0, 2),
            new(1, 0, 1, -1f, 1, 0),
            new(1, 0, 1, float.NaN, 1, 0),
            new(1, 0, 1, 0f, 1, 1),
        ];

        foreach (var binding in invalid)
        {
            Assert.False(AlsCurveRuntime.TrySample(
                binding, keys, 0, float.NaN, out var value, out var failure));
            AssertPositiveZero(value);
            Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
        }

        Assert.True(AlsCurveRuntime.TrySample(
            Missing(keys.Length), keys, 0, 0f, out var optionalValue, out var optionalFailure));
        AssertPositiveZero(optionalValue);
        Assert.Equal(AlsP5FailureCode.None, optionalFailure);
    }

    [Fact]
    public void RejectsTouchedMalformedKeysAndUnknownSelectedInterpolation()
    {
        var malformedTables = new[]
        {
            new[] { Key(float.NaN, 1f) },
            new[] { Key(0f, float.NaN) },
            new[]
            {
                Key(0f, 0f, interpolation: (AlsCurveInterpolationMode)byte.MaxValue),
                Key(1f, 1f),
            },
            new[]
            {
                Key(0f, 0f, leaveTangent: float.NaN, interpolation: AlsCurveInterpolationMode.Cubic),
                Key(1f, 1f),
            },
            new[]
            {
                Key(0f, 0f, interpolation: AlsCurveInterpolationMode.Cubic),
                Key(1f, 1f, arriveTangent: float.NaN),
            },
            new[]
            {
                Key(0f, 0f, arriveTangent: float.NaN, interpolation: AlsCurveInterpolationMode.Linear),
                Key(1f, 1f),
            },
            new[] { Key(0f, 0f), Key(0f, 1f) },
        };

        foreach (var keys in malformedTables)
        {
            Assert.False(AlsCurveRuntime.TrySample(
                Present(1, 0, keys.Length), keys, 0, 0.5f,
                out var value, out var failure));
            AssertPositiveZero(value);
            Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
        }
    }

    [Fact]
    public void RejectsTouchedInvertedBinarySearchBoundsTransactionally()
    {
        AlsCurveKey[] keys =
        [
            Key(0f, 0f),
            Key(2f, 2f),
            Key(1f, 1f),
        ];

        Assert.False(AlsCurveRuntime.TrySample(
            Present(1, 0, keys.Length), keys, 0, 0.5f,
            out var value, out var failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
    }

    [Fact]
    public void DerivedSampleOverflowFailsTransactionally()
    {
        AlsCurveKey[] keys =
        [
            Key(0f, 0f, leaveTangent: float.MaxValue, interpolation: AlsCurveInterpolationMode.Cubic),
            Key(float.MaxValue, 0f, arriveTangent: -float.MaxValue),
        ];

        Assert.False(AlsCurveRuntime.TrySample(
            Present(1, 0, 2), keys, 0, float.MaxValue * 0.5f,
            out var value, out var failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure);
    }

    [Fact]
    public void RejectsNonCanonicalSampleCycleAndNonFiniteTime()
    {
        AlsCurveKey[] keys = [Key(0f, 1f)];
        var cases = new (AlsCurveBinding Binding, long Cycle, float Time, AlsP5FailureCode Failure)[]
        {
            (Present(1, 0, 1), 1, 0f, AlsP5FailureCode.InvalidTimeline),
            (Present(1, 0, 1, duration: 1f, loop: 1), -1, 0f, AlsP5FailureCode.InvalidTimeline),
            (Present(1, 0, 1, duration: 1f, loop: 1), 0, -0.1f, AlsP5FailureCode.InvalidTimeline),
            (Present(1, 0, 1, duration: 1f, loop: 1), 0, 1f, AlsP5FailureCode.InvalidTimeline),
            (Present(1, 0, 1), 0, float.NaN, AlsP5FailureCode.NonFiniteInput),
            (Present(1, 0, 1), 0, float.PositiveInfinity, AlsP5FailureCode.NonFiniteInput),
        };

        foreach (var item in cases)
        {
            Assert.False(AlsCurveRuntime.TrySample(
                item.Binding, keys, item.Cycle, item.Time,
                out var value, out var failure));
            AssertPositiveZero(value);
            Assert.Equal(item.Failure, failure);
        }
    }

    [Fact]
    public void RejectsNegativeOrNonFiniteWeight()
    {
        AlsCurveKey[] keys = [Key(0f, 2f)];
        AlsCurveBinding[] bindings = [Present(99, 0, 1)];
        var invalidWeights = new[]
        {
            -0.1f, float.NaN, float.NegativeInfinity, float.PositiveInfinity,
        };

        foreach (var weight in invalidWeights)
        {
            AlsCurveBlendSample[] samples = [new(0, 0, 0f, weight)];
            Assert.False(AlsCurveRuntime.TryBlend(
                bindings, keys, samples, out var value, out var failure));
            AssertPositiveZero(value);
            Assert.Equal(AlsP5FailureCode.NonFiniteInput, failure);
        }

        AlsCurveBlendSample[] negativeZero = [new(0, 0, 0f, -0f)];
        Assert.True(AlsCurveRuntime.TryBlend(
            bindings, keys, negativeZero, out var zeroValue, out var zeroFailure));
        AssertPositiveZero(zeroValue);
        Assert.Equal(AlsP5FailureCode.None, zeroFailure);
    }

    [Fact]
    public void NormalizesWeightedBlend()
    {
        AlsCurveKey[] keys = [Key(0f, 2f), Key(0f, 10f)];
        AlsCurveBinding[] bindings =
        [
            Present(42, 0, 1),
            Present(1000, 1, 1),
        ];
        AlsCurveBlendSample[] samples =
        [
            new(0, 0, 0f, 1f),
            new(1, 0, 0f, 3f),
        ];

        Assert.True(AlsCurveRuntime.TryBlend(
            bindings, keys, samples, out var value, out var failure));
        AssertFloatBits(8f, value);
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Fact]
    public void BlendUsesBindingOrdinalAndAcceptsDuplicateBindingSamples()
    {
        AlsCurveKey[] keys = [Key(0f, 4f)];
        AlsCurveBinding[] bindings = [Present(999, 0, 1)];
        AlsCurveBlendSample[] samples =
        [
            new(0, 0, 0f, 1f),
            new(0, 0, 0f, 2f),
        ];

        Assert.True(AlsCurveRuntime.TryBlend(
            bindings, keys, samples, out var value, out var failure));
        AssertFloatBits(4f, value);
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Fact]
    public void BlendAccumulationIsStableInCallerOrder()
    {
        AlsCurveKey[] keys =
        [
            Key(0f, float.MaxValue),
            Key(0f, -float.MaxValue),
            Key(0f, 1f),
        ];
        AlsCurveBinding[] bindings =
        [
            Present(10, 0, 1),
            Present(20, 1, 1),
            Present(30, 2, 1),
        ];
        AlsCurveBlendSample[] cancellingFirst =
        [
            new(0, 0, 0f, 1f),
            new(1, 0, 0f, 1f),
            new(2, 0, 0f, 1f),
        ];
        AlsCurveBlendSample[] smallMiddle =
        [
            new(0, 0, 0f, 1f),
            new(2, 0, 0f, 1f),
            new(1, 0, 0f, 1f),
        ];

        Assert.True(AlsCurveRuntime.TryBlend(
            bindings, keys, cancellingFirst, out var cancellingValue, out var cancellingFailure));
        Assert.True(AlsCurveRuntime.TryBlend(
            bindings, keys, smallMiddle, out var smallMiddleValue, out var smallMiddleFailure));
        AssertFloatBits(1f / 3f, cancellingValue);
        AssertPositiveZero(smallMiddleValue);
        Assert.Equal(AlsP5FailureCode.None, cancellingFailure);
        Assert.Equal(AlsP5FailureCode.None, smallMiddleFailure);
    }

    [Fact]
    public void ZeroTotalWeightReturnsZero()
    {
        Assert.True(AlsCurveRuntime.TryBlend(
            [], [], [], out var emptyValue, out var emptyFailure));
        AssertPositiveZero(emptyValue);
        Assert.Equal(AlsP5FailureCode.None, emptyFailure);

        AlsCurveKey[] keys = [Key(0f, 8f)];
        AlsCurveBinding[] bindings = [Present(1, 0, 1)];
        AlsCurveBlendSample[] zeroSamples = [new(0, 0, 0f, 0f)];
        Assert.True(AlsCurveRuntime.TryBlend(
            bindings, keys, zeroSamples, out var zeroValue, out var zeroFailure));
        AssertPositiveZero(zeroValue);
        Assert.Equal(AlsP5FailureCode.None, zeroFailure);

        AlsCurveBlendSample[] invalidZeroSample = [new(1, 0, 0f, 0f)];
        Assert.False(AlsCurveRuntime.TryBlend(
            bindings, keys, invalidZeroSample, out var invalidValue, out var invalidFailure));
        AssertPositiveZero(invalidValue);
        Assert.Equal(AlsP5FailureCode.InvalidBinding, invalidFailure);
    }

    [Fact]
    public void BlendRejectsAccumulatedFloatOverflow()
    {
        AlsCurveKey[] keys = [Key(0f, float.MaxValue)];
        AlsCurveBinding[] bindings = [Present(1, 0, 1)];
        AlsCurveBlendSample[] samples = [new(0, 0, 0f, 2f)];

        Assert.False(AlsCurveRuntime.TryBlend(
            bindings, keys, samples, out var value, out var failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure);
    }

    [Fact]
    public void ZeroWeightStillValidatesBindingAndTimeline()
    {
        AlsCurveKey[] keys = [Key(0f, 1f)];
        AlsCurveBinding[] malformedBindings = [new(1, int.MaxValue, 1, 0f, 1, 0)];
        AlsCurveBlendSample[] malformedBindingSample = [new(0, 0, 0f, 0f)];
        Assert.False(AlsCurveRuntime.TryBlend(
            malformedBindings, keys, malformedBindingSample, out var value, out var failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);

        AlsCurveBinding[] loopBindings = [Present(1, 0, 1, duration: 1f, loop: 1)];
        AlsCurveBlendSample[] malformedTimelineSample = [new(0, 0, 1f, 0f)];
        Assert.False(AlsCurveRuntime.TryBlend(
            loopBindings, keys, malformedTimelineSample, out value, out failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.InvalidTimeline, failure);
    }

    [Fact]
    public void AdditiveSemanticUsesFrozenMissingDefault()
    {
        AlsCurveBinding[] bindings = [Missing(0)];
        AlsCurveBlendSample[] samples = [new(0, 0, 0f, 100f)];

        Assert.True(AlsCurveRuntime.TryBlendAdditiveToDefault(
            1f, 0f, 2f, bindings, [], samples,
            out var missingValue, out var missingFailure));
        AssertFloatBits(1f, missingValue);
        Assert.Equal(AlsP5FailureCode.None, missingFailure);

        AlsCurveKey[] keys = [Key(0f, 0.25f)];
        bindings = [Present(1, 0, 1)];
        samples = [new(0, 0, 0f, 2f)];
        Assert.True(AlsCurveRuntime.TryBlendAdditiveToDefault(
            1f, 0f, 2f, bindings, keys, samples,
            out var addedValue, out var addedFailure));
        AssertFloatBits(1.5f, addedValue);
        Assert.Equal(AlsP5FailureCode.None, addedFailure);
    }

    [Fact]
    public void NegativeTransitionSampleDisablesAndClamps()
    {
        AlsCurveKey[] keys = [Key(0f, -1f)];
        AlsCurveBinding[] bindings = [Present(1, 0, 1)];
        AlsCurveBlendSample[] samples = [new(0, 0, 0f, 1f)];

        Assert.True(AlsCurveRuntime.TryBlendAdditiveToDefault(
            1f, 0f, 1f, bindings, keys, samples,
            out var value, out var failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.None, failure);

        AlsCurveKey[] clampOnceKeys = [Key(0f, 2f), Key(0f, -2f)];
        AlsCurveBinding[] clampOnceBindings = [Present(1, 0, 1), Present(2, 1, 1)];
        AlsCurveBlendSample[] clampOnceSamples =
        [
            new(0, 0, 0f, 1f),
            new(1, 0, 0f, 1f),
        ];
        Assert.True(AlsCurveRuntime.TryBlendAdditiveToDefault(
            1f, 0f, 1f, clampOnceBindings, clampOnceKeys, clampOnceSamples,
            out value, out failure));
        AssertFloatBits(1f, value);
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    [Fact]
    public void AdditiveRejectsInvalidFrozenPolicyTransactionally()
    {
        var cases = new[]
        {
            (Default: float.NaN, Minimum: 0f, Maximum: 1f),
            (Default: 0f, Minimum: float.NaN, Maximum: 1f),
            (Default: 0f, Minimum: 0f, Maximum: float.PositiveInfinity),
            (Default: 0f, Minimum: 2f, Maximum: 1f),
        };

        foreach (var item in cases)
        {
            Assert.False(AlsCurveRuntime.TryBlendAdditiveToDefault(
                item.Default, item.Minimum, item.Maximum, [], [], [],
                out var value, out var failure));
            AssertPositiveZero(value);
            Assert.Equal(AlsP5FailureCode.InvalidBinding, failure);
        }
    }

    [Fact]
    public void AdditiveValidatesZeroWeightSamplesAndClampsEmptyDefaultOnce()
    {
        AlsCurveKey[] keys = [Key(0f, 1f)];
        AlsCurveBinding[] bindings = [Present(1, 0, 1)];
        AlsCurveBlendSample[] invalidZeroSamples = [new(1, 0, 0f, 0f)];

        Assert.False(AlsCurveRuntime.TryBlendAdditiveToDefault(
            0f, 0f, 1f, bindings, keys, invalidZeroSamples,
            out var invalidValue, out var invalidFailure));
        AssertPositiveZero(invalidValue);
        Assert.Equal(AlsP5FailureCode.InvalidBinding, invalidFailure);

        Assert.True(AlsCurveRuntime.TryBlendAdditiveToDefault(
            2f, 0f, 1f, [], [], [], out var clampedValue, out var clampedFailure));
        AssertFloatBits(1f, clampedValue);
        Assert.Equal(AlsP5FailureCode.None, clampedFailure);
    }

    [Fact]
    public void AdditiveRejectsAccumulatedFloatOverflow()
    {
        AlsCurveKey[] keys = [Key(0f, float.MaxValue)];
        AlsCurveBinding[] bindings = [Present(1, 0, 1)];
        AlsCurveBlendSample[] samples = [new(0, 0, 0f, 2f)];

        Assert.False(AlsCurveRuntime.TryBlendAdditiveToDefault(
            0f, -float.MaxValue, float.MaxValue, bindings, keys, samples,
            out var value, out var failure));
        AssertPositiveZero(value);
        Assert.Equal(AlsP5FailureCode.NonFiniteOutput, failure);
    }

    private static AlsCurveKey Key(
        float time,
        float value,
        float arriveTangent = 0f,
        float leaveTangent = 0f,
        AlsCurveInterpolationMode interpolation = AlsCurveInterpolationMode.Linear) =>
        new(time, value, arriveTangent, leaveTangent, interpolation);

    private static AlsCurveBinding Present(
        int curveId,
        int keyOffset,
        int keyCount,
        float duration = 0f,
        byte loop = 0) =>
        new(curveId, keyOffset, keyCount, duration, 1, loop);

    private static AlsCurveBinding Missing(
        int keyOffset,
        float duration = 0f,
        byte loop = 0) =>
        new(-1, keyOffset, 0, duration, 0, loop);

    private static void AssertSample(
        in AlsCurveBinding binding,
        ReadOnlySpan<AlsCurveKey> keys,
        float time,
        float expected)
    {
        Assert.True(AlsCurveRuntime.TrySample(
            binding, keys, 0, time, out var value, out var failure));
        AssertFloatBits(expected, value);
        Assert.Equal(AlsP5FailureCode.None, failure);
    }

    private static void AssertContract<T>() where T : struct
    {
        Assert.Equal(LayoutKind.Sequential, typeof(T).StructLayoutAttribute?.Value);
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    private static void AssertPropertyOrder<T>(params string[] expected)
    {
        var actual = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(static property => property.MetadataToken)
            .Select(static property => property.Name);
        Assert.Equal(expected, actual);
    }

    private static void AssertStorageFieldOrder<T>(params (string Name, Type Type)[] expected)
    {
        var actual = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(static field => field.MetadataToken)
            .Select(static field => (NormalizeStorageFieldName(field.Name), field.FieldType));
        Assert.Equal(expected, actual);
    }

    private static string NormalizeStorageFieldName(string name)
    {
        const string suffix = ">k__BackingField";
        return name.Length > suffix.Length + 1 && name[0] == '<' && name.EndsWith(suffix, StringComparison.Ordinal)
            ? name[1..^suffix.Length]
            : name;
    }

    private static void AssertFloatBits(float expected, float actual) =>
        Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));

    private static void AssertPositiveZero(float value) =>
        Assert.Equal(0, BitConverter.SingleToInt32Bits(value));
}
