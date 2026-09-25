using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredQuickStopNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ActualNativeNotifyAndContinuousSlotMatch(int hz)
    {
        var f=AlsRefactoredQuickStopTests.Data.Value;var q=f.Quick;var bank=new AlsMontageRuntime([],sequences:q.Assets);
        var queue=new AlsTransitionQueueRuntime(bank,11,1);
        var basis=f.Catalog.CompileAbsolutePoseWithCurves(AlsRefactoredStandingRestGraph.IdleSequence);
        var names=basis.Curves.Names.ToArray().Concat(q.Assets.ToArray().SelectMany(a=>f.Catalog.CompileAdditivePose(q.SourcePath(a.AnimationId)).CurveNames.ToArray())).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var source=new AlsRefactoredTransitionPose(f.Catalog,q,basis.Pose.BoneNames,basis.Pose.Parents,names);var sampler=source.CreateSampler();
        var basePose=new AlsPrecisePose[79];var baseCurves=new AlsInertialCurve[names.Length];var raw=new AlsInertialCurve[basis.Curves.Names.Length];
        basis.Pose.CreateSampler(basis.Curves).Sample(0,true,false,false,basePose,raw);
        for(var c=0;c<raw.Length;c++)baseCurves[Array.IndexOf(names,basis.Curves.Names[c])]=raw[c];
        var mixer=new AlsMontageSlotPose(basePose,basis.Pose.Parents,names.Length);var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[names.Length];
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_quick_stop_trace"));var root=doc.RootElement;
        Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32());
        foreach(var hash in root.GetProperty("resourceHashes").EnumerateObject())
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(hash.Name)))),hash.Value.GetString()!.ToUpperInvariant());
        var trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("hz").GetInt32()==hz);
        double maxPose=0,maxCurve=0,maxClock=0,maxWeight=0;var frame=0;var mixed=0;var created=new HashSet<long>();var sources=new HashSet<string>();
        foreach(var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input=row.GetProperty("input");var id=new AlsFrameIdentity(frame,11,1);bank.Begin(id,input.GetProperty("delta").GetSingle());queue.Begin(id);
            var weights=bank.Frame.SlotWeights(AlsMontageSlot.Transition);
            Compare(weights.SourceWeight,row.GetProperty("sourceWeight").GetDouble(),ref maxWeight,2e-6,"sourceWeight");
            Compare(weights.SlotNodeWeight,row.GetProperty("slotWeight").GetDouble(),ref maxWeight,2e-6,"slotWeight");
            Compare(weights.TotalNodeWeight,row.GetProperty("totalWeight").GetDouble(),ref maxWeight,2e-6,"totalWeight");
            var entries=row.GetProperty("evaluation");Assert.Equal(entries.GetArrayLength(),bank.Evaluation.Length);if(bank.Evaluation.Length>1)mixed++;
            for(var i=0;i<bank.Evaluation.Length;i++)
            {
                var entry=bank.Evaluation[i];Assert.Equal(entries[i].GetProperty("source").GetString(),q.SourcePath(entry.AnimationId));
                Compare(entry.Position,entries[i].GetProperty("position").GetDouble(),ref maxClock,2e-6,"sampleTime");Compare(entry.Weight,entries[i].GetProperty("weight").GetDouble(),ref maxWeight,2e-6,"sampleWeight");
            }
            mixer.Evaluate(bank.Frame,id,AlsMontageSlot.Transition,basePose,baseCurves,pose,curves,sampler);
            var native=row.GetProperty("result").GetProperty("pose");Assert.Equal(790,native.GetArrayLength());
            for(var b=0;b<79;b++)
            {
                var p=pose[b];double[] values=[p.Position.X,p.Position.Y,p.Position.Z,p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W,p.Scale.X,p.Scale.Y,p.Scale.Z];
                var dot=Enumerable.Range(3,4).Sum(c=>values[c]*native[b*10+c].GetDouble());
                for(var c=0;c<10;c++)Compare(values[c]*(c is >=3 and <=6&&dot<0?-1:1),native[b*10+c].GetDouble(),ref maxPose,c<3?2e-5:2e-6,$"bone{b}/{c}");
            }
            var expectedCurves=row.GetProperty("result").GetProperty("curves").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetDouble(),StringComparer.OrdinalIgnoreCase);
            Assert.Equal(expectedCurves.Count,curves.Count(c=>c.Present));
            for(var c=0;c<curves.Length;c++)
            {Assert.Equal(expectedCurves.ContainsKey(names[c]),curves[c].Present);if(curves[c].Present)Compare(curves[c].Value,expectedCurves[names[c]],ref maxCurve,2e-6,names[c]);}
            if(input.GetProperty("stop").GetBoolean())queue.StopImmediate(input.GetProperty("stopDuration").GetSingle());
            var quick=input.GetProperty("quick");var count=quick.GetProperty("count").GetInt32();
            var command=q.Command(new(quick.GetProperty("rotation").GetString()=="Als.RotationMode.VelocityDirection",input.GetProperty("stance").GetString()=="Als.Stance.Crouching",
                quick.GetProperty("hasInput").GetBoolean(),quick.GetProperty("inputYaw").GetDouble(),quick.GetProperty("targetYaw").GetDouble(),quick.GetProperty("actorYaw").GetDouble()));
            for(var i=0;i<count;i++)queue.PlayImmediate(command,"",false);
            var states=row.GetProperty("instances");Assert.Equal(states.GetArrayLength(),bank.Candidate.Length);
            for(var i=0;i<bank.Candidate.Length;i++)
            {
                var actual=bank.Candidate[i];var expected=states[i];created.Add(actual.InstanceId);sources.Add(q.SourcePath(actual.AnimationId));
                Assert.Equal(expected.GetProperty("source").GetString(),q.SourcePath(actual.AnimationId));Assert.Equal(expected.GetProperty("instance").GetInt64(),actual.InstanceId);
                Assert.Equal(expected.GetProperty("rate").GetSingle(),actual.PlayRate);Assert.Equal(expected.GetProperty("playing").GetBoolean(),actual.Playing);
                Compare(actual.Position,expected.GetProperty("position").GetDouble(),ref maxClock,2e-6,"time");Compare(actual.BlendTime,expected.GetProperty("blendTime").GetDouble(),ref maxClock,2e-6,"blendTime");
                Compare(actual.Blend.CurrentWeight,expected.GetProperty("weight").GetDouble(),ref maxWeight,2e-6,"weight");Assert.Equal(expected.GetProperty("desired").GetSingle(),actual.Blend.DesiredWeight);
            }
            queue.Commit(id);bank.Commit(id);frame++;
            void Compare(double a,double e,ref double maximum,double tolerance,string field)
            {var error=Math.Abs(a-e);maximum=Math.Max(maximum,error);Assert.True(error<=tolerance,$"{hz}/{frame}/{field}: {a:R} vs {e:R} error {error:R}");}
        }
        Assert.Equal(hz*5,frame);Assert.Equal(14,created.Count);Assert.Equal(4,sources.Count);Assert.True(mixed>0);Assert.Empty(bank.Committed.ToArray());
        output.WriteLine($"hz={hz} frames={frame} bones={frame*79} mixed={mixed} pose={maxPose:R} curve={maxCurve:R} clock={maxClock:R} weight={maxWeight:R}");
    }
}
