using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Dispatch;

public partial class P2bRealRigHarness : Node
{
    private const string MannequinAssetId = "86d98d8177feb473c8a5f406c5b42f8c2a2f7b07";
    private const string WalkClipId = "6124eafdcbeaaf04bca366add34c821faa0e4963";

    private AlsRealRigHarnessContext _context = null!;

    public override void _Ready()
    {
        try
        {
            if (!TryReadArguments(out var mode, out var characterCount, out var frames))
            {
                GetTree().Quit(2);
                return;
            }

            var manifest = AlsManifestSerializer.Load(ProjectSettings.GlobalizePath(AlsGodotImportCoordinator.ManifestPath));
            var definition = AlsAnimationSetCompiler.Compile(manifest);
            var mannequin = definition.SkeletalMeshes[
                definition.AssetIndex.GetSkeletalMeshId(MannequinAssetId)];
            var walk = definition.Animations[definition.AssetIndex.GetAnimationId(WalkClipId)];
            var mannequinScene = LoadScene(mannequin.ResourcePath, mannequin.Name);
            var walkScene = LoadScene(walk.ResourcePath, walk.Name);
            _context = new AlsRealRigHarnessContext(
                mode,
                characterCount,
                frames,
                System.Environment.CurrentManagedThreadId,
                mannequinScene,
                walkScene,
                walk,
                definition.Skeletons[walk.SkeletonId]);

            var gather = new AlsRealRigGatherStage { Name = "Gather" };
            gather.Configure(_context);
            AddChild(gather);
            for (var index = 0; index < characterCount; index++)
            {
                AddCharacter(index);
            }

            var commit = new AlsRealRigCommitStage { Name = "Commit" };
            commit.Configure(_context, this);
            AddChild(commit);
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    public void ReplaceCharacter(int entryIndex)
    {
        var oldEntry = _context.Entries[entryIndex];
        oldEntry.Worker.QueueFree();
        if (!_context.Registry.Release(oldEntry.Handle))
        {
            throw new InvalidOperationException("Failed to release the current real-rig slot handle.");
        }

        AddCharacter(entryIndex);
        _context.Replacements++;
    }

    private void AddCharacter(int entryIndex)
    {
        var entry = new AlsRealRigHarnessEntry(_context.Registry.Acquire(), new AlsRealRigExchange());
        var worker = new AlsRealRigWorkerRoot
        {
            Name = $"RealRig_{entry.Handle.CharacterId}_Generation_{entry.Handle.Generation}",
        };
        entry.Worker = worker;
        worker.Configure(_context, entry);
        if (!worker.IsWarm)
        {
            throw new InvalidOperationException("Real-rig worker did not complete warmup.");
        }
        _context.Entries[entryIndex] = entry;
        AddChild(worker);
    }

    private static PackedScene LoadScene(string resourcePath, string assetName) =>
        ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(resourcePath))
        ?? throw new InvalidOperationException($"Unable to load real-rig scene: {assetName}");

    private static bool TryReadArguments(
        out AlsHarnessMode mode,
        out int characterCount,
        out int frames)
    {
        mode = AlsHarnessMode.Parallel;
        characterCount = 1;
        frames = 120;
        foreach (var argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("--als-mode=", StringComparison.Ordinal))
            {
                mode = argument["--als-mode=".Length..] switch
                {
                    "single" => AlsHarnessMode.Single,
                    "parallel" => AlsHarnessMode.Parallel,
                    var value => throw new ArgumentException($"Invalid ALS real-rig mode: {value}"),
                };
            }
            else if (argument.StartsWith("--als-characters=", StringComparison.Ordinal))
            {
                var value = argument["--als-characters=".Length..];
                if (!int.TryParse(value, out characterCount) || characterCount is not (1 or 10))
                {
                    throw new ArgumentException($"Invalid ALS real-rig character count: {value}");
                }
            }
            else if (argument.StartsWith("--als-frames=", StringComparison.Ordinal))
            {
                var value = argument["--als-frames=".Length..];
                if (!int.TryParse(value, out frames) || frames != 120)
                {
                    throw new ArgumentException($"Invalid ALS real-rig frame count: {value}");
                }
            }
        }
        return true;
    }
}

public partial class AlsRealRigGatherStage : Node
{
    private AlsRealRigHarnessContext _context = null!;

    public void Configure(AlsRealRigHarnessContext context)
    {
        _context = context;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        var frameId = Volatile.Read(ref _context.PublishedFrameId) + 1;
        if (frameId > _context.TargetFrames)
        {
            return;
        }

        foreach (var entry in _context.Entries)
        {
            var identity = new AlsFrameIdentity(frameId, entry.Handle.CharacterId, entry.Handle.Generation);
            entry.Exchange.PublishInput(new AlsRealRigInput(identity, 1.0 / 60.0));
        }
        Volatile.Write(ref _context.PublishedFrameId, frameId);
    }
}

public partial class AlsRealRigCommitStage : Node
{
    private const ulong Prime = 1099511628211UL;

    private AlsRealRigHarnessContext _context = null!;
    private P2bRealRigHarness _harness = null!;

    public void Configure(AlsRealRigHarnessContext context, P2bRealRigHarness harness)
    {
        _context = context;
        _harness = harness;
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 2;
    }

    public override void _PhysicsProcess(double delta)
    {
        var frameId = Volatile.Read(ref _context.PublishedFrameId);
        if (frameId <= 0 || frameId > _context.TargetFrames)
        {
            return;
        }

        foreach (var entry in _context.Entries)
        {
            var identity = new AlsFrameIdentity(frameId, entry.Handle.CharacterId, entry.Handle.Generation);
            if (!entry.Exchange.TryConsumeResult(identity, out var result, out var stale))
            {
                _context.MissingResults++;
                _context.StaleResults += stale ? 1 : 0;
                continue;
            }
            AppendResult(ref _context.Digest, result);
        }

        if (frameId == 60)
        {
            _harness.ReplaceCharacter(0);
        }
        if (frameId == _context.TargetFrames)
        {
            Finish();
        }
    }

    private void Finish()
    {
        var offMainWorkers = _context.Entries.Sum(entry => Volatile.Read(ref entry.ObservedOffMainThread));
        var valid = _context.MissingResults == 0 &&
            _context.StaleResults == 0 &&
            _context.Replacements == 1 &&
            (_context.Mode == AlsHarnessMode.Single
                ? offMainWorkers == 0
                : offMainWorkers == _context.Entries.Length);
        var mode = _context.Mode == AlsHarnessMode.Single ? "single" : "parallel";
        var marker = valid ? "GODOT_ALS_P2B_RIG_OK" : "GODOT_ALS_P2B_RIG_FAIL";
        GD.Print(
            $"{marker} mode={mode} characters={_context.Entries.Length} frames={_context.TargetFrames} " +
            $"digest={_context.Digest:X16} missing={_context.MissingResults} stale={_context.StaleResults} " +
            $"off_main={offMainWorkers} replacements={_context.Replacements}");
        GetTree().Quit(valid ? 0 : 1);
    }

    private static void AppendResult(ref ulong digest, AlsRealRigResult result)
    {
        Append(ref digest, result.Identity.FrameId);
        Append(ref digest, result.Identity.CharacterId);
        Append(ref digest, result.Identity.SlotGeneration);
        Append(ref digest, result.PoseDigest);
    }

    private static void Append(ref ulong digest, long value) => Append(ref digest, unchecked((ulong)value));

    private static void Append(ref ulong digest, uint value) => Append(ref digest, (ulong)value);

    private static void Append(ref ulong digest, ulong value)
    {
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= Prime;
        }
    }
}
