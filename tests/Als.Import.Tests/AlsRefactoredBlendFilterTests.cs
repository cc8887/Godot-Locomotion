using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredBlendFilterTests(ITestOutputHelper output)
{
    [Fact]
    public void NativeContinuousFilterWeightsAndTransactionalPosesAcrossThreeRates()
    {
        var inputs=MantlingHostFixture.Read("refactored_triangulation_inputs");var catalogJson=MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string p)=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p));
        var profiles=AlsRefactoredTriangulationCompiler.Compile(inputs,catalogJson,Read);var catalog=new AlsRefactoredAnimationCatalog(catalogJson,Read);
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_blend_filter_reference"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs))).ToLowerInvariant(),doc.RootElement.GetProperty("inputsSha256").GetString());
        var frames=0;float maxFilter=0,maxWeight=0;var values=new AlsAimGridVertex[3];
        foreach(var space in doc.RootElement.GetProperty("spaces").EnumerateArray())
        {
            var path=space.GetProperty("source").GetString()!;var profile=profiles[path];var source=new AlsRefactoredBlendPoseSource(profile,catalog);
            foreach(var run in space.GetProperty("runs").EnumerateArray())
            {
                var state=default(AlsRefactoredBlendFilterState);var cache=-1;var frame=0;
                var clean=new AlsRefactoredBlendEvaluatorRuntime(source);var retry=new AlsRefactoredBlendEvaluatorRuntime(source);
                foreach(var row in run.GetProperty("cases").EnumerateArray())
                {
                    var input=row.GetProperty("input");var raw=new Vector2(input[0].GetSingle(),input[1].GetSingle());
                    var delta=input[2].GetSingle();var reset=input[3].GetBoolean();if(reset){state=default;cache=-1;}
                    var next=AlsRefactoredBlendFilter.Advance(state,raw,delta,profile.FilterWindows);
                    var expected=row.GetProperty("filtered");var error=Vector2.Abs(next.Output-new Vector2(expected[0].GetSingle(),expected[1].GetSingle()));
                    maxFilter=MathF.Max(maxFilter,MathF.Max(error.X,error.Y));Assert.True(error.X<=1e-6f&&error.Y<=1e-6f,$"{path} frame={frame} filter={error}");
                    Assert.Equal(row.GetProperty("previousCache").GetInt32(),cache);
                    var n=profile.Weights.Evaluate(new(next.Output.X,next.Output.Y),cache,values,out var nextCache);
                    Assert.Equal(row.GetProperty("cache").GetInt32(),nextCache);var weights=row.GetProperty("weights");Assert.Equal(weights.GetArrayLength(),n);
                    for(var i=0;i<n;i++)
                    {
                        Assert.Equal(weights[i].GetProperty("sample").GetInt32(),values[i].Sample);
                        var diff=MathF.Abs(weights[i].GetProperty("weight").GetSingle()-values[i].Weight);maxWeight=MathF.Max(maxWeight,diff);Assert.True(diff<=1e-6f);
                    }
                    var time=(frame%31)/30f;
                    Assert.Throws<ArgumentException>(()=>retry.Prepare(frame,new(float.NaN,0),delta,time,reset));
                    clean.Prepare(frame,raw,delta,time,reset);clean.Evaluate(frame);
                    var before=retry.CommittedInput;
                    retry.Prepare(frame,raw,delta,time,reset);
                    Assert.Throws<ArgumentException>(()=>retry.ValidateCommit(frame));
                    retry.Evaluate(frame);retry.Cancel();
                    Assert.Equal(before,retry.CommittedInput);
                    retry.Prepare(frame,raw,delta,time,reset);retry.Evaluate(frame);retry.Evaluate(frame);
                    Assert.Equal(next.Output,retry.CandidateInput);
                    Assert.True(clean.Pose.SequenceEqual(retry.Pose));Assert.True(clean.Curves.SequenceEqual(retry.Curves));
                    Assert.Throws<ArgumentException>(()=>retry.ValidateCommit(frame+1));
                    clean.ValidateCommit(frame);retry.ValidateCommit(frame);clean.Commit(frame);retry.Commit(frame);
                    Assert.Throws<ArgumentException>(()=>retry.Prepare(frame,raw,delta,time));
                    state=next;cache=nextCache;frame++;frames++;
                }
            }
        }
        Assert.Equal(6720,frames);output.WriteLine($"frames={frames} maxFilter={maxFilter:R} maxWeight={maxWeight:R}");
    }
    [Fact]
    public void HistoryOverflowRejectsWithoutMutatingCommittedValueAndResetRecovers()
    {
        var state=default(AlsRefactoredBlendFilterState);
        for(var i=0;i<260;i++)state=AlsRefactoredBlendFilter.Advance(state,Vector2.One,.001f,Vector2.One);
        Assert.Throws<InvalidOperationException>(()=>AlsRefactoredBlendFilter.Advance(state,Vector2.Zero,.001f,Vector2.One));
        Assert.Equal(Vector2.One,state.Output);
        Assert.Equal(Vector2.Zero,AlsRefactoredBlendFilter.Advance(default,Vector2.Zero,.001f,Vector2.One).Output);
        Assert.Equal(new Vector2(2),AlsRefactoredBlendFilter.Advance(default,new(2),0,Vector2.Zero).Output);
    }
}
