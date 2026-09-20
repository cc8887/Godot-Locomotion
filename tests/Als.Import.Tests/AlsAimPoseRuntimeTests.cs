using System.Numerics;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Tests;

public sealed class AlsAimPoseRuntimeTests
{
    private static readonly Lazy<AlsAimPoseDefinition> Definition = new(AlsAimPoseCompilerTests.Model);
    private static AlsAimPoseDefinition Graph => Definition.Value;
    private static AlsAimingInputState Input(int frame, uint owner = 1) => new(new(frame, owner, 1), default, default,
        new(Math.Sin(frame * .16) * 179, Math.Cos(frame * .09) * 89), default, .5, frame % 101 / 100.0,
        .5 - Math.Abs(Math.Sin(frame * .16)) / 2, .5 + Math.Abs(Math.Sin(frame * .16)) / 2, .5 + Math.Sin(frame * .16) / 2);

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void OrderedNestedEvaluationRetainsHeadProfileCurveUnionAndOneSamplePerState(int hz)
    {
        var history = new AlsAimFrameRuntime(Graph, 1, 1); var source = new Source();
        var runtime = new AlsAimPoseRuntime(Graph, source, 3, 1, 1);
        var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[3];
        var visited = 0; var stacked = 0; var profile = 0;
        var weights = new[] { new AlsAimStateMachine(Graph, AlsAimMachineKind.Behavior),
            new AlsAimStateMachine(Graph, AlsAimMachineKind.Input), new AlsAimStateMachine(Graph, AlsAimMachineKind.Camera) };
        for (var frame = 1; frame <= hz * 12; frame++)
        {
            var input = Input(frame);
            // Abrupt back-left/back-right crossing after a fully weighted hold
            // exercises SwitchSidesBlendPose; a sine crosses Looking Forwards.
            if (frame > hz * 8) input = input with { SmoothedAngle = new(frame / hz % 2 == 0 ? -170 : 170, 35) };
            history.Prepare(input, frame > hz * 8 ? AlsRotationMode.Aiming : (AlsRotationMode)(frame / 27 % 3), frame % 22 < 11, 1f / hz, frame, true);
            source.Calls.Clear(); runtime.Evaluate(history.Candidate, pose, curves);
            visited |= runtime.EvaluatedSourceMask;
            Assert.All(source.Calls.Values, count => Assert.Equal(1, count));
            // Translation is linear across nested machines. Check it independently
            // from the cache/ordered pose traversal using joint leaf contributions.
            for (var bone = 0; bone < pose.Length; bone++)
            {
                var expected = Vector3.Zero; var total = 0f;
                for (var evaluator = 0; evaluator < 7; evaluator++)
                {
                    var leaf = Graph.Evaluators[evaluator]; var parent = leaf.Machine - 1;
                    var parentWeight = weights[0].BoneStateWeight(history.Candidate.GetMachine(AlsAimMachineKind.Behavior), parent, bone);
                    if (parentWeight == 0) continue;
                    var weight = parentWeight * weights[leaf.Machine].BoneStateWeight(
                        history.Candidate.GetMachine((AlsAimMachineKind)leaf.Machine), leaf.State, bone);
                    expected += new Vector3(evaluator, history.Candidate.GetEvaluator(evaluator).Input.Time, bone * .01f) * weight;
                    total += weight;
                }
                Assert.InRange(MathF.Abs(total - 1), 0, .00001f);
                Assert.True(Vector3.Distance(expected, pose[bone].Position) < .00001f);
                Assert.InRange(MathF.Abs(pose[bone].Rotation.LengthSquared() - 1), 0, .00001f);
            }
            Assert.False(curves[2].Present);
            for (var m = 0; m < 3; m++)
            {
                var machine = history.Candidate.GetMachine((AlsAimMachineKind)m);
                if (machine.LastUpdateSerial != frame) continue;
                if (machine.Transitions.Count > 1) stacked++;
                for (var i = 0; i < machine.Transitions.Count; i++)
                    if (Graph.Machines[m].Edges[machine.GetActiveEdge(i)].HeadProfile) profile++;
            }
            history.Commit(Input(frame).Identity);
        }
        Assert.Equal(127, visited); Assert.True(stacked > 0); Assert.True(profile > 0);
    }

    [Fact]
    public void ZeroWeightTargetStillSamplesButDoesNotIntroduceItsCurveNames()
    {
        var history = new AlsAimFrameRuntime(Graph, 1, 1); var source = new Source();
        var runtime = new AlsAimPoseRuntime(Graph, source, 3, 1, 1);
        var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[3];
        history.Prepare(Input(1), AlsRotationMode.VelocityDirection, false, .01f, 1, true);
        runtime.Evaluate(history.Candidate, pose, curves); history.Commit(Input(1).Identity);
        history.Prepare(Input(2), AlsRotationMode.VelocityDirection, true, 0, 2, true);
        source.Calls.Clear(); runtime.Evaluate(history.Candidate, pose, curves);
        Assert.Equal(0, history.Candidate.GetMachine(AlsAimMachineKind.Input).Transitions.Latest.Alpha);
        Assert.True(source.Calls.ContainsKey(1)); Assert.True(curves[0].Present); Assert.False(curves[1].Present);
    }

    [Fact]
    public void LateSourceFailureDoesNotPublishOrCommitAndRetryDiscardsPartialCaches()
    {
        var history = new AlsAimFrameRuntime(Graph, 1, 1); var source = new Source();
        var runtime = new AlsAimPoseRuntime(Graph, source, 3, 1, 1);
        var output = new AlsLocalPose[79]; var curves = new AlsInertialCurve[3];
        history.Prepare(Input(1), AlsRotationMode.VelocityDirection, true, .01f, 1, true);
        runtime.Evaluate(history.Candidate, output, curves); history.Commit(Input(1).Identity);
        history.Prepare(Input(2), AlsRotationMode.Aiming, true, .01f, 2, true);
        var saved = output.ToArray(); var savedCurves = curves.ToArray(); source.FailOnCall = 2; source.TotalCalls = 0;
        Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(history.Candidate, output, curves));
        Assert.Equal(saved, output); Assert.Equal(savedCurves, curves); Assert.Equal(Input(1).Identity, history.Committed.Identity);
        source.FailOnCall = 0; source.Calls.Clear(); runtime.Evaluate(history.Candidate, output, curves);
        var freshPose = new AlsLocalPose[79]; var freshCurves = new AlsInertialCurve[3];
        new AlsAimPoseRuntime(Graph, new Source(), 3, 1, 1).Evaluate(history.Candidate, freshPose, freshCurves);
        Assert.Equal(freshPose, output); Assert.Equal(freshCurves, curves);
        Assert.All(source.Calls.Values, count => Assert.Equal(1, count));
        history.Cancel(); history.Prepare(Input(2), AlsRotationMode.Aiming, true, .01f, 2, true);
        var retry = new AlsLocalPose[79]; runtime.Evaluate(history.Candidate, retry, curves); Assert.Equal(output, retry);
        history.Commit(Input(2).Identity);
    }

    [Fact]
    public void RejectsHiddenForeignLayoutsAndConcurrentScratchUse()
    {
        var history = new AlsAimFrameRuntime(Graph, 1, 1); var source = new Source();
        var runtime = new AlsAimPoseRuntime(Graph, source, 3, 1, 1); var output = new AlsLocalPose[79]; var curves = new AlsInertialCurve[3];
        history.Prepare(Input(1), AlsRotationMode.Aiming, false, .1f, 1, false);
        Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(history.Candidate, output, curves)); history.Cancel();
        history.Prepare(Input(1), AlsRotationMode.Aiming, false, .1f, 1, true);
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(history.Candidate, output.AsSpan(0, 68), curves));
        var other = new AlsAimPoseRuntime(Graph, source, 3, 2, 1);
        Assert.Throws<ArgumentException>(() => other.Evaluate(history.Candidate, output, curves));
        source.Reenter = () => Assert.Throws<InvalidOperationException>(() => runtime.Evaluate(history.Candidate, output, curves));
        runtime.Evaluate(history.Candidate, output, curves);
    }

    private sealed class Source : IAlsAimPoseSource
    {
        public readonly Dictionary<int, int> Calls = new();
        public int TotalCalls, FailOnCall;
        public Action? Reenter;
        public void Sample(int evaluator, in AlsAimEvaluatorInput input, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
        {
            Calls[evaluator] = Calls.GetValueOrDefault(evaluator) + 1;
            TotalCalls++; if (TotalCalls == FailOnCall) throw new InvalidOperationException("Late source failure");
            Reenter?.Invoke(); Fill(evaluator, input, pose, curves);
        }
    }

    private static void Fill(int evaluator, in AlsAimEvaluatorInput input, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        for (var bone = 0; bone < pose.Length; bone++) pose[bone] = new(new(evaluator, input.Time, bone * .01f),
            Quaternion.CreateFromYawPitchRoll(evaluator * .7f + input.Time, bone * .04f + input.Position * .01f, evaluator * .4f), Vector3.Zero);
        curves.Clear(); curves[evaluator % 2] = new(evaluator + input.Time);
    }

}
