using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredCachedOverlayRuntime
{
    private readonly AlsRefactoredCachedOverlayProfile _profile;
    private readonly int _player;
    private readonly AlsOverlayActionMix _mix = new();
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsOverlayActionBlendState _actions, _nextActions;
    private AlsRefactoredDefaultOverlayState _prediction, _nextPrediction;
    private AlsRefactoredDefaultOverlayInput _input;
    private AlsRefactoredSourcePlayerInput _sourceInput;
    private long _frame, _committed = -1;
    private bool _prepared, _evaluated, _faulted;
    private AlsRefactoredSourcePlayerRuntime? _sampledSource;
    public AlsOverlayActionBlendState CommittedActions => _actions;
    public AlsRefactoredDefaultOverlayState CommittedPrediction => _prediction;
    public AlsRefactoredDefaultOverlayState Prediction => _prepared ? _nextPrediction : throw new InvalidOperationException("No cache frame.");
    public Vector4 Weights => _prepared ? _nextActions.Weights : throw new InvalidOperationException("No cache frame.");
    public float CacheWeight => _prepared ? MathF.Max(MathF.Max(Weights.X, Weights.Y), MathF.Max(Weights.Z, Weights.W)) : throw new InvalidOperationException("No cache frame.");
    public AlsRefactoredSourcePlayerInput SourceInput => _prepared ? _sourceInput : throw new InvalidOperationException("No cache frame.");
    public ReadOnlySpan<AlsPrecisePose> Pose => _prepared && _evaluated && !_faulted ? _pose : throw new InvalidOperationException("Cached Overlay not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _prepared && _evaluated && !_faulted ? _curves : throw new InvalidOperationException("Cached Overlay not evaluated.");
    internal AlsRefactoredCachedOverlayRuntime(AlsRefactoredCachedOverlayProfile profile, int player)
    {
        if (player < 0) throw new ArgumentOutOfRangeException(nameof(player));
        _profile = profile; _player = player;
        _pose = new AlsPrecisePose[profile.BoneNames.Length]; _curves = new AlsInertialCurve[profile.CurveNames.Length];
    }
    public void Prepare(long frame, AlsOverlayAction action, in AlsRefactoredDefaultOverlayInput input, float delta, bool reinitialize = false)
    {
        if (_prepared || frame <= _committed) throw new ArgumentException("Invalid cached Overlay frame.");
        // Validate caller input even for fields unused by the Barrel graph.
        _ = AlsRefactoredDefaultOverlay.Advance(default, input, delta);
        var effective = _profile.UsesAir ? input : input with { InAir = 0, GroundPrediction = 0 };
        var prediction = AlsRefactoredDefaultOverlay.Advance(reinitialize ? default : _prediction, effective, delta);
        var actions = AlsOverlayActionBlend.Advance(reinitialize ? default : _actions, action, delta, new(.3f, .1f, 0, .1f)).State;
        _mix.Prepare(actions.Weights);
        var w = actions.Weights; var maximum = MathF.Max(MathF.Max(w.X, w.Y), MathF.Max(w.Z, w.W));
        // All action consumers reference one SaveCachedPose. UE defers its
        // update and selects the highest-weight context, never the sum.
        _sourceInput = new(_player, default, 1, maximum * _profile.IdleAlpha, reinitialize);
        _input = effective; _nextActions = actions; _nextPrediction = prediction; _frame = frame;
        _prepared = true; _evaluated = false; _faulted = false;
    }
    public void Evaluate(long frame, AlsRefactoredSourcePlayerRuntime players)
    {
        if (!_prepared || _faulted || frame != _frame) throw new ArgumentException("Invalid cache evaluation frame.");
        try
        {
            players.ValidateCommit(frame);
            if (players.CatalogDigest != _profile.CatalogDigest || players.Source(_player) != AlsRefactoredDefaultOverlayProfile.IdleSource ||
                !players.BoneNames(_player).SequenceEqual(_profile.BoneNames) || !players.CurveNames(_player).SequenceEqual(_profile.IdleNames))
                throw new ArgumentException("Foreign cached Overlay player.");
            if (_evaluated)
            {
                if (!ReferenceEquals(_sampledSource, players)) throw new ArgumentException("Cached pose source owner changed within frame.");
                return;
            }
            players.Evaluate(frame, _player);
            Span<AlsPrecisePose> frames = stackalloc AlsPrecisePose[3]; Span<AlsPrecisePose> branches = stackalloc AlsPrecisePose[4];
            for (var bone = 0; bone < _pose.Length; bone++)
            {
                for (var f = 0; f < 3; f++) frames[f] = _profile.Poses[f * _pose.Length + bone];
                var cached = AlsPrecisePoseBlender.LocalApply(AlsRefactoredDefaultOverlay.Bone(frames, _profile.Reference[bone], _input, _nextPrediction),
                    players.Pose(_player)[bone], _profile.IdleAlpha);
                branches.Fill(cached); _pose[bone] = _mix.Pose(branches);
            }
            Span<AlsInertialCurve> source = stackalloc AlsInertialCurve[3]; Span<AlsInertialCurve> branchCurves = stackalloc AlsInertialCurve[4];
            for (var c = 0; c < _curves.Length; c++)
            {
                for (var f = 0; f < 3; f++) source[f] = _profile.Curves[f * _curves.Length + c];
                var cached = AlsRefactoredDefaultOverlay.Curve(source, _input, _nextPrediction); var idle = _profile.IdleMap[c];
                if (idle >= 0) cached = AlsStandingCycleCurves.Accumulate(cached, players.Curves(_player)[idle], _profile.IdleAlpha);
                branchCurves.Fill(cached);
                if (_profile.CurveNames[c] == "LayerArmLeft" || _profile.BothArms && _profile.CurveNames[c] == "LayerArmRight")
                    branchCurves[2] = branchCurves[3] = AlsStandingCycleCurves.ModifyBlend(cached, 3, 1);
                _curves[c] = _mix.Curve(branchCurves);
            }
            _sampledSource = players; _evaluated = true;
        }
        catch { _faulted = true; throw; }
    }
    public void ValidateCommit(long frame)
    { if (!_prepared || _faulted || frame != _frame) throw new ArgumentException("Cached Overlay cannot commit."); }
    public void Commit(long frame)
    { ValidateCommit(frame); _actions = _nextActions; _prediction = _nextPrediction; _committed = frame; Cancel(); }
    public void Cancel() { _prepared = false; _evaluated = false; _faulted = false; _sampledSource = null; }
}
