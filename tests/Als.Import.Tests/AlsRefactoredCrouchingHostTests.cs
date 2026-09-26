using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredCrouchingHostTests
{
    internal static readonly Lazy<AlsRefactoredCrouchingHostProfile> Data=new(()=>
    {
        var shared=AlsRefactoredStandingHostTests.Data.Value;
        return new(shared,MantlingHostFixture.Read("refactored_stance_machines"),new(MantlingHostFixture.Read("refactored_skeleton_curves"),shared.Catalog));
    });
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void RealCrouchingPoseAndActionsAreTransactional(int hz)
    {
        var p=Data.Value;var host=p.CreateRuntime(27,1);var clean=p.CreateRuntime(27,1);
        var initialization=new AlsGraphTraversalCounter(0,0);var counter=initialization;
        var states=new HashSet<int>();var moved=false;var turned=false;
        for(var frame=0;frame<hz*11;frame++)
        {
            var id=new AlsFrameIdentity(frame,27,1);var context=new AlsPoseUpdateContext(id,1,1f/hz).WithUpdateCounter(counter);
            var source=AlsRefactoredStandingHostTests.Input(frame,hz);
            var input=source with{Rest=source.Rest with{Stance=AlsRefactoredRestStance.Crouching},QuickStop=source.QuickStop with{Crouching=true}};
            void Prepare(AlsRefactoredCrouchingHost h){h.Prepare(context,input,initialization,frame==0);h.Evaluate(AlsPrecisePose.Identity);}
            Prepare(host);states.Add(host.State);var pose=host.Pose.ToArray();var curves=host.Curves.ToArray();
            foreach(var bone in pose)bone.Validate(.001);
            Assert.Equal(new AlsInertialCurve(1),curves[p.CurveNames.IndexOf("PoseCrouching")]);
            moved|=curves[p.CurveNames.IndexOf("PoseMoving")].Value>.9f;
            turned|=host.MontageFrame.Evaluations.Length>0;
            host.Evaluate(AlsPrecisePose.Identity);Assert.Equal(pose,host.Pose.ToArray());Assert.Equal(curves,host.Curves.ToArray());
            host.PostUpdateActions();host.Cancel();Prepare(host);Prepare(clean);
            Assert.Equal(pose,host.Pose.ToArray());Assert.Equal(clean.Pose.ToArray(),host.Pose.ToArray());Assert.Equal(clean.Curves.ToArray(),host.Curves.ToArray());
            host.PostUpdateActions();clean.PostUpdateActions();host.ValidateCommit(id);clean.ValidateCommit(id);host.Commit(id);clean.Commit(id);
            counter=counter.Next((ulong)frame+1);
        }
        Assert.Equal(5,states.Count);Assert.True(moved);Assert.True(turned);
    }
}
