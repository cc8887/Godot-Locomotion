using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsMantlingContactTests
{
    [Fact]
    public void BakedContactsBindFinalPoseRootsAndRejectForeignOrMissingKeys()
    {
        var root=RepositoryRoot.Find();
        var animation=File.ReadAllText(Path.Combine(root,"assets/config/refactored_mantle_animation_inputs.json"));
        var roots=File.ReadAllText(Path.Combine(root,"assets/config/refactored_mantle_root_tracks.json"));
        var path=Path.Combine(root,"assets/generated/mantle_contacts",AlsMantlingContactCompiler.Digest(animation)+"-"+
            AlsMantlingContactCompiler.Digest(roots)+".json");
        var json=File.ReadAllText(path);var curves=AlsMantlingContactCompiler.Compile(json,animation,roots);
        var low=curves.Single(c=>c.Key.EndsWith(".A_Als_Mantle_Low_Right",StringComparison.Ordinal)).Value;
        var before=low.Contacts.Sample(.4f,true);var after=low.Contacts.Sample(.65f,true);
        Assert.Equal(1,before.Weight);Assert.Equal(before.LedgeAnchor,after.LedgeAnchor);
        Assert.Equal(0,low.Contacts.Sample(.5f,false).Weight);
        Assert.Throws<ArgumentException>(()=>AlsMantlingContactCompiler.Compile(json,animation+" ",roots));
        Assert.Throws<ArgumentException>(()=>AlsMantlingContactCompiler.Compile(json,animation,roots+" "));
        var changed=JsonNode.Parse(json)!;changed["sequences"]![0]!["keys"]!.AsArray().RemoveAt(10);
        Assert.Throws<ArgumentException>(()=>AlsMantlingContactCompiler.Compile(changed.ToJsonString(),animation,roots));
    }
}
