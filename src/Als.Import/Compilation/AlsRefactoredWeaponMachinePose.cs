using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Ordered native transition-stack evaluation. Per-state diagnostic
/// contribution weights cannot replace the quaternion accumulation order here.</summary>
public sealed class AlsRefactoredWeaponMachinePose
{
    private readonly AlsRefactoredWeaponStatePose _profile;
    private readonly AlsRefactoredWeaponStatePose.Sampler _sampler;
    private readonly AlsPrecisePose[][] _states;
    private readonly AlsInertialCurve[][] _stateCurves;
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private readonly bool[] _sampled = new bool[3];
    private int _busy;
    public AlsRefactoredWeaponMachinePose(AlsRefactoredWeaponStatePose profile)
    {
        _profile = profile; _sampler = profile.CreateSampler();
        _states = Enumerable.Range(0, 3).Select(_ => new AlsPrecisePose[profile.BoneNames.Length]).ToArray();
        _stateCurves = Enumerable.Range(0, 3).Select(_ => new AlsInertialCurve[profile.CurveNames.Length]).ToArray();
        _pose = new AlsPrecisePose[profile.BoneNames.Length]; _curves = new AlsInertialCurve[profile.CurveNames.Length];
    }
    public void Sample(long frame, float pitch, AlsRefactoredWeaponMachineRuntime machine, AlsRefactoredWeaponSourceRuntime update,
        AlsRefactoredSourcePlayerRuntime players, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if (!ReferenceEquals(machine.Profile, _profile.SourceProfile.Machine) || pose.Length != _pose.Length || curves.Length != _curves.Length)
            throw new ArgumentException("Invalid weapon machine pose layout.");
        update.ValidateMachine(frame, machine);
        if (Interlocked.Exchange(ref _busy, 1) != 0) throw new InvalidOperationException("Reentrant weapon machine pose.");
        try
        {
            Array.Clear(_sampled); var state = machine.Candidate.State;
            if (state.Transitions.Count == 0)
            {
                SampleState(state.CurrentState); _states[state.CurrentState].CopyTo(_pose, 0); _stateCurves[state.CurrentState].CopyTo(_curves, 0);
            }
            else
            {
                for (var i = 0; i < state.Transitions.Count; i++)
                {
                    var transition = state.Transitions.GetTransition(i);
                    var edge = machine.Profile.Resources.Edges[state.GetActivePath(i).Edge];
                    if (i == 0) { SampleState(transition.From); _states[transition.From].CopyTo(_pose, 0); _stateCurves[transition.From].CopyTo(_curves, 0); }
                    SampleState(transition.To);
                    for (var b = 0; b < _pose.Length; b++)
                    {
                        var weights = edge.QuickFeet ? machine.Profile.Resources.QuickFeet.Weights(b, transition.Alpha) : new Vector2(transition.Alpha, 1 - transition.Alpha);
                        _pose[b] = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(_pose[b], weights.Y), _states[transition.To][b], weights.X);
                    }
                    for (var c = 0; c < _curves.Length; c++) _curves[c] = AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(_curves[c], 1 - transition.Alpha), _stateCurves[transition.To][c], transition.Alpha);
                }
                for (var b = 0; b < _pose.Length; b++) _pose[b] = _pose[b].Normalized();
            }
            _pose.CopyTo(pose); _curves.CopyTo(curves);
        }
        finally { Volatile.Write(ref _busy, 0); }
        void SampleState(int state)
        {
            if (_sampled[state]) return;
            _sampler.Sample(frame, state, pitch, update, players, _states[state], _stateCurves[state]); _sampled[state] = true;
        }
    }
}
