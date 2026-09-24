using Godot;
using GodotAls.Assets;
using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using GodotAls.Import.Runtime;
using GodotAls.Physics;

namespace GodotAls.Locomotion;

public partial class CameraMeshScaleSmoke : Node3D
{
    private int _ticks;
    public override void _Ready()
    {
        var wall = new StaticBody3D { Position = new(0, 0, -2), CollisionLayer = 1, CollisionMask = 0 };
        wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, 10, .2f) } });
        AddChild(wall);
    }
    public override void _PhysicsProcess(double delta)
    {
        if (++_ticks < 3) return;
        SetPhysicsProcess(false);
        try
        {
            var rig = AlsCameraRigDefinition.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_inputs.json"));
            var sockets = AlsCameraSocketCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_sockets.json"));
            var assets = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            using var probe = new AlsCameraCollisionProbe(this, 1, []);
            var poses = 0; var rejected = 0; var maximum = 0d;
            foreach (var name in new[] { "Mannequin", "AnimMan" })
            {
                var asset = assets.SkeletalMeshes.Single(m => m.ObjectPath == AlsPhysicsAssetCompiler.MeshRoot + name + "." + name);
                var model = ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot + "/" + asset.ResourcePath).Instantiate<Node3D>();
                AddChild(model);
                try
                {
                    var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!;
                    var binding = new AlsCameraSocketBinding(skeleton, rig, sockets);
                    foreach (var yaw in new[] { 0f, .7f, -1.4f })
                    {
                        // Pose in native component coordinates, then the FBX axis
                        // conversion, then scale in the mesh's own bone axes.
                        var native = new AlsPrecisePose(new(1000, 2000, 300),
                            AlsCameraMath.Quaternion(new(11, yaw * 180 / Math.PI, -8)), AlsDoubleVector.One);
                        var unit = AlsCorePhysicsPose.ToWorld(native);
                        skeleton.GlobalTransform = unit;
                        var unitSockets = binding.Sample();
                        foreach (var scale in new[] { Vector3.One * .5f, Vector3.One, Vector3.One * 2, new Vector3(1.2f, .7f, 1.6f) })
                        {
                            skeleton.GlobalTransform = new(unit.Basis * Basis.FromScale(scale), unit.Origin);
                            var mesh = AlsCameraMeshPose.FromWorld(skeleton.GlobalTransform);
                            Require(Math.Abs(mesh.Scale - scale.Z) < 1e-6, "Mesh used a world axis or maximum scale instead of native Z.");
                            var direction = new AlsDoubleVector(12, -7, 31);
                            Compare(direction.Rotate(mesh.Rotation), direction.Rotate(native.Rotation), .0001);
                            var sampled = binding.Sample();
                            if (scale.X == scale.Z && scale.Y == scale.Z)
                            {
                                Compare(sampled.FirstPerson, native.Position + (unitSockets.FirstPerson - native.Position) * scale.Z, .001);
                                Compare(sampled.RightShoulder, native.Position + (unitSockets.RightShoulder - native.Position) * scale.Z, .001);
                            }
                            var curves = new AlsCameraFollowCurves(new(10,-5,12),new(-300,40,25),0,0,0,0,0,0);
                            var input = new AlsCameraFollowInput(1f/60,false,default,sampled.FirstPivot,sampled.SecondPivot,
                                sampled.FirstPerson,sampled.RightShoulder,false,default,mesh.Rotation,mesh.Scale,
                                0,"",false,default,AlsQuaternion.Identity,false,90,0);
                            var candidate = AlsCameraFollow.Step(AlsCameraFollowState.Initial,input,rig.Follow,curves, request =>
                            {
                                Require(Math.Abs(request.Radius - rig.Follow.TraceRadius * scale.Z) < 1e-5, "Unscaled trace radius.");
                                return new(request.Start,request.End);
                            });
                            var target = (sampled.FirstPivot + sampled.SecondPivot) * .5;
                            Compare(candidate.Location, target + (curves.PivotOffset * scale.Z).Rotate(native.Rotation) + curves.CameraOffset * scale.Z, .001);
                            // Real geometry: overlap 5 cm*scale with the wall face,
                            // recover to radius + the native 1 cm*scale clearance.
                            var start = new Vector3(0,0,-1.9f + .10f * scale.Z);
                            var hit = probe.Query(new(AlsCameraCollisionProbe.Native(start),default,15 * mesh.Scale),default,mesh.Scale);
                            Require(probe.StartedPenetrating && probe.AdjustedStart, "Scaled overlap was not recovered.");
                            Compare(hit.Start,new(190 - 16 * scale.Z,0,0),.03);
                            poses++;
                        }
                    }
                }
                finally { model.Free(); }
            }
            foreach (var basis in new[] { Basis.FromScale(new(1,1,0)), Basis.FromScale(new(-1,1,1)),
                new Basis(Vector3.Right, new(.2f,1,0), Vector3.Back), Basis.FromScale(new(float.NaN,1,1)) })
            {
                try { AlsCameraMeshPose.FromWorld(new(basis,Vector3.Zero)); throw new InvalidOperationException("Invalid camera transform accepted."); }
                catch (ArgumentException) { rejected++; }
            }
            GD.Print($"ALS_CAMERA_MESH_SCALE_OK models=2 poses={poses} rejected={rejected} max_cm={maximum:R}");
            GetTree().Quit();
            void Compare(AlsDoubleVector actual, AlsDoubleVector expected, double tolerance)
            {
                var error = Math.Sqrt((actual-expected).LengthSquared); maximum = Math.Max(maximum,error);
                Require(error <= tolerance,$"Camera scale mismatch: {error:R} > {tolerance:R}");
            }
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
}
