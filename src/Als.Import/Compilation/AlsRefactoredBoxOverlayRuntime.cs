using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredBoxOverlayRuntime
{
    private readonly AlsRefactoredBoxOverlayProfile _profile;
    private readonly int _player;
    private readonly AlsRefactoredSourcePlayerInput[] _source = new AlsRefactoredSourcePlayerInput[1];
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsOverlayActionBlendState _state, _next;
    private AlsRefactoredDefaultOverlayInput _input;
    private long _frame, _committed = -1;
    private bool _prepared, _evaluated, _faulted, _resetPending, _nextResetPending;
    private int _sourceCount;
    public AlsOverlayActionBlendState CommittedState => _state;
    public Vector4 Weights => _prepared ? _next.Weights : throw new InvalidOperationException("No Box Overlay candidate.");
    public ReadOnlySpan<AlsRefactoredSourcePlayerInput> SourceInputs => _prepared ? _source.AsSpan(0, _sourceCount) : throw new InvalidOperationException("No Box Overlay candidate.");
    public ReadOnlySpan<AlsPrecisePose> Pose => _prepared && _evaluated && !_faulted ? _pose : throw new InvalidOperationException("Box Overlay not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _prepared && _evaluated && !_faulted ? _curves : throw new InvalidOperationException("Box Overlay not evaluated.");
    internal AlsRefactoredBoxOverlayRuntime(AlsRefactoredBoxOverlayProfile profile, int player)
    {
        if (player < 0) throw new ArgumentOutOfRangeException(nameof(player));
        _profile = profile; _player = player;
        _pose = new AlsPrecisePose[profile.BoneNames.Length]; _curves = new AlsInertialCurve[profile.CurveNames.Length];
    }
    public void Prepare(long frame, AlsOverlayAction action, float walking, float standing, float crouching,
        float delta, bool reinitialize = false)
    {
        if (_prepared || frame <= _committed) throw new ArgumentException("Invalid Box frame.");
        var input = new AlsRefactoredDefaultOverlayInput(walking, standing, crouching, 0, 0);
        _ = AlsRefactoredDefaultOverlay.Advance(default, input, delta);
        var result = AlsOverlayActionBlend.Advance(reinitialize ? default : _state, action, delta, _profile.BlendTimes);
        _next = result.State; _input = input; _frame = frame;
        _sourceCount = _next.Weights.X > AlsPoseBlender.WeightThreshold || result.ZeroWeightPrevious == 0 ? 1 : 0;
        // The instant-transition old child update occurs before nonzero children.
        // With this graph only branch zero contains a player.
        _nextResetPending = _resetPending || reinitialize;
        _source[0] = new(_player, default, 1, _next.Weights.X * .75f, _nextResetPending);
        if (_sourceCount > 0) _nextResetPending = false;
        _prepared = true; _evaluated = false; _faulted = false;
    }
    public void Evaluate(long frame, AlsRefactoredSourcePlayerRuntime players)
    {
        if (!_prepared || _faulted || frame != _frame) throw new ArgumentException("Invalid Box evaluation frame.");
        try
        {
            players.ValidateCommit(frame);
            if (players.CatalogDigest != _profile.CatalogDigest || players.Source(_player) != AlsRefactoredDefaultOverlayProfile.IdleSource ||
                !players.BoneNames(_player).SequenceEqual(_profile.BoneNames) || !players.CurveNames(_player).SequenceEqual(_profile.IdleNames))
                throw new ArgumentException("Foreign Box player binding.");
            var baseActive = _next.Weights.X > AlsPoseBlender.WeightThreshold;
            if (baseActive) players.Evaluate(frame, _player);
            Span<int> active = stackalloc int[4]; var count = 0;
            for (var i = 0; i < 4; i++) if (_next.Weights[i] > AlsPoseBlender.WeightThreshold) active[count++] = i;
            var passThrough = count == 1 && _next.Weights[active[0]] >= 1 - AlsPoseBlender.WeightThreshold;
            var two = count == 2 && MathF.Abs(_next.Weights[active[0]] + _next.Weights[active[1]] - 1f) <= 1e-8f;
            Span<AlsPrecisePose> frames = stackalloc AlsPrecisePose[3];
            for (var bone = 0; bone < _pose.Length; bone++)
            {
                for (var f = 0; f < 3; f++) frames[f] = _profile.Poses[f * _pose.Length + bone];
                var value = default(AlsPrecisePose); var first = true;
                for (var branch = 0; branch < 4; branch++)
                {
                    var weight = _next.Weights[branch]; if (weight <= AlsPoseBlender.WeightThreshold) continue;
                    if (two && branch == active[1]) weight = 1f - _next.Weights[active[0]];
                    var pose = branch == 2 ? frames[2] : frames[0];
                    if (branch == 0) pose = AlsPrecisePoseBlender.LocalApply(
                        AlsRefactoredDefaultOverlay.Bone(frames, _profile.Reference[bone], _input, default), players.Pose(_player)[bone], .75f);
                    value = passThrough ? pose : first ? AlsPrecisePoseBlender.Scale(pose, weight) : AlsPrecisePoseBlender.Accumulate(value, pose, weight); first = false;
                }
                _pose[bone] = passThrough ? value : value.Normalized();
            }
            Span<AlsInertialCurve> frameCurves = stackalloc AlsInertialCurve[3];
            for (var c = 0; c < _curves.Length; c++)
            {
                for (var f = 0; f < 3; f++) frameCurves[f] = _profile.Curves[f * _curves.Length + c];
                var value = default(AlsInertialCurve); var first = true;
                for (var branch = 0; branch < 4; branch++)
                {
                    var weight = _next.Weights[branch]; if (weight <= AlsPoseBlender.WeightThreshold) continue;
                    if (two && branch == active[1]) weight = 1f - _next.Weights[active[0]];
                    var curve = branch == 2 ? frameCurves[2] : frameCurves[0];
                    if (branch == 0)
                    {
                        curve = AlsRefactoredDefaultOverlay.Curve(frameCurves, _input, default);
                        var idle = _profile.IdleMap[c];
                        if (idle >= 0) curve = AlsStandingCycleCurves.Accumulate(curve, players.Curves(_player)[idle], .75f);
                    }
                    else if (branch == 1 && _profile.CurveNames[c] is "LayerHeadAdditive" or "LayerSpineAdditive")
                        curve = AlsStandingCycleCurves.ModifyBlend(curve, .5f, 1);
                    else if (branch is 2 or 3 && _profile.CurveNames[c] is "LayerArmLeft" or "LayerArmRight")
                        curve = AlsStandingCycleCurves.ModifyBlend(curve, 3, 1);
                    value = passThrough ? curve : two ? (first ? curve : AlsStandingCycleCurves.Lerp(value, curve, weight)) :
                        first ? AlsStandingCycleCurves.Scale(curve, weight) : AlsStandingCycleCurves.Accumulate(value, curve, weight); first = false;
                }
                _curves[c] = value;
            }
            _evaluated = true;
        }
        catch { _faulted = true; throw; }
    }
    public void ValidateCommit(long frame)
    { if (!_prepared || _faulted || frame != _frame) throw new ArgumentException("Box frame cannot commit."); }
    public void Commit(long frame) { ValidateCommit(frame); _state = _next; _resetPending = _nextResetPending; _committed = frame; Cancel(); }
    public void Cancel() { _prepared = false; _evaluated = false; _faulted = false; }
}
