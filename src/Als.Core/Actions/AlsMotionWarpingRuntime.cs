using System.Collections.Immutable;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using M=System.Math;

namespace GodotAls.Core.Actions;

public enum AlsMotionWarpingModifierState:byte { Waiting,Active,MarkedForRemoval,Disabled }
public readonly record struct AlsMotionWarpingWindow(int AssetId,float Start,float End,bool StaticWarpPoint,
    AlsPrecisePose WarpPoint,AlsPrecisePose RootAtEnd);
public readonly record struct AlsMotionWarpingContext(int AssetId,float Previous,float Current,float Weight,float Rate,float Delta);
public readonly record struct AlsMotionWarpingTarget(string Name,AlsPrecisePose Transform,bool WarpPaused=false,bool RootPaused=false);
public enum AlsMotionWarpingTargetOperation:byte { Set,Remove }
public readonly record struct AlsMotionWarpingTargetRequest(AlsMotionWarpingTargetOperation Operation,AlsMotionWarpingTarget Target);
public interface IAlsMotionWarpingRootSource
{ AlsPrecisePose ExtractRange(int montageAsset,float previous,float current); }
public readonly record struct AlsMotionWarpingModifier(long Id,AlsMotionWarpingWindow Window,AlsMotionWarpingModifierState State,
    float Previous,float Current,float Weight,float Rate,float ActualStart,AlsPrecisePose StartTransform,
    AlsPrecisePose TotalWindow,AlsPrecisePose Target,bool OffsetPresent,AlsPrecisePose Offset,bool RootPaused,bool WarpPaused);

public sealed class AlsMotionWarpingCandidate
{
    internal readonly AlsMotionWarpingRuntime Owner;
    internal readonly ImmutableDictionary<string,AlsMotionWarpingTarget> Targets;
    internal readonly long Serial;
    public AlsFrameIdentity Identity {get;}
    public ImmutableArray<AlsMotionWarpingModifier> Modifiers {get;}
    public AlsPrecisePose Warped {get;}
    internal AlsMotionWarpingCandidate(AlsMotionWarpingRuntime owner,AlsFrameIdentity identity,
        ImmutableDictionary<string,AlsMotionWarpingTarget> targets,long serial,ImmutableArray<AlsMotionWarpingModifier> modifiers,AlsPrecisePose warped)
    {Owner=owner;Identity=identity;Targets=targets;Serial=serial;Modifiers=modifiers;Warped=warped;}
}

// Original Lyra SkewWarp profile: in-place translation, Linear added motion,
// ignore Z, feet location, Default/Slerp rotation and no remaining-root offset.
// Targets and modifier history are candidates; no pose or physical publication.
public sealed class AlsMotionWarpingRuntime
{
    private readonly ImmutableArray<AlsMotionWarpingWindow> _windows;
    private readonly IAlsMotionWarpingRootSource _source;
    private readonly uint _character,_generation;
    private ImmutableDictionary<string,AlsMotionWarpingTarget> _targets=ImmutableDictionary.Create<string,AlsMotionWarpingTarget>(StringComparer.OrdinalIgnoreCase);
    private ImmutableArray<AlsMotionWarpingModifier> _modifiers=[];
    private long _serial,_lastFrame=-1;
    private AlsMotionWarpingCandidate? _pending;
    public ImmutableArray<AlsMotionWarpingModifier> Committed=>_modifiers;
    public AlsMotionWarpingRuntime(uint character,uint generation,ImmutableArray<AlsMotionWarpingWindow> windows,IAlsMotionWarpingRootSource source)
    {
        _character=character;_generation=generation;_windows=windows;_source=source??throw new ArgumentNullException(nameof(source));
        foreach(var w in windows)
        {if(w.AssetId<0||!float.IsFinite(w.Start)||!float.IsFinite(w.End)||w.Start<0||w.End<=w.Start)throw new ArgumentException("Invalid MotionWarping window.");w.WarpPoint.Validate();w.RootAtEnd.Validate();}
        if(windows.DistinctBy(w=>(w.AssetId,w.Start,w.End)).Count()!=windows.Length)throw new ArgumentException("Duplicate MotionWarping window.");
    }
    public AlsMotionWarpingCandidate Begin(AlsFrameIdentity id,bool rootPresent,in AlsPrecisePose local,
        in AlsMotionWarpingContext context,in AlsPrecisePose actor,in AlsPrecisePose visualRoot,in AlsPrecisePose baseOffset,
        ReadOnlySpan<AlsMotionWarpingTargetRequest> targets=default,bool disable=false)
    {
        if(_pending is not null||id.CharacterId!=_character||id.SlotGeneration!=_generation||id.FrameId<=_lastFrame)
            throw new InvalidOperationException("Foreign, old or pending MotionWarping frame.");
        if(context.AssetId< -1||!float.IsFinite(context.Previous)||!float.IsFinite(context.Current)||!float.IsFinite(context.Weight)||
            !float.IsFinite(context.Rate)||!float.IsFinite(context.Delta)||context.Delta<0)throw new ArgumentException("Invalid MotionWarping context.");
        local.Validate();actor.Validate();visualRoot.Validate();baseOffset.Validate();
        var nextTargets=_targets;
        foreach(var r in targets)
        {
            if(string.IsNullOrWhiteSpace(r.Target.Name))throw new ArgumentException("Missing warp target name.");
            if(r.Operation==AlsMotionWarpingTargetOperation.Set){r.Target.Transform.Validate();nextTargets=nextTargets.SetItem(r.Target.Name,r.Target);}
            else if(r.Operation==AlsMotionWarpingTargetOperation.Remove)nextTargets=nextTargets.Remove(r.Target.Name);
            else throw new ArgumentException("Invalid warp target operation.");
        }
        var list=_modifiers.Select(m=>disable?m with{State=AlsMotionWarpingModifierState.Disabled}:m).ToList();long serial=_serial;var warped=local;
        if(rootPresent)
        {
            foreach(var w in _windows)
                if(w.AssetId==context.AssetId&&context.Previous>=w.Start&&context.Previous<w.End&&!list.Any(m=>m.Window.AssetId==w.AssetId&&m.Window.Start==w.Start&&m.Window.End==w.End))
                    list.Add(new(++serial,w,AlsMotionWarpingModifierState.Waiting,0,0,0,1,0,AlsPrecisePose.Identity,
                        AlsPrecisePose.Identity,AlsPrecisePose.Identity,false,AlsPrecisePose.Identity,false,false));
            for(int i=0;i<list.Count;i++)list[i]=Update(list[i],context,nextTargets,visualRoot,baseOffset);
            list.RemoveAll(m=>m.State==AlsMotionWarpingModifierState.MarkedForRemoval);
            foreach(var m in list)if(m.State==AlsMotionWarpingModifierState.Active)warped=Process(m,warped,context.Delta,actor,visualRoot,baseOffset);
        }
        return _pending=new(this,id,nextTargets,serial,list.ToImmutableArray(),warped);
    }
    private AlsMotionWarpingModifier Update(AlsMotionWarpingModifier m,in AlsMotionWarpingContext c,
        ImmutableDictionary<string,AlsMotionWarpingTarget> targets,in AlsPrecisePose visual,in AlsPrecisePose offset)
    {
        if(c.AssetId!=m.Window.AssetId)return m with{State=AlsMotionWarpingModifierState.MarkedForRemoval};
        m=m with{Previous=c.Previous,Current=c.Current,Weight=c.Weight,Rate=c.Rate};
        if(c.Previous>=m.Window.End)return m with{State=AlsMotionWarpingModifierState.MarkedForRemoval};
        if(m.State==AlsMotionWarpingModifierState.Active&&c.Previous<m.Window.End&&(c.Current>m.Window.End||c.Current<m.Window.Start)&&
            MathF.Abs((c.Current-c.Previous)-c.Delta*c.Rate)>1e-4f)return m with{State=AlsMotionWarpingModifierState.MarkedForRemoval};
        if(c.Previous>=m.Window.Start&&c.Previous<m.Window.End&&m.State==AlsMotionWarpingModifierState.Waiting)
            m=m with{State=AlsMotionWarpingModifierState.Active,ActualStart=c.Previous,StartTransform=visual,TotalWindow=_source.ExtractRange(m.Window.AssetId,m.Window.Start,m.Window.End)};
        if(m.State!=AlsMotionWarpingModifierState.Active)return m;
        if(!targets.TryGetValue("Align",out var target))return m with{State=AlsMotionWarpingModifierState.Disabled};
        m=m with{RootPaused=target.RootPaused,WarpPaused=target.WarpPaused};var transformed=target.Transform;
        if(m.Window.StaticWarpPoint)
        {
            if(!m.OffsetPresent)
            {
                var inverse=new AlsPrecisePose(default,offset.Rotation.Conjugate(),AlsDoubleVector.One);
                var root=AlsPrecisePose.Compose(inverse,m.Window.RootAtEnd);var point=AlsPrecisePose.Compose(inverse,m.Window.WarpPoint);
                m=m with{OffsetPresent=true,Offset=AlsPrecisePose.Relative(root,point)};
            }
            transformed=AlsPrecisePose.Compose(m.Offset,transformed);
        }
        if(!Equal(m.Target,transformed))m=m with{Target=transformed,ActualStart=c.Previous,StartTransform=visual};
        return m;
    }
    private AlsPrecisePose Process(in AlsMotionWarpingModifier m,in AlsPrecisePose input,float delta,
        in AlsPrecisePose actor,in AlsPrecisePose visual,in AlsPrecisePose offset)
    {
        var total=_source.ExtractRange(m.Window.AssetId,m.Previous,m.Window.End);
        var part=_source.ExtractRange(m.Window.AssetId,m.Previous,MathF.Min(m.Current,m.Window.End));
        var extra=m.Current>m.Window.End?_source.ExtractRange(m.Window.AssetId,m.Window.End,m.Current):AlsPrecisePose.Identity;
        var position=input.Position;var rotation=input.Rotation;
        if(m.RootPaused)position=default;
        else if(!m.WarpPaused)
        {
            if(!m.TotalWindow.Position.NearlyZero(2e-4))throw new NotSupportedException("This original Lyra SkewWarp profile requires in-place translation.");
            var target=m.Target.Position with{Z=visual.Position.Z};var direction=target-visual.Position;
            if(direction.NearlyZero(1e-4f))position=default;
            else
            {
                float alpha=M.Clamp((m.Current-m.ActualStart)/(m.Window.End-m.ActualStart),0,1);
                var next=m.StartTransform.Position+(target-m.StartTransform.Position)*alpha;next=next with{Z=visual.Position.Z};
                double yaw=M.Atan2(direction.Y,direction.X);var facing=new AlsQuaternion(0,0,M.Sin(yaw*.5),M.Cos(yaw*.5));
                var forward=new AlsDoubleVector(1,0,0).Rotate(actor.Rotation.Conjugate()*facing);
                position=(forward*M.Sqrt((next-visual.Position).LengthSquared)).Rotate(offset.Rotation.Conjugate())+extra.Position;
            }
        }
        if(m.RootPaused)rotation=AlsQuaternion.Identity;
        else if(m.WarpPaused)rotation=part.Rotation;
        else
        {
            var current=actor.Rotation*offset.Rotation;var target=current.Conjugate()*(m.Target.Rotation*offset.Rotation);
            float remaining=m.Window.End-m.Previous;float alpha=M.Clamp(delta*m.Rate/remaining,0,1);
            var thisFrame=AlsFootPlacementMath.Slerp(total.Rotation,target,alpha);var change=thisFrame*total.Rotation.Conjugate();
            if(Equal(change,AlsQuaternion.Identity))change=AlsQuaternion.Identity;
            rotation=extra.Rotation*(change*part.Rotation);
        }
        return input with{Position=position,Rotation=rotation};
    }
    private static bool Equal(in AlsPrecisePose a,in AlsPrecisePose b)=>Equal(a.Rotation,b.Rotation)&&(a.Position-b.Position).NearlyZero(1e-4f)&&(a.Scale-b.Scale).NearlyZero(1e-4f);
    private static bool Equal(AlsQuaternion a,AlsQuaternion b)
    {
        bool Near(AlsQuaternion v)=>M.Abs(v.X)<=1e-4f&&M.Abs(v.Y)<=1e-4f&&M.Abs(v.Z)<=1e-4f&&M.Abs(v.W)<=1e-4f;
        return Near(a+-b)||Near(a+b);
    }
    public void ValidateCommit(AlsMotionWarpingCandidate candidate)
    {if(!ReferenceEquals(candidate,_pending)||candidate.Owner!=this)throw new InvalidOperationException("Foreign, cancelled or consumed MotionWarping candidate.");}
    public void Commit(AlsMotionWarpingCandidate candidate)
    {ValidateCommit(candidate);_targets=candidate.Targets;_modifiers=candidate.Modifiers;_serial=candidate.Serial;_lastFrame=candidate.Identity.FrameId;_pending=null;}
    public void Cancel()=>_pending=null;
}
