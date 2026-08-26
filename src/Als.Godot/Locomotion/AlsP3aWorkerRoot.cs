using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

public partial class AlsP3aWorkerRoot : Node
{
    private AlsP3aHarnessContext _context = null!;
    private AlsP3aHarnessEntry _entry = null!;
    private AlsRuntimeState _runtimeState;
    private AlsFrameResult _result;

    public void Configure(AlsP3aHarnessContext context, AlsP3aHarnessEntry entry)
    {
        _context = context;
        _entry = entry;
        ProcessThreadGroup = context.Mode == AlsHarnessMode.Parallel
            ? ProcessThreadGroupEnum.SubThread
            : ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 1;
    }

    public override void _PhysicsProcess(double delta)
    {
        try
        {
            var frameId = Volatile.Read(ref _context.PublishedFrameId);
            if (frameId <= 0 || frameId > AlsP3aHarnessContext.TotalFrames)
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

            var measure = frameId > AlsP3aHarnessContext.WarmupFrames;
            var isMainThread =
                System.Environment.CurrentManagedThreadId == _context.MainManagedThreadId;
            if ((_context.Mode == AlsHarnessMode.Single) != isMainThread)
            {
                Interlocked.Increment(ref _context.AffinityViolations);
            }
            if (!isMainThread)
            {
                Interlocked.Exchange(ref _entry.ObservedOffMainThread, 1);
            }

            var beforeModel = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
            AlsLocomotionModel.Evaluate(input, ref _runtimeState, ref _result, _context.Settings);
            if (measure)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeModel;
                if (allocated != 0)
                {
                    Interlocked.CompareExchange(
                        ref _context.FirstModelAllocationFrame,
                        frameId,
                        0);
                }
                Interlocked.Add(
                    ref _context.ModelAllocations,
                    allocated);
            }

            var beforeExchange = measure ? GC.GetAllocatedBytesForCurrentThread() : 0;
            _entry.Exchange.PublishResult(_result);
            _entry.PublishedResultCharacterId = checked((int)_result.Identity.CharacterId);
            _entry.PublishedResultGeneration = checked((int)_result.Identity.SlotGeneration);
            Volatile.Write(ref _entry.PublishedResultFrameId, _result.Identity.FrameId);
            Volatile.Write(ref _entry.HasPublishedResult, 1);
            if (measure)
            {
                Interlocked.Add(
                    ref _context.ExchangeAllocations,
                    GC.GetAllocatedBytesForCurrentThread() - beforeExchange);
            }

        }
        catch (Exception exception)
        {
            _context.RecordFailure(exception);
        }
    }
}
