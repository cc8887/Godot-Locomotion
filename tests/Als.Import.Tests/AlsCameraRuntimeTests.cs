using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Inspection;
using GodotAls.Import.Runtime;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsCameraRuntimeTests
{
    [Theory]
    [InlineData(17, 0, false, 110)]
    [InlineData(17, .5f, false, 100)]
    [InlineData(17, .5f, true, 100)]
    [InlineData(500, 0, false, 175)]
    [InlineData(-500, 0, false, 5)]
    [InlineData(500, 1, false, 70)]
    [InlineData(500, 1, true, 80)]
    public void GraphFovOffsetReachesComponentExceptFullFirstPerson(float offset, float firstPerson, bool overwrite, float expected)
    {
        var definition = AlsCameraRigDefinition.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "refactored_camera_inputs.json")));
        // Explicit fixture: current imported graph does not author nonzero FovOffset.
        // Wrap it in the same supported ModifyCurve node, without editing source assets.
        var root = new AlsCameraModifyCurves("fov-fixture", definition.Graph.Root, false,
            new Dictionary<string, float> { ["FovOffset"] = offset, ["FirstPersonOverride"] = firstPerson });
        definition = definition with { Graph = definition.Graph with { Root = root },
            Follow = definition.Follow with { FirstPersonFov = 70, ThirdPersonFov = 90 } };
        var runtime = new AlsCameraRuntime(definition);
        var scene = new AlsCameraFollowInput(1f / 60, true, default, default, new(0, 0, 180),
            new(0, 0, 170), new(0, 30, 140), false, default, AlsQuaternion.Identity, 1,
            0, "", false, default, AlsQuaternion.Identity, overwrite, 80, 3);
        var candidate = runtime.Prepare(1, AlsCameraGraphInput.Default, scene, q => new(q.Start, q.End));
        Assert.Equal(expected, candidate.Fov);
        Assert.Equal(AlsCameraFollowState.Initial, runtime.State);
        runtime.Discard();
        Assert.Equal(candidate, runtime.Prepare(1, AlsCameraGraphInput.Default, scene, q => new(q.Start, q.End)));
        runtime.Commit(); Assert.Equal(offset, runtime.Curves["FovOffset"]);
        Assert.Equal(expected, runtime.State.Fov);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void SceneQueryFailuresDoNotAdvanceGraphOrSpatialHistory(int hz)
    {
        var definition = AlsCameraRigDefinition.Compile(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets", "config", "refactored_camera_inputs.json")));
        Assert.Equal("root", definition.FirstPivotSocket); Assert.Equal("head", definition.SecondPivotSocket);
        Assert.Equal("FirstPersonCamera", definition.FirstPersonSocket);
        Assert.Equal(3, definition.NativeTraceChannel); Assert.True(definition.IgnoreTimeDilation);
        var runtime = new AlsCameraRuntime(definition); var clean = new AlsCameraRuntime(definition);
        for (var frame = 1; frame <= hz * 3; frame++)
        {
            var graph = AlsCameraGraphInput.Default with { RotationMode = frame < hz ? "Als.RotationMode.ViewDirection" : "Als.RotationMode.Aiming",
                RightShoulder = frame % 19 < 9 };
            var scene = new AlsCameraFollowInput(1f / hz, true, new(10, frame, 0), new(frame, 0, 0), new(frame, 0, 180),
                new(frame, 0, 170), new(frame, 30, 140), false, default, AlsQuaternion.Identity, 1,
                0, "", false, default, AlsQuaternion.Identity, false, 90, 0);
            var old = runtime.State;
            Assert.Throws<InvalidOperationException>(() => runtime.Prepare(frame, graph, scene,
                _ => throw new InvalidOperationException("Injected query failure")));
            Assert.Equal(frame - 1, runtime.CommittedFrame); Assert.Equal(old, runtime.State);
            var expected = clean.Prepare(frame, graph, scene, Miss); clean.Commit();
            var candidate = runtime.Prepare(frame, graph, scene, Miss);
            Assert.Equal(expected, candidate); Assert.Equal(old, runtime.State);
            Assert.Throws<InvalidOperationException>(() => runtime.Prepare(frame, graph, scene, Miss));
            runtime.Discard(); Assert.Equal(candidate, runtime.Prepare(frame, graph, scene, Miss)); runtime.Commit();
            Assert.Equal(clean.State, runtime.State);
            Assert.Equal(clean.Curves.OrderBy(p => p.Key), runtime.Curves.OrderBy(p => p.Key));
        }
        static AlsCameraTraceResponse Miss(AlsCameraTraceRequest q) => new(q.Start, q.End);
    }
}
