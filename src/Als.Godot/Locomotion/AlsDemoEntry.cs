using Godot;
using GodotAls.Animation;

namespace GodotAls.Locomotion;

// Bootstrap before instantiating the scene: child constructors assign process
// groups, so changing animation modes in the Demo's _Ready would be too late.
public partial class AlsDemoEntry : Node
{
    internal P4LocomotionDemo Demo { get; private set; } = null!;

    public override void _Ready()
    {
        try
        {
            AlsAnimationRuntimeOptions.ConfigureDemo();
            var scene = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn")
                ?? throw new InvalidOperationException("ALS Demo scene is missing.");
            Demo = scene.Instantiate<P4LocomotionDemo>();
            AddChild(Demo);
        }
        catch (Exception exception)
        {
            GD.PushError($"ALS Demo initialization failed: {exception}");
            GetTree().Quit(1);
        }
    }
}
