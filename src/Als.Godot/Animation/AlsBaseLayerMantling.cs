using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

internal sealed partial class AlsBaseLayerFrameRuntime
{
    private readonly AlsMantlingDemoResources? _mantleResources;
    private readonly AlsMantlingHostResources? _mantleHost;
    private readonly AlsMantlingBranchingRuntime? _mantleBranches;
    private readonly AlsMontageSlotPose? _mantleSlot;
    private readonly IAlsMontagePoseSource? _mantlePose;
    private AlsMantlingFrame _committedMantling, _candidateMantling;
    private long _committedMantleInstance, _candidateMantleInstance;
    private AlsMantlingBranchInputs _committedMantleInputs = new(false, "", "", ""), _candidateMantleInputs;
    private bool _locomotionUpdated;
    private bool _committedMantleEnded, _candidateMantleEnded;

    private bool MantlePhysicalActionEnded()
    {
        if (!_committedMantling.Active) return false;
        // Native Character observes the previous animation update's branching
        // action. This pre-physics montage tick must not stop the root source
        // one physical step earlier than the pose's ActionEnd marker.
        if (_committedMantleEnded) return true;
        foreach (var instance in _montages.Candidate)
            if (instance.InstanceId == _committedMantleInstance && instance.Playing && !instance.Interrupted) return false;
        return true;
    }

    private void BeginMantleMontages(AlsFrameIdentity identity, float delta, bool ragdoll)
    {
        _mantleBranches?.Capture(_committedMantleInputs, _committedMantling.Started ? "Als.LocomotionAction.Mantling" : _committedMantling.Active ? null : "");
        AlsMontagePositionOverride? seek = null;
        if (_committedMantling.Active && !ragdoll && _committedMantleInstance > 0)
            foreach (var instance in _montages.Committed)
                if (instance.InstanceId == _committedMantleInstance && instance.Playing && !instance.Interrupted)
                    seek = new(instance.InstanceId, MathF.Min(instance.Duration, _committedMantling.PositionBeforeAdvance(delta)));
        _actions.Begin(identity, delta, ragdoll, seek);
        _candidateMantleEnded = _committedMantling.Active && _mantleBranches?.CandidateAction != "Als.LocomotionAction.Mantling";
    }

    private void PrepareMantling(in AlsFrameInput frame)
    {
        _candidateMantling = frame.Mantling; _candidateMantleInstance = _committedMantleInstance;
        _candidateMantleInputs = new(frame.InputDirection.LengthSquared() > 0,
            frame.Mantling.Active ? "" : frame.Floor.IsGrounded == 1 ? "Als.LocomotionMode.Grounded" : "Als.LocomotionMode.InAir",
            frame.RotationMode == AlsRotationMode.Aiming ? "Als.RotationMode.Aiming" : "Als.RotationMode.ViewDirection",
            frame.Stance == AlsStance.Crouching ? "Als.Stance.Crouching" : "Als.Stance.Standing");
        if (frame.Mantling.Started)
        {
            if (_mantleHost is null || !frame.Mantling.Active) throw new InvalidOperationException("Unbound mantle gameplay request.");
            var definition = _mantleHost.ActionIds[frame.Mantling.DefinitionId];
            if (!_montages.PlayAction(definition, 1)) throw new InvalidOperationException("Mantle montage is absent from the physical bank.");
            _candidateMantleInstance = _montages.ActiveActionInstance(definition);
        }
        if (_committedMantling.Active && (frame.Mantling.Interrupted || _cancelForRuntimeFailure))
            _montages.StopInstance(_committedMantleInstance, .3f, AlsActionBlendOption.HermiteCubic);
    }

    private void ApplyMantleCurveAliases()
    {
        if (_montages.SlotWeights(AlsMontageSlot.PostLocomotion).SlotNodeWeight <= AlsPoseBlender.WeightThreshold) return;
        Copy("PoseStanding", "BasePose_N"); Copy("PoseCrouching", "BasePose_CLF");
        Copy("FootLeftIk", "Enable_FootIK_L"); Copy("FootRightIk", "Enable_FootIK_R");
        Copy("FootLeftLock", "FootLock_L"); Copy("FootRightLock", "FootLock_R");
        Copy("LayeringArmLeft", "Layering_Arm_L"); Copy("LayeringArmRight", "Layering_Arm_R");
        Copy("LayeringSpine", "Layering_Spine"); Copy("LayeringHead", "Layering_Head");
        Copy("LayeringLegs", "Layering_Legs"); Copy("LayeringPelvis", "Layering_Pelvis");
        void Copy(string native, string legacy)
        {
            var a = CurveNames.IndexOf(native); var b = CurveNames.IndexOf(legacy);
            if (a >= 0 && b >= 0) _rawCurves[b] = _rawCurves[a];
        }
    }
}
