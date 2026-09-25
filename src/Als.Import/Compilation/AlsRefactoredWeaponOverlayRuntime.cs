using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Outer action lifecycle and pose. Source players remain in the caller's
/// shared batch; local machine/source/action candidates commit together.</summary>
public sealed class AlsRefactoredWeaponOverlayRuntime
{
    private readonly AlsRefactoredWeaponActionProfile _profile;
    private readonly AlsRefactoredWeaponMachineRuntime _machine;
    private readonly AlsRefactoredWeaponSourceRuntime _source;
    private readonly AlsRefactoredWeaponMachinePose _machinePose;
    private readonly AlsOverlayActionMix _mix = new();
    private readonly AlsPrecisePose[] _pose, _base;
    private readonly AlsInertialCurve[] _curves, _baseCurves;
    private AlsOverlayActionBlendState _state, _next;
    private AlsRefactoredSourcePlayerRuntime? _sampledSource;
    private bool _prepared, _evaluated, _faulted, _machineUpdated, _reset, _nextReset;
    private long _frame, _committed = -1;
    private float _pitch;
    public AlsOverlayActionBlendState CommittedActions => _state;
    public AlsOverlayActionBlendState CandidateActions => _prepared ? _next : throw new InvalidOperationException("No weapon action candidate.");
    public bool MachineUpdated => _prepared && _machineUpdated;
    public AlsRefactoredWeaponMachineRuntime Machine => MachineUpdated ? _machine : throw new InvalidOperationException("Weapon machine is hidden.");
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _prepared ? _machineUpdated ? _source.SourceInputs : [] : throw new InvalidOperationException("No weapon action candidate.");
    public ReadOnlySpan<AlsPrecisePose> Pose => _prepared && _evaluated && !_faulted ? _pose : throw new InvalidOperationException("Weapon overlay pose unavailable.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _prepared && _evaluated && !_faulted ? _curves : throw new InvalidOperationException("Weapon overlay curves unavailable.");
    public AlsRefactoredWeaponOverlayRuntime(AlsRefactoredWeaponActionProfile profile, int firstPlayer)
    {
        _profile = profile; var source = profile.MachinePose.SourceProfile;
        _machine = source.Machine.CreateRuntime(); _source = source.CreateRuntime(firstPlayer); _machinePose = new(profile.MachinePose);
        _pose = new AlsPrecisePose[profile.BoneNames.Length]; _base = new AlsPrecisePose[_pose.Length];
        _curves = new AlsInertialCurve[profile.CurveNames.Length]; _baseCurves = new AlsInertialCurve[profile.MachinePose.CurveNames.Length];
    }
    public void Prepare(long frame, string actionTag, in AlsRefactoredWeaponRuleInput rules, AlsRefactoredWeaponPoseInput input, float pitch, float delta, bool reinitialize = false)
    {
        if (_prepared || frame < 0 || frame <= _committed || !float.IsFinite(pitch) || pitch is < 0 or > 1) throw new ArgumentException("Invalid weapon overlay frame.");
        input.Validate();
        var next = AlsOverlayActionBlend.Advance(reinitialize ? default : _state, AlsOverlayActionBlend.Resolve(actionTag), delta, _profile.BlendTimes);
        var update = next.State.Weights.X > AlsPoseBlender.WeightThreshold || next.ZeroWeightPrevious == 0;
        var reset = _reset || reinitialize;
        try
        {
            if (update)
            {
                _machine.Prepare(frame, rules, delta, next.State.Weights.X, next.State.Weights.X <= AlsPoseBlender.WeightThreshold, reset);
                _source.Prepare(frame, _machine, input, delta); reset = false;
            }
            _mix.Prepare(next.State.Weights);
        }
        catch { _machine.Cancel(); _source.Cancel(); throw; }
        _next = next.State; _nextReset = reset; _machineUpdated = update; _pitch = pitch; _frame = frame;
        _prepared = true; _evaluated = false; _faulted = false; _sampledSource = null;
    }
    public void Evaluate(long frame, AlsRefactoredSourcePlayerRuntime players)
    {
        ValidateCommit(frame);
        try
        {
            players.ValidateCommit(frame);
            if (players.CatalogDigest != _profile.MachinePose.SourceProfile.Players.CatalogDigest) throw new ArgumentException("Foreign weapon overlay catalog.");
            if (_evaluated) { if (!ReferenceEquals(_sampledSource, players)) throw new ArgumentException("Weapon overlay source owner changed."); return; }
            var visible = _next.Weights.X > AlsPoseBlender.WeightThreshold;
            if (visible) _machinePose.Sample(frame, _pitch, _machine, _source, players, _base, _baseCurves);
            Span<AlsPrecisePose> poses = stackalloc AlsPrecisePose[4];
            for (var b = 0; b < _pose.Length; b++)
            {
                poses[0] = visible ? _base[b] : _profile.Poses[0][b];
                for (var i = 0; i < 3; i++) poses[i + 1] = _profile.Poses[i][b];
                _pose[b] = _mix.Pose(poses);
            }
            Array.Clear(_curves);
            if (visible) for (var c = 0; c < _baseCurves.Length; c++) _curves[_profile.MachineCurveMap[c]] = _baseCurves[c];
            Span<AlsInertialCurve> curves = stackalloc AlsInertialCurve[4];
            for (var c = 0; c < _curves.Length; c++)
            {
                curves[0] = _curves[c]; for (var i = 0; i < 3; i++) curves[i + 1] = _profile.Curves[i][c];
                _curves[c] = _mix.Curve(curves);
            }
            _sampledSource = players; _evaluated = true;
        }
        catch { _faulted = true; throw; }
    }
    public void ValidateCommit(long frame)
    {
        if (!_prepared || _faulted || frame != _frame) throw new ArgumentException("Invalid weapon overlay commit.");
        if (_machineUpdated) { _machine.ValidateCommit(frame); _source.ValidateCommit(frame); }
    }
    public void Commit(long frame)
    {
        ValidateCommit(frame);
        if (_machineUpdated) { _machine.Commit(frame); _source.Commit(frame); }
        _state = _next; _reset = _nextReset; _committed = frame; Cancel();
    }
    public void Cancel()
    {
        _machine.Cancel(); _source.Cancel(); _prepared = false; _evaluated = false; _faulted = false; _machineUpdated = false; _sampledSource = null;
    }
}
