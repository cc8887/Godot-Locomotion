using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingCurveTests(ITestOutputHelper output)
{
    private static string Read(string name)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets/config/"+name+".json"));
    [Fact]
    public void CurvesSharePoseSampleTimeAndMatchDenseNativeRawEvaluation()
    {
        var animation=Read("refactored_mantle_animation_inputs");var curveJson=Read("refactored_mantle_curves");
        var sources=AlsMantlingPoseCompiler.Compile(animation,Read("refactored_mantle_root_tracks"));
        var curves=AlsMantlingCurveCompiler.Compile(curveJson,animation);
        var oracle=JsonNode.Parse(Read("refactored_mantle_curve_reference"))!;
        Assert.Equal(oracle["curvesSha256"]!.GetValue<string>().ToUpperInvariant(),Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(curveJson))));
        var count=0;var partial=0;float max=0;
        foreach(var row in oracle["sequences"]!.AsArray())
        {
            var path=row!["source"]!.GetValue<string>();var source=curves[path];var sampler=sources[path].CreateSampler(source);
            var pose=new AlsPrecisePose[79];var values=new AlsInertialCurve[source.Names.Length];
            foreach(var sample in row["samples"]!.AsArray())
            {
                var key=sampler.Sample(sample!["requestedTime"]!.GetValue<double>(),true,false,false,pose,values);
                // The exporter reports the requested extraction time, not DataModel's quantized key time.
                Assert.Equal(sample["timeSeconds"]!.GetValue<double>(),sample["requestedTime"]!.GetValue<double>());
                Assert.Equal(AlsRawSequencePoseSampler.SelectKeys(sources[path].Data,sample["requestedTime"]!.GetValue<double>()).SampleTimeSeconds,key.SampleTimeSeconds);
                Assert.Equal(values.Length,sample["curves"]!.AsObject().Count);
                for(var i=0;i<values.Length;i++)
                {
                    var expected=sample["curves"]![source.Names[i]]!.GetValue<float>();
                    Assert.True(values[i].Present);var error=Math.Abs(values[i].Value-expected);max=Math.Max(max,error);
                    Assert.True(error<=2e-6,$"{path} {source.Names[i]} time={key.SampleTimeSeconds:R} expected={expected:R} actual={values[i].Value:R}");
                    count++;if(expected>0&&expected<1)partial++;
                }
            }
        }
        Assert.Equal(726,count);Assert.True(partial>50);output.WriteLine($"values={count} partial={partial} max_error={max:R}");
    }

    [Theory]
    [InlineData("hash")][InlineData("missing-source")][InlineData("missing-curve")][InlineData("duplicate-curve")]
    [InlineData("weighted")][InlineData("infinity")][InlineData("key-order")][InlineData("empty")]
    public void RejectsUnimplementedOrIncompleteCurveData(string mutation)
    {
        var json=JsonNode.Parse(Read("refactored_mantle_curves"))!;var curves=json["sequences"]![0]!["curves"]!.AsArray();
        switch(mutation)
        {
            case "hash":json["animationInputsSha256"]=new string('0',64);break;
            case "missing-source":json["sequences"]!.AsArray().RemoveAt(0);break;
            case "missing-curve":curves.RemoveAt(0);break;
            case "duplicate-curve":curves.Add(curves[0]!.DeepClone());break;
            case "weighted":curves[0]!["keys"]![0]!["weightMode"]="RCTWM_WeightedBoth";break;
            case "infinity":curves[0]!["preInfinity"]="RCCE_Cycle";break;
            case "key-order":curves[1]!["keys"]![1]!["time"]=0;break;
            case "empty":curves[0]!["keys"]=new JsonArray();break;
        }
        Assert.Throws<ArgumentException>(()=>AlsMantlingCurveCompiler.Compile(json.ToJsonString(),Read("refactored_mantle_animation_inputs")));
    }

    [Fact]
    public void RejectsCrossAssetAndCrossVersionBindingBeforeSampling()
    {
        var animation=Read("refactored_mantle_animation_inputs");var curves=AlsMantlingCurveCompiler.Compile(Read("refactored_mantle_curves"),animation);
        var poses=AlsMantlingPoseCompiler.Compile(animation,Read("refactored_mantle_root_tracks"));
        var first=poses.First();Assert.Throws<ArgumentException>(()=>first.Value.CreateSampler(curves.First(c=>c.Key!=first.Key).Value));
        var changed=AlsMantlingPoseCompiler.Compile(animation+"\n",Read("refactored_mantle_root_tracks"));
        Assert.Throws<ArgumentException>(()=>changed[first.Key].CreateSampler(curves[first.Key]));
    }
}
