using GodotAls.Core.Actions;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Explicit physical-skin compatibility adapter, not arbitrary retargeting.
/// Generates host virtual bones per native key before blending, then converts final
/// double transforms to the host FBX basis/meters. Each pose source owns its scratch.</summary>
public sealed class AlsMantlingHostPoseProfile
{
    private readonly AlsMantlingMontageProfile _adapted;
    private readonly int[] _curveTargets;
    private readonly string[] _curveNames;
    public int BoneCount { get; }
    public ReadOnlySpan<string> CurveNames=>_curveNames;
    public AlsMantlingHostPoseProfile(AlsMantlingMontageProfile profile,AlsRawAnimationSkeletonDefinition host,ReadOnlySpan<string> curveNames)
    {
        ArgumentNullException.ThrowIfNull(profile);ArgumentNullException.ThrowIfNull(host);
        _curveNames=curveNames.ToArray();BoneCount=host.LogicalBoneCount;
        if(_curveNames.Any(string.IsNullOrWhiteSpace)||_curveNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=_curveNames.Length)
            throw new ArgumentException("Host mantle curve layout must have unique names.");
        var curveLayout=profile.Curves.Values.First().Names;
        _curveTargets=new int[curveLayout.Length];
        for(var i=0;i<curveLayout.Length;i++)
        {
            var name=curveLayout[i];_curveTargets[i]=Array.FindIndex(_curveNames,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));
            if(_curveTargets[i]<0)throw new ArgumentException("Host curve layout omits native mantle curve: "+name);
        }
        var poses=profile.Definitions.Values.GroupBy(d=>d.SequencePath,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>
            profile.Poses[g.Key].AdaptHostLayout(host,g.Select(d=>d.Asset.AnimationId).Distinct().Single()),StringComparer.Ordinal);
        _adapted=new(profile.Definitions.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal),poses,profile.Curves,profile.GroupName);
    }
    public IAlsMontagePoseSource CreatePoseSource()=>new Source(this);
    private sealed class Source : IAlsMontagePoseSource
    {
        private readonly AlsMantlingHostPoseProfile _profile;
        private readonly IAlsMontagePoseSource _native;
        private readonly AlsPrecisePose[] _pose;
        private readonly AlsInertialCurve[] _curves;
        private int _sampling;
        public Source(AlsMantlingHostPoseProfile profile)
        {_profile=profile;_native=profile._adapted.CreatePoseSource();_pose=new AlsPrecisePose[profile.BoneCount];_curves=new AlsInertialCurve[profile._curveTargets.Length];}
        public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {
            if(pose.Length!=_pose.Length||curves.Length!=_profile._curveNames.Length)throw new ArgumentException("Host mantle output layout differs.");
            if(Interlocked.CompareExchange(ref _sampling,1,0)!=0)throw new InvalidOperationException("Host mantle sampling scratch is already in use.");
            try
            {
                _native.Sample(entry,_pose,_curves);
                for(var bone=0;bone<_pose.Length;bone++)_pose[bone]=ToFbx(_pose[bone]);
                _pose.CopyTo(pose);curves.Clear();
                for(var i=0;i<_curves.Length;i++)curves[_profile._curveTargets[i]]=_curves[i];
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
