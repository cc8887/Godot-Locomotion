using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponOverlayNativeTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases() => AlsRefactoredWeaponNativeTests.Cases();
    [Theory]
    [MemberData(nameof(Cases))]
    public void WholeOverlayMatchesNativeAcrossActionsAndHiddenReset(AlsRefactoredWeaponKind kind, int hz)
    {
        var catalog=new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        var source=new AlsRefactoredWeaponSourceProfile(catalog,new(catalog,new(MantlingHostFixture.Read("refactored_weapon_machines"),catalog,kind)));
        var profile=new AlsRefactoredWeaponActionProfile(catalog,source);var runtime=new AlsRefactoredWeaponOverlayRuntime(profile,0);
        var players=new AlsRefactoredSourcePlayerRuntime(catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),catalog),new Dictionary<string,AlsRefactoredTriangulationProfile>(),source.Players.Bind(0,new Dictionary<string,int>{["Secondary Motion"]=0,["Movement"]=1}));
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_weapon_action_trace_"+kind));var root=doc.RootElement;
        Assert.Equal(AlsRefactoredWeaponMachineResources.Blueprint(kind),root.GetProperty("source").GetString());
        foreach(var hash in root.GetProperty("resourceHashes").EnumerateObject())
        {
            var digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(hash.Name))));
            Assert.Equal(hash.Value.GetString()!.ToUpperInvariant(),digest);
        }
        Assert.Equal(profile.BoneNames.ToArray(),root.GetProperty("names").EnumerateArray().Select(n=>n.GetString()!));
        var trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==hz+"hz");
        int frame=0,hidden=0,zero=0,resets=0;double maxP=0,maxQ=0,maxS=0,maxC=0,maxTime=0;var full=new HashSet<int>();
        foreach(var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input=row.GetProperty("input");var ps=input.GetProperty("poseState");float V(string n)=>ps.GetProperty(n).GetSingle();
            var vel=input.GetProperty("groundedState").GetProperty("VelocityBlend");
            var poseInput=new AlsRefactoredWeaponPoseInput(V("GaitWalkingAmount"),V("GaitRunningAmount"),V("GaitSprintingAmount"),V("StandingAmount"),V("CrouchingAmount"),V("InAirAmount"),input.GetProperty("inAirState").GetProperty("GroundPredictionAmount").GetSingle(),input.GetProperty("standingState").GetProperty("SprintAccelerationAmount").GetSingle(),new Vector4(vel.GetProperty("ForwardAmount").GetSingle(),vel.GetProperty("BackwardAmount").GetSingle(),vel.GetProperty("LeftAmount").GetSingle(),vel.GetProperty("RightAmount").GetSingle()));
            var rules=new AlsRefactoredWeaponRuleInput(input.GetProperty("rotationMode").GetString()!,input.GetProperty("gait").GetString()!,input.GetProperty("locomotionMode").GetString()!,input.GetProperty("moving").GetBoolean(),input.GetProperty("allowed").GetBoolean());
            var delta=input.GetProperty("delta").GetSingle();var pitch=input.GetProperty("viewState").GetProperty("PitchAmount").GetSingle();var reset=input.GetProperty("reset").GetBoolean();var tag=input.GetProperty("action").GetString()!;
            void Prepare(){runtime.Prepare(frame,tag,rules,poseInput,pitch,delta,reset);players.Prepare(frame,runtime.SourceInputs,delta);runtime.Evaluate(frame,players);}
            Prepare();var pose=runtime.Pose.ToArray();var curves=runtime.Curves.ToArray();
            if(!runtime.MachineUpdated)hidden++;
            else
            {
                if(runtime.CandidateActions.Weights.X==0)zero++;
                if(runtime.Machine.Candidate.Reinitialized)resets++;
                Assert.Equal(row.GetProperty("current").GetInt32(),runtime.Machine.Candidate.State.CurrentState);
                Assert.InRange(Math.Abs(runtime.Machine.Candidate.State.ElapsedSeconds-row.GetProperty("elapsed").GetSingle()),0,2e-6);
            }
            for(var i=0;i<4;i++)if(runtime.CandidateActions.Weights[i]==1)full.Add(i);
            var nativePlayers=row.GetProperty("players").EnumerateArray().ToDictionary(p=>p.GetProperty("propertyIndex").GetInt32());
            foreach(var tick in runtime.SourceInputs)
            {
                var expected=nativePlayers[source.Players.Players[tick.PlayerId].PropertyIndex];var time=players.Players.ToArray().Single(p=>p.PlayerId==tick.PlayerId).Time;
                maxTime=Math.Max(maxTime,Math.Abs(time-expected.GetProperty("time").GetSingle()));Assert.True(maxTime<=2e-6,$"{kind}/{hz}/{frame} clock={maxTime}");
                Assert.InRange(Math.Abs(tick.Weight-expected.GetProperty("weight").GetSingle()),0,2e-6);
            }
            for(var b=0;b<pose.Length;b++)
            {
                var native=row.GetProperty("pose")[b];double[] A(string n)=>native.GetProperty(n).EnumerateArray().Select(v=>v.GetDouble()).ToArray();var p=A("position");var q=A("rotation");var s=A("scale");var actual=pose[b];
                var dp=Math.Sqrt(Math.Pow(actual.Position.X-p[0],2)+Math.Pow(actual.Position.Y-p[1],2)+Math.Pow(actual.Position.Z-p[2],2));
                var sign=actual.Rotation.X*q[0]+actual.Rotation.Y*q[1]+actual.Rotation.Z*q[2]+actual.Rotation.W*q[3]<0?-1:1;
                var dq=new[]{Math.Abs(actual.Rotation.X-sign*q[0]),Math.Abs(actual.Rotation.Y-sign*q[1]),Math.Abs(actual.Rotation.Z-sign*q[2]),Math.Abs(actual.Rotation.W-sign*q[3])}.Max();
                var ds=new[]{Math.Abs(actual.Scale.X-s[0]),Math.Abs(actual.Scale.Y-s[1]),Math.Abs(actual.Scale.Z-s[2])}.Max();
                maxP=Math.Max(maxP,dp);maxQ=Math.Max(maxQ,dq);maxS=Math.Max(maxS,ds);Assert.True(dp<=1e-4&&dq<=2e-6&&ds<=2e-6,$"{kind}/{hz}/{frame}/{b} p={dp:R} q={dq:R} s={ds:R}");
            }
            var nativeCurves=row.GetProperty("curves");var present=0;
            for(var c=0;c<curves.Length;c++){Assert.Equal(curves[c].Present,nativeCurves.TryGetProperty(profile.CurveNames[c],out var expected));if(!curves[c].Present)continue;present++;var diff=Math.Abs(curves[c].Value-expected.GetSingle());maxC=Math.Max(maxC,diff);Assert.True(diff<=2e-6,$"{kind}/{hz}/{frame}/{profile.CurveNames[c]} diff={diff}");}
            Assert.Equal(present,nativeCurves.EnumerateObject().Count());
            if(frame%17==0){runtime.Cancel();players.Cancel();Prepare();Assert.Equal(pose,runtime.Pose.ToArray());Assert.Equal(curves,runtime.Curves.ToArray());}
            runtime.ValidateCommit(frame);players.ValidateCommit(frame);runtime.Commit(frame);players.Commit(frame++);
        }
        Assert.Equal(10*hz+2,frame);Assert.True(hidden>hz&&zero>0&&resets>=3);Assert.Equal(4,full.Count);
        output.WriteLine($"{kind}/{hz} frames={frame} hidden={hidden} zero={zero} resets={resets} maxP={maxP:R} maxQ={maxQ:R} maxS={maxS:R} maxC={maxC:R} maxTime={maxTime:R}");
    }
}
