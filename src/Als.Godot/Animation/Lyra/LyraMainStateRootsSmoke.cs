using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainStateRootsSmoke : Node
{
    public override void _Ready()
    {
        try { LyraMainSourceStopSmoke.RunJoint(stateRoots:true); GetTree().Quit(); }
        catch(Exception error) { GD.PushError("Main state roots failed: "+error); GetTree().Quit(1); }
    }
}
