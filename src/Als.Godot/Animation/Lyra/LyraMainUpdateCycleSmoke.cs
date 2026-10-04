using Godot;
namespace GodotAls.Animation.Lyra;
public partial class LyraMainUpdateCycleSmoke : Node
{
    public override void _Ready()
    {
        try { LyraCycleLayerSourceSmoke.Run(true, true, true, true, true, true, true); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Complete Main/Cycle failed: " + error); GetTree().Quit(1); }
    }
}
