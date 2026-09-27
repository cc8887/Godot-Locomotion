using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using GodotAls.Locomotion;

namespace GodotAls.Physics;

// Owns one Core island and its scene bindings. Animation remains owned by the
// character: this object can only consume a committed pose, never sample a clip.
// Capsule/movement activation is the caller's responsibility after Create succeeds.
internal sealed class AlsCharacterRagdollSimulation : IDisposable
{
    private AlsSceneContactSet? _scene;
    private AlsGodotContactQuery? _query;
    private AlsPhysicsContactShapes? _shapes;
    private AlsCoreJointHost _host = null!;
    private AlsWorldContacts _contacts = null!;
    private AlsAnimatedJointInputs _animation = null!;
    private AlsP3Character _character = null!;
    private AlsCorePhysicsPose _bridge = null!;
    private AlsLocalPose[] _entryPose = [];
    private string[] _snapshotBones = [];
    private int[] _snapshotToLogical = [];
    private string _snapshotName = "", _meshName = "";
    private AlsPrecisePose[] _flail = [], _candidateFlail = [];
    private bool _hasFlail, _disposed;
    private AlsRagdollSpeedLimit _speedLimit;
    internal AlsRagdollActivationFrame Activation { get; private set; }
    internal AlsFrameIdentity AnimationIdentity { get; private set; }
    internal long CompletedSteps { get; private set; }
    internal AlsJointIsland Island => _host.Island;
    internal AlsRagdollSpeedLimit SpeedLimit => _speedLimit;
    internal AlsDoubleVector PelvisVelocity => _animation.PelvisVelocity;
    internal Vector3 PelvisPosition => new((float)(_animation.PelvisPosition.Y * .01),
        (float)(_animation.PelvisPosition.Z * .01), (float)(-_animation.PelvisPosition.X * .01));
    internal int EnvironmentBodies => _scene!.BodyCount;
    internal int LastContactCount => _contacts.LastContactCount;

    private AlsCharacterRagdollSimulation() { }

    internal static AlsCharacterRagdollSimulation Create(Node parent, Node environment,
        AlsP3Character character, AlsP3RuntimeContext context, bool limitInitialSpeed = true)
    {
        Main();
        var result = new AlsCharacterRagdollSimulation();
        try { result.Initialize(parent, environment, character, context, limitInitialSpeed); return result; }
        catch { result.Dispose(); throw; }
    }

    private void Initialize(Node parent, Node environment, AlsP3Character character,
        AlsP3RuntimeContext context, bool limitInitialSpeed)
    {
        if (!parent.IsInsideTree()) throw new ArgumentException("Ragdoll owner must be in the scene tree.", nameof(parent));
        _character = character;
        var history = character.BodyHistory ?? throw new InvalidOperationException("Character has no physical history.");
        var profile = history.Profile;
        var skeleton = context.AnimationSet.Skeletons[context.Profile.SkeletonId];
        var mesh = context.AnimationSet.SkeletalMeshes[context.Profile.MannequinMeshId];
        if (profile.Definition.Mesh != mesh.ObjectPath)
            throw new InvalidOperationException("Ragdoll profile belongs to another mesh.");
        _snapshotName = context.MovementGraph?.RagdollPose.SnapshotName ??
            throw new InvalidOperationException("Ragdoll recovery requires the complete root graph.");
        _meshName = mesh.Name;
        _snapshotBones = skeleton.PhysicalBones.Select(b => b.Name).ToArray();
        _snapshotToLogical = skeleton.PhysicalToLogical.ToArray();
        var definition = profile.Definition;
        var names = skeleton.LogicalBones.Select(b => b.Name).ToArray();
        var parents = skeleton.LogicalBones.Select(b => b.ParentLogicalId).ToArray();
        _entryPose = new AlsLocalPose[names.Length]; _flail = new AlsPrecisePose[names.Length];
        _candidateFlail = new AlsPrecisePose[names.Length];
        var identity = character.Diagnostics.Identity;
        var entryBodies = new AlsIslandBodyState[definition.Bodies.Length];
        Activation = history.PrepareActivation(identity, limitInitialSpeed, entryBodies);
        character.CopyCommittedAnimationPose(identity, _entryPose);
        _bridge = new(definition, names, parents, identity.CharacterId, identity.SlotGeneration);
        // Seed the capture bridge with the same animation, without replacing the
        // already resolved/clamped entry states supplied by the character.
        var scratch = new AlsIslandBodyState[entryBodies.Length];
        _bridge.Seed(identity, Activation.Entry.SkeletonToWorld, _entryPose, Vector3.Zero, Vector3.Zero, scratch);
        for (var i = 0; i < scratch.Length; i++)
            if (scratch[i].Actor != entryBodies[i].Actor) throw new InvalidOperationException("Activation pose binding differs.");

        _scene = new(environment, entryBodies.Length);
        var bodies = new AlsIslandBody[entryBodies.Length + _scene.BodyCount];
        var states = new AlsIslandBodyState[bodies.Length]; entryBodies.CopyTo(states, 0);
        var sleepSettings = new AlsSleepBodySettings[bodies.Length]; profile.Sleep.Bodies.CopyTo(sleepSettings, 0);
        foreach (var body in definition.Bodies)
            bodies[body.Index] = new(body.MassLocal, body.PhysicsType == 1 ? default :
                new((float)(1 / body.MassKg), new AlsDoubleVector(profile.Conditioning[body.Index].ConditionedInverseInertia)),
                body.Defaults.GetProperty("linearDamping").GetDouble(), body.Defaults.GetProperty("angularDamping").GetDouble(),
                body.Defaults.GetProperty("bEnableGravity").GetBoolean());
        _scene.InitializeBodies(bodies, states);
        var island = new AlsJointIsland(bodies, profile.Joints, states, sleepSettings: sleepSettings,
            sleepSmoothing: profile.Sleep.Smoothing);
        _animation = new(profile.AuthoredDefinition, profile.Settings, names, parents, island, 1.5f, 1.5f);
        _animation.Prepare(_entryPose); // Reject incompatible motor bindings before publishing any owner.

        var registry = new AlsContactRegistry(bodies.Length, definition.Bodies.Sum(b => b.Shapes.Length) + _scene.ShapeCount);
        _query = new(registry, profile.Detector);
        _shapes = new(); _shapes.Bind(definition, registry, _query, profile.Shapes);
        foreach (var body in definition.Bodies)
            _query.BindBodyBounds(body.Index, profile.Conditioning[body.Index].NativeBoundsSize);
        _scene.Bind(registry, _query);
        var material = definition.Bodies[0].Material;
        var friction = material.GetProperty("friction").GetSingle();
        var staticFriction = Math.Max(friction, material.GetProperty("staticFriction").GetSingle());
        var restitution = material.GetProperty("restitution").GetSingle();
        foreach (var body in definition.Bodies)
            if (body.Material.GetProperty("friction").GetSingle() != friction ||
                Math.Max(friction, body.Material.GetProperty("staticFriction").GetSingle()) != staticFriction ||
                body.Material.GetProperty("restitution").GetSingle() != restitution ||
                body.Material.GetProperty("frictionCombine").GetInt32() != 0 ||
                body.Material.GetProperty("restitutionCombine").GetInt32() != 0 ||
                body.Material.GetProperty("overrideFrictionCombine").GetBoolean() ||
                body.Material.GetProperty("overrideRestitutionCombine").GetBoolean() ||
                body.Defaults.GetProperty("gravityGroupIndex").GetInt32() != 0 ||
                body.Defaults.GetProperty("bGyroscopicTorqueEnabled").GetBoolean())
                throw new NotSupportedException("Ragdoll requires the supported homogeneous material and gravity policy.");
        var contactSettings = profile.Contact;
        var overlap = new float[bodies.Length]; contactSettings.BodyOverlapVelocities.CopyTo(overlap, 0);
        _contacts = new(registry, _query, new(staticFriction, friction, friction),
            new(1f / Engine.PhysicsTicksPerSecond, restitution, contactSettings.RestitutionThreshold, contactSettings.MaxPushOutVelocity),
            16, island, bodyOverlapVelocities: contactSettings.EnableInitialDepenetration ? overlap : []);
        _host = new(definition, island);
        _speedLimit = Activation.SpeedLimit; AnimationIdentity = identity;
    }

    // Holding an animation frame still advances physics and its speed budget.
    // This is also the entry motor pose until the shared graph commits Flail.
    internal void StepHeldAnimation(double delta)
    {
        Check();
        Step(delta, _hasFlail ? _animation.Prepare(_flail) : _animation.Prepare(_entryPose));
    }

    // Ordinary caller: consume only its existing shared graph. A missing Flail
    // in a newer frame is a wiring error, not permission to sample another clock
    // or feed the physically blended final pose back into the motor targets.
    internal void StepCharacterAnimation(double delta)
    {
        Check();
        if (!_character.BodyHistoryActive)
            throw new InvalidOperationException("Ragdoll animation owner is inactive.");
        var identity = _character.Diagnostics.Identity;
        if (identity == AnimationIdentity) { StepHeldAnimation(delta); return; }
        if (!_character.TryCopyCommittedPreciseFlail(identity, _candidateFlail))
            throw new InvalidOperationException("The shared character graph has no committed Flail pose.");
        StepCommittedFlail(delta, identity, _candidateFlail);
    }

    internal void StepCommittedFlail(double delta, AlsFrameIdentity identity, ReadOnlySpan<AlsPrecisePose> pose)
    {
        Check();
        if (identity.CharacterId != AnimationIdentity.CharacterId || identity.SlotGeneration != AnimationIdentity.SlotGeneration ||
            identity.FrameId <= AnimationIdentity.FrameId || pose.Length != _flail.Length)
            throw new ArgumentException("Flail requires a newer committed frame of the same character and exact skeleton.");
        var drives = _animation.Prepare(pose);
        Step(delta, drives);
        pose.CopyTo(_flail); _hasFlail = true; AnimationIdentity = identity;
    }

    private void Step(double delta, ReadOnlySpan<AlsIslandAngularDrive> drives)
    {
        if (CompletedSteps == long.MaxValue) throw new InvalidOperationException("Ragdoll step sequence exhausted.");
        _host.StepRagdollScene(delta, new(0, 0, -980), _contacts, _scene!, drives, ref _speedLimit);
        CompletedSteps++;
    }

    internal void Capture(Transform3D skeletonToWorld, Span<AlsLocalPose> destination)
    { Check(); _bridge.Capture(Island, skeletonToWorld, destination); }
    internal void Capture(Transform3D skeletonToWorld, ReadOnlySpan<AlsLocalPose> animationPose, Span<AlsLocalPose> destination)
    { Check(); _bridge.Capture(Island, skeletonToWorld, animationPose, destination); }

    internal AlsRagdollExitDecision DecideExit(bool grounded)
    { Check(); return AlsRagdollExit.Decide(_animation.PelvisRotation, PelvisVelocity, grounded); }

    // The caller supplies the proposed RESTORED mesh world transform, not the
    // current followed capsule transform. Capture rebases physical bones to it
    // while retaining current animation locals on nonphysical branches.
    internal AlsRagdollRecoveryFrame PrepareRecovery(AlsFrameIdentity identity,
        Transform3D restoredSkeletonToWorld, bool grounded)
    {
        Check();
        if (!_character.BodyHistoryActive || _character.RagdollSimulation != this || CompletedSteps <= 0 ||
            identity != AnimationIdentity || identity != _character.Diagnostics.Identity ||
            _character.PublishedFrameId != _character.RuntimeCommittedFrameId)
            throw new InvalidOperationException("Recovery requires this character's idle completed ragdoll frame.");
        var decision = DecideExit(grounded);
        var animation = new AlsLocalPose[_entryPose.Length];
        _character.CopyCommittedAnimationPose(identity, animation);
        var rebased = new AlsLocalPose[animation.Length];
        Capture(restoredSkeletonToWorld, animation, rebased);
        var physical = new AlsLocalPose[_snapshotToLogical.Length];
        for (var i = 0; i < physical.Length; i++)
        {
            if ((uint)_snapshotToLogical[i] >= (uint)rebased.Length)
                throw new InvalidOperationException($"Snapshot bone {i} has invalid logical binding {_snapshotToLogical[i]}.");
            physical[i] = rebased[_snapshotToLogical[i]];
        }
        var snapshot = new AlsNamedPoseSnapshot(identity, _snapshotName, _meshName, _snapshotBones, physical);
        return new(Activation.Entry.Identity, CompletedSteps, decision, restoredSkeletonToWorld, snapshot);
    }

    internal bool IsRecoveryCurrent(AlsRagdollRecoveryFrame recovery)
    {
        Check();
        return _character.BodyHistoryActive && _character.RagdollSimulation == this &&
            recovery.ActivationIdentity == Activation.Entry.Identity && recovery.CompletedSteps == CompletedSteps &&
            recovery.Snapshot.Identity == AnimationIdentity && AnimationIdentity == _character.Diagnostics.Identity &&
            _character.PublishedFrameId == _character.RuntimeCommittedFrameId;
    }

    private static void Main()
    { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Character ragdoll simulation requires Main."); }
    private void Check() { Main(); ObjectDisposedException.ThrowIf(_disposed, this); }
    public void Dispose()
    {
        Main(); if (_disposed) return;
        // Scene binding rejects disposal under a locked solve. Do not poison
        // this owner before that guard succeeds: the same step must be retryable.
        _scene?.Dispose(); _query?.Dispose(); _shapes?.Dispose();
        _disposed = true;
    }
}
