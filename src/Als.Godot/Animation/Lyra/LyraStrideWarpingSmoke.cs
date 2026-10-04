using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraStrideWarpingSmoke : Node
{
    public override void _Ready()
    {
        try { LyraCycleLayerSourceSmoke.Run(true, true, true, true); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Stride warping failed: " + error); GetTree().Quit(1); }
    }
}
