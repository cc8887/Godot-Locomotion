using GodotAls.Core.Contracts;

namespace GodotAls.Locomotion;

public interface IAlsLocomotionCommandSource
{
    AlsLocomotionCommand GetCommand(long frameId);
}

public sealed class AlsReplayInputAdapter : IAlsLocomotionCommandSource
{
    private readonly AlsLocomotionCommand[] _commands;

    public AlsReplayInputAdapter(long firstFrameId, ReadOnlySpan<AlsLocomotionCommand> commands)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstFrameId);
        if (commands.IsEmpty)
        {
            throw new ArgumentException("Replay command sequence must not be empty.", nameof(commands));
        }

        _ = checked(firstFrameId + commands.Length - 1L);
        FirstFrameId = firstFrameId;
        _commands = commands.ToArray();
    }

    public long FirstFrameId { get; }

    public long LastFrameId => FirstFrameId + _commands.Length - 1L;

    public AlsLocomotionCommand GetCommand(long frameId)
    {
        var index = frameId - FirstFrameId;
        if ((ulong)index >= (ulong)_commands.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frameId),
                $"Frame {frameId} is outside replay range [{FirstFrameId}, {LastFrameId}].");
        }

        return _commands[index];
    }
}
