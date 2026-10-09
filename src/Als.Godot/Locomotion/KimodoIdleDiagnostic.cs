using Godot;
using GodotAls.Dispatch;
using System.Text.Json;

namespace GodotAls.Locomotion;

// Observes the ordinary committed Demo output without injecting animation poses.
public partial class KimodoIdleDiagnostic : Node
{
    private AlsDemoEntry _entry = null!;
    private int _tick;
    private bool _done;
    private readonly List<object> _frames = [];
    private string? _captureDirectory;
    private int _captureTick;
    public override void _Ready()
    {
        try
        {
            Engine.PhysicsTicksPerSecond = 60;
            _entry = new AlsDemoEntry { ConfigureBeforeReady = demo => demo.ConfigureRuntimePolicyForSmoke(AlsHarnessMode.Single, true) };
            AddChild(_entry);
            if (!_entry.Demo.IsRuntimeReady) throw new Exception("Demo initialization failed.");
            _captureDirectory = System.Environment.GetEnvironmentVariable("KIMODO_IDLE_CAPTURE_DIR");
            if (_captureDirectory is not null)
            {
                System.IO.Directory.CreateDirectory(_captureDirectory);
                RenderingServer.FramePostDraw += Capture;
            }
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
        }
        catch (Exception e) { Fail(e); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            var demo = _entry.Demo;
            var character = demo.ActiveCharacter;
            if (demo.ErrorCount != 0 || character.FailureDiagnosticCount != 0 || character.IsPoseFrozen) throw new Exception("Animation frame failed.");
            _tick++;
            if (_tick is 60 or 180 or 300) _captureTick = _tick;
            var skeleton = character.PhysicalDisplaySkeleton;
            var bones = new Dictionary<string, object>();
            for (var i = 0; i < skeleton.GetBoneCount(); i++)
            {
                var t = skeleton.GetBoneGlobalPose(i); var q = t.Basis.GetRotationQuaternion();
                bones[skeleton.GetBoneName(i)] = new { p = new[] { t.Origin.X,t.Origin.Y,t.Origin.Z }, q = new[] { q.X,q.Y,q.Z,q.W } };
            }
            _frames.Add(new { tick = _tick, committed = character.RuntimeCommittedFrameId, bones });
            if (_tick < 360) return;
            var path = System.Environment.GetEnvironmentVariable("KIMODO_IDLE_DIAGNOSTIC_OUTPUT") ?? throw new Exception("Missing output path.");
            System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new { assetRoot = System.Environment.GetEnvironmentVariable("ALS_ASSET_ROOT"), rawRoot = System.Environment.GetEnvironmentVariable("ALS_RAW_SOURCE_GENERATED_DIR"), frames = _frames }));
            GD.Print($"KIMODO_IDLE_DIAGNOSTIC_OK frames={_frames.Count} output={path}");
            _done = true; GetTree().Quit();
        }
        catch (Exception e) { Fail(e); }
    }
    private void Capture()
    {
        if (_captureTick == 0 || _captureDirectory is null) return;
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_captureDirectory, $"idle-{_captureTick}.png"));
        _captureTick = 0;
    }
    private void Fail(Exception e) { _done = true; GD.PushError(e.ToString()); GetTree().Quit(1); }
}
