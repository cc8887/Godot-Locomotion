using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Main graph node13. Input is the complete Grounded result in the
/// profile layout; no clock or second montage bank is owned here.</summary>
public sealed class AlsRefactoredTransitionSlot
{
    private readonly AlsRefactoredCharacterActionProfile _profile;
    private readonly AlsRefactoredCharacterActionRuntime _owner;
    private readonly AlsMontageSlotPose _mixer;
    private readonly IAlsMontagePoseSource _sampler;
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsInertialCurve[] _curves;
    private AlsPoseUpdateContext _context;
    private AlsSlotWeights _weights;
    private AlsSlotSourceUpdate _source;
    private float _committedSource;
    private bool _prepared, _evaluated;
    public AlsSlotSourceUpdate SourceUpdate { get { Check(); return _source; } }
    public AlsSlotWeights Weights { get { Check(); return _weights; } }
    public AlsMontageInertialRequest? InertializationRequest
    { get { Check(); return _owner.Frame.TryGetInertializationRequest(_profile.Standing.Montages.HostGroupId, out var request) ? request : null; } }
    public ReadOnlySpan<AlsPrecisePose> Pose { get { RequirePose(); return _pose; } }
    public ReadOnlySpan<AlsInertialCurve> Curves { get { RequirePose(); return _curves; } }
    internal AlsRefactoredTransitionSlot(AlsRefactoredCharacterActionProfile profile, AlsRefactoredCharacterActionRuntime owner)
    {
        _profile = profile; _owner = owner; _sampler = profile.Samples.CreateSampler();
        _mixer = new(profile.Reference, profile.Parents, profile.CurveNames.Length);
        _pose = new AlsPrecisePose[profile.BoneNames.Length]; _curves = new AlsInertialCurve[profile.CurveNames.Length];
    }
    public void Prepare(in AlsPoseUpdateContext context, bool initialize = false)
    {
        if (_prepared) throw new InvalidOperationException("Transition Slot already prepared.");
        _owner.ValidateUpdate(context);
        if (!context.HasSharedContext || context.UpdateCounter is not { HasUpdated: true }) throw new ArgumentException("Invalid Slot traversal context.");
        _weights = _owner.Frame.SlotWeights(AlsMontageSlot.Transition);
        _source = AlsSlotSourceUpdate.Resolve(initialize ? 0 : _committedSource, _weights, context, _profile.Transition.AlwaysUpdateSource);
        _context = context; _prepared = true;
    }
    public void Evaluate(ReadOnlySpan<AlsPrecisePose> groundedPose, ReadOnlySpan<AlsInertialCurve> groundedCurves)
    {
        Check(); _owner.ValidateUpdate(_context); _evaluated = false;
        try
        {
            _mixer.Evaluate(_owner.Frame, _context.Identity, AlsMontageSlot.Transition, groundedPose, groundedCurves, _pose, _curves, _sampler);
            _evaluated = true;
        }
        catch { _owner.Discard(); throw; }
    }
    public void ValidateCommit(in AlsFrameIdentity identity)
    { Check(); if (identity != _context.Identity) throw new ArgumentException("Foreign Transition Slot commit."); }
    internal void Commit(in AlsFrameIdentity identity)
    { ValidateCommit(identity); _committedSource = _weights.SourceWeight; Cancel(); }
    internal void Cancel() { _prepared = _evaluated = false; }
    private void Check()
    {
        if (!_prepared || _owner.Frame.Identity != _context.Identity || _owner.Frame.SlotWeights(AlsMontageSlot.Transition) != _weights)
            throw new InvalidOperationException("Transition Slot is missing or stale.");
    }
    private void RequirePose() { Check(); if (!_evaluated) throw new InvalidOperationException("Transition Slot was not evaluated."); }
}
