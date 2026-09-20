using System.Text.Json;
using GodotAls.Core.Locomotion;
using V3=System.Numerics.Vector3;

namespace GodotAls.Animation;

// Test-only isolation: native upstream poses, real Godot graph requests.
// These outputs never feed the production owner or its committed feedback.
internal sealed class AlsNativeInertiaDiagnostic
{
    private readonly JsonElement _frames;
    private readonly int[] _mapping;
    private readonly AlsLocalPose[] _input,_singleOutput,_preciseOutput;
    private readonly AlsQuaternion[] _rotations,_outputRotations;
    private readonly AlsInertialCurve[] _curves,_singleCurves,_preciseCurves;
    private readonly AlsInertialization _single,_precise;
    internal ReadOnlySpan<AlsLocalPose> SinglePose=>_singleOutput;
    internal ReadOnlySpan<AlsLocalPose> PrecisePose=>_preciseOutput;
    internal ReadOnlySpan<AlsInertialCurve> SingleCurves=>_singleCurves;
    internal ReadOnlySpan<AlsInertialCurve> PreciseCurves=>_preciseCurves;

    internal AlsNativeInertiaDiagnostic(JsonElement native,string traceName,ReadOnlySpan<string> names,int curveCount,int frames)
    {
        var nativeNames=native.GetProperty("names").EnumerateArray().Select(n=>n.GetString()!).ToArray();
        _mapping=names.ToArray().Select(n=>Array.FindIndex(nativeNames,u=>u.Equals(n,StringComparison.OrdinalIgnoreCase))).ToArray();
        _frames=native.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==traceName).GetProperty("frames");
        if(_mapping.Any(i=>i<0) || _frames.GetArrayLength()!=frames)throw new ArgumentException("Native inertia fixture layout differs.");
        _input=new AlsLocalPose[names.Length]; _singleOutput=new AlsLocalPose[names.Length]; _preciseOutput=new AlsLocalPose[names.Length];
        _rotations=new AlsQuaternion[names.Length]; _outputRotations=new AlsQuaternion[names.Length];
        _curves=new AlsInertialCurve[curveCount]; _singleCurves=new AlsInertialCurve[curveCount]; _preciseCurves=new AlsInertialCurve[curveCount];
        _single=new(names.Length,curveCount,.01f,-V3.UnitX); _precise=new(names.Length,curveCount,.01f,-V3.UnitX);
    }

    internal void Evaluate(int frame,float delta,float request,in AlsLocalPose component,ReadOnlySpan<string> curveNames)
    {
        var row=_frames[frame-1];
        if(row.GetProperty("serial").GetInt32()!=frame || row.GetProperty("input").GetProperty("delta").GetDouble()!=(double)delta)
            throw new ArgumentException("Native inertia frame differs.");
        var stage=row.GetProperty("stages").GetProperty("MainMovement");
        var poses=stage.GetProperty("pose"); var curves=stage.GetProperty("curves");
        for(var b=0;b<_input.Length;b++)
        {
            var pose=poses[_mapping[b]]; var p=pose.GetProperty("position"); var q=pose.GetProperty("rotation"); var s=pose.GetProperty("scale");
            _rotations[b]=new(-q[0].GetDouble(),q[1].GetDouble(),-q[2].GetDouble(),q[3].GetDouble());
            _input[b]=new(new((float)(p[0].GetDouble()*.01),(float)(-p[1].GetDouble()*.01),(float)(p[2].GetDouble()*.01)),
                _rotations[b].ToSingle(),new(s[0].GetSingle(),s[1].GetSingle(),s[2].GetSingle()));
        }
        for(var c=0;c<_curves.Length;c++)_curves[c]=curves.TryGetProperty(curveNames[c],out var value) ? new(value.GetSingle()) : default;
        _single.Update(delta); _precise.Update(delta);
        if(request>=0){_single.Request(request); _precise.Request(request);}
        _single.Evaluate(_input,_curves,component,0,0,_singleOutput,_singleCurves);
        _precise.EvaluatePrecise(_input,_curves,component,0,0,_preciseOutput,_preciseCurves,
            _rotations,new(component.Rotation),_outputRotations);
    }
}
