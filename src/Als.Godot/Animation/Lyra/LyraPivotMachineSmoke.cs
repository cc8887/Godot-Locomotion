using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraPivotMachineSmoke : Node
{
    public override void _Ready()
    {
        try {
            LyraPivotSourceSmoke.Run(machineMode:true);
            LyraPivotSourceSmoke.Run(machineMode:true,reentry:true);
            GetTree().Quit(); }
        catch (Exception e) { GD.PushError("Pivot machine failed: "+e); GetTree().Quit(1); }
    }
}
