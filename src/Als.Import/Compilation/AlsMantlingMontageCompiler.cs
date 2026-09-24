using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsMantlingMontageDefinition(string Path,string SequencePath,AlsAuthoredMontageAsset Asset);

/// <summary>A Refactored resource scope. Its dense IDs are local to this profile and must
/// not be inserted into the V4 bank without explicit identity/group remapping.</summary>
public sealed class AlsMantlingMontageProfile
{
    public IReadOnlyDictionary<string,AlsMantlingMontageDefinition> Definitions { get; }
    public IReadOnlyDictionary<string,AlsMantlingPoseSource> Poses { get; }
    public IReadOnlyDictionary<string,AlsMantlingCurveSource> Curves { get; }
    public string GroupName { get; }
    internal AlsMantlingMontageProfile(Dictionary<string,AlsMantlingMontageDefinition> definitions,
        IReadOnlyDictionary<string,AlsMantlingPoseSource> poses,IReadOnlyDictionary<string,AlsMantlingCurveSource> curves,string group)
    {Definitions=new ReadOnlyDictionary<string,AlsMantlingMontageDefinition>(definitions);Poses=poses;Curves=curves;GroupName=group;}
    public AlsMontageRuntime CreateRuntime()=>new([],Definitions.Values.Select(d=>d.Asset).ToArray());
    public AlsMontageRuntime CreateRuntime(AlsMantlingBranchingRuntime branching)=>new([],Definitions.Values.Select(d=>d.Asset).ToArray(),branching:branching);
    public IAlsMontagePoseSource CreatePoseSource()=>new PoseSource(this);
    private sealed class PoseSource : IAlsMontagePoseSource
    {
        private readonly Dictionary<int,AlsMantlingPoseSource.Sampler> _sources;
        public PoseSource(AlsMantlingMontageProfile profile)=>_sources=profile.Definitions.Values
            .GroupBy(d=>d.Asset.AnimationId).ToDictionary(g=>g.Key,g=>
            {
                var path=g.Select(d=>d.SequencePath).Distinct(StringComparer.Ordinal).Single();
                return profile.Poses[path].CreateSampler(profile.Curves[path]);
            });
        public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {
            if(entry.Slot!=AlsMontageSlot.PostLocomotion||entry.AdditiveType!=0||!_sources.TryGetValue(entry.AnimationId,out var source))
                throw new ArgumentException("Foreign mantle slot source.");
            source.Sample(entry.Position,true,false,false,pose,curves);
        }
    }
}

/// <summary>Binds real one-section/one-segment mantle montages to the shared physical
/// montage clock and slot evaluator. Branching-point callbacks are deliberately not queued here.</summary>
public static class AlsMantlingMontageCompiler
{
    public static AlsMantlingMontageProfile Compile(string animationJson,string rootJson,string curveJson)
    {
        var poses=AlsMantlingPoseCompiler.Compile(animationJson,rootJson);
        var curves=AlsMantlingCurveCompiler.Compile(curveJson,animationJson);
        using var document=JsonDocument.Parse(animationJson);var root=document.RootElement;
        var skeleton=root.GetProperty("skeletons").EnumerateObject().Single().Value;
        var groups=Regex.Matches(Text(skeleton,"nativeText"),@"(?m)^   SlotGroups\((\d+)\)=\(([^\r\n]+)\)")
            .Where(m=>Regex.IsMatch(m.Groups[2].Value,"SlotNames=\\([^)]*\"PostLocomotion\"" )).ToArray();
        Require(groups.Length==1,"PostLocomotion must belong to exactly one native slot group.");
        var group=int.Parse(groups[0].Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
        var groupName=Regex.Match(groups[0].Groups[2].Value,"GroupName=\"([^\"]+)\"").Groups[1].Value;
        Require(groupName.Length>0,"Missing mantle slot group identity.");
        var result=new Dictionary<string,AlsMantlingMontageDefinition>(StringComparer.Ordinal);
        var curveLayout=curves.Values.First().Names.ToArray();
        Require(curves.Values.All(c=>c.Names.SequenceEqual(curveLayout)),"Mantle sources require a common explicit curve layout.");
        foreach(var row in root.GetProperty("montages").EnumerateArray().OrderBy(m=>Text(m,"path"),StringComparer.Ordinal))
        {
            var path=Text(row,"path");var text=Text(row,"nativeText");
            Require(Text(row,"slot")=="PostLocomotion","Mantle montage is bound to another slot.");
            var sections=Regex.Matches(text,@"(?m)^   CompositeSections\((\d+)\)=([^\r\n]+)");
            Require(sections.Count==1&&sections[0].Groups[1].Value=="0"&&
                sections[0].Groups[2].Value.Contains("SectionName=\"Default\"",StringComparison.Ordinal)&&
                !Regex.IsMatch(sections[0].Value,@"(?:NextSectionName|LinkValue|StartTime)="),
                "Mantle requires one default terminal section at zero.");
            Require(!Regex.IsMatch(text,@"(?m)^   (?:BlendModeIn|BlendModeOut|BlendProfileIn|BlendProfileOut)="),
                "Custom montage blend modes/profiles need an explicit runtime binding.");
            var segments=row.GetProperty("segments");Require(segments.GetArrayLength()==1,"Unsupported mantle segment count.");
            var segment=segments[0];var sequence=Text(segment,"sequence");var source=poses[sequence];
            var length=Number(row,"length");var start=Number(segment,"animationStart");var end=Number(segment,"animationEnd");
            var rate=Number(segment,"segmentRate");var scale=Number(row,"rateScale");
            Require(Number(segment,"trackStart")==0&&segment.GetProperty("loopCount").GetInt32()==1&&start>=0&&end>start&&
                end<=(float)source.Data.PlayLength&&rate>0&&scale>0&&MathF.Abs((end-start)/rate-length)<=1e-6f,
                "Unsupported mantle segment timeline.");
            var blendIn=row.GetProperty("blendIn");var blendOut=row.GetProperty("blendOut");
            Require(blendIn.GetProperty("customCurve").ValueKind==JsonValueKind.Null&&blendOut.GetProperty("customCurve").ValueKind==JsonValueKind.Null,
                "Custom mantle blend curves are unbound.");
            var lifecycle=new AlsActionLifecycleSettings(row.GetProperty("autoBlendOut").GetBoolean()?AlsActionLifecycleMode.MontageAutoBlendOut:
                AlsActionLifecycleMode.MontageHoldAtEnd,Number(blendIn,"time"),Blend(Text(blendIn,"option")),
                Number(blendOut,"time"),Blend(Text(blendOut,"option")),Number(row,"blendOutTriggerTime"));
            var sequencePolicy=root.GetProperty("sequences").EnumerateArray().Single(s=>Text(s.GetProperty("raw"),"source")==sequence).GetProperty("evaluation");
            var asset=new AlsAuthoredMontageAsset(result.Count,source.Data.Identity.AnimationId,AlsMontageSlot.PostLocomotion,group,
                length,start,rate,lifecycle,sequencePolicy.GetProperty("enableRootMotion").GetBoolean(),result.Count){RateScale=scale};
            Require(result.TryAdd(path,new(path,sequence,asset)),"Duplicate mantle montage.");
        }
        var profile=new AlsMantlingMontageProfile(result,poses,curves,groupName);_=profile.CreateRuntime();return profile;
    }
    private static AlsActionBlendOption Blend(string name)=>name switch
    {"LINEAR"=>AlsActionBlendOption.Linear,"CUBIC"=>AlsActionBlendOption.Cubic,"HERMITE_CUBIC"=>AlsActionBlendOption.HermiteCubic,
        _=>throw new ArgumentException("Unsupported mantle blend option.")};
    private static string Text(JsonElement row,string field)=>row.GetProperty(field).GetString()??throw new ArgumentException(field);
    private static float Number(JsonElement row,string field){var v=row.GetProperty(field).GetSingle();Require(float.IsFinite(v),"Nonfinite mantle montage value.");return v;}
    private static void Require(bool condition,string message){if(!condition)throw new ArgumentException(message);}
}
