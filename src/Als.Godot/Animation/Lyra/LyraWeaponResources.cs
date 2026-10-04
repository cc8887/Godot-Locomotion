using System.Text.Json;
using GodotAls.Core.Locomotion;
using NVector=System.Numerics.Vector3;
using NQuaternion=System.Numerics.Quaternion;

namespace GodotAls.Animation.Lyra;

// Weapon bones have their own address space. These source keys never enter
// the ALS81 character bank or replace the character's skin skeleton.
internal sealed class LyraWeaponResources
{
    internal const string Root="res://assets/generated/lyra_als/weapon_resources/";
    public JsonElement Catalog {get;}
    public string Sha256 {get;}
    public LyraWeaponResources()
    {
        var bytes=Godot.FileAccess.GetFileAsBytes(Root+"catalog.json");Sha256=LyraLogicalSourceBank.Sha(bytes);
        using var doc=JsonDocument.Parse(bytes);Catalog=doc.RootElement.Clone();
        if(Catalog.GetProperty("schemaVersion").GetInt32()!=1||Catalog.GetProperty("montages").GetArrayLength()!=6||
            Catalog.GetProperty("meshes").GetArrayLength()!=3)throw new InvalidOperationException("Incomplete independent weapon resources.");
        foreach(var dep in Catalog.GetProperty("dependencies").EnumerateObject())
            if(dep.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+dep.Name)))
                throw new InvalidOperationException("Stale weapon resource dependency.");
        foreach(var mesh in Catalog.GetProperty("meshes").EnumerateArray())
            if(mesh.GetProperty("sha256").GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+mesh.GetProperty("file").GetString())))
                throw new InvalidOperationException("Changed original weapon FBX.");
        using var textures=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(Root+"textures.json"));
        if(textures.RootElement.GetProperty("schemaVersion").GetInt32()!=1||
            textures.RootElement.GetProperty("dependencies").GetProperty("weapon_resources/catalog.json").GetString()!=Sha256)
            throw new InvalidOperationException("Stale original weapon textures.");
        foreach(var texture in textures.RootElement.GetProperty("entries").EnumerateArray())
            if(texture.GetProperty("sha256").GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(Root+texture.GetProperty("file").GetString())))
                throw new InvalidOperationException("Changed original weapon texture.");
    }
    public JsonElement Mesh(string kind)=>Catalog.GetProperty("meshes").EnumerateArray().Single(r=>r.GetProperty("kind").GetString()==kind);
    public LyraWeaponSequenceSampler CreateSampler(string path,ReadOnlySpan<AlsPrecisePose> reference=default)
    {
        var entry=Catalog.GetProperty("sequences").GetProperty(path);
        var bytes=Godot.FileAccess.GetFileAsBytes(Root+entry.GetProperty("file").GetString());
        if(LyraLogicalSourceBank.Sha(bytes)!=entry.GetProperty("sha256").GetString())throw new InvalidOperationException("Changed original weapon sequence.");
        using var doc=JsonDocument.Parse(bytes);return new(doc.RootElement.Clone(),reference);
    }
}

internal sealed class LyraWeaponSequenceSampler
{
    private readonly AlsPreciseRawSequenceSampler _sampler;
    private readonly AlsPrecisePose _rootLock;
    private readonly bool _forceRootLock;
    public JsonElement Resource {get;}
    public string[] Names {get;}
    public int[] Parents {get;}
    public AlsPrecisePose[] Reference {get;}
    public double Length {get;}
    public LyraWeaponSequenceSampler(JsonElement resource,ReadOnlySpan<AlsPrecisePose> reference=default)
    {
        Resource=resource;var raw=resource.GetProperty("raw");var skeleton=resource.GetProperty("skeleton");var metadata=resource.GetProperty("metadata");
        Names=skeleton.GetProperty("logicalBoneNames").EnumerateArray().Select(p=>p.GetString()!).ToArray();
        Parents=skeleton.GetProperty("logicalParents").EnumerateArray().Select(p=>p.GetInt32()).ToArray();
        Reference=skeleton.GetProperty("referencePose").EnumerateArray().Select(LyraLogicalSourceBank.ParsePose).ToArray();
        // Native RefPose and absent raw channels use the actual mesh reference,
        // which differs from USkeleton's reference for these original weapons.
        if(!reference.IsEmpty)
        {
            if(reference.Length!=Names.Length)throw new ArgumentException("Invalid weapon mesh reference layout.");
            foreach(var pose in reference)pose.Validate();Reference=reference.ToArray();
        }
        int count=Names.Length,keys=raw.GetProperty("sampledKeyCount").GetInt32();Length=raw.GetProperty("playLength").GetDouble();
        if(count is not(7 or 8)||skeleton.GetProperty("virtualBones").GetArrayLength()!=0||
            !skeleton.GetProperty("logicalToPhysical").EnumerateArray().Select(p=>p.GetInt32()).SequenceEqual(Enumerable.Range(0,count))||
            skeleton.GetProperty("translationRetargetModes").EnumerateArray().Any(p=>p.GetString()!="Animation")||
            metadata.GetProperty("additiveType").GetString()!="AAT_None"||metadata.GetProperty("interpolation").GetString()!="Linear")
            throw new NotSupportedException("Changed original weapon skeleton/sampling policy.");
        var presence=new bool[count];var physical=new AlsLocalPose[count*keys];
        foreach(var track in raw.GetProperty("tracks").EnumerateArray())
        {
            int bone=Array.IndexOf(Names,track.GetProperty("bone").GetString());
            if(bone<0||presence[bone])throw new InvalidOperationException("Foreign or repeated weapon track.");presence[bone]=true;
            var p=track.GetProperty("positions");var q=track.GetProperty("rotations");var s=track.GetProperty("scales");
            if(p.GetArrayLength() is not 1&&p.GetArrayLength()!=keys||q.GetArrayLength() is not 1&&q.GetArrayLength()!=keys||
                s.GetArrayLength()!=0&&s.GetArrayLength()!=1&&s.GetArrayLength()!=keys)throw new InvalidOperationException("Unsupported weapon channel length.");
            for(int k=0;k<keys;k++)
            {
                var a=p[p.GetArrayLength()==1?0:k];var b=q[q.GetArrayLength()==1?0:k];
                physical[k*count+bone]=new(new NVector(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle()),
                    new NQuaternion(b[0].GetSingle(),b[1].GetSingle(),b[2].GetSingle(),b[3].GetSingle()),
                    s.GetArrayLength()==0?NVector.One:Vector(s[s.GetArrayLength()==1?0:k]));
            }
        }
        if(!presence.Any(p=>p))throw new InvalidOperationException("Empty original weapon DataModel.");
        var data=new AlsRawAnimationPoseData(new(0,resource.GetProperty("source").GetString()!,resource.GetProperty("source").GetString()!,0),
            raw.GetProperty("frameRateNumerator").GetInt32(),raw.GetProperty("frameRateDenominator").GetInt32(),keys,Length,
            AlsRawAnimationInterpolation.Linear,Enumerable.Range(0,count).ToArray(),[],presence,physical,[]);
        _sampler=new(data,Parents,Reference,[],AlsRawFrameTimeRounding.RoundSubframe);
        _forceRootLock=metadata.GetProperty("forceRootLock").GetBoolean();
        _rootLock=metadata.GetProperty("rootMotionRootLock").GetString() switch
        {"RefPose"=>Reference[0],"AnimFirstFrame"=>LyraLogicalSourceBank.ParsePose(metadata.GetProperty("rootLockFirstFrame")),"Zero"=>AlsPrecisePose.Identity,_=>throw new NotSupportedException("Unknown weapon root lock.")};
    }
    private static NVector Vector(JsonElement value)=>new(value[0].GetSingle(),value[1].GetSingle(),value[2].GetSingle());
    public void Sample(double seconds,Span<AlsPrecisePose> output)
    {_sampler.Sample(seconds,output);if(_forceRootLock)output[0]=_rootLock;}
}
