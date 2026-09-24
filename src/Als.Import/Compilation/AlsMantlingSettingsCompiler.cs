using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using static GodotAls.Import.Compilation.AlsYawOffsetCompiler;

namespace GodotAls.Import.Compilation;

public sealed record AlsMantlingSettingsDefinition(string Path, string Montage, float MontageLength, float RateScale,
    bool AutoStart, float ReferenceLow, float ReferenceHigh, float TimeLow, float TimeHigh,
    float WarpStart, float WarpEnd, AlsActionBlendOption LocationBlend, AlsActionBlendOption RotationBlend)
{
    public float CalculateStartTime(float height, double samplingFramesPerSecond, Func<float,double> sampleRootHeight) =>
        AutoStart ? AlsMantlingStartTime.Find(height,MontageLength,samplingFramesPerSecond,sampleRootHeight)
            : AlsMantlingStartTime.MapHeight(height,ReferenceLow,ReferenceHigh,TimeLow,TimeHigh);
}

public sealed class AlsMantlingSettingsProfile
{
    private readonly string _high, _inAir, _low;
    private readonly Dictionary<string,string> _overlays;
    public IReadOnlyDictionary<string,AlsMantlingSettingsDefinition> Settings { get; }
    internal AlsMantlingSettingsProfile(Dictionary<string,AlsMantlingSettingsDefinition> settings,
        string high,string inAir,string low,Dictionary<string,string> overlays)
    { Settings = new ReadOnlyDictionary<string,AlsMantlingSettingsDefinition>(settings); _high=high; _inAir=inAir; _low=low; _overlays=overlays; }
    public AlsMantlingSettingsDefinition Select(AlsMantlingType type,string overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return Settings[type switch
        {
            AlsMantlingType.High => _high, AlsMantlingType.InAir => _inAir,
            AlsMantlingType.Low => _overlays.GetValueOrDefault(overlay,_low),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        }];
    }
}

// Compile the authored switch edges and return pins. The exported "selection"
// cases and "rootSamples" are independent references and are never consumed.
public static class AlsMantlingSettingsCompiler
{
    public static AlsMantlingSettingsProfile Compile(string json)
    {
        using var document=JsonDocument.Parse(json); var root=document.RootElement;
        const string character="/ALS/ALS/Character/B_Als_Character.B_Als_Character";
        Require(root.GetProperty("schemaVersion").GetInt32()==1 && Text(root,"character")==character,"Foreign mantle schema/source.");
        var montages=root.GetProperty("montages").EnumerateArray().ToDictionary(r=>Text(r,"path"),StringComparer.Ordinal);
        var settings=new Dictionary<string,AlsMantlingSettingsDefinition>(StringComparer.Ordinal);
        foreach(var row in root.GetProperty("settings").EnumerateArray())
        {
            var path=Text(row,"path"); var montagePath=Text(row,"montage");
            Require(path.StartsWith("/ALS/ALS/Data/Character/Mantle/",StringComparison.Ordinal) &&
                montagePath.StartsWith("/ALS/ALS/Animations/Actions/Mantle/",StringComparison.Ordinal) && montages.ContainsKey(montagePath),"Unbound mantle asset.");
            var montage=montages[montagePath]; var length=Number(montage,"length"); var rate=Number(montage,"rateScale");
            var height=Pair(row,"referenceHeight"); var time=Pair(row,"startTime"); var warp=Pair(row,"warpRange");
            Require(length>0 && rate>0 && time.A>=0 && time.B>=0 && time.A<=length && time.B<=length &&
                warp.A>=0 && warp.B>warp.A && warp.B<=length,"Invalid mantle timing.");
            settings.Add(path,new(path,montagePath,length,rate,row.GetProperty("autoCalculateStartTime").GetBoolean(),
                height.A,height.B,time.A,time.B,warp.A,warp.B,Blend(Text(row,"locationBlend")),Blend(Text(row,"rotationBlend"))));
        }
        var native=root.GetProperty("graphs").EnumerateArray().Single(g=>Text(g,"path")==character+":SelectMantlingSettings")
            .GetProperty("nativeText").GetString()!;
        var graph=new Graph(native,true);
        var entry=graph.One("K2Node_FunctionEntry","SelectMantlingSettings");
        var typeSwitch=graph.Nodes.Single(n=>n.Kind=="K2Node_SwitchEnum");
        Require(typeSwitch.Body.Contains("/Script/ALS.EAlsMantlingType",StringComparison.Ordinal),"Wrong mantle enum switch.");
        graph.Link(typeSwitch,"execute",entry,"then"); graph.Link(typeSwitch,"Selection",entry,"MantlingType");
        var tagSwitch=Next(typeSwitch,"Low");
        Require(tagSwitch.Kind=="GameplayTagsK2Node_SwitchGameplayTag","Low mantle is not an authored tag switch.");
        Require(tagSwitch.Pins.Values.Any(p=>p.Name=="NotEqual_TagTag" && !p.Output && p.Links==""),"Unsupported mantle tag comparison.");
        var overlay=graph.One("K2Node_VariableGet","OverlayMode"); graph.Self(overlay);
        graph.Link(tagSwitch,"Selection",overlay,"OverlayMode");
        var names=Regex.Matches(tagSwitch.Body,"PinNames\\((\\d+)\\)=\"([^\"]+)\"");
        var tags=Regex.Matches(tagSwitch.Body,"PinTags\\((\\d+)\\)=\\(TagName=\"([^\"]+)\"\\)");
        Require(names.Count>0 && names.Count==tags.Count,"Incomplete mantle tag cases.");
        // GameplayTag equality uses FName, which is case-insensitive and exact
        // (a child tag does not automatically match its parent).
        var mapped=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for(var i=0;i<names.Count;i++)
        {
            Require(names[i].Groups[1].Value==i.ToString() && tags[i].Groups[1].Value==i.ToString() &&
                names[i].Groups[2].Value==tags[i].Groups[2].Value,"Mismatched mantle tag pin.");
            var name=names[i].Groups[2].Value;
            mapped.Add(name,Result(Next(tagSwitch,name)));
        }
        return new(settings,Result(Next(typeSwitch,"High")),Result(Next(typeSwitch,"InAir")),Result(Next(tagSwitch,"Default")),mapped);

        Node Next(Node node,string name)
        {
            var pin=node.Pins.Values.Single(p=>p.Name==name && p.Output);
            var links=Regex.Matches(pin.Links,@"(\w+) (\w+),"); Require(links.Count==1,"Missing/ambiguous mantle execution edge.");
            var target=graph.Named(links[0].Groups[1].Value); var input=target.Pins[links[0].Groups[2].Value];
            Require(!input.Output && input.Name=="execute" && input.Links.Contains(node.Name+" "+pin.Id+",",StringComparison.Ordinal),"Nonreciprocal mantle execution edge.");
            return target;
        }
        string Result(Node node)
        {
            Require(node.Kind=="K2Node_FunctionResult" && node.Member=="SelectMantlingSettings","Unsupported mantle return path.");
            var pin=node.Pins.Values.Single(p=>p.Name=="ReturnValue"); Require(!pin.Output && pin.Links=="","Dynamic mantle return requires a compiler extension.");
            var line=Regex.Matches(node.Body,@"CustomProperties Pin [^\r\n]+").Single(m=>m.Value.Contains("PinId="+pin.Id+",",StringComparison.Ordinal)).Value;
            var path=Regex.Match(line,"DefaultObject=\"([^\"]+)\"").Groups[1].Value;
            Require(settings.ContainsKey(path),"Unknown mantle return asset."); return path;
        }
    }
    private static AlsActionBlendOption Blend(string value)=>value switch
    { "LINEAR"=>AlsActionBlendOption.Linear,"HERMITE_CUBIC"=>AlsActionBlendOption.HermiteCubic,
        _=>throw new ArgumentException("Unsupported mantle warp blend: "+value) };
    private static string Text(JsonElement e,string name)=>e.GetProperty(name).GetString() ?? throw new ArgumentException("Missing mantle name.");
    private static float Number(JsonElement e,string name)
    { var value=e.GetProperty(name).GetSingle(); Require(float.IsFinite(value),"Nonfinite mantle input."); return value; }
    private static (float A,float B) Pair(JsonElement e,string name)
    {
        var a=e.GetProperty(name); Require(a.GetArrayLength()==2,"Invalid mantle range.");
        var x=a[0].GetSingle();var y=a[1].GetSingle();Require(float.IsFinite(x)&&float.IsFinite(y),"Nonfinite mantle range.");return(x,y);
    }
    private static void Require(bool ok,string message) { if(!ok) throw new ArgumentException(message); }
}
