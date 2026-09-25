using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Movement envelope only. The caller supplies the direction pose in
/// the frozen layout and schedules this owner once for the selected cache context.</summary>
public sealed class AlsRefactoredMovementCacheRuntime
{
    private readonly AlsRefactoredMovementCacheProfile _profile;
    private readonly AlsRefactoredBlendEvaluatorRuntime _lean;
    private readonly string[] _baseCurves, _curveNames;
    private readonly int[] _baseMap, _leanMap;
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private bool _initialized, _nextInitialized, _prepared, _evaluated, _evaluationAttempted;
    private float _history, _nextHistory, _alpha;
    private long _frame, _committed = -1;
    public AlsRefactoredMovementCacheProfile Profile => _profile;
    public ReadOnlySpan<string> BoneNames => _profile.Lean.BoneNames;
    public ReadOnlySpan<string> CurveNames => _curveNames;
    public float Alpha => _prepared ? _alpha : throw new InvalidOperationException("No Movement candidate.");
    public Vector2 FilteredLean => _lean.CandidateInput;
    public ReadOnlySpan<AlsPrecisePose> Pose => _evaluated ? _pose : throw new InvalidOperationException("Movement not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated ? _curves : throw new InvalidOperationException("Movement not evaluated.");

    public AlsRefactoredMovementCacheRuntime(AlsRefactoredMovementCacheProfile profile,
        ReadOnlySpan<string> baseBones, ReadOnlySpan<string> baseCurves)
    {
        if (!baseBones.SequenceEqual(profile.Lean.BoneNames)) throw new ArgumentException("Foreign Movement bone layout.");
        _profile = profile; _lean = new(profile.Lean); _baseCurves = baseCurves.ToArray();
        if (_baseCurves.Any(string.IsNullOrEmpty) || _baseCurves.Distinct(StringComparer.OrdinalIgnoreCase).Count() != _baseCurves.Length)
            throw new ArgumentException("Invalid Movement curve layout.");
        _curveNames = _baseCurves.Concat(profile.Lean.CurveNames.ToArray()).Append("PoseMoving").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _baseMap = _curveNames.Select(n => Array.FindIndex(_baseCurves, c => c.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        var leanNames = profile.Lean.CurveNames.ToArray();
        _leanMap = _curveNames.Select(n => Array.FindIndex(leanNames, c => c.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        _pose = new AlsPrecisePose[baseBones.Length]; _curves = new AlsInertialCurve[_curveNames.Length];
    }
    public void Prepare(long frame, float runningAmount, Vector2 lean, float delta, bool reinitialize = false)
    {
        if (_prepared || frame < 0 || frame <= _committed) throw new ArgumentException("Invalid Movement frame.");
        _nextInitialized = !reinitialize && _initialized; _nextHistory = reinitialize ? 0 : _history;
        _alpha = AlsOverlayPoseWeights.Alpha(runningAmount, _profile.AlphaPolicy, delta, ref _nextInitialized, ref _nextHistory);
        // Original evaluator uses X=right, Y=forward, normalized time zero and no sync group.
        _lean.Prepare(frame, lean, delta, 0, reinitialize);
        _frame = frame; _prepared = true; _evaluated = false; _evaluationAttempted = false;
    }
    public void Evaluate(long frame, ReadOnlySpan<AlsPrecisePose> direction, ReadOnlySpan<AlsInertialCurve> curves)
    {
        _evaluated = false; _evaluationAttempted = true;
        if (!_prepared || frame != _frame || direction.Length != _pose.Length || curves.Length != _baseCurves.Length)
            throw new ArgumentException("Invalid Movement direction pose.");
        foreach (var p in direction) p.Validate();
        foreach (var c in curves) if (c.Present && !float.IsFinite(c.Value)) throw new ArgumentException("Invalid Movement curve.");
        _lean.Evaluate(frame);
        for (var b = 0; b < _pose.Length; b++) _pose[b] = AlsPrecisePoseBlender.LocalApply(direction[b], _lean.Pose[b], _alpha);
        for (var c = 0; c < _curves.Length; c++)
        {
            var value = _baseMap[c] < 0 ? default : curves[_baseMap[c]];
            if (_leanMap[c] >= 0) value = AlsStandingCycleCurves.Accumulate(value, _lean.Curves[_leanMap[c]], _alpha);
            _curves[c] = _curveNames[c].Equals("PoseMoving", StringComparison.OrdinalIgnoreCase)
                ? AlsStandingCycleCurves.ModifyBlend(value, 1, 1) : value;
        }
        _evaluated = true;
    }
    public void ValidateCommit(long frame)
    {
        if (!_prepared || (_evaluationAttempted && !_evaluated) || frame != _frame) throw new ArgumentException("Incomplete Movement frame.");
        _lean.ValidateCommit(frame);
    }
    public void Commit(long frame)
    {
        ValidateCommit(frame); _lean.Commit(frame); _initialized = _nextInitialized; _history = _nextHistory;
        _committed = frame; Cancel();
    }
    public void Cancel() { _lean.Cancel(); _prepared = _evaluated = _evaluationAttempted = false; }
}
