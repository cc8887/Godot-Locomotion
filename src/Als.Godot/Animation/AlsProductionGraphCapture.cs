using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Opt-in diagnostic data only. Each candidate owns fresh arrays/dictionaries;
// publishing happens with the production pose commit and cancellation discards it.
internal sealed record AlsProductionGraphCapture(AlsFrameIdentity Identity, string[] Names,
    object Request, Dictionary<string,object> Row)
{
    internal object? Skeleton { get; init; }
    internal static AlsProductionGraphCapture Capture(AlsLayeredAnimationFrameRuntime owner,
        AlsAnimationSetDefinition set, AlsMovementGraphDefinition definition, string[] names, in AlsFrameInput input,
        in AlsFrameResult result, in AlsStandingMovementInput movement, in AlsLocalPose component, bool idleControls)
    {
        if(!owner.UsesRefactoredFeet || !owner.NormalVisited)
            throw new InvalidOperationException("Production graph capture requires a visited split-foot normal graph.");
        var properties=AlsFullGraphParityCapture.Properties(owner,movement,owner.CandidateLayeringInput,input.DeltaTime,result.ActualGait);
        var direction=owner.Base.Grounded.CommittedStanding.Detail.Standing.Feedback;
        properties["TrackedHipsDirection"]=direction.TrackedHips;
        properties["Pivot"]=direction.Pivot;
        properties["Stance"]=(int)result.ActualStance;
        properties["RotationMode"]=(int)result.ActualRotationMode;
        properties["MovementState"]=result.ResolvedLocomotionState==AlsLocomotionState.Grounded ? 1 :
            result.ResolvedLocomotionState==AlsLocomotionState.InAir ? 2 : throw new InvalidOperationException("Unsupported captured movement state.");
        // Source poses are in FBX bone axes; keep scene transforms separately in
        // their documented Godot world axes/meters. Never call them UE transforms.
        object request=new {delta=(double)input.DeltaTime,properties};
        if(idleControls)
        {
            foreach(var name in new[]{"Rotate_L","Rotate_R","RotateRate","RotationScale"}) properties.Remove(name);
            const double degrees=180/System.Math.PI;
            properties["AimYawRate"]=(double)input.AimYawRateDegrees;
            properties["ViewMode"]=(int)input.FirstPerson;
            properties["AimingRotation"]=new {Pitch=input.Command.AimPitch*degrees,Yaw=-input.Command.AimYaw*degrees,Roll=0d};
            request=new {delta=(double)input.DeltaTime,properties,characterYawDegrees=-input.CharacterYaw*degrees};
        }
        var players=new List<object>();
        var sync=owner.Base.Sources;
        for(var p=0;p<definition.Sources.Players.Length;p++)
        {
            var ticked=false;
            for(var h=0;h<sync.PlayerCount;h++)ticked|=sync.Players[h].PlayerId==p;
            players.Add(new {compiledIndex=definition.Sources.Players[p].CompiledNodeIndex,
                player=p,time=sync.Times[p],weight=sync.CachedWeights[p],epoch=sync.Epochs[p],ticked});
        }
        var row=new Dictionary<string,object>
        {
            ["serial"]=input.Identity.FrameId,["input"]=request,["identity"]=input.Identity,
            ["componentGodot"]=component,["motorInput"]=input,
            ["rigInput"]=owner.CandidateRefactoredRigInput,
            ["players"]=players,
            ["previousCurves"]=AlsFullGraphParityCapture.Curves(owner.CurveNames,owner.CommittedCurves),
            ["stages"]=new Dictionary<string,object>
            {
                ["MainMovement"]=new {pose=AlsFullGraphParityCapture.Pose(owner.Base.RawMovementPose),
                    curves=AlsFullGraphParityCapture.Curves(owner.Base.CurveNames,owner.Base.RawMovementCurves)},
                ["BaseLayer"]=new {pose=AlsFullGraphParityCapture.Pose(owner.Base.Pose),
                    curves=AlsFullGraphParityCapture.Curves(owner.Base.CurveNames,owner.Base.Curves)},
                ["PostLayering"]=new {pose=AlsFullGraphParityCapture.Pose(owner.PostLayeringPose),
                    curves=AlsFullGraphParityCapture.Curves(owner.CurveNames,owner.PostLayeringCurves)},
                ["PostAim"]=new {pose=AlsFullGraphParityCapture.Pose(owner.PreFootPose),
                    curves=AlsFullGraphParityCapture.Curves(owner.CurveNames,owner.PreFootCurves)},
                ["PreFoot"]=new {pose=AlsFullGraphParityCapture.Pose(owner.PreFootPose),
                    curves=AlsFullGraphParityCapture.Curves(owner.CurveNames,owner.PreFootCurves)}
            },
            ["montageEvaluations"]=owner.Base.Montages.Evaluation.ToArray().Select(e=>new {
                asset=set.Animations[e.AnimationId].ObjectPath,
                slot=e.Slot.Id switch {0=>"(N) Turn/Rotate",1=>"(CLF) Turn/Rotate",3=>"Grounded Slot",_=>$"other:{e.Slot.Id}"},position=e.Position,weight=e.Weight}).ToArray(),
            ["stopNotifies"]=owner.Base.Grounded.StopNotifies.ToArray().Select(n=>definition.StopTransitions.Resolve(n).NotifyName).ToArray()
        };
        if(idleControls)
        {
            var idle=owner.Base.CandidateControlInput.State.Idle;
            row["idleControl"]=new {Rotate_L=idle.RotateLeft,Rotate_R=idle.RotateRight,
                idle.RotateRate,idle.RotationScale,idle.ElapsedDelayTime};
        }
        return new(input.Identity,names,request,row);
    }
}
