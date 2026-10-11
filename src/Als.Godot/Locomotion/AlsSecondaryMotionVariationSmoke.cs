using System.Text.Json;
using Godot;
using GodotAls.Animation;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

// Inspect real source sampling, then observe the ordinary entry's committed skeleton and skinned soles.
public partial class AlsSecondaryMotionVariationSmoke : Node
{
    private AlsDemoEntry _entry = null!;
    private AlsSoleGeometryObserver _sole = null!;
    private StaticBody3D _floor = null!;
    private readonly List<object> _frames = [];
    private readonly List<object> _captures = [];
    private object _sources = null!;
    private string _output = null!;
    private string? _captureDirectory;
    private int _tick, _captureTick;
    private bool _done;
    private Camera3D? _camera;

    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            Engine.PhysicsTicksPerSecond = int.Parse(args.FirstOrDefault(a => a.StartsWith("--idle-hz="))?[10..] ?? "60");
            _output = args.Single(a => a.StartsWith("--idle-output="))[14..];
            _captureDirectory = args.FirstOrDefault(a => a.StartsWith("--capture-dir="))?[14..];
            _entry = new AlsDemoEntry { ConfigureBeforeReady = demo => demo.ConfigureRuntimePolicyForSmoke(
                args.Contains("--parallel") ? AlsHarnessMode.Parallel : AlsHarnessMode.Single, true) };
            AddChild(_entry);
            Require(_entry.Demo.IsRuntimeReady, "Ordinary Demo did not initialize.");
            _sources = InspectSources();
            _entry.Demo.OrbitCamera.SetMouseCaptured(false);
            _floor = _entry.Demo.GetNode<StaticBody3D>("World/StartFloor");
            _sole = new(_entry.Demo.ActiveCharacter.PhysicalDisplaySkeleton.GetParent());
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
            if (_captureDirectory is not null)
            {
                Require(DisplayServer.GetName() != "headless", "Captures require a rendered viewport.");
                Directory.CreateDirectory(_captureDirectory);
                Engine.MaxPhysicsStepsPerFrame = 1;
                _camera = new Camera3D { Fov = 35 };
                AddChild(_camera); _camera.MakeCurrent();
                RenderingServer.FramePostDraw += Capture;
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static object InspectSources()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var definition = AlsMovementGraphDefinition.Load(set, AlsLocomotionProfileCompiler.Compile(
            Godot.FileAccess.GetFileAsString("res://assets/config/p4_cycle_locomotion_profile.json"), set));
        var profile = definition.OverlaySources; var bank = definition.OverlayRawSources;
        var names = bank.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray()).Distinct().Order().ToArray();
        var skeleton = bank.GetSkeleton(profile.SkeletonId);
        var baseline = new AlsPreciseOverlayAnimationSourceSampler(profile, bank, set, names);
        var tuned = new AlsPreciseOverlayAnimationSourceSampler(profile, bank, set, names, AlsAnimationRuntimeOptions.SecondaryMotionRotationGains);
        var a = new AlsPrecisePose[skeleton.LogicalBoneCount]; var b = new AlsPrecisePose[a.Length];
        var first = new AlsPrecisePose[a.Length]; var ac = new AlsInertialCurve[names.Length]; var bc = new AlsInertialCurve[names.Length];
        var gains = AlsAnimationRuntimeOptions.SecondaryMotionRotationGains;
        var peaks = new Dictionary<string, double>(); var seams = new Dictionary<string, double>();
        var changedPlayers = 0;
        foreach (var node in profile.Players)
        {
            baseline.Sample(node.Id, 0, 0, first, ac);
            var selected = set.Animations[node.AnimationId].Name == "ALS_N_SecondaryMotion" &&
                (node.NodePath.Contains(".Default.") || node.NodePath.Contains(".Feminine.") || node.NodePath.Contains(".Masculine."));
            var duration = bank.GetSource(node.AnimationId).Policy.SequencePlayLength;
            var changed = false;
            for (var sample = 0; sample <= 80; sample++)
            {
                var time = duration * sample / 80;
                baseline.Sample(node.Id, time, .5, a, ac); tuned.Sample(node.Id, time, .5, b, bc);
                Require(ac.SequenceEqual(bc), "Variation changed source curves.");
                for (var bone = 0; bone < a.Length; bone++)
                {
                    var name = skeleton.LogicalBoneNames[bone];
                    var gain = selected && gains is not null && gains.TryGetValue(name, out var value) ? value : 1;
                    Require(a[bone].Position == b[bone].Position && a[bone].Scale == b[bone].Scale, "Variation changed bone lengths or scale.");
                    if (gain == 1) Require(a[bone] == b[bone], "Variation changed an unselected source or bone.");
                    else
                    {
                        var angle = Angle(a[bone].Rotation, first[bone].Rotation);
                        var enhanced = Angle(b[bone].Rotation, first[bone].Rotation);
                        Require(Math.Abs(enhanced - angle * gain) < .00002, "Temporal angle did not follow configured gain.");
                        peaks[name] = Math.Max(peaks.GetValueOrDefault(name), enhanced);
                        if (sample == 80) seams[name] = Math.Max(seams.GetValueOrDefault(name), enhanced);
                        changed |= a[bone] != b[bone];
                    }
                }
            }
            if (changed) changedPlayers++;
        }
        Require(changedPlayers == (gains is null ? 0 : 3), "Unexpected tuned source occurrence count.");
        // Repeated sampling must stay deterministic and allocation-free inside the existing source clock.
        var source = profile.Players.ToArray().First(n => n.NodePath.Contains(".Default.") && set.Animations[n.AnimationId].Name == "ALS_N_SecondaryMotion").Id;
        for (var i = 0; i < 1000; i++) tuned.Sample(source, .617, .5, b, bc);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) tuned.Sample(source, .617, .5, b, bc);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        tuned.Sample(source, .617, .5, a, ac);
        Require(a.SequenceEqual(b) && ac.SequenceEqual(bc) && allocated == 0, "Variation retry or allocation gate failed.");
        Require(seams.Values.All(angle => angle < .5), "Enhanced upper-body loop seam exceeds half a degree.");
        GD.Print($"ALS_SECONDARY_VARIATION_SOURCE_OK players=148 changed={changedPlayers} hot_samples=10000 allocated={allocated}");
        return new { changedPlayers, allocated, peaksDegrees = peaks, loopSeamDegrees = seams };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            var character = _entry.Demo.ActiveCharacter;
            Require(_entry.Demo.ErrorCount == 0 && character.FailureDiagnosticCount == 0 && !character.IsPoseFrozen, "Production frame failed.");
            ++_tick;
            var identity = character.FullMovementDiagnostics.Identity;
            Require(character.RuntimeCommittedFrameId == _tick, "Skipped or repeated character commit.");
            var skeleton = character.PhysicalDisplaySkeleton;
            if (_tick > 2)
            {
                var bones = new Dictionary<string, object>();
                foreach (var name in new[] { "root", "pelvis", "spine_01", "spine_02", "spine_03", "clavicle_l", "clavicle_r", "neck_01", "head", "foot_l", "foot_r", "hand_l", "hand_r" })
                {
                    var id = skeleton.FindBone(name); var q = skeleton.GetBonePoseRotation(id);
                    var p = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(id).Origin;
                    bones[name] = new { rotation = new[] { q.X, q.Y, q.Z, q.W }, world = new[] { p.X, p.Y, p.Z } };
                }
                var rig = character.FullMovementDiagnostics.RefactoredRig;
                var soles = _sole.CaptureQuality(identity, (long)_floor.GetInstanceId(), _floor.GlobalTransform, .25f,
                    rig.Left.Location.ContactApplied, rig.Right.Location.ContactApplied);
                Require(soles.Left.MinimumSupportHeight >= -2 && soles.Right.MinimumSupportHeight >= -2, "Idle sole penetration gate failed.");
                _frames.Add(new { tick = _tick, committed = character.RuntimeCommittedFrameId, bones, soles });
                if (_camera is not null)
                {
                    var root = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("root")).Origin;
                    var head = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(skeleton.FindBone("head")).Origin;
                    var center = (root + head) * .5f;
                    _camera.GlobalPosition = center + new Vector3(2.8f, .4f, 3.5f);
                    _camera.LookAt(center); _camera.MakeCurrent();
                    if (_tick % Engine.PhysicsTicksPerSecond == 0 && _tick / Engine.PhysicsTicksPerSecond is 2 or 3 or 4 or 5)
                        _captureTick = _tick;
                }
            }
            if (_tick >= Engine.PhysicsTicksPerSecond * 8)
            {
                Save(); _done = true;
                GD.Print($"ALS_SECONDARY_VARIATION_GAME_OK hz={Engine.PhysicsTicksPerSecond} frames={_frames.Count} captures={_captures.Count}");
                GetTree().Quit();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void Capture()
    {
        if (_captureTick == 0 || _done) return;
        try
        {
            Require(_captureTick == _tick && _entry.Demo.ActiveCharacter.RuntimeCommittedFrameId == _tick, "Capture did not match the committed frame.");
            var path = Path.Combine(_captureDirectory!, "idle-" + _tick + ".png");
            using var image = GetViewport().GetTexture().GetImage();
            Require(image.SavePng(path) == Error.Ok, "Screenshot save failed.");
            _captures.Add(new { tick = _tick, path }); _captureTick = 0;
        }
        catch (Exception error) { Fail(error); }
    }

    private void Save() => File.WriteAllText(_output, JsonSerializer.Serialize(new
    {
        enhanced = AlsAnimationRuntimeOptions.SecondaryMotionRotationGains is not null,
        hz = Engine.PhysicsTicksPerSecond, sources = _sources, frames = _frames, captures = _captures
    }, new JsonSerializerOptions { IncludeFields = true }));
    private void Fail(Exception error) { _done = true; if (_output is not null) Save(); GD.PushError(error.ToString()); GetTree().Quit(1); }
    private static double Angle(AlsQuaternion a, AlsQuaternion b) =>
        2 * Math.Acos(Math.Clamp(Math.Abs(AlsQuaternion.Dot(a, b)), 0, 1)) * 180 / Math.PI;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public override void _ExitTree() { if (_captureDirectory is not null) RenderingServer.FramePostDraw -= Capture; }
}
