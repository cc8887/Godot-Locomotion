using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// Injects a late worker fault while the actual model has a planted hand and a
// nonzero proportion correction. It observes the existing recovery transaction.
public partial class MantlingContactRollbackSmoke : Node
{
    private AlsDemoEntry _entry = null!;
    private AlsP3Character _character = null!;
    private StaticBody3D _obstacle = null!;
    private AlsHarnessMode _mode;
    private Transform3D[] _heldPose = [];
    private AlsMantlingContactDiagnostics _heldContacts;
    private AlsDoubleVector _heldCorrection;
    private AlsRetargetContactState _heldModelContactState;
    private long _heldFrame;
    private int _tick, _phase, _recoveredTick;
    private bool _done;

    public override void _Ready()
    {
        try
        {
            _mode = OS.GetCmdlineUserArgs().Contains("--single") ? AlsHarnessMode.Single : AlsHarnessMode.Parallel;
            Engine.PhysicsTicksPerSecond = 60;
            _entry = new() { ConfigureBeforeReady = demo => demo.ConfigureRuntimePolicyForSmoke(_mode, false) };
            AddChild(_entry);
            Require(_entry.Demo.IsRuntimeReady, "Ordinary ALS initialization failed.");
            _character = _entry.Demo.ActiveCharacter;
            Require(_character.PresentationSkeleton != _character.PhysicalDisplaySkeleton,
                "This regression requires a model that preserves its proportions.");
            var world = _entry.Demo.GetNode<Node3D>("World");
            foreach (var child in world.GetChildren()) if (child.Name != "StartFloor") Disable(child);
            world.GetNode<StaticBody3D>("StartFloor").GetNode<CollisionShape3D>("CollisionShape3D").Transform = Transform3D.Identity;
            _obstacle = new() { Name = "MantleRollbackTarget", Position = new(-4, .5f, -2.1f), CollisionLayer = 0 };
            _obstacle.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(4, 1, 3) } });
            world.AddChild(_obstacle);
            ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
            ProcessThreadGroupOrder = AlsP3FrameStages.Observe + 1;
        }
        catch (Exception error) { Fail(error); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            Require(++_tick < 240 && !_character.IsPoseFrozen &&
                _entry.Demo.ErrorCount == _character.FailureDiagnosticCount, "Recovery stalled or produced an unexpected failure.");
            if (_tick == 15) _obstacle.CollisionLayer = 1;
            if (_tick == 30) Input.ActionPress("jump");
            if (_tick == 32) Input.ActionRelease("jump");
            var contacts = _character.FullMovementDiagnostics.MantleContacts;
            var releaseCase = OS.GetCmdlineUserArgs().Contains("--during-release");
            if (_phase == 0 && contacts.Applied &&
                (releaseCase ? _character.ModelContactState.Releasing : MathF.Max(contacts.LeftWeight, contacts.RightWeight) >= .999f) &&
                _character.ModelContactRootCorrectionCm.LengthSquared > 1)
            {
                _heldFrame = _character.RuntimeCommittedFrameId;
                _heldContacts = contacts;
                _heldCorrection = _character.ModelContactRootCorrectionCm;
                _heldModelContactState = _character.ModelContactState;
                var skeleton = _character.PresentationSkeleton;
                _heldPose = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBonePose).ToArray();
                _entry.Demo.RuntimeContext.ArmWorkerFailureInjection(AlsP3WorkerFailureInjectionStage.BeforePublish,
                    _character.HandleIdentity(_heldFrame + 1));
                _phase = 1;
            }
            else if (_phase == 1 && _character.AnimationRecoveryAttempts > 0)
            {
                Require(_character.AnimationRecoveryAttempts == 1 && _character.RuntimeCommittedFrameId == _heldFrame &&
                    _character.ResultPublishedFrameId == _heldFrame && contacts == _heldContacts &&
                    _character.ModelContactRootCorrectionCm == _heldCorrection &&
                    _character.ModelContactState == _heldModelContactState &&
                    _character.RuntimeDiagnostics.RollbackVerified &&
                    _character.WorkerTransactionRollbackDiagnostics.ControllerRestored,
                    "Failed Mantle candidate changed committed contacts, body correction or publication.");
                for (var bone = 0; bone < _heldPose.Length; bone++)
                    Require(_character.PresentationSkeleton.GetBonePose(bone) == _heldPose[bone],
                        "Failed Mantle candidate changed presentation bone " + bone);
                _phase = 2;
            }
            else if (_phase == 2 && _character.RuntimeCommittedFrameId > _heldFrame)
            {
                Require(_character.AnimationRecoveryAttempts == 0 && _character.FailureDiagnosticCount == 1,
                    "Mantle retry failed or duplicated its diagnostic.");
                _recoveredTick = _tick;
                _phase = 3;
            }
            else if (_phase == 3 && _tick >= _recoveredTick + 5)
            {
                Require(!contacts.Applied && !_character.LatestMotorInput.Mantling.Active &&
                    _character.ModelContactRootCorrectionCm == default,
                    "Interrupted Mantle retained hand support or model body correction.");
                _done = true;
                GD.Print($"MANTLE_CONTACT_ROLLBACK_OK mode={_mode} root={AlsAnimationRuntimeOptions.SampleRootMotion} release={releaseCase} " +
                    $"held_frame={_heldFrame} bones={_heldPose.Length} body_cm={Math.Sqrt(_heldCorrection.LengthSquared):R} " +
                    "publication=retained model_pose=exact contacts=retained recovery=committed release=clear");
                GetTree().Quit();
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Disable(Node node)
    {
        if (node is Node3D spatial) spatial.Hide();
        if (node is CollisionObject3D body) { body.CollisionLayer = 0; body.CollisionMask = 0; }
        foreach (var child in node.GetChildren()) Disable(child);
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception error) { _done = true; GD.PushError(error.ToString()); GetTree().Quit(1); }
    public override void _ExitTree() => Input.ActionRelease("jump");
}
