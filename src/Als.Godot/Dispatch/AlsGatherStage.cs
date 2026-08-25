using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Simulation;

namespace GodotAls.Dispatch;

public partial class AlsGatherStage : Node
{
    private AlsHarnessContext _context = null!;

    public void Configure(AlsHarnessContext context)
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

        var measureAllocations = frameId > _context.WarmupFrames;
        var allocatedBefore = measureAllocations
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0;

        for (var index = 0; index < _context.Entries.Length; index++)
        {
            var entry = _context.Entries[index];
            var identity = new AlsFrameIdentity(
                frameId,
                entry.Handle.CharacterId,
                entry.Handle.Generation);
            entry.Exchange.PublishInput(AlsSyntheticInputSource.Create(identity, 1f / 60f));
        }

        Volatile.Write(ref _context.PublishedFrameId, frameId);

        if (measureAllocations)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Interlocked.Add(ref _context.GatherAllocations, allocated);
            Interlocked.Add(ref _context.SteadyStateAllocations, allocated);
        }
    }
}
