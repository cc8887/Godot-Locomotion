using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingSettingsTests
{
    private static JsonObject Source()=>JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/refactored_mantle_inputs.json")))!.AsObject();
    [Fact]
    public void AuthoredGraphMatchesAllIndependentNativeSelections()
    {
        var source=Source(); var references=source["selection"]!.AsArray();
        source.Remove("selection"); // Production cannot consume the oracle.
        foreach(var m in source["montages"]!.AsArray()) m!.AsObject().Remove("rootSamples");
        var profile=AlsMantlingSettingsCompiler.Compile(source.ToJsonString());
        Assert.Equal(7,profile.Settings.Count); Assert.Equal(39,references.Count);
        foreach(var row in references)
        {
            var type=row!["type"]!.GetValue<string>() switch
            { "HIGH"=>AlsMantlingType.High,"LOW"=>AlsMantlingType.Low,"IN_AIR"=>AlsMantlingType.InAir,_=>throw new Exception() };
            Assert.Equal(row["settings"]!.GetValue<string>(),profile.Select(type,row["overlay"]!.GetValue<string>()).Path);
        }
        Assert.Equal(profile.Select(AlsMantlingType.Low,"Als.OverlayMode.Default"),profile.Select(AlsMantlingType.Low,"Als.OverlayMode.Bow.Child"));
        Assert.Equal(profile.Select(AlsMantlingType.Low,"Als.OverlayMode.Bow"),profile.Select(AlsMantlingType.Low,"als.overlaymode.bow"));
    }
    [Fact]
    public void RealSettingsFeedNativeHeightMappingWithoutRootSampling()
    {
        var profile=AlsMantlingSettingsCompiler.Compile(Source().ToJsonString());
        foreach(var s in profile.Settings.Values)
        {
            Assert.False(s.AutoStart); Assert.True(s.RateScale>0);
            Assert.Equal(s.TimeLow,s.CalculateStartTime(s.ReferenceLow-10,0,_=>throw new Exception("Unexpected auto search")));
            Assert.Equal(s.TimeHigh,s.CalculateStartTime(s.ReferenceHigh+10,0,_=>throw new Exception("Unexpected auto search")));
            Assert.Equal(s.TimeLow+(s.TimeHigh-s.TimeLow)*.5f,s.CalculateStartTime((s.ReferenceLow+s.ReferenceHigh)*.5f,0,_=>throw new Exception()));
            Assert.Equal(AlsActionBlendOption.Linear,s.LocationBlend);Assert.Equal(AlsActionBlendOption.HermiteCubic,s.RotationBlend);
        }
    }
    [Fact]
    public void ChangingAuthoredReturnChangesSelectionWithoutChangingOracle()
    {
        var source=Source();var original=AlsMantlingSettingsCompiler.Compile(source.ToJsonString());
        var high=original.Select(AlsMantlingType.High,"").Path;
        var air=original.Select(AlsMantlingType.InAir,"").Path;
        var graph=source["graphs"]![0]!;
        graph["nativeText"]=graph["nativeText"]!.GetValue<string>().Replace("DefaultObject=\""+high+"\"","DefaultObject=\""+air+"\"",StringComparison.Ordinal);
        var changed=AlsMantlingSettingsCompiler.Compile(source.ToJsonString());
        Assert.Equal(air,changed.Select(AlsMantlingType.High,"").Path);
        Assert.NotEqual(high,changed.Select(AlsMantlingType.High,"").Path);
    }
    [Theory]
    [InlineData("timing")][InlineData("binding")][InlineData("blend")][InlineData("tag")][InlineData("edge")]
    public void InvalidInputsAreRejectedBeforeProfilePublication(string fault)
    {
        var source=Source();var setting=source["settings"]![0]!;
        switch(fault)
        {
            case "timing": setting["warpRange"]![1]=0;break;
            case "binding": setting["montage"]="/Missing/Montage";break;
            case "blend": setting["locationBlend"]="CUSTOM";break;
            case "tag":
                var graph=source["graphs"]![0]!;
                graph["nativeText"]=graph["nativeText"]!.GetValue<string>().Replace("PinNames(0)=\"Als.OverlayMode.Injured\"","PinNames(0)=\"wrong\"",StringComparison.Ordinal);break;
            case "edge":
                var broken=source["graphs"]![0]!;
                broken["nativeText"]=Regex.Replace(broken["nativeText"]!.GetValue<string>(),
                    "(PinName=\"then\"[^\\r\\n]*?)LinkedTo=\\([^)]*\\)","$1LinkedTo=()");break;
        }
        Assert.Throws<ArgumentException>(()=>AlsMantlingSettingsCompiler.Compile(source.ToJsonString()));
    }
}
