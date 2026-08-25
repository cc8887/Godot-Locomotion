using System.Globalization;
using System.Threading;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;

namespace GodotAls.Dispatch;

public partial class AlsRealRigWorkerRoot : Node
{
    private static readonly string[] PoseBoneNames =
    [
        "root", "pelvis", "spine_03", "hand_l", "hand_r", "foot_l", "foot_r",
    ];

    private AlsRealRigHarnessContext _context = null!;
    private AlsRealRigHarnessEntry _entry = null!;
    private AlsBoundAnimation _bound = null!;

    public bool IsWarm { get; private set; }

    public void Configure(AlsRealRigHarnessContext context, AlsRealRigHarnessEntry entry)
    {
        _context = context;
        _entry = entry;
        ProcessThreadGroup = context.Mode == AlsHarnessMode.Parallel
            ? ProcessThreadGroupEnum.SubThread
            : ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 1;

        _bound = AlsAnimationBinder.Bind(
            context.MannequinScene,
            context.WalkScene,
            context.WalkClip,
            context.SkeletonDefinition);
        AddChild(_bound.Root);
        _bound.Player.Advance(0.0);
        _ = AlsPoseDigest.Compute(_bound.Skeleton, 0, PoseBoneNames);
        IsWarm = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        var frameId = Volatile.Read(ref _context.PublishedFrameId);
        if (frameId <= 0)
        {
            return;
        }

        var identity = new AlsFrameIdentity(
            frameId,
            _entry.Handle.CharacterId,
            _entry.Handle.Generation);
        if (!_entry.Exchange.TryReadInput(identity, out var input))
        {
            return;
        }

        _bound.Player.Advance(input.DeltaTime);
        var digestText = AlsPoseDigest.Compute(_bound.Skeleton, checked((int)frameId), PoseBoneNames);
        var digest = ulong.Parse(digestText.AsSpan(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        _entry.Exchange.PublishResult(new AlsRealRigResult(identity, digest));

        if (_context.Mode == AlsHarnessMode.Parallel &&
            System.Environment.CurrentManagedThreadId != _context.MainManagedThreadId)
        {
            Interlocked.Exchange(ref _entry.ObservedOffMainThread, 1);
        }
    }
}
