using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Curves;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original rich curves, in the source policy's name order. No playback clock.</summary>
public sealed class AlsMantlingCurveSource
{
    private readonly string[] _names;
    private readonly AlsCurveKey[][] _keys;
    public string SourcePath { get; }
    public string AnimationInputsDigest { get; }
    public ReadOnlySpan<string> Names=>_names;
    internal AlsMantlingCurveSource(string path,string digest,string[] names,AlsCurveKey[][] keys)
    {SourcePath=path;AnimationInputsDigest=digest;_names=names.ToArray();_keys=keys.Select(k=>k.ToArray()).ToArray();}
    public void Sample(float seconds,Span<AlsInertialCurve> output)
    {
        if(output.Length!=_keys.Length||!float.IsFinite(seconds))throw new ArgumentException("Invalid mantle curve output/time.");
        for(var i=0;i<_keys.Length;i++)
        {
            if(!AlsCurveRuntime.TrySample(new(i,0,_keys[i].Length,0,1,0),_keys[i],0,seconds,out var value,out var failure))
                throw new InvalidOperationException("Invalid mantle curve sample: "+failure);
            output[i]=new(value);
        }
    }
}

public static class AlsMantlingCurveCompiler
{
    public static IReadOnlyDictionary<string,AlsMantlingCurveSource> Compile(string json,string animationJson)
        =>CompileInternal(json,animationJson,false);
    public static IReadOnlyDictionary<string,AlsMantlingCurveSource> CompileMontages(string json,string animationJson)
        =>CompileInternal(json,animationJson,true);
    private static IReadOnlyDictionary<string,AlsMantlingCurveSource> CompileInternal(string json,string animationJson,bool montage)
    {
        using var doc=JsonDocument.Parse(json);using var animation=JsonDocument.Parse(animationJson);
        var root=doc.RootElement;
        var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(animationJson)));
        Require(root.GetProperty("schemaVersion").GetInt32()==1&&animation.RootElement.GetProperty("schemaVersion").GetInt32()==1&&
            Text(root,"animationInputsSha256").Equals(digest,StringComparison.OrdinalIgnoreCase),"Foreign mantle curve source digest/schema.");
        var collection=montage?"montages":"sequences";
        var policies=animation.RootElement.GetProperty(collection).EnumerateArray()
            .ToDictionary(r=>montage?Text(r,"path"):Text(r.GetProperty("raw"),"source"),r=>r,StringComparer.Ordinal);
        var result=new Dictionary<string,AlsMantlingCurveSource>(StringComparer.Ordinal);
        foreach(var sequence in root.GetProperty(collection).EnumerateArray())
        {
            var path=Text(sequence,"source");Require(policies.TryGetValue(path,out var policy),"Unrelated mantle curve source.");
            // Text is used only for name closure, never for rounded key values.
            var modelCurveData=montage?Regex.Matches(Text(policy,"nativeText"),@"(?m)^      CurveData=([^\r\n]+)"):null;
            Require(!montage||modelCurveData!.Count==1,"Missing unique montage data-model curve payload.");
            var names=montage?Regex.Matches(modelCurveData![0].Value,"CurveName=\"([^\"]+)\"").Select(m=>m.Groups[1].Value).ToArray():
                policy.GetProperty("evaluation").GetProperty("floatCurveNames").EnumerateArray().Select(n=>n.GetString()!).ToArray();
            Require((montage||names.Length==policy.GetProperty("evaluation").GetProperty("floatCurveCount").GetInt32())&&names.All(n=>!string.IsNullOrWhiteSpace(n))&&
                names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==names.Length,"Invalid source curve layout.");
            var curves=new Dictionary<string,AlsCurveKey[]>(StringComparer.OrdinalIgnoreCase);
            foreach(var row in sequence.GetProperty("curves").EnumerateArray())
            {
                Require(Text(row,"preInfinity")=="RCCE_Constant"&&Text(row,"postInfinity")=="RCCE_Constant",
                    "Unsupported mantle curve infinity policy.");
                _=Number(row,"defaultValue");
                var keys=new List<AlsCurveKey>();
                foreach(var key in row.GetProperty("keys").EnumerateArray())
                {
                    Require(Text(key,"weightMode")=="RCTWM_WeightedNone","Weighted mantle curves need the native weighted evaluator.");
                    _=Number(key,"arriveWeight");_=Number(key,"leaveWeight");
                    Require(Text(key,"tangentMode") is "RCTM_Auto" or "RCTM_User" or "RCTM_Break" or "RCTM_None" or "RCTM_SmartAuto",
                        "Unknown mantle tangent authoring mode.");
                    var mode=Text(key,"interpolation") switch
                    { "RCIM_Constant"=>AlsCurveInterpolationMode.Constant,"RCIM_Linear"=>AlsCurveInterpolationMode.Linear,
                        "RCIM_Cubic"=>AlsCurveInterpolationMode.Cubic,_=>throw new ArgumentException("Unknown mantle curve interpolation.") };
                    var time=Number(key,"time");Require(keys.Count==0||time>keys[^1].TimeSeconds,"Unordered mantle curve keys.");
                    keys.Add(new(time,Number(key,"value"),Number(key,"arriveTangent"),Number(key,"leaveTangent"),mode));
                }
                Require(keys.Count>0&&curves.TryAdd(Text(row,"name"),keys.ToArray()),"Empty or duplicate mantle curve.");
            }
            Require(names.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(curves.Keys),"Incomplete mantle curve closure.");
            Require(result.TryAdd(path,new(path,digest,names,names.Select(n=>curves[n]).ToArray())),"Duplicate mantle curve source.");
        }
        Require(result.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(policies.Keys),"Incomplete mantle curve source closure.");
        return new ReadOnlyDictionary<string,AlsMantlingCurveSource>(result);
    }
    private static string Text(JsonElement row,string field)=>row.GetProperty(field).GetString()??throw new ArgumentException(field);
    private static float Number(JsonElement row,string field)
    {var value=row.GetProperty(field).GetSingle();Require(float.IsFinite(value),"Nonfinite mantle curve value.");return value;}
    private static void Require(bool condition,string message){if(!condition)throw new ArgumentException(message);}
}
