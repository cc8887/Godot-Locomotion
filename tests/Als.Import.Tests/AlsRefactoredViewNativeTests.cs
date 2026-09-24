using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredViewNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void IndependentHistoryMatchesOriginalViewSpineAndHeadCallbacks(int hz)
    {
        string Read(string name)=>MantlingHostFixture.Read("refactored_"+name);
        var inputs=Read("head_inputs");
        var settings=AlsRefactoredHeadSettingsCompiler.Compile(inputs,Read("layering_graphs"));
        using var document=JsonDocument.Parse(Read("view_trace"));
        var root=document.RootElement;
        Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs))),
            root.GetProperty("inputsSha256").GetString()!.ToUpperInvariant());
        var trace=root.GetProperty("traces").EnumerateArray().Single(t=>t.GetProperty("name").GetString()==$"{hz}hz");
        var view=AlsRefactoredViewState.Initial;
        var spine=AlsRefactoredSpineState.Initial;
        var head=AlsRefactoredHeadState.Initial;
        var maximum=new double[3];var maximumVelocity=0d;var frame=0;var hidden=0;var initialized=0;var switching=0;
        foreach(var row in trace.GetProperty("frames").EnumerateArray())
        {
            var request=row.GetProperty("request");
            var input=request.GetProperty("input").Deserialize<AlsRefactoredViewInput>();
            (view,spine)=AlsRefactoredViewModel.RefreshView(input,view,spine);
            if(request.GetProperty("initializeHead").GetBoolean())
            {head=AlsRefactoredViewModel.InitializeHead(head);initialized++;}
            if(request.GetProperty("updateHead").GetBoolean())
                head=AlsRefactoredViewModel.RefreshHead(input,view,head,settings);
            else hidden++;
            if(head.SwitchingSides)switching++;
            Compare("view",0,[view.YawAngle,view.PitchAngle,view.PitchAmount,view.HeadBlendAmount]);
            Compare("spine",1,[spine.Allowed?1:0,spine.Amount,spine.Scale,spine.Bias,spine.LastYaw,spine.LastWorldYaw,spine.Yaw,spine.FinalYaw]);
            Compare("head",2,[head.InitializationRequired?1:0,head.SwitchingSides?1:0,head.Pitch,head.Yaw,head.YawVelocity,head.YawAmount]);
            frame++;

            void Compare(string field,int group,double[] actual)
            {
                var expected=row.GetProperty(field).EnumerateArray().Select(v=>v.GetDouble()).ToArray();
                Assert.Equal(expected.Length,actual.Length);
                for(var index=0;index<actual.Length;index++)
                {
                    var difference=Math.Abs(expected[index]-actual[index]);
                    var isVelocity=group==2&&index==4;
                    if(isVelocity)maximumVelocity=Math.Max(maximumVelocity,difference);
                    else maximum[group]=Math.Max(maximum[group],difference);
                    var isBoolean=(group==1&&index==0)||(group==2&&index<2);
                    var isWeight=(group==0&&index>=2)||(group==1&&index>=1&&index<=3)||(group==2&&index==5);
                    // Separate unitless weights, degrees and degrees/second.
                    // 0.001 degrees is <0.018 mm at a one-metre lever arm.
                    // 0.001 deg/s is about eight float ULPs at 1024 deg/s;
                    // it corresponds to <0.000034 degrees over a 30 Hz step.
                    Assert.True(double.IsFinite(actual[index])&&difference<=(isBoolean?0:isWeight?2e-6:1e-3),
                        $"{hz}Hz frame={frame} {field}[{index}] native={expected[index]:R} actual={actual[index]:R} diff={difference:R}");
                }
            }
        }
        Assert.Equal(hz*5,frame);Assert.Equal(hz/2,hidden);Assert.Equal(2,initialized);Assert.Equal(hz,switching);
        output.WriteLine($"{hz}Hz frames={frame} max View={maximum[0]:R} Spine={maximum[1]:R} Head={maximum[2]:R} HeadVelocity={maximumVelocity:R}");
    }
}
