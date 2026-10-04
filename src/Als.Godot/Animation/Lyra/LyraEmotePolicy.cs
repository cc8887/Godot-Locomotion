using System.Text.Json;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraEmotePolicy
{
    public int Asset {get;}
    public float BlendOut {get;}
    public LyraEmotePolicy(LyraMontageCatalog catalog)
    {
        const string root="res://assets/generated/lyra_als/";
        using var doc=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"emote_v1_policy.json"));
        var data=doc.RootElement;
        foreach(var dependency in data.GetProperty("dependencies").EnumerateObject())
            if(dependency.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+dependency.Name)))
                throw new InvalidOperationException("Stale original Emote policy.");
        var trace=data.GetProperty("trace");
        string[] fields=["InstancingPolicy","bRetriggerInstancedAbility","ActivationGroup","CancelAbilitiesWithTag","BlockAbilitiesWithTag","Montage to Play"];
        var properties=trace.GetProperty("properties").EnumerateArray().Where(p=>fields.Contains(p.GetProperty("name").GetString())).ToDictionary(p=>p.GetProperty("name").GetString()!,p=>p.GetProperty("value").GetString()!);
        if(properties["InstancingPolicy"]!="InstancedPerActor"||properties["bRetriggerInstancedAbility"]!="False"||properties["ActivationGroup"]!="Independent"||
            properties["CancelAbilitiesWithTag"]!="()"||properties["BlockAbilitiesWithTag"]!="()")
            throw new NotSupportedException("Changed original GA_Emote activation policy.");
        var montage=properties["Montage to Play"].Split('\'')[1];Asset=catalog.Paths.IndexOf(montage);
        if(Asset<0)throw new InvalidOperationException("Original Emote Montage is missing.");
        BlendOut=catalog.Definitions[Asset].Lifecycle.BlendOutSeconds;
    }
}
