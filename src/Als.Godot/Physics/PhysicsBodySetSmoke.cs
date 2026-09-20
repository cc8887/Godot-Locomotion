using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Actual engine body/mass/shape and pose-handoff checks; no joint or gameplay
// Ragdoll certification. Collision-free flight isolates these required boundaries.
public partial class PhysicsBodySetSmoke : Node3D
{
    private sealed record Case(AlsRagdollPhysicsDefinition Definition, Node3D Model, Skeleton3D Skeleton,
        AlsPhysicsBodySet Bodies, AlsLocalPose[] Reference, AlsLocalPose[] Snapshot, Transform3D World, Vector3[] Initial,
        Vector3[] Paused, Vector3[] Velocities);
    private readonly List<Case> _cases = [];
    private int _frame, _hz = 60, _contactSeconds = 4; private bool _done, _contact;
    private float _geometryError, _poseError, _tensorError;
    public override void _Ready()
    {
        try
        {
            _hz = int.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
            Require(_hz is 30 or 60 or 120,"Expected 30/60/120 Hz."); Engine.PhysicsTicksPerSecond = _hz;
            _contact = OS.GetCmdlineUserArgs().Contains("--contact");
            _contactSeconds = int.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--seconds="))?[10..] ?? "4");
            Require(_contactSeconds >= 4,"Contact observation must include at least four seconds.");
            var iterations = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--iterations="));
            if (iterations is not null) PhysicsServer3D.SpaceSetParam(GetWorld3D().Space,PhysicsServer3D.SpaceParameter.SolverIterations,int.Parse(iterations[13..]));
            var floorMode = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--floor="))?[8..];
            GD.Print($"PHYSICS_BODY_BACKEND {ProjectSettings.GetSetting("physics/3d/physics_engine","default")} penetration={PhysicsServer3D.SpaceGetParam(GetWorld3D().Space,PhysicsServer3D.SpaceParameter.ContactMaxAllowedPenetration)}");
            if (_contact)
            {
                var floor = new StaticBody3D { CollisionLayer = 1, CollisionMask = 2, Position = new(0,-.1f,0) };
                floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100,.2f,100) } }); AddChild(floor);
                if (floorMode == "plane") { floor.Position=Vector3.Zero; ((CollisionShape3D)floor.GetChild(0)).Shape=new WorldBoundaryShape3D { Plane=new Plane(Vector3.Up,0) }; }
                if (floorMode == "small") { floor.Position=new(4,-.1f,0); ((CollisionShape3D)floor.GetChild(0)).Shape=new BoxShape3D { Size=new(12,.2f,6) }; }
            }
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var json = Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json");
            foreach (var name in new[] {"Mannequin","AnimMan"})
            {
                var definition = AlsPhysicsAssetCompiler.Compile(json,AlsPhysicsAssetCompiler.MeshRoot+name+"."+name);
                var asset = set.SkeletalMeshes.Single(m => m.ObjectPath == definition.Mesh);
                var model = ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot+"/"+asset.ResourcePath).Instantiate<Node3D>(); AddChild(model);
                model.GlobalTransform = new(new Basis(Vector3.Up,.37f),new(_cases.Count*5,3,0));
                var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!;
                Require(skeleton is not null,"No imported physical skeleton.");
                var names = Enumerable.Range(0,skeleton!.GetBoneCount()).Select(skeleton.GetBoneName).Select(n => n.ToString()).ToArray();
                var parents = Enumerable.Range(0,names.Length).Select(skeleton.GetBoneParent).ToArray();
                var reference = Enumerable.Range(0,names.Length).Select(i => AlsPhysicsBodySet.Pose(skeleton.GetBoneRest(i))).ToArray();
                var bodies = new AlsPhysicsBodySet(this,definition,names,parents,7,1,collisionMask:_contact ? 1u : 0u);
                if (_contact) for (var i=0; i<bodies.BodyCount; i++) bodies.BodyAt(i).MaxContactsReported=8;
                var diagnostic = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--diagnostic="))?[13..];
                for (var i=0; i<bodies.BodyCount; i++)
                {
                    if (diagnostic == "damping") bodies.BodyAt(i).AngularDamp=0;
                    if (diagnostic == "bounce") bodies.BodyAt(i).PhysicsMaterialOverride!.Bounce=0;
                    if (diagnostic == "friction") bodies.BodyAt(i).PhysicsMaterialOverride!.Friction=0;
                    if (diagnostic == "convex") foreach (var node in bodies.BodyAt(i).GetChildren().OfType<CollisionShape3D>())
                        if (node.Shape is BoxShape3D box) node.Shape = new ConvexPolygonShape3D { Margin=0, Points=Enumerable.Range(0,8).Select(k => box.Size * new Vector3((k&1)==0 ? -.5f:.5f,(k&2)==0 ? -.5f:.5f,(k&4)==0 ? -.5f:.5f)).ToArray() };
                }
                var world = skeleton.GlobalTransform;
                bodies.Seed(new(1,7,1),world,reference,new(2,0,0),Vector3.Zero);
                var initial = Enumerable.Range(0,bodies.BodyCount).Select(i => bodies.BodyAt(i).GlobalPosition).ToArray();
                var item = new Case(definition,model,skeleton,bodies,reference,new AlsLocalPose[names.Length],world,initial,
                    new Vector3[bodies.BodyCount],new Vector3[bodies.BodyCount]); _cases.Add(item);
                VerifyGeometry(item); VerifySnapshot(item,true);
                var id = bodies.PoseIdentity;
                Reject(() => bodies.Seed(new(2,7,2),world,reference,Vector3.Zero,Vector3.Zero));
                var invalid = reference.ToArray(); invalid[^1] = invalid[^1] with { Position = new(float.NaN,0,0) };
                Reject(() => bodies.Seed(new(2,7,1),world,invalid,Vector3.Zero,Vector3.Zero));
                invalid[^1] = reference[^1] with { Rotation = default };
                Reject(() => bodies.Seed(new(2,7,1),world,invalid,Vector3.Zero,Vector3.Zero));
                Require(bodies.PoseIdentity == id && bodies.BodyAt(0).GlobalPosition == initial[0],"Invalid pose changed the physical owner.");
                var angular = new Vector3(.3f,.7f,-.2f); var linear = new Vector3(2,0,0);
                bodies.Seed(new(2,7,1),world,reference,linear,angular); bodies.Start();
                for (var i=0; i<bodies.BodyCount; i++) if (definition.Bodies[i].PhysicsType != 1)
                {
                    var body = bodies.BodyAt(i);
                    Require(body.LinearVelocity.DistanceTo(linear+angular.Cross(body.GlobalPosition-world.Origin)) < 1e-5f &&
                        body.AngularVelocity.DistanceTo(angular) < 1e-5f,"Handoff lost tangential or angular velocity.");
                }
                bodies.Suspend(); bodies.Seed(new(3,7,1),world,reference,linear,Vector3.Zero);
                bodies.Start(); Reject(bodies.Start);
            }
        }
        catch (Exception e) { Fail(e); }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) return;
        try
        {
            _frame++;
            foreach (var c in _cases)
            {
                if (_frame == 3) VerifyMass(c);
                if (_frame == _hz/2)
                {
                    for (var i = 0; i < c.Bodies.BodyCount; i++)
                    {
                        var body = c.Bodies.BodyAt(i);
                        if (c.Definition.Bodies[i].PhysicsType == 1) Require(body.GlobalPosition.DistanceTo(c.Initial[i]) < 1e-5f,"Kinematic root fell.");
                        else Require(body.GlobalPosition.Y < c.Initial[i].Y-.5f && body.GlobalPosition.X > c.Initial[i].X+.7f,"Native bodies did not simulate with inherited velocity.");
                        c.Paused[i] = body.GlobalPosition; c.Velocities[i] = body.LinearVelocity;
                    }
                    VerifySnapshot(c,false); c.Bodies.Suspend();
                    // Parent/capsule motion cannot move detached physical bodies.
                    Position += new Vector3(.3f,0,0);
                }
                if (_frame > _hz/2 && _frame < _hz)
                    for (var i = 0; i < c.Bodies.BodyCount; i++) Require(c.Bodies.BodyAt(i).GlobalPosition.DistanceTo(c.Paused[i]) < 1e-5f,"Suspended body kept moving or followed parent.");
                if (_frame == _hz) c.Bodies.Resume();
                if (_frame == _hz+3)
                    for (var i = 0; i < c.Bodies.BodyCount; i++) if (c.Definition.Bodies[i].PhysicsType != 1)
                        Require(c.Bodies.BodyAt(i).LinearVelocity.X > c.Velocities[i].X-.05f &&
                            c.Bodies.BodyAt(i).GlobalPosition.Y < c.Paused[i].Y,"Resume lost physical velocity.");
                if (_frame == (_contact ? _hz*_contactSeconds : _hz*3/2))
                {
                    if (_contact) VerifyContact(c);
                    VerifySnapshot(c,false); c.Bodies.Stop(); Reject(c.Bodies.Resume);
                    Require(c.Bodies.PoseIdentity == default && !c.Bodies.Active,"Stopped physical ownership survived.");
                    c.Bodies.Seed(new(99,7,1),c.World,c.Reference,Vector3.Zero,Vector3.Zero); VerifySnapshot(c,true);
                    c.Bodies.Dispose(); c.Bodies.Dispose(); Reject(c.Bodies.Start);
                }
            }
            if (_frame == (_contact ? _hz*_contactSeconds : _hz*3/2))
            {
                GD.Print($"PHYSICS_BODY_SET_OK hz={_hz} contact={_contact} bodies=40 shapes=43 meshes=2 geometry_error_m={_geometryError:G9} pose_error_m={_poseError:G9} tensor_relative_error={_tensorError:G9} lifecycle=seed_flight_suspend_resume_stop_dispose joints=not_yet_bound");
                _done = true; GetTree().Quit();
            }
        }
        catch (Exception e) { Fail(e); }
    }

    private void VerifyMass(Case c)
    {
        for (var i = 0; i < c.Bodies.BodyCount; i++)
        {
            var source = c.Definition.Bodies[i]; var body = c.Bodies.BodyAt(i);
            var state = PhysicsServer3D.BodyGetDirectState(body.GetRid()); Require(state is not null,"No actual physics body state.");
            if (source.PhysicsType == 1) continue;
            Require(MathF.Abs(1/state!.InverseMass-(float)source.MassKg) < 1e-4f,"Backend mass differs from native mass.");
            Require(state.CenterOfMassLocal.Length() < 1e-5f,"Backend center of mass shifted away from exported principal frame.");
            var inertia = source.InertiaKgCm2 * .0001;
            var inverse = new Vector3((float)(1/inertia.X),(float)(1/inertia.Y),(float)(1/inertia.Z));
            var axes = state.Transform.Basis.Orthonormalized();
            var expected = axes * new Basis(new(inverse.X,0,0),new(0,inverse.Y,0),new(0,0,inverse.Z)) * axes.Transposed();
            for (var axis = 0; axis < 3; axis++)
            {
                var error = (expected[axis]-state.InverseInertiaTensor[axis]).Length()/MathF.Max(expected[axis].Length(),1);
                _tensorError = MathF.Max(_tensorError,error); Require(error < 1e-4f,"Backend world inertia tensor lost mass-axis rotation.");
            }
        }
    }

    private static void VerifyContact(Case c)
    {
        for (var i = 0; i < c.Bodies.BodyCount; i++)
        {
            if (c.Definition.Bodies[i].PhysicsType == 1) continue;
            var body = c.Bodies.BodyAt(i); var bottom = float.PositiveInfinity;
            foreach (var node in body.GetChildren().OfType<CollisionShape3D>())
            {
                var t = node.GlobalTransform; float y;
                switch (node.Shape)
                {
                    case SphereShape3D sphere: y = t.Origin.Y-sphere.Radius; break;
                    case BoxShape3D box: y = t.Origin.Y-(MathF.Abs(t.Basis.X.Y)*box.Size.X+MathF.Abs(t.Basis.Y.Y)*box.Size.Y+MathF.Abs(t.Basis.Z.Y)*box.Size.Z)/2; break;
                    case CapsuleShape3D capsule: y = t.Origin.Y-MathF.Abs(t.Basis.Y.Y)*(capsule.Height/2-capsule.Radius)-capsule.Radius; break;
                    case ConvexPolygonShape3D convex: y = convex.Points.Min(p => (t*p).Y); break;
                    default: throw new InvalidOperationException("Unknown contact shape.");
                }
                bottom = MathF.Min(bottom,y);
            }
            if (bottom is <= -.02f or >= .03f || body.LinearVelocity.Length() >= .2f)
            {
                var state = PhysicsServer3D.BodyGetDirectState(body.GetRid());
                GD.Print($"CONTACT_DETAIL bone={body.Name} bottom={bottom} velocity={body.LinearVelocity} sleeping={body.Sleeping} origin={body.GlobalPosition} contacts={state.GetContactCount()} ccd={body.ContinuousCd}");
                for (var k=0;k<state.GetContactCount();k++) GD.Print($"CONTACT_POINT local={state.GetContactLocalPosition(k)} normal={state.GetContactLocalNormal(k)} collider={state.GetContactColliderPosition(k)}");
            }
            Require(bottom is > -.02f and < .03f,$"Body did not settle against actual floor geometry: {body.Name} bottom={bottom}.");
            Require(body.LinearVelocity.Length() < .2f,$"Body did not settle: {body.Name} speed={body.LinearVelocity.Length()}.");
        }
    }

    private void VerifySnapshot(Case c,bool reference)
    {
        c.Bodies.CaptureLocalPose(c.World,c.Snapshot);
        var components = new Transform3D[c.Snapshot.Length];
        for (var i = 0; i < components.Length; i++)
        {
            var parent = c.Skeleton.GetBoneParent(i); var local = AlsPhysicsBodySet.Local(c.Snapshot[i]);
            components[i] = parent < 0 ? local : components[parent]*local;
            if (reference)
            { var error = local.Origin.DistanceTo(AlsPhysicsBodySet.Local(c.Reference[i]).Origin); _poseError = MathF.Max(_poseError,error); Require(error < .0001f,"Physics handoff changed reference local position."); }
        }
        for (var i = 0; i < c.Bodies.BodyCount; i++)
        {
            var bone = c.Skeleton.FindBone(c.Definition.Bodies[i].Bone);
            if (bone < 0) bone = Enumerable.Range(0,components.Length).Single(b => c.Skeleton.GetBoneName(b).ToString().Equals(c.Definition.Bodies[i].Bone,StringComparison.OrdinalIgnoreCase));
            var reconstructed = c.World*components[bone]*c.Bodies.BoneToMass(i);
            var actual = c.Bodies.BodyAt(i).GlobalTransform;
            Require(reconstructed.Origin.DistanceTo(actual.Origin) < .0001f &&
                MathF.Abs(reconstructed.Basis.GetRotationQuaternion().Dot(actual.Basis.GetRotationQuaternion())) > .99999f,"Snapshot did not reconstruct the simulated bone.");
        }
    }

    private void VerifyGeometry(Case c)
    {
        for (var i = 0; i < c.Bodies.BodyCount; i++)
        {
            var body = c.Definition.Bodies[i]; var nodes = c.Bodies.BodyAt(i).GetChildren().OfType<CollisionShape3D>().ToArray();
            Require(nodes.Length == body.Shapes.Length,"Dropped an authored collision primitive.");
            var expectedExceptions = c.Definition.DisabledCollisions.Where(p => p.A == i || p.B == i).Select(p => (PhysicsBody3D)c.Bodies.BodyAt(p.A == i ? p.B : p.A)).ToHashSet();
            Require(expectedExceptions.SetEquals(c.Bodies.BodyAt(i).GetCollisionExceptions()),"Collision exclusion pairs differ from the native asset.");
            for (var s = 0; s < nodes.Length; s++)
            {
                var source = body.Shapes[s]; var node = nodes[s];
                switch (source.Type)
                {
                    case "sphere": Compare(new(source.RadiusCm,0,0),new((float)(source.RadiusCm*.01),0,0)); break;
                    case "box":
                        for (var corner = 0; corner < 8; corner++)
                        { var p = source.SizeCm * new AlsDoubleVector((corner&1)==0 ? -.5:.5,(corner&2)==0 ? -.5:.5,(corner&4)==0 ? -.5:.5); Compare(p,new((float)(p.X*.01),(float)(-p.Y*.01),(float)(p.Z*.01))); } break;
                    case "capsule":
                        var tip = source.CylinderLengthCm/2+source.RadiusCm;
                        Compare(new(0,0,tip),new(0,(float)(tip*.01),0)); Compare(new(0,0,-tip),new(0,(float)(-tip*.01),0)); break;
                    case "convex":
                        var points = ((ConvexPolygonShape3D)node.Shape).Points;
                        for (var v = 0; v < points.Length; v++) Compare(source.VerticesCm[v],points[v]); break;
                }
                void Compare(AlsDoubleVector native,Vector3 local)
                {
                    // Independent double-precision native geometry -> bone -> component;
                    // compare actual Godot collision node, including principal-frame cancellation.
                    var inBone = (native*source.Local.Scale).Rotate(source.Local.Rotation)+source.Local.Position;
                    var inComponent = inBone.Rotate(body.ReferenceComponent.Rotation)+body.ReferenceComponent.Position;
                    var expected = c.World*new Vector3((float)(inComponent.X*.01),(float)(-inComponent.Y*.01),(float)(inComponent.Z*.01));
                    var error = expected.DistanceTo(node.GlobalTransform*local); _geometryError = MathF.Max(_geometryError,error);
                    Require(error < .0003f,$"Native collision geometry mismatch: {body.Bone}/{source.Type} error={error}.");
                }
            }
        }
    }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new InvalidOperationException("Invalid physical lifecycle was accepted."); }
    private static void Require(bool condition,string message) { if (!condition) throw new InvalidOperationException(message); }
    private void Fail(Exception e) { _done = true; foreach (var c in _cases) c.Bodies.Dispose(); GD.PushError(e.ToString()); GetTree().Quit(1); }
    public override void _ExitTree() { foreach (var c in _cases) c.Bodies.Dispose(); }
}
