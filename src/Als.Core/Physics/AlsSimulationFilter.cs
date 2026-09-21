namespace GodotAls.Core.Physics;

// Native channels are a separate domain from the host scene's routing layers.
// Keep all 64 bits: UE channels must not be truncated to Godot's 32-bit layers.
public readonly record struct AlsSimulationFilter(ulong Channel, ulong BlockChannels)
{
    public static AlsSimulationFilter Unrestricted => new(ulong.MaxValue, ulong.MaxValue);
    public static AlsSimulationFilter WorldStatic => new(1, ulong.MaxValue);
    public bool Allows(AlsSimulationFilter other) =>
        (Channel & other.BlockChannels) != 0 && (other.Channel & BlockChannels) != 0;
}
