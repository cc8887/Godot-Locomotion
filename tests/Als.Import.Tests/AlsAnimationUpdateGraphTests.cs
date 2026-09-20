using System.Numerics;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsAnimationUpdateGraphTests
{
    [Fact]
    public void FormalDispatchHasDistinctStateBranches()=>AlsAnimationUpdateGraphCompiler.Validate(Read("v4_layering_inputs.json"));

    [Theory]
    [InlineData("MovementState","FloorState")]
    [InlineData("NewEnumerator1","NewEnumerator3")]
    [InlineData("NewEnumerator2","NewEnumerator4")]
    [InlineData("NewEnumerator3","NewEnumerator0")]
    [InlineData("UpdateInAirValues","UpdateMovementValues")]
    [InlineData("UpdateFootIK","UpdateCharacterInfo")]
    public void RejectsChangedGlobalDispatch(string from,string to)
    {
        var root=JsonNode.Parse(Read("v4_layering_inputs.json"))!;
        var graph=root["graphs"]!.AsArray().Single(g=>g!["name"]!.GetValue<string>()=="UpdateGraph")!;
        var original=graph["nativeText"]!.GetValue<string>();
        var changed=original.Replace(from,to,StringComparison.Ordinal); Assert.NotEqual(original,changed);
        graph["nativeText"]=changed;
        Assert.ThrowsAny<Exception>(()=>AlsAnimationUpdateGraphCompiler.Validate(root.ToJsonString()));
    }

    [Theory]
    [InlineData(0,0)] [InlineData(0,1)] [InlineData(1,0)] [InlineData(1,1)]
    [InlineData(2,0)] [InlineData(2,1)] [InlineData(3,0)] [InlineData(3,1)]
    [InlineData(4,0)] [InlineData(4,1)] [InlineData(255,0)] [InlineData(255,1)]
    public void GroundPropertiesAndDoOnceUseMovementStateInsteadOfFloor(int rawState,int floor)
    {
        var json=Read("v4_movement_runtime_inputs.json"); var curves=AlsMovementInputCurveCompiler.Compile(json);
        var functions=AlsMovementInputFunctionCompiler.Compile(json,curves);
        var rates=AlsGroundedRateCompiler.Compile(json,curves,functions);
        var settings=AlsLocomotionInputCompiler.Compile(json).Movement;
        var model=AlsGroundedUpdateCompiler.Compile(json,curves,AlsGroundedInputFunctionCompiler.Compile(json,curves),rates,
            AlsMovementInputStateCompiler.Compile(json));
        var control=AlsYawOffsetCompiler.CompileGlobalControl(json,Read("v4_yaw_inputs.json"),Read("v4_grounded_control_inputs.json"));
        var first=Frame(1,1,3.5f); var moving=AlsStandingMovementInputModel.Evaluate(first.Identity,first.ActualVelocity,1,settings);
        var ground=model.Evaluate(first,moving,AlsGait.Running,model.InitialState,new(.7f,-.2f),default,1,AlsMovementStateInput.Grounded);
        var prior=control.Evaluate(first,AlsGait.Running,AlsRotationMode.LookingDirection,ground.State,control.InitialState,
            movementState:AlsMovementStateInput.Grounded);
        var input=Frame(2,(byte)floor,0); var stopped=AlsStandingMovementInputModel.Evaluate(input.Identity,input.ActualVelocity,0,settings);
        var feedback=new AlsAnimationInputFeedback(first.Identity,true,default);
        var state=(AlsMovementStateInput)rawState;
        var next=model.Evaluate(input,stopped,AlsGait.Walking,ground.State,ground.Lean,feedback,1,state);
        var nextControl=control.Evaluate(input,AlsGait.Walking,AlsRotationMode.Aiming,next.State,prior.State,movementState:state);
        Assert.False(next.Updated); Assert.Equal(ground.Lean,next.Lean);
        if(state==AlsMovementStateInput.Grounded)
        {
            Assert.False(next.State.ShouldMove); Assert.True(nextControl.Execution.ChangedToFalse);
            Assert.True(nextControl.Execution.WhileFalse);
        }
        else
        {
            Assert.Equal(ground.State with {Identity=input.Identity},next.State);
            Assert.Equal(prior.State with {Identity=input.Identity},nextControl.State);
            Assert.Equal(new AlsMovementUpdateGate(prior.State.Gate,false,false,false,false),nextControl.Execution);
        }
        var land=AlsLandPredictionCompiler.Compile(json,curves);
        var air=AlsInAirUpdateCompiler.Compile(json,curves,functions,land);
        if(state==AlsMovementStateInput.InAir)
            Assert.Equal(input.Identity,air.Evaluate(input,ground.Lean,feedback,state).Identity);
        else Assert.Throws<ArgumentException>(()=>air.Evaluate(input,ground.Lean,feedback,state));
    }
    private static AlsFrameInput Frame(long frame,byte floor,float speed)=>AlsFrameInput.CreateDefault(new(frame,11,2),1f/60) with
    {ActualVelocity=new(0,0,-speed),MaxAcceleration=8,MaxBrakingDeceleration=16,Floor=new(floor,Vector3.UnitY,-1,Matrix4x4.Identity,default)};
    private static string Read(string name)=>File.ReadAllText(Path.Combine(RepositoryRoot.Find(),"assets","config",name));
}
