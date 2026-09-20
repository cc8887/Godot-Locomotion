using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Main-thread body ownership and pose transport. The joint solver is a separate
// consumer of BodyAt()/BoneToMass(); this alone must not enable gameplay Ragdoll.
internal sealed class AlsPhysicsBodySet : IDisposable
{
    private readonly Node3D _root;
    private readonly AlsRagdollPhysicsDefinition _definition;
    private readonly RigidBody3D[] _bodies;
    private readonly Transform3D[] _massLocal, _components, _seedLocal;
    private readonly Vector3[] _savedLinear, _savedAngular;
    private readonly int[] _boneToBody, _parents, _bodyToBone;
    private readonly uint _character, _generation, _layer, _mask;
    private readonly bool _continuousCollisionDetection;
    private bool _disposed, _active, _suspended;
    internal AlsFrameIdentity PoseIdentity { get; private set; }
    internal int BodyCount => _bodies.Length;
    internal bool Active => _active;
    internal RigidBody3D BodyAt(int index) { Check(); return _bodies[index]; }
    internal Transform3D BoneToMass(int index) { Check(); return _massLocal[index]; }

    internal AlsPhysicsBodySet(Node parent, AlsRagdollPhysicsDefinition definition,
        IReadOnlyList<string> names, ReadOnlySpan<int> parents, uint character, uint generation,
        uint collisionLayer = 2, uint collisionMask = 3, bool continuousCollisionDetection = true)
    {
        Main();
        if (!parent.IsInsideTree() || generation == 0 || names.Count != parents.Length)
            throw new ArgumentException("Physics bodies require an active Main parent and a scoped skeleton.");
        _definition = definition; _character = character; _generation = generation; _layer = collisionLayer; _mask = collisionMask;
        _continuousCollisionDetection = continuousCollisionDetection;
        _parents = parents.ToArray(); _bodyToBone = definition.Bind(names);
        _boneToBody = Enumerable.Repeat(-1,names.Count).ToArray();
        for (var i = 0; i < _parents.Length; i++)
            if (_parents[i] < -1 || _parents[i] >= i) throw new ArgumentException("Physics skeleton must be parent-first.");
        for (var i = 0; i < _bodyToBone.Length; i++) _boneToBody[_bodyToBone[i]] = i;
        _bodies = new RigidBody3D[definition.Bodies.Length]; _massLocal = new Transform3D[_bodies.Length];
        _components = new Transform3D[names.Count]; _seedLocal = new Transform3D[names.Count];
        _savedLinear = new Vector3[_bodies.Length]; _savedAngular = new Vector3[_bodies.Length];
        _root = new Node3D { Name = "ALS_PhysicsBodies", TopLevel = true }; parent.AddChild(_root);
        try
        {
            for (var i = 0; i < _bodies.Length; i++) CreateBody(i);
            foreach (var (a,b) in definition.DisabledCollisions)
            { _bodies[a].AddCollisionExceptionWith(_bodies[b]); _bodies[b].AddCollisionExceptionWith(_bodies[a]); }
        }
        catch { Dispose(); throw; }
    }

    private void CreateBody(int index)
    {
        var source = _definition.Bodies[index]; _massLocal[index] = NativeToFbx(source.MassLocal);
        var material = source.Material;
        var body = new RigidBody3D
        {
            Name = source.Bone, Freeze = true, FreezeMode = RigidBody3D.FreezeModeEnum.Static,
            CollisionLayer = 0, CollisionMask = 0, Mass = (float)source.MassKg,
            // Body axes are the exported principal mass axes. This preserves the
            // complete rotated tensor, rather than assigning its diagonal to bone axes.
            CenterOfMassMode = RigidBody3D.CenterOfMassModeEnum.Custom, CenterOfMass = Vector3.Zero,
            Inertia = Vec(source.InertiaKgCm2 * .0001),
            LinearDampMode = RigidBody3D.DampMode.Replace, AngularDampMode = RigidBody3D.DampMode.Replace,
            LinearDamp = source.Defaults.GetProperty("linearDamping").GetSingle(),
            AngularDamp = source.Defaults.GetProperty("angularDamping").GetSingle(),
            GravityScale = source.Defaults.GetProperty("bEnableGravity").GetBoolean() ? 1 : 0,
            // Explicit Godot transport policy: discrete 30 Hz bodies can cross a
            // thin floor. The original UE CCD flag remains in the source definition.
            ContinuousCd = _continuousCollisionDetection || source.Defaults.GetProperty("bUseCCD").GetBoolean(),
            PhysicsMaterialOverride = new PhysicsMaterial { Friction = material.GetProperty("friction").GetSingle(),
                Bounce = material.GetProperty("restitution").GetSingle() },
        };
        _bodies[index] = body; _root.AddChild(body);
        foreach (var sourceShape in source.Shapes)
        {
            // These exported assets use QueryAndPhysics for every shape. Do not
            // silently turn a query-only shape into a colliding rigid body shape.
            if (sourceShape.CollisionEnabled is not (0 or 3)) throw new NotSupportedException("Physics shape collision mode requires a separate query adapter.");
            var local = NativeToFbx(sourceShape.Local);
            Shape3D shape;
            switch (sourceShape.Type)
            {
                case "sphere": shape = new SphereShape3D { Radius = (float)(sourceShape.RadiusCm * .01) }; break;
                case "box": shape = new BoxShape3D { Size = Vec(sourceShape.SizeCm * .01) }; break;
                case "capsule":
                    shape = new CapsuleShape3D { Radius = (float)(sourceShape.RadiusCm * .01),
                        Height = (float)((sourceShape.CylinderLengthCm + 2*sourceShape.RadiusCm) * .01) };
                    // UE capsules extend along Z; Godot capsules extend along Y.
                    local *= new Transform3D(new Basis(Vector3.Right,Mathf.Pi/2),Vector3.Zero); break;
                case "convex":
                    // Bake the authored convex scale into points. Godot collision
                    // transforms remain rigid, including the mass-frame correction.
                    shape = new ConvexPolygonShape3D { Points = sourceShape.VerticesCm.Select(v =>
                        FbxPosition(v * sourceShape.Local.Scale)).ToArray() };
                    local = new Transform3D(local.Basis.Orthonormalized(),local.Origin); break;
                default: throw new NotSupportedException("Unmapped physics shape: " + sourceShape.Type);
            }
            // RestOffset and Godot collision margin have different semantics.
            // These assets author zero rest offset; nonzero requires an adapter.
            if (sourceShape.RestOffsetCm != 0) throw new NotSupportedException("Nonzero UE rest offset requires a contact adapter.");
            shape.Margin = 0;
            body.AddChild(new CollisionShape3D { Shape = shape, Transform = _massLocal[index].AffineInverse() * local,
                Disabled = sourceShape.CollisionEnabled == 0 });
        }
    }

    // Accept a committed local pose in the target skeleton's FBX basis. No live
    // Skeleton3D or partially updated worker pose is read by the physics owner.
    internal void Seed(AlsFrameIdentity identity, Transform3D componentToWorld, ReadOnlySpan<AlsLocalPose> localPose,
        Vector3 linearVelocity, Vector3 angularVelocity)
    {
        Check();
        if (_active || identity.CharacterId != _character || identity.SlotGeneration != _generation || identity.FrameId <= 0 ||
            localPose.Length != _parents.Length || !linearVelocity.IsFinite() || !angularVelocity.IsFinite())
            throw new InvalidOperationException("Invalid or active physics pose handoff.");
        Rigid(componentToWorld);
        // Validate the entire input before changing physical state.
        foreach (var pose in localPose)
        {
            if (!float.IsFinite(pose.Rotation.LengthSquared()) || MathF.Abs(pose.Rotation.LengthSquared()-1) > .001f)
                throw new ArgumentException("Physics pose requires unit rotation quaternions.");
            Rigid(Local(pose));
        }
        for (var i = 0; i < localPose.Length; i++)
        {
            _seedLocal[i] = Local(localPose[i]);
            _components[i] = _parents[i] < 0 ? _seedLocal[i] : _components[_parents[i]] * _seedLocal[i];
        }
        for (var i = 0; i < _bodies.Length; i++)
        {
            var transform = componentToWorld * _components[_bodyToBone[i]] * _massLocal[i];
            _bodies[i].GlobalTransform = transform;
            _savedLinear[i] = linearVelocity + angularVelocity.Cross(transform.Origin-componentToWorld.Origin);
            _savedAngular[i] = angularVelocity;
        }
        PoseIdentity = identity; _suspended = false;
    }

    internal void Start()
    {
        Check(); if (_active || PoseIdentity.FrameId <= 0) throw new InvalidOperationException("Physics start requires a fresh seeded pose.");
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i]; body.CollisionLayer = _layer; body.CollisionMask = _mask;
            body.FreezeMode = _definition.Bodies[i].PhysicsType == 1 ? RigidBody3D.FreezeModeEnum.Kinematic : RigidBody3D.FreezeModeEnum.Static;
            body.Freeze = _definition.Bodies[i].PhysicsType == 1;
            if (body.Freeze) PhysicsServer3D.BodySetState(body.GetRid(),PhysicsServer3D.BodyState.Transform,body.GlobalTransform);
            if (!body.Freeze) { body.LinearVelocity = _savedLinear[i]; body.AngularVelocity = _savedAngular[i]; body.Sleeping = false; }
        }
        _active = true; _suspended = false;
    }

    internal void Suspend()
    {
        Check(); if (!_active) return;
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i]; _savedLinear[i] = body.LinearVelocity; _savedAngular[i] = body.AngularVelocity;
            // Kinematic mode is not a pause: GodotBody3D integrates its queued
            // new_transform when contact reporting keeps it active. Static mode
            // holds the current transform and cannot replay that stale target.
            body.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
            body.Freeze = true; body.CollisionLayer = 0; body.CollisionMask = 0;
        }
        _active = false; _suspended = true;
    }
    internal void Resume()
    { Check(); if (!_suspended) throw new InvalidOperationException("Physics resume requires suspension."); Start(); }

    internal void Stop()
    {
        Check(); Suspend(); _suspended = false; PoseIdentity = default;
    }

    internal void CaptureLocalPose(Transform3D componentToWorld, Span<AlsLocalPose> destination)
    {
        Check(); Rigid(componentToWorld);
        if (PoseIdentity.FrameId <= 0 || destination.Length != _parents.Length) throw new InvalidOperationException("Physics snapshot requires a seeded skeleton.");
        var inverse = componentToWorld.AffineInverse();
        for (var bone = 0; bone < destination.Length; bone++)
        {
            var body = _boneToBody[bone]; var parent = _parents[bone]; Transform3D local;
            if (body >= 0)
            {
                _components[bone] = inverse * _bodies[body].GlobalTransform * _massLocal[body].AffineInverse();
                local = parent < 0 ? _components[bone] : _components[parent].AffineInverse() * _components[bone];
            }
            else
            {
                local = _seedLocal[bone]; _components[bone] = parent < 0 ? local : _components[parent] * local;
            }
            destination[bone] = Pose(local);
        }
    }

    internal static Transform3D NativeToFbx(AlsPrecisePose p) => new(
        new Basis(new Quaternion((float)-p.Rotation.X,(float)p.Rotation.Y,(float)-p.Rotation.Z,(float)p.Rotation.W).Normalized())
            .ScaledLocal(Vec(p.Scale)), FbxPosition(p.Position));
    internal static Transform3D Local(AlsLocalPose p) => new(new Basis(new Quaternion(p.Rotation.X,p.Rotation.Y,p.Rotation.Z,p.Rotation.W)).ScaledLocal(new(p.Scale.X,p.Scale.Y,p.Scale.Z)),new(p.Position.X,p.Position.Y,p.Position.Z));
    internal static AlsLocalPose Pose(Transform3D t)
    { var q = t.Basis.Orthonormalized().GetRotationQuaternion(); return new(new(t.Origin.X,t.Origin.Y,t.Origin.Z),new(q.X,q.Y,q.Z,q.W),System.Numerics.Vector3.One); }
    private static Vector3 FbxPosition(AlsDoubleVector p) => new((float)(p.X*.01),(float)(-p.Y*.01),(float)(p.Z*.01));
    private static Vector3 Vec(AlsDoubleVector p) => new((float)p.X,(float)p.Y,(float)p.Z);
    private static void Rigid(Transform3D t)
    {
        if (!t.IsFinite() || !t.Basis.Scale.IsEqualApprox(Vector3.One) || MathF.Abs(t.Basis.Determinant()-1) > .001f)
            throw new ArgumentException("Physics pose must be a finite, rigid transform.");
    }
    private void Check() { Main(); ObjectDisposedException.ThrowIf(_disposed,this); }
    private static void Main() { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Physics scene access requires Main."); }
    public void Dispose()
    {
        Main(); if (_disposed) return; _disposed = true; _active = false; PoseIdentity = default;
        if (GodotObject.IsInstanceValid(_root)) _root.Free();
    }
}
