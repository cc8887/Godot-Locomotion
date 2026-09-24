using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Original fixed-frame additive resource. Native centimeters and skeleton basis.
/// Base assets are sampled as raw poses, never recursively as additive deltas.</summary>
public sealed class AlsRefactoredAdditiveSource
{
    private readonly AlsMantlingPoseSource _target,_basis;
    private readonly AlsMantlingCurveSource _targetCurves,_baseCurves;
    private readonly string[] _curveNames;
    private readonly double _baseTime;
    private readonly bool _mesh;
    public ReadOnlySpan<string> BoneNames=>_target.BoneNames;
    public ReadOnlySpan<int> Parents=>_target.Parents;
    public ReadOnlySpan<string> CurveNames=>_curveNames;
    internal AlsRefactoredAdditiveSource(AlsMantlingPoseSource target,AlsMantlingPoseSource basis,
        AlsMantlingCurveSource targetCurves,AlsMantlingCurveSource baseCurves,double baseTime,bool mesh)
    {
        _target=target;_basis=basis;_targetCurves=targetCurves;_baseCurves=baseCurves;_baseTime=baseTime;_mesh=mesh;
        _curveNames=targetCurves.Names.ToArray().Concat(baseCurves.Names.ToArray()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public Sampler CreateSampler()=>new(this);
    public sealed class Sampler
    {
        private readonly AlsRefactoredAdditiveSource _owner;
        private readonly AlsMantlingPoseSource.Sampler _target;
        private readonly AlsPrecisePose[] _basis,_pose,_result;
        private readonly AlsQuaternion[] _scratch;
        private readonly AlsInertialCurve[] _sourceCurves,_mapped,_base,_curves;
        private readonly int[] _map;
        private int _busy;
        internal Sampler(AlsRefactoredAdditiveSource owner)
        {
            _owner=owner;_target=owner._target.CreateSampler(owner._targetCurves);
            var count=owner.BoneNames.Length;
            _basis=new AlsPrecisePose[count];_pose=new AlsPrecisePose[count];_result=new AlsPrecisePose[count];_scratch=new AlsQuaternion[count*2];
            _sourceCurves=new AlsInertialCurve[owner._targetCurves.Names.Length];
            _mapped=new AlsInertialCurve[owner._curveNames.Length];_base=new AlsInertialCurve[_mapped.Length];_curves=new AlsInertialCurve[_mapped.Length];
            int Index(string name)=>Array.FindIndex(owner._curveNames,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));
            _map=owner._targetCurves.Names.ToArray().Select(Index).ToArray();
            var baseValues=new AlsInertialCurve[owner._baseCurves.Names.Length];
            owner._basis.CreateSampler(owner._baseCurves).Sample(owner._baseTime,true,false,false,_basis,baseValues);
            for(var i=0;i<baseValues.Length;i++)_base[Index(owner._baseCurves.Names[i])]=baseValues[i];
        }
        // No clock advancement. Each worker owns its sampler; failures publish neither buffer.
        public void Sample(double seconds,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {
            if(!double.IsFinite(seconds)||pose.Length!=_pose.Length||curves.Length!=_curves.Length)
                throw new ArgumentException("Invalid additive sample time or layout.");
            if(Interlocked.Exchange(ref _busy,1)!=0)throw new InvalidOperationException("Reentrant additive sampler.");
            try
            {
                _target.Sample(seconds,true,false,false,_pose,_sourceCurves);
                if(_owner._mesh)AlsPrecisePoseBlender.MeshDifference(_pose,_basis,_owner.Parents,_scratch,_result);
                else for(var i=0;i<_pose.Length;i++)_result[i]=AlsPrecisePoseBlender.LocalDifference(_pose[i],_basis[i]);
                for(var i=0;i<_map.Length;i++)_mapped[_map[i]]=_sourceCurves[i];
                AlsLayeringCurves.Difference(_mapped,_base,_curves);
                _result.CopyTo(pose);_curves.CopyTo(curves);
            }
            finally {Volatile.Write(ref _busy,0);}
        }
    }
}
