using System.Runtime.CompilerServices;
using Godot;
using GodotAls.Animation;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

// Value-only publication: no prop Node or shared mutable array crosses the Worker barrier.
[InlineArray(32)]
internal struct AlsPropPoseBuffer { private AlsLocalPose _element0; }
internal struct AlsOverlayPropFrame
{
    public AlsFrameIdentity Identity;
    public AlsOverlayKind Overlay;
    public Transform3D Attachment;
    public float Draw;
    public int BoneCount;
    public AlsPropPoseBuffer Pose;
}

internal sealed class AlsOverlayPropSampler
{
    private readonly AlsPreciseAnimationPoseSourceSampler _bow;
    private readonly AlsPrecisePose[] _pose;
    private readonly AlsOverlayPropProfile _profile;
    public AlsOverlayPropSampler(AlsOverlayPropProfile profile, AlsRawAnimationSourceBank bank, AlsAnimationSetDefinition set)
    {
        _profile = profile;
        var source = bank.GetSource(profile.BowAnimationId);
        _pose = new AlsPrecisePose[source.PoseData.LogicalBoneCount];
        if (_pose.Length > 32) throw new ArgumentException("Prop pose exceeds publication capacity.");
        _bow = new(source, bank, set, []);
    }
    public AlsOverlayPropFrame Capture(in AlsFrameIdentity identity, AlsOverlayKind overlay, AlsLocomotionAnimationController controller)
    {
        var frame = new AlsOverlayPropFrame { Identity = identity, Overlay = overlay, Attachment = Transform3D.Identity };
        var binding = _profile.Get(overlay);
        if (!binding.HasProp) return frame;
        frame.Attachment = controller.PendingPropAttachment(binding.LogicalBone);
        frame.Attachment *= new Transform3D(Basis.Identity, new(binding.Offset.X, binding.Offset.Y, binding.Offset.Z));
        AlsP3Presentation.ThrowIfNonFinite(frame.Attachment);
        if (binding.AnimationId < 0) return frame;
        frame.Draw = controller.PendingPropDraw(_profile.DrawCurve);
        SampleBow(frame.Draw, ref frame);
        return frame;
    }
    internal void SampleBow(float draw, ref AlsOverlayPropFrame frame)
    {
        if (!float.IsFinite(draw)) throw new ArgumentException("Nonfinite prop evaluator time.");
        _bow.Sample(draw, true, false, false, _pose, []);
        frame.BoneCount = _pose.Length;
        for (var i = 0; i < _pose.Length; i++) frame.Pose[i] = _pose[i].ToSingle();
    }
}
