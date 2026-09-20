using GodotAls.Core.Actions;

namespace GodotAls.Core.Locomotion;

public readonly record struct AlsStopTransitionBinding(int GeneratedIndex, string NotifyName, int AnimationId,
    int AdditiveType, float BlendIn, float BlendOut, float PlayRate, float StartTime);

// V4's two unconditional state-entered consumers. Overlay's standing/ShouldMove
// gate does not apply, even if movement changed before deferred notify dispatch.
public sealed class AlsStopTransitionDefinition
{
    private readonly AlsStopTransitionBinding[] _bindings;
    public string BindingDigest { get; }
    public ReadOnlySpan<AlsStopTransitionBinding> Bindings => _bindings;
    public AlsMontageSlot Slot => AlsMontageSlot.Grounded;
    public int LoopCount => 1;
    public float BlendOutTriggerTime => 0;
    public AlsStopTransitionDefinition(string digest, AlsStopTransitionBinding[] bindings)
    {
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit) || bindings.Length != 2 ||
            bindings.Select(b => b.GeneratedIndex).Distinct().Count() != 2 ||
            bindings.Select(b => b.AnimationId).Distinct().Count() != 2 ||
            bindings[0].NotifyName != "->N Stop L" || bindings[1].NotifyName != "->N Stop R" ||
            bindings.Any(b => b.GeneratedIndex < 0 || b.AnimationId < 0 || b.AdditiveType != 2 ||
                !float.IsFinite(b.BlendIn) || b.BlendIn < 0 || !float.IsFinite(b.BlendOut) || b.BlendOut < 0 ||
                !float.IsFinite(b.PlayRate) || b.PlayRate <= 0 || !float.IsFinite(b.StartTime) || b.StartTime < 0))
            throw new ArgumentException("Incomplete original Stop transition consumers.");
        BindingDigest = digest; _bindings = (AlsStopTransitionBinding[])bindings.Clone();
    }
    public AlsStopTransitionBinding Resolve(in AlsGroundedMachineEvent notify)
    {
        if (notify.Kind != AlsGroundedEventKind.StateEntered || notify.SourceIndex is < 3 or > 6)
            throw new ArgumentException("Stop playback requires an authored Lock/Plant state entry.");
        var binding = _bindings[notify.SourceIndex is 3 or 5 ? 0 : 1];
        if (binding.GeneratedIndex != notify.NotifyIndex) throw new ArgumentException("Stop state and generated notify identity differ.");
        return binding;
    }
}
