using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using GodotAls.Import.Runtime;
using GodotAls.Physics;

namespace GodotAls.Locomotion;

public partial class CameraSocketSmoke : Node3D
{
    public override void _Ready()
    {
        try
        {
            var rig = AlsCameraRigDefinition.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_inputs.json"));
            var sockets = AlsCameraSocketCompiler.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_sockets.json"));
            var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
            var count = 0; var rejected = 0; var maximum = 0d;
            foreach (var name in new[] { "Mannequin", "AnimMan" })
            {
                var asset = set.SkeletalMeshes.Single(m => m.ObjectPath == AlsPhysicsAssetCompiler.MeshRoot + name + "." + name);
                var model = ResourceLoader.Load<PackedScene>(AlsGodotImportCoordinator.AssetRoot + "/" + asset.ResourcePath).Instantiate<Node3D>();
                AddChild(model);
                try
                {
                    var skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(model)!;
                    var binding = new AlsCameraSocketBinding(skeleton, rig, sockets);
                    var head = Enumerable.Range(0, skeleton.GetBoneCount()).Single(i => skeleton.GetBoneName(i).ToString().Equals("head", StringComparison.OrdinalIgnoreCase));
                    var root = Enumerable.Range(0, skeleton.GetBoneCount()).Single(i => skeleton.GetBoneName(i).ToString().Equals("root", StringComparison.OrdinalIgnoreCase));
                    var restRotation = skeleton.GetBonePoseRotation(head);
                    for (var variant = 0; variant < 4; variant++)
                    {
                        model.GlobalTransform = new(Basis.FromEuler(new(.1f * variant, .7f * variant, -.2f * variant)), new(3 * variant, 2 + variant, -variant));
                        skeleton.SetBonePoseRotation(head, restRotation * new Quaternion(Vector3.Right, variant * .3f));
                        var actual = binding.Sample();
                        var nativeHead = AlsCorePhysicsPose.FromWorld(skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(head));
                        var nativeRoot = AlsCorePhysicsPose.FromWorld(skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(root));
                        Compare(nativeRoot.Position, actual.FirstPivot); Compare(nativeHead.Position, actual.SecondPivot);
                        Compare(Expected(rig.FirstPersonSocket), actual.FirstPerson);
                        Compare(Expected(rig.LeftShoulderSocket), actual.LeftShoulder);
                        Compare(Expected(rig.RightShoulderSocket), actual.RightShoulder);
                        AlsDoubleVector Expected(string socket) => nativeHead.Position + sockets[socket].Local.Position.Rotate(nativeHead.Rotation);
                    }
                    var blocked = System.Threading.Tasks.Task.Run(() =>
                    { try { binding.Sample(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
                    if (!blocked) throw new InvalidOperationException("Worker read camera skeleton."); rejected++;
                    var oldName = skeleton.GetBoneName(head); skeleton.SetBoneName(head, "changed_head");
                    try { binding.Sample(); throw new Exception("Replaced skeleton binding accepted."); }
                    catch (InvalidOperationException) { rejected++; }
                    skeleton.SetBoneName(head, oldName);
                    void Compare(AlsDoubleVector expected, AlsDoubleVector actual)
                    {
                        var error = System.Math.Sqrt((expected - actual).LengthSquared); maximum = System.Math.Max(maximum, error); count++;
                        if (error > .0002) throw new InvalidOperationException($"Socket world differs: {name} error={error:R} cm");
                    }
                }
                finally { model.Free(); }
            }
            GD.Print($"ALS_CAMERA_SOCKET_SMOKE_OK models=2 poses=8 positions={count} rejected={rejected} max_cm={maximum:R}");
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
}
