using System.Numerics;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsBasePoseEvaluatorTick(AlsFrameIdentity Identity, int NodeIndex, int AnimationId,
    long InitializationEpoch, float PreviousTime, float CurrentTime, float DeltaTime, float PlayRate, bool Reinitialized)
{
    public bool IsEvaluator => true;
    public bool Teleport => true;
}

public readonly record struct AlsBasePoseEvaluatorState(int NodeIndex, int AnimationId, long InitializationEpoch,
    bool Initialized, bool Reinitialized, float Time, float BlendWeight, bool HasBeenFullWeight,
    AlsFrameIdentity LastUpdateIdentity, long UpdateCount, long EvaluationCount);

public readonly record struct AlsBasePosesState(AlsFrameIdentity Identity,
    AlsGraphTraversalCounter Initialization, AlsGraphTraversalCounter Bones,
    Vector2 DesiredAlphas, Vector2 CachedAlphas,
    AlsBasePoseEvaluatorState Normal, AlsBasePoseEvaluatorState Crouching)
{
    public bool Initialized => Normal.Initialized && Crouching.Initialized;
}

/// <summary>Source callbacks write candidate state only. Evaluator node identity is independent of animation ID.
/// A teleport tick is still submitted at zero graph weight; Evaluate must never advance time or emit notifies.</summary>
public interface IAlsBasePosesSink
{
    void InitializeEvaluator(in AlsBasePoseEvaluatorDefinition evaluator);
    void CacheEvaluatorBones(in AlsBasePoseEvaluatorDefinition evaluator);
    void UpdateEvaluator(in AlsBasePoseEvaluatorDefinition evaluator, in AlsBasePoseEvaluatorTick tick,
        in AlsPoseUpdateContext context);
    void EvaluateEvaluator(in AlsBasePoseEvaluatorDefinition evaluator, float seconds,
        Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves);
}

/// <summary>Authored BasePoses MultiWayBlend and its two fixed-time evaluators. Begin before the parent graph;
/// forward its actual Initialize/CacheBones/Update visits, then commit or cancel with the other graph owners.</summary>
public sealed class AlsBasePosesRuntime
{
    private readonly AlsBasePosesDefinition _definition;
    private readonly AlsLocalPose[] _reference, _samples;
    private readonly AlsInertialCurve[] _sampleCurves;
    private readonly string[] _curveNames;
    private AlsBasePosesState _committed, _candidate;
    private IAlsBasePosesSink? _sink;
    private bool _begun, _busy;

    public bool HasCandidate => _begun;
    public AlsBasePosesState State => _begun ? _candidate : _committed;
    public AlsBasePosesState CommittedState => _committed;
    public AlsFrameIdentity Identity => State.Identity;
    public ReadOnlySpan<string> CurveNames => _curveNames;
    public AlsBasePosesDefinition Definition => _definition;

    public AlsBasePosesRuntime(AlsBasePosesDefinition definition, ReadOnlySpan<AlsLocalPose> referencePose,
        ReadOnlySpan<string> curveNames)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (referencePose.IsEmpty) throw new ArgumentException("BasePoses requires the real skeleton reference pose.");
        ValidatePayload(referencePose, []);
        _curveNames = curveNames.ToArray();
        if (_curveNames.Any(string.IsNullOrWhiteSpace) || _curveNames.Distinct(StringComparer.Ordinal).Count() != _curveNames.Length)
            throw new ArgumentException("BasePoses requires a unique final curve layout.");
        _definition = definition; _reference = referencePose.ToArray();
        _samples = new AlsLocalPose[checked(referencePose.Length * 2)];
        _sampleCurves = new AlsInertialCurve[checked(curveNames.Length * 2)];
        _committed = new(default, default, default,
            new(definition.DefaultDesiredAlphas[0], definition.DefaultDesiredAlphas[1]), default,
            InitialSource(definition.Evaluators[0]), InitialSource(definition.Evaluators[1]));
    }

    public void BeginCandidate(AlsFrameIdentity identity, IAlsBasePosesSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (_begun || _busy || identity.SlotGeneration == 0 ||
            _committed.Identity.SlotGeneration != 0 &&
            (identity.CharacterId != _committed.Identity.CharacterId || identity.SlotGeneration != _committed.Identity.SlotGeneration ||
             identity.FrameId <= _committed.Identity.FrameId))
            throw new InvalidOperationException("Invalid BasePoses candidate identity.");
        _candidate = _committed with { Identity = identity }; _sink = sink; _begun = true;
    }

    // The parent SaveCachedPose owns counter suppression. An actual forwarded visit always runs.
    // MultiWay Initialize reads retained DesiredAlphas, not the properties from a future Update.
    public void Initialize(AlsGraphTraversalCounter counter)
    {
        Enter();
        try
        {
            if (!counter.HasUpdated) throw new ArgumentException("Invalid BasePoses initialization counter.");
            _candidate = _candidate with { Initialization = counter, CachedAlphas = CacheAlphas(_candidate.DesiredAlphas) };
            for (var source = 0; source < 2; source++)
            {
                var state = Source(source);
                SetSource(source, state with { InitializationEpoch = checked(state.InitializationEpoch + 1),
                    Initialized = true, Reinitialized = true, HasBeenFullWeight = false });
                _sink!.InitializeEvaluator(_definition.Evaluators[source]);
            }
        }
        catch { Abort(); throw; }
        finally { _busy = false; }
    }

    public void CacheBones(AlsGraphTraversalCounter counter)
    {
        Enter();
        try
        {
            RequireInitialized();
            if (!counter.HasUpdated) throw new ArgumentException("Invalid BasePoses bone-cache counter.");
            _candidate = _candidate with { Bones = counter };
            for (var source = 0; source < 2; source++) _sink!.CacheEvaluatorBones(_definition.Evaluators[source]);
        }
        catch { Abort(); throw; }
        finally { _busy = false; }
    }

    public void Update(in AlsPoseUpdateContext context, in AlsLayeringInput input)
    {
        Enter();
        try
        {
            RequireInitialized();
            if (context.Identity != _candidate.Identity || input.Identity != _candidate.Identity)
                throw new ArgumentException("BasePoses Update must consume this frame's layer properties and context.");
            var desired = new Vector2(ReadWeight(input, 0), ReadWeight(input, 1));
            _candidate = _candidate with { DesiredAlphas = desired, CachedAlphas = CacheAlphas(desired) };
            for (var source = 0; source < 2; source++)
            {
                var alpha = _candidate.CachedAlphas[source];
                if (!Relevant(alpha)) continue;
                var definition = _definition.Evaluators[source]; var previous = Source(source);
                var child = context.WithWeight(context.Weight * alpha);
                // Teleport + None/DoNotSync takes the native fixed-time branch, but still creates an evaluator tick.
                var time = System.Math.Clamp(definition.ExplicitTime, 0, definition.Length);
                var tick = new AlsBasePoseEvaluatorTick(context.Identity, definition.NodeIndex, definition.AnimationId,
                    previous.InitializationEpoch, previous.Time, time, 0, 0, previous.Reinitialized);
                SetSource(source, previous with { Time = time, BlendWeight = child.Weight,
                    HasBeenFullWeight = previous.HasBeenFullWeight || child.Weight >= 1 - AlsPoseBlender.WeightThreshold,
                    LastUpdateIdentity = context.Identity, UpdateCount = checked(previous.UpdateCount + 1) });
                _sink!.UpdateEvaluator(definition, tick, child);
                SetSource(source, Source(source) with { Reinitialized = false });
            }
        }
        catch { Abort(); throw; }
        finally { _busy = false; }
    }

    public void Evaluate(Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        Enter();
        try
        {
            RequireInitialized();
            if (pose.Length != _reference.Length || curves.Length != _curveNames.Length)
                throw new ArgumentException("BasePoses evaluation layout differs from its complete source binding.");
            var count = 0;
            for (var source = 0; source < 2; source++)
            {
                if (!Relevant(_candidate.CachedAlphas[source])) continue;
                var sample = Sample(source); var sampleCurves = SampleCurves(source);
                _reference.CopyTo(sample); sampleCurves.Clear();
                _sink!.EvaluateEvaluator(_definition.Evaluators[source], Source(source).Time, sample, sampleCurves);
                ValidatePayload(sample, sampleCurves);
                var state = Source(source); SetSource(source, state with { EvaluationCount = checked(state.EvaluationCount + 1) });
                count++;
            }
            if (count == 0) { _reference.CopyTo(pose); curves.Clear(); return; }
            var first = Relevant(_candidate.CachedAlphas.X) ? 0 : 1;
            var second = 1; // Authored pin order is always N, then CLF.
            var firstPose = Sample(first); var firstCurves = SampleCurves(first);
            var firstWeight = _candidate.CachedAlphas[first];
            for (var bone = 0; bone < pose.Length; bone++)
            {
                var value = AlsPoseBlender.Scale(firstPose[bone], firstWeight);
                if (count == 2) value = AlsPoseBlender.Accumulate(value, Sample(second)[bone], _candidate.CachedAlphas.Y);
                // BlendPosesTogether normalizes multiple poses; MultiWay then normalizes its output again.
                if (count == 2) value = AlsPoseBlender.Normalize(value);
                pose[bone] = AlsPoseBlender.Normalize(value);
            }
            for (var curve = 0; curve < curves.Length; curve++)
            {
                var value = AlsStandingCycleCurves.Scale(firstCurves[curve], firstWeight);
                if (count == 2) value = AlsStandingCycleCurves.Accumulate(value, SampleCurves(second)[curve], _candidate.CachedAlphas.Y);
                curves[curve] = value;
            }
            ValidatePayload(pose, curves);
        }
        catch { Abort(); throw; }
        finally { _busy = false; }
    }

    // A source can be initialized/cached but entirely irrelevant this frame. Its lifecycle still commits.
    public void Commit()
    {
        ValidateCommit();
        _committed = _candidate; _begun = false; _sink = null;
    }
    public void ValidateCommit()
    {
        if (!_begun || _busy) throw new InvalidOperationException("BasePoses has no closed candidate to commit.");
    }
    public void Cancel()
    {
        if (_busy) throw new InvalidOperationException("Cannot cancel BasePoses inside its source callback.");
        Abort();
    }
    private void Abort() { _begun = false; _sink = null; }
    private void Enter()
    {
        if (!_begun || _busy) throw new InvalidOperationException("BasePoses has no candidate or is already traversing.");
        _busy = true;
    }
    private void RequireInitialized()
    { if (!_candidate.Initialized) throw new InvalidOperationException("BasePoses must receive its actual initialization visit first."); }
    private float ReadWeight(in AlsLayeringInput input, int source)
    {
        var value = input.GetValue(_definition.WeightProperties[source]); var result = (float)value;
        if (!double.IsFinite(value) || !float.IsFinite(result)) throw new ArgumentException("Invalid BasePoses weight property.");
        return result;
    }
    private Vector2 CacheAlphas(Vector2 desired)
    {
        // Preserve native float addition and clamping. Finite float inputs may sum to +infinity;
        // normalization then produces zero weights, rather than a fabricated 50/50 blend.
        var total = 0f; total += desired.X; total += desired.Y;
        if (!Relevant(ScaleBias(total))) return Vector2.Zero;
        return _definition.NormalizeAlphas ? new(ScaleBias(desired.X / total), ScaleBias(desired.Y / total)) :
            new(ScaleBias(desired.X), ScaleBias(desired.Y));
    }
    private float ScaleBias(float value) => System.Math.Clamp(value * _definition.AlphaScale + _definition.AlphaBias, 0, 1);
    private static bool Relevant(float weight) => weight > AlsPoseBlender.WeightThreshold;
    private static AlsBasePoseEvaluatorState InitialSource(AlsBasePoseEvaluatorDefinition definition) =>
        new(definition.NodeIndex, definition.AnimationId, 0, false, false, 0, 0, false, default, 0, 0);
    private AlsBasePoseEvaluatorState Source(int index) => index == 0 ? _candidate.Normal : _candidate.Crouching;
    private void SetSource(int index, AlsBasePoseEvaluatorState state) =>
        _candidate = index == 0 ? _candidate with { Normal = state } : _candidate with { Crouching = state };
    private Span<AlsLocalPose> Sample(int source) => _samples.AsSpan(source * _reference.Length, _reference.Length);
    private Span<AlsInertialCurve> SampleCurves(int source) => _sampleCurves.AsSpan(source * _curveNames.Length, _curveNames.Length);
    private static void ValidatePayload(ReadOnlySpan<AlsLocalPose> pose, ReadOnlySpan<AlsInertialCurve> curves)
    {
        foreach (var bone in pose)
            if (!Finite(bone.Position) || !Finite(bone.Scale) || !float.IsFinite(bone.Rotation.X) || !float.IsFinite(bone.Rotation.Y) ||
                !float.IsFinite(bone.Rotation.Z) || !float.IsFinite(bone.Rotation.W) || !float.IsFinite(bone.Rotation.LengthSquared()) ||
                bone.Rotation.LengthSquared() < 1e-8f) throw new ArgumentException("Invalid BasePoses sample atom.");
        foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Non-finite BasePoses curve payload.");
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
