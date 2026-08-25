using Godot;
using GodotAls.Core.Exchange;

namespace GodotAls.Dispatch;

public partial class P1DispatchHarness : Node
{
    private AlsHarnessContext _context = null!;

    public override void _Ready()
    {
        if (!TryReadArguments(out var mode, out var characterCount, out var frames))
        {
            GetTree().Quit(2);
            return;
        }

        _context = new AlsHarnessContext(
            mode,
            characterCount,
            frames,
            System.Environment.CurrentManagedThreadId);

        var gather = new AlsGatherStage { Name = "Gather" };
        gather.Configure(_context);
        AddChild(gather);

        for (var index = 0; index < characterCount; index++)
        {
            AddCharacter(index, 0);
        }

        var commit = new AlsCommitStage { Name = "Commit" };
        commit.Configure(_context, this);
        AddChild(commit);
    }

    public void ReplaceCharacter(int entryIndex, long completedFrame)
    {
        var oldEntry = _context.Entries[entryIndex];
        oldEntry.Worker.QueueFree();

        if (!_context.Registry.Release(oldEntry.Handle))
        {
            throw new InvalidOperationException("Failed to release the current ALS slot handle.");
        }

        AddCharacter(entryIndex, completedFrame);
        _context.Replacements++;
    }

    private void AddCharacter(int entryIndex, long startFrame)
    {
        var handle = _context.Registry.Acquire();
        var entry = new AlsHarnessEntry(handle, new AlsFrameExchange(), startFrame);
        var worker = new AlsVisualWorkerRoot
        {
            Name = $"Character_{handle.CharacterId}_Generation_{handle.Generation}",
        };
        entry.Worker = worker;
        worker.Configure(_context, entry);
        _context.Entries[entryIndex] = entry;
        AddChild(worker);
    }

    private static bool TryReadArguments(
        out AlsHarnessMode mode,
        out int characterCount,
        out int frames)
    {
        mode = AlsHarnessMode.Parallel;
        characterCount = 1;
        frames = 90;

        foreach (var argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--als-mode=", StringComparison.Ordinal))
            {
                var value = argument["--als-mode=".Length..];
                if (value == "single")
                {
                    mode = AlsHarnessMode.Single;
                }
                else if (value == "parallel")
                {
                    mode = AlsHarnessMode.Parallel;
                }
                else
                {
                    GD.PushError($"Invalid ALS harness mode: {value}");
                    return false;
                }
            }
            else if (argument.StartsWith("--als-characters=", StringComparison.Ordinal))
            {
                var value = argument["--als-characters=".Length..];
                if (!int.TryParse(value, out characterCount) || characterCount is < 1 or > 32)
                {
                    GD.PushError($"Invalid ALS character count: {value}");
                    return false;
                }
            }
            else if (argument.StartsWith("--als-frames=", StringComparison.Ordinal))
            {
                var value = argument["--als-frames=".Length..];
                if (!int.TryParse(value, out frames) || frames is < 30 or > 600)
                {
                    GD.PushError($"Invalid ALS frame count: {value}");
                    return false;
                }
            }
        }

        return true;
    }
}
