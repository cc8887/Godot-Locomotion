using System.Numerics;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredSourcePlayerTests
{
    [Fact]
    public void InstanceResetIsAtomicAndHiddenPlayersResetOnTheirNextTick()
    {
        var r=Resources();const string walk="/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward";
        var runtime=new AlsRefactoredSourcePlayerRuntime(r.Catalog,r.Bank,r.Profiles,[new(0,walk,-1,.2f),new(1,walk,-1,.6f)]);
        AlsRefactoredSourcePlayerInput[] both=[new(0,default,1,1),new(1,default,1,1)];
        runtime.Prepare(0,both,.1f);var before=runtime.Players.ToArray();runtime.Commit(0);
        runtime.Prepare(1,both[..1],0,true);Assert.Equal(.2f,runtime.Players[0].Time);Assert.Equal(before[0].Epoch+1,runtime.Players[0].Epoch);runtime.Cancel();
        runtime.Prepare(1,both,0);Assert.Equal(before.Select(p=>p.Time),runtime.Players.ToArray().Select(p=>p.Time));runtime.Cancel();
        runtime.Prepare(1,both[..1],0,true);runtime.Commit(1);
        runtime.Prepare(2,both[1..],0);Assert.Equal(.6f,runtime.Players[0].Time);Assert.Equal(before[1].Epoch+1,runtime.Players[0].Epoch);runtime.Commit(2);
        runtime.Prepare(3,[],0,true);runtime.Commit(3);
        runtime.Prepare(4,both[1..],0);Assert.Equal(.6f,runtime.Players[0].Time);Assert.Equal(before[1].Epoch+2,runtime.Players[0].Epoch);runtime.Cancel();
    }
    private static (AlsRefactoredAnimationCatalog Catalog,AlsRefactoredSyncBank Bank,IReadOnlyDictionary<string,AlsRefactoredTriangulationProfile> Profiles) Resources()
    {
        var json=MantlingHostFixture.Read("refactored_animation_sources");
        byte[] Read(string p)=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p));
        var catalog=new AlsRefactoredAnimationCatalog(json,Read);
        return(catalog,new(MantlingHostFixture.Read("refactored_sync_inputs"),catalog),
            AlsRefactoredTriangulationCompiler.Compile(MantlingHostFixture.Read("refactored_triangulation_inputs"),json,Read));
    }
    [Fact]
    public void DuplicateAssetsKeepSeparateClocksAndSequenceAdditivePosesMatchResources()
    {
        var r=Resources();const string walk="/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward";
        const string land="/ALS/ALS/Animations/Air/Land/A_Als_Land_Heavy_Additive.A_Als_Land_Heavy_Additive";
        var runtime=new AlsRefactoredSourcePlayerRuntime(r.Catalog,r.Bank,r.Profiles,[new(0,walk,-1,.1f),new(1,walk,-1,.6f),new(2,land,-1)]);
        var absolute=r.Catalog.CompileAbsolutePoseWithCurves(walk);var additive=r.Catalog.CompileAdditivePose(land);
        var a=absolute.Pose.CreateSampler(absolute.Curves);var b=additive.CreateSampler();
        var pose=new AlsPrecisePose[79];var curves=new AlsInertialCurve[absolute.Curves.Names.Length];var addCurves=new AlsInertialCurve[additive.CurveNames.Length];
        AlsRefactoredSourcePlayerInput[] input=[new(1,default,1,1),new(2,default,1,1),new(0,default,1,1)];
        for(var frame=0;frame<90;frame++)
        {
            runtime.Prepare(frame,input,1f/60);
            Assert.Empty(runtime.Groups.ToArray());Assert.Equal(3,runtime.Players.Length);
            Assert.Equal(runtime.Ticks[0].AssetId,runtime.Ticks[2].AssetId);Assert.NotEqual(runtime.Ticks[0].PlayerId,runtime.Ticks[2].PlayerId);
            Assert.Equal(3,runtime.Samples.ToArray().Select(s=>s.SampleId).Distinct().Count());
            Assert.True(MathF.Abs(runtime.Players.ToArray().Single(p=>p.PlayerId==0).Time-runtime.Players.ToArray().Single(p=>p.PlayerId==1).Time)>.1f);
            for(var player=0;player<3;player++)
            {
                var time=runtime.Players.ToArray().Single(p=>p.PlayerId==player).Time;runtime.Evaluate(frame,player);
                if(player<2){a.Sample(time,true,false,false,pose,curves);Assert.True(curves.AsSpan().SequenceEqual(runtime.Curves(player)));}
                else {b.Sample(time,pose,addCurves);Assert.True(addCurves.AsSpan().SequenceEqual(runtime.Curves(player)));}
                Assert.True(pose.AsSpan().SequenceEqual(runtime.Pose(player)));
            }
            runtime.Commit(frame);
            Assert.Throws<InvalidOperationException>(()=>{_=runtime.Pose(0).Length;});
        }
    }
    [Fact]
    public void LatePrepareFailureAndCancelPreserveAllGroupsAndRecoverWithoutClockLeak()
    {
        var r=Resources();const string prefix="/ALS/ALS/Animations/Grounded/WalkRun/";
        AlsRefactoredSourcePlayerDefinition[] definitions=[new(0,prefix+"BS_Als_WalkRun_Forward.BS_Als_WalkRun_Forward",0),
            new(1,prefix+"BS_Als_WalkRun_Backward.BS_Als_WalkRun_Backward",1),new(2,prefix+"A_Als_Walk_Forward.A_Als_Walk_Forward",-1)];
        var clean=new AlsRefactoredSourcePlayerRuntime(r.Catalog,r.Bank,r.Profiles,definitions);
        var retry=new AlsRefactoredSourcePlayerRuntime(r.Catalog,r.Bank,r.Profiles,definitions);
        for(var frame=0;frame<120;frame++)
        {
            AlsRefactoredSourcePlayerInput[] input=[new(2,default,1,1),new(0,new(.73f,.37f),1,1),new(1,new(.83f,.67f),1,1)];
            var before=Enumerable.Range(0,3).Select(retry.CommittedTime).ToArray();
            var broken=input.ToArray();broken[^1]=broken[^1] with {PlayRate=float.NaN};
            Assert.Throws<ArgumentException>(()=>retry.Prepare(frame,broken,1f/60));
            Assert.Throws<ArgumentException>(()=>retry.ValidateCommit(frame));
            Assert.Equal(before,Enumerable.Range(0,3).Select(retry.CommittedTime));
            clean.Prepare(frame,input,1f/60);retry.Prepare(frame,input,1f/60);
            retry.Evaluate(frame,0);retry.Cancel();retry.Prepare(frame,input,1f/60);
            Assert.True(clean.Players.SequenceEqual(retry.Players));Assert.True(clean.Samples.SequenceEqual(retry.Samples));Assert.True(clean.Groups.SequenceEqual(retry.Groups));
            Assert.Equal(2,retry.Groups.Length);Assert.Equal(2,retry.Players[^1].PlayerId);
            for(var id=0;id<3;id++)
            {clean.Evaluate(frame,id);retry.Evaluate(frame,id);Assert.True(clean.Pose(id).SequenceEqual(retry.Pose(id)));Assert.True(clean.Curves(id).SequenceEqual(retry.Curves(id)));}
            clean.Commit(frame);retry.Commit(frame);
        }
    }
}
