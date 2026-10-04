using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraFootPlantRigNodeContract(bool DefaultEnabled)
{
    public static LyraFootPlantRigNodeContract Load()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/main_layer_graph_v1.json"));
        var node = document.RootElement.GetProperty("classes").GetProperty("main")
            .GetProperty("graphs").GetProperty("AnimGraph").GetProperty("nodes")
            .EnumerateArray().Single(n => n.GetProperty("index").GetInt32() == 73);
        var settings = node.GetProperty("settings");
        var blend = settings.GetProperty("alphaBoolBlend");
        if (settings.GetProperty("alphaInputType").GetString() != "Bool" ||
            blend.GetProperty("blendOption").GetString() != "Linear" ||
            blend.GetProperty("blendInTime").GetSingle() != .2f ||
            blend.GetProperty("blendOutTime").GetSingle() != .2f ||
            settings.GetProperty("bSetRefPoseFromSkeleton").GetBoolean() ||
            settings.GetProperty("controlRigAssetReference").GetProperty("blueprintRigClass").GetString() !=
                "/Game/Characters/Heroes/Mannequin/Rig/CR_Mannequin_FootPlant.CR_Mannequin_FootPlant_C" ||
            !settings.GetProperty("destPropertyNames").EnumerateArray().Select(p => p.GetString())
                .SequenceEqual(new[] { "isCrouching", "isMoving2D" }))
            throw new NotSupportedException("Changed Main73 ControlRig contract.");
        return new(settings.GetProperty("bAlphaBoolEnabled").GetBoolean());
    }
}

internal readonly record struct LyraFootPlantRigUpdateState(AlsLinearBoolBlend Blend, float Alpha,
    float RigDelta, bool Crouching, bool Moving);

internal sealed record LyraFootPlantRigUpdateCandidate(LyraFootPlantRigUpdateHost Owner,
    long Epoch, long Frame, bool Visited, LyraFootPlantRigUpdateState Updated);

// Main73 node update, owned by the final Rig pose transaction.
// The original Main handler resolves DisableLegIK <= 0 && !UseFootPlacement;
// EnableControlRig is not that binding. Resolve it from committed Main feedback.
internal sealed class LyraFootPlantRigUpdateHost
{
    private readonly long _epoch;
    private long _frame;
    private LyraFootPlantRigUpdateCandidate? _pending;
    private LyraFootPlantRigUpdateState _after;
    private bool _evaluated;

    public LyraFootPlantRigUpdateState State { get; private set; } =
        new(AlsLinearBoolBlend.Default, 1, 0, false, false);

    public LyraFootPlantRigUpdateHost(LyraFootPlantRigNodeContract contract, long epoch)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (epoch <= 0) throw new ArgumentOutOfRangeException(nameof(epoch));
        _epoch = epoch;
    }

    public static bool ResolveEnabled(float committedDisableLegIk, bool useFootPlacement)
    {
        if (!float.IsFinite(committedDisableLegIk)) throw new ArgumentOutOfRangeException(nameof(committedDisableLegIk));
        return committedDisableLegIk <= 0 && !useFootPlacement;
    }

    public LyraFootPlantRigUpdateCandidate Prepare(long epoch, long frame, float delta,
        bool visited, bool initialize, bool crouching, bool moving, bool resolvedBool)
    {
        if (_pending is not null || epoch != _epoch || frame <= _frame ||
            !float.IsFinite(delta) || delta < 0)
            throw new InvalidOperationException("Invalid Main73 update frame.");
        var state = State;
        // CacheBones propagates inputs and runs requested construction even
        // when initialization precedes a frame with no subsequent visit.
        if (initialize)
            state = state with { Blend = state.Blend.Reinitialize(), RigDelta = 0,
                Crouching = crouching, Moving = moving };
        if (visited)
        {
            var blend = state.Blend.Step(resolvedBool, delta);
            state = new(blend, Math.Clamp(blend.Value, 0, 1), delta, crouching, moving);
        }
        _pending = new(this, epoch, frame, visited, state);
        _after = state; _evaluated = false;
        return _pending;
    }

    // Called after the enclosing pose has evaluated. A relevant Rig consumes
    // delta; alpha-zero passthrough keeps it unless pending Construction ran
    // before the alpha gate, as UControlRig does in UE.
    public void CompleteEvaluation(LyraFootPlantRigUpdateCandidate candidate, bool constructionExecuted = false)
    {
        Validate(candidate);
        if (!candidate.Visited) throw new InvalidOperationException("Hidden Main73 evaluation.");
        _after = constructionExecuted || candidate.Updated.Alpha > 1e-5f
            ? candidate.Updated with { RigDelta = 0 } : candidate.Updated;
        _evaluated = true;
    }

    public LyraFootPlantRigUpdateState Output(LyraFootPlantRigUpdateCandidate candidate)
    { Validate(candidate); return _after; }

    public void ValidateCommit(LyraFootPlantRigUpdateCandidate candidate, bool updateOnly)
    {
        Validate(candidate);
        if (updateOnly ? _evaluated : !_evaluated)
            throw new InvalidOperationException("Incomplete Main73 update transaction.");
    }

    public void Commit(LyraFootPlantRigUpdateCandidate candidate, bool updateOnly)
    {
        ValidateCommit(candidate, updateOnly);
        State = _after; _frame = candidate.Frame; _pending = null; _evaluated = false;
    }

    public void Cancel(LyraFootPlantRigUpdateCandidate candidate)
    { Validate(candidate); _pending = null; _evaluated = false; _after = State; }

    private void Validate(LyraFootPlantRigUpdateCandidate candidate)
    {
        if (!ReferenceEquals(candidate, _pending) || !ReferenceEquals(candidate.Owner, this) ||
            candidate.Epoch != _epoch || candidate.Frame <= _frame)
            throw new InvalidOperationException("Stale or foreign Main73 candidate.");
    }
}
