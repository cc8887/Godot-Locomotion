using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

// Resolved per-body native thresholds, not scene defaults or energy estimates.
public readonly record struct AlsSleepBodySettings(float LinearThreshold, float AngularThreshold,
    int CounterThreshold, bool NeverSleep = false);
public readonly record struct AlsSleepMetrics(AlsDoubleVector Linear, AlsDoubleVector Angular, int ParticleCounter);

// Default Chaos full-island sleeping for one already assembled dynamic group.
// Fixed bodies do not vote. Partial/isolated particle sleeping and graph discovery
// belong to the world. Candidate metrics are transactional with the solver step.
internal sealed class AlsIslandSleep
{
    private readonly bool[] _dynamic;
    private readonly AlsSleepBodySettings[] _settings;
    private AlsSleepMetrics[] _metrics, _scratch;
    private readonly double _rate;
    private bool _staged, _nextSleeping;
    private int _nextCounter;
    public bool Sleeping { get; private set; }
    public int Counter { get; private set; }
    public AlsSleepMetrics At(int body) => _metrics[body];

    public AlsIslandSleep(ReadOnlySpan<AlsIslandBody> bodies, ReadOnlySpan<AlsSleepBodySettings> settings,
        ReadOnlySpan<AlsIslandBodyState> initial, float smoothing)
    {
        if (settings.Length != bodies.Length || !float.IsFinite(smoothing) || smoothing is < 0 or > 1)
            throw new ArgumentException("Sleep settings must match the island and use a finite smoothing rate in [0,1].");
        _settings = settings.ToArray(); _dynamic = new bool[bodies.Length]; _rate = smoothing;
        _metrics = new AlsSleepMetrics[bodies.Length]; _scratch = new AlsSleepMetrics[bodies.Length];
        var count = 0;
        for (var i = 0; i < bodies.Length; i++)
        {
            var s = settings[i];
            if (!float.IsFinite(s.LinearThreshold) || s.LinearThreshold < 0 || !float.IsFinite(s.AngularThreshold) ||
                s.AngularThreshold < 0 || s.CounterThreshold < 0 || !float.IsFinite(s.LinearThreshold * s.LinearThreshold) ||
                !float.IsFinite(s.AngularThreshold * s.AngularThreshold)) throw new ArgumentException("Invalid sleep thresholds.");
            _dynamic[i] = bodies[i].InverseMass.Mass > 0; if (_dynamic[i]) count++;
        }
        if (count == 0) throw new ArgumentException("Sleep needs a dynamic participant.");
        Reset(initial);
    }
    public void Reset(ReadOnlySpan<AlsIslandBodyState> states)
    {
        for (var i = 0; i < states.Length; i++) _metrics[i] = new(new(states[i].Velocity.Linear), new(states[i].Velocity.Angular), 0);
        Sleeping = false; Counter = 0; _staged = false;
    }
    public bool Stage(ReadOnlySpan<AlsIslandBodyState> before, ReadOnlySpan<AlsIslandBodyState> after,
        double dt, bool wake, ReadOnlySpan<AlsBodyStepForces> forces, bool allowSleep)
    {
        _staged = false; var within = true; var threshold = 0;
        for (var i = 0; i < _metrics.Length; i++)
        {
            var m = _metrics[i]; _scratch[i] = m; if (!_dynamic[i]) continue;
            var s = _settings[i];
            // Native SetParticleDynamics seeds smoothing from external inputs
            // using a fixed 1/30 s estimate. Gravity ForceRules do not use this.
            if (!forces.IsEmpty)
            {
                var f = forces[i];
                if (!f.Acceleration.NearlyZero(1e-4) || !f.LinearImpulseVelocity.NearlyZero(1e-4))
                    m = m with { Linear = Lerp(m.Linear, new AlsDoubleVector(before[i].Velocity.Linear) + f.Acceleration * (1d / 30) + f.LinearImpulseVelocity) };
                if (!f.AngularAcceleration.NearlyZero(1e-4) || !f.AngularImpulseVelocity.NearlyZero(1e-4))
                    m = m with { Angular = Lerp(m.Angular, new AlsDoubleVector(before[i].Velocity.Angular) + f.AngularAcceleration * (1d / 30) + f.AngularImpulseVelocity) };
            }
            if (!allowSleep || s.NeverSleep || (s.LinearThreshold <= 0 && s.AngularThreshold <= 0))
            { within = false; _scratch[i] = m; continue; }
            if (dt > (double)1e-8f)
            {
                // Chaos uses actor X/P and R/Q, not COM displacement or final
                // impulse velocity. Preserve float particle quaternion inputs.
                var p = before[i].Actor; var q = after[i].Actor;
                var rotation = q.Rotation;
                if (AlsQuaternion.Dot(p.Rotation, rotation) < 0) rotation = -rotation;
                var w = ((rotation + -p.Rotation) * (1 / dt) * p.Rotation.Conjugate()) * 2;
                m = m with { Linear = Lerp(m.Linear, (q.Position - p.Position) * (1 / dt)),
                    Angular = Lerp(m.Angular, new(w.X, w.Y, w.Z)) };
            }
            if (!m.Linear.IsFinite || !m.Angular.IsFinite) throw new ArgumentException("Sleep metrics overflowed.");
            if (m.Linear.LengthSquared > (double)(s.LinearThreshold * s.LinearThreshold) ||
                m.Angular.LengthSquared > (double)(s.AngularThreshold * s.AngularThreshold))
            { within = false; m = m with { ParticleCounter = 0 }; }
            else
            {
                m = m with { ParticleCounter = System.Math.Min(127, System.Math.Min(m.ParticleCounter + 1, s.CounterThreshold)) };
                threshold = System.Math.Max(threshold, s.CounterThreshold);
            }
            _scratch[i] = m;
        }
        var previous = wake ? 0 : Counter;
        _nextCounter = within ? (previous == int.MaxValue ? previous : previous + 1) : 0;
        _nextSleeping = within && _nextCounter > threshold;
        if (_nextSleeping) _nextCounter = 0;
        _staged = true; return _nextSleeping;
    }
    public void Publish()
    {
        if (!_staged) throw new InvalidOperationException("Stage sleep before publishing.");
        (_metrics, _scratch) = (_scratch, _metrics); Sleeping = _nextSleeping; Counter = _nextCounter; _staged = false;
    }
    public void Abort() => _staged = false;
    private AlsDoubleVector Lerp(AlsDoubleVector a, AlsDoubleVector b) => a + (b - a) * _rate;
}
