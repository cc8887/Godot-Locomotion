using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed record AlsRootPoseDefinition(int NodeIndex, AlsBinaryBlendSettings Blend);

// The ALS AnimGraph's final two-child BlendListByEnum. Child graph ownership,
// physics, and snapshot capture remain with the enclosing animation instance.
public sealed class AlsRootPoseRuntime
{
    private readonly AlsRootPoseDefinition _definition;
    private readonly AlsLocalPose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsAnimationGraphFrame _committedTraversal, _traversal;
    private AlsBinaryBlendUpdate _candidate;
    private AlsPoseUpdateContext _context;
    private bool _prepared, _evaluated;
    public AlsFrameIdentity CommittedIdentity => _committedTraversal.Identity;
    public AlsBinaryBlendState CommittedState { get; private set; }
    public AlsBinaryBlendState CandidateState => _prepared ? _candidate.State : throw new InvalidOperationException("No root candidate.");
    // Initialization and CacheBones visit both children, even a zero-weight one.
    // The caller propagates the same traversal counts to both child owners.
    public bool InitializeChildren { get; private set; }
    public bool CacheChildBones { get; private set; }
    public int ZeroWeightPreviousChild => _prepared ? _candidate.ZeroWeightPreviousChild : -1;
    public ReadOnlySpan<AlsLocalPose> Pose => _evaluated ? _pose : throw new InvalidOperationException("Root has no evaluated pose.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated ? _curves : throw new InvalidOperationException("Root has no evaluated curves.");

    public AlsRootPoseRuntime(AlsRootPoseDefinition definition, int bones, int curves)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.NodeIndex < 0 || bones <= 0 || curves < 0) throw new ArgumentException("Invalid root graph layout.");
        _ = AlsBinaryBlendList.Advance(default, 0, 0, definition.Blend);
        _definition = definition; _pose = new AlsLocalPose[bones]; _curves = new AlsInertialCurve[curves];
    }

    public void Prepare(AlsMovementStateInput state, in AlsPoseUpdateContext context, in AlsAnimationGraphFrame traversal)
    {
        if (_prepared) throw new InvalidOperationException("Root candidate already pending.");
        traversal.Validate(context.Identity);
        if (CommittedIdentity != default && (context.Identity.CharacterId != CommittedIdentity.CharacterId ||
            context.Identity.SlotGeneration != CommittedIdentity.SlotGeneration || context.Identity.FrameId <= CommittedIdentity.FrameId))
            throw new ArgumentException("Foreign or stale root frame.");
        InitializeChildren = CommittedIdentity == default || !traversal.Initialization.MatchesCounter(_committedTraversal.Initialization);
        CacheChildBones = CommittedIdentity == default || !traversal.Bones.MatchesCounter(_committedTraversal.Bones);
        // The exported enum exposes only Ragdoll. Every other entry, including
        // an out-of-range native byte, takes the default pose pin.
        _candidate = AlsBinaryBlendList.Advance(InitializeChildren ? default : CommittedState,
            state == AlsMovementStateInput.Ragdoll ? 1 : 0, context.Delta, _definition.Blend);
        _context = context; _traversal = traversal; _prepared = true; _evaluated = false;
    }

    public bool Visits(int child)
    {
        if (!_prepared || (uint)child > 1) throw new InvalidOperationException("Invalid root child query.");
        return Weight(child) > AlsPoseBlender.WeightThreshold;
    }

    public AlsPoseUpdateContext ChildContext(int child)
    {
        if (!Visits(child)) throw new InvalidOperationException("Root child is not relevant.");
        // FractionalWeight changes pose relevance, not RootMotionWeight.
        var context = _context.WithWeight(_context.Weight * Weight(child));
        return child == _candidate.State.ActiveChild ? context : context.AsInactive();
    }

    public AlsPoseUpdateContext PreviousZeroContext()
    {
        if (!_prepared || _candidate.ZeroWeightPreviousChild < 0) throw new InvalidOperationException("No instant-transition zero update.");
        return _context.WithWeight(0);
    }

    public void Evaluate(ReadOnlySpan<AlsLocalPose> normal, ReadOnlySpan<AlsInertialCurve> normalCurves,
        ReadOnlySpan<AlsLocalPose> ragdoll, ReadOnlySpan<AlsInertialCurve> ragdollCurves)
    {
        if (!_prepared || _evaluated) throw new InvalidOperationException("Root requires one prepared evaluation.");
        try
        {
            var first = Visits(0); var second = Visits(1);
            if (first) Validate(normal, normalCurves);
            if (second) Validate(ragdoll, ragdollCurves);
            if (first && second)
            {
                var alpha = 1 - _candidate.State.FirstWeight;
                // Native fast path keeps WeightOfPoseOne directly. Recomputing
                // it as 1 - (1 - weight) would lose precision near zero.
                for (var i = 0; i < _pose.Length; i++)
                    _pose[i] = AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(new(normal[i]), _candidate.State.FirstWeight),
                        new(ragdoll[i]), alpha).Normalized().ToSingle();
                for (var i = 0; i < _curves.Length; i++) _curves[i] = AlsStandingCycleCurves.Lerp(normalCurves[i], ragdollCurves[i], alpha);
            }
            else if (first) { normal.CopyTo(_pose); normalCurves.CopyTo(_curves); }
            else if (second) { ragdoll.CopyTo(_pose); ragdollCurves.CopyTo(_curves); }
            else throw new InvalidOperationException("Root lost both normalized pose weights.");
            _evaluated = true;
        }
        catch { Cancel(); throw; }
    }

    private void Validate(ReadOnlySpan<AlsLocalPose> pose, ReadOnlySpan<AlsInertialCurve> curves)
    {
        if (pose.Length != _pose.Length || curves.Length != _curves.Length) throw new ArgumentException("Missing or foreign root child pose.");
        foreach (var bone in pose) new AlsPrecisePose(bone).Validate();
        foreach (var curve in curves) if (curve.Present && !float.IsFinite(curve.Value)) throw new ArgumentException("Invalid root child curve.");
    }
    private float Weight(int child) => child == 0 ? _candidate.State.FirstWeight : _candidate.State.SecondWeight;
    public void ValidateCommit(AlsFrameIdentity identity)
    {
        if (!_prepared || !_evaluated || identity != _context.Identity) throw new InvalidOperationException("No complete root candidate for commit.");
    }
    public void Commit(AlsFrameIdentity identity)
    {
        ValidateCommit(identity); CommittedState = _candidate.State; _committedTraversal = _traversal;
        _prepared = _evaluated = false;
    }
    public void Cancel() { _prepared = _evaluated = false; }
}
