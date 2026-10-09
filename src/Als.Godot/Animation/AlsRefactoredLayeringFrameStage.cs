using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Original Layering topology, with an explicit V4 Overlay boundary. Aim/spine
// and hands stay with their current owners; this stage never ticks a source.
internal sealed class AlsRefactoredLayeringFrameStage : IAlsLayerBlendingSink
{
    private readonly AlsLayerBlendingRuntime _graph;
    private readonly AlsRefactoredLayeringInputModel _inputs;
    private readonly int[] _map, _parents, _targetParents, _baseCurves;
    private readonly AlsLogicalVirtualBone[] _virtuals;
    private readonly AlsVirtualBoneDefinition[] _targetVirtuals;
    private readonly (int Native, int Legacy)[] _aliases;
    private readonly AlsLocalPose[] _base, _overlay, _nativeOutput, _pose;
    private readonly AlsPrecisePose[] _components, _targetComponents;
    private readonly AlsInertialCurve[] _baseValues, _overlayValues, _curves;
    private readonly Dictionary<int, AlsLocalPose[]> _bases;
    private readonly AlsPoseUpdateContext[] _contexts = new AlsPoseUpdateContext[2];
    private readonly bool[] _updated = new bool[2];
    private AlsFrameIdentity _identity;
    private bool _prepared, _evaluated, _visited;
    internal ReadOnlySpan<AlsLocalPose> Pose => _evaluated ? _pose : throw new InvalidOperationException("No native Layering pose.");
    internal ReadOnlySpan<AlsInertialCurve> Curves => _evaluated ? _curves : throw new InvalidOperationException("No native Layering curves.");
    internal AlsPoseUpdateContext BaseContext => Context(0);
    internal AlsPoseUpdateContext OverlayContext => Context(1);
    internal long CommittedFrames { get; private set; }

    internal AlsRefactoredLayeringFrameStage(AlsSkeletonDefinition target, ReadOnlySpan<string> curves, ReadOnlySpan<string> baseNames)
    {
        if(!AlsAnimationRuntimeOptions.Has("--refactored-stance-hosts"))
            throw new ArgumentException("Original Layering requires the validated Refactored stance skeleton boundary.");
        var profile = AlsRefactoredDemoResources.PostLayer.Value;
        var layout = profile.BasePoses[37].Pose;
        var names = curves.ToArray(); _inputs = new(names);
        _map = layout.BoneNames.ToArray().Select(target.GetLogicalBoneId).ToArray();
        _parents = layout.Parents.ToArray(); _targetParents = target.LogicalBones.Select(b=>b.ParentLogicalId).ToArray();
        _virtuals = AlsRefactoredDemoResources.NativeVirtuals; _targetVirtuals = target.VirtualBones;
        for (var bone = 0; bone < _map.Length; bone++)
            if ((_map[bone] < 0 && !_virtuals.Any(v => v.Bone == bone)) ||
                (_map[bone] >= 0 && _targetParents[_map[bone]] != (_parents[bone] < 0 ? -1 : _map[_parents[bone]])))
                throw new ArgumentException("Native Layering requires the validated physical skeleton.");
        _graph = new(profile.Layering, layout.BoneNames, _parents, curves, layout.ReferencePose.ToArray().Select(Float).ToArray());
        _base = new AlsLocalPose[_map.Length]; _overlay = new AlsLocalPose[_map.Length]; _nativeOutput = new AlsLocalPose[_map.Length];
        _pose = new AlsLocalPose[target.LogicalBones.Length]; _components = new AlsPrecisePose[_map.Length];
        _targetComponents = new AlsPrecisePose[_pose.Length];
        _baseValues = new AlsInertialCurve[names.Length]; _overlayValues = new AlsInertialCurve[names.Length];
        _curves = new AlsInertialCurve[names.Length];
        _baseCurves = baseNames.ToArray().Select(n => Array.IndexOf(names, n)).ToArray();
        if (_baseCurves.Any(i => i < 0)) throw new ArgumentException("Missing Locomotion curves.");
        _bases = profile.BasePoses.ToDictionary(p => p.Key, p =>
        {
            var pose = new AlsPrecisePose[_map.Length]; p.Value.CreateSampler().Evaluate(pose, []);
            return pose.Select(Float).ToArray();
        });
        var pairs = new Dictionary<string, string>
        {
            ["LayerHead"]="Layering_Head", ["LayerHeadAdditive"]="Layering_Head_Add",
            ["LayerSpine"]="Layering_Spine", ["LayerSpineAdditive"]="Layering_Spine_Add",
            ["LayerPelvis"]="Layering_Pelvis", ["LayerLegs"]="Layering_Legs",
            ["LayerArmLeft"]="Layering_Arm_L", ["LayerArmRight"]="Layering_Arm_R",
            ["LayerArmLeftAdditive"]="Layering_Arm_L_Add", ["LayerArmRightAdditive"]="Layering_Arm_R_Add",
            ["LayerArmLeftLocalSpace"]="Layering_Arm_L_LS", ["LayerArmRightLocalSpace"]="Layering_Arm_R_LS",
            ["LayerHandLeft"]="Layering_Hand_L", ["LayerHandRight"]="Layering_Hand_R",
        };
        _aliases = pairs.Select(p => (Native:Array.IndexOf(names,p.Key), Legacy:Array.IndexOf(names,p.Value))).ToArray();
        if (_aliases.Any(p => p.Native < 0 || p.Legacy < 0)) throw new ArgumentException("Incomplete V4 Overlay curve adapter.");
    }
    internal void Prepare(in AlsPoseUpdateContext context, AlsFrameIdentity previous, ReadOnlySpan<AlsInertialCurve> feedback,
        in AlsAnimationGraphFrame traversal, bool visited)
    {
        if (_prepared) throw new InvalidOperationException("Layering candidate already open.");
        _identity=context.Identity; _visited=visited; _prepared=true; _evaluated=false; Array.Clear(_updated);
        var input=_inputs.Evaluate(_identity,previous,feedback);
        _graph.Prepare(context.WithUpdateCounter(traversal.Update),input,feedback,traversal.Initialization,traversal.Bones,traversal.Evaluation,this,visited);
    }
    internal void Evaluate(ReadOnlySpan<AlsLocalPose> basis, ReadOnlySpan<AlsInertialCurve> baseValues,
        ReadOnlySpan<AlsLocalPose> overlay, ReadOnlySpan<AlsInertialCurve> overlayValues)
    {
        if (!_prepared || !_visited || baseValues.Length!=_baseCurves.Length || overlayValues.Length!=_curves.Length)
            throw new InvalidOperationException("Incomplete native Layering sources.");
        MapIn(basis,_base); MapIn(overlay,_overlay); Array.Clear(_baseValues);
        for(var i=0;i<baseValues.Length;i++)_baseValues[_baseCurves[i]]=baseValues[i];
        overlayValues.CopyTo(_overlayValues);
        // V4-authored Overlay curves are converted only at this boundary. Native
        // Locomotion values keep their presence and authored float values.
        foreach(var p in _aliases)_overlayValues[p.Native]=_overlayValues[p.Legacy];
        _graph.Evaluate(_nativeOutput,_curves);
        for(var b=0;b<_map.Length;b++)if(_map[b]>=0)_pose[_map[b]]=FromNative(new(_nativeOutput[b])).ToSingle();
        foreach(var v in _targetVirtuals)_pose[v.LogicalBoneId]=AlsLocalPose.Identity;
        Components(_pose,_targetParents,_targetComponents);
        foreach(var v in _targetVirtuals)
        {
            _pose[v.LogicalBoneId]=AlsPrecisePose.Relative(_targetComponents[v.TargetLogicalBoneId],_targetComponents[v.SourceLogicalBoneId]).Normalized().ToSingle();
            _targetComponents[v.LogicalBoneId]=_targetComponents[v.TargetLogicalBoneId];
        }
        _evaluated=true;
    }
    internal void ValidateCommit(AlsFrameIdentity identity)
    {
        if(!_prepared||identity!=_identity||_visited&&!_evaluated)throw new InvalidOperationException("Incomplete native Layering candidate.");
        _graph.ValidateCommit();
    }
    internal void Commit(AlsFrameIdentity identity){ValidateCommit(identity);_graph.Commit();if(_visited)CommittedFrames++;_prepared=_evaluated=false;}
    internal void Cancel(){_graph.Cancel();_prepared=_evaluated=false;}
    private void MapIn(ReadOnlySpan<AlsLocalPose> input, Span<AlsLocalPose> output)
    {
        if(input.Length!=_pose.Length)throw new ArgumentException("Foreign physical source pose.");
        for(var b=0;b<_map.Length;b++)output[b]=_map[b]>=0?ToNative(new(input[_map[b]])).ToSingle():AlsLocalPose.Identity;
        Components(output,_parents,_components);
        foreach(var v in _virtuals){output[v.Bone]=AlsPrecisePose.Relative(_components[v.Target],_components[v.Source]).Normalized().ToSingle();_components[v.Bone]=_components[v.Target];}
    }
    private static void Components(ReadOnlySpan<AlsLocalPose> pose,int[] parents,AlsPrecisePose[] output)
    {for(var b=0;b<parents.Length;b++)output[b]=parents[b]<0?new(pose[b]):AlsPrecisePose.Compose(new(pose[b]),output[parents[b]]).Normalized();}
    private static AlsPrecisePose ToNative(AlsPrecisePose p)=>new(new(p.Position.X*100,-p.Position.Y*100,p.Position.Z*100),new(-p.Rotation.X,p.Rotation.Y,-p.Rotation.Z,p.Rotation.W),p.Scale);
    private static AlsPrecisePose FromNative(AlsPrecisePose p)=>new(new(p.Position.X*.01,-p.Position.Y*.01,p.Position.Z*.01),new(-p.Rotation.X,p.Rotation.Y,-p.Rotation.Z,p.Rotation.W),p.Scale);
    private static AlsLocalPose Float(AlsPrecisePose p)=>p.ToSingle();
    private AlsPoseUpdateContext Context(int source)=>_prepared&&_updated[source]?_contexts[source]:throw new InvalidOperationException("Missing native linked context.");
    public void InitializeInput(int node,string name){}
    public void CacheInputBones(int node,string name){}
    public void UpdateInput(int node,string name,in AlsPoseUpdateContext context)
    {
        if(name is not ("Locomotion Input" or "Overlay Input"))return;
        var source=name=="Locomotion Input"?0:1;
        if(_updated[source])throw new InvalidOperationException("Native input visited twice.");_updated[source]=true;_contexts[source]=context;
    }
    public void EvaluateInput(int node,string name,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {
        curves.Clear();
        if(_bases.TryGetValue(node,out var basis)){basis.CopyTo(pose);return;}
        if(name=="Locomotion Input"){_base.CopyTo(pose);_baseValues.CopyTo(curves);}
        else if(name=="Overlay Input"){_overlay.CopyTo(pose);_overlayValues.CopyTo(curves);}
        else throw new ArgumentException("Unbound native Layering input.");
    }
    public void InitializeSlot(int node,string name){}
    // The existing production bank registers action/locomotion Slots only.
    public AlsSlotWeights GetSlotWeights(int node,string name,in AlsPoseUpdateContext context)=>AlsSlotWeights.Passthrough;
    public void UpdateSlot(int node,string name,in AlsSlotWeights weights,in AlsSlotSourceUpdate source,in AlsPoseUpdateContext context){}
    public void EvaluateSlot(int node,string name,in AlsSlotWeights weights,bool evaluated,ReadOnlySpan<AlsLocalPose> source,
        ReadOnlySpan<AlsInertialCurve> values,Span<AlsLocalPose> pose,Span<AlsInertialCurve> curves)
    {if(!evaluated||weights!=AlsSlotWeights.Passthrough)throw new InvalidOperationException("No regional Montage binding.");source.CopyTo(pose);values.CopyTo(curves);}
    public void OnCachedUpdatesSkipped(int handler,ReadOnlySpan<AlsPoseUpdateContext> skipped)
    {throw new InvalidOperationException("Unexpected skipped linked source update.");}
}
