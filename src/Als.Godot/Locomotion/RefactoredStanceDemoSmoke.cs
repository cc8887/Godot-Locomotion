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
    private long _locomotion;
    private int _locomotionStates;
    private long _pivotNotifies;
    private int _detailsStates;
    private long _footFeedback, _dynamicRequests;
    private long _standingDynamic, _beforeCrouchDisplacement;
    private int _plantedTicks;
    private int _distinctMovingFrames;
    private bool _crouchDisplaced;
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
            if(At(650))Input.ActionPress("jump");
            if(At(660))Input.ActionRelease("jump");
            if(At(830))Input.ActionPress("move_forward");
            if(At(880)){Input.ActionRelease("move_forward");Input.ActionPress("move_back");}
            if(At(900)){Input.ActionRelease("move_back");Input.ActionPress("move_forward");}
            if(At(970)){Input.ActionRelease("move_forward");Input.ActionPress("move_back");}
            if(At(1060))Input.ActionRelease("move_back");
            // A small external capsule displacement while planted exercises
            // actual world-space lock drift, without writing curves or locks.
            if (At(1125)) character.GetNode<AlsCharacterMotor>("Motor").GlobalPosition += new Vector3(.15f, 0, 0);
            if (At(1150)) _standingDynamic = _dynamicRequests;
            if (At(1160)) Input.ActionPress("aim");
            if (stage > 1160 && stage < 1280) demo.OrbitCamera.ApplyMouseMotion(new(8f * 60 / _hz, 0));
            if (At(1320)) Input.ActionPress("crouch_toggle");
            if (At(1330)) Input.ActionRelease("crouch_toggle");
            if (stage > 1360 && stage < 1480) demo.OrbitCamera.ApplyMouseMotion(new(-8f * 60 / _hz, 0));
            if (At(1580)) Input.ActionRelease("aim");
            if(stage>30&&stage<430&&_tick%4==0)demo.OrbitCamera.ApplyMouseMotion(new(2,0));
            var full=character.FullMovementDiagnostics;var frame=character.Diagnostics;
            if (full.Identity.SlotGeneration != 0)
            {
                if (full.RefactoredMotion.Identity != full.Identity || full.FootMotion != full.RefactoredMotion)
                    throw new Exception("Graph and foot lock used different locomotion observations.");
                if (full.RefactoredMotion.Moving != full.RefactoredMotion.MovingSmooth) _distinctMovingFrames++;
            }
            if(full.Identity.SlotGeneration!=0&&!full.LockCurveProducersMatch)
                throw new Exception($"Foot lock curve alias mismatch: raw={full.RawLockProducers}; final={full.LockProducers}.");
            _standing=Math.Max(_standing,full.RefactoredStandingFrames);_crouching=Math.Max(_crouching,full.RefactoredCrouchingFrames);
            _transition=Math.Max(_transition,full.RefactoredTransitionFrames);
            _grounded=Math.Max(_grounded,full.RefactoredGroundedFrames);_groundedStates|=full.RefactoredGroundedStateMask;
            _locomotion=Math.Max(_locomotion,full.RefactoredLocomotionFrames);_locomotionStates|=full.RefactoredLocomotionStateMask;
            _pivotNotifies = Math.Max(_pivotNotifies, full.RefactoredPivotNotifies);
            _detailsStates |= full.RefactoredMovementDetailsMask;
            _footFeedback = Math.Max(_footFeedback, full.RefactoredFootFeedbackFrames);
            _dynamicRequests = Math.Max(_dynamicRequests, full.RefactoredDynamicRequests);
            if (stage > 1500 && !_crouchDisplaced)
            {
                var planted = frame.Result.ActualStance == AlsStance.Crouching && full.RefactoredTransitionsAllowed &&
                    full.RefactoredRestFeet.LeftAmount >= .999f && full.RefactoredRestFeet.RightAmount >= .999f;
                _plantedTicks = planted ? _plantedTicks + 1 : 0;
                if (_plantedTicks >= _hz / 5)
                {
                    _beforeCrouchDisplacement = _dynamicRequests;
                    character.GetNode<AlsCharacterMotor>("Motor").GlobalPosition += new Vector3(.15f, 0, 0);
                    _crouchDisplaced = true;
                    GD.Print($"REST_FEET_CROUCH_DISPLACEMENT stage={stage}");
                }
            }
            if (stage > 1100 && _tick % (_hz / 2) == 0)
            {
                var feet = full.RefactoredRestFeet;
                GD.Print($"REST_FEET_SAMPLE stage={stage} allowed={full.RefactoredTransitionsAllowed} left={feet.LeftAmount} right={feet.RightAmount} ld2={(feet.LeftTarget-feet.LeftLock).LengthSquared} rd2={(feet.RightTarget-feet.RightLock).LengthSquared} requests={_dynamicRequests}");
            }
            if(frame.Result.ActualStance==AlsStance.Crouching)_crouch++;
            if(frame.Result.ActualGait==AlsGait.Sprinting)_sprint++;
            if(frame.Result.ResolvedLocomotionState==AlsLocomotionState.InAir)_air++;
            if(stage>20&&full.Identity!=frame.Identity)throw new Exception("New chain escaped normal frame ownership.");
            if(_capture is not null)foreach(var at in new[]{60,110,165,210,255,310,350,390,470,485,495,545,555,670,710,750,790,850,885,895,915,985,1010,1120,1128,1140,1160,1200,1250,1400,1460,1550,1560,1575,1600})if(At(at))_captures.Add(at);
            _previousStage=stage;
            if(stage>=1700)
            {
                if (_footFeedback < _hz * 15)
                    throw new Exception($"Missing global foot feedback: {_footFeedback}.");
                if (_standingDynamic == 0 || !_crouchDisplaced || _dynamicRequests <= _beforeCrouchDisplacement)
                    throw new Exception($"Planted displacement did not trigger both stance transitions: standing={_standingDynamic}, before_crouch={_beforeCrouchDisplacement}, total={_dynamicRequests}.");
                if (_pivotNotifies == 0 || (_detailsStates & 8) == 0)
                    throw new Exception($"Real input never reached Pivot: notifications={_pivotNotifies}, details={_detailsStates}.");
                if(_grounded<_hz*5||(_groundedStates&30)!=30)throw new Exception($"Original Grounded pose not covered: frames={_grounded} states={_groundedStates}.");
                if(_locomotion<_hz*12||(_locomotionStates&29)!=29)throw new Exception($"Original Locomotion not covered: frames={_locomotion} states={_locomotionStates}.");
                if(_standing<_hz*3||_crouching<_hz||_transition<_hz*5||_crouch<_hz||_air==0||_sprint==0)
                    throw new Exception($"Incomplete native-host coverage standing={_standing} crouching={_crouching} transition={_transition} crouch={_crouch} air={_air} sprint={_sprint}.");
                GD.Print($"ALS_REFACTORED_STANCE_DEMO_OK hz={_hz} standing={_standing} crouching={_crouching} transition={_transition} grounded={_grounded} grounded_states={_groundedStates} locomotion={_locomotion} locomotion_states={_locomotionStates} air={_air} sprint={_sprint} pivot_notifies={_pivotNotifies} details={_detailsStates}");
                GD.Print($"ALS_REFACTORED_REST_FEEDBACK_OK frames={_footFeedback} dynamic_requests={_dynamicRequests}");
                GD.Print($"ALS_REFACTORED_MOTION_OK distinct_moving_frames={_distinctMovingFrames}");
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
        foreach(var action in new[]{"move_forward","move_back","move_left","move_right","walk","sprint","crouch_toggle","jump","aim"})Input.ActionRelease(action);
        if(_capture is not null)RenderingServer.FramePostDraw-=Capture;
        Input.MouseMode=_mouse;
    }
}
