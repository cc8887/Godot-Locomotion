using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraRigTargetOutputSmoke : Node
{
    public override void _Ready()
    {
        try { LyraRigSolverSmoke.Run(compareOutputs: true, targetReference: true); GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Rig target output failed: " + e); GetTree().Quit(1); }
    }
}
