using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

// Single-owner bridge from committed native animation locals to per-step Core
// inputs. Solver joint indices must retain the physics asset's joint order.
// Current targets come from committed physics settings, never displayed bones.
public sealed class AlsRagdollMotorInputs
{
    private readonly record struct Binding(int Child, int Parent, AlsPrecisePose ChildFrame,
        AlsPrecisePose ParentFrame, AlsJointAngularDrive Drive);
    private readonly int[] _parents;
    private readonly Binding[] _bindings;
    private readonly AlsIslandAngularDrive[] _candidate;
    private readonly float _stiffnessScale, _dampingScale;
    public int OutputCount => _candidate.Length;

    public AlsRagdollMotorInputs(AlsRagdollPhysicsDefinition definition,
        ReadOnlySpan<AlsPhysicsJointSettings> settings, float stiffnessScale, float dampingScale)
    {
        if (settings.Length != definition.Joints.Length) throw new ArgumentException("Motor joint count differs.");
        Validate(stiffnessScale); Validate(dampingScale);
        _stiffnessScale = stiffnessScale; _dampingScale = dampingScale;
        _parents = definition.Bones.Select(b => b.Parent).ToArray();
        _bindings = new Binding[settings.Length];
        var count = 0;
        for (var i = 0; i < settings.Length; i++)
        {
            var joint = definition.Joints[i]; var drive = settings[i].AngularDrive;
            if (joint.Index != i || settings[i].Index != i) throw new ArgumentException("Motor joint order differs.");
            if (drive.SlerpPosition || drive.SlerpVelocity || drive.VelocityTarget != AlsDoubleVector.Zero)
                throw new NotSupportedException("Motor inputs require swing/twist drives and zero target velocity.");
            _bindings[i] = new(definition.Bodies[joint.ChildBody].BoneIndex,
                definition.Bodies[joint.ParentBody].BoneIndex, joint.ChildFrame, joint.ParentFrame, drive);
            if (Enabled(drive)) count++;
        }
        _candidate = new AlsIslandAngularDrive[count];
    }

    // ALS RefreshRagdolling: double speed -> float amount -> float spring.
    public static float SpringFromPelvisVelocity(AlsDoubleVector velocityCm)
    {
        var squared = AlsDoubleVector.Dot(velocityCm, velocityCm);
        if (!double.IsFinite(squared)) throw new ArgumentException("Invalid pelvis velocity.");
        return System.Math.Clamp((float)(System.Math.Sqrt(squared) / 1000f), 0f, 1f) * 25000f;
    }

    public void Evaluate(ReadOnlySpan<AlsPrecisePose> animationLocals,
        ReadOnlySpan<AlsQuaternion> committedTargets, AlsDoubleVector pelvisVelocityCm,
        Span<AlsIslandAngularDrive> output) => EvaluateParameters(animationLocals, committedTargets,
            SpringFromPelvisVelocity(pelvisVelocityCm), 0, output);

    // Explicit raw parameter boundary also permits independent native captures.
    // SetAllMotors uses an unlimited torque (zero) in ALS ragdolling.
    public void EvaluateParameters(ReadOnlySpan<AlsPrecisePose> animationLocals,
        ReadOnlySpan<AlsQuaternion> committedTargets, float spring, float damping,
        Span<AlsIslandAngularDrive> output)
    {
        Validate(spring); Validate(damping);
        // UE promotes the float profile parameters into Chaos FVec3 before
        // multiplying the engine scale. Do not round this product to float.
        var k = (double)spring * _stiffnessScale; var c = (double)damping * _dampingScale;
        if (animationLocals.Length != _parents.Length || committedTargets.Length != _bindings.Length || output.Length != OutputCount)
            throw new ArgumentException("Motor pose/target/output binding differs.");
        var index = 0;
        for (var i = 0; i < _bindings.Length; i++)
        {
            var binding = _bindings[i]; var drive = binding.Drive; var target = committedTargets[i];
            if (!Enabled(drive)) continue;
            if (!double.IsFinite(target.LengthSquared) || System.Math.Abs(target.LengthSquared - 1) > .001)
                throw new ArgumentException("Invalid committed motor target.");
            if ((drive.TwistPosition || drive.SwingPosition) && AlsJointMotorTarget.TryEvaluate(animationLocals,
                    _parents, binding.Child, binding.Parent, binding.ChildFrame, binding.ParentFrame, out var animated))
                target = animated;
            _candidate[index++] = new(i, target,
                new(drive.TwistPosition ? k : 0, drive.SwingPosition ? k : 0, drive.SwingPosition ? k : 0),
                new(drive.TwistVelocity ? c : 0, drive.SwingVelocity ? c : 0, drive.SwingVelocity ? c : 0));
        }
        // Failed evaluation must not partially overwrite the caller's step input.
        _candidate.AsSpan().CopyTo(output);
    }

    private static bool Enabled(AlsJointAngularDrive drive) =>
        drive.TwistPosition || drive.SwingPosition || drive.TwistVelocity || drive.SwingVelocity;

    private static void Validate(float value)
    {
        if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
    }
}
