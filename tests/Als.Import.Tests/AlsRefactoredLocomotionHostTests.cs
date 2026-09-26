using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLocomotionHostTests
{
    internal static readonly Lazy<AlsRefactoredLocomotionHostProfile> Data=new(()=>
    {
        var ground=AlsRefactoredGroundedHostTests.Data.Value;
        var air=new AlsRefactoredAirSettings(MantlingHostFixture.Read("refactored_air_settings"),ground.Standing.Catalog,ground.Standing.MovementSettings);
        return new(ground,air,MantlingHostFixture.Read("refactored_locomotion_machines"),AlsRefactoredLocomotionPoseTests.Data.Value);
    });
    [Fact]
    public void OriginalAirSettingsAndLandingCallbackLoad()
    {
        var p=Data.Value;Assert.Equal(5,p.Air.Lean.Keys.Length);Assert.Equal(.2f,p.Air.LeanHalfLife);
        Assert.Equal(1.4f,p.LandStanding.PlayRate);Assert.Equal(.1f,p.LandStanding.BlendInTime);Assert.Equal(.2f,p.LandStanding.BlendOutTime);
        Assert.NotEqual(p.LandStanding.AnimationId,p.LandCrouching.AnimationId);
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void CharacterTransactionCommitsAirParentAndLandingActionsTogether(int hz)
    {
        var p=Data.Value;var host=p.CreateRuntime(4,1);var clean=p.CreateRuntime(4,1);var counter=new AlsGraphTraversalCounter(0,0);
        var states=new HashSet<int>();var landed=false;var priorStand=1f;var priorCrouch=0f;
        for(var f=0;f<hz*12;f++)
        {
            var t=f/(float)hz;var cycle=(int)(t/4);var local=t%4;var inAir=local is >=.5f and <2.5f;
            var ground=AlsRefactoredStandingHostTests.Input(0,hz);var crouch=cycle==2;
            ground=ground with{Movement=ground.Movement with{PendingUpdate=f==0,Velocity=new(250,50,inAir?400-(local-.5f)*850:0),Speed=255},
                Rest=ground.Rest with{PendingUpdate=f==0,Stance=crouch?AlsRefactoredRestStance.Crouching:AlsRefactoredRestStance.Standing},
                FootPlanted=cycle==0?-1:1};
            var input=new AlsRefactoredLocomotionHostInput(ground,inAir?AlsRefactoredLocomotionMode.InAir:AlsRefactoredLocomotionMode.Grounded,
                f% (hz*4)==hz/2,cycle==1,false,inAir&&local>2?.6f:0,priorStand,priorCrouch);
            var id=new AlsFrameIdentity(f,4,1);var context=new AlsPoseUpdateContext(id,1,1f/hz).WithUpdateCounter(counter);
            void Prepare(AlsRefactoredLocomotionHost h)
            {h.BeginGlobal(context,input,new(0,0),f==0);h.Prepare(context,f==0);h.Evaluate(AlsPrecisePose.Identity);}
            Prepare(host);var pose=host.Pose.ToArray();var curves=host.Curves.ToArray();var air=host.Air;var movement=host.Movement;
            states.Add(host.Graph.MainUpdate.State.CurrentState);host.PostUpdate();var actions=host.Actions.Candidate.ToArray();
            landed|=actions.Any(a=>a.AnimationId==p.LandStanding.AnimationId);
            host.Discard();Prepare(host);Prepare(clean);
            Assert.Equal(pose,host.Pose.ToArray());Assert.Equal(curves,host.Curves.ToArray());Assert.Equal(air,host.Air);Assert.Equal(movement,host.Movement);
            Assert.Equal(clean.Pose.ToArray(),pose);Assert.Equal(clean.Air,air);host.PostUpdate();clean.PostUpdate();
            Assert.Equal(actions,host.Actions.Candidate.ToArray());Assert.Equal(clean.Actions.Candidate.ToArray(),actions);
            priorStand=curves[p.Pose.CurveNames.IndexOf("PoseStanding")].Value;priorCrouch=curves[p.Pose.CurveNames.IndexOf("PoseCrouching")].Value;
            host.ValidateCommit(id);clean.ValidateCommit(id);host.Commit(id);clean.Commit(id);counter=counter.Next((ulong)f+1);
        }
        Assert.Equal(new[]{0,2,3,4},states.Order());Assert.True(landed);
    }

    [Fact]
    public void HiddenJumpLatchAndAirToGroundLeanShareParentTransaction()
    {
        var p=Data.Value;var host=p.CreateRuntime(4,1);var counter=new AlsGraphTraversalCounter(0,0);
        for(var f=0;f<5;f++)
        {
            var ground=AlsRefactoredStandingHostTests.Input(0,60);
            ground=ground with{Movement=ground.Movement with{PendingUpdate=f==0,Speed=600,Velocity=new(350,175,-700)}};
            var input=new AlsRefactoredLocomotionHostInput(ground,AlsRefactoredLocomotionMode.InAir,f is 0 or 1,false,false,.4f,1,0);
            var id=new AlsFrameIdentity(f,4,1);var context=new AlsPoseUpdateContext(id,1,1f/60).WithUpdateCounter(counter);
            host.BeginGlobal(context,input,new(0,0),f==0);
            if(f==0)Assert.False(host.Air.Jumped);
            if(f is 1 or 2)Assert.True(host.Air.Jumped);
            if(f>=3)
            {
                host.Prepare(context);Assert.False(host.Air.Jumped);Assert.Equal(1.5f,host.Air.JumpRate);
                Assert.Equal(-700,host.Air.VerticalVelocity);Assert.Equal(.4f,host.Air.Prediction);Assert.NotEqual(default,host.Movement.Lean);
                if(f==3)
                {
                    Assert.ThrowsAny<ArgumentException>(()=>host.Evaluate(new(new(double.NaN,0,0),AlsQuaternion.Identity,new(1,1,1))));
                    Assert.ThrowsAny<Exception>(()=>host.Commit(id));
                    host.BeginGlobal(context,input,new(0,0));host.Prepare(context);
                }
            }
            host.PostUpdate();host.ValidateCommit(id);host.Commit(id);counter=counter.Next((ulong)f+1);
        }
    }
}
