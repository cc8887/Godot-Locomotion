using Godot;
using GodotAls.Animation;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

internal sealed class AlsCharacterModelBinding
{
    private readonly Skeleton3D _driver;
    private readonly AlsSkeletonPoseRetargetModel _retarget;
    private readonly AlsPrecisePose[] _input, _output;
    private readonly Transform3D[] _transforms;
    private readonly Vector3[] _savedPositions, _savedScales;
    private readonly Quaternion[] _savedRotations;
    private double _contactResidual, _contactBendLoss, _savedResidual, _savedBendLoss;
    private AlsDoubleVector _contactCorrection, _savedCorrection;
    private double _bodyPenetration, _bodyResidual, _savedBodyPenetration, _savedBodyResidual;
    private double _preparationWeight, _preparationSourceExcess, _savedPreparationWeight, _savedPreparationSourceExcess;
    internal Skeleton3D Skeleton { get; }
    internal double ContactResidualCm => _contactResidual;
    internal double ContactBendLossDegrees => _contactBendLoss;
    internal AlsDoubleVector ContactRootCorrectionCm => _contactCorrection;
    internal double ContactRootCorrectionLimitCm => _retarget.MaximumContactRootCorrection;
    internal double BodyPenetrationCm => _bodyPenetration;
    internal double BodyClearanceResidualCm => _bodyResidual;
    internal int BodyGeometryPointCount => _retarget.BodyGeometryPointCount;
    internal double PreparationWeight => _preparationWeight;
    internal double PreparationSourceReachExcessCm => _preparationSourceExcess;

    internal AlsCharacterModelBinding(Skeleton3D driver, Skeleton3D model)
    {
        _driver = driver; Skeleton = model;
        var count = driver.GetBoneCount();
        var names = Enumerable.Range(0, count).Select(i => driver.GetBoneName(i).ToString()).ToArray();
        var parents = Enumerable.Range(0, count).Select(driver.GetBoneParent).ToArray();
        var reference = Enumerable.Range(0, count).Select(i => NativePose(driver.GetBoneRest(i))).ToArray();
        _retarget = CreateRetarget(names, parents, reference, model, includeBodyGeometry: true);
        _input = new AlsPrecisePose[count]; _output = new AlsPrecisePose[count]; _transforms = new Transform3D[count];
        _savedPositions = new Vector3[count]; _savedScales = new Vector3[count]; _savedRotations = new Quaternion[count];
    }

    // Called by the existing visual frame writer, including its rollback and ragdoll paths.
    internal void Apply(bool matchEndEffectors = true, in AlsMantlingContactDiagnostics contacts = default)
    {
        for (var bone = 0; bone < _input.Length; bone++) _input[bone] = NativePose(_driver.GetBonePose(bone));
        Span<float> weights = stackalloc float[] { contacts.Applied ? contacts.LeftWeight : 0,
            contacts.Applied ? contacts.RightWeight : 0 };
        _retarget.Evaluate(_input, _output, matchEndEffectors, weights);
        WritePose();
    }

    internal void ApplyContactFrame(in AlsRetargetContactState committed, float characterDeltaSeconds,
        bool reset, out AlsRetargetContactState candidate, in AlsMantlingContactDiagnostics contacts,
        in GodotAls.Core.Actions.AlsMantlingFrame mantle,
        in (AlsRetargetContactPreparation Left, AlsRetargetContactPreparation Right) preparation)
    {
        for (var bone = 0; bone < _input.Length; bone++) _input[bone] = NativePose(_driver.GetBonePose(bone));
        Span<float> weights = stackalloc float[] { contacts.Applied ? contacts.LeftWeight : 0,
            contacts.Applied ? contacts.RightWeight : 0 };
        var previous = reset ? default : committed;
        Span<AlsRetargetContactPreparation> preparations = stackalloc AlsRetargetContactPreparation[]
            { preparation.Left, preparation.Right };
        var environment = default(AlsRetargetContactEnvironment);
        if (mantle.Active && mantle.HasContactBase)
        {
            var transform = Skeleton.GlobalTransform;
            var p = transform.Origin; var q = transform.Basis.GetRotationQuaternion(); var s = transform.Basis.Scale;
            var component = AlsFootIkCoordinates.FbxComponentToNativeWorld(new AlsLocalPose(
                new(p.X, p.Y, p.Z), new(q.X, q.Y, q.Z, q.W), new(s.X, s.Y, s.Z)));
            environment = new(true, AlsPrecisePose.Relative(mantle.ContactBase, component), .2);
        }
        // Presentation response, in the existing character's time domain. It
        // does not change support timing or the committed animation clock.
        _retarget.EvaluateContactFrame(_input, _output, weights, previous, characterDeltaSeconds,
            .2f, out candidate, environment, preparations);
        WritePose();
    }

    internal void CapturePose()
    {
        for (var bone = 0; bone < _savedPositions.Length; bone++)
        {
            _savedPositions[bone] = Skeleton.GetBonePosePosition(bone);
            _savedRotations[bone] = Skeleton.GetBonePoseRotation(bone);
            _savedScales[bone] = Skeleton.GetBonePoseScale(bone);
        }
        _savedResidual = _contactResidual; _savedBendLoss = _contactBendLoss; _savedCorrection = _contactCorrection;
        _savedBodyPenetration = _bodyPenetration; _savedBodyResidual = _bodyResidual;
        _savedPreparationWeight = _preparationWeight; _savedPreparationSourceExcess = _preparationSourceExcess;
    }

    internal void RestoreCapturedPose()
    {
        // Restore the actual presentation checkpoint, including a preceding
        // physical/ragdoll display. Re-evaluating it with animation IK would
        // change that pose and could restart a contact release.
        for (var bone = 0; bone < _savedPositions.Length; bone++)
        {
            Skeleton.SetBonePosePosition(bone, _savedPositions[bone]);
            Skeleton.SetBonePoseRotation(bone, _savedRotations[bone]);
            Skeleton.SetBonePoseScale(bone, _savedScales[bone]);
        }
        _contactResidual = _savedResidual; _contactBendLoss = _savedBendLoss; _contactCorrection = _savedCorrection;
        _bodyPenetration = _savedBodyPenetration; _bodyResidual = _savedBodyResidual;
        _preparationWeight = _savedPreparationWeight; _preparationSourceExcess = _savedPreparationSourceExcess;
    }

    internal bool CapturedPoseMatches()
    {
        for (var bone = 0; bone < _savedPositions.Length; bone++)
            if (Skeleton.GetBonePosePosition(bone) != _savedPositions[bone] ||
                Skeleton.GetBonePoseRotation(bone) != _savedRotations[bone] ||
                Skeleton.GetBonePoseScale(bone) != _savedScales[bone]) return false;
        return _contactResidual == _savedResidual && _contactBendLoss == _savedBendLoss && _contactCorrection == _savedCorrection &&
            _bodyPenetration == _savedBodyPenetration && _bodyResidual == _savedBodyResidual &&
            _preparationWeight == _savedPreparationWeight && _preparationSourceExcess == _savedPreparationSourceExcess;
    }

    private void WritePose()
    {
        for (var bone = 0; bone < _output.Length; bone++) _transforms[bone] = AlsMantlingVisualBinding.ToFbx(_output[bone]);
        for (var bone = 0; bone < _output.Length; bone++) Skeleton.SetBonePose(bone, _transforms[bone]);
        _contactResidual = _retarget.MaximumConditionalResidual;
        _contactBendLoss = _retarget.MaximumConditionalBendLossDegrees;
        _contactCorrection = _retarget.ContactRootCorrection;
        _bodyPenetration = _retarget.MaximumBodyPenetrationCm;
        _bodyResidual = _retarget.MaximumBodyClearanceResidualCm;
        _preparationWeight = _retarget.PreparationWeight;
        _preparationSourceExcess = _retarget.MaximumPreparationSourceReachExcessCm;
    }

    internal static AlsSkeletonPoseRetargetModel CreateRetarget(ReadOnlySpan<string> names, ReadOnlySpan<int> parents,
        ReadOnlySpan<AlsPrecisePose> reference, Skeleton3D model, bool includeBodyGeometry = false)
    {
        if (names.Length != parents.Length || reference.Length != names.Length)
            throw new ArgumentException("Model reference mapping differs.");
        var map = names.ToArray().Select((name, bone) => (name, bone)).ToDictionary(p => p.name, p => p.bone, StringComparer.OrdinalIgnoreCase);
        var target = reference.ToArray();
        for (var bone = 0; bone < model.GetBoneCount(); bone++)
        {
            var logical = map[model.GetBoneName(bone).ToString()];
            var parent = model.GetBoneParent(bone);
            if (parents[logical] != (parent < 0 ? -1 : map[model.GetBoneName(parent).ToString()]))
                throw new ArgumentException("Model physical/logical parents differ.");
            target[logical] = NativePose(model.GetBoneRest(bone));
            if (1 - Math.Abs(AlsQuaternion.Dot(reference[logical].Rotation, target[logical].Rotation)) > 1e-5)
                throw new ArgumentException("Retarget reference axes differ at " + names[logical]);
        }
        var translated = new[] { map["root"], map["pelvis"] }.Concat(map.Where(p =>
            p.Key.StartsWith("ik_", StringComparison.OrdinalIgnoreCase)).Select(p => p.Value)).ToArray();
        AlsRetargetLimb[] limbs = [new(map["thigh_l"], map["calf_l"], map["foot_l"]),
            new(map["thigh_r"], map["calf_r"], map["foot_r"])];
        AlsRetargetLimb[] contacts = [new(map["upperarm_l"], map["lowerarm_l"], map["hand_l"]),
            new(map["upperarm_r"], map["lowerarm_r"], map["hand_r"])];
        AlsRetargetContactMarker[] markers = [];
        if (model.HasMeta("als_retarget_foot_markers"))
        {
            var values = model.GetMeta("als_retarget_foot_markers").AsGodotDictionary();
            AlsDoubleVector ReadMarker(string name)
            {
                var value = values[name].AsVector3();
                return new(value.X * 100d, -value.Y * 100d, value.Z * 100d);
            }
            markers = [new(map["foot_l"], ReadMarker("source_l"), ReadMarker("target_l")),
                new(map["foot_r"], ReadMarker("source_r"), ReadMarker("target_r"))];
            GD.Print($"ALS_RETARGET_CONTACT_MARKERS left_source={markers[0].SourceLocal} left_target={markers[0].TargetLocal} right_source={markers[1].SourceLocal} right_target={markers[1].TargetLocal}");
        }
        var bodyGeometry = includeBodyGeometry
            ? new AlsSoleGeometryObserver(model, referenceOnly: true).CreateBodyGeometry(names, parents, map["pelvis"],
                limbs.Concat(contacts).ToArray()) : null;
        return new(parents, reference, target, translated, limbs, contacts, map["pelvis"], names, markers, bodyGeometry);
    }

    internal static AlsPrecisePose NativePose(Transform3D value)
    {
        if (!value.IsFinite()) throw new ArgumentException("Nonfinite model transform.");
        var q = value.Basis.GetRotationQuaternion().Normalized(); var p = value.Origin; var s = value.Basis.Scale;
        return new(new(p.X * 100d, -p.Y * 100d, p.Z * 100d), new(-q.X, q.Y, -q.Z, q.W), new(s.X, s.Y, s.Z));
    }

    internal static AlsPrecisePose NativePose(in AlsPrecisePose fbx)
    {
        fbx.Validate(); var p = fbx.Position; var q = fbx.Rotation;
        return new(new(p.X * 100, -p.Y * 100, p.Z * 100), new AlsQuaternion(-q.X, q.Y, -q.Z, q.W).Normalized(), fbx.Scale);
    }
}
