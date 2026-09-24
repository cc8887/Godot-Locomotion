using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original 2D BlendSpace pose/curve evaluation. Native skeleton and centimeters.
/// Sampling is separate from player clocks, filtering, sync and notify dispatch.</summary>
public sealed class AlsRefactoredBlendPoseSource
{
    private readonly AlsRefactoredTriangulationProfile _profile;
    private readonly AlsMantlingPoseSource?[] _absolute;
    private readonly AlsMantlingCurveSource?[] _absoluteCurves;
    private readonly AlsRefactoredAdditiveSource?[] _additive;
    private readonly float[] _lengths;
    private readonly string[][] _sourceCurveNames;
    private readonly string[] _boneNames,_curveNames;
    private readonly int[] _parents;
    public ReadOnlySpan<string> BoneNames=>_boneNames;
    public ReadOnlySpan<int> Parents=>_parents;
    public ReadOnlySpan<string> CurveNames=>_curveNames;
    public bool IsAdditive { get; }
    public System.Numerics.Vector2 FilterWindows=>_profile.FilterWindows;
    public AlsRefactoredBlendPoseSource(AlsRefactoredTriangulationProfile profile,AlsRefactoredAnimationCatalog catalog)
    {
        if(profile.CatalogDigest!=catalog.IndexDigest)throw new ArgumentException("BlendSpace and source catalog versions differ.");
        _profile=profile;var count=profile.Samples.Length;
        _absolute=new AlsMantlingPoseSource?[count];_absoluteCurves=new AlsMantlingCurveSource?[count];_additive=new AlsRefactoredAdditiveSource?[count];
        _lengths=new float[count];_sourceCurveNames=new string[count][];
        string[]? names=null;int[]? parents=null;bool? additive=null;
        for(var i=0;i<count;i++)
        {
            var path=profile.Samples[i];var policy=catalog.Read(path).GetProperty("evaluation");
            var type=policy.GetProperty("additiveType").GetString();
            if(type is not ("AAT_None" or "AAT_LocalSpaceBase"))throw new ArgumentException("Unsupported 2D sample additive type.");
            var isAdditive=type=="AAT_LocalSpaceBase";
            if(additive.HasValue&&additive!=isAdditive)throw new ArgumentException("Mixed additive and absolute BlendSpace samples.");
            additive=isAdditive;_lengths[i]=policy.GetProperty("sequencePlayLength").GetSingle();
            string[] sourceNames;int[] sourceParents;
            if(isAdditive)
            {
                var source=catalog.CompileAdditivePose(path);_additive[i]=source;
                sourceNames=source.BoneNames.ToArray();sourceParents=source.Parents.ToArray();_sourceCurveNames[i]=source.CurveNames.ToArray();
            }
            else
            {
                var source=catalog.CompileAbsolutePoseWithCurves(path);_absolute[i]=source.Pose;_absoluteCurves[i]=source.Curves;
                sourceNames=source.Pose.BoneNames.ToArray();sourceParents=source.Pose.Parents.ToArray();_sourceCurveNames[i]=source.Curves.Names.ToArray();
            }
            if(names is not null&&(!names.SequenceEqual(sourceNames)||!parents!.SequenceEqual(sourceParents)))
                throw new ArgumentException("Incompatible BlendSpace skeletons.");
            names=sourceNames;parents=sourceParents;
        }
        _boneNames=names!;_parents=parents!;IsAdditive=additive!.Value;
        _curveNames=_sourceCurveNames.SelectMany(n=>n).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public Sampler CreateSampler()=>new(this);
    public sealed class Sampler
    {
        private readonly AlsRefactoredBlendPoseSource _owner;
        private readonly AlsMantlingPoseSource.Sampler?[] _absolute;
        private readonly AlsRefactoredAdditiveSource.Sampler?[] _additive;
        private readonly AlsInertialCurve[][] _sourceCurves;
        private readonly int[][] _curveMap;
        private readonly AlsPrecisePose[] _sample,_result;
        private readonly AlsInertialCurve[] _curves;
        private int _busy;
        internal Sampler(AlsRefactoredBlendPoseSource owner)
        {
            _owner=owner;_absolute=new AlsMantlingPoseSource.Sampler?[owner._lengths.Length];_additive=new AlsRefactoredAdditiveSource.Sampler?[_absolute.Length];
            _sourceCurves=new AlsInertialCurve[_absolute.Length][];_curveMap=new int[_absolute.Length][];
            for(var i=0;i<_absolute.Length;i++)
            {
                _absolute[i]=owner._absolute[i]?.CreateSampler(owner._absoluteCurves[i]!);_additive[i]=owner._additive[i]?.CreateSampler();
                _sourceCurves[i]=new AlsInertialCurve[owner._sourceCurveNames[i].Length];
                _curveMap[i]=owner._sourceCurveNames[i].Select(n=>Array.FindIndex(owner._curveNames,c=>c.Equals(n,StringComparison.OrdinalIgnoreCase))).ToArray();
            }
            _sample=new AlsPrecisePose[owner.BoneNames.Length];_result=new AlsPrecisePose[_sample.Length];_curves=new AlsInertialCurve[owner.CurveNames.Length];
        }
        // Time is a normalized evaluator position, not an advancing or marker-synced player.
        // The returned cache belongs to the caller's candidate frame.
        public int Evaluate(AlsBlendPoint input,float normalizedTime,int previousCache,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {
            if(!float.IsFinite(normalizedTime)||pose.Length!=_result.Length||curves.Length!=_curves.Length)
                throw new ArgumentException("Invalid original BlendSpace pose request.");
            if(Interlocked.Exchange(ref _busy,1)!=0)throw new InvalidOperationException("Reentrant BlendSpace pose sampler.");
            try
            {
                Span<AlsAimGridVertex> weights=stackalloc AlsAimGridVertex[3];
                var count=_owner._profile.Weights.Evaluate(input,previousCache,weights,out var candidate);
                var time=Math.Clamp(normalizedTime,0,1);Array.Clear(_curves);
                for(var i=0;i<count;i++)
                {
                    var vertex=weights[i];var index=vertex.Sample;var seconds=time*_owner._lengths[index];
                    if(_absolute[index] is {} absolute)absolute.Sample(seconds,true,false,false,_sample,_sourceCurves[index]);
                    else _additive[index]!.Sample(seconds,_sample,_sourceCurves[index]);
                    for(var bone=0;bone<_result.Length;bone++)_result[bone]=i==0?
                        AlsPrecisePoseBlender.Scale(_sample[bone],vertex.Weight):AlsPrecisePoseBlender.Accumulate(_result[bone],_sample[bone],vertex.Weight);
                    for(var curve=0;curve<_sourceCurves[index].Length;curve++)
                    {
                        var slot=_curveMap[index][curve];var value=_sourceCurves[index][curve];
                        _curves[slot]=i==0?AlsStandingCycleCurves.Scale(value,vertex.Weight):AlsStandingCycleCurves.Accumulate(_curves[slot],value,vertex.Weight);
                    }
                }
                for(var bone=0;bone<_result.Length;bone++)
                {
                    if(count>1)_result[bone]=_result[bone].Normalized();
                    _result[bone]=_result[bone].Normalized();
                }
                _result.CopyTo(pose);_curves.CopyTo(curves);return candidate;
            }
            finally {Volatile.Write(ref _busy,0);}
        }
    }
}
