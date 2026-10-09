using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Contracts;
using GodotAls.Dispatch;
using System.Text.Json;

namespace GodotAls.Locomotion;

// Drives the ordinary input owner and observes the committed, actually skinned mesh.
public partial class AlsDirectionalMovementSmoke : Node
{
    private readonly record struct Scenario(string Mode, string Direction, bool Crouch, bool Walk, bool Sprint, bool Aim);
    private readonly List<Scenario> _scenarios = [];
    private readonly List<object> _rows = [];
    private readonly List<object> _captures = [];
    private AlsDemoEntry _entry = null!;
    private AlsSoleGeometryObserver _sole = null!;
    private StaticBody3D _floor = null!;
    private Camera3D? _camera;
    private Label? _label;
    private int _tick;
    private string _output = null!;
    private string? _capture;
    private string? _pending;
    private int _requestedCaptureTick;
    private int? _previousMaxPhysicsSteps;
    private bool? _previousAlwaysOnTop;
    private bool _done;
    private bool _crouch;
    private int _unsupportedFrames;
    private AlsFootQualityMetrics _left, _right, _leftPlanted, _rightPlanted;
    public override void _Ready()
    {
        try
        {
            Engine.PhysicsTicksPerSecond = 60;
            var args = OS.GetCmdlineUserArgs();
            _output = args.Single(a=>a.StartsWith("--direction-output="))[19..];
            _capture = args.FirstOrDefault(a=>a.StartsWith("--capture-dir="))?[14..];
            foreach(var mode in new[]{"walk","run","sprint","crouch","aim-walk","aim-run","aim-crouch"})
                foreach(var direction in new[]{"forward","back","left","right"})
                    _scenarios.Add(new(mode,direction,mode.Contains("crouch"),mode.Contains("walk"),mode=="sprint",mode.StartsWith("aim")));
            _entry = new AlsDemoEntry { ConfigureBeforeReady = demo=>demo.ConfigureRuntimePolicyForSmoke(
                args.Contains("--parallel")?AlsHarnessMode.Parallel:AlsHarnessMode.Single,true) };
            AddChild(_entry);
            if(!_entry.Demo.IsRuntimeReady) throw new Exception("Ordinary entry failed.");
            // Controlled directional input must not rotate with desktop mouse
            // motion while the rendered test owns a separate tracking camera.
            _entry.Demo.OrbitCamera.SetMouseCaptured(false);
            var world = _entry.Demo.GetNode<Node3D>("World");
            foreach(var child in world.GetChildren()) if(child.Name!="StartFloor") Disable(child);
            _floor = world.GetNode<StaticBody3D>("StartFloor");
            _floor.GetNode<CollisionShape3D>("CollisionShape3D").Position = Vector3.Zero;
            _floor.GetNode<CollisionShape3D>("CollisionShape3D").Shape = new BoxShape3D{Size=new(300,.5f,300)};
            var mesh = _floor.GetNode<MeshInstance3D>("MeshInstance3D");
            mesh.Position = new(0,.25f,0); mesh.Scale=Vector3.One; mesh.Mesh=new PlaneMesh{Size=new(300,300)};
            _sole = new(_entry.Demo.ActiveCharacter.PhysicalDisplaySkeleton.GetParent());
            if(_capture is not null)
            {
                if(DisplayServer.GetName()=="headless")throw new Exception("Screenshots require rendering.");
                _previousMaxPhysicsSteps=Engine.MaxPhysicsStepsPerFrame;
                Engine.MaxPhysicsStepsPerFrame=1;
                _previousAlwaysOnTop=GetWindow().AlwaysOnTop;
                GetWindow().AlwaysOnTop=true;
                RenderingServer.ViewportSetUpdateMode(GetViewport().GetViewportRid(),RenderingServer.ViewportUpdateMode.Always);
                Directory.CreateDirectory(_capture);
                _camera = new Camera3D{Fov=45}; AddChild(_camera); _camera.MakeCurrent();
                var canvas = new CanvasLayer(); AddChild(canvas);
                _label = new Label{Position=new(18,235)}; canvas.AddChild(_label);
                RenderingServer.FramePostDraw+=Capture;
            }
            ProcessThreadGroup=ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder=AlsP3FrameStages.Observe+1;
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double delta)
    {
        if(_done)return;
        try
        {
            var character=_entry.Demo.ActiveCharacter;
            if(_entry.Demo.ErrorCount!=0||character.FailureDiagnosticCount!=0||character.IsPoseFrozen)throw new Exception("Production frame failed.");
            var index=_tick/240; var phase=_tick%240; ++_tick;
            if(index>=_scenarios.Count)
            {
                Save();GD.Print($"ALS_DIRECTIONAL_MOVEMENT_OK scenarios={_scenarios.Count} frames={_rows.Count} output={_output}");
                _done=true;GetTree().Quit();return;
            }
            var s=_scenarios[index];
            if(phase==0)
            {
                Release();_left=default;_right=default;_leftPlanted=default;_rightPlanted=default;_unsupportedFrames=0;
                if(_crouch!=s.Crouch){Input.ActionPress("crouch_toggle");_crouch=s.Crouch;}
                if(s.Walk)Input.ActionPress("walk");
                if(s.Sprint)Input.ActionPress("sprint");
                if(s.Aim)Input.ActionPress("aim");
            }
            if(phase==2)Input.ActionRelease("crouch_toggle");
            if(phase==30)Input.ActionPress("move_"+s.Direction);
            if(phase==210)Release();
            var input=character.LatestMotorInput;var full=character.FullMovementDiagnostics;
            var frame=character.Diagnostics.Result;
            var sample=_sole.CaptureQuality(full.Identity,(long)_floor.GetInstanceId(),_floor.GlobalTransform,.25f,
                full.RefactoredRig.Left.Location.ContactApplied,full.RefactoredRig.Right.Location.ContactApplied);
            if(phase>=90&&phase<210)
            {
                if(input.Identity!=full.Identity)throw new Exception("Motor/graph identity differs.");
                var velocity=input.ActualVelocity;var direction=input.InputDirection;
                var speed=Math.Sqrt(velocity.X*velocity.X+velocity.Z*velocity.Z);
                var alignment=(velocity.X*direction.X+velocity.Z*direction.Z)/Math.Max(speed,.0001);
                if(speed<.5||alignment<.99)throw new Exception($"Wrong physical direction {s}: speed={speed} alignment={alignment}");
                if(frame.ActualStance!=(s.Crouch?AlsStance.Crouching:AlsStance.Standing))throw new Exception("Wrong stance.");
                _left=_left.Advance(sample.Left,delta);_right=_right.Advance(sample.Right,delta);
                var locks=full.RefactoredLocks;
                _leftPlanted=_leftPlanted.Advance(sample.Left with {Contact=sample.Left.Contact&&locks.Left.Amount>=.999f&&
                    !locks.Left.ThighConstrained&&!locks.Left.FootConstrained},delta);
                _rightPlanted=_rightPlanted.Advance(sample.Right with {Contact=sample.Right.Contact&&locks.Right.Amount>=.999f&&
                    !locks.Right.ThighConstrained&&!locks.Right.FootConstrained},delta);
            }
            var skeleton=character.PhysicalDisplaySkeleton;
            var bones=new Dictionary<string,object>();
            foreach(var name in new[]{"root","pelvis","thigh_l","calf_l","foot_l","thigh_r","calf_r","foot_r"})
            {
                var bone=skeleton.GetBoneGlobalPose(skeleton.FindBone(name));
                var p=skeleton.GlobalTransform*bone.Origin;
                bones[name]=new {world=new[]{p.X,p.Y,p.Z},component=new[]{bone.Origin.X,bone.Origin.Y,bone.Origin.Z}};
            }
            var pelvisOffset=skeleton.GlobalBasis*(skeleton.GetBoneGlobalPose(skeleton.FindBone("pelvis")).Origin-
                skeleton.GetBoneGlobalPose(skeleton.FindBone("root")).Origin);
            var horizontalOffset=Math.Sqrt(pelvisOffset.X*pelvisOffset.X+pelvisOffset.Z*pelvisOffset.Z);
            _rows.Add(new{tick=_tick,scenario=s.Mode+"-"+s.Direction,phase,
                speed=full.RefactoredMotion.Speed,inputDirection=new[]{input.InputDirection.X,input.InputDirection.Y,input.InputDirection.Z},
                velocity=new[]{input.ActualVelocity.X,input.ActualVelocity.Y,input.ActualVelocity.Z},yaw=input.CharacterYaw,
                gait=frame.ActualGait.ToString(),stance=frame.ActualStance.ToString(),rotationMode=frame.ActualRotationMode.ToString(),
                left=sample.Left,right=sample.Right,bones,horizontalPelvisOffsetMeters=horizontalOffset,
                actorPosition=new[]{character.MovementAnchor.GlobalPosition.X,character.MovementAnchor.GlobalPosition.Y,character.MovementAnchor.GlobalPosition.Z},
                qualityLeft=_left,qualityRight=_right,plantedLeft=_leftPlanted,plantedRight=_rightPlanted});
            if(phase>=90&&phase<210)
            {
                if(horizontalOffset>.5)throw new Exception($"Body is detached from its root: {s.Mode}/{s.Direction}, pelvis offset {horizontalOffset:F3}m.");
                if(Math.Min(sample.Left.MinimumSupportHeight,sample.Right.MinimumSupportHeight)<-2)
                    throw new Exception($"Rendered foot penetrates floor: {s.Mode}/{s.Direction}.");
                _unsupportedFrames=Math.Min(sample.Left.MinimumSupportHeight,sample.Right.MinimumSupportHeight)>5?_unsupportedFrames+1:0;
                if(_unsupportedFrames>=30)throw new Exception($"Both rendered soles remain above floor for 0.5s: {s.Mode}/{s.Direction}.");
            }
            if(_camera is not null)
            {
                var position=character.MovementAnchor.GlobalPosition;
                _camera.GlobalPosition=position+new Vector3(3.2f,1.8f,3.2f);
                _camera.LookAt(position+new Vector3(0,.8f,0));
                _label!.Text=$"{s.Mode} / {s.Direction} | sample {phase}/240 | actual {frame.ActualGait} / {frame.ActualStance}\nSole support height: L {sample.Left.MinimumSupportHeight:F1} cm / R {sample.Right.MinimumSupportHeight:F1} cm";
                if(phase is 120 or 135 or 150 or 165)
                {
                    _pending=s.Mode+"-"+s.Direction+"-"+phase;
                    _requestedCaptureTick=_tick;
                }
            }
        }
        catch(Exception e){Fail(e);}
    }
    private void Save()=>File.WriteAllText(_output,JsonSerializer.Serialize(new{scenarios=_scenarios.Count,frames=_rows,captures=_captures},new JsonSerializerOptions{IncludeFields=true}));
    private void Capture()
    {
        if(_pending is null)return;
        var character=_entry.Demo.ActiveCharacter;var skeleton=character.PhysicalDisplaySkeleton;
        if(_tick!=_requestedCaptureTick||character.RuntimeCommittedFrameId!=_requestedCaptureTick)
        {Fail(new Exception("Character advanced before requested render capture."));return;}
        var bones=new Dictionary<string,object>();
        foreach(var name in new[]{"root","pelvis","thigh_l","calf_l","foot_l","thigh_r","calf_r","foot_r"})
        {
            var bone=skeleton.GetBoneGlobalPose(skeleton.FindBone(name));var p=skeleton.GlobalTransform*bone.Origin;
            bones[name]=new{world=new[]{p.X,p.Y,p.Z},component=new[]{bone.Origin.X,bone.Origin.Y,bone.Origin.Z}};
        }
        var path=Path.Combine(_capture!,_pending+".png");
        using var image=GetViewport().GetTexture().GetImage();
        if(image.SavePng(path)!=Error.Ok){Fail(new Exception("PNG save failed."));return;}
        using var body=image.GetRegion(new Rect2I(0,300,image.GetWidth(),image.GetHeight()-300));
        var bodyPixelsSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body.GetData()));
        _captures.Add(new{requestedTick=_requestedCaptureTick,observedTick=_tick,committed=character.RuntimeCommittedFrameId,
            presented=character.PublishedFrameId,screenshot=path,bones,bodyPixelsSha256});
        _pending=null;
    }
    private static void Release(){foreach(var action in new[]{"move_forward","move_back","move_left","move_right","walk","sprint","aim","crouch_toggle"})Input.ActionRelease(action);}
    private static void Disable(Node node){if(node is Node3D spatial)spatial.Hide();if(node is CollisionObject3D body){body.CollisionLayer=0;body.CollisionMask=0;}foreach(var child in node.GetChildren())Disable(child);}
    private void Fail(Exception e){_done=true;if(_output is not null)Save();GD.PushError(e.ToString());GetTree().Quit(1);}
    public override void _ExitTree(){Release();if(_capture is not null)RenderingServer.FramePostDraw-=Capture;if(_previousMaxPhysicsSteps.HasValue)Engine.MaxPhysicsStepsPerFrame=_previousMaxPhysicsSteps.Value;if(_previousAlwaysOnTop.HasValue)GetWindow().AlwaysOnTop=_previousAlwaysOnTop.Value;Input.MouseMode=Input.MouseModeEnum.Visible;}
}
