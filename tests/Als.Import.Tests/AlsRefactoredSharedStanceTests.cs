using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredSharedStanceTests
{
    private static readonly Lazy<AlsRefactoredCharacterActionProfile> Profile = new(() =>
    {
        var standing=AlsRefactoredStandingHostTests.Data.Value;
        var actions=AlsRefactoredCharacterActionTests.Data.Value;
        var catalog=new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        return new(standing,actions.Weapons,actions.Profile.AnimationIds,20,
            MantlingHostFixture.Read("refactored_stance_machines"),new(MantlingHostFixture.Read("refactored_skeleton_curves"),catalog));
    });
    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void SharedStanceCrossfadeAndHiddenParentFrameRetry(int hz)
    {
        var p=Profile.Value;var r=p.CreateRuntime(19,1);var clean=p.CreateRuntime(19,1);
        var counter=new AlsGraphTraversalCounter(0,0);var init=counter;
        var pose=new AlsPrecisePose[p.BoneNames.Length];var curves=new AlsInertialCurve[p.CurveNames.Length];
        var overlap=0;var hidden=0;
        for(var f=0;f<hz*5;f++)
        {
            var id=new AlsFrameIdentity(f,19,1);var context=new AlsPoseUpdateContext(id,1,1f/hz).WithUpdateCounter(counter);
            var input=AlsRefactoredStandingHostTests.Input(f+hz*2,hz);
            var crouching=(f/hz)%2==1;input=input with {Rest=input.Rest with {Stance=crouching?AlsRefactoredRestStance.Crouching:AlsRefactoredRestStance.Standing},
                QuickStop=input.QuickStop with {Crouching=crouching}};
            var weight=(float)(f%hz)/hz;var show=f<hz*4;
            void Prepare(AlsRefactoredCharacterActionRuntime runtime)
            {
                runtime.BeginGlobal(context,input,f==0);
                if(!show){runtime.StopTransitions();return;}
                runtime.Transition.Prepare(context);
                // Both graph instances can be visited during a stance crossfade.
                runtime.Standing.Prepare(context.WithWeight(1-weight),input,init);
                runtime.Crouching!.Prepare(context.WithWeight(weight),input,init);
                runtime.Standing.Evaluate(AlsPrecisePose.Identity);runtime.Crouching.Evaluate(AlsPrecisePose.Identity);
                for(var b=0;b<pose.Length;b++)pose[b]=AlsPrecisePoseBlender.Accumulate(
                    AlsPrecisePoseBlender.Scale(runtime.Standing.Pose[b],1-weight),runtime.Crouching.Pose[b],weight).Normalized();
                Array.Clear(curves);
                for(var c=0;c<runtime.Standing.Curves.Length;c++)curves[p.CurveNames.IndexOf(runtime.Standing.CurveNames[c])]=
                    AlsStandingCycleCurves.Scale(runtime.Standing.Curves[c],1-weight);
                for(var c=0;c<runtime.Crouching.Curves.Length;c++)
                {var i=p.CurveNames.IndexOf(runtime.Crouching.CurveNames[c]);curves[i]=AlsStandingCycleCurves.Accumulate(curves[i],runtime.Crouching.Curves[c],weight);}
                runtime.Transition.Evaluate(pose,curves);
            }
            Prepare(r);var expected=show?r.Transition.Pose.ToArray():[];r.PostUpdateActions();var actions=r.Candidate.ToArray();
            r.Discard();Prepare(r);Prepare(clean);
            if(show){Assert.Equal(expected,r.Transition.Pose.ToArray());Assert.Equal(expected,clean.Transition.Pose.ToArray());overlap++;}else hidden++;
            r.PostUpdateActions();clean.PostUpdateActions();Assert.Equal(actions,r.Candidate.ToArray());Assert.Equal(actions,clean.Candidate.ToArray());
            r.Commit(id);clean.Commit(id);counter=counter.Next((ulong)f+1);
        }
        Assert.Equal(hz*4,overlap);Assert.Equal(hz,hidden);
    }
}
