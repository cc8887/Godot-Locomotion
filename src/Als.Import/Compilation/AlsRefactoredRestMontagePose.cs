using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Absolute Turn and mesh-additive DynamicTransition samples at the
/// physical montage instance time, in the original 79-bone skeleton basis.</summary>
public sealed class AlsRefactoredRestMontagePose
{
    public string CatalogDigest { get; }
    private sealed record Source(AlsSequenceMontageAsset Asset, AlsMantlingPoseSource? Absolute,
        AlsMantlingCurveSource? AbsoluteCurves, AlsRefactoredAdditiveSource? Additive, string[] Names);
    private readonly Dictionary<int,Source> _sources = new();
    private readonly string[] _bones,_curves;
    private readonly int[] _parents;
    public ReadOnlySpan<string> BoneNames => _bones;
    public ReadOnlySpan<int> Parents => _parents;
    public ReadOnlySpan<string> CurveNames => _curves;
    public AlsRefactoredRestMontagePose(AlsRefactoredAnimationCatalog catalog, AlsRefactoredRestMontages montages, ReadOnlySpan<string> hostCurves)
        : this(catalog, montages.Settings.CatalogDigest, montages.Assets, montages.SourcePath, hostCurves) { }
    internal AlsRefactoredRestMontagePose(AlsRefactoredAnimationCatalog catalog, string digest,
        ReadOnlySpan<AlsSequenceMontageAsset> assets, Func<int,string> sourcePath, ReadOnlySpan<string> hostCurves)
    {
        if(catalog.IndexDigest!=digest)throw new ArgumentException("Foreign rest montage pose catalog.");
        CatalogDigest=catalog.IndexDigest;
        var reference=catalog.CompileAbsolutePose(AlsRefactoredStandingRestGraph.IdleSequence);
        _bones=reference.BoneNames.ToArray();_parents=reference.Parents.ToArray();
        var names=hostCurves.ToArray();
        if(names.Any(string.IsNullOrWhiteSpace)||names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Length)throw new ArgumentException("Invalid rest montage host curves.");
        foreach(var asset in assets)
        {
            var path=sourcePath(asset.AnimationId);Source source;
            if(asset.AdditiveType==0)
            {
                var absolute=catalog.CompileAbsolutePoseWithCurves(path);
                if(!absolute.Pose.BoneNames.SequenceEqual(_bones)||!absolute.Pose.Parents.SequenceEqual(_parents))throw new ArgumentException("Foreign turn skeleton.");
                source=new(asset,absolute.Pose,absolute.Curves,null,absolute.Curves.Names.ToArray());
            }
            else
            {
                var additive=catalog.CompileAdditivePose(path);
                if(!additive.BoneNames.SequenceEqual(_bones)||!additive.Parents.SequenceEqual(_parents))throw new ArgumentException("Foreign dynamic transition skeleton.");
                source=new(asset,null,null,additive,additive.CurveNames.ToArray());
            }
            _sources.Add(asset.AnimationId,source);
        }
        _curves=names.Concat(_sources.Values.SelectMany(s=>s.Names)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public Sampler CreateSampler()=>new(this);
    public sealed class Sampler : IAlsMontagePoseSource
    {
        private sealed record Local(Source Source,AlsMantlingPoseSource.Sampler? Absolute,AlsRefactoredAdditiveSource.Sampler? Additive,int[] Map,AlsInertialCurve[] Curves);
        private readonly Dictionary<int,Local> _sources;
        private readonly AlsPrecisePose[] _pose;
        private readonly AlsInertialCurve[] _curves;
        private int _busy;
        internal Sampler(AlsRefactoredRestMontagePose profile)
        {
            _sources=profile._sources.ToDictionary(p=>p.Key,p=>new Local(p.Value,p.Value.Absolute?.CreateSampler(p.Value.AbsoluteCurves!),p.Value.Additive?.CreateSampler(),
                p.Value.Names.Select(n=>Array.FindIndex(profile._curves,c=>c.Equals(n,StringComparison.OrdinalIgnoreCase))).ToArray(),new AlsInertialCurve[p.Value.Names.Length]));
            _pose=new AlsPrecisePose[profile._bones.Length];_curves=new AlsInertialCurve[profile._curves.Length];
        }
        public void Sample(in AlsMontageEvaluation entry, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
        {
            if(!_sources.TryGetValue(entry.AnimationId,out var local)||entry.Slot!=local.Source.Asset.Slot||entry.AdditiveType!=local.Source.Asset.AdditiveType||
                entry.ActionDefinitionId!=-1||!float.IsFinite(entry.Position)||entry.Position<0||entry.Position>local.Source.Asset.Duration||output.Length!=_pose.Length||curves.Length!=_curves.Length)
                throw new ArgumentException("Foreign rest montage sample/layout.");
            if(Interlocked.Exchange(ref _busy,1)!=0)throw new InvalidOperationException("Reentrant rest montage sampling.");
            try
            {
                if(local.Absolute is {} absolute)absolute.Sample(entry.Position,true,false,false,_pose,local.Curves);
                else local.Additive!.Sample(entry.Position,_pose,local.Curves);
                Array.Clear(_curves);for(var c=0;c<local.Map.Length;c++)_curves[local.Map[c]]=local.Curves[c];
                _pose.CopyTo(output);_curves.CopyTo(curves);
            }
            finally{Volatile.Write(ref _busy,0);}
        }
    }
}
