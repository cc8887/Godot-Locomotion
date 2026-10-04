using Godot;
using GodotAls.Animation;

namespace GodotAls.Locomotion;

// Bootstrap before instantiating the scene: child constructors assign process
// groups, so changing animation modes in the Demo's _Ready would be too late.
public partial class AlsDemoEntry : Node
{
    internal P4LocomotionDemo Demo { get; private set; } = null!;
    internal DemoLocomotionSwitcher? Switcher { get; private set; }
    internal Action<P4LocomotionDemo>? ConfigureBeforeReady { get; init; }

    public override void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            bool initialLyra = arguments.Contains("--locomotion=lyra");
            if (initialLyra && (arguments.Contains("--lyra-main-smoke") || arguments.Contains("--lyra-terrain-smoke")))
            {
                if (ConfigureBeforeReady is not null) throw new InvalidOperationException("ALS fixture configuration cannot target a Lyra character.");
                var lyra = ResourceLoader.Load<PackedScene>("res://scenes/demo/lyra_locomotion_demo.tscn")
                    ?? throw new InvalidOperationException("Lyra Main Demo scene is missing.");
                AddChild(lyra.Instantiate<LyraLocomotionDemo>());
                return;
            }
            AlsAnimationRuntimeOptions.ConfigureDemo();
            var scene = ResourceLoader.Load<PackedScene>("res://scenes/demo/p4_locomotion_demo.tscn")
                ?? throw new InvalidOperationException("ALS Demo scene is missing.");
            Demo = scene.Instantiate<P4LocomotionDemo>();
            Demo.EnableNativeCamera = true;
            ConfigureBeforeReady?.Invoke(Demo);
            AddChild(Demo);
            if (Demo.IsRuntimeReady) AlsMantlingCourse.Add(Demo.GetNode<Node3D>("World"));
            if (ConfigureBeforeReady is null && Demo.IsRuntimeReady)
            {
                MarkLyraGround(Demo.GetNode<Node3D>("World"));
                Switcher = new DemoLocomotionSwitcher { Name = "PlayerSwitcher" };
                Switcher.Configure(Demo, initialLyra);
                AddChild(Switcher);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"ALS Demo initialization failed: {exception}");
            GetTree().Quit(1);
        }
    }

    private static void MarkLyraGround(Node node)
    {
        if (node is StaticBody3D or AnimatableBody3D)
            ((CollisionObject3D)node).CollisionLayer |= 4;
        foreach (var child in node.GetChildren()) MarkLyraGround(child);
    }
}
