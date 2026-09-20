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
    private int _cases, _bodies, _captures, _rejections;
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
                        _cases++; _bodies += definition.Bodies.Length;
                    }
                }
                finally { model.Free(); }
            }
            var result = new { cases = _cases, body_handoffs = _bodies, captures = _captures, rejected_inputs = _rejections,
                max_position_m = _positionError, max_basis_error = _basisError, max_velocity_mps = _velocityError,
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
