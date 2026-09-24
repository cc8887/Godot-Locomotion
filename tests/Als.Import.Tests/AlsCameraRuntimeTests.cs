using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Inspection;
using GodotAls.Import.Runtime;

namespace GodotAls.Import.Tests;

public sealed class AlsCameraRuntimeTests
{
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
