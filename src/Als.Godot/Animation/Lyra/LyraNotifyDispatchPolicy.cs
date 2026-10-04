using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraNotifyDispatchPolicy
{
    public const int MainMachine=7;
    public const int PivotState=4;
    public LyraNotifyDispatchPolicy()
    {
        const string root="res://assets/generated/lyra_als/";
        using var doc=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"notify_dispatch_v1_policy.json"));
        var data=doc.RootElement;
        if(data.GetProperty("schemaVersion").GetInt32()!=1)throw new InvalidOperationException("Unknown notify source-state policy.");
        foreach(var d in data.GetProperty("dependencies").EnumerateObject())
            if(LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name))!=d.Value.GetString())
                throw new InvalidOperationException("Stale notify dispatch dependency: "+d.Name);
        var policy=data.GetProperty("trace");var calls=policy.GetProperty("notifyCalls");
        if(policy.GetProperty("mainMachineContextIndex").GetInt32()!=MainMachine||!policy.GetProperty("mainMachineNotifyMetadata").GetBoolean()||
            calls.GetArrayLength()!=1||calls[0].GetProperty("function").GetString()!="WasAnimNotifyStateActiveInSourceState"||
            calls[0].GetProperty("previous").GetString()!="Pivot"||calls[0].GetProperty("next").GetString()!="Cycle")
            throw new InvalidOperationException("Original Pivot notify query changed.");
        var classes=policy.GetProperty("classes");
        if(classes.GetArrayLength()!=10)throw new InvalidOperationException("Incomplete notify receiver inventory.");
        foreach(var c in classes.EnumerateArray())
            if(c.GetProperty("receive").GetBoolean()||c.GetProperty("propagate").GetBoolean()||
                c.GetProperty("functions").EnumerateArray().Any(f=>f.GetProperty("found").GetBoolean()))
                throw new NotSupportedException("Changed original named notify receiver.");
    }
}
