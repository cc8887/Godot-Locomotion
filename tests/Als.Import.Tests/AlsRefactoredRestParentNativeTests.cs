using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredRestParentNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void ActualWorkerRefreshFunctionsMatchContinuousParentState(int hz)
    {
        var catalog=new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        var settings=new AlsRefactoredRestSettings(MantlingHostFixture.Read("refactored_rest_settings"),catalog);
        var callbacks=new AlsRefactoredStanceCallbacks(catalog,false);
        var functions=callbacks.Nodes.ToArray().GroupBy(c=>c.Function.ToString()).ToDictionary(g=>g.Key,g=>g.First());
        var parent=new AlsRefactoredRestParentRuntime(settings,callbacks);
        using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_rest_parent_trace"));var root=doc.RootElement;
        Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32());
        foreach(var hash in root.GetProperty("resourceHashes").EnumerateObject())
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(hash.Name)))),hash.Value.GetString()!.ToUpperInvariant());
        var trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==hz.ToString());
        var frame=0;double maxRate=0,maxDelay=0;var turns=new HashSet<string>();var dynamics=new HashSet<string>();var sides=new HashSet<int>();
        foreach(var row in trace.GetProperty("frames").EnumerateArray())
        {
            var request=row.GetProperty("request");var i=request.GetProperty("input");var state=row.GetProperty("state");var id=new AlsFrameIdentity(frame,19,1);
            var input=new AlsRefactoredRestInput(N("delta"),N("yaw"),N("speed"),B("moving"),B("firstPerson"),(AlsRefactoredRestRotation)i.GetProperty("rotation").GetInt32(),
                (AlsRefactoredRestStance)i.GetProperty("stance").GetInt32(),B("allowed"),B("pending"),N("scale"),N("leftLock"),N("rightLock"),
                V("leftTarget"),V("leftLocation"),V("rightTarget"),V("rightLocation"),B("gameWorld"));
            void Prepare(){parent.Prepare(id,input);foreach(var operation in request.GetProperty("operations").EnumerateArray())parent.Apply(id,functions[operation.GetString()!]);}
            Prepare();var actual=parent.Candidate;
            Assert.Equal(state.GetProperty("left").GetBoolean(),actual.Rotate.Left);Assert.Equal(state.GetProperty("right").GetBoolean(),actual.Rotate.Right);
            if(actual.Rotate.Left)sides.Add(0);if(actual.Rotate.Right)sides.Add(1);
            Compare(actual.Rotate.PlayRate,state.GetProperty("rotateRate").GetDouble(),ref maxRate,"rotateRate");
            Compare(actual.TurnDelay,state.GetProperty("turnDelay").GetDouble(),ref maxDelay,"turnDelay");
            Assert.Equal(state.GetProperty("turnRate").GetSingle(),actual.TurnPlayRate);
            Assert.Equal(state.GetProperty("dynamicDelay").GetInt32(),actual.DynamicFrameDelay);
            Assert.Equal(state.GetProperty("turnSequence").GetString(),actual.QueuedTurn?.Sequence??"");
            Assert.Equal(state.GetProperty("transition").GetString(),actual.QueuedTransition?.Sequence??"");
            if(actual.QueuedTurn is {} turn)
            {
                turns.Add(turn.Sequence);Assert.Equal(state.GetProperty("turnSlot").GetString(),turn.Slot);Assert.Equal(state.GetProperty("turnAssetRate").GetSingle(),turn.PlayRate);
                var rate=state.GetProperty("turnAssetRate").GetSingle();if(state.GetProperty("turnScale").GetBoolean())rate*=MathF.Abs(state.GetProperty("turnYaw").GetSingle()/state.GetProperty("turnAnimatedAngle").GetSingle());
                Assert.Equal(rate,turn.CurvePlayRate);
            }
            if(actual.QueuedTransition is {} dynamic)
            {
                dynamics.Add(dynamic.Sequence);Assert.Equal(state.GetProperty("transitionRate").GetSingle(),dynamic.PlayRate);Assert.Equal(state.GetProperty("transitionStart").GetSingle(),dynamic.StartTime);
                Assert.Equal(state.GetProperty("transitionIn").GetSingle(),dynamic.BlendIn);Assert.Equal(state.GetProperty("transitionOut").GetSingle(),dynamic.BlendOut);
            }
            parent.Cancel();Prepare();Assert.Equal(actual,parent.Candidate);parent.Commit(frame++);
            float N(string name)=>i.GetProperty(name).GetSingle();bool B(string name)=>i.GetProperty(name).GetBoolean();
            AlsDoubleVector V(string name){var v=i.GetProperty(name);return new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());}
            void Compare(double a,double e,ref double max,string name){var error=Math.Abs(a-e);max=Math.Max(max,error);Assert.True(error<=2e-6,$"{hz}/{frame}/{name}: {a:R} vs {e:R}, error={error:R}");}
        }
        Assert.Equal(hz*16,frame);Assert.Equal(8,turns.Count);Assert.Equal(4,dynamics.Count);Assert.Equal(2,sides.Count);
        output.WriteLine($"hz={hz} frames={frame} rate={maxRate:R} delay={maxDelay:R} turns={turns.Count} dynamic={dynamics.Count}");
    }
}
