using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredAnimationCatalogTests(ITestOutputHelper output)
{
    private static string Index()=>MantlingHostFixture.Read("refactored_animation_sources");
    private static byte[] Bytes(string name)=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",name));
    [Fact]
    public void CompleteSourceBatchHasValidPayloadsAndBlendSpaceClosure()
    {
        var catalog=new AlsRefactoredAnimationCatalog(Index(),Bytes);
        Assert.Equal(180,catalog.Assets.Count);Assert.Equal(127,catalog.Assets.Values.Count(a=>a.Class=="AnimSequence"));
        Assert.Equal(21,catalog.Assets.Values.Count(a=>a.Class=="AnimBlueprint"));
        foreach(var entry in catalog.Assets.Values)
        {
            var payload=catalog.Read(entry.Source);
            if(entry.Class is "BlendSpace" or "BlendSpace1D")
                foreach(var sample in payload.GetProperty("samples").EnumerateArray())
                    Assert.Equal("AnimSequence",catalog.Assets[sample.GetProperty("sequence").GetString()!].Class);
            if(entry.Class=="AnimBlueprint")
                Assert.Equal(entry.Source,payload.GetProperty("compiled").GetProperty("source").GetString());
            if(entry.Class=="AnimSequence")
            {
                Assert.Equal(entry.Source,payload.GetProperty("raw").GetProperty("source").GetString());
                var basis=payload.GetProperty("evaluation").GetProperty("baseAsset");
                if(basis.ValueKind==System.Text.Json.JsonValueKind.String)Assert.Contains(basis.GetString()!,catalog.Assets.Keys);
            }
        }
    }
    [Fact]
    public void AllAbsoluteSourcesCompileAndMatchNativeRetargetedPoseSamples()
    {
        var catalog=new AlsRefactoredAnimationCatalog(Index(),Bytes);var sequences=0;var samples=0;var additives=0;
        double maxP=0,maxQ=0,maxS=0;
        foreach(var entry in catalog.Assets.Values.Where(a=>a.Class=="AnimSequence"))
        {
            var payload=catalog.Read(entry.Source);
            if(payload.GetProperty("evaluation").GetProperty("additiveType").GetString()!="AAT_None")
            {Assert.Throws<ArgumentException>(()=>catalog.CompileAbsolutePose(entry.Source));additives++;continue;}
            var source=catalog.CompileAbsolutePose(entry.Source);var sampler=source.CreateSampler();var pose=new AlsPrecisePose[source.BoneNames.Length];
            var references=payload.GetProperty("poseReference");Assert.Equal(5,references.GetArrayLength());
            foreach(var reference in references.EnumerateArray())
            {
                Assert.Equal(source.BoneNames.ToArray(),reference.GetProperty("names").EnumerateArray().Select(v=>v.GetString()!));
                sampler.Sample(reference.GetProperty("timeSeconds").GetDouble(),true,false,false,pose);
                for(var bone=0;bone<pose.Length;bone++)
                {
                    var expected=reference.GetProperty("pose")[bone];
                    double[] V(string name)=>expected.GetProperty(name).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                    var p=V("position");var q=V("rotation");var s=V("scale");var a=pose[bone];
                    var dp=Math.Sqrt(Math.Pow(a.Position.X-p[0],2)+Math.Pow(a.Position.Y-p[1],2)+Math.Pow(a.Position.Z-p[2],2));
                    var sign=a.Rotation.X*q[0]+a.Rotation.Y*q[1]+a.Rotation.Z*q[2]+a.Rotation.W*q[3]<0?-1:1;
                    var dq=new[]{Math.Abs(a.Rotation.X-sign*q[0]),Math.Abs(a.Rotation.Y-sign*q[1]),Math.Abs(a.Rotation.Z-sign*q[2]),Math.Abs(a.Rotation.W-sign*q[3])}.Max();
                    var ds=new[]{Math.Abs(a.Scale.X-s[0]),Math.Abs(a.Scale.Y-s[1]),Math.Abs(a.Scale.Z-s[2])}.Max();
                    maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);
                    Assert.True(dp<=1e-6&&dq<=1e-8&&ds<=1e-8,$"{entry.Source} t={reference.GetProperty("timeSeconds")} bone={bone} p={dp:R} q={dq:R} s={ds:R}");
                }
                samples++;
            }
            sequences++;
        }
        Assert.Equal(85,sequences);Assert.Equal(42,additives);Assert.Equal(425,samples);
        output.WriteLine($"absolute={sequences} additive={additives} poses={samples} maxP_cm={maxP:R} maxQ={maxQ:R} maxS={maxS:R}");
    }
    [Theory]
    [InlineData("file")][InlineData("duplicate")][InlineData("counts")][InlineData("hash")]
    public void RejectsChangedResourceBindings(string change)
    {
        var node=JsonNode.Parse(Index())!;var rows=node["assets"]!.AsArray();
        if(change=="file")rows[0]!["file"]="../outside.json";
        else if(change=="duplicate")rows.Add(rows[0]!.DeepClone());
        else if(change=="counts")node["counts"]!["AnimSequence"]=0;
        else rows[0]!["sha256"]=new string('0',64);
        if(change=="hash")
        {var catalog=new AlsRefactoredAnimationCatalog(node.ToJsonString(),Bytes);Assert.Throws<ArgumentException>(()=>catalog.Read(rows[0]!["source"]!.GetValue<string>()));}
        else Assert.Throws<ArgumentException>(()=>new AlsRefactoredAnimationCatalog(node.ToJsonString(),Bytes));
    }
}
