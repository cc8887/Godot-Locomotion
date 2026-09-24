using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredWeaponMachineResourcesTests(ITestOutputHelper output)
{
    private static AlsRefactoredAnimationCatalog Catalog()=>new(MantlingHostFixture.Read("refactored_animation_sources"),p=>File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(),"assets/config",p)));
    [Theory]
    [InlineData(AlsRefactoredWeaponKind.Bow)]
    [InlineData(AlsRefactoredWeaponKind.PistolOneHanded)]
    [InlineData(AlsRefactoredWeaponKind.PistolTwoHanded)]
    [InlineData(AlsRefactoredWeaponKind.Rifle)]
    public void RealBakedResourcesPreserveStatePrioritiesCurvesAndQuickFeet(AlsRefactoredWeaponKind kind)
    {
        var json=MantlingHostFixture.Read("refactored_weapon_machines");var resources=new AlsRefactoredWeaponMachineResources(json,Catalog(),kind);
        Assert.Equal(new[]{"Relaxed","Aiming","Ready"},resources.States.ToArray().Select(s=>s.Name));
        Assert.Equal(new[]{2,3,4,5},resources.States[2].Exits.ToArray());Assert.Equal(6,resources.Edges.Length);
        Assert.Equal(kind==AlsRefactoredWeaponKind.Bow?.5f:.2f,resources.Edges[4].Seconds);
        Assert.True(resources.Edges[2].QuickFeet);Assert.False(resources.Edges[3].QuickFeet);
        Assert.Equal(0,resources.Edges[0].StartNotify);Assert.Equal(1,resources.Edges[2].StartNotify);
        using var doc=JsonDocument.Parse(json);var native=doc.RootElement.GetProperty("weapons")[(int)kind];
        float maxCurve=0,maxBone=0;
        for(var c=0;c<2;c++)foreach(var row in native.GetProperty("curves")[c].GetProperty("verification").EnumerateArray())
            maxCurve=MathF.Max(maxCurve,MathF.Abs(resources.Curves[c].Sample(row.GetProperty("input").GetSingle())-row.GetProperty("value").GetSingle()));
        var profile=native.GetProperty("blendProfiles")[0];var bones=profile.GetProperty("bones");
        foreach(var row in profile.GetProperty("nativeCases").EnumerateArray())for(var b=0;b<79;b++)
        {
            var entry=bones[b].GetProperty("entry").GetInt32();if(entry<0)continue;
            var weights=resources.QuickFeet.Weights(b,row.GetProperty("alpha").GetSingle());
            maxBone=MathF.Max(maxBone,MathF.Max(MathF.Abs(weights.X-row.GetProperty("incoming")[entry].GetSingle()),MathF.Abs(weights.Y-row.GetProperty("outgoing")[entry].GetSingle())));
        }
        output.WriteLine($"{kind} curves=402 profilePairs=594 maxCurve={maxCurve:R} maxProfile={maxBone:R}");
    }
    [Theory]
    [InlineData("catalog")]
    [InlineData("priority")]
    [InlineData("duration")]
    [InlineData("player")]
    [InlineData("root")]
    [InlineData("curve")]
    [InlineData("profile")]
    public void ForeignOrIncompleteWeaponResourcesAreRejected(string change)
    {
        var catalog=Catalog();var root=JsonNode.Parse(MantlingHostFixture.Read("refactored_weapon_machines"))!;var item=root["weapons"]![0]!;var machine=item["bakedMachines"]![0]!;
        if(change=="catalog")root["catalogSha256"]=new string('0',64);
        if(change=="priority")machine["states"]![2]!["transitions"]!.AsArray().RemoveAt(0);
        if(change=="duration")machine["transitions"]![4]!["crossfadeDuration"]=.2f;
        if(change=="player")machine["states"]![0]!["playerNodeIndices"]!.AsArray().RemoveAt(0);
        if(change=="root")machine["states"]![0]!["stateRootNodeIndex"]=0;
        if(change=="curve")item["curves"]![0]!["curve"]!["keys"]![1]!["value"]=.9f;
        if(change=="profile")item["blendProfiles"]![0]!["bones"]![49]!["scale"]=1;
        Assert.Throws<ArgumentException>(()=>new AlsRefactoredWeaponMachineResources(root.ToJsonString(),catalog,AlsRefactoredWeaponKind.Bow));
    }
}
