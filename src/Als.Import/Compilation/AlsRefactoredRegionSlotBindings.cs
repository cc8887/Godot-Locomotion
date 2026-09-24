using System.Text.Json;
using System.Text.RegularExpressions;
using GodotAls.Core.Actions;

namespace GodotAls.Import.Compilation;

/// <summary>The original Layer group is shared by all seven regional Slots.
/// The host supplies a free group id in its merged bank; native indices are not host ids.</summary>
public sealed class AlsRefactoredRegionSlotBindings
{
    public int HostGroupId { get; }
    public int NativeGroupIndex { get; }
    public string Skeleton { get; }
    private AlsRefactoredRegionSlotBindings(int hostGroup,int nativeGroup,string skeleton)
    { HostGroupId=hostGroup; NativeGroupIndex=nativeGroup; Skeleton=skeleton; }

    public static AlsRefactoredRegionSlotBindings Compile(string inventoryJson,string skeleton,int hostGroupId)
    {
        if(hostGroupId<0)throw new ArgumentOutOfRangeException(nameof(hostGroupId));
        using var document=JsonDocument.Parse(inventoryJson);
        var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1 || root.GetProperty("searchRoot").GetString()!="/ALS")
            throw new ArgumentException("Foreign Slot inventory.");
        var asset=root.GetProperty("skeletons").GetProperty(skeleton);
        var groups=asset.GetProperty("groups").EnumerateArray().ToArray();
        var layer=groups.Where(g=>g.GetProperty("name").GetString()=="Layer").ToArray();
        if(layer.Length!=1)throw new ArgumentException("Missing or ambiguous native Layer group.");
        var names=layer[0].GetProperty("slots").EnumerateArray().Select(n=>n.GetString()!).ToArray();
        var ids=names.Select(n=>AlsMontageSlot.FromRefactoredLayerName(n).Id).Order().ToArray();
        if(!ids.SequenceEqual(Enumerable.Range(5,7)))throw new ArgumentException("Native Layer Slot closure differs.");
        foreach(var name in names)
            if(groups.Sum(g=>g.GetProperty("slots").EnumerateArray().Count(n=>n.GetString()==name))!=1)
                throw new ArgumentException("Regional Slot belongs to multiple groups.");
        var index=layer[0].GetProperty("index").GetInt32();
        var native=Regex.Match(asset.GetProperty("nativeText").GetString()!,
            @"(?m)^\s+SlotGroups\("+index+"\\)=\\(GroupName=\"Layer\",SlotNames=\\(([^\\r\\n]*)\\)\\)\\r?$");
        var exported=native.Success?Regex.Matches(native.Groups[1].Value,"\"([^\"]+)\"").Select(m=>m.Groups[1].Value).ToArray():[];
        if(!exported.SequenceEqual(names))throw new ArgumentException("Layer group differs from native skeleton text.");
        return new(hostGroupId,index,skeleton);
    }

    public AlsSequenceMontageAsset BindSequence(int animationId,string slot,float duration,byte additiveType)
    {
        if(animationId<0 || !float.IsFinite(duration) || duration<=.00005f || additiveType>2)
            throw new ArgumentException("Invalid region sequence resource.");
        return new(animationId,AlsMontageSlot.FromRefactoredLayerName(slot),HostGroupId,duration,additiveType);
    }
}
