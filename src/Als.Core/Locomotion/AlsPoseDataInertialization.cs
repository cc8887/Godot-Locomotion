using System.Numerics;
using GodotAls.Core.Curves;

namespace GodotAls.Core.Locomotion;

/// <summary>Precise pose inertia with curve flags and root-motion velocity history.
/// The existing ALS kernel owns pose/scalar history. Other attributes pass through
/// at the caller, and candidate instances can copy or reset the complete history.</summary>
public sealed class AlsPoseDataInertialization
{
    private readonly int _boneCount;
    private readonly AlsInertialization _kernel;
    private readonly AlsInertialCurve[] _inputCurves,_outputCurves;
    private readonly bool[] _currentPresence,_previousPresence,_differencePresence;
    private readonly uint[] _differenceFlags;
    private AlsTransformAnimationAttribute _currentRoot;
    private AlsPrecisePose _lastComponent=AlsPrecisePose.Identity;
    private AlsDoubleVector _lastRootBone;
    private float _delta,_historyDelta,_request=-1;
    private Vector3 _rootTranslation,_rootRotation,_rootScale;
    private float _translationMagnitude,_rotationMagnitude,_scaleMagnitude;
    public bool Active=>_kernel.IsActive;
    public float Elapsed=>_kernel.ElapsedSeconds;
    public float Duration=>_kernel.DurationSeconds;
    public float Deficit=>_kernel.DeficitSeconds;
    public int HistoryCount=>_kernel.HistoryCount;
    public float PendingDelta=>_delta;
    public float PendingRequest=>_request;
    public AlsPoseDataInertialization(int boneCount, int curveCount, float unitsPerCentimeter = 1)
    {
        _kernel=new(boneCount,curveCount,unitsPerCentimeter);_boneCount=boneCount;
        _inputCurves=new AlsInertialCurve[curveCount];_outputCurves=new AlsInertialCurve[curveCount];
        _currentPresence=new bool[curveCount];_previousPresence=new bool[curveCount];
        _differencePresence=new bool[curveCount];_differenceFlags=new uint[curveCount];
    }
    public void Reset()
    {
        _kernel.Reset();_delta=_historyDelta=0;_request=-1;_currentRoot=default;_lastComponent=AlsPrecisePose.Identity;_lastRootBone=default;
        Array.Clear(_currentPresence);Array.Clear(_previousPresence);Array.Clear(_differencePresence);Array.Clear(_differenceFlags);
        _rootTranslation=_rootRotation=_rootScale=default;_translationMagnitude=_rotationMagnitude=_scaleMagnitude=0;
    }
    public void CopyFrom(AlsPoseDataInertialization source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _kernel.CopyFrom(source._kernel);_delta=source._delta;_historyDelta=source._historyDelta;_request=source._request;
        _currentRoot=source._currentRoot;_lastComponent=source._lastComponent;_lastRootBone=source._lastRootBone;
        source._currentPresence.CopyTo(_currentPresence,0);source._previousPresence.CopyTo(_previousPresence,0);
        source._differencePresence.CopyTo(_differencePresence,0);source._differenceFlags.CopyTo(_differenceFlags,0);
        _rootTranslation=source._rootTranslation;_rootRotation=source._rootRotation;_rootScale=source._rootScale;
        _translationMagnitude=source._translationMagnitude;_rotationMagnitude=source._rotationMagnitude;_scaleMagnitude=source._scaleMagnitude;
    }
    public void Update(float delta)
    {if(!float.IsFinite(delta)||delta<0||!float.IsFinite(_delta+delta))throw new ArgumentException("Invalid animation inertia delta.");_kernel.Update(delta);_delta+=delta;}
    public void Request(float duration)
    {_kernel.Request(duration);_request=_request<0?duration:MathF.Min(_request,duration);}
    public void Evaluate(ReadOnlySpan<AlsPrecisePose> pose, ReadOnlySpan<AlsAnimationCurveSample> curves,
        AlsTransformAnimationAttribute rootMotion, in AlsPrecisePose component, long actorParent, float teleportDistance,
        Span<AlsPrecisePose> outputPose, Span<AlsAnimationCurveSample> outputCurves, out AlsTransformAnimationAttribute outputRootMotion)
    {
        if(pose.Length != _boneCount || outputPose.Length != pose.Length || curves.Length != _inputCurves.Length ||
            outputCurves.Length != curves.Length || !float.IsFinite(teleportDistance) || teleportDistance < 0 ||
            curves.Overlaps(outputCurves, out var offset) && offset != 0)
            throw new ArgumentException("Invalid animation inertia layout or input.");
        if(rootMotion.Present)rootMotion.Value.Validate(.001);
        var pending=_request>=0&&HistoryCount>0;
        static AlsDoubleVector World(in AlsPrecisePose c,AlsDoubleVector p)=>(c.Scale*p).Rotate(c.Rotation)+c.Position;
        var teleported=HistoryCount>0&&teleportDistance>0&&(World(component,pose[0].Position)-World(_lastComponent,_lastRootBone)).LengthSquared>(double)teleportDistance*teleportDistance;
        for(var c=0;c<_inputCurves.Length;c++)_inputCurves[c]=new(curves[c].Value,curves[c].Present);
        _kernel.EvaluatePrecisePose(pose,_inputCurves,component,actorParent,teleportDistance,outputPose,_outputCurves);
        if(teleported)_delta=0;
        if(pending&&Active&&!teleported)
        {
            for(var c=0;c<_differenceFlags.Length;c++)
            {
                // NamedValueArray Union constructs missing elements with
                // default flags; only CopyFrom of the incoming curve sets flags.
                _differenceFlags[c]=curves[c].Present?curves[c].Flags:0;
                _differencePresence[c]=curves[c].Present||_currentPresence[c]||(_historyDelta>.0001f&&_previousPresence[c]);
            }
            _rootTranslation=_rootRotation=_rootScale=default;_translationMagnitude=_rotationMagnitude=_scaleMagnitude=0;
            if(rootMotion.Present&&_currentRoot.Present&&_historyDelta>.0001f&&_delta>.0001f)
            {
                var before=_currentRoot.Value;var now=rootMotion.Value;
                Difference(before.Position*(1d/_historyDelta)-now.Position*(1d/_delta),out _rootTranslation,out _translationMagnitude);
                Difference(Log(before.Rotation)*(1d/_historyDelta)-Log(now.Rotation)*(1d/_delta),out _rootRotation,out _rotationMagnitude);
                Difference(before.Scale*(1d/_historyDelta)-now.Scale*(1d/_delta),out _rootScale,out _scaleMagnitude);
            }
        }
        for(var c=0;c<outputCurves.Length;c++)
        {
            var source=curves[c];var value=_outputCurves[c];
            outputCurves[c]=Active?new(value.Value,source.Present||_differencePresence[c],(source.Present?source.Flags:0)|_differenceFlags[c]):source;
        }
        outputRootMotion=rootMotion;
        if(Active&&rootMotion.Present)
        {
            float Decay(float value)=>AlsInertialDecay.Evaluate(value,0,Elapsed,Duration);
            var root=rootMotion.Value;
            outputRootMotion=new(root with{
                Position=root.Position+new AlsDoubleVector(_rootTranslation)*_delta*Decay(_translationMagnitude),
                Rotation=Exp(new AlsDoubleVector(_rootRotation)*_delta*Decay(_rotationMagnitude))*root.Rotation,
                Scale=root.Scale+new AlsDoubleVector(_rootScale)*_delta*Decay(_scaleMagnitude)},true);
        }
        _currentPresence.CopyTo(_previousPresence,0);for(var c=0;c<_currentPresence.Length;c++)_currentPresence[c]=outputCurves[c].Present;
        _currentRoot=outputRootMotion;_lastRootBone=outputPose[0].Position;_lastComponent=component;
        _historyDelta=_delta;_delta=0;_request=-1;
    }
    private static void Difference(AlsDoubleVector delta,out Vector3 direction,out float magnitude)
    {
        magnitude=(float)System.Math.Sqrt(delta.LengthSquared);
        direction=magnitude>.0001f?delta.ToSingle()*(1f/magnitude):default;
    }
    private static AlsDoubleVector Log(AlsQuaternion q)
    {
        var scale=1d;if(System.Math.Abs(q.W)<1){var angle=System.Math.Acos(q.W);var sin=System.Math.Sin(angle);if(System.Math.Abs(sin)>=1e-8)scale=angle/sin;}
        return new(q.X*scale*2,q.Y*scale*2,q.Z*scale*2);
    }
    private static AlsQuaternion Exp(AlsDoubleVector rotation)
    {
        var v=rotation*.5;var angle=System.Math.Sqrt(v.LengthSquared);var sin=System.Math.Sin(angle);var scale=System.Math.Abs(sin)>=1e-8?sin/angle:1d;
        return new(v.X*scale,v.Y*scale,v.Z*scale,System.Math.Cos(angle));
    }
}
