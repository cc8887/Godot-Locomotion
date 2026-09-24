using System.Collections.ObjectModel;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Immutable original-key pose resource in native UE centimeters/bone basis.
/// Not a V4 manifest, playback instance, curve evaluator or notify timeline.</summary>
public sealed class AlsMantlingPoseSource
{
    private readonly string[] _names;
    private readonly int[] _parents;
    private readonly AlsLogicalVirtualBone[] _virtuals;
    private readonly AlsPrecisePose[] _target, _source;
    private readonly AlsPrecisePoseRetargetModel _retarget;
    private readonly int[] _modes;
    private readonly AlsPrecisePose _lockedRoot;
    private readonly bool _enableRoot, _forceRoot;
    public AlsRawAnimationPoseData Data { get; }
    public string SkeletonPath { get; }
    public string AnimationInputsDigest { get; }
    public ReadOnlySpan<string> BoneNames => _names;
    public ReadOnlySpan<int> Parents => _parents;
    public ReadOnlySpan<AlsPrecisePose> ReferencePose => _target;

    internal AlsMantlingPoseSource(AlsRawAnimationPoseData data,string skeleton,string[] names,int[] parents,
        AlsLogicalVirtualBone[] virtuals,AlsPrecisePose[] target,AlsPrecisePose[] source,int[] modes,
        AlsPrecisePose lockedRoot,bool enableRoot,bool forceRoot,string digest)
    {
        Data=data;SkeletonPath=skeleton;AnimationInputsDigest=digest;_names=names.ToArray();_parents=parents.ToArray();_virtuals=virtuals.ToArray();
        _target=target.ToArray();_source=target.ToArray();
        if(source.Length<data.PhysicalBoneCount)throw new ArgumentException("Missing physical retarget references.");
        for(var physical=0;physical<data.PhysicalBoneCount;physical++)_source[data.PhysicalToLogical[physical]]=source[physical];
        _lockedRoot=lockedRoot;_enableRoot=enableRoot;_forceRoot=forceRoot;
        _retarget=new(data.LogicalToPhysical,modes,data.LogicalTrackPresence,target,source);
        _modes=modes.ToArray();
        // Validate topology now, rather than deferring malformed resources until playback.
        _=new AlsPreciseRawSequenceSampler(Data,_parents,_target,_virtuals);
    }

    public Sampler CreateSampler()=>new(this);
    public Sampler CreateSampler(AlsMantlingCurveSource curves)=>new(this,curves);

    internal AlsMantlingPoseSource AdaptHostLayout(AlsRawAnimationSkeletonDefinition host,int animationId)
    {
        if(host.PhysicalBoneCount!=Data.PhysicalBoneCount||Data.VirtualTrackPresence.Contains(true))
            throw new ArgumentException("Mantle host adaptation requires the same physical skin and generated virtual tracks.");
        var names=host.LogicalBoneNames.ToArray();var parents=host.LogicalParents.ToArray();
        var sourceByName=_names.Select((name,index)=>(name,index)).ToDictionary(p=>p.name,p=>p.index,StringComparer.OrdinalIgnoreCase);
        var physicalMap=new int[host.PhysicalBoneCount];var target=new AlsPrecisePose[names.Length];var source=new AlsPrecisePose[host.PhysicalBoneCount];
        var modes=new int[host.PhysicalBoneCount];var presence=new bool[names.Length];
        for(var bone=0;bone<names.Length;bone++)target[bone]=AlsMantlingHostPoseProfile.ToNative(host.PreciseReferencePose[bone]);
        for(var physical=0;physical<physicalMap.Length;physical++)
        {
            var logical=host.PhysicalToLogical[physical];
            if(!sourceByName.TryGetValue(names[logical],out var original)||Data.LogicalToPhysical[original]<0)
                throw new ArgumentException("Missing mantle physical bone: "+names[logical]);
            var parent=parents[logical];var originalParent=_parents[original];
            if((parent<0)!=(originalParent<0)||parent>=0&&!names[parent].Equals(_names[originalParent],StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Mantle physical hierarchy differs: "+names[logical]);
            var expected=AlsMantlingHostPoseProfile.ToFbx(_target[original]);var actual=host.PreciseReferencePose[logical];
            if((actual.Position-expected.Position).LengthSquared>4e-8||(actual.Scale-expected.Scale).LengthSquared>4e-8||
                1-System.Math.Abs(AlsQuaternion.Dot(actual.Rotation,expected.Rotation))>1e-5)
                throw new ArgumentException("Mantle physical rest requires explicit retarget: "+names[logical]);
            physicalMap[physical]=Data.LogicalToPhysical[original];presence[logical]=Data.LogicalTrackPresence[original];
            target[logical]=_target[original];source[physical]=_source[original];modes[physical]=_modes[Data.LogicalToPhysical[original]];
        }
        if(host.LogicalToPhysical[0]!=0||!names[0].Equals(_names[0],StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Mantle root lock requires the same root.");
        var keys=new AlsLocalPose[Data.SampledKeyCount*physicalMap.Length];
        for(var key=0;key<Data.SampledKeyCount;key++)
        for(var physical=0;physical<physicalMap.Length;physical++)keys[key*physicalMap.Length+physical]=Data.GetPhysicalKey(key)[physicalMap[physical]];
        var virtuals=host.VirtualBones.ToArray();
        var data=new AlsRawAnimationPoseData(Data.Identity with {AnimationId=animationId,SkeletonId=host.SkeletonId},Data.FrameRateNumerator,
            Data.FrameRateDenominator,Data.SampledKeyCount,Data.PlayLength,Data.Interpolation,host.LogicalToPhysical,
            virtuals.Select(v=>v.Bone).ToArray(),presence,keys,new AlsLocalPose[Data.SampledKeyCount*virtuals.Length]);
        return new(data,host.Source,names,parents,virtuals,target,source,modes,_lockedRoot,_enableRoot,_forceRoot,AnimationInputsDigest);
    }

    /// <summary>One owner per sampler; resources can be shared by independent workers.</summary>
    public sealed class Sampler
    {
        private readonly AlsMantlingPoseSource _owner;
        private readonly AlsPreciseRawSequenceSampler _target, _source;
        private readonly AlsMantlingCurveSource? _curves;
        internal Sampler(AlsMantlingPoseSource owner,AlsMantlingCurveSource? curves=null)
        {
            if(curves is not null&&(curves.SourcePath!=owner.Data.Identity.AssetPath||curves.AnimationInputsDigest!=owner.AnimationInputsDigest))
                throw new ArgumentException("Mantle pose and curve resource versions differ.");
            _curves=curves;
            _owner=owner;
            _target=new(owner.Data,owner._parents,owner._target,owner._virtuals);
            _source=new(owner.Data,owner._parents,owner._source,owner._virtuals);
        }
        public AlsRawPoseKeySelection Sample(double seconds,bool retarget,bool extractRootMotion,bool ignoreRootLock,
            Span<AlsPrecisePose> output)
        {
            var keys=(retarget?_target:_source).Sample(seconds,output);
            _owner._retarget.Apply(output,retarget);
            if(extractRootMotion&&_owner._enableRoot||_owner._forceRoot&&!ignoreRootLock) output[0]=_owner._lockedRoot;
            return keys;
        }
        public AlsRawPoseKeySelection Sample(double seconds,bool retarget,bool extractRootMotion,bool ignoreRootLock,
            Span<AlsPrecisePose> output,Span<AlsInertialCurve> curves)
        {
            if(_curves is null||curves.Length!=_curves.Names.Length)throw new ArgumentException("Unbound mantle curve layout.");
            var keys=Sample(seconds,retarget,extractRootMotion,ignoreRootLock,output);
            _curves.Sample((float)keys.SampleTimeSeconds,curves);
            return keys;
        }
    }
}

/// <summary>Compiles the exported Refactored pose closure without any sampled-pose oracle.
/// Keeps native units for the mantle motion pipeline; scene output needs an explicit bone-space adapter.</summary>
public static class AlsMantlingPoseCompiler
{
    public static IReadOnlyDictionary<string,AlsMantlingPoseSource> Compile(string json,string rootJson)
        =>CompileInternal(json,rootJson,null);
    // Shared raw-key machinery for graph-owned sequences with no mantle motion/montage binding.
    internal static IReadOnlyDictionary<string,AlsMantlingPoseSource> CompileStandaloneSequences(string json,string[] expectedPaths)
        =>CompileInternal(json,null,expectedPaths);
    // Returns absolute raw targets, never additive deltas. The caller validates
    // and evaluates the declared additive base before composing a delta.
    internal static IReadOnlyDictionary<string,AlsMantlingPoseSource> CompileRawAdditiveTargets(string json,string[] expectedPaths)
        =>CompileInternal(json,null,expectedPaths,true);
    private static IReadOnlyDictionary<string,AlsMantlingPoseSource> CompileInternal(string json,string? rootJson,string[]? expectedPaths,bool additiveTargets=false)
    {
        using var document=JsonDocument.Parse(json);using var rootsDocument=rootJson is null?null:JsonDocument.Parse(rootJson);
        var root=document.RootElement;var roots=rootsDocument?.RootElement??default;
        var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Require(root.GetProperty("schemaVersion").GetInt32()==1,"Unsupported mantle pose schema.");
        if(rootJson is not null) Require(Text(root,"rootBindingsSha256").Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rootJson))),
            StringComparison.OrdinalIgnoreCase),"Mantle pose/root source digests differ.");
        var rootSamplers=rootJson is null?null:AlsMantlingRootCompiler.Compile(rootJson);
        var skeletons=root.GetProperty("skeletons").EnumerateObject().ToArray();
        Require(skeletons.Length==1,"Mantle pose closure requires one explicit skeleton.");
        var skeleton=skeletons[0].Value.GetProperty("metadata");var skeletonPath=Text(skeleton,"source");
        Require(skeletons[0].Name==skeletonPath,"Skeleton owner differs from its metadata.");
        var names=Strings(skeleton,"logicalBoneNames");var rawNames=Strings(skeleton,"rawBoneNames");
        var parents=Ints(skeleton,"logicalParents");var rawParents=Ints(skeleton,"rawParents");
        var mapping=Ints(skeleton,"logicalToPhysical");var reference=Poses(skeleton,"referencePose");
        Require(names.Length>0&&names.All(n=>!string.IsNullOrWhiteSpace(n))&&
            names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==names.Length&&parents.Length==names.Length&&
            mapping.Length==names.Length&&reference.Length==names.Length&&rawParents.Length==rawNames.Length,
            "Incomplete mantle skeleton layout.");
        var physicalToLogical=Enumerable.Range(0,names.Length).Where(i=>mapping[i]>=0).OrderBy(i=>mapping[i]).ToArray();
        Require(physicalToLogical.Length==rawNames.Length,"Physical skeleton coverage differs.");
        for(var i=0;i<physicalToLogical.Length;i++)
        {
            var logical=physicalToLogical[i];var parent=parents[logical];
            Require(parent>=-1&&parent<logical&&mapping[logical]==i&&rawNames[i].Equals(names[logical],StringComparison.OrdinalIgnoreCase)&&
                rawParents[i]==(parent<0?-1:mapping[parent])&&(parent<0||mapping[parent]>=0),"Raw skeleton order or parent differs.");
        }
        Require(mapping[0]==0&&parents[0]==-1,"Mantle root must be logical and physical bone zero.");
        var virtuals=skeleton.GetProperty("virtualBones").EnumerateArray().Select(v=>new AlsLogicalVirtualBone(
            v.GetProperty("bone").GetInt32(),v.GetProperty("source").GetInt32(),v.GetProperty("target").GetInt32())).ToArray();
        var modes=Strings(skeleton,"translationRetargetModes").Select(mode=>mode switch
        { "Animation"=>0,"Skeleton"=>1,"AnimationScaled"=>2,"AnimationRelative"=>3,"OrientAndScale"=>4,
            _=>throw new ArgumentException("Unknown native retarget mode.") }).ToArray();
        Require(modes.Length==rawNames.Length,"Incomplete retarget modes.");
        var result=new Dictionary<string,AlsMantlingPoseSource>(StringComparer.Ordinal);
        var bindings=rootJson is null?expectedPaths!.ToDictionary(p=>p,_=>default(JsonElement),StringComparer.Ordinal):
            roots.GetProperty("sequences").EnumerateArray().ToDictionary(r=>Text(r,"source"),StringComparer.Ordinal);
        foreach(var row in root.GetProperty("sequences").EnumerateArray())
        {
            var raw=row.GetProperty("raw");var policy=row.GetProperty("evaluation");var path=Text(raw,"source");
            Require(bindings.TryGetValue(path,out var binding)&&Text(raw,"skeletonSource")==skeletonPath,"Foreign mantle pose source.");
            if(rootJson is not null) foreach(var field in new[]{"skeletonSource","frameRateNumerator","frameRateDenominator","sampledKeyCount","playLength"})
                Require(Same(raw.GetProperty(field),binding.GetProperty(field)),"Mantle pose/root timing or skeleton differs.");
            var count=raw.GetProperty("sampledKeyCount").GetInt32();Require(count>0,"Invalid mantle key count.");
            var presence=new bool[names.Length];var keys=new AlsLocalPose[checked(count*rawNames.Length)];
            var virtualKeys=new AlsLocalPose[checked(count*virtuals.Length)];
            foreach(var track in raw.GetProperty("tracks").EnumerateArray())
            {
                var name=Text(track,"bone");var logical=Array.FindIndex(names,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));
                Require(logical>=0&&!presence[logical],"Unknown or duplicate mantle track.");presence[logical]=true;
                var p=Channel(track,"positions",count,3,false);var q=Channel(track,"rotations",count,4,false);
                var s=Channel(track,"scales",count,3,true);var physical=mapping[logical];
                var vb=Array.FindIndex(virtuals,v=>v.Bone==logical);
                Require(physical>=0||vb>=0,"Unbound virtual track.");
                for(var key=0;key<count;key++)
                {
                    var a=p[p.Length==1?0:key];var b=q[q.Length==1?0:key];
                    var scale=s.Length==0?Vector3.One:Vector(s[s.Length==1?0:key]);
                    var pose=new AlsLocalPose(Vector(a),new(b[0],b[1],b[2],b[3]),scale);
                    if(physical>=0) keys[key*rawNames.Length+physical]=pose;else virtualKeys[key*virtuals.Length+vb]=pose;
                }
            }
            if(rootJson is not null)
            {
                var actualRoot=raw.GetProperty("tracks").EnumerateArray().Where(t=>Text(t,"bone").Equals(names[0],StringComparison.OrdinalIgnoreCase)).ToArray();
                var expectedRoot=binding.GetProperty("tracks");
                Require(actualRoot.Length==expectedRoot.GetArrayLength()&&(actualRoot.Length==0||Same(actualRoot[0],expectedRoot[0])),
                    "Mantle full-pose root channels differ from motion source.");
            }
            var nonAdditive=Text(policy,"additiveType")=="AAT_None"&&Text(policy,"basePoseType")=="ABPT_None"&&
                policy.GetProperty("baseAsset").ValueKind==JsonValueKind.Null&&policy.GetProperty("baseFrame").GetInt32()==0;
            var meshTarget=additiveTargets&&Text(policy,"additiveType")=="AAT_RotationOffsetMeshSpace"&&
                Text(policy,"basePoseType")=="ABPT_AnimFrame"&&policy.GetProperty("baseAsset").ValueKind==JsonValueKind.String&&
                policy.GetProperty("baseFrame").GetInt32()==0&&!policy.GetProperty("enableRootMotion").GetBoolean()&&
                !policy.GetProperty("forceRootLock").GetBoolean();
            Require((nonAdditive||meshTarget)&&
                policy.GetProperty("transformCurveCount").GetInt32()==0&&policy.GetProperty("animatedBoneAttributeCount").GetInt32()==0,
                "Unsupported mantle additive, transform curve or attribute policy.");
            Require(Text(policy,"retargetSource")=="None"&&Text(policy,"retargetTransformsSourceName")=="None"&&
                policy.GetProperty("retargetSourceAsset").ValueKind==JsonValueKind.Null&&
                policy.GetProperty("retargetSourceAssetReferencePose").GetArrayLength()==0&&
                Same(policy.GetProperty("retargetTransforms"),skeleton.GetProperty("referencePose")),
                "Unsupported mantle source reference selection.");
            var interpolation=Text(policy,"interpolation") switch
            { "Linear"=>AlsRawAnimationInterpolation.Linear,"Step"=>AlsRawAnimationInterpolation.Step,_=>throw new ArgumentException("Unknown interpolation.") };
            Require(policy.GetProperty("sequencePlayLength").GetDouble()==(double)(float)raw.GetProperty("playLength").GetDouble(),
                "Sequence duration differs from its raw keys.");
            var data=new AlsRawAnimationPoseData(new(result.Count,Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant(),path,0),
                raw.GetProperty("frameRateNumerator").GetInt32(),raw.GetProperty("frameRateDenominator").GetInt32(),count,
                raw.GetProperty("playLength").GetDouble(),interpolation,mapping,virtuals.Select(v=>v.Bone).ToArray(),presence,keys,virtualKeys);
            var locked=Text(policy,"rootMotionRootLock") switch
            { "RefPose"=>reference[0],"AnimFirstFrame"=>Pose(policy.GetProperty("rootLockFirstFrame")),
                "Zero"=>AlsPrecisePose.Identity,_=>throw new ArgumentException("Unknown root lock.") };
            Require(result.TryAdd(path,new(data,skeletonPath,names,parents,virtuals,reference,Poses(policy,"retargetTransforms"),modes,
                locked,policy.GetProperty("enableRootMotion").GetBoolean(),policy.GetProperty("forceRootLock").GetBoolean(),digest)),"Duplicate mantle pose source.");
        }
        Require(result.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(bindings.Keys),"Incomplete mantle pose closure.");
        if(rootJson is null)return new ReadOnlyDictionary<string,AlsMantlingPoseSource>(result);
        var montagePaths=new HashSet<string>(StringComparer.Ordinal);
        var rootMontages=roots.GetProperty("montages").EnumerateArray().ToDictionary(r=>Text(r,"path"),StringComparer.Ordinal);
        foreach(var montage in root.GetProperty("montages").EnumerateArray())
        {
            var path=Text(montage,"path");Require(montagePaths.Add(path)&&rootMontages.TryGetValue(path,out _),"Foreign or duplicate mantle montage.");
            Require(Text(montage,"skeleton")==skeletonPath&&Same(montage.GetProperty("segments"),rootMontages[path].GetProperty("segments")),
                "Mantle pose/motion montage binding differs.");
        }
        Require(montagePaths.SetEquals(rootSamplers!.Keys),"Incomplete mantle montage closure.");
        return new ReadOnlyDictionary<string,AlsMantlingPoseSource>(result);
    }

    private static bool Same(JsonElement a,JsonElement b)=>JsonNode.DeepEquals(JsonNode.Parse(a.GetRawText()),JsonNode.Parse(b.GetRawText()));
    private static string Text(JsonElement row,string field)=>row.GetProperty(field).GetString()??throw new ArgumentException(field);
    private static string[] Strings(JsonElement row,string field)=>row.GetProperty(field).EnumerateArray().Select(v=>v.GetString()??throw new ArgumentException(field)).ToArray();
    private static int[] Ints(JsonElement row,string field)=>row.GetProperty(field).EnumerateArray().Select(v=>v.GetInt32()).ToArray();
    private static AlsPrecisePose[] Poses(JsonElement row,string field)=>row.GetProperty(field).EnumerateArray().Select(Pose).ToArray();
    private static AlsPrecisePose Pose(JsonElement row)
    {
        double[] V(string field,int count) {var v=row.GetProperty(field).EnumerateArray().Select(x=>x.GetDouble()).ToArray();Require(v.Length==count,"Invalid pose components.");return v;}
        var p=V("position",3);var q=V("rotation",4);var s=V("scale",3);
        var pose=new AlsPrecisePose(new(p[0],p[1],p[2]),new(q[0],q[1],q[2],q[3]),new(s[0],s[1],s[2]));pose.Validate();return pose;
    }
    private static float[][] Channel(JsonElement row,string field,int count,int width,bool empty)
    {
        var v=row.GetProperty(field).EnumerateArray().Select(a=>a.EnumerateArray().Select(x=>x.GetSingle()).ToArray()).ToArray();
        Require((v.Length==1||v.Length==count||empty&&v.Length==0)&&v.All(a=>a.Length==width&&a.All(float.IsFinite)),"Invalid raw channel.");return v;
    }
    private static Vector3 Vector(float[] v)=>new(v[0],v[1],v[2]);
    private static void Require(bool condition,string message) {if(!condition) throw new ArgumentException(message);}
}
