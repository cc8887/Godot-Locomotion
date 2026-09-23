using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// The original SequencePlayer has its own identity even though Jump can also
// sample ALS_Flail. Only the immutable raw asset is shared between those nodes.
internal sealed class AlsRagdollAnimationSource : IAlsPreciseRagdollPoseSource, IDisposable
{
    private readonly AlsMovementAnimationSource _source;
    private readonly AlsRawAnimationSkeletonDefinition _skeleton;
    private readonly string[] _names;
    private readonly float _duration;
    public AlsRagdollAnimationSource(AlsMovementGraphDefinition definition, AlsAnimationSetDefinition set, ReadOnlySpan<string> names)
    {
        var profile = definition.RagdollPose; var frame = definition.RagdollFrame;
        if (profile.AnimationId != frame.Sequence.AnimationId || profile.PlayerNodeIndex != frame.PlayerNodeIndex)
            throw new ArgumentException("Ragdoll pose and frame source identities differ.");
        var bank = definition.RagdollRawSources; var source = bank.GetSource(profile.AnimationId);
        _duration = frame.Sequence.DurationSeconds; _skeleton = bank.GetSkeleton(profile.SkeletonId); _names = names.ToArray();
        if (_duration != (float)source.Policy.SequencePlayLength || source.Policy.AdditiveType != AlsRawAnimationAdditiveType.None)
            throw new ArgumentException("Ragdoll clock and pose resource differ.");
        _source = new(source, bank, set, _names, rawBonePose: false);
    }
    public void Sample(float seconds, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        if (curves.Length != _names.Length) throw new ArgumentException("Ragdoll curve layout differs.");
        _source.SampleSourceSeconds(_skeleton.ReferencePose, seconds, _duration, pose);
        for (var i = 0; i < curves.Length; i++) curves[i] = _source.Curve(seconds, _names[i]);
    }
    public void Dispose() => _source.Dispose();
    internal void SamplePrecise(float seconds, Span<AlsPrecisePose> pose) =>
        _source.SampleSourceSeconds(_skeleton.PreciseReferencePose, seconds, _duration, pose);
    public void SamplePrecise(float seconds, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
    {
        if(curves.Length!=_names.Length) throw new ArgumentException("Ragdoll curve layout differs.");
        SamplePrecise(seconds,pose);
        for(var i=0;i<curves.Length;i++) curves[i]=_source.Curve(seconds,_names[i]);
    }
}
