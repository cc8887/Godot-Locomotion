using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using Xunit.Abstractions;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsFullPrecisionInertializationTests(ITestOutputHelper outputLog)
{
    [Fact]
    public void ExistingNativeTrajectoriesAlsoMatchWithoutPoseProjection()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_inertialization_native.json")));
        double maxP = 0, maxQ = 0, maxS = 0; float maxCurve = 0; var count = 0;
        foreach (var trace in fixture.RootElement.GetProperty("traces").EnumerateArray())
        {
            var node = new AlsInertialization(4, 3); var result = new AlsPrecisePose[4]; var values = new AlsInertialCurve[3];
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var input = row.GetProperty("input").EnumerateArray().Select(Pose).ToArray();
                var expected = row.GetProperty("output").EnumerateArray().Select(Pose).ToArray();
                for (var u = 0; u < row.GetProperty("updates").GetInt32(); u++) node.Update(row.GetProperty("delta").GetSingle());
                foreach (var request in row.GetProperty("requests").EnumerateArray()) node.Request(request.GetSingle());
                node.EvaluatePrecisePose(input, Curves(row.GetProperty("inputCurves")), Pose(row.GetProperty("component")), 0,
                    trace.GetProperty("teleportThreshold").GetSingle(), result, values);
                for (var b = 0; b < 4; b++)
                {
                    var p = System.Math.Sqrt((result[b].Position - expected[b].Position).LengthSquared);
                    var s = System.Math.Sqrt((result[b].Scale - expected[b].Scale).LengthSquared);
                    var q = result[b].Rotation; if (AlsQuaternion.Dot(q, expected[b].Rotation) < 0) q = -q;
                    var difference = q + -expected[b].Rotation; var r = System.Math.Sqrt(difference.LengthSquared);
                    maxP = System.Math.Max(maxP, p); maxS = System.Math.Max(maxS, s); maxQ = System.Math.Max(maxQ, r);
                    Assert.True(p < .00003 && s < .00003 && r < .0001, $"native frame={count} bone={b} P={p} S={s} Q={r}");
                }
                var curves = Curves(row.GetProperty("outputCurves"));
                for (var c = 0; c < 3; c++)
                {
                    Assert.Equal(curves[c].Present, values[c].Present); var error = MathF.Abs(curves[c].Value - values[c].Value);
                    maxCurve = MathF.Max(maxCurve, error); Assert.True(error < .00003f);
                }
                count++;
            }
        }
        Assert.Equal(900, count); outputLog.WriteLine($"Native full-pose frames={count} P={maxP:R} Q={maxQ:R} S={maxS:R} curve={maxCurve:R}");
        static AlsInertialCurve[] Curves(JsonElement row) => new[] { "Both", "Outgoing", "Incoming" }
            .Select(n => row.TryGetProperty(n, out var v) ? new AlsInertialCurve(v.GetSingle()) : default).ToArray();
        static AlsPrecisePose Pose(JsonElement row)
        {
            var p = row.GetProperty("position"); var q = row.GetProperty("rotation"); var s = row.GetProperty("scale");
            return new(new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()),
                new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()), new(s[0].GetDouble(), s[1].GetDouble(), s[2].GetDouble()));
        }
    }
    [Fact]
    public void InactivePassThroughDoesNotRoundTranslationOrScaleHistory()
    {
        var node = new AlsInertialization(1, 0);
        AlsPrecisePose[] input = [new(new(1000000000.125, -.000000000013, 37.123456789), AlsQuaternion.Identity, new(1.000000013, 1, 1))];
        var output = new AlsPrecisePose[1];
        for (var frame = 0; frame < 4; frame++)
        {
            node.Update(.01f); node.EvaluatePrecisePose(input, [], AlsPrecisePose.Identity, 0, 0, output, []);
            Assert.Equal(input, output); Assert.False(node.IsActive);
        }
    }
    [Fact]
    public void NativeFloatDifferencesAreAppliedToDoubleTransforms()
    {
        var node = new AlsInertialization(1, 0); var output = new AlsPrecisePose[1];
        var basis = new AlsPrecisePose(new(1000000000.125, 0, 0), AlsQuaternion.Identity, new(1.000000013, 1, 1));
        for (var i = 0; i < 2; i++) { node.Update(.01f); node.EvaluatePrecisePose([basis], [], AlsPrecisePose.Identity, 0, 0, output, []); }
        var target = basis with { Position = basis.Position + new AlsDoubleVector(.25, 0, 0), Scale = basis.Scale + new AlsDoubleVector(.125, 0, 0) };
        node.Update(.01f); node.Request(.5f); node.EvaluatePrecisePose([target], [], AlsPrecisePose.Identity, 0, 0, output, []);
        Assert.True(node.IsActive);
        Assert.Equal(target.Position.X - AlsInertialDecay.Evaluate(.25f, 0, .01f, .5f), output[0].Position.X);
        Assert.Equal(target.Scale.X - AlsInertialDecay.Evaluate(.125f, 0, .01f, .5f), output[0].Scale.X);
        Assert.NotEqual((double)(float)output[0].Position.X, output[0].Position.X);
    }
    [Fact]
    public void CopyRetryInterruptionsAttachmentAndTeleportKeepAllPreciseHistory()
    {
        var node = new AlsInertialization(2, 1); var retry = new AlsInertialization(2, 1);
        var a = new AlsPrecisePose[2]; var b = new AlsPrecisePose[2]; var ac = new AlsInertialCurve[1]; var bc = new AlsInertialCurve[1];
        for (var frame = 0; frame < 400; frame++)
        {
            var q = AlsQuaternion.FromAxisAngle(Vector3.UnitZ, frame % 17 * .01f);
            var p = new AlsPrecisePose(new(100.000000017 + frame % 13, 0, 0), q, new(1.0000000013 + frame % 9 * .01, 1, 1));
            var component = AlsPrecisePose.Identity with { Position = new(frame % 11 * 50, 0, 0), Rotation = q };
            node.Update(frame % 19 == 0 ? 0 : .01f); if (frame % 3 == 0) node.Request(.3f);
            retry.CopyFrom(node);
            node.EvaluatePrecisePose([p, p], [new(.31f)], component, frame % 5, 100, a, ac);
            retry.EvaluatePrecisePose([p, p], [new(.31f)], component, frame % 5, 100, b, bc);
            Assert.Equal(a, b); Assert.Equal(ac, bc); Assert.Equal(node.IsActive, retry.IsActive);
            Assert.Equal(node.DeficitSeconds, retry.DeficitSeconds); foreach (var pose in a) pose.Validate();
        }
        var before = a.ToArray();
        Assert.Throws<ArgumentException>(() => node.EvaluatePrecisePose([AlsPrecisePose.Identity], [], AlsPrecisePose.Identity, 0, 0, a, ac));
        Assert.Equal(before, a);
        Assert.Throws<InvalidOperationException>(() => node.Evaluate([AlsLocalPose.Identity, AlsLocalPose.Identity], ac, AlsLocalPose.Identity, 0, 0, new AlsLocalPose[2], ac));
        node.Reset(); node.Evaluate([AlsLocalPose.Identity, AlsLocalPose.Identity], ac, AlsLocalPose.Identity, 0, 0, new AlsLocalPose[2], ac);
    }
}
