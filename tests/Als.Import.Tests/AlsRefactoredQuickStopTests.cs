using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredQuickStopTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog=new(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        public readonly AlsRefactoredStandingResources Standing;
        public readonly AlsRefactoredQuickStop Quick;
        public Fixture()
        {
            Standing=new(MantlingHostFixture.Read("refactored_stance_machines"),Catalog);
            var settings=new AlsRefactoredRestSettings(MantlingHostFixture.Read("refactored_rest_settings"),Catalog);
            var paths=settings.Turns.ToArray().Select(t=>t.Sequence).Concat(Enumerable.Range(0,4).Select(i=>settings.DynamicSequence(i>=2,i%2==0))).ToArray();
            var rest=new AlsRefactoredRestMontages(Catalog,settings,MantlingHostFixture.Read("refactored_slot_inventory"),paths.Select((p,i)=>(p,i)).ToDictionary(v=>v.p,v=>v.i),5);
            using var doc=JsonDocument.Parse(MantlingHostFixture.Read("refactored_quick_stop_settings"));
            var ids=doc.RootElement.GetProperty("sequences").EnumerateObject().Select((p,i)=>(p,i)).ToDictionary(v=>v.p.Value.GetString()!,v=>v.i+70);
            Quick=new(MantlingHostFixture.Read("refactored_quick_stop_settings"),Catalog,Standing,rest,ids);
        }
    }
    private static readonly Lazy<Fixture> Data=new(()=>new());
    [Theory]
    [InlineData(0,0,1.75f)] [InlineData(90,1,2.375f)] [InlineData(-90,0,2.375f)]
    [InlineData(175,1,2.9652777f)] [InlineData(176,0,3.0277779f)] [InlineData(180,0,3)]
    [InlineData(-180,0,3)] [InlineData(540,0,3)] [InlineData(-540,0,3)]
    public void OriginalAngleRemapAndUnclampedRate(double angle,int side,float rate)
    {
        var q=Data.Value.Quick;var result=q.Command(new(true,false,true,angle,42,0));
        Assert.Equal(q.Assets[side].AnimationId,result.AnimationId);Assert.Equal(rate,result.PlayRate);
        var target=q.Command(new(true,false,false,42,angle+30,30));Assert.Equal(result,target);
        var view=q.Command(new(false,false,true,angle,42,0));Assert.Equal(q.Assets[0].AnimationId,view.AnimationId);Assert.Equal(q.MinRate,view.PlayRate);
    }
    [Fact]
    public void WorldAnglesSubtractBeforeFloatConversionAndInvalidValuesCannotPlay()
    {
        var q=Data.Value.Quick;
        Assert.Equal(q.Command(new(true,false,true,90,0,0)),q.Command(new(true,false,true,1000000090,0,1000000000)));
        Assert.Equal(q.Command(new(true,false,true,176,0,0)).AnimationId,q.Assets[0].AnimationId);
        Assert.True(q.Command(new(true,false,true,176,0,0)).PlayRate>q.MaxRate);
        Assert.Throws<ArgumentException>(()=>q.Command(new(true,false,true,double.NaN,0,0)));
        Assert.Throws<ArgumentException>(()=>q.Command(new(true,false,true,double.MaxValue,0,0)));
    }
    [Theory]
    [InlineData(false,false)] [InlineData(false,true)] [InlineData(true,false)] [InlineData(true,true)]
    public void ActualQuickStopNotifyPlaysOriginalAssetAndReturnsToBase(bool crouching,bool right)
    {
        var f=Data.Value;var q=f.Quick;var bank=new AlsMontageRuntime([],sequences:q.Assets);var queue=new AlsTransitionQueueRuntime(bank,9,1);
        var machine=new AlsRefactoredStandingRuntime(f.Standing);var counter=new AlsGraphTraversalCounter(0,0);
        AlsRefactoredStandingObservation[] clocks=[new(12,0,0,false),new(9,0,0,false)];
        machine.Prepare(0,new(false,false,false),clocks,.01f,updateCounter:counter);machine.Commit(0);counter=counter.Next(1);
        machine.Prepare(1,new(true,false,false),clocks,.01f,updateCounter:counter);machine.Commit(1);counter=counter.Next(2);
        var id=new AlsFrameIdentity(2,9,1);var input=new AlsRefactoredQuickStopInput(true,crouching,true,right?90:-90,0,0);
        void Prepare(){queue.Begin(id);bank.Begin(id,0);machine.Prepare(2,new(false,false,false),clocks,.01f,updateCounter:counter);}
        Prepare();Assert.Equal(1,machine.Candidate.EventCount);Assert.Equal(1,q.Dispatch(queue,bank,id,machine,input));
        var instances=bank.Candidate.ToArray();var instance=Assert.Single(instances);Assert.Equal(q.Command(input).AnimationId,instance.AnimationId);
        Assert.Equal(q.StartTime,instance.Position);Assert.Equal(2.375f,instance.PlayRate);Assert.Null(queue.Candidate.Play);Assert.Empty(bank.Evaluation.ToArray());
        queue.Discard();bank.Discard();machine.Cancel();Prepare();q.Dispatch(queue,bank,id,machine,input);Assert.Equal(instances,bank.Candidate.ToArray());
        queue.Commit(id);bank.Commit(id);machine.Commit(2);
        var sources=q.Assets.ToArray().Select(a=>f.Catalog.CompileAdditivePose(q.SourcePath(a.AnimationId))).ToArray();
        var rest=new AlsRefactoredStandingRestPose(f.Catalog,new(f.Catalog),new(MantlingHostFixture.Read("refactored_skeleton_curves"),f.Catalog),sources.SelectMany(s=>s.CurveNames.ToArray()).Distinct(StringComparer.OrdinalIgnoreCase));
        var poseProfile=new AlsRefactoredTransitionPose(f.Catalog,q,rest.BoneNames,rest.Parents,rest.CurveNames);var sampler=poseProfile.CreateSampler();
        var reference=f.Catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence);
        var mixer=new AlsMontageSlotPose(reference.ReferencePose,rest.Parents,rest.CurveNames.Length);
        var basis=new AlsPrecisePose[79];var baseCurves=new AlsInertialCurve[rest.CurveNames.Length];rest.SampleIdleSource(basis,baseCurves);
        var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[baseCurves.Length];var changed=false;
        for(var frame=3;frame<123;frame++)
        {
            id=new(frame,9,1);bank.Begin(id,1f/60);mixer.Evaluate(bank.Frame,id,AlsMontageSlot.Transition,basis,baseCurves,pose,curves,sampler);
            changed|=!basis.SequenceEqual(pose);var expected=pose.ToArray();var expectedCurves=curves.ToArray();
            bank.Discard();bank.Begin(id,1f/60);mixer.Evaluate(bank.Frame,id,AlsMontageSlot.Transition,basis,baseCurves,pose,curves,sampler);
            Assert.Equal(expected,pose);Assert.Equal(expectedCurves,curves);bank.Commit(id);
        }
        Assert.True(changed);Assert.Empty(bank.Committed.ToArray());Assert.Equal(basis,pose);Assert.Equal(baseCurves,curves);
    }
}
