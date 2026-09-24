using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsRefactoredLayeringInputTests
{
    [Fact]
    public void AllRawLayerValuesPreserveNegativeAndAboveOneValues()
    {
        var names=AlsRefactoredLayeringInputModel.CurveNames.ToArray();var model=new AlsRefactoredLayeringInputModel(names);
        var curves=Enumerable.Range(0,names.Length).Select(i=>new AlsInertialCurve(i%2==0?-i-.25f:i+.5f)).ToArray();
        var result=model.Evaluate(new(2,3,1),new(1,3,1),curves);
        string[] properties=["HeadBlendAmount","HeadAdditiveBlendAmount","HeadSlotBlendAmount",
            "ArmLeftBlendAmount","ArmLeftAdditiveBlendAmount","ArmLeftSlotBlendAmount","ArmLeftLocalSpaceBlendAmount",
            "ArmRightBlendAmount","ArmRightAdditiveBlendAmount","ArmRightSlotBlendAmount","ArmRightLocalSpaceBlendAmount",
            "HandLeftBlendAmount","HandRightBlendAmount","SpineBlendAmount","SpineAdditiveBlendAmount","SpineSlotBlendAmount",
            "PelvisBlendAmount","PelvisSlotBlendAmount","LegsBlendAmount","LegsSlotBlendAmount"];
        for(var i=0;i<properties.Length;i++)Assert.Equal(curves[i].Value,(float)typeof(AlsRefactoredLayeringInput).GetProperty(properties[i])!.GetValue(result)!);
        Assert.Equal(1,result.ViewAmount);Assert.Equal(0,result.ViewHeadBlendAmount);Assert.Equal(1,result.ViewSpineBlendAmount);
    }
    [Fact]
    public void MeshSpaceSwitchUsesFloatFullWeightThresholdRatherThanV4Floor()
    {
        var model=new AlsRefactoredLayeringInputModel(["LayerArmLeftLocalSpace","LayerArmRightLocalSpace"]);
        var threshold=1f-AlsPoseBlender.WeightThreshold;
        foreach(var value in new[]{-1f,0f,.5f,MathF.BitDecrement(threshold),threshold,MathF.BitIncrement(threshold),1f,2f})
        {
            var result=model.Evaluate(new(2,3,1),new(1,3,1),[new(value),new(value)]);
            Assert.Equal(value,result.ArmLeftLocalSpaceBlendAmount);Assert.Equal(value,result.ArmRightLocalSpaceBlendAmount);
            Assert.Equal(value>=threshold?0:1,result.ArmLeftMeshSpaceBlendAmount);Assert.Equal(result.ArmLeftMeshSpaceBlendAmount,result.ArmRightMeshSpaceBlendAmount);
        }
    }
    [Fact]
    public void ViewFactorsClampOnlyTheirOwnInputsAndRespectPresence()
    {
        var model=new AlsRefactoredLayeringInputModel(["ViewBlock","PoseAiming","LayerHead"]);
        foreach(var block in new[]{-1f,0f,.25f,.75f,1f,2f})
        foreach(var aim in new[]{-1f,0f,.25f,.75f,1f,2f})
        {
            var input=model.Evaluate(new(2,3,1),new(1,3,1),[new(block),new(aim),new(-1)]);
            var view=1-System.Math.Clamp(block,0,1);Assert.Equal(view,input.ViewAmount);
            Assert.Equal(view*(1-System.Math.Clamp(aim,0,1)),input.ViewHeadBlendAmount);
            Assert.Equal(view*System.Math.Clamp(aim,0,1),input.ViewSpineBlendAmount);Assert.Equal(-1,input.HeadBlendAmount);
        }
        var absent=model.Evaluate(new(2,3,1),new(1,3,1),[default,default,default]);
        Assert.Equal(1,absent.ViewHeadBlendAmount);Assert.Equal(0,absent.HeadBlendAmount);
    }
    [Fact]
    public void FeedbackRequiresACommittedPastFrameAndSupportsDeterministicRetry()
    {
        var model=new AlsRefactoredLayeringInputModel(["LayerHead"]);var id=new AlsFrameIdentity(2,3,1);
        var cold=model.Evaluate(id,default,[]);Assert.Equal(0,cold.HeadSlotBlendAmount);Assert.Equal(1,cold.ArmLeftMeshSpaceBlendAmount);
        Assert.Equal(model.Evaluate(id,new(1,3,1),[new(-1)]),model.Evaluate(id,new(1,3,1),[new(-1)]));
        Assert.Throws<ArgumentException>(()=>model.Evaluate(id,id,[new(0)]));
        Assert.Throws<ArgumentException>(()=>model.Evaluate(id,new(1,4,1),[new(0)]));
        Assert.Throws<ArgumentException>(()=>model.Evaluate(id,new(1,3,2),[new(0)]));
        Assert.Throws<ArgumentException>(()=>model.Evaluate(id,default,[new(0)]));
        Assert.Throws<ArgumentException>(()=>model.Evaluate(id,new(1,3,1),[new(float.NaN)]));
        Assert.Throws<ArgumentException>(()=>new AlsRefactoredLayeringInputModel(["LayerHead","layerhead"]));
    }
}
