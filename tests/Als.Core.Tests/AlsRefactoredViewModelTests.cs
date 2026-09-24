using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRefactoredViewModelTests
{
    // Header defaults for unit scenarios only; production must bind the actual settings asset.
    private static readonly AlsRefactoredHeadSettings Settings=new(.1f,.1f,.2f,.01f,.01f);
    private static AlsRefactoredViewInput Input=>new(60,30,0,0,0,0,0,0,false,false,
        AlsRotationMode.LookingDirection,false,false,false,0,1f/60,1f/60,0,0);

    [Fact]
    public void ActionFreezesRelativeAnglesButNotBlendCurvesOrSpinePermission()
    {
        var a=AlsRefactoredViewModel.RefreshView(Input,AlsRefactoredViewState.Initial,AlsRefactoredSpineState.Initial);
        Assert.Equal(60,a.View.YawAngle);Assert.Equal(30,a.View.PitchAngle);Assert.Equal(.5f-30f/180,a.View.PitchAmount);
        var b=AlsRefactoredViewModel.RefreshView(Input with {HasAction=true,ViewYaw=-130,ViewPitch=-70,
            RotationMode=AlsRotationMode.Aiming,PendingUpdate=true,ViewBlock=.25f,PoseAiming=.8f},a.View,a.Spine);
        Assert.Equal(a.View.YawAngle,b.View.YawAngle);Assert.Equal(a.View.PitchAmount,b.View.PitchAmount);
        Assert.Equal(.75f*(1-.8f),b.View.HeadBlendAmount);Assert.True(b.Spine.Allowed);
        Assert.Equal(1,b.Spine.Amount);Assert.Equal(60*.75f*.8f,b.Spine.FinalYaw,4);
    }

    [Fact]
    public void PermissionReversalAtZeroDeltaPreservesCurrentSpineYaw()
    {
        var active=Input with {RotationMode=AlsRotationMode.Aiming,PoseAiming=1};
        var a=AlsRefactoredViewModel.RefreshView(active,AlsRefactoredViewState.Initial,AlsRefactoredSpineState.Initial);
        Assert.InRange(a.Spine.Amount,.01f,.99f);
        var b=AlsRefactoredViewModel.RefreshView(active with {RotationMode=AlsRotationMode.LookingDirection,Delta=0},a.View,a.Spine);
        Assert.Equal(a.Spine.Yaw,b.Spine.Yaw,4);
        var c=AlsRefactoredViewModel.RefreshView(active with {Delta=0},b.View,b.Spine);
        Assert.Equal(b.Spine.Yaw,c.Spine.Yaw,4);
    }

    [Fact]
    public void ReleasedSpineFollowsMovingBaseAndCapsWorldOffset()
    {
        var initial=AlsRefactoredViewModel.RefreshView(Input with {RotationMode=AlsRotationMode.Aiming,PendingUpdate=true,PoseAiming=1},
            AlsRefactoredViewState.Initial,AlsRefactoredSpineState.Initial);
        var released=AlsRefactoredViewModel.RefreshView(Input with {PoseAiming=1,Delta=0},initial.View,initial.Spine);
        var stationary=AlsRefactoredViewModel.RefreshView(Input with {CharacterYaw=100,Delta=0,PoseAiming=1},released.View,released.Spine);
        Assert.Equal(70,stationary.Spine.LastWorldYaw);Assert.Equal(30,stationary.Spine.Yaw);
        var moving=AlsRefactoredViewModel.RefreshView(Input with {CharacterYaw=100,Delta=0,PoseAiming=1,RelativeBaseRotation=true,BaseDeltaYaw=100},released.View,released.Spine);
        Assert.Equal(100,moving.Spine.LastWorldYaw);Assert.Equal(60,moving.Spine.Yaw);
        var fast=AlsRefactoredViewModel.RefreshView(Input with {ViewYawSpeed=200},released.View,released.Spine);
        var slow=AlsRefactoredViewModel.RefreshView(Input,released.View,released.Spine);
        Assert.True(fast.Spine.Amount<slow.Spine.Amount);
    }

    [Fact]
    public void FirstPersonEnablesSpineWithoutAimingAndCurvesClampIndependently()
    {
        var a=AlsRefactoredViewModel.RefreshView(Input with {FirstPerson=true,PendingUpdate=true,ViewBlock=-1,PoseAiming=2},
            AlsRefactoredViewState.Initial,AlsRefactoredSpineState.Initial);
        Assert.True(a.Spine.Allowed);Assert.Equal(60,a.Spine.FinalYaw);Assert.Equal(0,a.View.HeadBlendAmount);
        var b=AlsRefactoredViewModel.RefreshView(Input with {ViewBlock=2,PoseAiming=-1},a.View,a.Spine);
        Assert.Equal(0,b.Spine.FinalYaw);Assert.Equal(0,b.View.HeadBlendAmount);
    }

    [Fact]
    public void ScalarAngleBlendKeeps175ButRemapsLargerPositiveAnglesCounterClockwise()
    {
        var input=Input with {RotationMode=AlsRotationMode.Aiming,PendingUpdate=true,PoseAiming=.5f,ViewYaw=175};
        var a=AlsRefactoredViewModel.RefreshView(input,AlsRefactoredViewState.Initial,AlsRefactoredSpineState.Initial);
        var b=AlsRefactoredViewModel.RefreshView(input with {ViewYaw=176},AlsRefactoredViewState.Initial,AlsRefactoredSpineState.Initial);
        Assert.Equal(87.5f,a.Spine.FinalYaw);Assert.Equal(-92,b.Spine.FinalYaw);
    }

    [Fact]
    public void HeadSwitchSideHysteresisDependsOnInputAndClearsNearTarget()
    {
        var head=AlsRefactoredHeadState.Initial with {InitializationRequired=false,Yaw=120};
        var view=new AlsRefactoredViewState(-170,20,.5f,1);
        var a=AlsRefactoredViewModel.RefreshHead(Input,view,head,Settings);
        Assert.True(a.SwitchingSides);Assert.True(a.Yaw>120);
        var b=AlsRefactoredViewModel.RefreshHead(Input with {HasInput=true},view,head,Settings);
        Assert.False(b.SwitchingSides);Assert.True(b.Yaw>120);
        var c=AlsRefactoredViewModel.RefreshHead(Input,view with {YawAngle=a.Yaw},a,Settings);
        Assert.False(c.SwitchingSides);
    }

    [Fact]
    public void FirstPersonUsesRealTimeForPitchButGameTimeForYawSpring()
    {
        var head=AlsRefactoredHeadState.Initial with {InitializationRequired=false};
        var view=new AlsRefactoredViewState(60,30,.5f,1);
        var a=AlsRefactoredViewModel.RefreshHead(Input with {FirstPerson=true,Delta=0,RealDelta=.1f},view,head,Settings);
        Assert.True(a.Pitch>25);Assert.Equal(0,a.Yaw);Assert.Equal(0,a.YawVelocity);
        var b=AlsRefactoredViewModel.RefreshHead(Input with {Delta=0,RealDelta=.1f},view,head,Settings);
        Assert.Equal(0,b.Pitch);Assert.Equal(0,b.Yaw);
    }

    [Fact]
    public void VelocityLookFavorsBodyTurnAndRetainsSideNear180()
    {
        var input=Input with {RotationMode=AlsRotationMode.VelocityDirection,HasInput=true,InputYaw=179};
        var head=AlsRefactoredHeadState.Initial with {Yaw=40};
        var a=AlsRefactoredViewModel.RefreshHead(input,AlsRefactoredViewState.Initial,head,Settings);
        Assert.Equal(40,a.Yaw);Assert.Equal(0,a.Pitch);
        var b=AlsRefactoredViewModel.RefreshHead(input with {CharacterYawVelocity=-21},AlsRefactoredViewState.Initial,head,Settings);
        Assert.Equal(-179,b.Yaw);
        var c=AlsRefactoredViewModel.RefreshHead(input with {HasInput=false,TargetYaw=-45},AlsRefactoredViewState.Initial,head,Settings);
        Assert.Equal(-45,c.Yaw);
    }

    [Fact]
    public void InitializationIsExplicitAndZeroHalfLifeSnaps()
    {
        var head=AlsRefactoredHeadState.Initial with {InitializationRequired=false,Yaw=45,YawVelocity=80};
        var flagged=AlsRefactoredViewModel.InitializeHead(head);Assert.Equal(45,flagged.Yaw);Assert.True(flagged.InitializationRequired);
        var a=AlsRefactoredViewModel.RefreshHead(Input,new(180,30,.5f,1),flagged,Settings);
        Assert.Equal(175,a.Yaw);Assert.Equal(0,a.YawVelocity);Assert.False(a.InitializationRequired);
        var b=AlsRefactoredViewModel.RefreshHead(Input,new(-60,-30,.5f,1),head,default);
        Assert.Equal(-60,b.Yaw);Assert.Equal(0,b.YawVelocity);
    }

    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void CandidateReplayHasNoHiddenHistoryAcrossPermissionActionAndBaseChanges(int hz)
    {
        var view=AlsRefactoredViewState.Initial;var spine=AlsRefactoredSpineState.Initial;var head=AlsRefactoredHeadState.Initial;
        for(var frame=0;frame<hz*4;frame++)
        {
            var input=Input with {Delta=1f/hz,RealDelta=2f/hz,ViewYaw=frame%2==0?179:-179,CharacterYaw=frame*.7,
                RotationMode=frame%35<20?AlsRotationMode.Aiming:AlsRotationMode.LookingDirection,
                HasAction=frame%19<4,FirstPerson=frame%47<9,RelativeBaseRotation=true,BaseDeltaYaw=.4,PoseAiming=.8f};
            var first=AlsRefactoredViewModel.RefreshView(input,view,spine);
            var firstHead=AlsRefactoredViewModel.RefreshHead(input,first.View,head,Settings);
            Assert.Equal(first,AlsRefactoredViewModel.RefreshView(input,view,spine));
            Assert.Equal(firstHead,AlsRefactoredViewModel.RefreshHead(input,first.View,head,Settings));
            Assert.True(float.IsFinite(firstHead.YawVelocity));Assert.InRange(first.Spine.Amount,0,1);
            (view,spine)=first;head=firstHead;
        }
        Assert.Throws<ArgumentException>(()=>AlsRefactoredViewModel.RefreshView(Input with {Delta=float.NaN},view,spine));
        Assert.Throws<ArgumentException>(()=>AlsRefactoredViewModel.RefreshHead(Input,view,head,Settings with {YawHalfLife=-1}));
    }
}
