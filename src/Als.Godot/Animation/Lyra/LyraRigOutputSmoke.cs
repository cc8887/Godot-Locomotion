using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraRigOutputSmoke : Node
{
    public override void _Ready()
    {
        try { LyraRigSolverSmoke.Run(compareOutputs: true); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Rig output failed: " + e); GetTree().Quit(1); }
    }
}
