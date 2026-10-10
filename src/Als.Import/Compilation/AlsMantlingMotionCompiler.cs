using System.Collections.ObjectModel;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsMantlingMotionDefinition(AlsMantlingSettingsDefinition Settings,
    AlsMontageRootTransformSampler Roots, float BlendInTime, AlsActionBlendOption BlendInOption)
{
    public AlsMantlingProgressCurve? Progress { get; init; }
    public float? ContactApproachStartTime { get; init; }
    public AlsMantlingWarpSettings CreateWarpSettings(float height,double samplingFramesPerSecond,bool useRootMotion=true)
    {
        var start=useRootMotion ? Settings.CalculateStartTime(height,samplingFramesPerSecond,t=>Roots.Sample(t).Position.Z)
            : AlsMantlingStartTime.MapHeight(height,Settings.ReferenceLow,Settings.ReferenceHigh,Settings.TimeLow,Settings.TimeHigh);
        return new((Settings.MontageLength-start)/Settings.RateScale,start,Settings.RateScale,
            BlendInTime,BlendInOption,Settings.WarpStart,Settings.WarpEnd,Settings.LocationBlend,Settings.RotationBlend);
    }
    public AlsMantlingWarpSettings CreateProgressSettings(float height)
    {
        var start=(Progress ?? throw new InvalidOperationException("Missing mantle progress curve.")).MatchStartTime(height);
        var liftStart=start;
        // Height matching may select an already planted pose. Keep enough
        // approach for the ordinary montage blend before either hand supports
        // the body; both motion and contact still use the same montage clock.
        if(ContactApproachStartTime is {} approach)
            start=MathF.Min(start,MathF.Max(0,approach-BlendInTime*Settings.RateScale));
        return new((Settings.MontageLength-start)/Settings.RateScale,start,Settings.RateScale,
            BlendInTime,BlendInOption,Settings.WarpStart,Settings.WarpEnd,Settings.LocationBlend,Settings.RotationBlend)
            {
                ProgressLiftStartTime=liftStart,
                ProgressTargetHeight=ContactApproachStartTime.HasValue ? height : null,
                ProgressPreparationEndTime=ContactApproachStartTime is {} ready
                    ? MathF.Min(ready, MathF.Min(Settings.MontageLength,start+BlendInTime*Settings.RateScale)) : null
            };
    }
}

public sealed class AlsMantlingMotionProfile
{
    private readonly AlsMantlingSettingsProfile _selector;
    public IReadOnlyDictionary<string,AlsMantlingMotionDefinition> Definitions { get; }
    internal AlsMantlingMotionProfile(AlsMantlingSettingsProfile selector,Dictionary<string,AlsMantlingMotionDefinition> definitions)
    { _selector=selector;Definitions=new ReadOnlyDictionary<string,AlsMantlingMotionDefinition>(definitions); }
    public AlsMantlingMotionDefinition Select(AlsMantlingType type,string overlay)=>Definitions[_selector.Select(type,overlay).Path];
    public AlsMantlingMotionProfile WithContactProgress(IReadOnlyDictionary<string,AlsMantlingContactDefinition> contacts,
        IReadOnlyDictionary<string,AlsMantlingMontageDefinition> montages)
    {
        var definitions=new Dictionary<string,AlsMantlingMotionDefinition>(StringComparer.Ordinal);
        foreach(var pair in Definitions)
        {
            var montage=montages[pair.Value.Settings.Montage];
            if(montage.Asset.ClipStart!=0||montage.Asset.ClipRate!=1)
                throw new ArgumentException("Contact controls require an explicit remapped segment timeline.");
            var contact=contacts[montage.SequencePath];
            definitions.Add(pair.Key,pair.Value with {Progress=contact.Progress,
                ContactApproachStartTime=contact.Contacts.ApproachStartTime});
        }
        return new(_selector,definitions);
    }
}

/// <summary>Binds authored selector/settings, original root tracks and reflected montage blends.
/// Native motion trajectories are not inputs to this compiler.</summary>
public static class AlsMantlingMotionCompiler
{
    public static AlsMantlingMotionProfile Compile(string settingsJson,string rootJson,string blendJson,bool compileProgress=false)
    {
        var settings=AlsMantlingSettingsCompiler.Compile(settingsJson);
        var roots=AlsMantlingRootCompiler.Compile(rootJson);
        using var trajectoryDocument=JsonDocument.Parse(rootJson);
        var trajectorySources=trajectoryDocument.RootElement.GetProperty("sequences").EnumerateArray()
            .ToDictionary(row=>row.GetProperty("source").GetString()!,StringComparer.Ordinal);
        var trajectoryMontages=trajectoryDocument.RootElement.GetProperty("montages").EnumerateArray()
            .ToDictionary(row=>row.GetProperty("path").GetString()!,StringComparer.Ordinal);
        using var document=JsonDocument.Parse(blendJson);
        if(document.RootElement.GetProperty("schemaVersion").GetInt32()!=1) throw new ArgumentException("Unsupported mantle blend schema.");
        var blends=new Dictionary<string,(float Time,AlsActionBlendOption Option)>(StringComparer.Ordinal);
        foreach(var row in document.RootElement.GetProperty("montages").EnumerateArray())
        {
            var time=row.GetProperty("time").GetSingle();
            var option=row.GetProperty("option").GetString() switch
            {
                "LINEAR"=>AlsActionBlendOption.Linear,"CUBIC"=>AlsActionBlendOption.Cubic,
                "HERMITE_CUBIC"=>AlsActionBlendOption.HermiteCubic,
                _=>throw new ArgumentException("Unsupported mantle blend option.")
            };
            var path=row.GetProperty("path").GetString();
            if(!float.IsFinite(time)||time<0||string.IsNullOrWhiteSpace(path)||!blends.TryAdd(path,(time,option)))
                throw new ArgumentException("Invalid or duplicate mantle blend.");
        }
        var used=settings.Settings.Values.Select(s=>s.Montage).ToHashSet(StringComparer.Ordinal);
        if(!used.SetEquals(blends.Keys)||!used.SetEquals(roots.Keys)) throw new ArgumentException("Incomplete or unrelated mantle motion closure.");
        var definitions=new Dictionary<string,AlsMantlingMotionDefinition>(StringComparer.Ordinal);
        foreach(var setting in settings.Settings.Values)
        {
            var blend=blends[setting.Montage];
            var sampler=roots[setting.Montage];
            if(!compileProgress)
            {
                definitions.Add(setting.Path,new(setting,sampler,blend.Time,blend.Option));
                continue;
            }
            // Compile independent target-progress buffers once, before action playback.
            // Preserve the native sampler path for root-enabled/native comparisons.
            var times=ProgressTimes(trajectoryMontages[setting.Montage],trajectorySources,setting.MontageLength);
            var count=times.Length;var planar=new float[count];var lift=new float[count];
            var first=sampler.Sample(0).Position;var end=sampler.Sample(setting.MontageLength).Position;
            var horizontal=end-first;horizontal=horizontal with {Z=0};
            var distance=Math.Sqrt(horizontal.LengthSquared);
            var direction=distance>1e-6 ? horizontal*(1/distance) : default;
            for(var i=0;i<count;i++)
            {
                var point=sampler.Sample(times[i]).Position-first;
                planar[i]=(float)AlsDoubleVector.Dot(point,direction);lift[i]=(float)point.Z;
            }
            definitions.Add(setting.Path,new(setting,sampler,blend.Time,blend.Option)
                {Progress=new(times,planar,lift)});
        }
        return new(settings,definitions);
    }

    // Preserve source key times and segment boundaries, including trimming and
    // rate conversion. A fixed resampling FPS can miss lift/contact transitions.
    private static float[] ProgressTimes(JsonElement montage,Dictionary<string,JsonElement> sources,float length)
    {
        var result=new SortedSet<float>{0,length};
        foreach(var segment in montage.GetProperty("segments").EnumerateArray())
        {
            var source=sources[segment.GetProperty("sequence").GetString()!];
            var fps=(double)source.GetProperty("frameRateNumerator").GetInt32()/source.GetProperty("frameRateDenominator").GetInt32();
            var start=segment.GetProperty("animationStart").GetDouble();var end=segment.GetProperty("animationEnd").GetDouble();
            var track=segment.GetProperty("trackStart").GetDouble();
            var rate=segment.GetProperty("segmentRate").GetDouble()*source.GetProperty("rateScale").GetDouble();
            if(Math.Abs(rate)<=1e-8)rate=1;
            for(var loop=0;loop<segment.GetProperty("loopCount").GetInt32();loop++)
            {
                var offset=track+loop*(end-start)/Math.Abs(rate);
                Add(offset);Add(offset+(end-start)/Math.Abs(rate));
                for(var key=0;key<source.GetProperty("sampledKeyCount").GetInt32();key++)
                {
                    var time=key/fps;if(time<start||time>end)continue;
                    Add(offset+(rate>=0?time-start:end-time)/Math.Abs(rate));
                }
            }
        }
        return result.ToArray();
        void Add(double value){if(double.IsFinite(value)&&value>=0&&value<=length)result.Add((float)value);}
    }
}
