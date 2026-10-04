using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraCycleRuntimeSmoke : Node
{
    public override void _Ready()
    {
        try { LyraCycleLayerSourceSmoke.Run(true, true, true, true, true); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Original Cycle runtime failed: " + error); GetTree().Quit(1); }
    }
}
