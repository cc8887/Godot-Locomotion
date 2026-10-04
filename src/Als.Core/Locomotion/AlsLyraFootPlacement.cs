using System.Collections.Immutable;
using M=System.Math;
using F=GodotAls.Core.Locomotion.AlsFootPlacementMath;
namespace GodotAls.Core.Locomotion;

public readonly record struct AlsFootGroundHit(bool Walkable,AlsDoubleVector Point,AlsDoubleVector Normal);
public readonly record struct AlsFootTraceQuery(AlsDoubleVector Start,AlsDoubleVector Direction,float StartOffset,float EndOffset,float Radius,bool Complex);
public interface IAlsFootGroundQuery{AlsFootGroundHit Sweep(int leg,in AlsFootTraceQuery query);}
public readonly record struct AlsFootCharacterInput(AlsPrecisePose Component,bool Walking,bool Blocking,
    AlsDoubleVector FloorPoint,AlsDoubleVector FloorNormal,AlsDoubleVector Velocity,float TeleportDistance=0);
public sealed record AlsFootLegHistory(AlsFootPlane Plane,AlsPrecisePose AlignedRS,AlsPrecisePose AlignedWS,
    AlsPrecisePose UnalignedRS,AlsPrecisePose UnalignedWS,AlsFootFloatSpring HeightSpring,
    AlsFootQuaternionSpring RotationSpring,AlsFootVectorSpring OffsetSpring,AlsFootQuaternionSpring OffsetRotationSpring,
    bool Reachable,float Alignment,float Distance,float Speed,float TimeUnaligned)
{
    public static AlsFootLegHistory Default=>new(new(F.Up,0),AlsPrecisePose.Identity,AlsPrecisePose.Identity,
        AlsPrecisePose.Identity,AlsPrecisePose.Identity,default,AlsFootQuaternionSpring.Default,default,AlsFootQuaternionSpring.Default,false,0,0,0,0);
    public AlsFootLegHistory ResetInterpolation()=>this with{HeightSpring=default,RotationSpring=AlsFootQuaternionSpring.Default,
        OffsetSpring=default,OffsetRotationSpring=AlsFootQuaternionSpring.Default};
}
public sealed record AlsFootPlacementHistory(bool First,float Delta,short Counter,AlsDoubleVector PelvisOffset,
    AlsFootVectorSpring PelvisSpring,AlsPrecisePose Component,AlsDoubleVector ComponentDelta,AlsDoubleVector GroundNormal,
    AlsFootQuaternionSpring GroundSpring,bool OnGround,AlsPrecisePose Root,ImmutableArray<AlsFootLegHistory> Legs)
{
    public static AlsFootPlacementHistory Default=>new(true,0,-1,default,default,AlsPrecisePose.Identity,default,default,
        AlsFootQuaternionSpring.Default,false,AlsPrecisePose.Identity,[AlsFootLegHistory.Default,AlsFootLegHistory.Default]);
    public AlsFootPlacementHistory ResetInterpolation()=>this with{First=true,PelvisOffset=default,PelvisSpring=default,
        Legs=Legs.Select(l=>l.ResetInterpolation()).ToImmutableArray()};
}

// Original Lyra configuration: Manual speed, no named per-leg curves, Unlocked,
// AllLegs pelvis, SuddenMotionOnly compensation, floor/pelvis interpolation.
// Inputs are UE axes/cm; collision queries stay outside this math operator.
public sealed class AlsLyraFootPlacement
{
    private readonly AlsComponentPose _pose;
    private readonly int _pelvis,_ikRoot;
    private readonly (int Foot,int Ik,int Ball,int Hip,float Limb,float Length)[] _legs;
    private readonly record struct Input(AlsPrecisePose Fk,AlsPrecisePose Foot,AlsPrecisePose Hip,AlsPrecisePose Ball,
        AlsPrecisePose FootToBall,AlsPrecisePose BallToFoot,float Distance);
    public ReadOnlySpan<float> LimbLengths=>_lengths;
    private readonly float[] _lengths;
    public AlsLyraFootPlacement(ReadOnlySpan<int> parents,ReadOnlySpan<AlsPrecisePose> reference,int pelvis,int ikRoot,
        int rightFoot,int rightIk,int rightBall,int leftFoot,int leftIk,int leftBall)
    {
        if(parents.Length!=reference.Length||parents.IsEmpty)throw new ArgumentException("Invalid FootPlacement layout.");
        for(var b=0;b<parents.Length;b++)if(parents[b]<-1||parents[b]>=b)throw new ArgumentException("FootPlacement requires parent-first bones.");
        _pelvis=pelvis;_ikRoot=ikRoot;_pose=new(parents);_legs=new (int,int,int,int,float,float)[2];_lengths=new float[4];
        for(var i=0;i<2;i++)
        {
            int foot=i==0?rightFoot:leftFoot,ik=i==0?rightIk:leftIk,ball=i==0?rightBall:leftBall;
            if((uint)foot>=parents.Length||(uint)ik>=parents.Length||(uint)ball>=parents.Length||(uint)pelvis>=parents.Length||(uint)ikRoot>=parents.Length)
                throw new ArgumentException("Invalid FootPlacement bones.");
            var knee=parents[foot];var hip=knee<0?-1:parents[knee];if(hip<0)throw new ArgumentException("FootPlacement needs two bones per limb.");
            var limb=(float)M.Sqrt(reference[foot].Position.LengthSquared)+(float)M.Sqrt(reference[knee].Position.LengthSquared);
            var length=(float)M.Sqrt(reference[ball].Position.LengthSquared);_legs[i]=(foot,ik,ball,hip,limb,length);_lengths[i*2]=limb;_lengths[i*2+1]=length;
        }
    }
    public static AlsFootPlacementHistory Update(AlsFootPlacementHistory history,bool visited,bool initialize,float alpha,float delta,short counter)
    {
        if(!float.IsFinite(alpha)||alpha is <0 or >1||!float.IsFinite(delta)||delta<0||counter==-1)throw new ArgumentException("Invalid FootPlacement update.");
        if(initialize)history=history.ResetInterpolation();
        if(!visited||alpha<=AlsPoseBlender.WeightThreshold)return history;
        var next=unchecked((short)(history.Counter+1));if(next==-1)next=0;
        if(!history.First&&history.Counter!=-1&&history.Counter!=counter&&next!=counter)history=history.ResetInterpolation();
        return history with{Counter=counter,Delta=history.Delta+delta};
    }
    public AlsFootPlacementHistory Evaluate(ReadOnlySpan<AlsPrecisePose> source,float alpha,in AlsFootCharacterInput character,
        IAlsFootGroundQuery ground,AlsFootPlacementHistory history,Span<AlsPrecisePose> output)
    {
        if(source.Overlaps(output)||source.Length!=output.Length||!float.IsFinite(alpha)||alpha is <0 or >1||history.Legs.Length!=2)
            throw new ArgumentException("Invalid FootPlacement buffers or history.");
        character.Component.Validate();if(!character.FloorPoint.IsFinite||!character.FloorNormal.IsFinite||!character.Velocity.IsFinite)
            throw new ArgumentException("Invalid FootPlacement character observation.");
        if(alpha<=AlsPoseBlender.WeightThreshold){source.CopyTo(output);return history;}
        _pose.Begin(source);var next=Evaluate(_pose,alpha,character,ground,history);_pose.Export(output);return next;
    }
    internal AlsFootPlacementHistory Evaluate(AlsComponentPose pose,float alpha,in AlsFootCharacterInput character,IAlsFootGroundQuery ground,AlsFootPlacementHistory history)
    {
        if(alpha<=AlsPoseBlender.WeightThreshold)return history;
        var root=pose.Component(0);
        if(!history.First&&character.TeleportDistance>0&&
            (F.Position(character.Component,root.Position)-F.Position(history.Component,history.Root.Position)).LengthSquared>character.TeleportDistance*character.TeleportDistance)
            history=history.ResetInterpolation();
        var pelvis=pose.Component(_pelvis);var ikRoot=pose.Component(_ikRoot);var data=new Input[2];var legs=history.Legs.ToArray();
        var approach=F.Down.Rotate(character.Component.Rotation);var invComponent=F.Inverse(character.Component);var invRoot=F.Inverse(root);
        for(var i=0;i<2;i++)
        {
            var def=_legs[i];var fk=pose.Component(def.Foot);var ball=pose.Component(def.Ball);var foot=pose.Component(def.Ik);var hip=pose.Component(def.Hip);
            var footToBall=AlsPrecisePose.Relative(ball,fk);var ballToFoot=AlsPrecisePose.Relative(fk,ball);var ikBall=AlsPrecisePose.Compose(footToBall,foot);
            var plane=AlsFootPlane.At(ikRoot.Position,F.Up.Rotate(ikRoot.Rotation));var distance=M.Min(plane.Distance(foot.Position,F.Down),plane.Distance(ikBall.Position,F.Down));
            data[i]=new(fk,foot,hip,ikBall,footToBall,ballToFoot,distance);
            if(history.First)
            {
                var rs=AlsPrecisePose.Relative(foot,root);var ws=AlsPrecisePose.Compose(foot,character.Component);
                legs[i]=legs[i] with{AlignedRS=rs,UnalignedRS=rs,AlignedWS=ws,UnalignedWS=ws,
                    Plane=AlsFootPlane.At(F.InversePosition(root,ikRoot.Position),F.Up)};
            }
            legs[i]=legs[i] with{Distance=distance,Speed=60,Alignment=1};
        }
        history=ProcessCharacter(history,character,approach,legs);var updatedLegs=legs;
        for(var i=0;i<2;i++)
        {
            var l=legs[i];var input=data[i];var inputRS=AlsPrecisePose.Relative(input.Foot,root);
            // Unlocked + no disable-lock curve: the offset is identity. The
            // original still updates its unplant spring states toward zero.
            var offsetSpring=l.OffsetSpring;_ = F.Spring(default(AlsDoubleVector),default,ref offsetSpring,history.Delta,250);
            var offsetRotation=l.OffsetRotationSpring;_ = F.Spring(AlsQuaternion.Identity,AlsQuaternion.Identity,ref offsetRotation,history.Delta,450);
            var unalignedWS=AlsPrecisePose.Compose(AlsPrecisePose.Compose(inputRS,root),character.Component);
            var lastPlane=l.Plane.Transform(root).Transform(character.Component);
            var query=new AlsFootTraceQuery(unalignedWS.Position,approach,-75,100,5,true);var hit=ground.Sweep(i,query);
            if(!hit.Point.IsFinite||!hit.Normal.IsFinite)throw new ArgumentException("Nonfinite ground hit.");
            var floorPoint=character.Blocking?character.FloorPoint:character.Component.Position;
            var impact=hit.Walkable?hit.Point:AlsFootPlane.At(floorPoint,approach*-1).Intersect(unalignedWS.Position,approach);
            var targetPlane=AlsFootPlane.At(impact,hit.Walkable?hit.Normal:approach*-1);
            if(!history.OnGround)targetPlane=AlsFootPlane.At(F.Position(character.Component,ikRoot.Position),approach*-1);
            var height=l.HeightSpring;var rotation=l.RotationSpring;
            if(!history.First)
            {
                var currentIntersection=targetPlane.Intersect(unalignedWS.Position,approach);
                var lastIntersection=lastPlane.Intersect(l.AlignedWS.Position,approach);var previousIntersection=lastPlane.Intersect(unalignedWS.Position,approach);
                var lastDelta=(float)(lastIntersection.Z-currentIntersection.Z);var previousDelta=(float)(previousIntersection.Z-currentIntersection.Z);
                var adjusted=(float)(M.Abs(lastDelta)<M.Abs(previousDelta)?lastIntersection.Z:previousIntersection.Z);
                if(history.OnGround)
                {
                    var planeDelta=(float)(currentIntersection.Z-adjusted);
                    adjusted=(float)(adjusted+(planeDelta>0?M.Min(M.Abs(-history.ComponentDelta.Z),planeDelta):M.Max(-M.Abs(history.ComponentDelta.Z),planeDelta)));
                }
                var springHeight=F.Spring(adjusted,(float)currentIntersection.Z,ref height,history.Delta,2000);
                currentIntersection=currentIntersection with{Z=springHeight};
                if(hit.Walkable)
                {
                    var penetration=-AlsFootPlane.At(impact,targetPlane.Normal).Distance(currentIntersection,F.Down)-10;
                    if(penetration>0)currentIntersection-=F.Down*penetration;
                }
                var q=AlsComponentQuaternion.Between(lastPlane.Normal,targetPlane.Normal);var smooth=F.Spring(AlsQuaternion.Identity,q,ref rotation,history.Delta,450);
                targetPlane=AlsFootPlane.At(currentIntersection,lastPlane.Normal.Rotate(smooth));
            }
            var aligned=Align(input,unalignedWS,ikRoot,character.Component,approach,targetPlane);
            var alignedCS=AlsPrecisePose.Compose(aligned,invComponent);var alignedRS=AlsPrecisePose.Relative(alignedCS,root);
            updatedLegs[i]=l with{HeightSpring=height,RotationSpring=rotation,OffsetSpring=offsetSpring,OffsetRotationSpring=offsetRotation,
                UnalignedRS=inputRS,UnalignedWS=unalignedWS,AlignedRS=alignedRS,AlignedWS=AlsPrecisePose.Compose(alignedCS,character.Component),
                Plane=targetPlane.Transform(invComponent).Transform(invRoot)};
        }
        var targetPelvis=SolvePelvis(pelvis,root,data,updatedLegs);var targetRS=AlsPrecisePose.Relative(targetPelvis,root);
        var pelvisRS=F.InversePosition(root,pelvis.Position);var desired=F.ClampSize(targetRS.Position-pelvisRS,50);
        var pelvisSpring=history.PelvisSpring;var offset=F.Spring(history.PelvisOffset,desired,ref pelvisSpring,history.Delta,350);
        var finalPelvis=AlsPrecisePose.Compose(targetRS with{Position=pelvisRS+offset},root);
        var changes=new AlsPrecisePose[3];var indices=new int[3];indices[0]=_pelvis;changes[0]=finalPelvis;
        for(var i=0;i<2;i++)
        {
            var result=Finalize(i,data[i],pelvis,finalPelvis,root,updatedLegs[i]);indices[i+1]=_legs[i].Ik;changes[i+1]=result.Pose;
            updatedLegs[i]=updatedLegs[i] with{Reachable=result.Reachable};
        }
        if(indices[1]>indices[2]){(indices[1],indices[2])=(indices[2],indices[1]);(changes[1],changes[2])=(changes[2],changes[1]);}
        pose.Apply(indices,changes,alpha);
        return history with{First=false,Delta=0,PelvisOffset=offset,PelvisSpring=pelvisSpring,Root=root,Legs=updatedLegs.ToImmutableArray()};
    }
    private static AlsFootPlacementHistory ProcessCharacter(AlsFootPlacementHistory h,in AlsFootCharacterInput c,AlsDoubleVector approach,AlsFootLegHistory[] legs)
    {
        var last=h.First?c.Component.Position:h.Component.Position;var normal=h.First?approach*-1:h.GroundNormal;
        var spring=h.First?AlsFootQuaternionSpring.Default:h.GroundSpring;var onGround=c.Walking&&c.Blocking;var move=default(AlsDoubleVector);var offset=h.PelvisOffset;
        if(onGround&&h.OnGround)
        {
            var slope=AlsComponentQuaternion.Between(normal,c.FloorNormal);slope=F.Spring(AlsQuaternion.Identity,slope,ref spring,h.Delta,450);normal=normal.Rotate(slope);
            var adjusted=M.Abs(AlsDoubleVector.Dot(approach,normal))>(double)1e-5f?AlsFootPlane.At(last,normal).Intersect(c.Component.Position,approach):c.Component.Position;
            var capsule=(c.Component.Position-adjusted)*(approach*-1);move-=capsule;
            if(!F.NearlyZero(capsule))
            {
                var local=capsule.Rotate(c.Component.Rotation.Conjugate());offset-=local;
                for(var i=0;i<2;i++)legs[i]=legs[i] with{Plane=legs[i].Plane.Translate(local*-1)};
            }
        }
        move+=c.Component.Position-last;
        return h with{Component=c.Component,ComponentDelta=move,GroundNormal=normal,GroundSpring=spring,OnGround=onGround,PelvisOffset=offset};
    }
    private static AlsPrecisePose Align(Input input,AlsPrecisePose unaligned,AlsPrecisePose ikRoot,AlsPrecisePose component,AlsDoubleVector approach,AlsFootPlane plane)
    {
        var source=AlsPrecisePose.Compose(input.Foot,component);var groundRoot=AlsPrecisePose.Compose(ikRoot,component);
        var rootPlane=AlsFootPlane.At(groundRoot.Position,F.Up.Rotate(groundRoot.Rotation));var distance=rootPlane.Distance(source.Position,approach);
        var location=plane.Intersect(unaligned.Position,approach)-approach*distance;
        var delta=AlsComponentQuaternion.Between(rootPlane.Normal,plane.Normal);var aligned=delta*source.Rotation;
        var relative=aligned.Conjugate()*unaligned.Rotation;var normal=plane.Normal.Rotate(aligned.Conjugate());var twist=F.Twist(relative,normal);
        var rotation=aligned*twist;var footDelta=rotation.Conjugate()*unaligned.Rotation;var ankle=F.Twist(footDelta,F.SafeNormal(input.FootToBall.Position));
        rotation*=F.Slerp(AlsQuaternion.Identity,ankle,.75);return new(location,rotation,AlsDoubleVector.One);
    }
    private float MaxExtension(float desired,int leg)=>desired>_legs[leg].Limb?desired:desired+(_legs[leg].Limb-desired)*.5f;
    private float MinExtension(float desired,int leg)=>M.Min(desired,_legs[leg].Limb*.3f);
    private (float Desired,float Max,float Min) Range(int leg,Input input,AlsPrecisePose pelvis,AlsPrecisePose rebalanced,AlsDoubleVector target)
    {
        var hip=AlsPrecisePose.Compose(AlsPrecisePose.Relative(input.Hip,pelvis),rebalanced).Position;
        var desiredSquared=(float)(input.Foot.Position-input.Hip.Position).LengthSquared;var desired=MathF.Sqrt(desiredSquared);var max=MaxExtension(desired,leg);
        var plane=AlsFootPlane.At(target,F.Up);var height=(float)plane.Dot(hip);var desiredTarget=target;var maxTarget=target;
        if(height>0)
        {
            var fk=input.Foot.Position-plane.Normal*plane.Dot(input.Foot.Position);var hp=hip-plane.Normal*plane.Dot(hip);
            var initial=(float)M.Sqrt((hp-fk).LengthSquared);var toFk=fk-target;var fkDistance=(float)M.Sqrt(toFk.LengthSquared);
            target+=F.SafeNormal(toFk)*M.Min(fkDistance,_legs[leg].Length)*.5f;var toHip=hp-target;var offset=(float)M.Sqrt(toHip.LengthSquared);
            AlsDoubleVector Adjust(float length)
            {
                var minHeight=height-10;var maxOffset=MathF.Sqrt(M.Max(0,length*length-minHeight*minHeight));var clamp=M.Max(initial,maxOffset);
                return offset>clamp?target+F.SafeNormal(toHip)*(offset-clamp):target;
            }
            maxTarget=Adjust(max);desiredTarget=Adjust(desired);
        }
        var maxLocation=F.SphereLine(maxTarget,max,hip-F.Down*100,F.Down);var desiredLocation=F.SphereLine(desiredTarget,desired,hip-F.Down*100,F.Down);
        var minLocation=desiredTarget+F.Up*MinExtension(desired,leg);
        return ((float)(desiredLocation-hip).Z,(float)(maxLocation-hip).Z,(float)(minLocation-hip).Z-input.Distance);
    }
    private AlsPrecisePose SolvePelvis(AlsPrecisePose pelvis,AlsPrecisePose root,Input[] data,AlsFootLegHistory[] legs)
    {
        var avg=default(AlsDoubleVector);for(var i=0;i<2;i++)avg+=(F.Position(root,legs[i].AlignedRS.Position)-data[i].Foot.Position)*.5;
        var offset=(avg-F.Down*AlsDoubleVector.Dot(F.Down,avg))*.3f;var rebalanced=pelvis with{Position=pelvis.Position+offset};
        float maxMin=3.4e38f,desiredMin=3.4e38f,desiredAvg=0,minMax=-3.4e38f;
        for(var i=0;i<2;i++){var r=Range(i,data[i],pelvis,rebalanced,F.Position(root,legs[i].AlignedRS.Position));desiredAvg+=r.Desired/2;desiredMin=M.Min(desiredMin,r.Desired);maxMin=M.Min(maxMin,r.Max);minMax=M.Max(minMax,r.Min);}
        var minAvg=desiredAvg-desiredMin;var minMaxDelta=maxMin-desiredMin;desiredMin-=.05f;var divisor=minAvg+minMaxDelta;
        var z=M.Abs(divisor)<=1e-4f?desiredMin:desiredMin+(minAvg*minMaxDelta)/divisor;
        // UE Clamp retains its branch order even when the two limb limits cross.
        z=z<minMax?minMax:z<maxMin?z:maxMin;offset+=F.Down*(-z);
        return pelvis with{Position=pelvis.Position+offset};
    }
    private (AlsPrecisePose Pose,bool Reachable) Finalize(int leg,Input input,AlsPrecisePose pelvis,AlsPrecisePose finalPelvis,AlsPrecisePose root,AlsFootLegHistory history)
    {
        var hip=AlsPrecisePose.Compose(AlsPrecisePose.Relative(input.Hip,pelvis),finalPelvis);var corrected=AlsPrecisePose.Compose(history.AlignedRS,root);
        var ball=AlsPrecisePose.Compose(input.FootToBall,corrected).Position;var inputDelta=input.Foot.Position-input.Hip.Position;
        var direction=F.SafeNormal(corrected.Position-hip.Position);var reachable=history.Reachable;
        if(!F.NearlyZero(inputDelta)&&!F.NearlyZero(direction))
        {
            var desired=(float)M.Sqrt(inputDelta.LengthSquared);var max=MaxExtension(desired,leg);var current=(float)M.Sqrt((corrected.Position-hip.Position).LengthSquared);var remaining=current-max;
            if(current>max)
            {
                reachable=false;
                if(history.TimeUnaligned==0)
                {
                    var pull=M.Min(_legs[leg].Length,remaining)*history.Alignment;remaining-=pull;var location=corrected.Position-direction*pull;
                    var q=F.BetweenVectors(ball-corrected.Position,ball-location);corrected=corrected with{Position=location,Rotation=AlsComponentQuaternion.Normalize(q*corrected.Rotation)};
                }
                if(remaining>0)corrected=corrected with{Position=F.SphereLine(hip.Position,max,corrected.Position,direction)};
            }
            else reachable=true;
        }
        var plane=history.Plane.Transform(root);var penetration=M.Min(plane.Distance(ball,F.Down),plane.Distance(corrected.Position,F.Down));penetration-=M.Min(0,input.Distance);
        if(penetration<0)corrected=corrected with{Position=corrected.Position+F.Down*penetration};
        var minimum=MinExtension((float)M.Abs(AlsDoubleVector.Dot(inputDelta,F.Down)),leg);var hipPlane=AlsFootPlane.At(hip.Position+F.Down*minimum,F.Down);var distance=(float)hipPlane.Dot(corrected.Position);
        if(distance<0)corrected=corrected with{Position=corrected.Position-F.Down*distance};return (corrected,reachable);
    }
}
