using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredTriangulationTests(ITestOutputHelper output)
{
    private static string Inputs()=>MantlingHostFixture.Read("refactored_triangulation_inputs");
    private static IReadOnlyDictionary<string,AlsRefactoredTriangulationProfile> Compile(string json)=>
        AlsRefactoredTriangulationCompiler.Compile(json,MantlingHostFixture.Read("refactored_animation_sources"),
            p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    [Fact]
    public void AllOriginal2DSpacesMatchNativeCachedWalkAndOrderedWeights()
    {
        var json=Inputs();var profiles=Compile(json);Assert.Equal(8,profiles.Count);
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_triangulation_reference"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(),doc.RootElement.GetProperty("inputsSha256").GetString());
        var count=0;var maximum=0f;var values=new AlsAimGridVertex[3];var retry=new AlsAimGridVertex[3];
        foreach(var space in doc.RootElement.GetProperty("spaces").EnumerateArray())
        {
            var path=space.GetProperty("source").GetString()!;var profile=profiles[path];var cache=-1;
            foreach(var row in space.GetProperty("cases").EnumerateArray())
            {
                Assert.Equal(row.GetProperty("previousCache").GetInt32(),cache);
                var input=new AlsBlendPoint(row.GetProperty("input")[0].GetDouble(),row.GetProperty("input")[1].GetDouble());
                var n=profile.Weights.Evaluate(input,cache,values,out var next);
                var expected=row.GetProperty("weights");Assert.Equal(expected.GetArrayLength(),n);
                Assert.Equal(row.GetProperty("cache").GetInt32(),next);
                for(var i=0;i<n;i++)
                {
                    Assert.Equal(expected[i].GetProperty("sample").GetInt32(),values[i].Sample);
                    var error=MathF.Abs(expected[i].GetProperty("weight").GetSingle()-values[i].Weight);maximum=MathF.Max(maximum,error);
                    Assert.True(error<=1e-7f,$"{path} input={input} sample={i} error={error:R}");
                }
                // Retry starts with committed cache, not native recorded next cache.
                Assert.Equal(n,profile.Weights.Evaluate(input,cache,retry,out var repeated));Assert.Equal(next,repeated);
                Assert.Equal(values[..n],retry[..n]);cache=next;count++;
            }
        }
        Assert.Equal(4096,count);output.WriteLine($"spaces={profiles.Count} cases={count} maxWeight={maximum:R}");
    }
    [Theory]
    [InlineData("hash")][InlineData("missing")][InlineData("normal")][InlineData("vertex")][InlineData("adjacency")]
    public void RejectsInvalidTopologyAndBindings(string change)
    {
        var node=JsonNode.Parse(Inputs())!;var spaces=node["spaces"]!.AsArray();var t=spaces[0]!["data"]!["triangles"]![0]!;
        switch(change)
        {
            case "hash":node["catalogSha256"]=new string('0',64);break;
            case "missing":spaces.RemoveAt(0);break;
            case "normal":t["edgeInfo"]![0]!["normal"]!["x"]=0;break;
            case "vertex":t["vertices"]![0]!["x"]=.1;break;
            case "adjacency":t["edgeInfo"]![0]!["neighbourTriangleIndex"]=0;break;
        }
        Assert.Throws<ArgumentException>(()=>Compile(node.ToJsonString()));
    }
}
