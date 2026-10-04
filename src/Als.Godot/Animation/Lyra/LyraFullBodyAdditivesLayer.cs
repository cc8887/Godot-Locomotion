using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

internal enum LyraAdditiveState { Identity, AirIdentity }

internal sealed class LyraFullBodyAdditivesLayer
{
    private readonly Skeleton3D _skeleton;
    private readonly AlsLocalPose[] _basis;
    private readonly AlsLocalPose[] _output;
    private bool _hasApplied;

    public LyraFullBodyAdditivesLayer(LyraBoundRig rig)
    {
        LyraPoseLayerContracts.Validate();
        _skeleton = rig.Skeleton;
        _basis = new AlsLocalPose[_skeleton.GetBoneCount()];
        _output = new AlsLocalPose[_basis.Length];
    }

    public LyraAdditiveState State { get; private set; }
    public int EvaluatedFrames { get; private set; }

    public void RestoreBase()
    {
        if (!_hasApplied) return;
        LyraUnarmedAimOffset.WritePose(_skeleton, _basis);
        _hasApplied = false;
    }

    public void Apply(bool isOnGround)
    {
        if (_hasApplied) throw new InvalidOperationException("Restore the previous additive pose before evaluating.");
        LyraUnarmedAimOffset.CapturePose(_skeleton, _basis);
        EvaluateAdditive(isOnGround, _output);
        for (var bone = 0; bone < _basis.Length; bone++)
            _output[bone] = AlsLocalAdditivePose.Apply(_basis[bone], _output[bone]);
        LyraUnarmedAimOffset.WritePose(_skeleton, _output);
        _hasApplied = true;
    }

    // FullBodyAdditives has no input pose. Return additive atoms; its caller
    // supplies the absolute pose and ApplyAdditive alpha from the main graph.
    public void EvaluateAdditive(bool isOnGround, Span<AlsLocalPose> output)
    {
        if (output.Length != _basis.Length) throw new ArgumentException("Incomplete Lyra additive pose.");
        if (State == LyraAdditiveState.Identity && !isOnGround) State = LyraAdditiveState.AirIdentity;
        // Legacy Demo identity approximation. Actual compiled handler9 reads
        // IsOnGround; LyraAdditivesLayerHost implements its real recovery branch.
        // This path awaits replacement at the complete Main graph boundary.
        output.Fill(new AlsLocalPose(NVector3.Zero, NQuaternion.Identity, NVector3.Zero));
        EvaluatedFrames++;
    }
    public void EvaluateAdditive(bool isOnGround, Span<AlsPrecisePose> output)
    {
        if (output.Length != 81) throw new ArgumentException("Incomplete Lyra logical additive pose.");
        if (State == LyraAdditiveState.Identity && !isOnGround) State = LyraAdditiveState.AirIdentity;
        output.Fill(new(AlsDoubleVector.Zero, AlsQuaternion.Identity, AlsDoubleVector.Zero));
        EvaluatedFrames++;
    }
}
