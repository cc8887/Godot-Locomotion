using System.Collections.Immutable;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraMontageNotifyWindow(LyraNotifyPlayback Playback,int Asset,int Slot,
    float Previous,float Current,float ClipPrevious,float ClipCurrent,float Weight,bool Extract);
internal sealed record LyraMontageNotifyFrame(LyraMontageNotifyBinding Owner,AlsFrameIdentity Identity,
    AlsMontageFrame BankFrame,ImmutableArray<AlsMontageTraversal> Traversal,
    ImmutableArray<LyraMontageNotifyWindow> Windows,ushort RelevantMask,ushort CurrentRelevantMask,ulong PreparationSerial);
internal interface ILyraMontageNotifySlots
{
    ushort NotifyRelevantMask {get;}
    ushort CurrentNotifyRelevantMask {get;}
    void ValidateCommit(AlsFrameIdentity identity);
}

// Only the currently bound single-section/single-segment shape is supported.
// The physical bank provides HandleEvents ranges; this bridge owns no clock.
internal sealed class LyraMontageNotifyBinding(LyraNotifyCatalog notifies,LyraMontageCatalog catalog,
    AlsMontageRuntime runtime,ILyraMontageNotifySlots slots)
{
    public LyraMontageNotifyFrame Capture(AlsFrameIdentity identity)
    {
        runtime.ValidateCommit(identity);slots.ValidateCommit(identity);
        var frame=runtime.Frame;var traversal=runtime.NotifyTraversal.ToArray().ToImmutableArray();
        var windows=ImmutableArray.CreateBuilder<LyraMontageNotifyWindow>();
        foreach(var tick in traversal)
        {
            if(tick.Interrupted)continue;
            if(tick.InstanceId<=0||(uint)tick.ActionDefinitionId>=catalog.Definitions.Length)
                throw new InvalidOperationException("Unbound Montage notify instance.");
            var asset=catalog.Definitions[tick.ActionDefinitionId];
            if(tick.AnimationId!=asset.AnimationId||tick.Slot!=asset.Slot||!float.IsFinite(tick.NotifyWeight)||tick.NotifyWeight<0||
                tick.PreviousPosition<0||tick.CurrentPosition<0||tick.PreviousPosition>asset.Duration||tick.CurrentPosition>asset.Duration)
                throw new InvalidOperationException("Foreign physical Montage notify range.");
            var direct=notifies.Asset(catalog.Paths[tick.ActionDefinitionId]);
            if(direct.Length!=asset.Duration)throw new InvalidOperationException("Foreign Montage notify duration.");
            LyraNotifyPlayback Playback(int slot,int sample)=>new(LyraNotifySourceOwner.Main,
                slot<0?-1:LyraMontageCatalog.MainNodes[slot],checked((int)tick.InstanceId),sample,tick.InstanceId,
                AlsAssetNotifySourceKind.Montage,tick.InstanceId);
            windows.Add(new(Playback(-1,0),direct.Index,-1,tick.PreviousPosition,tick.CurrentPosition,
                tick.PreviousPosition,tick.CurrentPosition,tick.NotifyWeight,true));
            var tracks=catalog.Metadata[tick.ActionDefinitionId].GetProperty("slots").EnumerateArray().ToArray();
            for(var index=0;index<tracks.Length;index++)
            {
                var track=tracks[index];var slot=Array.IndexOf(LyraMontageCatalog.SlotNames,track.GetProperty("name").GetString());
                var segment=track.GetProperty("segments")[0];var sequence=notifies.Asset(segment.GetProperty("animation").GetString()!);
                var start=segment.GetProperty("clipStart").GetSingle();var end=segment.GetProperty("clipEnd").GetSingle();
                var rate=segment.GetProperty("clipRate").GetSingle();var length=(end-start)/rate;
                if(slot<0||segment.GetProperty("start").GetSingle()!=0||segment.GetProperty("loops").GetInt32()!=1||rate<=0||end>sequence.Length)
                    throw new NotSupportedException("Unbound Montage notify segment.");
                var previous=tick.PreviousPosition;var current=tick.CurrentPosition;var backwards=previous>current;
                var overlap=backwards?current<length&&previous>0:previous<length&&current>0;
                var clipPrevious=Math.Clamp(start+(backwards?MathF.Min(previous,length):MathF.Max(previous,0))*rate,start,end);
                var remaining=MathF.Abs(Math.Clamp(current,0,length)-Math.Clamp(previous,0,length));
                var point=backwards?start:end;var toPoint=(point-clipPrevious)/rate;
                var clipCurrent=MathF.Abs(remaining)<MathF.Abs(toPoint)?remaining*(backwards?-rate:rate)+clipPrevious:point;
                windows.Add(new(Playback(slot,index+1),sequence.Index,slot,previous,current,clipPrevious,clipCurrent,tick.NotifyWeight,overlap));
            }
        }
        return new(this,identity,frame,traversal,windows.ToImmutable(),slots.NotifyRelevantMask,slots.CurrentNotifyRelevantMask,runtime.PreparationSerial);
    }
    public void Validate(LyraMontageNotifyFrame frame)
    {
        if(!ReferenceEquals(frame.Owner,this)||!ReferenceEquals(frame.BankFrame,runtime.Frame)||runtime.Frame.Identity!=frame.Identity||
            runtime.PreparationSerial!=frame.PreparationSerial||
            !runtime.NotifyTraversal.SequenceEqual(frame.Traversal.AsSpan())||slots.NotifyRelevantMask!=frame.RelevantMask||
            slots.CurrentNotifyRelevantMask!=frame.CurrentRelevantMask)
            throw new InvalidOperationException("Foreign or stale Montage notify frame.");
        runtime.ValidateCommit(frame.Identity);slots.ValidateCommit(frame.Identity);
    }
}
