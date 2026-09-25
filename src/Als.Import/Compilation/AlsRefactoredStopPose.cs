using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

/// <summary>Stop pose evaluation from a finalized cache66 pose. The caller owns
/// cache evaluation order and must supply this frame's post-node119 Movement Details.</summary>
public sealed class AlsRefactoredStopPose
{
    public AlsRefactoredStopPoseGraph Graph { get; }
    private readonly string[] _bones,_curves,_baseCurves;
    private readonly AlsPrecisePose[] _reference;
    private readonly int[] _baseMap,_leafMap,_locks;
    public ReadOnlySpan<string> BoneNames=>_bones;
    public ReadOnlySpan<string> CurveNames=>_curves;
    public AlsRefactoredStopPose(AlsRefactoredAnimationCatalog catalog,AlsRefactoredStopPoseGraph graph,
        AlsRefactoredSkeletonCurves metadata,ReadOnlySpan<string> movementCurves)
    {
        if(catalog.IndexDigest!=graph.Resources.CatalogDigest||catalog.IndexDigest!=metadata.CatalogDigest)throw new ArgumentException("Foreign Stop pose resources.");
        Graph=graph;_bones=graph.Evaluators.BoneNames.ToArray();_baseCurves=movementCurves.ToArray();
        if(_baseCurves.Any(string.IsNullOrEmpty)||_baseCurves.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=_baseCurves.Length)
            throw new ArgumentException("Invalid Movement Details curve layout.");
        var reference=catalog.CompileAbsolutePose("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose");
        if(!reference.BoneNames.SequenceEqual(_bones)||!reference.Parents.SequenceEqual(graph.Evaluators.Parents))throw new ArgumentException("Foreign Stop reference skeleton.");
        _reference=reference.ReferencePose.ToArray();
        _curves=_baseCurves.Concat(graph.Evaluators.CurveNames.ToArray()).Concat(new[]{"FootLeftLock","FootRightLock"}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _baseMap=_baseCurves.Select(Index).ToArray();_leafMap=graph.Evaluators.CurveNames.ToArray().Select(Index).ToArray();
        _locks=graph.States.ToArray().Select(s=>s.LockCurve==""?-1:Index(s.LockCurve)).ToArray();
    }
    private int Index(string name)=>Array.FindIndex(_curves,n=>n.Equals(name,StringComparison.OrdinalIgnoreCase));
    public Sampler CreateSampler()=>new(this);
    public sealed class Sampler
    {
        private readonly AlsRefactoredStopPose _profile;
        private readonly AlsPrecisePose[] _base,_result,_multi;
        private readonly AlsInertialCurve[] _baseCurves,_resultCurves,_multiCurves;
        private readonly AlsPrecisePose[][] _states,_channels,_leaves;
        private readonly AlsInertialCurve[][] _stateCurves,_channelCurves,_leafCurves;
        private readonly AlsQuaternion[] _rotations;
        private readonly bool[] _evaluated=new bool[5];
        private int _busy;
        private AlsRefactoredMovementInertialization? _movementOwner;
        public int StateEvaluations { get; private set; }
        internal Sampler(AlsRefactoredStopPose profile)
        {
            _profile=profile;var bones=profile._bones.Length;var curves=profile._curves.Length;
            _base=new AlsPrecisePose[bones];_result=new AlsPrecisePose[bones];_multi=new AlsPrecisePose[bones];
            _baseCurves=new AlsInertialCurve[curves];_resultCurves=new AlsInertialCurve[curves];_multiCurves=new AlsInertialCurve[curves];
            _states=Poses(5);_channels=Poses(4);_leaves=Poses(12);_stateCurves=Curves(5);_channelCurves=Curves(4);_leafCurves=Curves(12);
            _rotations=new AlsQuaternion[bones*3];var scratch=new AlsInertialCurve[profile._leafMap.Length];
            for(var i=0;i<12;i++)
            {
                profile.Graph.Evaluators.Sample(profile.Graph.Evaluators.Evaluators[i].PropertyIndex,_leaves[i],scratch);
                for(var c=0;c<scratch.Length;c++)_leafCurves[i][profile._leafMap[c]]=scratch[c];
            }
            AlsPrecisePose[][] Poses(int count)=>Enumerable.Range(0,count).Select(_=>new AlsPrecisePose[bones]).ToArray();
            AlsInertialCurve[][] Curves(int count)=>Enumerable.Range(0,count).Select(_=>new AlsInertialCurve[curves]).ToArray();
        }
        public void Sample(long frame,AlsRefactoredStopRuntime machine,AlsRefactoredStopSourceRuntime source,
            AlsRefactoredMovementInertialization movement,Span<AlsPrecisePose> output,Span<AlsInertialCurve> outputCurves)
        {
            movement.ValidateContext(source.Identity);
            if(_movementOwner is not null&&!ReferenceEquals(_movementOwner,movement)||
                !movement.BoneNames.SequenceEqual(_profile._bones)||!movement.CurveNames.SequenceEqual(_profile._baseCurves))
                throw new ArgumentException("Foreign Stop Movement cache owner/layout.");
            Sample(frame,machine,source,movement.Pose,movement.Curves,output,outputCurves);_movementOwner??=movement;
        }
        public void Sample(long frame,AlsRefactoredStopRuntime machine,AlsRefactoredStopSourceRuntime source,
            ReadOnlySpan<AlsPrecisePose> movementPose,ReadOnlySpan<AlsInertialCurve> movementCurves,
            Span<AlsPrecisePose> output,Span<AlsInertialCurve> outputCurves)
        {
            if(!ReferenceEquals(source.Profile,_profile.Graph)||movementPose.Length!=_base.Length||movementCurves.Length!=_profile._baseCurves.Length||
                output.Length!=_result.Length||outputCurves.Length!=_resultCurves.Length)throw new ArgumentException("Invalid Stop pose owner/layout.");
            source.ValidateTraversal(frame,machine);
            foreach(var p in movementPose)if(!p.Position.IsFinite||!p.Scale.IsFinite||!double.IsFinite(p.Rotation.LengthSquared)||p.Rotation.LengthSquared<1e-8)throw new ArgumentException("Invalid Movement Details pose.");
            foreach(var c in movementCurves)if(c.Present&&!float.IsFinite(c.Value))throw new ArgumentException("Invalid Movement Details curve.");
            if(Interlocked.Exchange(ref _busy,1)!=0)throw new InvalidOperationException("Reentrant Stop pose sampler.");
            try
            {
                movementPose.CopyTo(_base);Array.Clear(_baseCurves);
                for(var c=0;c<movementCurves.Length;c++)_baseCurves[_profile._baseMap[c]]=movementCurves[c];
                Array.Clear(_evaluated);StateEvaluations=0;
                var state=machine.Candidate.State;var stack=state.Transitions;var first=stack.Count==0?state.CurrentState:stack.GetTransition(0).From;
                State(first);_states[first].CopyTo(_result,0);_stateCurves[first].CopyTo(_resultCurves,0);
                for(var i=0;i<stack.Count;i++)
                {
                    var edge=stack.GetTransition(i);State(edge.To);
                    for(var b=0;b<_result.Length;b++)_result[b]=AlsPrecisePoseBlender.BlendRaw(_result[b],_states[edge.To][b],edge.Alpha);
                    for(var c=0;c<_resultCurves.Length;c++)_resultCurves[c]=AlsStandingCycleCurves.Accumulate(AlsStandingCycleCurves.Scale(_resultCurves[c],1-edge.Alpha),_stateCurves[edge.To][c],edge.Alpha);
                }
                if(stack.Count>0)for(var b=0;b<_result.Length;b++)_result[b]=_result[b].Normalized();
                _result.CopyTo(output);_resultCurves.CopyTo(outputCurves);
            }
            finally{Volatile.Write(ref _busy,0);}

            void State(int index)
            {
                if(_evaluated[index])return;var output=_states[index];var curves=_stateCurves[index];
                _base.CopyTo(output,0);_baseCurves.CopyTo(curves,0);
                if(index>=3)
                {
                    var weights=source.DirectionWeights;var initialized=false;Array.Clear(_multiCurves);var offset=(index-3)*6;
                    for(var channel=0;channel<4;channel++)
                    {
                        var weight=weights[channel];if(weight<=AlsPoseBlender.WeightThreshold)continue;
                        if(channel<2){_leaves[offset+channel].CopyTo(_channels[channel],0);_leafCurves[offset+channel].CopyTo(_channelCurves[channel],0);}
                        else
                        {
                            var selector=source.SelectorWeights(index,channel==3);var count=0;Array.Clear(_channelCurves[channel]);
                            for(var child=0;child<2;child++)
                            {
                                var w=selector[child];if(w<=AlsPoseBlender.WeightThreshold)continue;var leaf=offset+2+(channel-2)*2+child;
                                if(w==1){_leaves[leaf].CopyTo(_channels[channel],0);_leafCurves[leaf].CopyTo(_channelCurves[channel],0);count=1;break;}
                                Accumulate(_channels[channel],_channelCurves[channel],_leaves[leaf],_leafCurves[leaf],w,count++>0);
                            }
                            if(count==0)throw new InvalidOperationException("Stop lateral pose has no updated selector.");
                            if(selector.X!=1&&selector.Y!=1)Normalize(_channels[channel]);
                        }
                        Accumulate(_multi,_multiCurves,_channels[channel],_channelCurves[channel],weight,initialized);initialized=true;
                    }
                    if(initialized)Normalize(_multi);else _profile._reference.CopyTo(_multi,0);
                    AlsMeshSpacePoseBlend.Blend(_base,_multi,_profile.Graph.Evaluators.Parents,_profile.Graph.States[index].BoneWeights,_rotations,output);
                    // Metadata gate proves that this original skeleton has no LinkedBones filters.
                    AlsLayeringCurves.BlendLayers(_baseCurves,_multiCurves,[1],AlsLayerCurveBlendMode.Override,curves);
                }
                if(index>0)curves[_profile._locks[index]]=new(1);
                _evaluated[index]=true;StateEvaluations++;
            }
        }
        private static void Normalize(AlsPrecisePose[] pose){for(var b=0;b<pose.Length;b++)pose[b]=pose[b].Normalized();}
        private static void Accumulate(AlsPrecisePose[] pose,AlsInertialCurve[] curves,AlsPrecisePose[] source,AlsInertialCurve[] sourceCurves,float weight,bool initialized)
        {
            for(var b=0;b<pose.Length;b++)pose[b]=initialized?AlsPrecisePoseBlender.Accumulate(pose[b],source[b],weight):AlsPrecisePoseBlender.Scale(source[b],weight);
            for(var c=0;c<curves.Length;c++)curves[c]=AlsStandingCycleCurves.Accumulate(curves[c],sourceCurves[c],weight);
        }
    }
}
