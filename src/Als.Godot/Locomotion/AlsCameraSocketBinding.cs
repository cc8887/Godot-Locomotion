using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Runtime;
using GodotAls.Physics;

namespace GodotAls.Locomotion;

internal readonly record struct AlsCameraSocketLocations(AlsDoubleVector FirstPivot, AlsDoubleVector SecondPivot,
    AlsDoubleVector FirstPerson, AlsDoubleVector LeftShoulder, AlsDoubleVector RightShoulder);

// Bind to this exact visual skeleton generation, after the host has established
// its worker is idle. Sample only after committed animation/physical presentation.
internal sealed class AlsCameraSocketBinding
{
    private readonly Skeleton3D _skeleton;
    private readonly (int Bone, Transform3D Local)[] _bindings;
    private readonly string[] _boneNames;
    internal AlsCameraSocketBinding(Skeleton3D skeleton, AlsCameraRigDefinition rig,
        IReadOnlyDictionary<string, AlsCameraSocket> sockets)
    {
        Main(); _skeleton = skeleton;
        _boneNames = Enumerable.Range(0, skeleton.GetBoneCount()).Select(i => skeleton.GetBoneName(i).ToString()).ToArray();
        var bones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _boneNames.Length; i++)
            if (!bones.TryAdd(_boneNames[i], i)) throw new InvalidOperationException("Ambiguous visual skeleton.");
        _bindings = new[] { rig.FirstPivotSocket, rig.SecondPivotSocket, rig.FirstPersonSocket, rig.LeftShoulderSocket, rig.RightShoulderSocket }
            .Select(name =>
            {
                var isSocket = sockets.TryGetValue(name, out var socket); var bone = isSocket ? socket!.Bone : name;
                if (!bones.TryGetValue(bone, out var index)) throw new InvalidOperationException("Missing camera socket bone: " + bone);
                // Imported FBX BONE axes are (X,-Y,Z), not the scene's (Y,Z,-X).
                return (index, isSocket ? AlsPhysicsBodySet.NativeToFbx(socket!.Local) : Transform3D.Identity);
            }).ToArray();
    }
    internal AlsCameraSocketLocations Sample()
    {
        Main();
        if (!GodotObject.IsInstanceValid(_skeleton) || !_skeleton.IsInsideTree() || _skeleton.GetBoneCount() != _boneNames.Length)
            throw new InvalidOperationException("Camera visual skeleton expired or changed.");
        for (var i = 0; i < _boneNames.Length; i++)
            if (_skeleton.GetBoneName(i).ToString() != _boneNames[i]) throw new InvalidOperationException("Camera skeleton binding changed.");
        return new(Position(0), Position(1), Position(2), Position(3), Position(4));
    }
    private AlsDoubleVector Position(int slot)
    {
        var binding = _bindings[slot];
        var transform = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(binding.Bone) * binding.Local;
        if (!transform.IsFinite()) throw new InvalidOperationException("Nonfinite camera socket pose.");
        var p = transform.Origin;
        return new(-(double)p.Z * 100, (double)p.X * 100, (double)p.Y * 100);
    }
    private static void Main()
    { if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Camera sockets belong to Main."); }
}
