using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingRootTests(ITestOutputHelper output)
{
    private static JsonObject Source() => JsonNode.Parse(File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(),"assets/config/refactored_mantle_root_tracks.json")))!.AsObject();

    [Fact]
    public void ActualSourceKeysMatchAllNativeMontageSamplesWithoutConsumingOracle()
    {
        var source=Source();var extra=source["references"]!.AsArray();source.Remove("references");
        var samplers=AlsMantlingRootCompiler.Compile(source.ToJsonString());
        var original=JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Find(),
            "assets/config/refactored_mantle_inputs.json")))!;
        Assert.Equal(3,source["sequences"]!.AsArray().Count);Assert.Equal(6,samplers.Count);
        double maxPosition=0,maxRotation=0,maxScale=0;var count=0;
        void Compare(AlsPrecisePose actual,JsonNode expected)
        {
            double[] Values(string field)=>expected[field]!.AsArray().Select(v=>v!.GetValue<double>()).ToArray();
            var p=Values("position");var q=Values("rotation");var s=Values("scale");
            var position=(actual.Position-new AlsDoubleVector(p[0],p[1],p[2])).LengthSquared;
            var rotation=System.Math.Abs(1-System.Math.Abs(AlsQuaternion.Dot(actual.Rotation,new(q[0],q[1],q[2],q[3]))));
            var scale=(actual.Scale-new AlsDoubleVector(s[0],s[1],s[2])).LengthSquared;
            maxPosition=System.Math.Max(maxPosition,System.Math.Sqrt(position));
            maxRotation=System.Math.Max(maxRotation,rotation);maxScale=System.Math.Max(maxScale,System.Math.Sqrt(scale));
            Assert.True(position<=1e-6,$"Root position differs by {System.Math.Sqrt(position):G17} cm");
            Assert.True(rotation<=1e-7,$"Root rotation dot error {rotation:G17}");
            Assert.True(scale<=1e-12,$"Root scale error {scale:G17}");count++;
        }
        foreach(var montage in original["montages"]!.AsArray())
        {
            var sampler=samplers[montage!["path"]!.GetValue<string>()];
            foreach(var sample in montage["rootSamples"]!.AsArray())
                Compare(sampler.Sample(sample!["time"]!.GetValue<float>()),sample);
        }
        Assert.Equal(616,count);
        foreach(var montage in extra)
        {
            var sampler=samplers[montage!["path"]!.GetValue<string>()];
            foreach(var sample in montage["samples"]!.AsArray())
                Compare(sampler.Sample(sample!["time"]!.GetValue<float>()),sample);
            Compare(sampler.SampleLast(),montage["last"]!);
        }
        Assert.Equal(652,count);
        output.WriteLine($"Native roots={count}; max position={maxPosition:G17} cm; quaternion dot={maxRotation:G17}; scale={maxScale:G17}");
    }

    [Theory]
    [InlineData("source")][InlineData("channel")][InlineData("bone")][InlineData("binding")][InlineData("interpolation")]
    public void InvalidSourceDataIsRejected(string fault)
    {
        var source=Source();var sequence=source["sequences"]![0]!;
        switch(fault)
        {
            case "source": source["source"]="native sampled oracle";break;
            case "channel": sequence["tracks"]![0]!["positions"]=new JsonArray();break;
            case "bone": sequence["tracks"]![0]!["bone"]="pelvis";break;
            case "binding": source["montages"]![0]!["segments"]![0]!["sequence"]="missing";break;
            case "interpolation": sequence["interpolation"]="Cubic";break;
        }
        Assert.Throws<ArgumentException>(()=>AlsMantlingRootCompiler.Compile(source.ToJsonString()));
    }

    [Fact]
    public void EditingOriginalKeysChangesOutputWhileOracleStaysUnchanged()
    {
        var source=Source();var original=AlsMantlingRootCompiler.Compile(source.ToJsonString());
        var sequence=source["sequences"]![0]!;var path=sequence["source"]!.GetValue<string>();
        foreach(var key in sequence["tracks"]![0]!["positions"]!.AsArray()) key![2]=key[2]!.GetValue<float>()+10;
        var changed=AlsMantlingRootCompiler.Compile(source.ToJsonString());
        var montage=source["montages"]!.AsArray().First(m=>m!["segments"]![0]!["sequence"]!.GetValue<string>()==path)!["path"]!.GetValue<string>();
        Assert.Equal(10,changed[montage].Sample(0).Position.Z-original[montage].Sample(0).Position.Z,4);
    }
}
