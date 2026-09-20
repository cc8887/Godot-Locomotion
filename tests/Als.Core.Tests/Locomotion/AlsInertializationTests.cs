using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsInertializationTests
{
    private static readonly string[] CurveNames = ["Both", "Outgoing", "Incoming"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeNodeTrajectoriesMatchAtAllRatesIncludingInterruptionsAndCurves(bool precise)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_inertialization_native.json")));
        var traces = fixture.RootElement.GetProperty("traces");
        Assert.Equal(15, traces.GetArrayLength());
        foreach (var trace in traces.EnumerateArray())
        {
            var runtime = new AlsInertialization(4, 3);
            var candidate = new AlsInertialization(4, 3);
            var output = new AlsLocalPose[4];
            var outputCurves = new AlsInertialCurve[3];
            var retry = new AlsLocalPose[4];
            var retryCurves = new AlsInertialCurve[3];
            var frame = 0;
            var correctedFrames = 0;
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var input = row.GetProperty("input").EnumerateArray().Select(Pose).ToArray();
                var curves = Curves(row.GetProperty("inputCurves"));
                var expected = row.GetProperty("output").EnumerateArray().Select(Pose).ToArray();
                var expectedCurves = Curves(row.GetProperty("outputCurves"));
                if (Vector3.Distance(input[0].Position, expected[0].Position) > .0001f) correctedFrames++;
                var component = Pose(row.GetProperty("component"));
                for (var update = 0; update < row.GetProperty("updates").GetInt32(); update++) runtime.Update(row.GetProperty("delta").GetSingle());
                foreach (var request in row.GetProperty("requests").EnumerateArray()) runtime.Request(request.GetSingle());
                candidate.CopyFrom(runtime);
                if (precise)
                {
                    var rotations = row.GetProperty("input").EnumerateArray().Select(PreciseRotation).ToArray();
                    var componentRotation = PreciseRotation(row.GetProperty("component"));
                    for (var bone = 0; bone < input.Length; bone++) input[bone] = input[bone] with { Rotation = rotations[bone].ToSingle() };
                    component = component with { Rotation = componentRotation.ToSingle() };
                    var preciseOutput = new AlsQuaternion[4]; var preciseRetry = new AlsQuaternion[4];
                    runtime.EvaluatePrecise(input, curves, component, 0, trace.GetProperty("teleportThreshold").GetSingle(), output, outputCurves, rotations, componentRotation, preciseOutput);
                    candidate.EvaluatePrecise(input, curves, component, 0, trace.GetProperty("teleportThreshold").GetSingle(), retry, retryCurves, rotations, componentRotation, preciseRetry);
                    Assert.Equal(preciseOutput, preciseRetry);
                }
                else
                {
                    runtime.Evaluate(input, curves, component, 0, trace.GetProperty("teleportThreshold").GetSingle(), output, outputCurves);
                    candidate.Evaluate(input, curves, component, 0, trace.GetProperty("teleportThreshold").GetSingle(), retry, retryCurves);
                }
                Assert.Equal(output, retry); Assert.Equal(outputCurves, retryCurves);
                var context = $"hz={trace.GetProperty("hz")} scenario={trace.GetProperty("scenario")} frame={frame}";
                for (var bone = 0; bone < 4; bone++) AssertPose(expected[bone], output[bone], context + $" bone={bone}");
                for (var curve = 0; curve < 3; curve++)
                {
                    Assert.True(expectedCurves[curve].Present == outputCurves[curve].Present, context + " curve presence " + CurveNames[curve]);
                    Assert.True(MathF.Abs(expectedCurves[curve].Value - outputCurves[curve].Value) < .00003f,
                        context + $" curve={CurveNames[curve]} expected={expectedCurves[curve].Value} actual={outputCurves[curve].Value}");
                }
                frame++;
            }
            Assert.Equal(60, frame);
            Assert.True(correctedFrames > 0, "Native trace must exercise inertial correction, not just source passthrough.");
        }
    }

    [Fact]
    public void DecayClampsAwayVelocityAndShortensExcessiveTowardVelocity()
    {
        Assert.Equal(1, AlsInertialDecay.Evaluate(1, 20, 0, .2f));
        Assert.Equal(AlsInertialDecay.Evaluate(1, 0, .1f, .2f), AlsInertialDecay.Evaluate(1, 20, .1f, .2f));
        Assert.Equal(0, AlsInertialDecay.Evaluate(1, -100, .06f, 1));
        Assert.Equal(-AlsInertialDecay.Evaluate(1, -2, .1f, .3f), AlsInertialDecay.Evaluate(-1, 2, .1f, .3f));
        Assert.Equal(0, AlsInertialDecay.Evaluate(1, 0, 0, 1e-8f));
        Assert.Equal(0, AlsInertialDecay.Evaluate(0, -20, 0, 1));
    }

    [Fact]
    public void NoHistoryDropsRequestAndFirstActiveFrameAlreadyAdvances()
    {
        var runtime = new AlsInertialization(1, 0);
        var output = new AlsLocalPose[1];
        runtime.Request(.2f); runtime.Update(.01f);
        runtime.Evaluate([AlsLocalPose.Identity], [], AlsLocalPose.Identity, 0, 0, output, []);
        Assert.False(runtime.IsActive);
        runtime.Request(.2f); runtime.Update(.01f);
        runtime.Evaluate([AlsLocalPose.Identity with { Position = Vector3.UnitX }], [], AlsLocalPose.Identity, 0, 0, output, []);
        Assert.True(runtime.IsActive); Assert.Equal(.01f, runtime.ElapsedSeconds);
        Assert.InRange(output[0].Position.X, 0.000001f, .1f);
    }

    [Fact]
    public void ContinuousInterruptionsUseDeficitRatherThanRestartingFullDuration()
    {
        var runtime = new AlsInertialization(1, 0);
        var output = new AlsLocalPose[1];
        runtime.Evaluate([AlsLocalPose.Identity], [], AlsLocalPose.Identity, 0, 0, output, []);
        runtime.Request(.2f); runtime.Update(.01f);
        runtime.Evaluate([AlsLocalPose.Identity], [], AlsLocalPose.Identity, 0, 0, output, []);
        runtime.Request(.2f); runtime.Update(.01f);
        runtime.Evaluate([AlsLocalPose.Identity], [], AlsLocalPose.Identity, 0, 0, output, []);
        Assert.Equal(.2f, runtime.DurationSeconds);
        Assert.InRange(runtime.DeficitSeconds, .17999f, .18001f);
        runtime.Request(.2f); runtime.Update(.005f);
        runtime.Evaluate([AlsLocalPose.Identity], [], AlsLocalPose.Identity, 0, 0, output, []);
        Assert.InRange(runtime.DurationSeconds, .00999f, .01001f);
    }

    [Fact]
    public void CandidateRollbackAndMeterUnitsMatchWithoutAllocation()
    {
        var centimeters = new AlsInertialization(1, 0);
        var meters = new AlsInertialization(1, 0, .01f);
        var candidate = new AlsInertialization(1, 0, .01f);
        var cmInput = new[] { AlsLocalPose.Identity };
        var meterInput = new[] { AlsLocalPose.Identity };
        var cm = new AlsLocalPose[1]; var output = new AlsLocalPose[1];
        for (var frame = 0; frame < 100; frame++)
        {
            cmInput[0] = cmInput[0] with { Position = new Vector3(frame < 3 ? frame : 20, 0, 0) };
            meterInput[0] = cmInput[0] with { Position = cmInput[0].Position * .01f };
            centimeters.Update(1f / 60); meters.Update(1f / 60);
            if (frame == 3) { centimeters.Request(.2f); meters.Request(.2f); }
            candidate.CopyFrom(meters);
            candidate.Evaluate(meterInput, [], AlsLocalPose.Identity, 0, 0, output, []);
            meters.CopyFrom(candidate);
            centimeters.Evaluate(cmInput, [], AlsLocalPose.Identity, 0, 0, cm, []);
            Assert.InRange(Vector3.Distance(cm[0].Position * .01f, output[0].Position), 0, .000001f);
        }
        for (var i = 0; i < 64; i++) Step();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Step();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Step()
        {
            candidate.CopyFrom(meters); candidate.Update(1f / 60); candidate.Request(.2f);
            candidate.Evaluate(meterInput, [], AlsLocalPose.Identity, 0, 0, output, []);
        }
    }

    [Fact]
    public void InvalidInputsDoNotConsumePendingRequestOrModifyOutputs()
    {
        var runtime = new AlsInertialization(1, 0);
        var output = new[] { AlsLocalPose.Identity };
        runtime.Evaluate(output, [], AlsLocalPose.Identity, 0, 0, output, []);
        runtime.Request(.2f); runtime.Update(.01f);
        Assert.Throws<ArgumentException>(() => runtime.Evaluate([AlsLocalPose.Identity with { Position = new(float.NaN, 0, 0) }], [],
            AlsLocalPose.Identity, 0, 0, output, []));
        Assert.Equal(AlsLocalPose.Identity, output[0]);
        runtime.Evaluate([AlsLocalPose.Identity], [], AlsLocalPose.Identity, 0, 0, output, []);
        Assert.True(runtime.IsActive); Assert.Equal(.01f, runtime.ElapsedSeconds);
        runtime.Reset(); Assert.False(runtime.IsActive); Assert.Equal(0, runtime.HistoryCount);
    }

    [Fact]
    public void TeleportCancelsPendingButNotAnAlreadyActiveBlend()
    {
        var runtime = new AlsInertialization(1, 0);
        var output = new AlsLocalPose[1];
        runtime.Update(.01f); runtime.Evaluate([AlsLocalPose.Identity], [], AlsLocalPose.Identity, 0, 10, output, []);
        runtime.Request(.2f); runtime.Update(.01f);
        var moved = AlsLocalPose.Identity with { Position = new(20, 0, 0) };
        runtime.Evaluate([AlsLocalPose.Identity], [], moved, 0, 10, output, []);
        Assert.False(runtime.IsActive);
        runtime.Request(.2f); runtime.Update(.01f);
        runtime.Evaluate([AlsLocalPose.Identity], [], moved, 0, 10, output, []);
        Assert.True(runtime.IsActive);
        runtime.Update(.01f);
        runtime.Evaluate([AlsLocalPose.Identity], [], AlsLocalPose.Identity, 0, 10, output, []);
        Assert.True(runtime.IsActive);
    }

    [Fact]
    public void ParentChangeRebasesOnlyRootAndZeroDeltaDoesNotAdvanceBlend()
    {
        var runtime = new AlsInertialization(2, 0);
        var output = new AlsLocalPose[2];
        var component = AlsLocalPose.Identity with { Position = new(5, 0, 0) };
        runtime.Update(.01f); runtime.Evaluate([AlsLocalPose.Identity, AlsLocalPose.Identity], [], component, 1, 0, output, []);
        runtime.Request(.2f);
        runtime.Evaluate([AlsLocalPose.Identity, AlsLocalPose.Identity], [], AlsLocalPose.Identity, 2, 0, output, []);
        Assert.Equal(new Vector3(5, 0, 0), output[0].Position);
        Assert.Equal(Vector3.Zero, output[1].Position);
        Assert.Equal(0, runtime.ElapsedSeconds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RejectsInvalidRequestAndUpdate(float value)
    {
        var runtime = new AlsInertialization(1, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Update(value));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Request(value));
    }

    private static AlsInertialCurve[] Curves(JsonElement row) => CurveNames.Select(name => row.TryGetProperty(name, out var value)
        ? new AlsInertialCurve(value.GetSingle()) : default).ToArray();
    private static AlsLocalPose Pose(JsonElement row)
    {
        var p = row.GetProperty("position"); var r = row.GetProperty("rotation"); var s = row.GetProperty("scale");
        return new(new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
            new(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()), new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
    private static AlsQuaternion PreciseRotation(JsonElement row)
    {
        var q = row.GetProperty("rotation");
        return new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble());
    }
    private static void AssertPose(AlsLocalPose expected, AlsLocalPose actual, string context)
    {
        Assert.True(Vector3.Distance(expected.Position, actual.Position) < .00003f, context + " position");
        Assert.True(Vector3.Distance(expected.Scale, actual.Scale) < .00003f, context + " scale");
        var error = MathF.Min((expected.Rotation - actual.Rotation).Length(), (expected.Rotation + actual.Rotation).Length());
        Assert.True(error < .0001f, context + $" rotation error={error}");
    }
}
