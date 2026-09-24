using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using GodotAls.Import.Runtime;

namespace GodotAls.Import.Tests;

public sealed class AlsCameraGraphRuntimeTests
{
    private static AlsCameraGraphDefinition Graph() => AlsCameraGraphCompiler.Compile(File.ReadAllText(
        Path.Combine(RepositoryRoot.Find(), "assets", "config", "refactored_camera_inputs.json")));
    private static void Step(AlsCameraGraphRuntime runtime, AlsCameraGraphInput input, float delta)
    { runtime.Prepare(runtime.CommittedFrame + 1, delta, input); runtime.Commit(); }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void InterruptedGraphSequenceRetriesExactlyAndCachesUpdateOnce(int hz)
    {
        var graph = Graph(); var clean = new AlsCameraGraphRuntime(graph); var retry = new AlsCameraGraphRuntime(graph);
        var input = AlsCameraGraphInput.Default;
        for (var frame = 1; frame <= hz * 9; frame++)
        {
            var time = (float)frame / hz;
            input = input with
            {
                RotationMode = time < 1 ? "Als.RotationMode.ViewDirection" : time < 1.2f ? "Als.RotationMode.Aiming" :
                    time < 1.4f ? "Als.RotationMode.VelocityDirection" : "Als.RotationMode.Aiming",
                Gait = frame % 37 < 17 ? "Als.Gait.Walking" : "Als.Gait.Sprinting",
                Stance = time > 2 && time < 4 ? "Als.Stance.Crouching" : "Als.Stance.Standing",
                RightShoulder = time < 1.1f || time > 3,
                ViewMode = time > 2.1f && time < 3 ? "Als.ViewMode.FirstPerson" : "Als.ViewMode.ThirdPerson",
                LocomotionAction = time > 4 && time < 6.5f ? "Als.LocomotionAction.Ragdolling" :
                    time > 1.5f && time < 1.8f ? "Als.LocomotionAction.Rolling" : ""
            };
            Step(clean, input, 1f / hz);
            var candidate = retry.Prepare(frame, 1f / hz, input).ToArray();
            Assert.Equal(frame - 1, retry.CommittedFrame);
            Assert.All(retry.PendingUpdateCounts.Values, count => Assert.Equal(1, count));
            retry.Discard();
            var repeated = retry.Prepare(frame, 1f / hz, input).ToArray();
            Assert.Equal(candidate, repeated); retry.Commit();
            Assert.Equal(clean.Curves.OrderBy(p => p.Key), retry.Curves.OrderBy(p => p.Key));
            Assert.All(clean.Curves.Values, value => Assert.True(float.IsFinite(value)));
            if (time > 5.5f && time < 6.4f)
            {
                Assert.Equal(-320, clean.Curves["CameraOffsetX"]);
                Assert.Equal(0, clean.Curves["CameraOffsetY"]);
                Assert.DoesNotContain(retry.PendingUpdateCounts.Keys, id => id.EndsWith("AnimGraphNode_StateMachine_0", StringComparison.Ordinal));
            }
        }
        Assert.Equal(-200, clean.Curves["CameraOffsetX"]);
        Assert.Equal(.015f, clean.Curves["RotationLag"]);
        Assert.Equal(0, clean.Curves["FirstPersonOverride"]);
    }

    [Fact]
    public void FirstUpdateSelectsRequestedStateAndPendingFramesCannotLeak()
    {
        var runtime = new AlsCameraGraphRuntime(Graph());
        var candidate = runtime.Prepare(1, 1f / 60, AlsCameraGraphInput.Default);
        Assert.Equal(-280, candidate["CameraOffsetX"]); Assert.Equal(70, candidate["CameraOffsetY"]);
        Assert.Equal(40, candidate["PivotOffsetZ"]);
        Assert.Empty(runtime.Curves);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(1, .1f, AlsCameraGraphInput.Default));
        runtime.Discard(); Assert.Equal(0, runtime.CommittedFrame);
        Assert.Throws<ArgumentException>(() => runtime.Prepare(2, .1f, AlsCameraGraphInput.Default));
        Assert.Throws<ArgumentException>(() => runtime.Prepare(1, float.NaN, AlsCameraGraphInput.Default));
        Step(runtime, AlsCameraGraphInput.Default with { RotationMode = "" }, 1f / 60);
        Assert.Equal(-325, runtime.Curves["CameraOffsetX"]);
        Assert.Equal(1, runtime.Curves["TraceOverride"]);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ReturningFromExclusiveRagdollReinitializesLookStateBeforeActionBlend(int hz)
    {
        var runtime = new AlsCameraGraphRuntime(Graph()); var input = AlsCameraGraphInput.Default;
        Step(runtime, input, 1f / hz);
        for (var i = 0; i < hz * 2; i++) Step(runtime, input with { LocomotionAction = "Als.LocomotionAction.Ragdolling" }, 1f / hz);
        Assert.Equal(-320, runtime.Curves["CameraOffsetX"]);
        Step(runtime, input with { RotationMode = "Als.RotationMode.Aiming" }, 1f / hz);
        var progress = (1f / hz) / .4f;
        var alpha = -2 * progress * progress * progress + 3 * progress * progress;
        // The reactivated Look States machine snaps to Aiming on its first
        // update. Only the outer 0.4s action blend remains in flight.
        Assert.InRange(MathF.Abs(runtime.Curves["CameraOffsetX"] - (-320 + 120 * alpha)), 0, .0001f);
        Assert.InRange(MathF.Abs(runtime.Curves["RotationLag"] - (.05f + (.015f - .05f) * alpha)), 0, .000001f);
    }

    [Fact]
    public void FirstPersonTransitionUsesReflectedHermiteDefault()
    {
        var runtime = new AlsCameraGraphRuntime(Graph());
        Step(runtime, AlsCameraGraphInput.Default, 1f / 60);
        Step(runtime, AlsCameraGraphInput.Default with { ViewMode = "Als.ViewMode.FirstPerson" }, .025f);
        Assert.Equal(.15625f, runtime.Curves["FirstPersonOverride"]);
    }

    [Fact]
    public void RepeatedEveryFrameLookInterruptionsKeepCurveOwnershipAndTimeValid()
    {
        var runtime = new AlsCameraGraphRuntime(Graph());
        string[] modes = ["Als.RotationMode.ViewDirection", "Als.RotationMode.Aiming", "Als.RotationMode.VelocityDirection"];
        for (var frame = 0; frame < 480; frame++)
        {
            Step(runtime, AlsCameraGraphInput.Default with { RotationMode = modes[frame % modes.Length],
                RightShoulder = frame % 17 < 8, ViewMode = frame % 23 < 10 ? "Als.ViewMode.FirstPerson" : "Als.ViewMode.ThirdPerson" }, 1f / 120);
            Assert.All(runtime.Curves.Values, value => Assert.True(float.IsFinite(value)));
            Assert.InRange(runtime.Curves["FirstPersonOverride"], 0, 1);
            Assert.All(runtime.PendingUpdateCounts.Values, count => Assert.Equal(1, count));
        }
    }
}
