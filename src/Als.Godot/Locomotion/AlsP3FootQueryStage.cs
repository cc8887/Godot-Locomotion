using Godot;

namespace GodotAls.Locomotion;

internal partial class AlsP3FootQueryStage : Node
{
    private AlsP3WorkerRoot _worker = null!;
    private AlsRefactoredFootQueryGather? _gather;
    internal void Configure(AlsP3WorkerRoot worker, AlsCharacterMotor motor)
    {
        _worker = worker;
        // UE Visibility: static, dynamic and destructible world layers. Exclude
        // the character capsule; the motor floor mask is a separate contract.
        _gather = new(motor, 7, [motor.GetRid()]);
        ProcessThreadGroup = ProcessThreadGroupEnum.MainThread;
        ProcessThreadGroupOrder = 3;
    }
    public override void _PhysicsProcess(double delta) => _worker.GatherSplitFootFrame(_gather!);
    public override void _ExitTree() { _gather?.Dispose(); _gather = null; }
}
