using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainStateHistorySmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var inventory=LyraLocomotionLayerInventory.Load(LyraSourceNodeCatalog.Load());
            _=inventory.MainDefaults.GetProperty("fields").GetProperty("StartDirection").GetProperty("value");
            GD.Print($"LYRA_LOCOMOTION_CLOSURES_GODOT_OK providers={inventory.Providers.Count} layers=30 nodes={inventory.ClosureNodes} sourceOccurrences={inventory.SourceOccurrences} compiledStateLinks={inventory.CompiledStateLinks} missingSequences={inventory.MissingSequences.Count} runtime=false");
            LyraMainSourceStopSmoke.RunJoint(stateRoots:true,graphHistory:true); GetTree().Quit();
        }
        catch(Exception error) { GD.PushError("Main state history failed: "+error); GetTree().Quit(1); }
    }
}
