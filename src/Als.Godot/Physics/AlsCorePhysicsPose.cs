using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Physics;

// Bridges a committed imported FBX local pose and Core native WORLD bodies.
// Never samples a live skeleton or advances a second physics integration owner.
internal sealed class AlsCorePhysicsPose
{
    // Native local axes become FBX (X,-Y,Z), while native world axes become
    // Godot (Y,Z,-X). A bone frame needs this right-side basis, not merely the
    // conjugated world rotation used by the geometry query adapter.
    internal static readonly Transform3D FbxToWorld = new(new Basis(
        new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0)), Vector3.Zero);
    private readonly AlsRagdollPhysicsDefinition _definition;
    private readonly int[] _parents, _bodyToBone, _boneToBody;
    private readonly Transform3D[] _components;
    private readonly AlsLocalPose[] _seedPose;
    private readonly bool[] _rigidBone;
    private readonly AlsLocalPose[] _poseScratch;
    private readonly AlsIslandBodyState[] _states;
    private readonly uint _character, _generation;
    internal AlsFrameIdentity SeedIdentity { get; private set; }

    internal AlsCorePhysicsPose(AlsRagdollPhysicsDefinition definition, IReadOnlyList<string> names,
        ReadOnlySpan<int> parents, uint character, uint generation)
    {
        Main();
        if (generation == 0 || names.Count != parents.Length) throw new ArgumentException("Invalid physics skeleton scope.");
        _definition = definition; _parents = parents.ToArray(); _character = character; _generation = generation;
        for (var i = 0; i < parents.Length; i++) if (parents[i] < -1 || parents[i] >= i) throw new ArgumentException("Skeleton must be parent-first.");
        _bodyToBone = definition.Bind(names); _boneToBody = Enumerable.Repeat(-1, names.Count).ToArray();
        for (var i = 0; i < _bodyToBone.Length; i++) _boneToBody[_bodyToBone[i]] = i;
        _rigidBone = new bool[names.Count];
        foreach (var bone in _bodyToBone)
            for (var ancestor = bone; ancestor >= 0; ancestor = _parents[ancestor]) _rigidBone[ancestor] = true;
        _components = new Transform3D[names.Count]; _seedPose = new AlsLocalPose[names.Count];
        _poseScratch = new AlsLocalPose[names.Count]; _states = new AlsIslandBodyState[definition.Bodies.Length];
    }

    // Linear velocity is at component origin. Dynamic state stores COM velocity;
    // angular velocity is an axial vector and changes sign under handedness flips.
    // Destination may have an environment suffix, which this method preserves.
    internal void Seed(AlsFrameIdentity identity, Transform3D componentToWorld, ReadOnlySpan<AlsLocalPose> pose,
        Vector3 linearVelocity, Vector3 angularVelocity, Span<AlsIslandBodyState> destination)
    {
        Main(); Rigid(componentToWorld);
        if (identity.CharacterId != _character || identity.SlotGeneration != _generation || identity.FrameId <= 0 ||
            pose.Length != _parents.Length || destination.Length < _states.Length ||
            !linearVelocity.IsFinite() || !angularVelocity.IsFinite()) throw new ArgumentException("Invalid committed physics pose input.");
        for (var i = 0; i < pose.Length; i++)
        {
            var p = pose[i];
            if (!float.IsFinite(p.Rotation.LengthSquared()) || MathF.Abs(p.Rotation.LengthSquared() - 1) > .00001f)
                throw new ArgumentException("Physical pose requires unit rotations.");
            var local = AlsPhysicsBodySet.Local(p);
            if (!local.IsFinite()) throw new ArgumentException("Nonfinite animation transform.");
            // IK/virtual branches can carry animation scale without scaling any
            // physical body. Enforce rigidity only along physical ancestor chains.
            if (_rigidBone[i]) Rigid(local);
            _components[i] = _parents[i] < 0 ? local : _components[_parents[i]] * local;
        }
        for (var i = 0; i < _states.Length; i++)
        {
            var actorWorld = componentToWorld * _components[_bodyToBone[i]];
            // Every transform on this physical ancestor chain was validated as
            // rigid above. Float Basis products can still drift by ~1e-6 along
            // a long chain; reconstruct the rotation before native transport.
            actorWorld = actorWorld.Orthonormalized();
            var actor = FromWorld(actorWorld);
            var massWorld = actorWorld * AlsPhysicsBodySet.NativeToFbx(_definition.Bodies[i].MassLocal);
            var velocity = linearVelocity + angularVelocity.Cross(massWorld.Origin - componentToWorld.Origin);
            if (!velocity.IsFinite()) throw new ArgumentException("Physics handoff velocity overflow.");
            var linear = LinearToNative(velocity); var angular = AngularToNative(angularVelocity);
            if (!new AlsDoubleVector(linear).IsFinite || !new AlsDoubleVector(angular).IsFinite)
                throw new ArgumentException("Native physics handoff velocity overflow.");
            _states[i] = new(actor, _definition.Bodies[i].PhysicsType == 1 ? default : new(linear, angular));
        }
        // No externally visible mutation before every pose and velocity validates.
        _states.AsSpan().CopyTo(destination);
        pose.CopyTo(_seedPose);
        SeedIdentity = identity;
    }

    // Already-resolved native per-body velocities are inherited verbatim. They
    // are not a component velocity field and must not get another COM offset.
    internal void SeedWithBodyVelocities(AlsFrameIdentity identity, Transform3D componentToWorld,
        ReadOnlySpan<AlsLocalPose> pose, ReadOnlySpan<AlsProjectionVelocity> nativeVelocities,
        Span<AlsIslandBodyState> destination)
    {
        Main();
        if (nativeVelocities.Length != _states.Length) throw new ArgumentException("One native velocity per asset body is required.");
        foreach (var velocity in nativeVelocities)
            if (!new AlsDoubleVector(velocity.Linear).IsFinite || !new AlsDoubleVector(velocity.Angular).IsFinite)
                throw new ArgumentException("Native body velocity must be finite.");
        Seed(identity, componentToWorld, pose, Vector3.Zero, Vector3.Zero, destination);
        // Everything that can reject input has completed before these writes.
        for (var i = 0; i < _states.Length; i++)
            destination[i] = destination[i] with { Velocity = _definition.Bodies[i].PhysicsType == 1 ? default : nativeVelocities[i] };
    }

    internal void Capture(AlsJointIsland island, Transform3D componentToWorld, Span<AlsLocalPose> destination)
    {
        Main(); Rigid(componentToWorld);
        if (SeedIdentity.FrameId <= 0 || island.BodyCount < _states.Length || destination.Length != _parents.Length)
            throw new InvalidOperationException("Physical capture requires a seeded skeleton and matching body prefix.");
        var inverse = componentToWorld.AffineInverse();
        for (var i = 0; i < _parents.Length; i++)
        {
            var body = _boneToBody[i]; var parent = _parents[i]; Transform3D local;
            if (body >= 0)
            {
                _components[i] = inverse * ToWorld(island.BodyAt(body).Actor);
                local = parent < 0 ? _components[i] : _components[parent].AffineInverse() * _components[i];
            }
            else
            {
                local = AlsPhysicsBodySet.Local(_seedPose[i]); _components[i] = parent < 0 ? local : _components[parent] * local;
            }
            if (body >= 0) { Rigid(local); _poseScratch[i] = AlsPhysicsBodySet.Pose(local); }
            else _poseScratch[i] = _seedPose[i];
        }
        _poseScratch.AsSpan().CopyTo(destination);
    }

    internal static Transform3D ToWorld(AlsPrecisePose nativeActor) => AlsGodotContactQuery.ToGodot(nativeActor) * FbxToWorld;
    internal static AlsPrecisePose FromWorld(Transform3D fbxBoneWorld) => AlsSceneContactSet.FromWorld(fbxBoneWorld * FbxToWorld.AffineInverse());
    internal static NVector LinearToNative(Vector3 value) => AlsFootIkCoordinates.ToNative(new NVector(value.X, value.Y, value.Z)).ToSingle();
    internal static NVector AngularToNative(Vector3 value) => new(value.Z, -value.X, -value.Y);
    internal static Vector3 LinearFromNative(NVector value) { var v = AlsFootIkCoordinates.FromNative(new(value)); return new(v.X, v.Y, v.Z); }
    internal static Vector3 AngularFromNative(NVector value) => new(-value.Y, -value.Z, value.X);
    private static void Rigid(Transform3D value) => _ = AlsSceneContactSet.FromWorld(value);
    private static void Main() { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Physics pose transport requires Main."); }
}
