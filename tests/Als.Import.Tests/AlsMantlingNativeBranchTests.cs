using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using GodotAls.Core.Actions;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingNativeBranchTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    public AlsMantlingNativeBranchTests(Xunit.Abstractions.ITestOutputHelper output)=>_output=output;
    [Fact]
    public void ActualNativeMontageAndAlsStatesMatchAcrossRatesConditionsAndInterruptions()
    {
        var config=Path.Combine(RepositoryRoot.Find(),"assets/config");
        string Read(string name)=>File.ReadAllText(Path.Combine(config,name+".json"));
        var input=Read("refactored_mantle_animation_inputs");
        var profile=AlsMantlingMontageCompiler.Compile(input,Read("refactored_mantle_root_tracks"),Read("refactored_mantle_curves"));
        using var reference=JsonDocument.Parse(Read("refactored_mantle_branch_reference"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant(),reference.RootElement.GetProperty("animationInputsSha256").GetString());
        var traces=reference.RootElement.GetProperty("traces");Assert.Equal(126,traces.GetArrayLength());
        var coverage=new HashSet<(string,int,int)>();var samples=0;var maxError=0f;
        foreach(var trace in traces.EnumerateArray())
        {
            var path=trace.GetProperty("montage").GetString()!;var mode=trace.GetProperty("mode").GetInt32();
            var hz=trace.GetProperty("hz").GetInt32();Assert.Contains(hz,new[]{30,60,120});Assert.InRange(mode,0,6);Assert.True(coverage.Add((path,hz,mode)));
            var branch=AlsMantlingBranchCompiler.Compile(input,profile);var bank=profile.CreateRuntime(branch);
            branch.Capture(new(mode==1,mode==2?"Als.LocomotionMode.InAir":"Als.LocomotionMode.Grounded",
                mode==3?"Als.RotationMode.Aiming":"Als.RotationMode.ViewDirection",mode==4?"Als.Stance.Crouching":"Als.Stance.Standing"));
            bank.Begin(new(1,1,1),.01f);bank.PlayAction(profile.Definitions[path].Asset.ActionDefinitionId,1);bank.Commit(new(1,1,1));
            var frame=2L;var active=new HashSet<int>();
            var rows=trace.GetProperty("frames");
            for(var index=0;index<rows.GetArrayLength();index++,frame++)
            {
                var row=rows[index];bank.Begin(new(frame,1,1),row.GetProperty("delta").GetSingle());
                samples++;
                foreach(var e in branch.CandidateEvents){if(e.Begin)active.Add(e.StateIndex);else active.Remove(e.StateIndex);}
                var context=$"{path} mode={mode} hz={trace.GetProperty("hz")} frame={index}";
                var nativeAction=row.GetProperty("action").GetString();
                Assert.True((nativeAction=="None"?"":nativeAction)==branch.CandidateAction,context+" action");
                Assert.True(row.GetProperty("exists").GetBoolean()==(bank.Candidate.Length==1),context+" existence");
                if(bank.Candidate.Length==1)
                {
                    var state=bank.Candidate[0];
                    void Near(string key,float actual,float tolerance=2e-6f)
                    {
                        var error=MathF.Abs(row.GetProperty(key).GetSingle()-actual);maxError=MathF.Max(maxError,error);
                        Assert.True(error<=tolerance,context+$" {key}: native={row.GetProperty(key)} actual={actual}");
                    }
                    Near("position",state.Position);Near("weight",state.Blend.CurrentWeight);Near("desired",state.Blend.DesiredWeight);Near("blendTime",state.BlendTime);
                    Assert.Equal(row.GetProperty("playing").GetBoolean(),state.Playing);
                    var expected=row.GetProperty("active").EnumerateArray().Select(x=>x.GetString() switch
                    {
                        "/Script/ALS.AlsAnimNotifyState_SetLocomotionAction"=>0,
                        "/Script/ALS.AlsAnimNotifyState_EarlyBlendOut"=>1,
                        _=>throw new InvalidOperationException("Unbound native state")
                    }).Order().ToArray();
                    Assert.True(expected.SequenceEqual(active.Order()),context+" active states");
                }
                else Assert.Empty(active);
                // Native stops before its next weight update. Stop this candidate after
                // the current update, so the shared bank consumes it at that same boundary.
                if(index+1<rows.GetArrayLength()&&rows[index+1].GetProperty("stopBefore").GetBoolean())
                    bank.StopInstance(1,.4f,profile.Definitions[path].Asset.Lifecycle.BlendOutOption);
                bank.Commit(new(frame,1,1));
            }
            Assert.Empty(bank.Committed.ToArray());
        }
        _output.WriteLine($"Native traces={coverage.Count}, frames={samples}, maximum numeric error={maxError:R}");
    }
}
