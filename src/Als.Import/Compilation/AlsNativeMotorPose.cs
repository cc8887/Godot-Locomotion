using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

// Logical imported FBX locals -> asset-native UE locals. Virtual bones may be
// present in the input; every real native bone and its direct parent must bind.
// Single-owner scratch; a failed conversion never partially publishes output.
public sealed class AlsNativeMotorPose
{
    private readonly int[] _mapping;
    private readonly int _logicalCount;
    private readonly AlsPrecisePose[] _candidate;
    public AlsNativeMotorPose(AlsRagdollPhysicsDefinition definition, ReadOnlySpan<string> names, ReadOnlySpan<int> parents)
    {
        if (names.Length != parents.Length) throw new ArgumentException("Motor skeleton layout differs.");
        _logicalCount = names.Length;
        var indices = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        for (var i=0;i<names.Length;i++)
            if (string.IsNullOrWhiteSpace(names[i]) || !indices.TryAdd(names[i],i) || parents[i] < -1 || parents[i] >= i)
                throw new ArgumentException("Invalid logical motor skeleton.");
        _mapping = new int[definition.Bones.Length]; _candidate = new AlsPrecisePose[_mapping.Length];
        for (var i=0;i<_mapping.Length;i++)
            if (!indices.TryGetValue(definition.Bones[i].Name,out _mapping[i]))
                throw new ArgumentException("Missing native motor bone: " + definition.Bones[i].Name);
        for (var i=0;i<_mapping.Length;i++)
            if (parents[_mapping[i]] != (definition.Bones[i].Parent < 0 ? -1 : _mapping[definition.Bones[i].Parent]))
                throw new ArgumentException("Native motor parent chain differs.");
    }

    public void Convert(ReadOnlySpan<AlsLocalPose> logical, Span<AlsPrecisePose> native)
    {
        if (logical.Length != _logicalCount || native.Length != _mapping.Length)
            throw new ArgumentException("Motor pose layout differs.");
        for (var i=0;i<_mapping.Length;i++)
        {
            var p=logical[_mapping[i]];new AlsPrecisePose(p).Validate(); var q=p.Rotation;
            // USkeletalMeshComponent::FinalizePoseEvaluationResult normalizes
            // bone-space rotations before UpdateRBJointMotors reads them.
            var converted=new AlsPrecisePose(new((double)p.Position.X*100, -(double)p.Position.Y*100, (double)p.Position.Z*100),
                new AlsQuaternion(-q.X,q.Y,-q.Z,q.W).Normalized(), new(p.Scale));
            converted.Validate(); _candidate[i]=converted;
        }
        _candidate.AsSpan().CopyTo(native);
    }

    public void Convert(ReadOnlySpan<AlsPrecisePose> logical, Span<AlsPrecisePose> native)
    {
        if (logical.Length != _logicalCount || native.Length != _mapping.Length)
            throw new ArgumentException("Motor pose layout differs.");
        for (var i=0;i<_mapping.Length;i++)
        {
            var p=logical[_mapping[i]];p.Validate();var q=p.Rotation;
            var converted=new AlsPrecisePose(new(p.Position.X*100,-p.Position.Y*100,p.Position.Z*100),
                new AlsQuaternion(-q.X,q.Y,-q.Z,q.W).Normalized(),p.Scale);
            converted.Validate();_candidate[i]=converted;
        }
        _candidate.AsSpan().CopyTo(native);
    }
}
