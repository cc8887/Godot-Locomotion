using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Immutable original Transition pose resources in the host's native 79-bone
/// layout. Host curve indices are explicit; absent curves stay absent.</summary>
public sealed class AlsRefactoredTransitionPose
{
    private sealed record Source(AlsSequenceMontageAsset Asset, AlsRefactoredAdditiveSource Pose, int[] Curves);
    private readonly Dictionary<int, Source> _sources = new();
    private readonly string[] _bones, _curves;
    private readonly int[] _parents;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<int> Parents => _parents;
    public ReadOnlySpan<string> CurveNames => _curves;
    public AlsRefactoredTransitionPose(AlsRefactoredAnimationCatalog catalog, AlsRefactoredTransitionMontages montages,
        ReadOnlySpan<string> bones, ReadOnlySpan<int> parents, ReadOnlySpan<string> curves)
        :this(catalog,montages.CatalogDigest,montages.Assets,montages.SourcePath,bones,parents,curves) { }
    public AlsRefactoredTransitionPose(AlsRefactoredAnimationCatalog catalog, AlsRefactoredStandingActions actions,
        ReadOnlySpan<string> bones, ReadOnlySpan<int> parents, ReadOnlySpan<string> curves)
        :this(catalog,actions.CatalogDigest,actions.Assets,actions.SourcePath,bones,parents,curves) { }
    private AlsRefactoredTransitionPose(AlsRefactoredAnimationCatalog catalog,string digest,ReadOnlySpan<AlsSequenceMontageAsset> assets,
        Func<int,string> sourcePath,ReadOnlySpan<string> bones,ReadOnlySpan<int> parents,ReadOnlySpan<string> curves)
    {
        if (catalog.IndexDigest != digest || bones.Length != 79 || parents.Length != bones.Length ||
            curves.ToArray().Any(string.IsNullOrWhiteSpace) || curves.ToArray().Distinct(StringComparer.OrdinalIgnoreCase).Count() != curves.Length)
            throw new ArgumentException("Foreign transition pose layout/catalog.");
        _bones = bones.ToArray(); _parents = parents.ToArray(); _curves = curves.ToArray();
        foreach (var asset in assets)
        {
            var source = catalog.CompileAdditivePose(sourcePath(asset.AnimationId));
            if (!source.BoneNames.SequenceEqual(bones) || !source.Parents.SequenceEqual(parents))
                throw new ArgumentException("Transition pose requires the original native bone basis/layout.");
            var map = source.CurveNames.ToArray().Select(name => Array.FindIndex(_curves, c => c.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (map.Any(i => i < 0)) throw new ArgumentException("Host omitted an authored transition curve.");
            _sources.Add(asset.AnimationId, new(asset, source, map));
        }
    }
    public Sampler CreateSampler() => new(this);
    public sealed class Sampler : IAlsMontagePoseSource
    {
        private sealed record Local(Source Source, AlsRefactoredAdditiveSource.Sampler Sampler, AlsInertialCurve[] Curves);
        private readonly Dictionary<int, Local> _sources;
        private readonly AlsPrecisePose[] _pose;
        private readonly AlsInertialCurve[] _curves;
        private int _busy;
        internal Sampler(AlsRefactoredTransitionPose profile)
        {
            _sources = profile._sources.ToDictionary(p => p.Key, p => new Local(p.Value, p.Value.Pose.CreateSampler(), new AlsInertialCurve[p.Value.Curves.Length]));
            _pose = new AlsPrecisePose[profile._bones.Length]; _curves = new AlsInertialCurve[profile._curves.Length];
        }
        public void Sample(in AlsMontageEvaluation entry, Span<AlsPrecisePose> pose, Span<AlsInertialCurve> curves)
        {
            if (entry.Slot != AlsMontageSlot.Transition || entry.AdditiveType != 2 || entry.ActionDefinitionId != -1 ||
                !_sources.TryGetValue(entry.AnimationId, out var local) || !float.IsFinite(entry.Position) ||
                entry.Position < 0 || entry.Position > local.Source.Asset.Duration || pose.Length != _pose.Length || curves.Length != _curves.Length)
                throw new ArgumentException("Foreign transition sample or invalid time/output layout.");
            if (Interlocked.Exchange(ref _busy, 1) != 0) throw new InvalidOperationException("Reentrant Transition sampler.");
            try
            {
                // Sampling reads the physical instance time; it owns no playback clock.
                local.Sampler.Sample(entry.Position, _pose, local.Curves);
                _curves.AsSpan().Clear();
                for (var i = 0; i < local.Curves.Length; i++) _curves[local.Source.Curves[i]] = local.Curves[i];
                _pose.CopyTo(pose); _curves.CopyTo(curves);
            }
            finally { Volatile.Write(ref _busy, 0); }
        }
    }
}
