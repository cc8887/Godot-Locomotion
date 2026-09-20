using System.Text.Json;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

public partial class OverlayPropSmoke : Node
{
    private P4LocomotionDemo _demo = null!;
    private long _committed, _injected;
    private int _ticks, _hold, _resumed;
    private bool _done, _render;
    private Input.MouseModeEnum _oldMouse;
    private bool _oldAccumulation;
    private float _minDraw = float.MaxValue, _maxDraw = float.MinValue;
    private readonly HashSet<AlsOverlayKind> _seen = [];
    private Camera3D? _inspectionCamera;
    private OmniLight3D? _inspectionLight;
    public OverlayPropSmoke() { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }
    public override void _Ready()
    {
        try
        {
            _oldMouse = Input.MouseMode; _oldAccumulation = Input.UseAccumulatedInput; Input.UseAccumulatedInput = false;
            _render = OS.GetCmdlineUserArgs().Contains("--render-props");
            AlsAnimationRuntimeOptions.ConfigureDemo();
            _demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<P4LocomotionDemo>();
            _demo.ConfigureRuntimePolicyForSmoke(OS.GetCmdlineUserArgs().Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, true);
            AddChild(_demo); Require(_demo.IsRuntimeReady, "Demo failed to initialize.");
            if (_render)
            {
                _inspectionCamera = new Camera3D { Name = "PropInspectionCamera", Fov = 50 };
                AddChild(_inspectionCamera); _inspectionCamera.MakeCurrent();
                _inspectionLight = new OmniLight3D { Name = "PropInspectionFill", OmniRange = 10, LightEnergy = 4 };
                AddChild(_inspectionLight);
            }
            Require(_demo.ActiveCharacter.Props!.InstanceCount == 7, "Props were not cached/shared by mesh.");
            VerifyBowOracle(_demo.RuntimeContext);
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            Require(++_ticks < 2500 && _demo.ErrorCount == 0, "Prop runtime stalled or failed.");
            var character = _demo.ActiveCharacter; var props = character.Props!;
            if (_hold > 0 && _hold < 6)
            {
                Require(props.Committed.Identity.FrameId == 960 && props.Committed.Overlay == AlsOverlayKind.Pistol2H,
                    "Pending Bow switch escaped Main Commit.");
                if (++_hold == 6) character.GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Inherit;
                return;
            }
            if (_resumed == 1)
            {
                Require(props.VisibleComponent is null && !character.Visible, "Deactivated prop remained visible.");
                character.SetActive(true); _resumed = 2;
            }
            var diagnostics = character.Diagnostics;
            if (diagnostics.CommittedFrameId > _committed)
            {
                Require(diagnostics.CommittedFrameId == _committed + 1, "Lost a committed prop frame.");
                _committed = diagnostics.CommittedFrameId;
                var frame = props.Committed;
                Require(frame.Identity == diagnostics.Identity && frame.Overlay == character.LatestMotorInput.Command.RequestedOverlay,
                    "Prop/frame/Overlay identities disagree.");
                _seen.Add(frame.Overlay);
                var binding = _demo.RuntimeContext.PropProfile!.Get(frame.Overlay);
                Require((props.VisibleComponent is not null) == binding.HasProp, "Wrong equipped mesh visibility.");
                if (props.VisibleComponent is not null)
                    Require(props.VisibleComponent.GlobalTransform.IsEqualApprox(frame.Attachment), "Import basis was applied twice.");
                if (frame.Overlay == AlsOverlayKind.Bow)
                {
                    Require(frame.BoneCount == 24, "Missing Bow pose.");
                    _minDraw = MathF.Min(_minDraw, frame.Draw); _maxDraw = MathF.Max(_maxDraw, frame.Draw);
                    var skeleton = props.VisibleSkeleton!;
                    Require(skeleton.GetBonePosePosition(skeleton.FindBone("string_mid")).DistanceTo(
                        new(frame.Pose[1].Position.X, frame.Pose[1].Position.Y, frame.Pose[1].Position.Z)) < .00001f, "Bow string did not consume the pose.");
                }
                if (_render && _committed % 120 is 40 or 100) SaveImage(frame.Overlay, _committed);
                if (_committed == 1560)
                {
                    character.SetActive(false); _resumed = 1; return;
                }
                if (_committed >= 1600) { Complete(); return; }
                if (_committed == 1580) Require(frame.Overlay == AlsOverlayKind.Barrel, "Q did not wrap Default to Barrel.");
                if (_committed == 1581) Require(frame.Overlay == AlsOverlayKind.Default, "E did not return to Default.");
            }
            if (_injected == _committed)
            {
                _injected++;
                if (_injected is 1580 or 1581)
                { var key = _injected == 1580 ? Key.Q : Key.E; KeyEvent(key,true); KeyEvent(key,false); }
                if (_injected > 1 && (_injected - 1) % 120 == 0)
                { KeyEvent(Key.E, true); KeyEvent(Key.E, true, true); KeyEvent(Key.E, false); }
                var offset = (_injected - 1) % 120;
                Action("aim", offset >= 60); Action("move_left", offset is >= 10 and < 35); Action("move_right", offset is >= 35 and < 60);
                if (_injected == 961)
                { character.GetNode<Node>("Commit").ProcessMode = ProcessModeEnum.Disabled; _hold = 1; }
            }
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _Process(double delta)
    {
        if (_done || _inspectionCamera is null) return;
        var center = _demo.ActiveCharacter.MovementAnchor.GlobalPosition;
        _inspectionCamera.GlobalPosition = center + new Vector3(1.9f, .7f, -2.1f);
        _inspectionCamera.LookAt(center + new Vector3(0, .1f, 0));
        _inspectionLight!.GlobalPosition = center + new Vector3(1.5f, 2, -2);
    }
    private static void VerifyBowOracle(AlsP3RuntimeContext context)
    {
        var sampler = new AlsOverlayPropSampler(context.PropProfile!, context.PropSources!, context.AnimationSet);
        using var json = JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://assets/config/v4_overlay_prop_oracle.json")));
        var maxPosition = 0f; var maxRotation = 0f; var rows = 0;
        foreach (var row in json.RootElement.GetProperty("samples").EnumerateArray())
        {
            var frame = new AlsOverlayPropFrame(); sampler.SampleBow(row.GetProperty("timeSeconds").GetSingle(), ref frame);
            var index = 0;
            foreach (var bone in row.GetProperty("pose").EnumerateArray())
            {
                var p = bone.GetProperty("position"); var q = bone.GetProperty("rotation");
                var expected = new System.Numerics.Vector3(p[0].GetSingle(), -p[1].GetSingle(), p[2].GetSingle()) * .01f;
                var rotation = new System.Numerics.Quaternion(-q[0].GetSingle(), q[1].GetSingle(), -q[2].GetSingle(), q[3].GetSingle());
                maxPosition = MathF.Max(maxPosition, System.Numerics.Vector3.Distance(expected, frame.Pose[index].Position));
                maxRotation = MathF.Max(maxRotation, 1 - MathF.Abs(System.Numerics.Quaternion.Dot(rotation, frame.Pose[index].Rotation)));
                index++;
            }
            Require(index == frame.BoneCount, "Native Bow oracle bone count differs."); rows++;
        }
        Require(rows == 9 && maxPosition < .00001f && maxRotation < .000001f, $"Bow source mismatch: {maxPosition}, {maxRotation}.");
        var warm = new AlsOverlayPropFrame(); for (var i = 0; i < 100; i++) sampler.SampleBow(.5f, ref warm);
        var before = GC.GetAllocatedBytesForCurrentThread(); for (var i = 0; i < 1000; i++) sampler.SampleBow(.5f, ref warm);
        Require(GC.GetAllocatedBytesForCurrentThread() == before, "Bow sampling allocated per frame.");
        GD.Print($"PROP_BOW_ORACLE_OK rows={rows} bones=24 max_position={maxPosition} max_rotation={maxRotation} sampling_allocations=0");
    }
    private void SaveImage(AlsOverlayKind overlay, long frame)
    {
        using var image = GetViewport().GetTexture().GetImage();
        var path = ProjectSettings.GlobalizePath($"res://artifacts/overlay-props-20260920/prop-{frame:D4}-{overlay}.png");
        Require(image.SavePng(path) == Error.Ok, "Could not save prop render.");
    }
    private static void Action(string action, bool pressed)
    { if (pressed) Input.ActionPress(action); else Input.ActionRelease(action); }
    private static void KeyEvent(Key key, bool pressed, bool echo = false)
    {
        using var input = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, Echo = echo };
        Input.ParseInputEvent(input); Input.FlushBufferedEvents();
    }
    private void Complete()
    {
        Require(_seen.Count == 13 && _resumed == 2 && _hold == 6 && _maxDraw - _minDraw > .5f,
            $"Incomplete prop coverage: overlays={_seen.Count}, Draw={_minDraw}..{_maxDraw}.");
        GD.Print($"OVERLAY_PROPS_OK frames={_committed} overlays=13 cached_meshes=7 bow_draw={_minDraw}..{_maxDraw} held_commit=passed deactivate_resume=passed keys=E echo=ignored mode={_demo.RuntimeContext.Mode}");
        Cleanup(); _done = true; GetTree().Quit();
    }
    private void Cleanup()
    {
        foreach (var action in new[] { "aim", "move_left", "move_right" }) Input.ActionRelease(action);
        _demo?.DisposeRuntime(); Input.MouseMode = _oldMouse; Input.UseAccumulatedInput = _oldAccumulation;
    }
    private void Fail(Exception error) { GD.PushError(error.ToString()); Cleanup(); _done = true; GetTree().Quit(1); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
