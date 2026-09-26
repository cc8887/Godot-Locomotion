using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredGroundedHostTests
{
    internal static readonly Lazy<AlsRefactoredCharacterActionProfile> Data=new(()=>
    {
        var fixture=AlsRefactoredCharacterActionTests.Data.Value;var standing=AlsRefactoredStandingHostTests.Data.Value;
        return new(standing,fixture.Weapons,fixture.Profile.AnimationIds,20,MantlingHostFixture.Read("refactored_stance_machines"),
            new(MantlingHostFixture.Read("refactored_skeleton_curves"),standing.Catalog),MantlingHostFixture.Read("refactored_locomotion_machines"));
    });
    [Fact]
    public void OriginalGroundedPoseClosureLoadsAndSamplesRoll()
    {
        var p=Data.Value;Assert.NotNull(p.Grounded);Assert.Equal(79,p.Grounded!.Roll.Length);
        foreach(var bone in p.Grounded.Roll)bone.Validate(.001);
        Assert.Equal(1.5f,p.Grounded.RollToStanding.PlayRate);
        Assert.Equal(.2f,p.Grounded.RollToStanding.StartTime);
        Assert.NotEqual(p.Grounded.RollToStanding.AnimationId,p.Grounded.RollToCrouching.AnimationId);
        _=p.CreateRuntime(7,1);
    }
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void RealGroundedSourcesAndStancesCommitAndRetryTogether(int hz)
    {
        var p=Data.Value;var host=p.CreateRuntime(7,1);var clean=p.CreateRuntime(7,1);
        var counter=new AlsGraphTraversalCounter(0,0);var init=counter;
        var states=new HashSet<int>();var priorStand=1f;var priorCrouch=0f;var resets=0;var automatic=0;
        for(var f=0;f<hz*8;f++)
        {
            var t=f/(float)hz;var id=new AlsFrameIdentity(f,7,1);var context=new AlsPoseUpdateContext(id,1,1f/hz).WithUpdateCounter(counter);
            var source=AlsRefactoredStandingHostTests.Input(0,hz);var crouch=t is >=1 and <3 or >=4 and <4.6f or >=5 and <6;
            var input=source with{MovingSmooth=t is >=3 and <5,Rest=source.Rest with{Stance=crouch?AlsRefactoredRestStance.Crouching:AlsRefactoredRestStance.Standing},QuickStop=source.QuickStop with{Crouching=crouch}};
            var initialize=f==hz*7;var roll=initialize;
            void Prepare(AlsRefactoredCharacterActionRuntime r)
            {
                r.BeginGlobal(context,input,f==0);
                r.Transition.Prepare(context,initialize);
                var update=r.Transition.SourceUpdate;
                if(update.Updated){r.Grounded!.Prepare(update.Context,input,priorStand,priorCrouch,roll,init,initialize);r.Grounded.Evaluate(AlsPrecisePose.Identity);}
                r.Transition.Evaluate(update.Updated?r.Grounded!.Pose:[],update.Updated?r.Grounded!.Curves:[]);
            }
            Prepare(host);var pose=host.Transition.Pose.ToArray();var curves=host.Transition.Curves.ToArray();var clocks=host.Grounded!.Observations.ToArray();
            states.Add(host.Grounded.State);resets+=host.Grounded.ResetEntryMode?1:0;
            automatic+=clocks.Count(c=>c.CachedWeight>0&&c.Time>0);
            foreach(var bone in pose)bone.Validate(.001);
            Assert.Equal(new AlsInertialCurve(1),host.Grounded.Curves[p.CurveNames.IndexOf("PoseGrounded")]);
            host.PostUpdateActions();host.Discard();Prepare(host);Prepare(clean);
            Assert.Equal(pose,host.Transition.Pose.ToArray());Assert.Equal(curves,host.Transition.Curves.ToArray());
            Assert.Equal(clean.Transition.Pose.ToArray(),pose);Assert.Equal(clean.Grounded!.Observations.ToArray(),clocks);
            host.PostUpdateActions();clean.PostUpdateActions();host.ValidateCommit(id);clean.ValidateCommit(id);
            priorStand=curves[p.CurveNames.IndexOf("PoseStanding")].Value;priorCrouch=curves[p.CurveNames.IndexOf("PoseCrouching")].Value;
            host.Commit(id);clean.Commit(id);counter=counter.Next((ulong)f+1);
        }
        Assert.Equal(new[]{1,2,3,4,5},states.Order());Assert.True(resets>4);Assert.True(automatic>hz);
    }

    [Fact]
    public void HiddenFramesUpdateOnlyAndLateFailurePreserveAtomicOwnership()
    {
        var p=Data.Value;var sparse=p.CreateRuntime(9,2);var full=p.CreateRuntime(9,2);
        var counter=new AlsGraphTraversalCounter(0,0);var init=counter;
        for(var f=0;f<100;f++)
        {
            var id=new AlsFrameIdentity(f,9,2);var context=new AlsPoseUpdateContext(id,1,1f/60).WithUpdateCounter(counter);
            var source=AlsRefactoredStandingHostTests.Input(0,60);var crouch=f is >=10 and <65;
            var input=source with{MovingSmooth=false,Rest=source.Rest with{Stance=crouch?AlsRefactoredRestStance.Crouching:AlsRefactoredRestStance.Standing},QuickStop=source.QuickStop with{Crouching=crouch}};
            var hidden=f is >=30 and <45;
            void Prepare(AlsRefactoredCharacterActionRuntime r)
            {
                r.BeginGlobal(context,input,f==0);
                if(!hidden){r.Transition.Prepare(context);r.Grounded!.Prepare(r.Transition.SourceUpdate.Context,input,crouch?0:1,crouch?1:0,false,init);}
            }
            void Evaluate(AlsRefactoredCharacterActionRuntime r)
            {r.Grounded!.Evaluate(AlsPrecisePose.Identity);r.Transition.Evaluate(r.Grounded.Pose,r.Grounded.Curves);}
            Prepare(sparse);Prepare(full);
            if(!hidden)
            {
                Assert.Equal(full.Grounded!.State,sparse.Grounded!.State);
                Assert.Equal(full.Grounded.Observations.ToArray(),sparse.Grounded.Observations.ToArray());
                if(f==45)Assert.True(sparse.Grounded.ResetEntryMode);
                if(f==70)
                {
                    Assert.ThrowsAny<ArgumentException>(()=>sparse.Grounded.Evaluate(new(new(double.NaN,0,0),AlsQuaternion.Identity,new(1,1,1))));
                    Assert.Throws<InvalidOperationException>(()=>sparse.Frame);Prepare(sparse);
                }
                if(f%4==0)Evaluate(sparse);else Assert.Throws<InvalidOperationException>(()=>sparse.Grounded.Pose.Length);
                Evaluate(full);
            }
            sparse.PostUpdateActions();full.PostUpdateActions();
            if(!hidden)Assert.Throws<ArgumentException>(()=>sparse.Grounded!.Evaluate(AlsPrecisePose.Identity));
            sparse.ValidateCommit(id);full.ValidateCommit(id);
            sparse.Commit(id);full.Commit(id);Assert.Equal(id,sparse.CommittedIdentity);counter=counter.Next((ulong)f+1);
        }
    }
}
