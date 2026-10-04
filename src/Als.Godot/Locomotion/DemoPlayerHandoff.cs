using Godot;

namespace GodotAls.Locomotion;

// World metres and Godot yaw. Each motor rebuilds its own floor, base and pose history.
internal readonly record struct DemoPlayerHandoff(Vector3 Feet, float Yaw, Vector3 Velocity,
    bool Crouching, bool Grounded)
{
    internal void Validate()
    {
        if (!Feet.IsFinite() || !Velocity.IsFinite() || !float.IsFinite(Yaw))
            throw new ArgumentException("Player handoff must contain finite world state.");
    }
}
