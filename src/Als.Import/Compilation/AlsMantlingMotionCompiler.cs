using System.Collections.ObjectModel;
using System.Text.Json;
using GodotAls.Core.Actions;

namespace GodotAls.Import.Compilation;

public sealed record AlsMantlingMotionDefinition(AlsMantlingSettingsDefinition Settings,
    AlsMontageRootTransformSampler Roots, float BlendInTime, AlsActionBlendOption BlendInOption)
{
    public AlsMantlingWarpSettings CreateWarpSettings(float height,double samplingFramesPerSecond)
    {
        var start=Settings.CalculateStartTime(height,samplingFramesPerSecond,t=>Roots.Sample(t).Position.Z);
        return new((Settings.MontageLength-start)/Settings.RateScale,start,Settings.RateScale,
            BlendInTime,BlendInOption,Settings.WarpStart,Settings.WarpEnd,Settings.LocationBlend,Settings.RotationBlend);
    }
}

public sealed class AlsMantlingMotionProfile
{
    private readonly AlsMantlingSettingsProfile _selector;
    public IReadOnlyDictionary<string,AlsMantlingMotionDefinition> Definitions { get; }
    internal AlsMantlingMotionProfile(AlsMantlingSettingsProfile selector,Dictionary<string,AlsMantlingMotionDefinition> definitions)
    { _selector=selector;Definitions=new ReadOnlyDictionary<string,AlsMantlingMotionDefinition>(definitions); }
    public AlsMantlingMotionDefinition Select(AlsMantlingType type,string overlay)=>Definitions[_selector.Select(type,overlay).Path];
}

/// <summary>Binds authored selector/settings, original root tracks and reflected montage blends.
/// Native motion trajectories are not inputs to this compiler.</summary>
public static class AlsMantlingMotionCompiler
{
    public static AlsMantlingMotionProfile Compile(string settingsJson,string rootJson,string blendJson)
    {
        var settings=AlsMantlingSettingsCompiler.Compile(settingsJson);
        var roots=AlsMantlingRootCompiler.Compile(rootJson);
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
            definitions.Add(setting.Path,new(setting,roots[setting.Montage],blend.Time,blend.Option));
        }
        return new(settings,definitions);
    }
}
