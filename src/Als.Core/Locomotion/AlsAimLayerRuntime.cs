using ScalarMath = System.Math;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public sealed class AlsAimLayerDefinition
{
    private readonly string[] _spineBones;
    public string SpineCurve { get; }
    public int AimCacheRead { get; }
    public int SpineCacheRead { get; }
    public AlsPoseCacheDefinition Cache { get; }
    public ReadOnlySpan<string> SpineBones => _spineBones;
    public AlsAimLayerDefinition(string spineCurve, int nodeCount, int cache, int aimRead, int spineRead, string[] spineBones)
    {
        if (string.IsNullOrWhiteSpace(spineCurve) || spineBones.Length != 4 ||
            spineBones.Any(string.IsNullOrWhiteSpace) || spineBones.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            throw new ArgumentException("Incomplete outer Aim/spine definition.");
        SpineCurve = spineCurve; AimCacheRead = aimRead; SpineCacheRead = spineRead;
        _spineBones = (string[])spineBones.Clone();
        Cache = new(nodeCount, [cache], [new(aimRead, cache), new(spineRead, cache)]);
    }
}

// The native AnimGraph segment from Post Layering through TwoWayBlend_6.
// Its output precedes hand IK, Foot IK and the normal/ragdoll root selector.
// The parent evaluates Post Layering once and supplies that cached payload here.
public sealed class AlsAimLayerRuntime : IAlsPoseCacheUpdateSink
{
    private readonly AlsAimLayerDefinition _definition;
    private readonly AlsPoseCacheTraversal _updates;
    private readonly int[] _parents, _spine;
    private readonly int _spineCurve, _curveCount;
    private readonly AlsPrecisePose[] _base, _additive, _aim, _rotated, _component;
    private readonly bool[] _componentPath;
    private readonly AlsQuaternion[] _rotations;
    private readonly AlsLocalPose[] _output;
    private readonly AlsInertialCurve[] _aimCurves, _outputCurves;
    private AlsPoseUpdateContext _postContext;
    private AlsFrameIdentity _identity;
    private double _spineYaw;
    private bool _prepared, _evaluated, _postUpdated;
    public AlsFrameIdentity CommittedIdentity { get; private set; }
    public float SpineAlpha { get; private set; }
    public float AimAlpha { get; private set; }
    public bool AimRelevant { get; private set; }
    public AlsPoseUpdateContext AimContext { get; private set; }
    public AlsPoseUpdateContext PostContext => _prepared && _postUpdated ? _postContext : throw new InvalidOperationException("No Post Layering request.");
    public ReadOnlySpan<AlsLocalPose> Pose => _evaluated ? _output : throw new InvalidOperationException("Aim layer has not evaluated.");
    public ReadOnlySpan<AlsInertialCurve> Curves => _evaluated ? _outputCurves : throw new InvalidOperationException("Aim layer has not evaluated.");

    public AlsAimLayerRuntime(AlsAimLayerDefinition definition, ReadOnlySpan<string> bones, ReadOnlySpan<int> parents, ReadOnlySpan<string> curves)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (bones.IsEmpty || bones.Length != parents.Length || bones.ToArray().Distinct(StringComparer.OrdinalIgnoreCase).Count() != bones.Length)
            throw new ArgumentException("Aim layer requires a complete logical skeleton.");
        for (var i = 0; i < parents.Length; i++) if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Bones must be parent first.");
        _definition = definition; _parents = parents.ToArray(); _curveCount = curves.Length;
        var names = bones.ToArray();
        _spine = definition.SpineBones.ToArray().Select(n => Array.FindIndex(names, b => b.Equals(n, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (_spine.Any(i => i < 0)) throw new ArgumentException("Missing original spine bone.");
        for (var i = 1; i < _spine.Length; i++) if (_parents[_spine[i]] != _spine[i - 1]) throw new ArgumentException("Original spine chain changed.");
        _componentPath = new bool[bones.Length];
        for (var bone = _spine[^1]; bone >= 0; bone = _parents[bone]) _componentPath[bone] = true;
        _spineCurve = curves.IndexOf(definition.SpineCurve);
        if (_spineCurve < 0 || curves.ToArray().Distinct(StringComparer.Ordinal).Count() != curves.Length)
            throw new ArgumentException("Aim layer curve layout is incomplete or duplicated.");
        _updates = new(definition.Cache, 2);
        _base = new AlsPrecisePose[bones.Length]; _additive = new AlsPrecisePose[bones.Length];
        _aim = new AlsPrecisePose[bones.Length]; _rotated = new AlsPrecisePose[bones.Length];
        _component = new AlsPrecisePose[bones.Length];
        _rotations = new AlsQuaternion[bones.Length * 2]; _output = new AlsLocalPose[bones.Length];
        _aimCurves = new AlsInertialCurve[curves.Length]; _outputCurves = new AlsInertialCurve[curves.Length];
    }

    public void Prepare(in AlsPoseUpdateContext context, in AlsLayeringInput layer, in AlsAimingInputState aiming,
        ReadOnlySpan<AlsInertialCurve> previousCurves)
    {
        if (_prepared || layer.Identity != context.Identity || aiming.Identity != context.Identity ||
            previousCurves.Length != _curveCount || context.Weight > 1 ||
            CommittedIdentity != default && (CommittedIdentity.CharacterId != context.Identity.CharacterId ||
                CommittedIdentity.SlotGeneration != context.Identity.SlotGeneration || context.Identity.FrameId <= CommittedIdentity.FrameId) ||
            !double.IsFinite(layer.EnableAimOffset) || !float.IsFinite((float)layer.EnableAimOffset) ||
            aiming.SpineRotation.Pitch != 0 || aiming.SpineRotation.Roll != 0 || !double.IsFinite(aiming.SpineRotation.Yaw))
            throw new ArgumentException("Invalid Aim layer candidate.");
        Validate([], previousCurves);
        _identity = context.Identity; _spineYaw = aiming.SpineRotation.Yaw;
        SpineAlpha = ScalarMath.Clamp(previousCurves[_spineCurve].Present ? previousCurves[_spineCurve].Value : 0, 0, 1);
        AimAlpha = ScalarMath.Clamp((float)layer.EnableAimOffset, 0, 1);
        var a = SpineAlpha < 1 - AlsPoseBlender.WeightThreshold;
        var b = SpineAlpha > AlsPoseBlender.WeightThreshold;
        var aWeight = a ? b ? 1 - SpineAlpha : 1 : 0;
        var bWeight = b ? a ? SpineAlpha : 1 : 0;
        AimRelevant = a && AimAlpha > AlsPoseBlender.WeightThreshold;
        AimContext = context.WithWeight(context.Weight * aWeight * AimAlpha);
        _prepared = true; _evaluated = false; _postUpdated = false;
        try
        {
            _updates.Begin(_identity);
            if (a) _updates.Use(_definition.AimCacheRead, context.WithWeight(context.Weight * aWeight));
            if (b) _updates.Use(_definition.SpineCacheRead, context.WithWeight(context.Weight * bWeight));
            _updates.Drain(this);
        }
        catch { Cancel(); throw; }
    }

    public void Evaluate(ReadOnlySpan<AlsLocalPose> postPose, ReadOnlySpan<AlsInertialCurve> postCurves,
        ReadOnlySpan<AlsLocalPose> aimPose, ReadOnlySpan<AlsInertialCurve> aimCurves)
    {
        if (!_prepared || _evaluated) throw new InvalidOperationException("Aim layer needs one prepared evaluation.");
        try
        {
            if (postPose.Length != _base.Length || postCurves.Length != _curveCount ||
                (AimRelevant ? aimPose.Length != _base.Length || aimCurves.Length != _curveCount : !aimPose.IsEmpty || !aimCurves.IsEmpty))
                throw new ArgumentException("Aim layer source layouts/relevance differ.");
            Validate(postPose, postCurves); Validate(aimPose, aimCurves);
            for (var i = 0; i < _base.Length; i++) _base[i] = new(postPose[i]);
            _base.CopyTo(_aim, 0); postCurves.CopyTo(_aimCurves);
            if (AimRelevant)
            {
                for (var i = 0; i < _base.Length; i++) _additive[i] = new(aimPose[i]);
                AlsPrecisePoseBlender.MeshApply(_base, _additive, _parents, _rotations, _aim, AimAlpha);
                for (var c = 0; c < _curveCount; c++)
                    _aimCurves[c] = AlsStandingCycleCurves.Accumulate(postCurves[c], aimCurves[c], AimAlpha);
            }
            var spine = SpineAlpha > AlsPoseBlender.WeightThreshold;
            var mix = spine && SpineAlpha < 1 - AlsPoseBlender.WeightThreshold;
            if (spine) RotateSpine();
            for (var i = 0; i < _base.Length; i++)
                _output[i] = (mix ? AlsPrecisePoseBlender.Blend(_aim[i], _rotated[i], SpineAlpha) : spine ? _rotated[i] : _aim[i]).ToSingle();
            for (var c = 0; c < _curveCount; c++)
                _outputCurves[c] = mix ? AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(_aimCurves[c], 1 - SpineAlpha), postCurves[c], SpineAlpha)
                    : spine ? postCurves[c] : _aimCurves[c];
            Validate(_output, _outputCurves); _evaluated = true;
        }
        catch { Cancel(); throw; }
    }

    private void RotateSpine()
    {
        _base.CopyTo(_rotated, 0);
        // This owner samples imported FBX bone poses, not Godot world-space
        // transforms. UE quaternion (x,y,z,w) maps to (-x,y,-z,w) here, so
        // positive UE yaw is about -Z. The scene's -Y yaw mapping belongs to
        // the character/component transform and must not be used in this pose.
        // UpdateAimingValues supplies yaw/4; apply it to all four authored bones.
        var half = -_spineYaw * ScalarMath.PI / 360;
        var rotation = new AlsQuaternion(0, 0, ScalarMath.Sin(half), ScalarMath.Cos(half));
        for (var bone = 0; bone < _base.Length; bone++)
        {
            if (!_componentPath[bone]) continue;
            var parent = _parents[bone];
            // FCSPose lazily converts only requested ancestors, normalizing
            // after local-to-component multiplication. Keep full TRS here;
            // the eventual relative transform also preserves scale policy.
            var component = parent < 0 ? _base[bone] : AlsPrecisePose.Compose(_base[bone], _component[parent]).Normalized();
            if (Array.IndexOf(_spine, bone) >= 0)
                component = component with { Rotation = rotation * component.Rotation };
            _component[bone] = component;
        }
        // Each ModifyBone targets the next descendant, so no already-cached
        // child is invalidated. Untouched local children remain untouched.
        for (var bone = _base.Length - 1; bone >= 0; bone--)
            if (_componentPath[bone]) _rotated[bone] = _parents[bone] < 0 ? _component[bone] :
                AlsPrecisePose.Relative(_component[bone], _component[_parents[bone]]).Normalized();
    }

    public void ValidateCommit(AlsFrameIdentity identity)
    { if (!_prepared || !_evaluated || identity != _identity) throw new InvalidOperationException("Aim layer has no completed candidate."); }
    public void Commit(AlsFrameIdentity identity) { ValidateCommit(identity); CommittedIdentity = identity; Cancel(); }
    public void Cancel() { _prepared = _evaluated = _postUpdated = false; }
    public void UpdateCachedSource(int cacheNodeIndex, in AlsPoseUpdateContext context)
    {
        if (cacheNodeIndex != _definition.Cache.UpdateOrder[0] || _postUpdated) throw new InvalidOperationException("Unexpected Post Layering cache update.");
        _postContext = context; _postUpdated = true;
    }
    public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
        throw new InvalidOperationException("The final parent must forward its skipped-update handler.");
    private static void Validate(ReadOnlySpan<AlsLocalPose> pose, ReadOnlySpan<AlsInertialCurve> curves)
    {
        foreach (var bone in pose) new AlsPrecisePose(bone).Validate(.001);
        foreach (var curve in curves) if (!float.IsFinite(curve.Value)) throw new ArgumentException("Nonfinite Aim layer curve.");
    }
}
