using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsIslandBody(AlsPrecisePose MassLocal, AlsJointInverseMass InverseMass,
    double LinearDamping = 0, double AngularDamping = 0, bool GravityEnabled = true);
public readonly record struct AlsIslandBodyState(AlsPrecisePose Actor, AlsProjectionVelocity Velocity);
public readonly record struct AlsIslandProjection(bool Enabled, float LinearAlpha = 1,
    float TeleportDistance = 5, float VelocityAlpha = AlsLockedLinearProjection.ReferenceVelocityAlpha);
// Connector transforms are actor-local on input. Dynamic bodies solve in COM space;
// fixed bodies solve in actor space, even when the asset has an offset COM.
public readonly record struct AlsIslandJoint(int Parent, int Child, AlsPrecisePose ParentFrame,
    AlsPrecisePose ChildFrame, AlsAngularJointSettings Angular, AlsIslandProjection Projection);

// A single-owner, preallocated group of joints, not a collision world. All joints
// share one DP/DQ and velocity entry per body. No Godot/UE objects or global state.
// Awake bodies only. An optional contact owner shares the iteration buffers.
// Step accepts explicit gravity and per-step acceleration/impulse inputs;
// moving kinematics, sleep and island discovery remain separate work.
public sealed class AlsJointIsland
{
    private readonly AlsIslandBody[] _bodies;
    private readonly AlsIslandJoint[] _joints;
    private readonly AlsIslandBodyState[] _states, _next;
    private readonly AlsPrecisePose[] _initial, _predicted;
    private readonly AlsProjectionDelta[] _deltas;
    private readonly AlsProjectionVelocity[] _velocities;
    private readonly AlsCachedJoint[] _cached;
    private readonly AlsLockedLinearProjection[] _projections;
    private readonly int _positionIterations, _velocityIterations;
    private bool _stepping;
    public int BodyCount => _bodies.Length;
    public int JointCount => _joints.Length;
    public AlsIslandBodyState BodyAt(int index) => _states[index];

    public AlsJointIsland(ReadOnlySpan<AlsIslandBody> bodies, ReadOnlySpan<AlsIslandJoint> joints,
        ReadOnlySpan<AlsIslandBodyState> initial, int positionIterations = 8, int velocityIterations = 2)
    {
        if (bodies.Length == 0 || bodies.Length != initial.Length)
            throw new ArgumentException("An island requires one initial state per body.");
        if (positionIterations is < 1 or > 1024 || velocityIterations is < 0 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(positionIterations));
        _positionIterations = positionIterations; _velocityIterations = velocityIterations;
        _bodies = bodies.ToArray(); _joints = joints.ToArray(); _states = initial.ToArray();
        _next = new AlsIslandBodyState[bodies.Length];
        _initial = new AlsPrecisePose[bodies.Length]; _predicted = new AlsPrecisePose[bodies.Length];
        _deltas = new AlsProjectionDelta[bodies.Length]; _velocities = new AlsProjectionVelocity[bodies.Length];
        _cached = new AlsCachedJoint[joints.Length]; _projections = new AlsLockedLinearProjection[joints.Length];
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i]; ValidatePose(body.MassLocal); ValidateState(i, _states[i]);
            _ = AlsJointMassConditioning.Apply(body.InverseMass, default, 0, 0);
            if (!double.IsFinite(body.LinearDamping) || body.LinearDamping < 0 ||
                !double.IsFinite(body.AngularDamping) || body.AngularDamping < 0)
                throw new ArgumentException("Invalid body damping.");
        }
        for (var i = 0; i < _joints.Length; i++)
        {
            var joint = _joints[i];
            if ((uint)joint.Parent >= _bodies.Length || (uint)joint.Child >= _bodies.Length || joint.Parent == joint.Child)
                throw new ArgumentException("A joint must reference two distinct island bodies.");
            ValidatePose(joint.ParentFrame); ValidatePose(joint.ChildFrame);
            var projection = joint.Projection;
            if (projection.Enabled && !joint.Angular.UseSimd)
                throw new NotSupportedException("Only SIMD linear projection has been verified.");
            if (!float.IsFinite(projection.LinearAlpha) || projection.LinearAlpha is < 0 or > 1 ||
                !float.IsFinite(projection.TeleportDistance) || projection.TeleportDistance < 0 ||
                !float.IsFinite(projection.VelocityAlpha) || projection.VelocityAlpha < 0)
                throw new ArgumentException("Invalid projection settings.");
            _joints[i] = joint with { ParentFrame = SolverFrame(joint.Parent, joint.ParentFrame),
                ChildFrame = SolverFrame(joint.Child, joint.ChildFrame) };
        }
        // Validate every constraint before accepting a runnable island.
        Gather(1d / 60);
    }

    public void Reset(ReadOnlySpan<AlsIslandBodyState> states)
    {
        if (_stepping) throw new InvalidOperationException("Cannot reset an island during a step.");
        if (states.Length != _states.Length) throw new ArgumentException("Body state count differs.");
        for (var i = 0; i < states.Length; i++) ValidateState(i, states[i]);
        states.CopyTo(_states);
    }

    public void StepForceFree(double dt)
        => StepForceFree(dt, null);

    public void StepForceFree(double dt, IAlsIslandContacts? contacts)
        => Step(dt, default, default, contacts);

    public void Step(double dt, AlsDoubleVector gravity, ReadOnlySpan<AlsBodyStepForces> forces = default,
        IAlsIslandContacts? contacts = null, bool dragBeforeIntegration = false)
    {
        if (_stepping) throw new InvalidOperationException("Island stepping is not reentrant.");
        if (!double.IsFinite(dt) || dt <= 0 || !float.IsFinite(1 / (float)dt) || (float)dt == float.PositiveInfinity)
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (!gravity.IsFinite) throw new ArgumentException("Gravity must be finite.");
        if (!forces.IsEmpty && forces.Length != BodyCount) throw new ArgumentException("One force input per body is required.");
        foreach (var force in forces) force.Validate();
        _stepping = true;
        try { Solve(dt, gravity, forces, contacts, dragBeforeIntegration); }
        catch { contacts?.Abort(); throw; }
        finally { _stepping = false; }
    }

    private void Solve(double dt, AlsDoubleVector gravity, ReadOnlySpan<AlsBodyStepForces> forces,
        IAlsIslandContacts? contacts, bool dragBeforeIntegration)
    {
        Gather(dt, gravity, forces, dragBeforeIntegration);
        contacts?.Gather(_predicted, _velocities, _bodies, dt);
        for (var iteration = 0; iteration < _positionIterations; iteration++)
        {
            // UE default equal priorities are stable-sorted by container order:
            // collisions are registered before linear joints in the evolution.
            contacts?.SolvePosition(_deltas, iteration, _positionIterations);
            for (var j = 0; j < _joints.Length; j++)
            {
                var joint = _joints[j];
                _cached[j].SolvePosition(ref _deltas[joint.Parent], ref _deltas[joint.Child]);
            }
        }
        for (var i = 0; i < _bodies.Length; i++)
            _velocities[i] = AlsCachedJoint.AddImplicitVelocity(_velocities[i], _deltas[i], dt, Dynamic(i));
        for (var iteration = 0; iteration < _velocityIterations; iteration++)
        {
            contacts?.SolveVelocity(_velocities, iteration, _velocityIterations, dt);
            for (var j = 0; j < _joints.Length; j++)
            {
                var joint = _joints[j];
                _cached[j].SolveVelocity(ref _velocities[joint.Parent], ref _velocities[joint.Child]);
            }
        }
        CommitCorrections();
        // Native container caches ALL projection rows before any projection writes.
        for (var j = 0; j < _joints.Length; j++)
        {
            var joint = _joints[j]; var child = _bodies[joint.Child].InverseMass; var p = joint.Projection;
            _projections[j] = new(_predicted[joint.Parent], _predicted[joint.Child], joint.ParentFrame,
                joint.ChildFrame, (float)child.Mass, child.Inertia.ToSingle(), (float)joint.Angular.HardStiffness,
                p.LinearAlpha, p.TeleportDistance, p.Enabled);
        }
        for (var j = 0; j < _joints.Length; j++)
        {
            var joint = _joints[j];
            var added = _projections[j].Apply(_deltas[joint.Parent], ref _deltas[joint.Child], dt, joint.Projection.VelocityAlpha);
            ref var velocity = ref _velocities[joint.Child];
            velocity = new(velocity.Linear + added.Linear, velocity.Angular + added.Angular);
        }
        CommitCorrections();
        // Publish as one batch. A failed step leaves all externally visible states
        // unchanged, and the next Gather overwrites every temporary and lambda.
        for (var i = 0; i < _bodies.Length; i++)
        {
            _next[i] = Dynamic(i) ? new(AlsRigidBodyIntegration.StoreActor(_predicted[i], _bodies[i].MassLocal), _velocities[i]) : _states[i];
            ValidateState(i, _next[i]);
        }
        contacts?.StageCommit();
        contacts?.Commit();
        _next.AsSpan().CopyTo(_states);
    }

    private void Gather(double dt, AlsDoubleVector gravity = default, ReadOnlySpan<AlsBodyStepForces> forces = default,
        bool dragBeforeIntegration = false)
    {
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i]; var state = _states[i]; _deltas[i] = default;
            _initial[i] = Dynamic(i) ? AlsPrecisePose.Compose(body.MassLocal, state.Actor) : state.Actor;
            var force = forces.IsEmpty ? default : forces[i];
            if (body.GravityEnabled) force = force with { Acceleration = force.Acceleration + gravity };
            var result = Dynamic(i) ? AlsRigidBodyIntegration.Predict(state.Actor, body.MassLocal,
                state.Velocity, body.LinearDamping, body.AngularDamping, dt, force, dragBeforeIntegration) : new AlsPredictedRigidBody(state.Actor, default);
            _predicted[i] = result.MassPose; _velocities[i] = result.Velocity;
        }
        for (var j = 0; j < _joints.Length; j++)
        {
            var joint = _joints[j];
            _cached[j] = new(Input(joint.Parent, joint.ParentFrame), Input(joint.Child, joint.ChildFrame), joint.Angular, dt);
        }
    }
    private AlsJointBodyInput Input(int body, AlsPrecisePose connector) =>
        new(_initial[body], _predicted[body], connector, _bodies[body].InverseMass);
    private bool Dynamic(int body) => _bodies[body].InverseMass.Mass > 0;
    private AlsPrecisePose SolverFrame(int body, AlsPrecisePose frame) =>
        Dynamic(body) ? AlsPrecisePose.Relative(frame, _bodies[body].MassLocal) : frame;
    private void CommitCorrections()
    {
        for (var i = 0; i < _bodies.Length; i++)
        {
            if (Dynamic(i)) _predicted[i] = AlsLockedLinearProjection.Correct(_predicted[i], _deltas[i]);
            _deltas[i] = default;
        }
    }
    private void ValidateState(int body, in AlsIslandBodyState state)
    {
        ValidatePose(state.Actor);
        if (!new AlsDoubleVector(state.Velocity.Linear).IsFinite || !new AlsDoubleVector(state.Velocity.Angular).IsFinite)
            throw new ArgumentException("Body velocity must be finite.");
        if (!Dynamic(body) && state.Velocity != default)
            throw new NotSupportedException("Moving kinematics require a separate integration contract.");
    }
    private static void ValidatePose(in AlsPrecisePose pose)
    {
        if (!pose.Position.IsFinite || pose.Scale != AlsDoubleVector.One ||
            !double.IsFinite(pose.Rotation.LengthSquared) || System.Math.Abs(pose.Rotation.LengthSquared - 1) > 1e-5)
            throw new ArgumentException("Solver poses must be finite rigid transforms with unit rotations.");
    }
}
