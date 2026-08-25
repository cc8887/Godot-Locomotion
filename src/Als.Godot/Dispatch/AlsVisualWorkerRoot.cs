using System.Diagnostics;
using System.Threading;
using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Simulation;

namespace GodotAls.Dispatch;

public partial class AlsVisualWorkerRoot : Node
{
    private AlsHarnessContext _context = null!;
    private AlsHarnessEntry _entry = null!;
    private Skeleton3D _skeleton = null!;
    private AlsRuntimeState _runtimeState;
    private AlsFrameResult _result;
    private int _pelvisBone;

    public void Configure(AlsHarnessContext context, AlsHarnessEntry entry)
    {
        _context = context;
        _entry = entry;
        ProcessThreadGroup = context.Mode == AlsHarnessMode.Parallel
            ? ProcessThreadGroupEnum.SubThread
            : ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 1;
        BuildSyntheticRig();
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

        var measureAllocations = frameId > _context.WarmupFrames &&
            frameId > _entry.StartFrame + 2;
        var allocatedBefore = measureAllocations
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0;
        var started = Stopwatch.GetTimestamp();

        AlsSyntheticLocomotionModel.Evaluate(input, ref _runtimeState, ref _result);
        _skeleton.SetBonePosePosition(
            _pelvisBone,
            new Vector3(0f, _result.PelvisTarget.Y, 0f));

        _result.WorkerElapsedTicks = Stopwatch.GetTimestamp() - started;
        _entry.Exchange.PublishResult(_result);

        if (measureAllocations)
        {
            Interlocked.Add(
                ref _context.SteadyStateAllocations,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        }

        if (_context.Mode == AlsHarnessMode.Parallel &&
            System.Environment.CurrentManagedThreadId != _context.MainManagedThreadId)
        {
            Interlocked.Exchange(ref _entry.ObservedOffMainThread, 1);
        }
    }

    private void BuildSyntheticRig()
    {
        _skeleton = new Skeleton3D { Name = "SyntheticSkeleton" };
        var rootBone = _skeleton.AddBone("root");
        _skeleton.SetBoneRest(rootBone, Transform3D.Identity);
        _pelvisBone = _skeleton.AddBone("pelvis");
        _skeleton.SetBoneParent(_pelvisBone, rootBone);
        _skeleton.SetBoneRest(
            _pelvisBone,
            new Transform3D(Basis.Identity, new Vector3(0f, 1f, 0f)));
        AddChild(_skeleton);
    }
}
