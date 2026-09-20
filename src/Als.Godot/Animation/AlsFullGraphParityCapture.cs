using System.Text.Json;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using V3=System.Numerics.Vector3;
using Matrix=System.Numerics.Matrix4x4;
using Quaternion=System.Numerics.Quaternion;

namespace GodotAls.Animation;

// Test-only serialization of the real mapped owner. No alternative pose graph
// or input formula is used to generate the Godot side of the comparison.
internal static class AlsFullGraphParityCapture
{
    internal static void Run(Node host,AlsAnimationSetDefinition set,AlsLocomotionAnimationProfile profile,
        AlsPoseAnimationProfile poseProfile,AlsMovementGraphDefinition definition,AlsStandingMovementSettings settings,
        IAlsGroundedFrameRuntimeSink sink)
    {
        var output=OS.GetCmdlineUserArgs().Single(a=>a.StartsWith("--parity-output=",StringComparison.Ordinal))[16..];
        if(!System.IO.Path.IsPathFullyQualified(output))throw new ArgumentException("Absolute parity output required.");
        var zeroAcceleration=OS.GetCmdlineUserArgs().Contains("--parity-zero-acceleration");
        var zeroAim=OS.GetCmdlineUserArgs().Contains("--parity-zero-aim");
        var sprint=OS.GetCmdlineUserArgs().Contains("--parity-sprint");
        var retry=OS.GetCmdlineUserArgs().Contains("--parity-retry");
        var stages=OS.GetCmdlineUserArgs().Contains("--parity-stages");
        var stopPlayback=OS.GetCmdlineUserArgs().Contains("--parity-stop");
        if(stopPlayback && sprint)throw new ArgumentException("Stop trace uses the standing strafe schedule.");
        var seconds=sprint ? 6 : stopPlayback ? 5 : 3;
        var nativeInertiaPath=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--parity-native-inertia=",StringComparison.Ordinal))?[24..];
        using var nativeInertia=nativeInertiaPath is null ? null : JsonDocument.Parse(System.IO.File.ReadAllText(nativeInertiaPath));
        if(nativeInertia is not null && !stages)throw new ArgumentException("Native inertia isolation requires stage capture.");
        var skeleton=definition.OverlayRawSources.GetSkeleton(poseProfile.SkeletonId);
        var names=skeleton.LogicalBoneNames.ToArray(); var parents=skeleton.LogicalParents.ToArray();
        var component=AlsLocalPose.Identity with {Rotation=AlsFootIkCoordinates.FbxToGodotRotation};
        var left=Find("ik_foot_l"); var right=Find("ik_foot_r"); var root=Find("root");
        var traces=new List<object>(); var requests=new List<object>(); var count=0;
        foreach(var hz in new[]{30,60,120}) foreach(var side in new[]{-1,1})
        {
            using var library=AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding);
            library.UseMovementSources(set,definition.RawSources); host.AddChild(library.Root);
            using var graph=AlsLocomotionGraphBuilder.Build(library,profile,poseProfile,set,definition.Binding);
            using var owner=new AlsLayeredAnimationFrameRuntime(definition,library,graph.StandingCycle!,set,poseProfile,11,2,true);
            var captureSink=new MontageSink(owner,sink);
            var traceName=$"mapped_{(sprint ? "sprint" : "strafe")}_{side}_{hz}";
            var inertia=nativeInertia is null ? null : new AlsNativeInertiaDiagnostic(nativeInertia.RootElement,traceName,names,
                owner.Base.CurveNames.Length,hz*seconds);
            var last=skeleton.ReferencePose.ToArray(); var components=new AlsPrecisePose[last.Length];
            var rows=new List<object>(); var inputs=new List<object>(); var previousVelocity=V3.Zero;
            for(var frame=1;frame<=hz*seconds;frame++)
            {
                var t=(frame-1)/(float)hz; var delta=1f/hz; var id=new AlsFrameIdentity(frame,11,2);
                var moving=sprint ? t is >=.5f and <2.6f or >=3.4f and <5.5f : t>=.5f && t<2.5f;
                var sign=t<1.5f ? side : -side;
                var gait=sprint && (t is >=.5f and <1.8f or >=3.4f and <5.5f) ? AlsGait.Sprinting : AlsGait.Running;
                var speed=t<1.2f ? MathF.Min(6.5f,1+(t-.5f)*12) : t<1.8f ? 6.5f : t<2.6f ? 3.5f :
                    t<4.3f ? 6.5f : 5.5f+MathF.Sin((t-4.3f)*5);
                var velocity=moving ? sprint ? new V3(0,0,-speed) : new V3(sign*3.5f,0,0) : V3.Zero;
                for(var b=0;b<last.Length;b++)components[b]=parents[b]<0 ? new(last[b]) : AlsPrecisePose.Compose(new(last[b]),components[parents[b]]);
                var input=AlsFrameInput.CreateDefault(id,delta) with
                {
                    ActualVelocity=velocity,ActualAcceleration=zeroAcceleration ? V3.Zero : (velocity-previousVelocity)/delta,
                    MaxAcceleration=8,MaxBrakingDeceleration=16,InputDirection=moving ? V3.Normalize(velocity) : default,
                    CharacterTransform=Matrix.Identity,CharacterYaw=0,RotationMode=AlsRotationMode.LookingDirection,
                    Floor=new(1,V3.UnitY,-1,Matrix.Identity,default),
                    FootIk=new(1,owner.CommittedIdentity,component,components[left].ToSingle(),components[right].ToSingle(),
                        V3.Transform(components[root].Position.ToSingle(),component.Rotation),Quaternion.Identity,velocity,delta)
                };
                input=input with {Command=input.Command with {AimYaw=moving && !zeroAim ? (sprint ? side : 1)*.2f*MathF.Sin(t*3) : 0,AimPitch=moving && !zeroAim ? .25f : 0}};
                var result=AlsFrameResult.CreateDefault(id); result.ActualStance=AlsStance.Standing;
                result.ActualGait=gait; result.ActualRotationMode=AlsRotationMode.LookingDirection;
                result.ResolvedLocomotionState=AlsLocomotionState.Grounded; result.BlendCoordinates=new(velocity.X,-velocity.Z);
                var movement=AlsStandingMovementInputModel.Evaluate(id,velocity,moving ? 1 : 0,settings);
                var rules=new AlsGroundedRuleInput(movement.ShouldMove,false,false,result.ActualStance,true,false,0,0)
                    {MovementState=AlsMovementStateInput.Grounded,HasMovementInput=movement.HasMovementInput,Speed=movement.Speed};
                var ground=new AlsGroundedFrameInputs(delta,new(1.75f,3.75f,6.5f),1,1,1,default,default,
                    AlsSlotWeights.Passthrough,new(0,0),new(0,0),new((short)frame,(ulong)frame));
                // This pure model is exactly the input used by PrepareAfterGlobalInput;
                // it reads the same immutable committed final curves before traversal.
                var layer=definition.LayeringInput.Evaluate(id,owner.CommittedIdentity,owner.CommittedIdentity==default ? [] : owner.CurveNames,
                    owner.CommittedIdentity==default ? [] : owner.CommittedCurves);
                var direction=owner.Base.Grounded.CommittedStanding.Detail.Standing.Feedback;
                var previous=Curves(owner.CurveNames,owner.CommittedCurves);
                var oldIdentity=owner.CommittedIdentity;
                var oldSources=owner.Base.CommittedSources;
                var oldSprintInput=owner.Base.Grounded.CommittedStanding.SprintInput;
                owner.PrepareFromFrame(input,result,movement,rules,ground,new(id,1,delta),captureSink,component,0,0);
                var values=Properties(owner,movement,layer,delta,gait);
                values["TrackedHipsDirection"]=direction.TrackedHips; values["Pivot"]=direction.Pivot;
                var request=new {delta=(double)delta,properties=values}; inputs.Add(request);
                owner.Evaluate(component,0,0);
                if(retry)
                {
                    var expectedPose=owner.Pose.ToArray(); var expectedCurves=owner.Curves.ToArray();
                    var expectedSources=owner.Base.Sources; var expectedEvents=owner.Base.SourceEvents;
                    owner.Discard();
                    if(owner.CommittedIdentity!=oldIdentity || owner.Base.Grounded.CommittedStanding.SprintInput!=oldSprintInput ||
                        !StandingCycleSmoke.SameSync(owner.Base.CommittedSources,oldSources))
                        throw new InvalidOperationException("Full graph discard leaked committed Standing history or source clocks.");
                    owner.PrepareFromFrame(input,result,movement,rules,ground,new(id,1,delta),captureSink,component,0,0);
                    owner.Evaluate(component,0,0);
                    if(!owner.Pose.SequenceEqual(expectedPose) || !owner.Curves.SequenceEqual(expectedCurves) ||
                        !StandingCycleSmoke.SameSync(owner.Base.Sources,expectedSources) || owner.Base.SourceEvents.Count!=expectedEvents.Count)
                        throw new InvalidOperationException("Full graph retry changed pose, curves, source clocks or events.");
                    for(var e=0;e<expectedEvents.Count;e++)if(owner.Base.SourceEvents[e]!=expectedEvents[e])
                        throw new InvalidOperationException("Full graph retry changed a source event.");
                }
                var playerRows=new List<object>(); var sync=owner.Base.Sources;
                // Histories are compact and ordered by this frame's tick batch.
                // Times, epochs and cached weights instead use formal player IDs.
                for(var p=0;p<definition.Sources.Players.Length;p++)
                {
                    var ticked=false;
                    for(var h=0;h<sync.PlayerCount;h++)ticked|=sync.Players[h].PlayerId==p;
                    playerRows.Add(new {compiledIndex=definition.Sources.Players[p].CompiledNodeIndex,
                        player=p,time=sync.Times[p],weight=sync.CachedWeights[p],epoch=sync.Epochs[p],ticked});
                }
                var row=new Dictionary<string,object> {["serial"]=frame,["input"]=request,["previousCurves"]=previous,
                    ["pose"]=Pose(owner.Pose),["curves"]=Curves(owner.CurveNames,owner.Curves),["players"]=playerRows};
                if(stopPlayback)
                {
                    row["montageEvaluations"]=owner.Base.Montages.Evaluation.ToArray().Select(e=>new {
                        asset=set.Animations[e.AnimationId].ObjectPath,
                        slot=e.Slot.Id==3 ? "Grounded Slot" : $"unsupported:{e.Slot.Id}",
                        position=e.Position,weight=e.Weight}).ToArray();
                    row["stopNotifies"]=owner.Base.Grounded.StopNotifies.ToArray()
                        .Select(n=>definition.StopTransitions.Resolve(n).NotifyName).ToArray();
                }
                if(stages)
                {
                    var captured=new Dictionary<string,object>
                    {
                    ["MainMovement"]=new {pose=Pose(owner.Base.RawMovementPose),curves=Curves(owner.Base.CurveNames,owner.Base.RawMovementCurves)},
                    ["BaseLayer"]=new {pose=Pose(owner.Base.Pose),curves=Curves(owner.Base.CurveNames,owner.Base.Curves),
                        requestSeconds=owner.Base.InertiaRequestSeconds}
                    };
                    if(inertia is not null)
                    {
                        inertia.Evaluate(frame,delta,owner.Base.InertiaRequestSeconds,component,owner.Base.CurveNames);
                        captured["NativeInertiaSingle"]=new {pose=Pose(inertia.SinglePose),curves=Curves(owner.Base.CurveNames,inertia.SingleCurves)};
                        captured["NativeInertiaPrecise"]=new {pose=Pose(inertia.PrecisePose),curves=Curves(owner.Base.CurveNames,inertia.PreciseCurves)};
                    }
                    row["stages"]=captured;
                }
                rows.Add(row);
                owner.Pose.CopyTo(last); owner.Commit(id); previousVelocity=velocity; count++;
            }
            var name=$"mapped_{(sprint ? "sprint" : "strafe")}_{side}_{hz}";
            requests.Add(new {name,frames=inputs}); traces.Add(new {name,frames=rows});
        }
        var options=new JsonSerializerOptions();
        // UE Blueprint real properties are doubles. Preserve the exact promoted
        // float value instead of JSON's shorter single-precision representation.
        options.Converters.Add(new ExactSingleConverter());
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
        System.IO.File.WriteAllText(output,JsonSerializer.Serialize(new {schemaVersion=1,names,
            scope="Real full Godot owner; controlled velocity/scene observations; local pose converted to UE cm",zeroAcceleration,zeroAim,sprint,dispatchStopNotifies=stopPlayback,traces},options));
        System.IO.File.WriteAllText(System.IO.Path.ChangeExtension(output,"request.json"),
            JsonSerializer.Serialize(new {schemaVersion=1,dispatchStopNotifies=stopPlayback,traces=requests},options));
        GD.Print($"FULL_GRAPH_GODOT_CAPTURE_OK traces=6 frames={count} bones={names.Length} sources=224/258 output={output}");
        if(retry)GD.Print($"FULL_GRAPH_RETRY_OK frames={count} sprint={sprint} retry_every_frame=true");
        int Find(string name)=>Array.FindIndex(names,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));
    }

    // The physical owner samples the Slot itself. This callback observes its
    // update, including nonzero additive playback, and must not demand bypass.
    private sealed class MontageSink(AlsLayeredAnimationFrameRuntime owner,IAlsGroundedFrameRuntimeSink outer) : IAlsGroundedFrameRuntimeSink
    {
        public void UpdateGroundedSlot(int slot,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context)
        {
            weights.Validate();
            if(weights!=owner.Base.Montages.SlotWeights(AlsMontageSlot.Grounded) ||
                owner.Base.Montages.Frame.Identity!=context.Identity)
                throw new InvalidOperationException("Full graph capture observed foreign physical Slot weights.");
        }
        public void RefreshSourceBones(int cache)=>outer.RefreshSourceBones(cache);
        public void RequestInertialization(in AlsPoseUpdateContext context,float seconds)=>outer.RequestInertialization(context,seconds);
        public void OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped)=>outer.OnCachedUpdatesSkipped(handler,skipped);
    }
    internal static Dictionary<string,object> Properties(AlsLayeredAnimationFrameRuntime owner,in AlsStandingMovementInput movement,
        in AlsLayeringInput layer,float delta,AlsGait gait)
    {
        var g=owner.Base.CandidateGroundInput; var a=owner.CandidateAimInput; var air=owner.Base.CandidateGlobalInput;
        var control=owner.Base.CandidateControlInput.State; var idle=control.Idle; var feet=owner.CandidateFeet;
        var jump=owner.Base.CandidateJumpInput.Frame;
        var p=new Dictionary<string,object>(StringComparer.Ordinal)
        {
            ["DeltaTimeX"]=(double)delta,["MovementState"]=1,["MovementAction"]=0,["Stance"]=0,["Gait"]=(int)gait,["RotationMode"]=1,
            ["OverlayState"]=(int)owner.CandidateOverlayState.Overlay,["OverlayOverrideState"]=owner.CandidateOverlayValues.OverrideState,
            ["ShouldMove"]=g.ShouldMove,["IsMoving"]=movement.IsMoving,
            ["HasMovementInput"]=movement.HasMovementInput,["Speed"]=movement.Speed*100d,["MovementDirection"]=(int)control.MovementDirection,
            ["VelocityBlend"]=new Dictionary<string,double>{["F_3_2154ABAD4BD15DAC904154B63D704219"]=g.VelocityBlend.X,
                ["B_5_0A0855774CB13BB3E4B0A6847E7154F6"]=g.VelocityBlend.Y,["L_8_DFEBB8584D28F158D2562CA60EB07B6D"]=g.VelocityBlend.Z,
                ["R_9_79E6E09B4A52B442B9FE6DB7192CFBEE"]=g.VelocityBlend.W},
            ["RelativeAccelerationAmount"]=Vector(new(-g.RelativeAcceleration.Z,g.RelativeAcceleration.X,g.RelativeAcceleration.Y)),
            ["LeanAmount"]=new Dictionary<string,double>{["LR_17_ADF99333493B27F5B49BA89100DC4C05"]=air.Lean.X,
                ["FB_15_297866804FB14F4B81FB4A976A7F57D1"]=air.Lean.Y},
            ["WalkRunBlend"]=g.WalkRunBlend,["StrideBlend"]=g.Stride,["StandingPlayRate"]=g.StandingPlayRate,
            ["CrouchingPlayRate"]=g.CrouchingPlayRate,["DiagonalScaleAmount"]=g.DiagonalScale,
            ["FYaw"]=control.Yaw.X,["BYaw"]=control.Yaw.Y,["LYaw"]=control.Yaw.Z,["RYaw"]=control.Yaw.W,
            ["Rotate_L"]=idle.RotateLeft,["Rotate_R"]=idle.RotateRight,["RotateRate"]=idle.RotateRate,["RotationScale"]=idle.RotationScale,
            ["AimingAngle"]=new {X=a.Angle.Yaw,Y=a.Angle.Pitch},["SmoothedAimingAngle"]=new {X=a.SmoothedAngle.Yaw,Y=a.SmoothedAngle.Pitch},
            ["SmoothedAimingRotation"]=Rotation(a.SmoothedRotation),["SpineRotation"]=Rotation(a.SpineRotation),
            ["AimSweepTime"]=a.AimSweepTime,["InputYawOffsetTime"]=a.InputYawOffsetTime,["LeftYawTime"]=a.LeftYawTime,
            ["RightYawTime"]=a.RightYawTime,["ForwardYawTime"]=a.ForwardYawTime,
            ["LandPrediction"]=air.LandPrediction,["FallSpeed"]=air.FallSpeed*100d,["Jumped"]=jump.Jumped,["JumpPlayRate"]=jump.PlayRate,
            ["FootLock_L_Alpha"]=feet.LeftLock.Alpha,["FootLock_R_Alpha"]=feet.RightLock.Alpha,
            ["FootLock_L_Location"]=Vector(feet.LeftLock.Location),["FootLock_R_Location"]=Vector(feet.RightLock.Location),
            ["FootLock_L_Rotation"]=Rotation(feet.LeftLock.Rotation),["FootLock_R_Rotation"]=Rotation(feet.RightLock.Rotation),
            ["FootOffset_L_Location"]=Vector(feet.LeftOffset.Location),["FootOffset_R_Location"]=Vector(feet.RightOffset.Location),
            ["FootOffset_L_Rotation"]=Rotation(feet.LeftOffset.Rotation),["FootOffset_R_Rotation"]=Rotation(feet.RightOffset.Rotation),
            ["PelvisAlpha"]=feet.Pelvis.Alpha,["PelvisOffset"]=Vector(feet.Pelvis.Offset)
        };
        foreach(var name in new[]{"Enable_AimOffset","BasePose_N","BasePose_CLF","Spine_Add","Head_Add","Arm_L_Add","Arm_R_Add",
            "Hand_L","Hand_R","Enable_HandIK_L","Enable_HandIK_R","Arm_L_LS","Arm_L_MS","Arm_R_LS","Arm_R_MS"})p[name]=layer.GetValue(name);
        return p;
    }
    private static object Vector(AlsDoubleVector v)=>new {v.X,v.Y,v.Z};
    internal sealed class ExactSingleConverter : System.Text.Json.Serialization.JsonConverter<float>
    {
        public override float Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)=>reader.GetSingle();
        public override void Write(Utf8JsonWriter writer,float value,JsonSerializerOptions options)=>writer.WriteNumberValue((double)value);
    }
    private static object Rotation(AlsAimingRotation r)=>new {r.Pitch,r.Yaw,r.Roll};
    internal static Dictionary<string,float> Curves(ReadOnlySpan<string> names,ReadOnlySpan<AlsInertialCurve> curves)
    {
        var result=new Dictionary<string,float>(StringComparer.Ordinal);
        for(var c=0;c<names.Length;c++)if(curves[c].Present)result.Add(names[c],curves[c].Value);
        return result;
    }
    internal static object[] Pose(ReadOnlySpan<AlsLocalPose> locals)
    {
        var result=new object[locals.Length];
        for(var b=0;b<locals.Length;b++)
        {
            var t=locals[b]; var q=t.Rotation;
            // Owner poses retain FBX bone-local axes, not Godot world axes.
            // Invert AlsRawAnimationSourceCompiler.ConvertPose's Y reflection.
            result[b]=new {position=new[]{(double)t.Position.X*100,-(double)t.Position.Y*100,(double)t.Position.Z*100},
                rotation=new[]{-(double)q.X,(double)q.Y,-(double)q.Z,(double)q.W},
                scale=new[]{(double)t.Scale.X,(double)t.Scale.Y,(double)t.Scale.Z}};
        }
        return result;
    }
}
