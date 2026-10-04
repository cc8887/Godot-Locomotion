using GodotAls.Core.Sync;

namespace GodotAls.Core.Tests;

[Collection(AllocationTestCollection.Name)]
public sealed class AlsAssetSyncMarkerIntersectionTests
{
    [Theory]
    [InlineData(1f,.4f,.6f,1.2f)]
    [InlineData(-1f,.6f,.4f,.8f)]
    public void SharedNamesSkipExtraLeaderMarkersAndKeepOriginalIndices(float rate,float start,float end,float follower)
    {
        var f=new Fixture();f.Players[0]=f.Players[0] with { Time=start,PlayRate=rate };
        f.Players[1]=f.Players[1] with { Time=1 };f.Tick(.2f);
        Assert.Equal(4UL,f.Group.ValidMarkerMask);Assert.Equal(0,f.Group.LeaderPlayerId);
        Assert.InRange(MathF.Abs(f.History[0].Time-end),0,1e-7f);Assert.InRange(MathF.Abs(f.History[1].Time-follower),0,2e-7f);
        Assert.Equal(1,f.History[0].Marker.PreviousIndex);Assert.Equal(3,f.History[0].Marker.NextIndex);
        Assert.Equal(0,f.History[1].Marker.PreviousIndex);Assert.Equal(1,f.History[1].Marker.NextIndex);
        Assert.Equal(2,f.Group.MarkerStart.PreviousSymbol);Assert.Equal(2,f.Group.MarkerStart.NextSymbol);
        Assert.Equal(2,f.Group.MarkerEnd.PreviousSymbol);Assert.Equal(2,f.Group.MarkerEnd.NextSymbol);
        Assert.Equal(f.History[0].Marker,f.SamplesOut[0].Marker);
    }
    [Fact]
    public void NoSharedNamesFallsBackToLengthAndClearsBothRecords()
    {
        var f=new Fixture();f.Tick(.1f);
        f.Markers[4]=new(3,.5f);f.Markers[5]=new(3,1.5f);
        f.Players[1]=f.Players[1] with { AssetMarkerMask=8 };
        f.Tick(.1f);
        Assert.Equal(0UL,f.Group.ValidMarkerMask);Assert.False(f.Group.MarkerEnd.Valid);
        Assert.False(f.History[0].Marker.Initialized);Assert.False(f.History[1].Marker.Initialized);
        Assert.Equal(f.History[0].Time*2,f.History[1].Time);
    }
    [Fact]
    public void IntersectedNamesPreserveHistoryAndAllocateNothingAcrossCycles()
    {
        var f=new Fixture();for(var i=0;i<100;i++) f.Tick(1f/60);
        var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<2000;i++) f.Tick(1f/60);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
        Assert.InRange(f.History[0].Time,0,1);Assert.InRange(f.History[1].Time,0,2);
        Assert.Contains(f.History[0].Marker.PreviousIndex,new[]{1,3});Assert.Contains(f.History[0].Marker.NextIndex,new[]{1,3});
    }
    private sealed class Fixture
    {
        public AlsAssetSyncSequence[] Sequences=[new(0,1,1,0,4),new(1,2,1,4,2)];
        public AlsAssetSyncMarker[] Markers=[new(1,0),new(2,.25f),new(1,.5f),new(2,.75f),new(2,.5f),new(2,1.5f)];
        public AlsAssetSyncPlayer[] Players=[new(0,0,1,AlsAssetSyncKind.Sequence,0,1,.1f,0,1,6,Role:AlsAssetSyncRole.AlwaysLeader),
            new(1,1,1,AlsAssetSyncKind.Sequence,0,1,.9f,1,1,4,Role:AlsAssetSyncRole.AlwaysFollower)];
        public AlsAssetSyncSample[] Samples=[new(0,0,1),new(1,1,1)];
        public AlsAssetPlayerHistory[] History=[];
        public AlsAssetSampleHistory[] SamplesOut=[];
        private readonly AlsAssetPlayerHistory[] _output=new AlsAssetPlayerHistory[2];
        private readonly AlsAssetSampleHistory[] _samples=new AlsAssetSampleHistory[2];
        public AlsAssetSyncGroupHistory Group;
        public void Tick(float delta)
        {
            Assert.True(AlsSyncRuntime.TryEvaluateAssetSyncGroup(0,Group,Players,Samples,Sequences,Markers,History,SamplesOut,delta,
                _output,_samples,out var group,out _));Group=group;History=_output;SamplesOut=_samples;
            for(var i=0;i<2;i++) Players[i]=Players[i] with { Time=History[i].Time,MarkerRecord=History[i].Marker };
        }
    }
}
