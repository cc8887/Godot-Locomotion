using GodotAls.Core.Actions;
using GodotAls.Core.Events;

namespace GodotAls.Import.Compilation;

public static class AlsMontageActionPlaybackCompiler
{
    public static AlsMontageActionPlaybackReader Compile(AlsAnimationSetDefinition set,
        ReadOnlySpan<AlsAuthoredMontageAsset> assets, AlsMontageNotifyBinding notifies)
    {
        var bindings = new List<AlsMontagePlaybackBinding>();
        foreach (var asset in assets)
        {
            var montage = set.Montages[asset.MontageId];
            if (montage.Slots.Length != 1 || montage.Slots[0].Segments.Length != 1 || montage.Sections.Length != 1)
                throw new ArgumentException("Playback summary requires the compiled single-section montage.");
            var segment = montage.Slots[0].Segments[0];
            if (segment.AnimationId != asset.AnimationId || segment.AnimationStartTime != asset.ClipStart ||
                segment.PlayRate != asset.ClipRate || montage.PlayLength != asset.Duration)
                throw new ArgumentException("Playback summary segment differs from the physical montage.");
            // Match the sequence occurrence used by the current notify compiler;
            // its handle can change when root/overlay sources join the layout.
            var matches = notifies.Ranges.ToArray().Where(r => !r.Direct &&
                r.ActionDefinitionId == asset.ActionDefinitionId && r.AnimationId == asset.AnimationId && r.Slot == asset.Slot).ToArray();
            if (matches.Length != 1 || matches[0].ClipStart != asset.ClipStart || matches[0].ClipRate != asset.ClipRate)
                throw new ArgumentException("Playback summary lacks its unique sequence notify occurrence.");
            bindings.Add(new(asset, montage.Sections[0].SectionId, segment.SegmentId, matches[0].Handle));
        }
        return new(bindings.ToArray());
    }
}
