using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// Captured on the scene owner and immutable after publication. Bone order and
// local poses are copied so a physics/skeleton update cannot mutate a reader.
public sealed class AlsNamedPoseSnapshot
{
    private readonly string[] _bones;
    private readonly AlsLocalPose[] _poses;
    public AlsFrameIdentity Identity { get; }
    public string Name { get; }
    public string MeshName { get; }
    public bool IsValid { get; }
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<AlsLocalPose> LocalPoses => _poses;
    public AlsNamedPoseSnapshot(AlsFrameIdentity identity, string name, string meshName,
        ReadOnlySpan<string> bones, ReadOnlySpan<AlsLocalPose> poses, bool isValid = true)
    {
        if (identity.SlotGeneration == 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(meshName))
            throw new ArgumentException("Snapshot requires character/generation and named source mesh.");
        foreach (var bone in bones) if (string.IsNullOrWhiteSpace(bone)) throw new ArgumentException("Invalid snapshot bone name.");
        foreach (var pose in poses) new AlsPrecisePose(pose).Validate();
        Identity = identity; Name = name; MeshName = meshName; IsValid = isValid;
        _bones = bones.ToArray(); _poses = poses.ToArray();
    }
}

// FAnimNode_PoseSnapshot's NamedSnapshot evaluation. The enclosing graph owns
// publication/retirement; this component does not capture or simulate physics.
public sealed class AlsNamedPoseSnapshotRuntime
{
    private readonly string _name, _targetMesh;
    private readonly uint _character, _generation;
    private readonly string[] _targetBones;
    private readonly int[] _logicalToMesh, _sourceMapping;
    private readonly AlsLocalPose[] _reference;
    private string? _mappedSourceMesh;
    public AlsNamedPoseSnapshotRuntime(string name, string targetMesh, uint character, uint generation,
        ReadOnlySpan<string> targetMeshBones, ReadOnlySpan<int> logicalToMesh, ReadOnlySpan<AlsLocalPose> reference)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(targetMesh) || generation == 0 ||
            reference.IsEmpty || logicalToMesh.Length != reference.Length || targetMeshBones.IsEmpty)
            throw new ArgumentException("Incomplete snapshot target layout.");
        foreach (var bone in targetMeshBones) if (string.IsNullOrWhiteSpace(bone)) throw new ArgumentException("Invalid target bone name.");
        foreach (var index in logicalToMesh) if (index < -1 || index >= targetMeshBones.Length) throw new ArgumentException("Invalid mesh bone mapping.");
        foreach (var pose in reference) new AlsPrecisePose(pose).Validate();
        _name = name; _targetMesh = targetMesh; _character = character; _generation = generation;
        _targetBones = targetMeshBones.ToArray(); _logicalToMesh = logicalToMesh.ToArray(); _reference = reference.ToArray();
        _sourceMapping = new int[_targetBones.Length];
    }
    public void Evaluate(AlsFrameIdentity identity, AlsNamedPoseSnapshot? snapshot, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if (identity.CharacterId != _character || identity.SlotGeneration != _generation || pose.Length != _reference.Length)
            throw new ArgumentException("Foreign snapshot evaluation target.");
        if (snapshot is not null && (snapshot.Identity.CharacterId != _character || snapshot.Identity.SlotGeneration != _generation || snapshot.Identity.FrameId > identity.FrameId))
            throw new ArgumentException("Retired or future snapshot cannot feed this character.");
        _reference.CopyTo(pose); curves.Clear();
        if (snapshot is null || !snapshot.Name.Equals(_name, StringComparison.OrdinalIgnoreCase)) return;
        // UE GetPoseSnapshot searches by FName, and ApplyPose does not gate on
        // FPoseSnapshot.bIsValid. Preserve partial snapshots, including that flag.
        var sameMesh = snapshot.MeshName.Equals(_targetMesh, StringComparison.OrdinalIgnoreCase);
        if (!sameMesh && !snapshot.MeshName.Equals(_mappedSourceMesh, StringComparison.OrdinalIgnoreCase))
        {
            for (var target = 0; target < _targetBones.Length; target++)
            {
                _sourceMapping[target] = -1;
                for (var source = 0; source < snapshot.BoneNames.Length; source++)
                    if (_targetBones[target].Equals(snapshot.BoneNames[source], StringComparison.OrdinalIgnoreCase))
                    { _sourceMapping[target] = source; break; }
            }
            _mappedSourceMesh = snapshot.MeshName;
        }
        for (var logical = 0; logical < pose.Length; logical++)
        {
            var meshIndex = _logicalToMesh[logical];
            if (meshIndex < 0) continue;
            var source = sameMesh ? meshIndex : _sourceMapping[meshIndex];
            if ((uint)source < (uint)snapshot.LocalPoses.Length) pose[logical] = snapshot.LocalPoses[source];
        }
    }
}
