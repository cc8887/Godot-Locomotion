using GodotAls.Core.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredCharacterNotifyTests
{
    [Theory]
    [InlineData(30)][InlineData(60)][InlineData(120)]
    public void ActualWalkAndRollRangesHaveCharacterNamespacePayloadsAndCanceledRetry(int hz)
    {
        string Read(string n)=>MantlingHostFixture.Read("refactored_"+n);
        var catalog=new AlsRefactoredAnimationCatalog(Read("animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
        var sync=new AlsRefactoredSyncBank(Read("sync_inputs"),catalog);var notifies=new AlsRefactoredNotifyBank(Read("notify_inputs"),catalog,sync);
        var scope=new AlsRefactoredSourceSyncScope(9,1,catalog,sync,new Dictionary<string,AlsRefactoredTriangulationProfile>(),
            [new(11,[new(0,"/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward",-1),
                new(1,"/ALS/ALS/Animations/Actions/Roll/A_Als_Roll.A_Als_Roll",-1,Looping:false)],new Dictionary<int,string>())],notifies);
        var adapter=new AlsRefactoredCharacterNotifyBinding(new([],[],[],[],[]),notifies,700,32);
        var output=new AlsAdditionalSourceNotify[64];var count=0;var foot=0;var root=0;var entry=0;
        for(var frame=1;frame<=hz*3;frame++)
        {
            var context=new AlsPoseUpdateContext(new(frame,9,1),1,1f/hz).WithUpdateCounter(new(0,(ulong)frame));
            Prepare();count=adapter.Extract(scope,output);var first=output[..count].ToArray();
            foreach(var value in first)
            {
                Assert.True(adapter.TryTimeline(value.Input.Reference,out var timeline));
                Assert.True(adapter.TryPayload(timeline.EventId,out var payload));
                Assert.True(adapter.TrySourceAssetPath(timeline.EventId,out var path));Assert.StartsWith("/ALS/",path);
                Assert.Equal(AlsTimelineSourceKind.Animation,timeline.SourceKind);Assert.Equal(-1,timeline.SourceActionId);
                Assert.InRange(value.Input.Reference.OccurrenceHandleId,700,731);
                Assert.Equal(timeline.DurationSeconds,value.Input.Duration);Assert.Equal(1f,value.Input.EffectiveWeight);
                if(payload is AlsRefactoredFootstepPayload){foot++;Assert.Equal(AlsTimelineEventKind.Footstep,timeline.Kind);}
                if(payload is AlsRefactoredRootScalePayload){root++;Assert.Equal(1.3f,timeline.Payload.ScalarValue0);}
                if(payload is AlsRefactoredGroundedEntryPayload){entry++;Assert.Equal(AlsTimelineEventKind.SetGroundedEntry,timeline.Kind);}
            }
            scope.Discard();Assert.Throws<InvalidOperationException>(()=>adapter.Extract(scope,output));
            Prepare();Assert.Equal(count,adapter.Extract(scope,output));Assert.Equal(first,output[..count]);scope.Commit(context.Identity);
            void Prepare(){scope.Begin(context);scope.Enqueue(11,new(0,default,1,1),context);if(frame<=hz*2)scope.Enqueue(11,new(1,default,1,1),context);scope.Complete();}
        }
        Assert.True(foot>8&&root>hz&&entry==1);
        var sequence=notifies.Assets.Values.First(a=>!a.Montage&&a.Count>0);
        var reference=new AlsAssetNotifyReference(sequence.Offset,700,.1f,true,false);
        Assert.True(adapter.TryTimeline(reference,out _));
        for(var i=0;i<1000;i++)adapter.TryTimeline(reference,out _);
        var allocated=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<10000;i++)adapter.TryTimeline(reference,out _);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-allocated);
        Assert.False(adapter.TryTimeline(reference with {OccurrenceHandleId=732},out _));
        Assert.False(adapter.TryPayload(-1,out _));
    }
}
