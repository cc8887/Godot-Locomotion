using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Math;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

internal readonly record struct AlsFullMovementDiagnostics(AlsFrameIdentity Identity, AlsAnimationInputFeedback Feedback,
    AlsGroundedAnimationInput Ground, AlsInAirAnimationInput Global, AlsGraphTraversalCounter Evaluation, int MovementState)
{
    public AlsFrameIdentity RootIdentity { get; init; }
    public AlsBinaryBlendState RootState { get; init; }
    public AlsRagdollFrameDiagnostics Ragdoll { get; init; }
    public int StopTransitions { get; init; }
    public bool HasPoseMovingChannel { get; init; }
    public AlsInertialCurve PoseMoving { get; init; }
    public AlsRefactoredAnimationFeedback RefactoredFeedback { get; init; }
    public AlsRefactoredPoseCurveHistory RefactoredInputPose { get; init; }
    public float RefactoredPrediction { get; init; }
    public bool UsesRefactoredFeet { get; init; }
    public AlsRefactoredFootRigState RefactoredRig { get; init; }
    public AlsRefactoredFootRigPose RefactoredRigFeet { get; init; }
    public AlsFrameIdentity FootPoseIdentity { get; init; }
    public AlsBasedFootLockFrameState RefactoredLocks { get; init; }
    public bool LockCurveProducersMatch { get; init; }
    public AlsProductionGraphCapture? GraphCapture { get; init; }
    public AlsOverlayKind Overlay { get; init; }
}

// Exclusive production adapter. The Worker retains publication authority; the
// native-foot option supplies the final animated pose without legacy correction.
internal sealed class AlsProductionMovementRuntime : IDisposable, IAlsGroundedFrameRuntimeSink
{
    private readonly AlsBaseLayerFrameRuntime _base;
    private readonly AlsLayeredAnimationFrameRuntime? _layered;
    public bool UsesLayeredPose => _layered is not null;
    public bool UsesNativeFootIk => _layered?.UsesNativeFootIk == true;
    public bool UsesRefactoredFeet => _layered?.UsesRefactoredFeet == true;
    public bool CandidatePresentationPending
    {
        get
        {
            RequirePrepared();
            if (!UsesRefactoredFeet) return false;
            var input = _layered!.CandidateRefactoredRigInput;
            // Cold foot transforms need the previous committed final pelvis.
            // An unvisited locomotion branch does not require this foot rig.
            return input.ExecuteRig && !input.FootTransformsValid;
        }
    }
    public AlsRefactoredFootRigState CommittedRefactoredRig => _layered?.CommittedRefactoredRig ?? default;
    public NVector3 CandidatePelvisOffset => UsesRefactoredFeet
        ? new(0, _layered!.CandidateRefactoredRig.PelvisOffset * .01f, 0)
        : AlsFootIkCoordinates.FromNative(CandidateFeet.Pelvis.Offset) * Math.Clamp((float)CandidateFeet.Pelvis.Alpha, 0, 1);
    public float CandidateLeftLock => UsesRefactoredFeet ? _layered!.CandidateRefactoredLocks.Left.Amount : (float)CandidateFeet.LeftLock.Alpha;
    public float CandidateRightLock => UsesRefactoredFeet ? _layered!.CandidateRefactoredLocks.Right.Amount : (float)CandidateFeet.RightLock.Alpha;
    public AlsFootIkPropertyState CandidateFeet => _layered?.CandidateFeet ?? default;
    public AlsBasedFootLockDiagnostics CandidateBasedFeet => _layered?.CandidateBasedFeet ?? default;
    public AlsFootIkPropertyState CommittedFeet => _layered?.CommittedFeet ?? default;
    public AlsBasedFootLockFrameState CommittedBasedFeet => _layered?.CommittedBasedFeet ?? default;
    public AlsBasedFootLockFrameTrace? CommittedBasedTrace => _layered?.CommittedBasedTrace;
    public AlsBinaryBlendState CommittedRoot => _layered?.CommittedRoot ?? default;
    public AlsFrameIdentity CommittedRootIdentity => _layered?.CommittedRootIdentity ?? default;
    public AlsRagdollFrameDiagnostics CommittedRagdoll => _layered?.CommittedRagdoll ?? default;
    private ReadOnlySpan<string> CurveNames => _layered is null ? _base.CurveNames : _layered.CurveNames;
    private ReadOnlySpan<AlsInertialCurve> Curves => _layered is null ? _base.Curves : _layered.Curves;
    private readonly AlsStandingCycleGraph _standing;
    private readonly AlsMovementGraphDefinition _definition;
    private readonly Skeleton3D _skeleton;
    private readonly Node3D _component;
    private readonly long _attachParent;
    private readonly NVector3 _animatedSpeeds;
    private readonly Dictionary<int, AlsTurnProfile> _turnProfiles;
    private readonly AlsLocalPose[] _rollbackPose;
    private readonly int[] _godotToLogical;
    private readonly int _logicalBoneCount;
    private readonly int _leftIk, _rightIk, _leftLock, _rightLock, _rotationAmount, _yawOffset;
    private readonly int _v4LeftLock, _v4RightLock, _refLeftLock, _refRightLock;
    private bool _nextLockProducersMatch, _committedLockProducersMatch;
    private AlsAnimationInputFeedback _feedback, _nextFeedback;
    private readonly int _poseMoving;
    private AlsInertialCurve _committedPoseMoving, _nextPoseMoving;
    private readonly AlsRefactoredPoseCurveReader? _refactoredPoseReader;
    private readonly int _predictionBlock;
    private AlsRefactoredAnimationFeedback _committedRefactoredFeedback, _nextRefactoredFeedback;
    public AlsRefactoredAnimationFeedback CandidateRefactoredFeedback { get { RequirePrepared(); return _nextRefactoredFeedback; } }
    private AlsGraphTraversalCounter _evaluation, _nextEvaluation;
    private AlsFrameIdentity _identity;
    private AlsMovementNotifyState _notifyState, _nextNotifyState;
    private bool _prepared, _applied, _queriesPending;
    private readonly AlsAnimationSetDefinition? _captureSet;
    private readonly string[]? _captureNames;
    private readonly object? _captureSkeleton;
    private readonly bool _captureIdleControls;
    private AlsProductionGraphCapture? _nextGraphCapture, _committedGraphCapture;

    public AlsBaseLayerFrameRuntime Base => _base;
    public AlsAnimationInputFeedback CommittedFeedback => _feedback;
    public AlsCharacterRotationFeedback CandidateRotationFeedback
    {
        get
        {
            RequirePrepared();
            return new(_identity, Curve(_yawOffset), Curve(_rotationAmount),
                _yawOffset >= 0 && Curves[_yawOffset].Present,
                _rotationAmount >= 0 && Curves[_rotationAmount].Present, _nextNotifyState.Action);
        }
    }
    public AlsFrameIdentity Identity => _identity;
    public AlsFullMovementDiagnostics Diagnostics => new(_base.CommittedIdentity, _feedback, _base.CommittedGroundInput,
        _base.CommittedGlobalInput, _evaluation, _base.Movement.CommittedMovement.CurrentState)
        { RootIdentity = CommittedRootIdentity, RootState = CommittedRoot, Ragdoll = CommittedRagdoll,
            StopTransitions = _base.CommittedStopTransitionCount,
            HasPoseMovingChannel = _poseMoving >= 0, PoseMoving = _committedPoseMoving,
            RefactoredFeedback = _committedRefactoredFeedback, RefactoredInputPose = _base.CommittedRefactoredPose,
            RefactoredPrediction = _base.CommittedRefactoredPrediction,
            UsesRefactoredFeet = UsesRefactoredFeet, RefactoredRig = CommittedRefactoredRig,
            RefactoredRigFeet = _layered?.CommittedRefactoredRigFeet ?? default,
            FootPoseIdentity = _layered?.CommittedFootPoseIdentity ?? default,
            RefactoredLocks = UsesRefactoredFeet ? CommittedBasedFeet : default,
            LockCurveProducersMatch = _committedLockProducersMatch, GraphCapture = _committedGraphCapture,
            Overlay = _layered?.CommittedOverlayState.Overlay ?? AlsOverlayKind.Default };
    public AlsAnimationState AnimationState => _base.Movement.Update.State.CurrentState switch
    {
        0 => AlsAnimationState.Grounded, 1 => AlsAnimationState.FallLoop,
        2 => AlsAnimationState.JumpStart, 3 or 6 => AlsAnimationState.LandRecovery,
        _ => throw new InvalidOperationException("A movement conduit cannot be published."),
    };

    public AlsProductionMovementRuntime(AlsMovementGraphDefinition definition, AlsAnimationLibraryBuildResult library,
        AlsStandingCycleGraph standing, AlsAnimationSetDefinition set, AlsPoseAnimationProfile pose, NVector3 animatedSpeeds,
        uint character, uint generation)
    {
        _definition = definition; _standing = standing; _skeleton = library.Skeleton;
        var splitFeet = AlsAnimationRuntimeOptions.Has("--refactored-foot-frame");
        var gravityTwist = AlsAnimationRuntimeOptions.Has("--foot-lock-gravity-twist");
        var pinFinalContact = AlsAnimationRuntimeOptions.Has("--foot-lock-final-contact");
        var correctUnplantedPenetration = AlsAnimationRuntimeOptions.Has("--foot-ground-clearance");
        var pinContactToes = AlsAnimationRuntimeOptions.Has("--foot-contact-toes");
        if (pinContactToes && !pinFinalContact)
            throw new ArgumentException("--foot-contact-toes requires --foot-lock-final-contact.");
        if (correctUnplantedPenetration && !pinFinalContact)
            throw new ArgumentException("--foot-ground-clearance requires --foot-lock-final-contact.");
        if (pinFinalContact && !gravityTwist)
            throw new ArgumentException("--foot-lock-final-contact requires --foot-lock-gravity-twist.");
        if (gravityTwist && !splitFeet)
            throw new ArgumentException("--foot-lock-gravity-twist requires --refactored-foot-frame.");
        if (splitFeet && (!AlsAnimationRuntimeOptions.Has("--refactored-pose-curves") ||
            !AlsAnimationRuntimeOptions.Has("--foot-ik-frame") || !AlsAnimationRuntimeOptions.Has("--based-foot-lock")))
            throw new ArgumentException("Refactored foot dispatch requires --refactored-pose-curves --foot-ik-frame --based-foot-lock.");
        if (AlsAnimationRuntimeOptions.Has("--refactored-pose-curves") && !AlsAnimationRuntimeOptions.Has("--foot-ik-frame"))
            throw new ArgumentException("Refactored production curves require --foot-ik-frame for complete scene/pose inputs.");
        if (AlsAnimationRuntimeOptions.Has("--based-foot-lock") && !AlsAnimationRuntimeOptions.Has("--foot-ik-frame"))
            throw new ArgumentException("Based foot locking requires --foot-ik-frame.");
        _component = (Node3D)library.Root; _animatedSpeeds = animatedSpeeds;
        _attachParent = checked((long)_component.GetParent().GetInstanceId());
        _turnProfiles = pose.Turns.ToDictionary(turn => turn.AnimationId);
        if (definition.Sources.RuntimeStamp==definition.OverlaySharedSources.Sources.RuntimeStamp ||
            definition.Sources.RuntimeStamp==definition.RootSharedSources.Sources.RuntimeStamp)
        {
            _layered=new(definition,library,standing,set,pose,character,generation,AlsAnimationRuntimeOptions.Has("--foot-ik-frame"),
                AlsAnimationRuntimeOptions.Has("--based-foot-lock"), OS.GetCmdlineUserArgs().Contains("--based-foot-lock-trace"), splitFeet,
                gravityTwist ? AlsFootLockBaseRotationMode.GravityTwist : AlsFootLockBaseRotationMode.FullRotation, pinFinalContact, correctUnplantedPenetration, pinContactToes);
            _base=_layered.Base;
        }
        else _base = new(definition, library, standing, set, pose);
        if (splitFeet && !UsesRefactoredFeet) throw new ArgumentException("Refactored feet require the complete shared layered graph.");
        if(OS.GetCmdlineUserArgs().Contains("--production-graph-capture"))
        {
            if(!splitFeet)throw new ArgumentException("Production graph capture requires --refactored-foot-frame.");
            _captureSet=set;
            var capturedSkeleton=definition.OverlayRawSources.GetSkeleton(pose.SkeletonId);
            _captureNames=capturedSkeleton.LogicalBoneNames.ToArray();
            _captureSkeleton=new { names=_captureNames, parents=capturedSkeleton.LogicalParents.ToArray(),
                reference=AlsFullGraphParityCapture.Pose(capturedSkeleton.ReferencePose) };
            _captureIdleControls=OS.GetCmdlineUserArgs().Contains("--production-idle-capture");
        }
        var poseSources = library.MovementSources(set, pose.SkeletonId);
        _godotToLogical = poseSources.GodotToLogical; _logicalBoneCount = poseSources.BoneCount;
        _rollbackPose = new AlsLocalPose[_skeleton.GetBoneCount()];
        _leftIk = CurveNames.IndexOf(splitFeet ? "FootLeftIk" : "Enable_FootIK_L");
        _rightIk = CurveNames.IndexOf(splitFeet ? "FootRightIk" : "Enable_FootIK_R");
        _leftLock = CurveNames.IndexOf(splitFeet ? "FootLeftLock" : "FootLock_L");
        _rightLock = CurveNames.IndexOf(splitFeet ? "FootRightLock" : "FootLock_R");
        _v4LeftLock = CurveNames.IndexOf("FootLock_L"); _v4RightLock = CurveNames.IndexOf("FootLock_R");
        _refLeftLock = CurveNames.IndexOf("FootLeftLock"); _refRightLock = CurveNames.IndexOf("FootRightLock");
        _rotationAmount = CurveNames.IndexOf("RotationAmount");
        _yawOffset = CurveNames.IndexOf("YawOffset");
        _poseMoving = CurveNames.IndexOf("PoseMoving");
        _predictionBlock = CurveNames.IndexOf("GroundPredictionBlock");
        if (AlsAnimationRuntimeOptions.Has("--refactored-pose-curves")) _refactoredPoseReader = new(CurveNames);
        if (_standing.RefactoredMovementCurves is not null && _poseMoving < 0)
            throw new InvalidOperationException("Production root lost the movement cache curve layout.");
    }

    public void Prepare(in AlsFrameInput input, in AlsFrameResult result)
    {
        if (UsesRefactoredFeet) throw new InvalidOperationException("Refactored feet require split production dispatch.");
        var transform = _component.GlobalTransform;
        var componentPose = new AlsLocalPose(ToNumerics(transform.Origin), ToNumerics(transform.Basis.GetRotationQuaternion()), ToNumerics(transform.Basis.Scale));
        PrepareCore(input, result, componentPose);
    }

    // Pure first half: the owning worker supplies its future root transform as a
    // value snapshot. No Skeleton/Node access is allowed across this boundary.
    public AlsFootRigQueries PrepareFootQueries(in AlsFrameInput input, in AlsFrameResult result, in AlsLocalPose componentPose)
    {
        if (!UsesRefactoredFeet) throw new InvalidOperationException("No split foot dispatcher.");
        return PrepareCore(input, result, componentPose);
    }

    private AlsFootRigQueries PrepareCore(in AlsFrameInput input, in AlsFrameResult result, in AlsLocalPose componentPose)
    {
        if (_prepared || _queriesPending) throw new InvalidOperationException("A production movement candidate is already pending.");
        if (input.Identity != result.Identity || !AlsLocomotionModel.HasPendingSourceTiming(result))
            throw new ArgumentException("Production movement requires matching unresolved model inputs.");
        if (input.CharacterRotation.Applied != 1)
            throw new ArgumentException("Production movement requires character rotation before animation gathering.");
        if (_layered is null && input.Command.RequestedOverlay != AlsOverlayKind.Default)
            throw new InvalidOperationException("Non-default Overlay requires the layered animation owner.");
        _identity = input.Identity;
        _nextEvaluation = _evaluation.Next(checked((ulong)input.Identity.FrameId));
        var movement = _standing.CreateMovementInput(input);
        var rules = new AlsGroundedRuleInput(movement.ShouldMove, false, false, result.ActualStance,
            _notifyState.Action == AlsTimelineAction.None, _notifyState.Entry == AlsTimelineGroundedEntryMode.FromRoll, 0, 0)
        {
            MovementState = result.ResolvedLocomotionState switch
            {
                AlsLocomotionState.Grounded => AlsMovementStateInput.Grounded,
                AlsLocomotionState.InAir => AlsMovementStateInput.InAir,
                _ => throw new InvalidOperationException("Movement graph has no pose owner for this locomotion state."),
            },
            HasMovementInput = movement.HasMovementInput, Speed = movement.Speed,
        };
        var ground = new AlsGroundedFrameInputs(input.DeltaTime, _animatedSpeeds, 1,
            _base.Grounded.CommittedMain.Main.RecordedWeight, 1, default, default, AlsSlotWeights.Passthrough,
            new(0, 0), new(0, 0), _nextEvaluation);
        try
        {
            var parent = _attachParent;
            if (_layered is null)
            {
                _base.PrepareFromFrame(input, result, movement, rules, ground, _feedback,
                    new AlsPoseUpdateContext(input.Identity, 1, input.DeltaTime), this, MathF.Abs(componentPose.Scale.Y));
                _base.Evaluate(componentPose,parent,_definition.ComponentTeleportDistance);
            }
            else
            {
                _layered.PrepareFromFrame(input,result,movement,rules,ground,new(input.Identity,1,input.DeltaTime),this,
                    componentPose,parent,_definition.ComponentTeleportDistance,overlayKind:input.Command.RequestedOverlay,
                    meshVerticalScale:MathF.Abs(componentPose.Scale.Y));
                if (UsesRefactoredFeet)
                {
                    var queries = _layered.PrepareFootQueries(componentPose, parent, _definition.ComponentTeleportDistance);
                    if(_captureSet is not null)
                        _nextGraphCapture=AlsProductionGraphCapture.Capture(_layered,_captureSet,_definition,
                            _captureNames!,input,result,movement,componentPose,_captureIdleControls) with { Skeleton=_captureSkeleton };
                    _queriesPending = true;
                    return queries;
                }
                _layered.Evaluate(componentPose,parent,_definition.ComponentTeleportDistance);
            }
            CompleteEvaluation();
            return default;
        }
        catch { Discard(); throw; }
    }

    public void ResumeFootQueries(in AlsFootRigObservations observations)
    {
        if (!_queriesPending || observations.Queries.Identity != _identity)
            throw new InvalidOperationException("No matching split movement candidate.");
        try
        {
            _layered!.ResumeFootQueries(observations);
            CompleteEvaluation();
            _queriesPending = false;
        }
        catch { Discard(); throw; }
    }

    private void CompleteEvaluation()
    {
        if(_nextGraphCapture is { } graphCapture)
        {
            graphCapture.Row["pose"]=AlsFullGraphParityCapture.Pose(_layered!.Pose);
            graphCapture.Row["curves"]=AlsFullGraphParityCapture.Curves(CurveNames,Curves);
        }
        // The component is attached to a stable character rig. Movement-base
        // changes do not reparent this component; they must not reset inertia.
        _nextFeedback = AlsAnimationInputFeedback.FromCompletedFrame(_identity, CurveNames, Curves);
        _nextPoseMoving = _poseMoving >= 0 ? Curves[_poseMoving] : default;
        _nextLockProducersMatch = _refLeftLock >= 0 && _refRightLock >= 0 && _v4LeftLock >= 0 && _v4RightLock >= 0 &&
            Curves[_refLeftLock] == Curves[_v4LeftLock] && Curves[_refRightLock] == Curves[_v4RightLock];
        if (_refactoredPoseReader is not null)
            _nextRefactoredFeedback = new(_refactoredPoseReader.Read(_identity, Curves), Curve(_predictionBlock));
        _nextNotifyState = _notifyState.Advance(_base.SourceEvents, _base.ResetGroundedEntry);
        _prepared = true;
    }

    public void CompleteTiming(ref AlsRuntimeState state, ref AlsFrameResult result)
    {
        RequirePrepared();
        var ground = _base.CandidateGroundInput;
        var rate = result.ActualStance == AlsStance.Crouching ? ground.CrouchingPlayRate : ground.StandingPlayRate;
        var phase = _base.Grounded.StandingFrame.State.Phase;
        if (result.ResolvedLocomotionState == AlsLocomotionState.InAir)
        {
            rate = _base.CandidateJumpInput.Frame.PlayRate;
            phase = RelevantSourcePhase();
        }
        else if (result.ActualStance == AlsStance.Crouching || AnimationState != AlsAnimationState.Grounded)
            phase = RelevantSourcePhase();
        AlsLocomotionModel.CompleteMovementSourceTiming(_identity, new(ground.Stride, rate, phase), ref state, ref result);
        result.AnimationState = AnimationState; result.Lean = _base.CandidateGlobalInput.Lean;
    }

    private float RelevantSourcePhase()
    {
        var sync = _base.Sources; var weight = -1f; var phase = 0f;
        for (var i = 0; i < sync.PlayerCount; i++)
        {
            var history = sync.Players[i]; var binding = _definition.Sources.Players[history.PlayerId];
            if (_layered is not null && history.PlayerId>=_definition.OverlaySharedSources.MovementPlayerCount) continue;
            if (binding.Kind == AlsLocomotionSourceKind.TeleportEvaluator || sync.CachedWeights[history.PlayerId] <= weight) continue;
            weight = sync.CachedWeights[history.PlayerId];
            phase = binding.Kind == AlsLocomotionSourceKind.BlendSpace ? history.Time :
                history.Time / _definition.Sources.Samples[binding.SampleStart].DurationSeconds;
        }
        return AlsLocomotionSourceTiming.NormalizeLoopingPhase(phase);
    }

    public AlsFootCurveSample FootCurves()
    {
        RequirePrepared();
        return new(Math.Clamp(Curve(_leftIk), 0, 1), Math.Clamp(Curve(_rightIk), 0, 1),
            Math.Clamp(Curve(_leftLock), 0, 1), Math.Clamp(Curve(_rightLock), 0, 1));
    }

    public void CompleteRotation(in AlsFrameInput input, ref AlsRuntimeState state, ref AlsFrameResult result)
    {
        RequirePrepared();
        // Character rotation has already run before this animation input was
        // gathered. Publish diagnostics only; never rotate from the new pose.
        if (input.Floor.IsGrounded == 0 || _base.CandidateGroundInput.ShouldMove || _notifyState.Action != AlsTimelineAction.None) return;
        var delta = input.CharacterRotation.ActorYawDelta;
        var idle = _base.CandidateControlInput.State.Idle;
        if (idle.RotateLeft || idle.RotateRight)
        {
            result.RotateActive = 1; result.RotateDirection = (sbyte)(idle.RotateLeft ? -1 : 1);
            result.RotatePlayRate = idle.RotateRate; result.RotateYawDelta = delta;
            state.YawSource = AlsYawSource.RotateInPlace;
        }
        else if (_base.Montages.Evaluation.Length > 0)
        {
            var relevantWeight = AlsPoseBlender.WeightThreshold;
            foreach (var montage in _base.Montages.Evaluation)
            {
                if (montage.ActionDefinitionId >= 0 || montage.Weight <= relevantWeight) continue;
                if (!_turnProfiles.TryGetValue(montage.AnimationId,out var turn)) continue;
                var instanceFound = false;
                foreach (var instance in _base.Montages.Candidate)
                {
                    if (instance.InstanceId != montage.InstanceId) continue;
                    // Public phase/rate are normalized. The physical montage
                    // continues to own seconds, blend weight and playback time.
                    result.TurnPhase = Math.Clamp(montage.Position / instance.Duration, 0, 1);
                    result.TurnPlayRate = instance.PlayRate / instance.Duration;
                    instanceFound = true; break;
                }
                if (!instanceFound) throw new InvalidOperationException("Evaluated turn has no physical montage instance.");
                relevantWeight = montage.Weight;
                result.TurnActive = 1; result.TurnAnimationId = montage.AnimationId;
                result.TurnDirection = turn.Direction; result.TurnNominalDegrees = turn.NominalDegrees;
                result.TurnYawDelta = delta; state.YawSource = AlsYawSource.TurnInPlace;
            }
        }
    }

    public void CompleteEvents(ref AlsFrameResult result)
    {
        RequirePrepared();
        _base.CompleteEvents(ref result);
    }

    public void Apply()
    {
        RequirePrepared();
        if (_applied) throw new InvalidOperationException("Movement pose was already applied.");
        // Capture only on the visual worker immediately before its first write.
        for (var bone = 0; bone < _rollbackPose.Length; bone++) _rollbackPose[bone] = ReadBone(bone);
        try { WriteLogicalPose(_layered is null ? _base.Pose : _layered.Pose); _applied = true; }
        catch { WritePhysicalPose(_rollbackPose); throw; }
    }
    public void Commit()
    {
        RequirePrepared();
        if (!_applied) throw new InvalidOperationException("Unapplied movement pose cannot be committed.");
        if (_layered is null) _base.Commit(_identity); else _layered.Commit(_identity);
        if (_nextGraphCapture is not null)
            _nextGraphCapture.Row["postRigComponents"]=_layered!.CommittedRefactoredRigPose.ToArray().Select(t=>new {
                position=new[]{t.Position.X,t.Position.Y,t.Position.Z},
                rotation=new[]{t.Rotation.X,t.Rotation.Y,t.Rotation.Z,t.Rotation.W},
                scale=new[]{t.Scale.X,t.Scale.Y,t.Scale.Z} }).ToArray();
        _feedback = _nextFeedback; _evaluation = _nextEvaluation;
        _committedPoseMoving = _nextPoseMoving;
        _committedLockProducersMatch = _nextLockProducersMatch;
        _committedRefactoredFeedback = _nextRefactoredFeedback;
        _committedGraphCapture = _nextGraphCapture; _nextGraphCapture = null;
        _notifyState = _nextNotifyState; _prepared = _applied = false;
    }
    public void Discard()
    {
        try { if (_applied) WritePhysicalPose(_rollbackPose); }
        finally { if (_layered is null) _base.Discard(); else _layered.Discard(); _nextGraphCapture = null; _prepared = _applied = _queriesPending = false; }
    }

    private float Curve(int index) => index >= 0 && Curves[index].Present ? Curves[index].Value : 0;
    private AlsLocalPose ReadBone(int bone) => new(ToNumerics(_skeleton.GetBonePosePosition(bone)),
        ToNumerics(_skeleton.GetBonePoseRotation(bone)), ToNumerics(_skeleton.GetBonePoseScale(bone)));
    private void WriteLogicalPose(ReadOnlySpan<AlsLocalPose> poses)
    {
        if (poses.Length != _logicalBoneCount) throw new ArgumentException("Incomplete logical movement pose.");
        for (var bone = 0; bone < _godotToLogical.Length; bone++) WriteBone(bone, poses[_godotToLogical[bone]]);
    }
    private void WritePhysicalPose(ReadOnlySpan<AlsLocalPose> poses)
    {
        if (poses.Length != _rollbackPose.Length) throw new ArgumentException("Invalid physical rollback pose.");
        for (var bone = 0; bone < poses.Length; bone++) WriteBone(bone, poses[bone]);
    }
    private void WriteBone(int bone, in AlsLocalPose pose)
    {
        _skeleton.SetBonePosePosition(bone, new(pose.Position.X, pose.Position.Y, pose.Position.Z));
        _skeleton.SetBonePoseRotation(bone, new(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W));
        _skeleton.SetBonePoseScale(bone, new(pose.Scale.X, pose.Scale.Y, pose.Scale.Z));
    }
    private static NVector3 ToNumerics(Vector3 value) => new(value.X, value.Y, value.Z);
    private static NQuaternion ToNumerics(Quaternion value) => new(value.X, value.Y, value.Z, value.W);
    private void RequirePrepared() { if (!_prepared) throw new InvalidOperationException("No production movement candidate."); }
    public void RefreshSourceBones(int cache) { }
    public void UpdateGroundedSlot(int slot, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context)
    {
        var frame = _base.Montages.Frame;
        if (slot != _definition.Grounded.Runtime.SlotNodeIndex || frame.Identity != context.Identity ||
            weights != frame.SlotWeights(AlsMontageSlot.Grounded))
            throw new InvalidOperationException("Grounded slot update differs from its physical montage frame.");
    }
    public void RequestInertialization(in AlsPoseUpdateContext context, float seconds) =>
        throw new InvalidOperationException("Movement inertialization escaped the BaseLayer owner.");
    public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
        throw new InvalidOperationException("Unknown external cached update handler.");
    public void Dispose() { Discard(); if (_layered is null) _base.Dispose(); else _layered.Dispose(); }
}
