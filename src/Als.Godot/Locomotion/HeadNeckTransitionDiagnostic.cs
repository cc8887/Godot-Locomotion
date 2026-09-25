using System.Text.Json;
using Godot;
using GodotAls.Animation;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Opt-in reproduction only; does not change production animation or physics.
public partial class HeadNeckTransitionDiagnostic : Node
{
    private P4LocomotionDemo _demo = null!;
    private StreamWriter? _trace;
    private string _output = "";
    private long _frame;
    private bool _done, _capture, _connected;
    private bool _look, _renderOnly;
    private bool _stress;
    private StreamWriter? _renderTrace;
    private readonly string[] _names = ["spine_03", "neck_01", "head"];
    private Input.MouseModeEnum _mouse;
    private bool _accumulation;
    public HeadNeckTransitionDiagnostic()
    { ProcessThreadGroup = ProcessThreadGroupEnum.MainThread; ProcessThreadGroupOrder = -2; }
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.First(a => a.StartsWith("--output="))[9..]; Directory.CreateDirectory(_output);
            _trace = new StreamWriter(Path.Combine(_output, "trace.jsonl"));
            _renderTrace = new StreamWriter(Path.Combine(_output,"render.jsonl"));
            _capture = args.Contains("--capture");
            _look = args.Contains("--look"); _renderOnly=args.Contains("--render-only");
            _stress=args.Contains("--stress");
            _mouse = Input.MouseMode; _accumulation = Input.UseAccumulatedInput; Input.UseAccumulatedInput = false;
            Engine.PhysicsTicksPerSecond = 60; AlsAnimationRuntimeOptions.ConfigureDemo();
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe;
            _demo = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn").Instantiate<P4LocomotionDemo>();
            _demo.ConfigureRuntimePolicyForSmoke(args.Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel, false);
            AddChild(_demo); RollingGameplaySmoke.PlaceOnOpenFloor(_demo);
            KeyEvent(Key.W, true);
            _demo.OrbitCamera.SetMouseCaptured(true);
            if (_capture) { RenderingServer.FramePostDraw += Capture; _connected = true; }
        }
        catch (Exception error) { Fail(error); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            var character = _demo.ActiveCharacter; var d = character.Diagnostics;
            if (d.Identity.FrameId == _frame) return;
            _frame = d.Identity.FrameId;
            if (_demo.ErrorCount != 0) throw new InvalidOperationException("Production animation failure.");
            var skeleton = character.PhysicalDisplaySkeleton;
            var bones = _names.Select(name =>
            {
                var bone = skeleton.FindBone(name); var p = skeleton.GetBonePosePosition(bone);
                var q = skeleton.GetBonePoseRotation(bone); var scale = skeleton.GetBonePoseScale(bone);
                var world = _renderOnly ? Transform3D.Identity : skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(bone);
                return new {name, local = new[]{p.X,p.Y,p.Z}, rotation=new[]{q.X,q.Y,q.Z,q.W},
                    scale=new[]{scale.X,scale.Y,scale.Z}, world=new[]{world.Origin.X,world.Origin.Y,world.Origin.Z}};
            }).ToArray();
            var graph = character.FullMovementDiagnostics.GraphCapture;
            object? stages = null;
            if (graph is not null)
            {
                var element = JsonSerializer.SerializeToElement(graph.Row["stages"]);
                stages = element.EnumerateObject().ToDictionary(stage => stage.Name, stage => (object)new
                {
                    bones = _names.ToDictionary(name => name, name => stage.Value.GetProperty("pose")[Array.IndexOf(graph.Names,name)]),
                    curves = stage.Value.GetProperty("curves")
                });
            }
            _trace!.WriteLine(JsonSerializer.Serialize(new { frame=_frame, state=d.Result.ResolvedLocomotionState.ToString(),
                gait=d.Result.ActualGait.ToString(), stance=d.Result.ActualStance.ToString(),
                ragdoll=character.RagdollSimulation is not null, bones, stages }));
            switch (_frame)
            {
                case 30: KeyEvent(Key.Space,true); break;
                case 31: KeyEvent(Key.Space,false); break;
                case 50: KeyEvent(Key.Shift,true); break;
                case 140: KeyEvent(Key.Shift,false); break;
                case 180: KeyEvent(Key.Shift,true); break;
                case 240: KeyEvent(Key.Shift,false); KeyEvent(Key.Ctrl,true); break;
                case 241: KeyEvent(Key.Ctrl,false); break;
                case 280: KeyEvent(Key.Ctrl,true); break;
                case 281: KeyEvent(Key.Ctrl,false); KeyEvent(Key.Shift,true); break;
                case 340: KeyEvent(Key.R,true); break;
                case 341: KeyEvent(Key.R,false); break;
                case 400: KeyEvent(Key.Shift,false); KeyEvent(Key.W,false); break;
            }
            if (_look)
            {
                using var motion=new InputEventMouseMotion { Relative=_stress
                    ? new Vector2(Mathf.Sin(_frame*.11f)*65f,Mathf.Cos(_frame*.09f)*35f)
                    : new Vector2(4f,Mathf.Sin(_frame*.06f)*5f) };
                Input.ParseInputEvent(motion); Input.FlushBufferedEvents();
            }
            if (_frame >= 480) { _done=true; Cleanup(); GD.Print("HEAD_NECK_DIAGNOSTIC_COMPLETE frames="+_frame); GetTree().Quit(); }
        }
        catch(Exception error) { Fail(error); }
    }
    public override void _Process(double delta)
    {
        if (!_capture || _done || _demo is null) return;
        var camera = GetViewport().GetCamera3D(); if (camera is null) return;
        var center = _demo.ActiveCharacter.MovementAnchor.GlobalPosition;
        camera.GlobalPosition = center + new Vector3(1.6f,.65f,1.6f); camera.LookAt(center+Vector3.Up*.25f);
    }
    private void Capture()
    {
        if (!_done)
        {
            var skeleton=_demo.ActiveCharacter.PhysicalDisplaySkeleton;
            var component=new Transform3D[skeleton.GetBoneCount()];
            for(var b=0;b<component.Length;b++)
            { var local=skeleton.GetBonePose(b); var parent=skeleton.GetBoneParent(b); component[b]=parent<0?local:component[parent]*local; }
            foreach(var mesh in Descendants(_demo.ActiveCharacter).OfType<MeshInstance3D>())
            {
                var reference=mesh.GetSkinReference(); if(reference is null) continue;
                var skin=reference.GetSkin();
                for(var bind=0;bind<skin.GetBindCount();bind++)
                {
                    var name=skin.GetBindName(bind).ToString(); var bone=name.Length>0?skeleton.FindBone(name):skin.GetBindBone(bind);
                    if(bone<0 || !_names.Contains(skeleton.GetBoneName(bone).ToString())) continue;
                    var actual=RenderingServer.SkeletonBoneGetTransform(reference.GetSkeleton(),bind)*skin.GetBindPose(bind).AffineInverse();
                    _renderTrace!.WriteLine(JsonSerializer.Serialize(new {frame=_frame,bone=skeleton.GetBoneName(bone).ToString(),
                        positionError=actual.Origin.DistanceTo(component[bone].Origin),
                        basisError=Mathf.Max((actual.Basis.X-component[bone].Basis.X).Length(),Mathf.Max((actual.Basis.Y-component[bone].Basis.Y).Length(),(actual.Basis.Z-component[bone].Basis.Z).Length()))}));
                }
            }
        }
        if (_done || _frame < 25 || _frame > 415 || _frame % 5 != 0) return;
        using var image=GetViewport().GetTexture().GetImage(); image.SavePng(Path.Combine(_output,$"frame-{_frame:D4}.png"));
    }
    private static IEnumerable<Node> Descendants(Node root)
    { yield return root; foreach(var child in root.GetChildren()) foreach(var node in Descendants(child)) yield return node; }
    private static void KeyEvent(Key key,bool pressed)
    { using var e=new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=pressed}; Input.ParseInputEvent(e); Input.FlushBufferedEvents(); }
    private void Cleanup()
    {
        if (_connected) {RenderingServer.FramePostDraw-=Capture;_connected=false;}
        foreach(var key in new[]{Key.W,Key.Shift,Key.Space,Key.Ctrl,Key.R}) KeyEvent(key,false);
        _trace?.Dispose();_trace=null; _renderTrace?.Dispose();_renderTrace=null; _demo?.DisposeRuntime(); Input.MouseMode=_mouse;Input.UseAccumulatedInput=_accumulation;
    }
    private void Fail(Exception e) {_done=true;Cleanup();GD.PushError(e.ToString());GetTree().Quit(1);}
    public override void _ExitTree() => Cleanup();
}
