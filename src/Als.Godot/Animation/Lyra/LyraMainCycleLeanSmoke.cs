using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainCycleLeanSmoke : Node
{
    public override void _Ready()
    {
        try { LyraCycleLayerSourceSmoke.Run(true, true, true, true, true, true, true, true); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Main Cycle Lean failed: " + error); GetTree().Quit(1); }
    }
}

internal sealed class LyraMainCycleLeanComparison
{
    private int _frames, _changed, _clocks, _samples;
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Single(JsonElement row, string field, float value, string label)
    { Require(row.GetProperty(field + "Bits").GetUInt32() == unchecked((uint)BitConverter.SingleToInt32Bits(value)), label + "/Lean " + field); }
    public LyraMainCycleLeanComparison(LyraLogicalSourceBank bank)
    { Require(bank.MainLeanCatalogSha256 is not null, "Main Cycle needs original Lean resources."); }
    public void Clock(JsonElement expected, LyraMainLeanNodeState actual, string label)
    {
        Single(expected, "time", actual.Time, label); Single(expected, "pin", actual.Pin, label);
        Single(expected, "cachedWeight", actual.CachedWeight, label); Single(expected, "previous", actual.DeltaPrevious, label);
        Single(expected, "delta", actual.Delta, label);
        var rows = expected.GetProperty("samples"); Require(rows.GetArrayLength() == actual.Samples.Length, label + "/Lean sample inventory.");
        for (var i = 0; i < actual.Samples.Length; i++)
        {
            var row = rows[i]; var sample = actual.Samples[i];
            Require(row.GetProperty("index").GetInt32() == sample.Weight.SampleId, label + "/Lean sample identity/order.");
            Single(row, "weight", sample.Weight.Weight, label); Single(row, "weightRate", sample.Weight.WeightRate, label);
            Single(row, "time", sample.Clock.Time, label); Single(row, "previous", sample.Clock.PreviousTime, label);
            Single(row, "deltaPrevious", sample.Clock.DeltaPrevious, label); Single(row, "delta", sample.Clock.Delta, label); _samples++;
        }
        _clocks++;
    }
    public void Compare(JsonElement row, LyraMainCycleLeanHost host, LyraMainCycleLeanCandidate candidate,
        LyraCycleLayerPoseHost cycle, string label)
    {
        Clock(row.GetProperty("lean"), host.PreparedLean(candidate), label);
        Require(host.Curves.SequenceEqual(cycle.Curves) && host.Attributes.SequenceEqual(cycle.Attributes) && host.RootMotion == cycle.RootMotion,
            label + "/Main additive changed provider metadata or root attribute.");
        var changed = false;
        for (var bone = 0; bone < 81; bone++)
            if ((host.Pose[bone].Position - cycle.Pose[bone].Position).LengthSquared > 1e-12 ||
                Math.Abs(AlsQuaternion.Dot(host.Pose[bone].Rotation.Normalized(), cycle.Pose[bone].Rotation.Normalized())) < 1 - 1e-10)
                changed = true;
        if (changed) _changed++;
        _frames++;
    }
    public void Finish()
    {
        Require(_frames > 0 && _changed > 0 && _samples > 0, "Main Cycle Lean was bypassed.");
        GD.Print($"LYRA_MAIN_CYCLE_LEAN_TRACE_OK poseFrames={_frames} changed={_changed} clockChecks={_clocks} sampleChecks={_samples} exactBits=true metadata=forwarded basis=realLinkedCycle");
    }
    public void FinishClocks(int frames,string stage)
    {
        Require(_clocks==frames && _samples>0,"Main Lean clock coverage is incomplete.");
        GD.Print($"LYRA_MAIN_LEAN_CLOCKS_OK clockChecks={_clocks} sampleChecks={_samples} exactBits=true stage={stage}");
    }
}
