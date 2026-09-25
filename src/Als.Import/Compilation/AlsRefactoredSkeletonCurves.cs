using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

/// <summary>Original SK_Als metadata gate. Linked-bone filtering must not silently
/// disappear if a future asset introduces non-default curve metadata.</summary>
public sealed class AlsRefactoredSkeletonCurves
{
    private readonly string[] _names;
    public string CatalogDigest { get; }
    public ReadOnlySpan<string> Names=>_names;
    public AlsRefactoredSkeletonCurves(string json,AlsRefactoredAnimationCatalog catalog)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        const string source="/ALS/ALS/Character/SK_Als.SK_Als";
        Expect(root,new { schemaVersion=1,source,referenceSequence="/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose" });
        CatalogDigest=catalog.IndexDigest;
        Require(root.GetProperty("catalogSha256").GetString()!.Equals(CatalogDigest,StringComparison.OrdinalIgnoreCase),"Foreign skeleton curve catalog.");
        var text=root.GetProperty("nativeText").GetString()!;
        Require(text.StartsWith("Begin Object Class=/Script/Engine.Skeleton ",StringComparison.Ordinal)&&
            text.Contains("ExportPath=\"/Script/Engine.Skeleton'"+source+"'\"",StringComparison.Ordinal)&&
            text.Contains("ExportPath=\"/Script/Engine.AnimCurveMetaData'"+source+":AnimCurveMetaData_0'\"",StringComparison.Ordinal),"Foreign skeleton curve metadata.");
        var matches=Regex.Matches(text,"(?m)^      CurveMetaData=([^\\r\\n]+)");
        Require(matches.Count==1,"Missing/ambiguous skeleton curve metadata.");
        var names=new List<string>();
        foreach(var entry in Tuple(matches[0].Groups[1].Value))
        {
            var pair=Tuple(entry);Require(pair.Length==2&&pair[1]=="()","Linked-bone/non-default curve metadata requires explicit filtering.");
            var name=JsonSerializer.Deserialize<string>(pair[0]);Require(!string.IsNullOrWhiteSpace(name),"Missing curve name.");names.Add(name!);
        }
        Require(names.Count==43&&names.Distinct(StringComparer.OrdinalIgnoreCase).Count()==names.Count&&
            new[]{"FootLeftLock","FootRightLock","FootPlanted","PoseMoving","PoseStanding","RotationYawSpeed"}.All(names.Contains),"Incomplete original skeleton curves.");
        _names=names.Order(StringComparer.Ordinal).ToArray();
    }
    private static string[] Tuple(string value)
    {
        Require(value.Length>=2&&value[0]=='('&&value[^1]==')',"Malformed native metadata tuple.");
        var result=new List<string>();var depth=0;var quoted=false;var escaped=false;var start=1;
        for(var i=1;i<value.Length-1;i++)
        {
            var c=value[i];
            if(quoted){if(escaped)escaped=false;else if(c=='\\')escaped=true;else if(c=='"')quoted=false;continue;}
            if(c=='"')quoted=true;else if(c=='(')depth++;else if(c==')'){Require(depth>0,"Unbalanced metadata tuple.");depth--;}
            else if(c==','&&depth==0){result.Add(value[start..i].Trim());start=i+1;}
        }
        Require(!quoted&&depth==0,"Unterminated metadata tuple.");if(start<value.Length-1)result.Add(value[start..^1].Trim());return result.ToArray();
    }
    private static void Require(bool valid,string message){if(!valid)throw new ArgumentException(message);}
}
