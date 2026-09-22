using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Physics;

public readonly record struct AlsIslandBody(AlsPrecisePose MassLocal, AlsJointInverseMass InverseMass,
    double LinearDamping = 0, double AngularDamping = 0, bool GravityEnabled = true, bool ExternallyDriven = false);
public readonly record struct AlsIslandBodyState(AlsPrecisePose Actor, AlsProjectionVelocity Velocity);
// Sorted, unique, zero-inverse-mass inputs captured by the world owner. Velocity
// is prescribed at the actor origin (cm/s, rad/s), not integrated a second time.
public readonly record struct AlsIslandKinematicTarget(int Body, AlsIslandBodyState State);
public readonly record struct AlsIslandProjection(bool Enabled, float LinearAlpha = 1,
    float TeleportDistance = 5, float VelocityAlpha = AlsLockedLinearProjection.ReferenceVelocityAlpha);
// Connector transforms are actor-local on input. Dynamic bodies solve in COM space;
// fixed bodies solve in actor space, even when the asset has an offset COM.
public readonly record struct AlsIslandJoint(int Parent, int Child, AlsPrecisePose ParentFrame,
    AlsPrecisePose ChildFrame, AlsAngularJointSettings Angular, AlsIslandProjection Projection, bool ConnectivityOnly = false);

// A single-owner, preallocated group of joints, not a collision world. All joints
// share one DP/DQ and velocity entry per body. No Godot/UE objects or global state.
// An optional contact owner shares the iteration buffers. Optional resolved
// sleep settings apply to this entire preassembled group, including wake input.
// Step accepts explicit gravity and per-step acceleration/impulse inputs;
// externally driven bodies are prescribed inputs; partial sleep/island discovery
// and kinematic target interpolation remain the world owner's responsibility.
public sealed class AlsJointIsland
{
    private readonly AlsIslandBody[] _bodies;
    private readonly AlsIslandJoint[] _joints;
    private readonly AlsIslandBodyState[] _states, _next, _external;
    private readonly AlsPrecisePose[] _initial, _predicted, _predictedActors;
    private readonly AlsProjectionDelta[] _deltas;
    private readonly AlsProjectionVelocity[] _velocities;
    private readonly AlsCachedJoint[] _cached;
    private readonly AlsLockedLinearProjection[] _projections;
    private readonly int[] _jointOrder;
    private readonly int _positionIterations, _velocityIterations;
    private readonly AlsIslandSleep? _sleep;
    private bool _wakeRequested;
    private AlsDoubleVector _lastGravity;
    private IAlsIslandContacts? _lastContacts;
    private bool _stepping;
    private IAlsIslandStepObserver? _observer;
    public void SetStepObserver(IAlsIslandStepObserver? observer)
    {
        if (_stepping) throw new InvalidOperationException("Cannot replace the observer during a step.");
        _observer = observer;
    }
    public int BodyCount => _bodies.Length;
    public int JointCount => _joints.Length;
    public AlsIslandBodyState BodyAt(int index) => _states[index];
    public AlsIslandBody BodyDefinitionAt(int index) => _bodies[index];
    public AlsIslandJoint JointDefinitionAt(int index) => _joints[index];
    public bool IsSleeping => _sleep?.Sleeping ?? false;
    public int SleepCounter => _sleep?.Counter ?? 0;
    public AlsSleepMetrics SleepMetricsAt(int index) => _sleep?.At(index) ?? throw new InvalidOperationException("Sleeping is not configured.");

    public AlsJointIsland(ReadOnlySpan<AlsIslandBody> bodies, ReadOnlySpan<AlsIslandJoint> joints,
        ReadOnlySpan<AlsIslandBodyState> initial, int positionIterations = 8, int velocityIterations = 2,
        ReadOnlySpan<AlsSleepBodySettings> sleepSettings = default, float sleepSmoothing = .3f)
    {
        if (bodies.Length == 0 || bodies.Length != initial.Length)
            throw new ArgumentException("An island requires one initial state per body.");
        if (positionIterations is < 1 or > 1024 || velocityIterations is < 0 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(positionIterations));
        _positionIterations = positionIterations; _velocityIterations = velocityIterations;
        _bodies = bodies.ToArray(); _joints = joints.ToArray(); _states = initial.ToArray();
        _next = new AlsIslandBodyState[bodies.Length];
        _external = new AlsIslandBodyState[bodies.Length];
        _initial = new AlsPrecisePose[bodies.Length]; _predicted = new AlsPrecisePose[bodies.Length];
        _predictedActors = new AlsPrecisePose[bodies.Length];
        _deltas = new AlsProjectionDelta[bodies.Length]; _velocities = new AlsProjectionVelocity[bodies.Length];
        _cached = new AlsCachedJoint[joints.Length]; _projections = new AlsLockedLinearProjection[joints.Length];
        _jointOrder = new int[joints.Length];
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i]; ValidatePose(body.MassLocal); ValidateState(i, _states[i]);
            _states[i] = StoreParticleState(_states[i]);
            if (body.ExternallyDriven && Dynamic(i)) throw new ArgumentException("External motion cannot replace a dynamic integration owner.");
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
            if (joint.ConnectivityOnly && (joint.Angular != default || joint.Projection != default))
                throw new ArgumentException("Connectivity-only joints must not discard solver settings.");
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
        PrepareExternal(default, out _);
        Gather(1d / 60);
        if (!sleepSettings.IsEmpty) _sleep = new(_bodies, sleepSettings, _states, sleepSmoothing);
    }

    public void Reset(ReadOnlySpan<AlsIslandBodyState> states)
    {
        if (_stepping) throw new InvalidOperationException("Cannot reset an island during a step.");
        if (states.Length != _states.Length) throw new ArgumentException("Body state count differs.");
        for (var i = 0; i < states.Length; i++) ValidateState(i, states[i]);
        for (var i = 0; i < states.Length; i++) _states[i] = StoreParticleState(states[i]);
        _sleep?.Reset(_states); _wakeRequested = false; _lastGravity = default; _lastContacts = null;
    }

    // Request is consumed only by a successful step. Solver failure leaves the
    // sleeping state and request intact, so the same inputs can be retried.
    public void RequestWake()
    {
        if (_stepping) throw new InvalidOperationException("Cannot request wake during a physics step.");
        _wakeRequested = true;
    }

    public void StepForceFree(double dt)
        => StepForceFree(dt, null);

    public void StepForceFree(double dt, IAlsIslandContacts? contacts)
        => Step(dt, default, default, contacts);

    public void Step(double dt, AlsDoubleVector gravity, ReadOnlySpan<AlsBodyStepForces> forces = default,
        IAlsIslandContacts? contacts = null, bool dragBeforeIntegration = false, bool allowSleep = true,
        ReadOnlySpan<AlsIslandKinematicTarget> kinematicTargets = default)
    {
        if (_stepping) throw new InvalidOperationException("Island stepping is not reentrant.");
        if (!double.IsFinite(dt) || dt <= 0 || !float.IsFinite(1 / (float)dt) || (float)dt == float.PositiveInfinity)
            throw new ArgumentOutOfRangeException(nameof(dt));
        if (!gravity.IsFinite) throw new ArgumentException("Gravity must be finite.");
        if (!forces.IsEmpty && forces.Length != BodyCount) throw new ArgumentException("One force input per body is required.");
        foreach (var force in forces) force.Validate();
        var externalChanged = PrepareExternal(kinematicTargets, out var externalMoving);
        allowSleep &= !externalMoving;
        var wake = !allowSleep || _wakeRequested || gravity != _lastGravity || !ReferenceEquals(contacts, _lastContacts) || (contacts?.RequiresWake ?? false);
        wake |= externalChanged;
        if (!forces.IsEmpty) for (var i = 0; i < forces.Length; i++) if (Dynamic(i) && forces[i] != default) wake = true;
        if (IsSleeping && !wake) return;
        _stepping = true;
        try { Solve(dt, gravity, forces, contacts, dragBeforeIntegration, wake, allowSleep); _lastGravity = gravity; _lastContacts = contacts; _wakeRequested = false; }
        catch { _sleep?.Abort(); contacts?.Abort(); throw; }
        finally { _stepping = false; }
    }

    private void Solve(double dt, AlsDoubleVector gravity, ReadOnlySpan<AlsBodyStepForces> forces,
        IAlsIslandContacts? contacts, bool dragBeforeIntegration, bool wake, bool allowSleep)
    {
        Gather(dt, gravity, forces, dragBeforeIntegration);
        contacts?.Gather(_predicted, _velocities, _bodies, dt, _states, _predictedActors);
        for (var j = 0; j < _jointOrder.Length; j++) _jointOrder[j] = j;
        contacts?.PrepareConstraintOrder(this, _jointOrder);
        var observer = _observer?.Enabled == true ? _observer : null;
        observer?.Begin(dt, _positionIterations, _velocityIterations, _bodies, _joints, _initial, _predicted, _velocities, _jointOrder);
        for (var iteration = 0; iteration < _positionIterations; iteration++)
        {
            // UE default equal priorities are stable-sorted by container order:
            // collisions are registered before linear joints in the evolution.
            contacts?.SolvePosition(_deltas, iteration, _positionIterations);
            observer?.Capture("position_contacts", iteration, _predicted, _deltas, _velocities);
            foreach (var j in _jointOrder)
            {
                var joint = _joints[j];
                if (!joint.ConnectivityOnly) _cached[j].SolvePosition(ref _deltas[joint.Parent], ref _deltas[joint.Child]);
            }
            observer?.Capture("position_joints", iteration, _predicted, _deltas, _velocities);
        }
        for (var i = 0; i < _bodies.Length; i++)
            _velocities[i] = AlsCachedJoint.AddImplicitVelocity(_velocities[i], _deltas[i], dt, Dynamic(i));
        observer?.Capture("implicit", 0, _predicted, _deltas, _velocities);
        for (var iteration = 0; iteration < _velocityIterations; iteration++)
        {
            contacts?.SolveVelocity(_velocities, iteration, _velocityIterations, dt);
            observer?.Capture("velocity_contacts", iteration, _predicted, _deltas, _velocities);
            foreach (var j in _jointOrder)
            {
                var joint = _joints[j];
                if (!joint.ConnectivityOnly) _cached[j].SolveVelocity(ref _velocities[joint.Parent], ref _velocities[joint.Child]);
            }
            observer?.Capture("velocity_joints", iteration, _predicted, _deltas, _velocities);
        }
        CommitCorrections();
        observer?.Capture("projection_input", 0, _predicted, _deltas, _velocities);
        // Native container caches ALL projection rows before any projection writes.
        foreach (var j in _jointOrder)
        {
            var joint = _joints[j]; var child = _bodies[joint.Child].InverseMass; var p = joint.Projection;
            if (joint.ConnectivityOnly) continue;
            _projections[j] = new(_predicted[joint.Parent], _predicted[joint.Child], joint.ParentFrame,
                joint.ChildFrame, (float)child.Mass, child.Inertia.ToSingle(), (float)joint.Angular.HardStiffness,
                p.LinearAlpha, p.TeleportDistance, p.Enabled);
        }
        foreach (var j in _jointOrder)
        {
            var joint = _joints[j];
            if (joint.ConnectivityOnly) continue;
            var added = _projections[j].Apply(_deltas[joint.Parent], ref _deltas[joint.Child], dt, joint.Projection.VelocityAlpha);
            ref var velocity = ref _velocities[joint.Child];
            velocity = new(velocity.Linear + added.Linear, velocity.Angular + added.Angular);
        }
        observer?.Capture("projection", 0, _predicted, _deltas, _velocities);
        CommitCorrections();
        observer?.Capture("corrected", 0, _predicted, _deltas, _velocities);
        observer?.Complete();
        // Publish as one batch. A failed step leaves all externally visible states
        // unchanged, and the next Gather overwrites every temporary and lambda.
        for (var i = 0; i < _bodies.Length; i++)
        {
            _next[i] = Dynamic(i) ? new(AlsRigidBodyIntegration.StoreActor(_predicted[i], _bodies[i].MassLocal), _velocities[i]) : _external[i];
            ValidateState(i, _next[i]);
        }
        contacts?.StageCommit();
        if (_sleep?.Stage(_states, _next, dt, wake, forces, allowSleep) == true)
            for (var i = 0; i < _next.Length; i++) if (Dynamic(i)) _next[i] = _next[i] with { Velocity = default };
        contacts?.Commit();
        _sleep?.Publish();
        _next.AsSpan().CopyTo(_states);
    }

    private void Gather(double dt, AlsDoubleVector gravity = default, ReadOnlySpan<AlsBodyStepForces> forces = default,
        bool dragBeforeIntegration = false)
    {
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i]; var state = _states[i]; _deltas[i] = default;
            // UE applies kinematic targets before gathering solver bodies, so
            // both X/R and P/Q already contain the prescribed transform.
            _initial[i] = Dynamic(i) ? AlsPrecisePose.Compose(body.MassLocal, state.Actor) : _external[i].Actor;
            var force = forces.IsEmpty ? default : forces[i];
            if (body.GravityEnabled) force = force with { Acceleration = force.Acceleration + gravity };
            var result = Dynamic(i) ? AlsRigidBodyIntegration.Predict(state.Actor, body.MassLocal,
                state.Velocity, body.LinearDamping, body.AngularDamping, dt, force, dragBeforeIntegration) : new AlsPredictedRigidBody(_external[i].Actor, _external[i].Velocity, _external[i].Actor);
            _predicted[i] = result.MassPose; _velocities[i] = result.Velocity; _predictedActors[i] = result.ActorPose;
        }
        for (var j = 0; j < _joints.Length; j++)
        {
            var joint = _joints[j];
            if (joint.ConnectivityOnly) continue;
            _cached[j] = new(Input(joint.Parent, joint.ParentFrame), Input(joint.Child, joint.ChildFrame), joint.Angular, dt);
        }
    }
    private AlsJointBodyInput Input(int body, AlsPrecisePose connector) =>
        new(_initial[body], _predicted[body], connector, _bodies[body].InverseMass);
    private bool Dynamic(int body) => _bodies[body].InverseMass.Mass > 0;
    private bool PrepareExternal(ReadOnlySpan<AlsIslandKinematicTarget> targets, out bool moving)
    {
        // No new target means hold pose and reset velocity on the next step.
        // Staging here does not publish an external pose if any solver stage fails.
        for (var i = 0; i < BodyCount; i++) _external[i] = _states[i] with { Velocity = default };
        var previous = -1;
        foreach (var target in targets)
        {
            if (target.Body <= previous || (uint)target.Body >= BodyCount || !_bodies[target.Body].ExternallyDriven)
                throw new ArgumentException("Kinematic targets must be sorted, unique and reference externally driven bodies.");
            ValidateState(target.Body, target.State); _external[target.Body] = StoreParticleState(target.State); previous = target.Body;
        }
        var changed = false; moving = false;
        for (var i = 0; i < BodyCount; i++) if (_bodies[i].ExternallyDriven)
        {
            changed |= _external[i] != _states[i];
            moving |= _external[i].Actor != _states[i].Actor || _external[i].Velocity != default;
        }
        return changed;
    }
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
        if (!Dynamic(body) && !_bodies[body].ExternallyDriven && state.Velocity != default)
            throw new NotSupportedException("Moving kinematics require a separate integration contract.");
    }
    // Native SetR/SetQ stores actor rotation as float before the first Gather,
    // reset or external target. Preserve double position and do not normalize
    // after rounding: that would undo the particle storage boundary.
    private static AlsIslandBodyState StoreParticleState(in AlsIslandBodyState state) =>
        state with { Actor = state.Actor with { Rotation = new(state.Actor.Rotation.ToSingle()) } };
    private static void ValidatePose(in AlsPrecisePose pose)
    {
        if (!pose.Position.IsFinite || pose.Scale != AlsDoubleVector.One ||
            !double.IsFinite(pose.Rotation.LengthSquared) || System.Math.Abs(pose.Rotation.LengthSquared - 1) > 1e-5)
            throw new ArgumentException("Solver poses must be finite rigid transforms with unit rotations.");
    }
}
