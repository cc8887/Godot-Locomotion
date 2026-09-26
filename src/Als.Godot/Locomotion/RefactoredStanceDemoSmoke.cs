using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Exercises the normal scene entry and committed renderer, not an isolated pose host.
public partial class RefactoredStanceDemoSmoke : Node
{
    private AlsDemoEntry _entry=null!;
    private int _tick,_previousStage,_hz;
    private long _standing,_crouching,_transition,_grounded;
    private int _groundedStates;
    private int _air,_crouch,_sprint;
    private bool _done;
    private string? _capture;
    private readonly List<int> _captures=[];
    private Input.MouseModeEnum _mouse;
    public override void _Ready()
    {
        try
        {
            var args=OS.GetCmdlineUserArgs();_hz=int.Parse(args.FirstOrDefault(a=>a.StartsWith("--stance-hz="))?[12..]??"60");
            if(_hz is not(30 or 60 or 120))throw new ArgumentException("Invalid stance test frequency.");
            Engine.PhysicsTicksPerSecond=_hz;_mouse=Input.MouseMode;
            _capture=args.FirstOrDefault(a=>a.StartsWith("--capture-dir="))?[14..];
            if(_capture is not null){Directory.CreateDirectory(_capture);RenderingServer.FramePostDraw+=Capture;}
            _entry=new AlsDemoEntry {ConfigureBeforeReady=demo=>demo.ConfigureRuntimePolicyForSmoke(
                args.Contains("--stance-single")?AlsHarnessMode.Single:AlsHarnessMode.Parallel,true)};
            AddChild(_entry);
            if(!_entry.Demo.IsRuntimeReady)throw new Exception("Ordinary Demo failed to initialize.");
            var world=_entry.Demo.GetNode<Node3D>("World");
            foreach(var child in world.GetChildren())if(child.Name!="StartFloor")DisableTerrain(child);
            var floor=world.GetNode<StaticBody3D>("StartFloor");
            floor.GetNode<CollisionShape3D>("CollisionShape3D").Shape=new BoxShape3D {Size=new(100,.5f,100)};
            ProcessThreadGroup=ProcessThreadGroupEnum.MainThread;ProcessThreadGroupOrder=AlsP3FrameStages.Observe+1;
        }
        catch(Exception e){Fail(e);}
    }
    public override void _PhysicsProcess(double delta)
    {
        if(_done)return;
        try
        {
            var demo=_entry.Demo;var character=demo.ActiveCharacter;
            if(character.BodyHistory?.Failure is {} historyFailure)throw new Exception(historyFailure);
            if(demo.ErrorCount!=0||character.FailureDiagnosticCount!=0||character.IsPoseFrozen)throw new Exception("Ordinary animation frame failed.");
            var stage=++_tick*60/_hz;
            bool At(int value)=>_previousStage<value&&stage>=value;
            if(At(30))Input.ActionPress("move_forward");
            if(At(80))Input.ActionPress("sprint");
            if(At(140)){Input.ActionRelease("sprint");Input.ActionRelease("move_forward");}
            if(At(160))Input.ActionPress("crouch_toggle");
            if(At(170))Input.ActionRelease("crouch_toggle");
            if(At(180))Input.ActionPress("move_right");
            if(At(230)){Input.ActionRelease("move_right");Input.ActionPress("move_left");}
            if(At(280))Input.ActionRelease("move_left");
            if(At(300))Input.ActionPress("crouch_toggle");
            if(At(310))Input.ActionRelease("crouch_toggle");
            if(At(330)){Input.ActionPress("move_forward");Input.ActionPress("jump");}
            if(At(340)){Input.ActionRelease("jump");Input.ActionPress("sprint");}
            if(At(430)){Input.ActionRelease("move_forward");Input.ActionRelease("sprint");}
            // Let speed and RotateInPlace settle, then exercise both dedicated
            // stance clips. Mouse rotation deliberately takes the other route.
            if(At(480)||At(540))Input.ActionPress("crouch_toggle");
            if(At(490)||At(550))Input.ActionRelease("crouch_toggle");
            if(stage>30&&stage<430&&_tick%4==0)demo.OrbitCamera.ApplyMouseMotion(new(2,0));
            var full=character.FullMovementDiagnostics;var frame=character.Diagnostics;
            _standing=Math.Max(_standing,full.RefactoredStandingFrames);_crouching=Math.Max(_crouching,full.RefactoredCrouchingFrames);
            _transition=Math.Max(_transition,full.RefactoredTransitionFrames);
            _grounded=Math.Max(_grounded,full.RefactoredGroundedFrames);_groundedStates|=full.RefactoredGroundedStateMask;
            if(frame.Result.ActualStance==AlsStance.Crouching)_crouch++;
            if(frame.Result.ActualGait==AlsGait.Sprinting)_sprint++;
            if(frame.Result.ResolvedLocomotionState==AlsLocomotionState.InAir)_air++;
            if(stage>20&&full.Identity!=frame.Identity)throw new Exception("New chain escaped normal frame ownership.");
            if(_capture is not null)foreach(var at in new[]{60,110,165,210,255,310,350,390,470,485,495,545,555})if(At(at))_captures.Add(at);
            _previousStage=stage;
            if(stage>=620)
            {
                if(_grounded<_hz*5||(_groundedStates&30)!=30)throw new Exception($"Original Grounded pose not covered: frames={_grounded} states={_groundedStates}.");
                if(_standing<_hz*3||_crouching<_hz||_transition<_hz*5||_crouch<_hz||_air==0||_sprint==0)
                    throw new Exception($"Incomplete native-host coverage standing={_standing} crouching={_crouching} transition={_transition} crouch={_crouch} air={_air} sprint={_sprint}.");
                GD.Print($"ALS_REFACTORED_STANCE_DEMO_OK hz={_hz} standing={_standing} crouching={_crouching} transition={_transition} grounded={_grounded} grounded_states={_groundedStates} air={_air} sprint={_sprint}");
                _done=true;GetTree().Quit();
            }
        }
        catch(Exception e){Fail(e);}
    }
    private void Capture()
    {
        if(_captures.Count==0)return;
        using var image=GetViewport().GetTexture().GetImage();
        foreach(var tick in _captures)image.SavePng(Path.Combine(_capture!,$"stance-{tick}.png"));
        _captures.Clear();
    }
    private static void DisableTerrain(Node node)
    {if(node is Node3D spatial)spatial.Hide();if(node is CollisionObject3D body){body.CollisionLayer=0;body.CollisionMask=0;}foreach(var child in node.GetChildren())DisableTerrain(child);}
    private void Fail(Exception e){_done=true;GD.PushError(e.ToString());GetTree().Quit(1);}
    public override void _ExitTree()
    {
        foreach(var action in new[]{"move_forward","move_left","move_right","sprint","crouch_toggle","jump"})Input.ActionRelease(action);
        if(_capture is not null)RenderingServer.FramePostDraw-=Capture;
        Input.MouseMode=_mouse;
    }
}
