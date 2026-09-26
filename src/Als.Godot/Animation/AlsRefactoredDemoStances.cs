using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

/// <summary>Migration boundary: original Refactored Grounded, Transition and
/// Locomotion feed the existing layer/foot owners. All mutable data belongs
/// to this character; no Godot API is used during Prepare/Evaluate/Commit.</summary>
internal sealed class AlsRefactoredDemoStances
{
    private readonly AlsRefactoredCharacterActionProfile _profile;
    private readonly AlsRefactoredLocomotionHostProfile _locomotion;
    private AlsRefactoredLocomotionHost? _host;
    private AlsRefactoredCharacterActionRuntime? _runtime=>_host?.Actions;
    private ReadOnlySpan<string> NativeNames=>_locomotion.Pose.CurveNames;
    private readonly int[] _bones, _nativeCurves;
    private readonly string[] _names;
    private readonly AlsPrecisePose[] _targetComponents;
    private readonly int[] _targetParents;
    private readonly AlsVirtualBoneDefinition[] _targetVirtuals;
    private readonly AlsInertialCurve[] _nativeValues, _previousValues, _nextValues;
    private readonly (int Native, int Legacy, float Scale)[] _aliases;
    private AlsRefactoredStandingHostInput _input;
    private AlsFrameIdentity _identity;
    private AlsGraphTraversalCounter _counter, _nextCounter;
    private float _delta;
    private int _outputCurveCount;
    private bool _prepared, _visited, _standing, _crouching, _evaluated, _post;
    internal long CommittedStandingFrames { get; private set; }
    internal long CommittedCrouchingFrames { get; private set; }
    internal long CommittedTransitionFrames { get; private set; }
    internal long CommittedGroundedFrames { get; private set; }
    internal int CommittedGroundedStateMask { get; private set; }
    internal long CommittedLocomotionFrames {get;private set;}
    internal int CommittedLocomotionStateMask {get;private set;}
    internal long CommittedPivotNotifies { get; private set; }
    internal int CommittedMovementDetailsMask { get; private set; }
    internal bool ResetGroundedEntry => _visited && SourceUpdated && _runtime!.Grounded!.ResetEntryMode;
    internal int StandingState => _standing ? _runtime!.Standing.State : -1;
    internal int CrouchingState => _crouching ? _runtime!.Crouching!.State : -1;
    internal bool SourceUpdated => _visited && _host!.Graph.GroundedUpdated && _runtime!.Transition.SourceUpdate.Updated;

    internal AlsRefactoredDemoStances(AlsSkeletonDefinition skeleton, ReadOnlySpan<AlsPrecisePose> reference, ReadOnlySpan<string> names)
    {
        _locomotion=AlsRefactoredDemoResources.Locomotion.Value;_profile = _locomotion.Actions; _names = names.ToArray();
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
        var nativeNames = NativeNames.ToArray();
        _nativeCurves = nativeNames.Select(n => Array.IndexOf(_names, n)).ToArray();
        if (_nativeCurves.Any(i => i < 0)) throw new ArgumentException("Demo curve layout omitted Refactored curves.");
        _nativeValues = new AlsInertialCurve[nativeNames.Length]; _previousValues = new AlsInertialCurve[nativeNames.Length];
        _nextValues = new AlsInertialCurve[nativeNames.Length];
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

    // The Grounded cache and enclosing Locomotion output have different sorted
    // curve layouts. Bind once on Main after the outer movement layout exists.
    internal void BindOutputLayout(ReadOnlySpan<string> names)
    {
        if(_outputCurveCount!=0||_host is not null)throw new InvalidOperationException("Demo output layout already bound.");
        for(var i=0;i<_nativeCurves.Length;i++)
        {var target=names.IndexOf(NativeNames[i]);if(target<0)throw new ArgumentException("Missing outer Locomotion curve.");_nativeCurves[i]=target;}
        for(var i=0;i<_aliases.Length;i++)
        {var alias=_aliases[i];var target=names.IndexOf(_names[alias.Legacy]);if(target<0)throw new ArgumentException("Missing outer compatibility curve.");_aliases[i]=(alias.Native,target,alias.Scale);}
        _outputCurveCount=names.Length;
    }

    internal void Begin(in AlsFrameInput frame, in AlsFrameResult result, in AlsStandingMovementInput movement,float prediction)
    {
        if (_prepared) throw new InvalidOperationException("Refactored Demo frame still pending.");
        _identity = frame.Identity; _delta = frame.DeltaTime;
        _host ??= _locomotion.CreateRuntime(_identity.CharacterId, _identity.SlotGeneration);
        _nextCounter = _counter.HasUpdated ? _counter.Next((ulong)frame.Identity.FrameId) : new(0, (ulong)frame.Identity.FrameId);
        var pending = _host.CommittedIdentity.SlotGeneration == 0;
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
            var mode=result.ResolvedLocomotionState==AlsLocomotionState.Grounded?AlsRefactoredLocomotionMode.Grounded:
                result.ResolvedLocomotionState==AlsLocomotionState.InAir?AlsRefactoredLocomotionMode.InAir:AlsRefactoredLocomotionMode.Other;
            _host.BeginGlobal(Context(1),new(_input,mode,frame.JumpAccepted==1,movement.HasMovementInput,false,prediction,
                Value("PoseStanding"),Value("PoseCrouching")),new(0,0),pending);
            _previousValues.CopyTo(_nextValues,0); _prepared = true;
        }
        catch { Discard(); throw; }
    }
    private float Value(string name)
    { var i=NativeNames.IndexOf(name); return i>=0&&_previousValues[i].Present?_previousValues[i].Value:0; }
    private AlsPoseUpdateContext Context(float weight) => new AlsPoseUpdateContext(_identity,weight,_delta).WithUpdateCounter(_nextCounter);

    internal void PrepareLocomotion(in AlsPoseUpdateContext context, AlsGraphTraversalCounter initialization, bool fromRoll)
    {
        if (!_prepared || _visited) throw new InvalidOperationException("Invalid Demo stance traversal.");
        _host!.Prepare(context,initialization:initialization,fromRoll:fromRoll);
        _standing=_host.Graph.GroundedUpdated&&_runtime!.Grounded!.StandingUpdated;
        _crouching=_host.Graph.GroundedUpdated&&_runtime!.Grounded!.CrouchingUpdated;
        _visited=true;
    }
    internal void EvaluateLocomotion(Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if(!_visited)throw new InvalidOperationException("No Refactored Locomotion boundary.");
        // Existing outer world/teleport inertia remains the migration boundary.
        _host!.Evaluate(AlsPrecisePose.Identity);
        MapOut(_host.Pose,_host.Curves,NativeNames,pose,curves);
        _host.Curves.CopyTo(_nextValues);_evaluated=true;
    }
    private void MapOut(ReadOnlySpan<AlsPrecisePose> native,ReadOnlySpan<AlsInertialCurve> values,ReadOnlySpan<string> names,
        Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {
        if(_outputCurveCount==0||curves.Length!=_outputCurveCount||pose.Length!=_targetParents.Length)
            throw new ArgumentException("Foreign Demo Locomotion output layout.");
        for(var b=0;b<_bones.Length;b++)if(_bones[b]>=0)pose[_bones[b]]=FromNative(native[b]);
        foreach(var v in _targetVirtuals)pose[v.LogicalBoneId]=AlsPrecisePose.Identity;
        Components(pose,_targetParents,_targetComponents);
        foreach(var v in _targetVirtuals)
        {pose[v.LogicalBoneId]=AlsPrecisePose.Relative(_targetComponents[v.TargetLogicalBoneId],_targetComponents[v.SourceLogicalBoneId]).Normalized();_targetComponents[v.LogicalBoneId]=_targetComponents[v.TargetLogicalBoneId];}
        curves.Clear(); Array.Clear(_nativeValues);
        for(var c=0;c<names.Length;c++)
        {var i=NativeNames.IndexOf(names[c]);if(i<0)throw new InvalidOperationException("Foreign stance curve.");_nativeValues[i]=values[c];curves[_nativeCurves[i]]=values[c];}
        foreach(var alias in _aliases)curves[alias.Legacy]=AlsStandingCycleCurves.Scale(_nativeValues[alias.Native],alias.Scale==0?_delta:alias.Scale);
    }
    internal void PostUpdate(){if(!_prepared||_post)throw new InvalidOperationException("Invalid Demo post update.");_host!.PostUpdate();_post=true;}
    internal void CaptureFeedback(ReadOnlySpan<string> names,ReadOnlySpan<AlsInertialCurve> curves)
    {
        if(!_prepared||names.Length!=curves.Length)throw new ArgumentException("Foreign Refactored feedback.");
        for(var c=0;c<_nextValues.Length;c++)
        {var i=names.IndexOf(NativeNames[c]);_nextValues[c]=i<0?default:curves[i];}
    }
    internal void ValidateCommit(AlsFrameIdentity id)
    {if(!_prepared||!_post||id!=_identity)throw new InvalidOperationException("Incomplete Demo stance frame.");_host!.ValidateCommit(id);}
    internal void Commit(AlsFrameIdentity id)
    {
        ValidateCommit(id);
        var groundedState=_visited&&SourceUpdated?_runtime!.Grounded!.State:-1;
        if (_standing)
        {
            CommittedPivotNotifies += _runtime!.Standing.PivotDispatchCount;
            var details = _runtime.Standing.MovementDetailsState;
            if (details >= 0) CommittedMovementDetailsMask |= 1 << details;
        }
        if(_visited){CommittedLocomotionFrames++;CommittedLocomotionStateMask|=1<<_host!.Graph.MainUpdate.State.CurrentState;}
        _host!.Commit(id);
        if(groundedState>=0){CommittedGroundedFrames++;CommittedGroundedStateMask|=1<<groundedState;}
        if(_standing)CommittedStandingFrames++;if(_crouching)CommittedCrouchingFrames++;if(_evaluated&&groundedState>=0)CommittedTransitionFrames++;
        _nextValues.CopyTo(_previousValues,0);_counter=_nextCounter;Clear();
    }
    internal void Discard(){_host?.Discard();Clear();}
    private void Clear(){_prepared=_visited=_standing=_crouching=_evaluated=_post=false;}
    private static void Components(ReadOnlySpan<AlsPrecisePose> local,ReadOnlySpan<int> parents,Span<AlsPrecisePose> components)
    {
        for(var b=0;b<parents.Length;b++)components[b]=parents[b]<0?local[b]:AlsPrecisePose.Compose(local[b],components[parents[b]]).Normalized();
    }
    private static AlsPrecisePose FromNative(AlsPrecisePose value)=>new(new(value.Position.X*.01,-value.Position.Y*.01,value.Position.Z*.01),
        new(-value.Rotation.X,value.Rotation.Y,-value.Rotation.Z,value.Rotation.W),value.Scale);
}
