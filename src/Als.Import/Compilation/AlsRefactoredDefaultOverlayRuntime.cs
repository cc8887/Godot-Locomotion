using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original Default Overlay layer. The caller registers SourceInput in
/// its shared source-player batch, then evaluates and commits both frame owners.</summary>
public sealed class AlsRefactoredDefaultOverlayRuntime
{
    private readonly AlsRefactoredDefaultOverlayProfile _profile;
    private readonly int _playerId;
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsRefactoredDefaultOverlayState _state, _candidate;
    private AlsRefactoredDefaultOverlayInput _input;
    private AlsRefactoredSourcePlayerInput _sourceInput;
    private long _committedFrame = -1, _frame;
    private bool _prepared, _evaluated, _faulted;
    public AlsRefactoredDefaultOverlayState CommittedState => _state;
    public AlsRefactoredDefaultOverlayState CandidateState => _prepared ? _candidate : throw new InvalidOperationException("No Overlay candidate.");
    public AlsRefactoredSourcePlayerInput SourceInput => _prepared ? _sourceInput : throw new InvalidOperationException("No Overlay candidate.");
    public ReadOnlySpan<AlsPrecisePose> Pose => _prepared && _evaluated && !_faulted ? _pose : throw new InvalidOperationException("Overlay not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _prepared && _evaluated && !_faulted ? _curves : throw new InvalidOperationException("Overlay not evaluated.");

    internal AlsRefactoredDefaultOverlayRuntime(AlsRefactoredDefaultOverlayProfile profile, int playerId)
    {
        if (playerId < 0) throw new ArgumentOutOfRangeException(nameof(playerId));
        _profile = profile; _playerId = playerId;
        _pose = new AlsPrecisePose[profile.BoneNames.Length]; _curves = new AlsInertialCurve[profile.CurveNames.Length];
    }

    public void Prepare(long frame, in AlsRefactoredDefaultOverlayInput input, float delta, float weight = 1, bool reinitialize = false)
    {
        if (_prepared || frame <= _committedFrame || !float.IsFinite(weight) || weight is < 0 or > 1)
            throw new ArgumentException("Invalid Default Overlay frame.");
        var candidate = AlsRefactoredDefaultOverlay.Advance(reinitialize ? default : _state, input, delta);
        _sourceInput = new(_playerId, default, 1, weight * .75f, reinitialize);
        _input = input; _candidate = candidate; _frame = frame;
        _prepared = true; _evaluated = false; _faulted = false;
    }

    public void Evaluate(long frame, AlsRefactoredSourcePlayerRuntime sources)
    {
        if (!_prepared || _faulted || frame != _frame) throw new ArgumentException("Wrong Overlay frame.");
        try
        {
            if (sources.CatalogDigest != _profile.CatalogDigest || sources.Source(_playerId) != AlsRefactoredDefaultOverlayProfile.IdleSource ||
                !sources.BoneNames(_playerId).SequenceEqual(_profile.BoneNames) ||
                !sources.CurveNames(_playerId).SequenceEqual(_profile.IdleCurveNames))
                throw new ArgumentException("Foreign Default Overlay source binding.");
            sources.Evaluate(frame, _playerId);
            Span<AlsPrecisePose> frames = stackalloc AlsPrecisePose[3];
            for (var bone = 0; bone < _pose.Length; bone++)
            {
                for (var f = 0; f < 3; f++) frames[f] = _profile.Poses[f * _pose.Length + bone];
                var basis = AlsRefactoredDefaultOverlay.Bone(frames, _profile.Reference[bone], _input, _candidate);
                _pose[bone] = AlsPrecisePoseBlender.LocalApply(basis, sources.Pose(_playerId)[bone], .75f);
            }
            Span<AlsInertialCurve> curves = stackalloc AlsInertialCurve[3];
            for (var curve = 0; curve < _curves.Length; curve++)
            {
                for (var f = 0; f < 3; f++) curves[f] = _profile.Curves[f * _curves.Length + curve];
                var basis = AlsRefactoredDefaultOverlay.Curve(curves, _input, _candidate);
                var index = _profile.IdleCurveMap[curve];
                _curves[curve] = index < 0 ? basis : AlsStandingCycleCurves.Accumulate(basis, sources.Curves(_playerId)[index], .75f);
            }
            _evaluated = true;
        }
        catch { _faulted = true; throw; }
    }

    // Update-only frames are legal when a downstream full-weight slot skips Evaluate.
    public void ValidateCommit(long frame)
    { if (!_prepared || _faulted || frame != _frame) throw new ArgumentException("Overlay cannot commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _state = _candidate; _committedFrame = frame; Cancel(); }
    public void Cancel() { _prepared = false; _evaluated = false; _faulted = false; }
}
