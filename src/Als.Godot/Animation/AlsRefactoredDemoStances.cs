using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

/// <summary>Migration boundary: original Refactored stances, Grounded and
/// Transition Slot feed the existing air/layer/foot owners. All mutable data belongs
/// to this character; no Godot API is used during Prepare/Evaluate/Commit.</summary>
internal sealed class AlsRefactoredDemoStances
{
    private readonly AlsRefactoredCharacterActionProfile _profile;
    private AlsRefactoredCharacterActionRuntime? _runtime;
    private readonly int[] _bones, _nativeCurves;
    private readonly string[] _names;
    private readonly AlsPrecisePose[] _targetComponents;
    private readonly int[] _targetParents;
    private readonly AlsVirtualBoneDefinition[] _targetVirtuals;
    private readonly AlsInertialCurve[] _nativeValues, _previousValues, _nextValues;
    private readonly AlsRefactoredPoseInertia _inertia;
    private readonly (int Native, int Legacy, float Scale)[] _aliases;
    private AlsRefactoredStandingHostInput _input;
    private AlsFrameIdentity _identity;
    private AlsGraphTraversalCounter _counter, _nextCounter;
    private float _delta;
    private bool _prepared, _visited, _standing, _crouching, _evaluated, _post;
    internal long CommittedStandingFrames { get; private set; }
    internal long CommittedCrouchingFrames { get; private set; }
    internal long CommittedTransitionFrames { get; private set; }
    internal long CommittedGroundedFrames { get; private set; }
    internal int CommittedGroundedStateMask { get; private set; }
    internal bool ResetGroundedEntry => _visited && SourceUpdated && _runtime!.Grounded!.ResetEntryMode;
    internal int StandingState => _standing ? _runtime!.Standing.State : -1;
    internal int CrouchingState => _crouching ? _runtime!.Crouching!.State : -1;
    internal bool SourceUpdated => _visited && _runtime!.Transition.SourceUpdate.Updated;

    internal AlsRefactoredDemoStances(AlsSkeletonDefinition skeleton, ReadOnlySpan<AlsPrecisePose> reference, ReadOnlySpan<string> names)
    {
        _profile = AlsRefactoredDemoResources.Profile.Value; _names = names.ToArray();
        _bones = _profile.BoneNames.ToArray().Select(skeleton.GetLogicalBoneId).ToArray();
        _targetParents=skeleton.LogicalBones.Select(b=>b.ParentLogicalId).ToArray();
        _targetVirtuals=skeleton.VirtualBones;
        for(var b=0;b<_bones.Length;b++)
        {
            if(_bones[b]<0)
            {if(!AlsRefactoredDemoResources.NativeVirtuals.Any(v=>v.Bone==b))throw new ArgumentException("Missing physical Refactored bone: "+_profile.BoneNames[b]);continue;}
            var parent=_profile.Parents[b];var target=_bones[b];
            if(_targetParents[target]!=(parent<0?-1:_bones[parent]))throw new ArgumentException("Refactored physical hierarchy differs.");
            var expected=FromNative(AlsRefactoredDemoResources.NativeReference[b]);var actual=reference[target];
            if((expected.Position-actual.Position).LengthSquared>4e-8||1-Math.Abs(AlsQuaternion.Dot(expected.Rotation,actual.Rotation))>1e-5)
                throw new ArgumentException("Refactored physical rest requires retargeting: "+_profile.BoneNames[b]);
        }
        if(skeleton.PhysicalBones.Any(b=>!_bones.Contains(b.LogicalId)))throw new ArgumentException("Unmapped Demo physical bone.");
        _targetComponents=new AlsPrecisePose[reference.Length];
        var nativeNames = _profile.CurveNames.ToArray();
        _nativeCurves = nativeNames.Select(n => Array.IndexOf(_names, n)).ToArray();
        if (_nativeCurves.Any(i => i < 0)) throw new ArgumentException("Demo curve layout omitted Refactored curves.");
        _nativeValues = new AlsInertialCurve[nativeNames.Length]; _previousValues = new AlsInertialCurve[nativeNames.Length];
        _nextValues = new AlsInertialCurve[nativeNames.Length];
        _inertia = new(_bones.Length, nativeNames, "RotationYawSpeed");
        var aliases = new Dictionary<string, string>
        {
            ["PoseGait"]="Weight_Gait", ["PoseCrouching"]="BasePose_CLF", ["PoseStanding"]="BasePose_N",
            ["FootPlanted"]="Feet_Position", ["FeetCrossing"]="Feet_Crossing", ["HipsDirectionLock"]="HipOrientation_Bias",
            ["AllowTransitions"]="Enable_Transition", ["GroundPredictionBlock"]="Mask_LandPrediction",
            ["FootLeftIk"]="Enable_FootIK_L", ["FootRightIk"]="Enable_FootIK_R",
            ["FootLeftLock"]="FootLock_L", ["FootRightLock"]="FootLock_R", ["SprintBlock"]="Mask_Sprint",
            ["RotationYawOffset"]="YawOffset", ["RotationYawSpeed"]="RotationAmount",
            ["LayeringHead"]="Layering_Head", ["LayeringSpine"]="Layering_Spine", ["LayeringPelvis"]="Layering_Pelvis",
            ["LayeringLegs"]="Layering_Legs", ["LayeringArmLeft"]="Layering_Arm_L", ["LayeringArmRight"]="Layering_Arm_R",
            ["LayeringHandLeft"]="Layering_Hand_L", ["LayeringHandRight"]="Layering_Hand_R",
            ["LayeringArmLeftLocalSpace"]="Layering_Arm_L_LS", ["LayeringArmRightLocalSpace"]="Layering_Arm_R_LS",
        };
        _aliases = aliases.Select(p => (Native: Array.IndexOf(nativeNames, p.Key), Legacy: Array.IndexOf(_names, p.Value),
            Scale: p.Key == "RotationYawSpeed" ? 0f : 1f)).Where(p => p.Native >= 0 && p.Legacy >= 0).ToArray();
    }

    internal void Begin(in AlsFrameInput frame, in AlsFrameResult result, in AlsStandingMovementInput movement)
    {
        if (_prepared) throw new InvalidOperationException("Refactored Demo frame still pending.");
        _identity = frame.Identity; _delta = frame.DeltaTime;
        _runtime ??= _profile.CreateRuntime(_identity.CharacterId, _identity.SlotGeneration);
        _nextCounter = _counter.HasUpdated ? _counter.Next((ulong)frame.Identity.FrameId) : new(0, (ulong)frame.Identity.FrameId);
        var pending = _runtime.CommittedIdentity.SlotGeneration == 0;
        var velocity = AlsFootIkCoordinates.ToNative(frame.ActualVelocity);
        var yaw = -frame.CharacterYaw * (180d / Math.PI); var view = -frame.Command.ViewYaw * (180d / Math.PI);
        var half = yaw * (Math.PI / 360d);
        var nativeRotation = new AlsQuaternion(0, 0, Math.Sin(half), Math.Cos(half));
        var speed = movement.Speed * 100f;
        var velocityYaw = speed > .01f ? (float)(Math.Atan2(velocity.Y, velocity.X) * (180d / Math.PI)) : (float)yaw;
        var direction = AlsFootIkCoordinates.ToNative(frame.InputDirection);
        var inputYaw = movement.HasMovementInput ? Math.Atan2(direction.Y,direction.X)*(180d/Math.PI) : yaw;
        var gait = Value("PoseGait"); var grounded = Value("PoseGrounded");
        var unweightedGait = grounded > 1e-8f ? gait / grounded : gait;
        var moving = movement.IsMoving;
        var smooth = movement.ShouldMove;
        var crouch = result.ActualStance == AlsStance.Crouching;
        var rotation = result.ActualRotationMode switch { AlsRotationMode.VelocityDirection => AlsRefactoredRestRotation.VelocityDirection,
            AlsRotationMode.Aiming => AlsRefactoredRestRotation.Aiming, _ => AlsRefactoredRestRotation.ViewDirection };
        var nativeMovement = new AlsRefactoredMovementInput(velocity, AlsFootIkCoordinates.ToNative(frame.ActualAcceleration), nativeRotation,
            speed, 1, velocityYaw, view, frame.MaxAcceleration * 100f, frame.MaxBrakingDeceleration * 100f,
            result.ActualGait switch { AlsGait.Walking => "Als.Gait.Walking", AlsGait.Sprinting => "Als.Gait.Sprinting", _ => "Als.Gait.Running" },
            rotation == AlsRefactoredRestRotation.VelocityDirection, pending, _delta,
            Math.Clamp(unweightedGait-1,0,1), Math.Clamp(unweightedGait-2,0,1), Value("HipsDirectionLock"), Value("SprintBlock"));
        var relativeYaw = (float)((view-yaw)%360); if(relativeYaw>180)relativeYaw-=360; if(relativeYaw< -180)relativeYaw+=360;
        var rest = new AlsRefactoredRestInput(_delta, relativeYaw, frame.AimYawRateDegrees, moving, frame.FirstPerson == 1,
            rotation, crouch ? AlsRefactoredRestStance.Crouching : AlsRefactoredRestStance.Standing,
            Value("AllowTransitions") > .99f, pending, 1, 0, 0, default, default, default, default);
        _input = new(nativeMovement, rest, new(grounded, Math.Clamp(unweightedGait-1,0,1),1,Value("FeetCrossing")),
            new(rotation == AlsRefactoredRestRotation.VelocityDirection, crouch, movement.HasMovementInput, inputYaw, yaw, yaw),
            Math.Clamp(Value("FootPlanted"),-1,1), smooth);
        try
        {
            _runtime.BeginGlobal(Context(1), _input, pending);
            if (result.ResolvedLocomotionState != AlsLocomotionState.Grounded) _runtime.StopTransitions();
            _previousValues.CopyTo(_nextValues,0); _prepared = true;
        }
        catch { Discard(); throw; }
    }
    private float Value(string name)
    { var i=_profile.CurveNames.IndexOf(name); return i>=0&&_previousValues[i].Present?_previousValues[i].Value:0; }
    private AlsPoseUpdateContext Context(float weight) => new AlsPoseUpdateContext(_identity,weight,_delta).WithUpdateCounter(_nextCounter);

    internal void Prepare(in AlsMainGroundedCachedUpdate outer, AlsGraphTraversalCounter initialization, bool fromRoll)
    {
        if (!_prepared || _visited) throw new InvalidOperationException("Invalid Demo stance traversal.");
        if (!outer.MainUpdated) return;
        var context = Context(outer.MainContext.Weight);
        _runtime!.Transition.Prepare(context);
        var source = _runtime.Transition.SourceUpdate;
        _inertia.Prepare(context);
        if (_runtime.Transition.InertializationRequest is {} request) _inertia.Request(request.Duration);
        if (source.Updated)
        {
            _runtime.Grounded!.Prepare(source.Context,_input,Value("PoseStanding"),Value("PoseCrouching"),fromRoll,initialization);
            _standing=_runtime.Grounded.StandingUpdated;_crouching=_runtime.Grounded.CrouchingUpdated;
        }
        _visited=true;
    }
    internal void EvaluateStance(bool crouch, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if (!_visited || crouch&&!_crouching || !crouch&&!_standing) throw new InvalidOperationException("Unvisited Refactored stance.");
        // Mesh-space histories use a fixed component here; the enclosing existing
        // graph owns world-space movement/teleport inertia during migration.
        if(crouch)
        { var host=_runtime!.Crouching!;host.Evaluate(AlsPrecisePose.Identity);MapOut(host.Pose,host.Curves,host.CurveNames,pose,curves); }
        else
        { var host=_runtime!.Standing;host.Evaluate(AlsPrecisePose.Identity);MapOut(host.Pose,host.Curves,host.CurveNames,pose,curves); }
    }
    internal void FinishGrounded(Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if(!_visited)throw new InvalidOperationException("No Refactored Grounded boundary.");
        if(SourceUpdated)_runtime!.Grounded!.Evaluate(AlsPrecisePose.Identity);
        _runtime!.Transition.Evaluate(SourceUpdated?_runtime.Grounded!.Pose:ReadOnlySpan<AlsPrecisePose>.Empty,
            SourceUpdated?_runtime.Grounded!.Curves:ReadOnlySpan<AlsInertialCurve>.Empty);
        _inertia.Evaluate(_runtime.Transition.Pose,_runtime.Transition.Curves,AlsPrecisePose.Identity);
        MapOut(_inertia.Pose,_inertia.Curves,_profile.CurveNames,pose,curves);
        _inertia.Curves.CopyTo(_nextValues);_evaluated=true;
    }
    private void MapOut(ReadOnlySpan<AlsPrecisePose> native,ReadOnlySpan<AlsInertialCurve> values,ReadOnlySpan<string> names,
        Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {
        for(var b=0;b<_bones.Length;b++)if(_bones[b]>=0)pose[_bones[b]]=FromNative(native[b]);
        foreach(var v in _targetVirtuals)pose[v.LogicalBoneId]=AlsPrecisePose.Identity;
        Components(pose,_targetParents,_targetComponents);
        foreach(var v in _targetVirtuals)
        {pose[v.LogicalBoneId]=AlsPrecisePose.Relative(_targetComponents[v.TargetLogicalBoneId],_targetComponents[v.SourceLogicalBoneId]).Normalized();_targetComponents[v.LogicalBoneId]=_targetComponents[v.TargetLogicalBoneId];}
        curves.Clear(); Array.Clear(_nativeValues);
        for(var c=0;c<names.Length;c++)
        {var i=_profile.CurveNames.IndexOf(names[c]);if(i<0)throw new InvalidOperationException("Foreign stance curve.");_nativeValues[i]=values[c];curves[_nativeCurves[i]]=values[c];}
        foreach(var alias in _aliases)curves[alias.Legacy]=AlsStandingCycleCurves.Scale(_nativeValues[alias.Native],alias.Scale==0?_delta:alias.Scale);
    }
    internal void PostUpdate(){if(!_prepared||_post)throw new InvalidOperationException("Invalid Demo post update.");_runtime!.PostUpdateActions();_post=true;}
    internal void CaptureFeedback(ReadOnlySpan<string> names,ReadOnlySpan<AlsInertialCurve> curves)
    {
        if(!_prepared||names.Length!=curves.Length)throw new ArgumentException("Foreign Refactored feedback.");
        for(var c=0;c<_nextValues.Length;c++)
        {var i=names.IndexOf(_profile.CurveNames[c]);_nextValues[c]=i<0?default:curves[i];}
    }
    internal void ValidateCommit(AlsFrameIdentity id)
    {if(!_prepared||!_post||id!=_identity)throw new InvalidOperationException("Incomplete Demo stance frame.");_runtime!.ValidateCommit(id);if(_visited)_inertia.ValidateCommit(id);}
    internal void Commit(AlsFrameIdentity id)
    {
        ValidateCommit(id);
        var groundedState=_visited&&SourceUpdated?_runtime!.Grounded!.State:-1;
        _runtime!.Commit(id);if(_visited)_inertia.Commit(id);
        if(groundedState>=0){CommittedGroundedFrames++;CommittedGroundedStateMask|=1<<groundedState;}
        if(_standing)CommittedStandingFrames++;if(_crouching)CommittedCrouchingFrames++;if(_evaluated)CommittedTransitionFrames++;
        _nextValues.CopyTo(_previousValues,0);_counter=_nextCounter;Clear();
    }
    internal void Discard(){_runtime?.Discard();_inertia.Cancel();Clear();}
    private void Clear(){_prepared=_visited=_standing=_crouching=_evaluated=_post=false;}
    private static void Components(ReadOnlySpan<AlsPrecisePose> local,ReadOnlySpan<int> parents,Span<AlsPrecisePose> components)
    {
        for(var b=0;b<parents.Length;b++)components[b]=parents[b]<0?local[b]:AlsPrecisePose.Compose(local[b],components[parents[b]]).Normalized();
    }
    private static AlsPrecisePose FromNative(AlsPrecisePose value)=>new(new(value.Position.X*.01,-value.Position.Y*.01,value.Position.Z*.01),
        new(-value.Rotation.X,value.Rotation.Y,-value.Rotation.Z,value.Rotation.W),value.Scale);
}
