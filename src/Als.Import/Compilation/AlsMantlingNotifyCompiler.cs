using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Import.Compilation;

public sealed record AlsMantlingFootstep(string ObjectPath,string SettingsPath,AlsTimelineFoot Foot,bool SpawnDecal,bool SpawnParticles);
public sealed record AlsMantlingNotifyProfile(AlsMontageNotifyBinding Binding,IReadOnlyList<AlsMantlingFootstep> Footsteps);

/// <summary>Refactored-local queue identity scope. Audio and effect spawning are not dispatched here.
/// Native branching states are bound separately and never enter this queued timeline.</summary>
public static class AlsMantlingNotifyCompiler
{
    public static AlsMantlingNotifyProfile Compile(string json,AlsMantlingMontageProfile profile)
    {
        var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Require(profile.Poses.Values.All(p=>p.AnimationInputsDigest==digest),"Foreign mantle notification resource version.");
        using var doc=JsonDocument.Parse(json);
        var sequences=doc.RootElement.GetProperty("sequences").EnumerateArray().ToDictionary(s=>Text(s.GetProperty("raw"),"source"),StringComparer.Ordinal);
        Require(sequences.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(profile.Poses.Keys),"Foreign mantle notify inventory.");
        var objects=new List<AlsMantlingFootstep>();var objectIds=new Dictionary<string,int>(StringComparer.Ordinal);
        var names=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var policies=new List<AlsAssetNotifyPolicy>();var definitions=new List<AlsAssetNotifyDefinition>();
        var timeline=new List<AlsTimelineEventDefinition>();var ranges=new List<AlsMontageNotifyRange>();
        foreach(var montage in profile.Definitions.Values.OrderBy(d=>d.Asset.ActionDefinitionId))
        {
            var asset=montage.Asset;var source=sequences[montage.SequencePath];var events=source.GetProperty("notifies");
            var offset=policies.Count;var handle=ranges.Count;
            for(var index=0;index<events.GetArrayLength();index++)
            {
                var e=events[index];var path=Text(e,"notifyObject");
                Require(Text(e,"class")=="/Script/ALS.AlsAnimNotify_FootstepEffects"&&Text(e,"stateObject")==""&&
                    path.StartsWith(montage.SequencePath+":",StringComparison.Ordinal)&&e.GetProperty("index").GetInt32()==index&&
                    e.GetProperty("tickMode").GetInt32()==0&&e.GetProperty("stateBehaviorFlags").GetInt32()==0&&Number(e,"duration")==0,
                    "Unsupported mantle queued event class, identity or state.");
                if(!objectIds.TryGetValue(path,out var id))
                {
                    id=objects.Count;objectIds.Add(path,id);
                    // T3D omits class defaults: the native class declares Left and both spawn flags true.
                    var blocks=Regex.Matches(Text(source,"nativeText"),@"(?m)^   Begin Object Name=""[^""]+"" ExportPath=""/Script/ALS\.AlsAnimNotify_FootstepEffects'"+
                        Regex.Escape(path)+@"'""\r?\r?\n(?<body>[\s\S]*?)^   End Object");
                    Require(blocks.Count==1,"Missing unique footstep object payload.");var body=blocks[0].Groups["body"].Value;
                    var foot=Regex.Match(body,@"(?m)^      FootBone=(\w+)").Groups[1].Value;
                    Require(foot is "" or "Left" or "Right","Unsupported foot bone.");
                    var settings=Regex.Match(body,@"FootstepEffectsSettings=""/Script/ALS.AlsFootstepEffectsSettings'([^']+)'""");
                    Require(settings.Success,"Missing footstep effects settings reference.");
                    bool Flag(string name)
                    {
                        var match=Regex.Match(body,@"(?m)^      "+name+@"=(\w+)");
                        Require(!match.Success||match.Groups[1].Value is "True" or "False","Invalid native footstep flag.");
                        return !match.Success||match.Groups[1].Value=="True";
                    }
                    objects.Add(new(path,settings.Groups[1].Value,foot=="Right"?AlsTimelineFoot.Right:AlsTimelineFoot.Left,Flag("bSpawnDecal"),Flag("bSpawnParticleSystem")));
                }
                var name=Text(e,"name");if(!names.TryGetValue(name,out var nameId)){nameId=names.Count;names.Add(name,nameId);}
                var time=Number(e,"time");var trigger=Number(e,"triggerTime");var end=Number(e,"endTriggerTime");
                Require(trigger==time+Number(e,"triggerOffset")&&end==trigger+Number(e,"endTriggerOffset"),"Invalid native notify offsets.");
                var filter=e.GetProperty("filterType").GetInt32();Require(filter is 0 or 1,"Unsupported notify filter.");
                var threshold=Number(e,"weightThreshold");var chance=Number(e,"chance");
                Require(threshold>=0&&threshold<=1&&chance>=0&&chance<=1,"Invalid notify filter scalar.");
                var track=e.GetProperty("track").GetInt32();var lod=e.GetProperty("filterLod").GetInt32();Require(track>=0&&lod>=0,"Invalid notify filter index.");
                policies.Add(new(id,index,track,id,-1,nameId,threshold,chance,(AlsAssetNotifyFilterType)filter,lod,AlsTimelineTickMode.Queued,
                    e.GetProperty("filterViaRequest").GetBoolean(),e.GetProperty("onDedicatedServer").GetBoolean(),e.GetProperty("onFollower").GetBoolean()));
                definitions.Add(new(id,trigger,end));
                timeline.Add(new(id,asset.AnimationId,asset.ActionDefinitionId,handle,AlsTimelineSourceKind.MontageSegmentAnimation,index,track,0,
                    time,0,threshold,AlsTimelineEventKind.Footstep,AlsTimelineTickMode.Queued,new(0,(int)objects[id].Foot,0,0,0,0,AlsActionResultCode.None)));
            }
            ranges.Add(new(asset.ActionDefinitionId,asset.AnimationId,asset.Slot,handle,offset,events.GetArrayLength(),
                (float)profile.Poses[montage.SequencePath].Data.PlayLength,asset.ClipStart,asset.ClipRate,false));
        }
        return new(new([],policies.ToArray(),definitions.ToArray(),timeline.ToArray(),ranges.ToArray()),objects.AsReadOnly());
    }
    private static string Text(JsonElement row,string key)=>row.GetProperty(key).GetString()??throw new ArgumentException(key);
    private static float Number(JsonElement row,string key){var value=row.GetProperty(key).GetSingle();Require(float.IsFinite(value),"Nonfinite notify value.");return value;}
    private static void Require(bool condition,string message){if(!condition)throw new ArgumentException(message);}
}
