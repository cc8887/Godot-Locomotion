using Godot;
using GodotAls.Dispatch;

namespace GodotAls.Locomotion;

// This group owns no scene pose. It invokes only the managed snapshot half of
// the animation owner; VisualWorker remains the exclusive Skeleton writer.
internal partial class AlsP3FootPrepareStage : Node
{
    private AlsP3WorkerRoot _worker = null!;
    internal void Configure(AlsP3WorkerRoot worker, AlsHarnessMode mode)
    {
        _worker = worker;
        ProcessThreadGroup = mode == AlsHarnessMode.Single
            ? ProcessThreadGroupEnum.MainThread : ProcessThreadGroupEnum.SubThread;
        ProcessThreadGroupOrder = 2;
    }
    public override void _PhysicsProcess(double delta) => _worker.PrepareSplitFootFrame();
}
