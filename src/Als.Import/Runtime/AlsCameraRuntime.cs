using System.Text.Json;
using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Runtime;

public sealed record AlsCameraRigDefinition(AlsCameraGraphDefinition Graph, AlsCameraFollowSettings Follow,
    string FirstPivotSocket, string SecondPivotSocket, string FirstPersonSocket, string LeftShoulderSocket,
    string RightShoulderSocket, int NativeTraceChannel, bool IgnoreTimeDilation)
{
    public static AlsCameraRigDefinition Compile(string json)
    {
        var graph = AlsCameraGraphCompiler.Compile(json);
        using var doc = JsonDocument.Parse(json);
        var settings = doc.RootElement.GetProperty("settings");
        var first = settings.GetProperty("firstPerson"); var third = settings.GetProperty("thirdPerson");
        var offset = third.GetProperty("traceOverrideOffset");
        var follow = new AlsCameraFollowSettings(settings.GetProperty("teleportDistanceThreshold").GetSingle(),
            first.GetProperty("fieldOfView").GetSingle(), third.GetProperty("fieldOfView").GetSingle(),
            third.GetProperty("traceRadius").GetSingle(), new(offset[0].GetDouble(), offset[1].GetDouble(), offset[2].GetDouble()),
            third.GetProperty("enableTraceDistanceSmoothing").GetBoolean(), third.GetProperty("traceDistanceSmoothingHalfLife").GetSingle());
        return new(graph, follow, Name(third, "firstPivotSocketName"), Name(third, "secondPivotSocketName"),
            Name(first, "cameraSocketName"), Name(third, "traceShoulderLeftSocketName"), Name(third, "traceShoulderRightSocketName"),
            third.GetProperty("traceChannel").GetInt32(), settings.GetProperty("ignoreTimeDilation").GetBoolean());
        static string Name(JsonElement owner, string field) => owner.GetProperty(field).GetString() is { Length: > 0 } value
            ? value : throw new ArgumentException("Missing camera socket name.");
    }
}

// One exclusive owner for both graph and spatial history. Host queries must be
// read-only. NativeTraceChannel is a UE enum and must NOT be used as a Godot mask.
public sealed class AlsCameraRuntime(AlsCameraRigDefinition definition)
{
    private readonly AlsCameraGraphRuntime _graph = new(definition.Graph);
    private AlsCameraFollowState? _pending;
    public AlsCameraFollowState State { get; private set; } = AlsCameraFollowState.Initial;
    public long CommittedFrame => _graph.CommittedFrame;
    public IReadOnlyDictionary<string, float> Curves => _graph.Curves;

    public AlsCameraFollowState Prepare(long frame, AlsCameraGraphInput graphInput, AlsCameraFollowInput sceneInput,
        Func<AlsCameraTraceRequest, AlsCameraTraceResponse> trace)
    {
        if (_pending.HasValue) throw new InvalidOperationException("Camera candidate already pending.");
        try
        {
            var curves = _graph.Prepare(frame, sceneInput.Delta, graphInput);
            float Value(string name) => curves.GetValueOrDefault(name);
            AlsDoubleVector Vector(string prefix) => new(Value(prefix + "X"), Value(prefix + "Y"), Value(prefix + "Z"));
            var values = new AlsCameraFollowCurves(Vector("PivotOffset"), Vector("CameraOffset"),
                Value("LocationLagX"), Value("LocationLagY"), Value("LocationLagZ"), Value("RotationLag"),
                Value("TraceOverride"), Value("FirstPersonOverride"));
            // Native CalculateFovOffset reads the evaluated graph, after the
            // first/third-person blend and explicit FOV override. Follow retains
            // the full-first-person early return and final 5..175 clamp.
            var followInput = sceneInput with { FovOffset = sceneInput.FovOffset + Value("FovOffset") };
            var candidate = AlsCameraFollow.Step(State, followInput, definition.Follow, values, trace);
            _pending = candidate; return candidate;
        }
        catch { _graph.Discard(); throw; }
    }
    public void Commit()
    {
        if (!_pending.HasValue) throw new InvalidOperationException("No complete camera candidate.");
        _graph.Commit(); State = _pending.Value; _pending = null;
    }
    public void Discard() { _graph.Discard(); _pending = null; }
}
