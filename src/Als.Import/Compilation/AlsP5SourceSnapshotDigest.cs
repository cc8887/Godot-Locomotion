using GodotAls.Core.Contracts;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Compilation;

public static partial class AlsP5CoreRuntimeBindingCompiler
{
    // Domain-separated extension of the existing compiled payload digest. Full source and
    // inventory SHA-256 values cover immutable numeric tables as well as native provenance.
    private static ulong ComputeSourceSnapshotDigest(int version, ulong payloadDigest,
        AlsP5OccurrenceLayout layout, AlsLocomotionSourceProfile sources, AlsP5SourceInventory inventory,
        AlsStandingWalkRunDefinition[] directions, string animationSetDigest)
    {
        var writer = new FnvWriter();
        writer.Add(version); writer.Add(payloadDigest); writer.Add(layout.Version); writer.Add(layout.Digest);
        var stamp = sources.RuntimeStamp;
        writer.Add(stamp.Version); writer.Add(stamp.SkeletonId);
        writer.Add(stamp.Digest0); writer.Add(stamp.Digest1); writer.Add(stamp.Digest2); writer.Add(stamp.Digest3);
        writer.Add<byte>(Convert.FromHexString(inventory.Digest), static (ref FnvWriter w, in byte x) => w.Add(x));
        writer.Add<byte>(Convert.FromHexString(animationSetDigest), static (ref FnvWriter w, in byte x) => w.Add(x));
        writer.Add<AlsP5SourceOccurrenceMapping>(layout.SourceMappings, static (ref FnvWriter w, in AlsP5SourceOccurrenceMapping x) =>
        {
            w.Add(x.PlayerId); w.Add(x.SampleId); w.Add(x.CompiledNodeIndex); w.Add(x.SourceIndex);
            w.Add(x.AnimationId); w.Add(x.OccurrenceHandleId);
        });
        writer.Add<int>(layout.UnboundNativeSourceIndices, static (ref FnvWriter w, in int x) => w.Add(x));
        writer.Add<AlsStandingWalkRunDefinition>(directions, static (ref FnvWriter w, in AlsStandingWalkRunDefinition x) =>
        { w.Add(x.WalkPoseId); w.Add(x.WalkId); w.Add(x.RunPoseId); w.Add(x.RunId); });
        return writer.Value;
    }
}
