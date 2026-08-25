using Godot;
using GodotAls.Core.Contracts;

namespace GodotAls;

public partial class HeadlessSmoke : Node
{
    public override void _Ready()
    {
        var identity = new AlsFrameIdentity(0, 0, 1);

        GD.Print(
            $"GODOT_ALS_P0_OK frame={identity.FrameId} " +
            $"generation={identity.SlotGeneration}");

        GetTree().Quit();
    }
}
