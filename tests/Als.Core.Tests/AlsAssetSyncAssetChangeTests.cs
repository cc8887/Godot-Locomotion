using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

public sealed class AlsAssetSyncAssetChangeTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NamedRejoinDiscardsHiddenOccurrenceMarkerStorage(bool marked)
    {
        // UE FAnimGroupInstance::Prepare resets named-group records when the
        // leader has no markers or an occurrence was absent last frame. Its
        // hidden storage is not an index into its newly selected sequence.
        AlsAssetSyncMarker[] markers=marked ? [new(1,.4f),new(2,.8f),new(1,1.6f)] : [];
        AlsAssetSyncSequence[] sequences=[new(11,2.4f,1,0,markers.Length)];
        AlsAssetSyncPlayer[] players=[new(755,11,7,AlsAssetSyncKind.Sequence,1.2333332f,-17.999994f,.25f,0,1,marked?6UL:0,
            Looping:false,IsEvaluator:true,MarkerRecord:new(3,-1,-.3958332f,.70000005f))];
        AlsAssetSyncSample[] samples=[new(755,0,1)];
        var output=new AlsAssetPlayerHistory[1];var sampleOutput=new AlsAssetSampleHistory[1];
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(1,default,players,samples,sequences,markers,[],[],1f/30,
            output,sampleOutput,out var group,out _));
        Assert.True(group.HasLeader);Assert.Equal(marked?6UL:0,group.ValidMarkerMask);
        if(marked) { Assert.True(output[0].Marker.Initialized);Assert.InRange(output[0].Marker.PreviousIndex,-1,2);Assert.InRange(output[0].Marker.NextIndex,-1,2); }
        else { Assert.False(output[0].Marker.Initialized);Assert.Equal(-2,output[0].Marker.PreviousIndex);Assert.Equal(-2,output[0].Marker.NextIndex); }
        Assert.Equal(1.2333332f+(-17.999994f)*(1f/30),output[0].Time);
        var saved=output.ToArray();var savedSamples=sampleOutput.ToArray();
        // Independent sources have no enclosing Prepare that clears storage.
        Assert.False(AlsSyncRuntime.TryEvaluateIndependentAssetPlayers(players,samples,sequences,markers,[],[],1f/30,output,sampleOutput,out _));
        Assert.Equal(saved,output);Assert.Equal(savedSamples,sampleOutput);
        players[0]=players[0] with { MarkerRecord=new(2,-1,float.NaN,.7f) };
        Assert.False(AlsSyncRuntime.TryEvaluateAssetSyncGroup(1,default,players,samples,sequences,markers,[],[],1f/30,
            output,sampleOutput,out _,out _));
        Assert.Equal(saved,output);Assert.Equal(savedSamples,sampleOutput);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ChangedSequenceResetsOnlyProvenOccurrenceStorage(int corruption)
    {
        AlsAssetSyncSequence[] sequences = [new(10, 1.5f, 1, 0, 6), new(11, 1, 1, 6, 2)];
        AlsAssetSyncMarker[] markers = [new(1, .1f), new(2, .3f), new(1, .5f), new(2, .7f),
            new(1, .9f), new(2, 1.1f), new(1, .4f), new(2, .8f)];
        AlsAssetSyncPlayer[] players = [new(7, 10, 1, AlsAssetSyncKind.Sequence, .35f, 0, 1, 0, 1, 6, Looping: false, IsEvaluator: true)];
        AlsAssetSyncSample[] samples = [new(0, 0, 1)];
        var oldPlayers = new AlsAssetPlayerHistory[1]; var oldSamples = new AlsAssetSampleHistory[1];
        Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, default, players, samples, sequences, markers,
            [], [], 1f / 60, oldPlayers, oldSamples, out var oldGroup, out _));
        Assert.Equal(2, oldPlayers[0].Marker.NextIndex);
        var stored = oldPlayers[0].Marker;
        if (corruption == 1) stored = stored with { NextDistance = stored.NextDistance + .01f };
        if (corruption == 2) stored = stored with { NextIndex = 6 };
        players[0] = players[0] with { AssetId = 11, Time = .35f, PlayRate = -21, MarkerRecord = stored };
        samples[0] = new(0, 1, 1);
        var output = oldPlayers.ToArray(); var sampleOutput = oldSamples.ToArray();
        var passed = AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, oldGroup, players, samples, sequences, markers,
            oldPlayers, oldSamples, 1f / 60, output, sampleOutput, out _, out _);
        if (corruption == 0)
        {
            Assert.True(passed); Assert.Equal(0, output[0].Time);
            Assert.Equal(-1, output[0].Marker.PreviousIndex); Assert.Equal(0, output[0].Marker.NextIndex);
            Assert.Equal(.4f, output[0].Marker.NextDistance);
        }
        else
        {
            Assert.False(passed); Assert.Equal(oldPlayers, output); Assert.Equal(oldSamples, sampleOutput);
            players[0] = players[0] with { MarkerRecord = oldPlayers[0].Marker };
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0, oldGroup, players, samples, sequences, markers,
                oldPlayers, oldSamples, 1f / 60, output, sampleOutput, out _, out _));
        }
    }
}
