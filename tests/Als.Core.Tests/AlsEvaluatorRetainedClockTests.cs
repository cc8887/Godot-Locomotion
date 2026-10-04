using GodotAls.Core.Contracts;
using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

public sealed class AlsEvaluatorRetainedClockTests
{
    [Fact]
    public void ShorterAssetRetainsItsEvaluatorAccumulatorAcrossZeroDelta()
    {
        AlsAssetSyncSequence[] sequences=[new(0,2,1,0,0),new(1,1,1,0,0)];
        AlsAssetSyncSample[] samples=[new(9,0,1)];
        AlsAssetSyncPlayer[] players=[new(5,0,1,AlsAssetSyncKind.Sequence,1.5f,0,1,0,1,0,Looping:false,IsEvaluator:true)];
        var group=default(AlsAssetSyncGroupHistory);
        var previousPlayers=Array.Empty<AlsAssetPlayerHistory>(); var previousSamples=Array.Empty<AlsAssetSampleHistory>();
        void Tick(float delta)
        {
            var output=new AlsAssetPlayerHistory[1]; var sampleOutput=new AlsAssetSampleHistory[1];
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,group,players,samples,sequences,[],previousPlayers,
                previousSamples,delta,output,sampleOutput,out var next,out var failure),failure.ToString());
            previousPlayers=output; previousSamples=sampleOutput; group=next;
        }
        Tick(.25f);
        players[0]=players[0] with { AssetId=1 }; samples[0]=samples[0] with { SequenceIndex=1 };
        Tick(0);
        Assert.True(previousPlayers[0].IsNonLoopingEvaluator); Assert.Equal(1.5f,previousPlayers[0].Time);
        Assert.Equal(1.5f,group.PreviousRatio); Assert.Equal(1.5f,group.Ratio);
        var badOutput=new AlsAssetPlayerHistory[1]; var badSamples=new AlsAssetSampleHistory[1];
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,group,players,samples,sequences,[],
            [previousPlayers[0] with { IsNonLoopingEvaluator=false }],previousSamples,.25f,badOutput,badSamples,out _,out _));
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,group with { PreviousRatio=1.6f },players,samples,sequences,[],
            previousPlayers,previousSamples,.25f,badOutput,badSamples,out _,out _));
        Assert.Equal(default,badOutput[0]); Assert.Equal(default,badSamples[0]);
        players[0]=players[0] with { PlayRate=-2 };
        Tick(.25f);
        Assert.Equal(1,previousPlayers[0].Time); Assert.Equal(1.5f,previousSamples[0].PreviousTime);
        Assert.Equal(-.5f,previousPlayers[0].Delta);
        players[0]=players[0] with { Time=1,PlayRate=0 }; Tick(.25f);
        Assert.Equal(1,previousPlayers[0].Time); Assert.Equal(1,group.PreviousRatio);
    }

    [Theory]
    [InlineData(false,false)] [InlineData(true,true)]
    public void OutOfClipTimeStillRejectsOrdinaryPlayersAndLoopingEvaluators(bool evaluator,bool looping)
    {
        AlsAssetSyncPlayer[] players=[new(5,0,1,AlsAssetSyncKind.Sequence,1.5f,0,1,0,1,0,Looping:looping,IsEvaluator:evaluator)];
        var output=new AlsAssetPlayerHistory[1]; var sampleOutput=new AlsAssetSampleHistory[1];
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,default,players,[new(9,0,1)],[new(0,1,1,0,0)],[],[],[],
            0,output,sampleOutput,out _,out var failure));
        Assert.Equal(AlsP5FailureCode.InvalidSyncGroup,failure); Assert.Equal(default,output[0]); Assert.Equal(default,sampleOutput[0]);
    }
}
