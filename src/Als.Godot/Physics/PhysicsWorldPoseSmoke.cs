using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

public partial class PhysicsWorldPoseSmoke : Node3D
{
    private int _cases, _bodies, _captures, _rejections, _nativeVelocityHandoffs;
    private float _positionError, _basisError, _velocityError;
    public override void _Ready()
    {
        try
        {
            var report = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--report="))?[9..] ?? "";
            Require(System.IO.Path.IsPathFullyQualified(report) && !System.IO.File.Exists(report), "Require a new absolute report path.");
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            foreach (var name in new[] { "Mannequin", "AnimMan" })
            {
                var definition = AlsPhysicsAssetCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), AlsPhysicsAssetCompiler.MeshRoot + name + "." + name);
                var asset = set.SkeletalMeshes.Single(m => m.ObjectPath == definition.Mesh);
                var model = ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot + "/" + asset.ResourcePath).Instantiate<Node3D>(); AddChild(model);
                try
                {
                    var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!;
                    var names = Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => skeleton.GetBoneName(i).ToString()).ToArray();
                    var parents = Enumerable.Range(0, names.Length).Select(skeleton.GetBoneParent).ToArray();
                    var rest = Enumerable.Range(0, names.Length).Select(i => AlsPhysicsBodySet.Pose(skeleton.GetBoneRest(i))).ToArray();
                    var bridge = new AlsCorePhysicsPose(definition, names, parents, 7, 3);
                    using var proxies = new AlsPhysicsBodySet(this, definition, names, parents, 7, 3, collisionLayer: 0, collisionMask: 0);
                    var mapping = definition.Bind(names);
                    var physical = mapping.ToHashSet();
                    var snapshot = new AlsLocalPose[names.Length]; var independent = new AlsLocalPose[names.Length];
                    var components = new Transform3D[names.Length];
                    for (var variant = 0; variant < 4; variant++)
                    {
                        model.GlobalTransform = new(Basis.FromEuler(new(.17f * variant, .7f * variant, -.11f * variant)), new(4 * variant, 2 + variant, -3 * variant));
                        var world = skeleton.GlobalTransform; var pose = rest.ToArray();
                        if (variant > 0)
                        {
                            foreach (var boneName in new[] { "spine_02", "upperarm_l", "thigh_r" })
                            {
                                var index = Array.IndexOf(names, boneName);
                                var p = pose[index]; pose[index] = p with { Rotation = System.Numerics.Quaternion.Normalize(
                                    System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, .15f * variant) * p.Rotation) };
                            }
                        }
                        var velocity = new Vector3(2, -.4f, .7f); var angular = new Vector3(.3f, .7f, -.2f);
                        var states = new AlsIslandBodyState[definition.Bodies.Length + 1];
                        var sentinel = new AlsIslandBodyState(AlsPrecisePose.Identity with { Position = new(990, 80, 70) }, default); states[^1] = sentinel;
                        bridge.Seed(new(variant + 1, 7, 3), world, pose, velocity, angular, states);
                        Require(states[^1] == sentinel, "Pose handoff overwrote environment suffix.");
                        proxies.Seed(new(variant + 1, 7, 3), world, pose, velocity, angular);
                        for (var i = 0; i < definition.Bodies.Length; i++)
                        {
                            var expected = proxies.BodyAt(i).GlobalTransform;
                            Compare(expected, AlsCorePhysicsPose.ToWorld(AlsPrecisePose.Compose(definition.Bodies[i].MassLocal, states[i].Actor)));
                            if (definition.Bodies[i].PhysicsType != 1)
                            {
                                _velocityError = Math.Max(_velocityError, AlsCorePhysicsPose.LinearFromNative(states[i].Velocity.Linear).DistanceTo(
                                    velocity + angular.Cross(expected.Origin - world.Origin)));
                                Require(_velocityError < .0001f && AlsCorePhysicsPose.AngularFromNative(states[i].Velocity.Angular).DistanceTo(angular) < 1e-6f,
                                    "Handoff lost COM lever arm or axial-vector handedness.");
                            }
                        }
                        var bodies = definition.Bodies.Select(b => new AlsIslandBody(b.MassLocal,
                            b.PhysicsType == 1 ? default : new AlsJointInverseMass(1, AlsDoubleVector.One), GravityEnabled: false)).ToArray();
                        var island = new AlsJointIsland(bodies, [], states.AsSpan(0, bodies.Length));
                        var host = new AlsCoreJointHost(proxies, definition, island, worldSpace: true);
                        bridge.Capture(island, world, snapshot);
                        for (var bone = 0; bone < names.Length; bone++) Compare(AlsPhysicsBodySet.Local(pose[bone]), AlsPhysicsBodySet.Local(snapshot[bone]));
                        var saved = states.ToArray(); var identity = bridge.SeedIdentity;
                        var bad = pose.ToArray(); bad[^1] = bad[^1] with { Position = new(float.NaN, 0, 0) };
                        Reject(() => bridge.Seed(new(99, 7, 3), world, bad, velocity, angular, states));
                        Reject(() => bridge.Seed(new(99, 7, 4), world, pose, velocity, angular, states));
                        Reject(() => bridge.Seed(new(99, 7, 3), world, pose, new(float.MaxValue, 0, 0), Vector3.Zero, states));
                        Require(states.SequenceEqual(saved) && bridge.SeedIdentity == identity, "Invalid seed partially published.");
                        var worker = System.Threading.Tasks.Task.Run(() => { try { bridge.Capture(island, world, snapshot); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
                        Require(worker, "Worker accessed scene transport."); _rejections++;
                        for (var frame = 0; frame < 24; frame++)
                        {
                            host.Step(1d / 60);
                            // Capsule/component motion changes local representation,
                            // never the physical body's world pose.
                            var component = new Transform3D(new Basis(Vector3.Up, frame * .03f), new(frame * .01f, 0, -.02f * frame)) * world;
                            bridge.Capture(island, component, snapshot); proxies.CaptureLocalPose(component, independent);
                            for (var bone = 0; bone < names.Length; bone++)
                            {
                                var local = AlsPhysicsBodySet.Local(snapshot[bone]);
                                components[bone] = parents[bone] < 0 ? local : components[parents[bone]] * local;
                                Compare(local, AlsPhysicsBodySet.Local(independent[bone]));
                                if (!physical.Contains(bone)) Compare(local, AlsPhysicsBodySet.Local(pose[bone]));
                            }
                            for (var i = 0; i < definition.Bodies.Length; i++) Compare(component * components[mapping[i]], AlsCorePhysicsPose.ToWorld(island.BodyAt(i).Actor));
                            _captures++;
                        }
                        var inherited = Enumerable.Range(0, definition.Bodies.Length).Select(i =>
                            new AlsProjectionVelocity(new(600 + i, 40 - i, -80), new(.1f + i * .01f, .2f, -.3f))).ToArray();
                        bridge.SeedWithBodyVelocities(new(100 + variant, 7, 3), world, pose, inherited, states);
                        Require(states[^1] == sentinel, "Per-body velocity seed overwrote environment.");
                        for (var i = 0; i < inherited.Length; i++)
                        {
                            Require(states[i].Velocity == (definition.Bodies[i].PhysicsType == 1 ? default : inherited[i]),
                                "Inherited native velocity was converted or given an extra COM lever arm.");
                            Compare(AlsCorePhysicsPose.ToWorld(saved[i].Actor), AlsCorePhysicsPose.ToWorld(states[i].Actor));
                            _nativeVelocityHandoffs++;
                        }
                        var scaled = pose.ToArray(); var ikRoot = Array.IndexOf(names, "ik_foot_root");
                        Require(ikRoot >= 0 && !physical.Contains(ikRoot), "Missing nonphysical IK branch.");
                        scaled[ikRoot] = scaled[ikRoot] with { Scale = new(1.2f, .8f, 1) };
                        bridge.SeedWithBodyVelocities(new(150 + variant, 7, 3), world, scaled, inherited, states);
                        var scaledIsland = new AlsJointIsland(bodies, [], states.AsSpan(0, bodies.Length));
                        bridge.Capture(scaledIsland, world, snapshot);
                        Require(snapshot[ikRoot] == scaled[ikRoot], "Physics capture lost unrelated IK branch scale.");
                        var current = scaled.ToArray();
                        for (var bone = 0; bone < current.Length; bone++)
                            if (!physical.Contains(bone)) current[bone] = current[bone] with
                            { Position = current[bone].Position + new System.Numerics.Vector3(.013f, -.009f, .007f) };
                        bridge.Capture(scaledIsland, world, current, snapshot);
                        for (var bone = 0; bone < current.Length; bone++)
                        {
                            var local = AlsPhysicsBodySet.Local(snapshot[bone]);
                            components[bone] = parents[bone] < 0 ? local : components[parents[bone]] * local;
                            if (!physical.Contains(bone)) Require(snapshot[bone] == current[bone], "Current animation base was frozen at entry.");
                        }
                        for (var i = 0; i < bodies.Length; i++) Compare(world * components[mapping[i]], AlsCorePhysicsPose.ToWorld(scaledIsland.BodyAt(i).Actor));
                        var heldCapture = snapshot.ToArray();
                        Reject(() => bridge.Capture(scaledIsland, world, bad, snapshot));
                        Require(snapshot.SequenceEqual(heldCapture), "Invalid current animation partially published a capture.");
                        bridge.Capture(scaledIsland, world, snapshot);
                        Require(snapshot[ikRoot] == scaled[ikRoot], "Current animation capture changed the entry baseline.");
                        for (var i = 0; i < bodies.Length; i++) Compare(
                            AlsCorePhysicsPose.ToWorld(saved[i].Actor), AlsCorePhysicsPose.ToWorld(states[i].Actor));
                        var scaledRoot = pose.ToArray(); scaledRoot[0] = scaledRoot[0] with { Scale = new(1.2f, 1, 1) };
                        Reject(() => bridge.SeedWithBodyVelocities(new(199, 7, 3), world, scaledRoot, inherited, states));
                        // A nearly full-weight UE montage may omit its subthreshold
                        // source. Preserve its positions, but export a rigid actor.
                        var nearUnit = pose.Select(p => p with { Scale = p.Scale * .9999905f }).ToArray();
                        bridge.SeedWithBodyVelocities(new(199, 7, 3), world, nearUnit, inherited, states);
                        foreach (var state in states.Take(bodies.Length))
                            Require(Math.Abs(state.Actor.Rotation.LengthSquared - 1) < .00001,
                                "Near-unit animation did not produce a rigid body rotation.");
                        scaledRoot[0] = scaledRoot[0] with { Scale = new(1.00002f, 1, 1) };
                        Reject(() => bridge.SeedWithBodyVelocities(new(200, 7, 3), world, scaledRoot, inherited, states));
                        var held = states.ToArray(); var heldIdentity = bridge.SeedIdentity;
                        inherited[^1] = new(new(float.NaN, 0, 0), default);
                        Reject(() => bridge.SeedWithBodyVelocities(new(200 + variant, 7, 3), world, pose, inherited, states));
                        Require(states.SequenceEqual(held) && bridge.SeedIdentity == heldIdentity,
                            "Invalid last native velocity partially changed entry state.");
                        _cases++; _bodies += definition.Bodies.Length;
                    }
                }
                finally { model.Free(); }
            }
            var result = new { cases = _cases, body_handoffs = _bodies, captures = _captures, rejected_inputs = _rejections,
                max_position_m = _positionError, max_basis_error = _basisError, max_velocity_mps = _velocityError,
                native_velocity_handoffs = _nativeVelocityHandoffs,
                native_world_and_fbx_local = true, ordinary_ragdoll_connected = false };
            using var file = new System.IO.FileStream(report, FileMode.CreateNew);
            JsonSerializer.Serialize(file, result); GD.Print("CORE_WORLD_POSE_OK " + JsonSerializer.Serialize(result)); GetTree().Quit();
        }
        catch (Exception e) { GD.PushError("CORE_WORLD_POSE_FAILED " + e); GetTree().Quit(1); }
    }
    private void Compare(Transform3D a, Transform3D b)
    {
        _positionError = Math.Max(_positionError, a.Origin.DistanceTo(b.Origin));
        _basisError = Math.Max(_basisError, Math.Max((a.Basis.X - b.Basis.X).Length(), Math.Max((a.Basis.Y - b.Basis.Y).Length(), (a.Basis.Z - b.Basis.Z).Length())));
        Require(a.IsFinite() && b.IsFinite() && _positionError < .00005f && _basisError < .00005f,
            $"World/FBX pose mismatch p={_positionError:R} basis={_basisError:R}.");
    }
    private void Reject(Action action)
    { var rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Require(rejected, "Invalid seed accepted."); _rejections++; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
