using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Explicit physical-skin compatibility adapter, not arbitrary retargeting.
/// Generates host virtual bones per native key before blending, then converts final
/// double transforms to the host FBX basis/meters. ForNativeSkeleton retains the
/// original layout and centimetres. Each pose source owns its scratch.</summary>
public sealed class AlsMantlingHostPoseProfile
{
    private readonly AlsMantlingMontageProfile _adapted;
    private readonly int[] _curveTargets;
    private readonly string[] _curveNames;
    private readonly bool _convertToFbx;
    private readonly Dictionary<int,(AlsAuthoredMontageAsset Asset,AlsMantlingCurveSource Curves,int[] Targets)> _montageCurves=[];
    public int BoneCount { get; }
    public ReadOnlySpan<string> CurveNames=>_curveNames;
    public AlsMantlingHostPoseProfile(AlsMantlingMontageProfile profile,AlsRawAnimationSkeletonDefinition host,ReadOnlySpan<string> curveNames,
        IReadOnlyDictionary<string,AlsMantlingCurveSource>? montageCurves=null)
        :this(profile,host,curveNames,montageCurves,true) { }
    // Refactored linked graphs already use the original full logical skeleton in
    // native centimetres. Keep exactly that layout while sharing montage curves.
    public static AlsMantlingHostPoseProfile ForNativeSkeleton(AlsMantlingMontageProfile profile,ReadOnlySpan<string> curveNames,
        IReadOnlyDictionary<string,AlsMantlingCurveSource> montageCurves)
    {ArgumentNullException.ThrowIfNull(montageCurves);return new(profile,null,curveNames,montageCurves,false);}
    private AlsMantlingHostPoseProfile(AlsMantlingMontageProfile profile,AlsRawAnimationSkeletonDefinition? host,ReadOnlySpan<string> curveNames,
        IReadOnlyDictionary<string,AlsMantlingCurveSource>? montageCurves,bool convertToFbx)
    {
        ArgumentNullException.ThrowIfNull(profile);if(convertToFbx)ArgumentNullException.ThrowIfNull(host);
        _convertToFbx=convertToFbx;
        _curveNames=curveNames.ToArray();BoneCount=host?.LogicalBoneCount??profile.Poses.Values.First().Data.LogicalBoneCount;
        if(_curveNames.Any(string.IsNullOrWhiteSpace)||_curveNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=_curveNames.Length)
            throw new ArgumentException("Host mantle curve layout must have unique names.");
        var curveLayout=profile.Curves.Values.First().Names;
        _curveTargets=new int[curveLayout.Length];
        for(var i=0;i<curveLayout.Length;i++)
        {
            var name=curveLayout[i];_curveTargets[i]=Array.FindIndex(_curveNames,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));
            if(_curveTargets[i]<0)throw new ArgumentException("Host curve layout omits native mantle curve: "+name);
        }
        if(host is null)_adapted=profile;
        else
        {
            var poses=profile.Definitions.Values.GroupBy(d=>d.SequencePath,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>
                profile.Poses[g.Key].AdaptHostLayout(host,g.Select(d=>d.Asset.AnimationId).Distinct().Single()),StringComparer.Ordinal);
            _adapted=new(profile.Definitions.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal),poses,profile.Curves,profile.GroupName);
        }
        if(montageCurves is not null)
        {
            if(!montageCurves.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(profile.Definitions.Keys))
                throw new ArgumentException("Incomplete mantle montage curve inventory.");
            foreach(var definition in profile.Definitions.Values)
            {
                var source=montageCurves[definition.Path];var asset=definition.Asset;
                if(source.SourcePath!=definition.Path||source.AnimationInputsDigest!=profile.Poses[definition.SequencePath].AnimationInputsDigest)
                    throw new ArgumentException("Foreign mantle montage curves.");
                // Evaluation.Position currently carries sequence time. For these native assets
                // the timelines coincide; never reconstruct a rounded montage time by division.
                if(asset.ClipStart!=0||asset.ClipRate!=1)throw new ArgumentException("Montage curves require an explicit montage time for remapped segments.");
                var targets=source.Names.ToArray().Select(name=>Array.FindIndex(_curveNames,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase))).ToArray();
                if(targets.Any(i=>i<0))throw new ArgumentException("Host layout omits montage-owned curves.");
                _montageCurves.Add(asset.ActionDefinitionId,(asset,source,targets));
            }
        }
    }
    public IAlsMontagePoseSource CreatePoseSource()=>new Source(this);
    private sealed class Source : IAlsMontagePoseSource
    {
        private readonly AlsMantlingHostPoseProfile _profile;
        private readonly IAlsMontagePoseSource _native;
        private readonly AlsPrecisePose[] _pose;
        private readonly AlsInertialCurve[] _curves;
        private readonly AlsInertialCurve[] _montageScratch;
        private int _sampling;
        public Source(AlsMantlingHostPoseProfile profile)
        {_profile=profile;_native=profile._adapted.CreatePoseSource();_pose=new AlsPrecisePose[profile.BoneCount];_curves=new AlsInertialCurve[profile._curveTargets.Length];
            _montageScratch=new AlsInertialCurve[profile._montageCurves.Values.Select(v=>v.Targets.Length).DefaultIfEmpty(0).Max()];}
        public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {
            if(pose.Length!=_pose.Length||curves.Length!=_profile._curveNames.Length)throw new ArgumentException("Host mantle output layout differs.");
            if(Interlocked.CompareExchange(ref _sampling,1,0)!=0)throw new InvalidOperationException("Host mantle sampling scratch is already in use.");
            try
            {
                var montage=default((AlsAuthoredMontageAsset Asset,AlsMantlingCurveSource Curves,int[] Targets));
                if(_profile._montageCurves.Count>0)
                {
                    if(!_profile._montageCurves.TryGetValue(entry.ActionDefinitionId,out montage)||montage.Asset.AnimationId!=entry.AnimationId)
                        throw new ArgumentException("Foreign mantle montage curve identity.");
                    montage.Curves.Sample(entry.Position,_montageScratch.AsSpan(0,montage.Targets.Length));
                }
                _native.Sample(entry,_pose,_curves);
                if(_profile._convertToFbx)for(var bone=0;bone<_pose.Length;bone++)_pose[bone]=ToFbx(_pose[bone]);
                _pose.CopyTo(pose);curves.Clear();
                for(var i=0;i<_curves.Length;i++)curves[_profile._curveTargets[i]]=_curves[i];
                // UE SlotEvaluatePose: sequence curves.Combine(montage curves), before slot weighting.
                if(montage.Targets is not null)
                    for(var i=0;i<montage.Targets.Length;i++)curves[montage.Targets[i]]=_montageScratch[i];
            }
            finally{Volatile.Write(ref _sampling,0);}
        }
    }
    public static AlsPrecisePose ToFbx(in AlsPrecisePose pose)=>Convert(pose,.01);
    internal static AlsPrecisePose ToNative(in AlsPrecisePose pose)=>Convert(pose,100);
    private static AlsPrecisePose Convert(in AlsPrecisePose pose,double scale)
    {
        pose.Validate();var p=pose.Position;var q=pose.Rotation;
        var result=new AlsPrecisePose(new(p.X*scale,-p.Y*scale,p.Z*scale),new(-q.X,q.Y,-q.Z,q.W),pose.Scale);
        result.Validate();return result;
    }
}
