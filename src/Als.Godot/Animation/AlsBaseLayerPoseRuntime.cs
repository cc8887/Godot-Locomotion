using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public interface IAlsBaseLayerSlotPoseSink
{
    void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
        ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves);
    void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
        ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> output, Span<AlsInertialCurve> curves);
}

/// <summary>Candidate BaseLayer tail, before the separate LayerBlending/IK graph.
/// The caller owns Main Movement, montage state and the enclosing atomic commit.
/// SourceUpdate gates both the movement update and inertia history; a fully overriding
/// Slot consumes empty source spans. Montage output never enters inertia history.</summary>
public sealed class AlsBaseLayerPoseRuntime
{
    private readonly AlsBaseLayerProfile _profile;
    private AlsInertialization _committed, _candidate;
    private readonly AlsLocalPose[] _sourcePose;
    private readonly AlsLocalPose[] _rawProjection;
    private readonly AlsQuaternion[] _inputRotations, _outputRotations;
    private readonly AlsPrecisePose[] _preciseSourcePose;
    private readonly AlsInertialCurve[] _sourceCurves;
    private AlsFrameIdentity _identity;
    private AlsFrameIdentity _committedFrame;
    private AlsSlotWeights _weights;
    private float _previousSourceWeight = 1;
    private AlsGraphTraversalCounter _committedInitialization, _candidateInitialization;
    private bool _explicitLifecycle;
    private bool _initializedThisFrame;
    private Phase _phase;
    private enum Phase { Idle, Updating, Evaluated, Unvisited, Faulted }

    public AlsSlotSourceUpdate SourceUpdate { get; private set; }
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public int CommittedHistoryCount => _committed.HistoryCount;
    public bool IsFaulted => _phase == Phase.Faulted;

    public AlsBaseLayerPoseRuntime(AlsBaseLayerProfile profile, int boneCount, int curveCount)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.SlotName != "BaseLayer" || profile.InertializationNodeIndex < 0)
            throw new ArgumentException("Invalid BaseLayer profile.");
        _profile = profile;
        // Imported q = (-UE.X, UE.Y, -UE.Z, UE.W): the native fallback rotation axis is -X here.
        _committed = new(boneCount, curveCount, .01f, -System.Numerics.Vector3.UnitX);
        _candidate = new(boneCount, curveCount, .01f, -System.Numerics.Vector3.UnitX);
        _sourcePose = new AlsLocalPose[boneCount]; _sourceCurves = new AlsInertialCurve[curveCount];
        _rawProjection = new AlsLocalPose[boneCount]; _preciseSourcePose = new AlsPrecisePose[boneCount];
        _inputRotations = new AlsQuaternion[boneCount]; _outputRotations = new AlsQuaternion[boneCount];
    }

    public AlsSlotSourceUpdate Begin(in AlsPoseUpdateContext context, in AlsSlotWeights weights, AlsAnimationGraphFrame? traversal=null)
    {
        Require(Phase.Idle);
        PrepareHistory(context.Identity,traversal);
        var source = AlsSlotSourceUpdate.Resolve(_initializedThisFrame ? 1 : _previousSourceWeight, weights, context, false);
        if (!source.Updated && weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold)
            throw new ArgumentException("A passthrough Slot requires a source.");
        _weights = weights;
        SourceUpdate = source.Updated ? new(true, source.Context.WithInertialization(_profile.InertializationNodeIndex, true)) : default;
        if (source.Updated) _candidate.Update(context.Delta);
        _phase = Phase.Updating;
        return SourceUpdate;
    }

    public void PrepareUnvisited(in AlsAnimationGraphFrame traversal)
    {
        Require(Phase.Idle); PrepareHistory(traversal.Identity,traversal);
        SourceUpdate=default; _phase=Phase.Unvisited;
    }
    private void PrepareHistory(AlsFrameIdentity identity,AlsAnimationGraphFrame? traversal)
    {
        var prior=_committedFrame;
        if(identity.SlotGeneration==0 || prior.SlotGeneration!=0 &&
            (identity.CharacterId!=prior.CharacterId || identity.SlotGeneration!=prior.SlotGeneration || identity.FrameId<=prior.FrameId))
            throw new ArgumentException("Foreign or stale BaseLayer candidate.");
        if(traversal is { } validated)validated.Validate(identity);
        _candidate.CopyFrom(_committed); _identity=identity; _explicitLifecycle=traversal.HasValue; _initializedThisFrame=false;
        if(traversal is not { } frame)return;
        frame.Validate(identity); _candidateInitialization=frame.Initialization;
        if(!_candidateInitialization.MatchesCounter(_committedInitialization))
        {
            _candidate.Reset(); _initializedThisFrame=true;
        }
        // The authored bResetOnBecomingRelevant is false: Update gaps alone
        // preserve inertia. CacheBones only refreshes filters (none in this graph).
    }

    public void RequestInertialization(in AlsPoseUpdateContext context, float seconds)
    {
        Require(Phase.Updating);
        if (!SourceUpdate.Updated || context.Identity != _identity || context.InertializationRequester != _profile.InertializationNodeIndex)
            throw new ArgumentException("Request belongs to a different inertialization node.");
        _candidate.Request(seconds);
    }

    public void Evaluate(ReadOnlySpan<AlsLocalPose> raw, ReadOnlySpan<AlsInertialCurve> rawCurves,
        in AlsLocalPose component, long parent, float teleportDistance,
        Span<AlsLocalPose> output, Span<AlsInertialCurve> curves, IAlsBaseLayerSlotPoseSink? slot = null)
    {
        Require(Phase.Updating);
        try
        {
            if (output.Length != _sourcePose.Length || curves.Length != _sourceCurves.Length)
                throw new ArgumentException("BaseLayer output layout differs.");
            if (SourceUpdate.Updated)
                _candidate.Evaluate(raw, rawCurves, component, parent, teleportDistance, _sourcePose, _sourceCurves);
            else if (!raw.IsEmpty || !rawCurves.IsEmpty)
                throw new ArgumentException("A hidden BaseLayer source must not be evaluated.");
            if (_weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold)
            { _sourcePose.CopyTo(output); _sourceCurves.CopyTo(curves); }
            else
            {
                if (slot == null) throw new InvalidOperationException("Active BaseLayer Slot has no montage pose owner.");
                slot.EvaluateSlot(_weights, SourceUpdate.Updated ? _sourcePose : ReadOnlySpan<AlsLocalPose>.Empty,
                    SourceUpdate.Updated ? _sourceCurves : ReadOnlySpan<AlsInertialCurve>.Empty, output, curves);
            }
            foreach (var pose in output)
                if (!float.IsFinite(pose.Position.LengthSquared()) || !float.IsFinite(pose.Scale.LengthSquared()) ||
                    !float.IsFinite(pose.Rotation.LengthSquared()) || MathF.Abs(pose.Rotation.LengthSquared() - 1) > .001f)
                    throw new InvalidOperationException("Invalid BaseLayer output bone.");
            foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new InvalidOperationException("Invalid BaseLayer curve.");
            _phase = Phase.Evaluated;
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void EvaluatePrecise(ReadOnlySpan<AlsPrecisePose> raw, ReadOnlySpan<AlsInertialCurve> rawCurves,
        in AlsLocalPose component, long parent, float teleportDistance,
        Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves, IAlsBaseLayerSlotPoseSink? slot = null)
    {
        Require(Phase.Updating);
        try
        {
            if (output.Length != _sourcePose.Length || curves.Length != _sourceCurves.Length)
                throw new ArgumentException("BaseLayer output layout differs.");
            if (SourceUpdate.Updated)
            {
                if (raw.Length != _sourcePose.Length) throw new ArgumentException("Incomplete precise Main Movement pose.");
                for (var bone = 0; bone < raw.Length; bone++)
                {
                    _rawProjection[bone] = raw[bone].ToSingle();
                    _inputRotations[bone] = raw[bone].Rotation;
                }
                // The inertia implementation owns its native float translation/difference boundaries;
                // rotations come from actual upstream evaluation and stay double in both history frames.
                _candidate.EvaluatePrecise(_rawProjection, rawCurves, component, parent, teleportDistance,
                    _sourcePose, _sourceCurves, _inputRotations, new AlsQuaternion(component.Rotation), _outputRotations);
                for (var bone = 0; bone < raw.Length; bone++)
                    _preciseSourcePose[bone] = new AlsPrecisePose(_sourcePose[bone]) with { Rotation = _outputRotations[bone] };
            }
            else if (!raw.IsEmpty || !rawCurves.IsEmpty)
                throw new ArgumentException("A hidden BaseLayer source must not be evaluated.");
            if (_weights.SlotNodeWeight <= AlsPoseBlender.WeightThreshold)
            { _preciseSourcePose.CopyTo(output); _sourceCurves.CopyTo(curves); }
            else
            {
                if (slot is null) throw new InvalidOperationException("Active BaseLayer Slot has no montage pose owner.");
                slot.EvaluateSlot(_weights, SourceUpdate.Updated ? _preciseSourcePose : ReadOnlySpan<AlsPrecisePose>.Empty,
                    SourceUpdate.Updated ? _sourceCurves : ReadOnlySpan<AlsInertialCurve>.Empty, output, curves);
            }
            foreach (var pose in output) pose.Validate(.001);
            foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new InvalidOperationException("Invalid BaseLayer curve.");
            _phase = Phase.Evaluated;
        }
        catch { _phase = Phase.Faulted; throw; }
    }

    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity);
        (_candidate, _committed) = (_committed, _candidate);
        if(_phase!=Phase.Unvisited){_previousSourceWeight=_weights.SourceWeight; CommittedIdentity=identity;}
        else if(_initializedThisFrame)_previousSourceWeight=1;
        if(_explicitLifecycle)_committedInitialization=_candidateInitialization;
        _committedFrame=identity;
        _phase = Phase.Idle;
    }
    internal void ValidateCommit(AlsFrameIdentity identity)
    {
        if(_phase is not (Phase.Evaluated or Phase.Unvisited))throw new InvalidOperationException("Incomplete BaseLayer pose candidate.");
        if (identity != _identity) throw new ArgumentException("Foreign BaseLayer commit.");
    }
    public void Discard() => _phase = Phase.Idle;
    private void Require(Phase phase)
    { if (_phase != phase) throw new InvalidOperationException($"BaseLayer phase is {_phase}; expected {phase}."); }
}
