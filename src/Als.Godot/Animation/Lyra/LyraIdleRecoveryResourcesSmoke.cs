using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraIdleRecoveryResourcesSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch(Exception error) { GD.PushError("Idle Recovery resources failed: "+error);GetTree().Quit(1); }
    }
    private static void Run()
    {
        var bank=LyraLogicalSourceBank.Load(includeMainLean:true,includeLocomotionExtras:true);
        var inventory=LyraLocomotionLayerInventory.Load(LyraSourceNodeCatalog.Load());
        var bindings=inventory.BindSources(bank);
        using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.LocomotionExtrasRoot+"native.json"));
        if(native.RootElement.GetProperty("schemaVersion").GetInt32()!=1 ||
            native.RootElement.GetProperty("catalogSha256").GetString()!=bank.LocomotionExtrasCatalogSha256)
            throw new InvalidOperationException("Stale extra source oracle.");
        var samplers=bank.Slots.ToDictionary(s=>s,bank.CreateSampler);
        var pose=new AlsPrecisePose[81];var repeat=new AlsPrecisePose[81];
        var curves=new LyraCurveSample[bank.Curves.Names.Length];
        var attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        double maxP=0,maxQ=0,maxS=0;var samples=0;var oldSamples=0;
        foreach(var row in native.RootElement.GetProperty("rows").EnumerateArray())
        {
            Compare(row,true);samples++;
        }
        foreach(var path in new[]{LyraLogicalSourceBank.Root+"native.json",LyraLogicalSourceBank.MainLeanRoot+"native.json"})
        {
            using var old=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(path));
            foreach(var row in old.RootElement.GetProperty("rows").EnumerateArray()) { Compare(row,false);oldSamples++; }
        }
        void Compare(JsonElement row,bool joint)
        {
            var slot=row.GetProperty("slot").GetString()!;var time=row.GetProperty("seconds").GetDouble();
            var sampler=samplers[slot];
            sampler.SampleRaw(time,pose);
            LyraLogicalSourceSmoke.Compare(row.GetProperty("raw"),pose,ref maxP,ref maxQ,ref maxS,slot,time,"rawFullBank");
            if(joint) sampler.Sample(time,pose,curves,attributes);else sampler.Sample(time,pose);
            LyraLogicalSourceSmoke.Compare(row.GetProperty("output"),pose,ref maxP,ref maxQ,ref maxS,slot,time,"outputFullBank");
            bank.CreateSampler(slot).Sample(time,repeat);
            if(!pose.SequenceEqual(repeat))throw new InvalidOperationException("Shared extra occurrence scratch.");
            sampler.Sample(time,repeat);
            if(!pose.SequenceEqual(repeat))throw new InvalidOperationException("Extra sampling changed after repeat.");
        }
        var curveValues=0;var attributeValues=0;var curveRows=0;var extraSlots=new HashSet<string>();
        foreach(var row in native.RootElement.GetProperty("curveRows").EnumerateArray())
        {
            var slot=row.GetProperty("slot").GetString()!;var time=row.GetProperty("seconds").GetDouble();
            bank.Curves.SampleRaw(slot,time,curves);
            LyraSourceCurveSmoke.CheckCurves(bank.Curves,curves,row.GetProperty("raw"),slot,time,"rawExtra",ref curveValues);
            bank.Curves.Sample(slot,time,curves);
            LyraSourceCurveSmoke.CheckCurves(bank.Curves,curves,row.GetProperty("output"),slot,time,"outputExtra",ref curveValues);
            bank.Curves.Attributes.SampleRaw(slot,time,attributes);
            LyraSourceCurveSmoke.CheckAttributes(bank.Curves.Attributes,attributes,row.GetProperty("rawAttributes"),slot,time,ref attributeValues);
            bank.Curves.Attributes.Sample(slot,time,attributes);
            LyraSourceCurveSmoke.CheckAttributes(bank.Curves.Attributes,attributes,row.GetProperty("outputAttributes"),slot,time,ref attributeValues);
            extraSlots.Add(slot);curveRows++;
        }
        var nonzeroBases=extraSlots.Count(s=>bank.Get(s).IsAdditive&&bank.Get(s).BaseSampleTime>0);
        if(bank.Count!=245 || bank.Curves.Count!=245 || oldSamples!=960 || extraSlots.Count!=8 ||
            samples!=475 || curveRows!=samples || nonzeroBases!=3 || bindings.Count!=3)
            throw new InvalidOperationException("Incomplete original locomotion resource closure.");
        GD.Print($"LYRA_IDLE_RECOVERY_RESOURCES_GODOT_OK sources={bank.Count} extras=8 additiveBases={nonzeroBases} " +
            $"providers={bindings.Count} sequenceBindings={bindings.Values.Sum(v=>v.Count)} missing=0 " +
            $"samples={samples} oldSamples={oldSamples} curveRows={curveRows} curveValues={curveValues} attributeValues={attributeValues} " +
            $"positionCm={maxP} quaternion={maxQ} scale={maxS} logical=81 skin=68 runtime=false");
    }
}
