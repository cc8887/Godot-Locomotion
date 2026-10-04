using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraAimingNodeState(bool Initialized,float X,float Y,float Weight,float Alpha,
    float Time,float Previous,float Delta,int Cache,ImmutableArray<LyraMainLeanSampleState> Samples,
    AlsBlendSpaceSpringFilterState FilterX=default,AlsBlendSpaceSpringFilterState FilterY=default,
    AlsAssetMarkerRecord Marker=default,bool HasBeenFullWeight=false,bool LodEnabled=false);
internal sealed record LyraAimingCandidate(LyraAimWeightCandidate Weights,LyraAimingExposedPins Pins,
    LyraAirVisit Visit,float Delta,ImmutableArray<LyraAimingNodeState> Nodes,ImmutableArray<bool> Active);
internal readonly struct LyraAimingPoseView
{
    private readonly LyraAimingLayerHost _host;private readonly LyraAimingCandidate _candidate;
    internal LyraAimingPoseView(LyraAimingLayerHost host,LyraAimingCandidate candidate){_host=host;_candidate=candidate;}
    public ReadOnlySpan<AlsPrecisePose> Pose=>_host.Pose(_candidate);
    public ReadOnlySpan<LyraCurveSample> Curves=>_host.Curves(_candidate);
    public ReadOnlySpan<LyraAttributeSample> Attributes=>_host.Attributes(_candidate);
    public LyraRootMotionAttribute RootMotion=>_host.Root(_candidate);
}

// Original cached input and two RotationOffsetBlendSpace occurrences. Source
// weights are collected before the enclosing character's common Sync; no
// independent clocks or Skeleton writes are allowed here.
internal sealed class LyraAimingLayerHost
{
    private readonly LyraLocomotionResourceCatalog _resources;
    private readonly LyraAimWeightHost _weights;
    private readonly LyraAimingGraphDefinition _definition;
    private readonly AlsTriangulatedBlendSpace[] _grids=new AlsTriangulatedBlendSpace[2];
    private readonly LyraLogicalSourceSampler[][] _samplers=new LyraLogicalSourceSampler[2][];
    private readonly string[][] _slots=new string[2][];
    private readonly float[] _speed=new float[2];private readonly bool[] _ease=new bool[2],_legacy=new bool[2];
    private readonly float[,] _window=new float[2,2],_min=new float[2,2],_max=new float[2,2];
    private readonly int[] _sequenceBase=new int[2],_assets=new int[2],_players=new int[2];private readonly long _epoch;
    private readonly bool[] _attributeOverrides;private readonly bool _rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();
    private readonly AlsPrecisePose[][] _ao=[new AlsPrecisePose[81],new AlsPrecisePose[81]],_branches=[new AlsPrecisePose[81],new AlsPrecisePose[81]];
    private readonly AlsPrecisePose[] _scratch=new AlsPrecisePose[81],_pose=new AlsPrecisePose[81];
    private readonly AlsQuaternion[] _rotations=new AlsQuaternion[162];
    private readonly LyraCurveSample[] _curves;private readonly LyraAttributeSample[] _attributes,_sampleAttributes;
    private readonly LyraAttributeSample[][] _aoAttributes,_branchAttributes;
    private readonly float[] _bestAttributeWeights;
    private LyraRootMotionAttribute _root;private LyraAimingCandidate? _pending;
    private ImmutableArray<LyraAimingNodeState> _weighted,_resolved;
    private bool _collected,_synced,_evaluated,_failed,_externalWeights;
    public ImmutableArray<LyraAimingNodeState> Nodes {get;private set;}
    public LyraAimWeights Weights=>_weights.Weights;
    public ImmutableArray<LyraAimingNodeState> PreparedNodes=>_pending is null?throw new InvalidOperationException("No Aiming frame."):_synced?_resolved:_weighted;
    public AlsAssetSyncPlayer[] Players {get;private set;}=[];
    public AlsAssetSyncSample[] Samples {get;private set;}=[];
    public int[] Groups=>Enumerable.Repeat(-1,Players.Length).ToArray();
    public LyraAimingLayerHost(LyraLocomotionResourceCatalog resources,LyraMainLayerGraphCatalog graphs,string profile,int playerBase=700,long epoch=1)
    {
        _resources=resources;_definition=new(graphs,profile);_weights=new(graphs,profile);_epoch=epoch;
        if(playerBase<0 || playerBase>(int.MaxValue-15)/32-79 || epoch<=0)throw new ArgumentException("Invalid Aiming owner identity.");
        _players[0]=playerBase+79;_players[1]=playerBase+74;
        var bank=resources.Bank;_curves=new LyraCurveSample[bank.Curves.Names.Length];_attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        _sampleAttributes=new LyraAttributeSample[_attributes.Length];_aoAttributes=[new LyraAttributeSample[_attributes.Length],new LyraAttributeSample[_attributes.Length]];
        _bestAttributeWeights=new float[_attributes.Length];
        _branchAttributes=[new LyraAttributeSample[_attributes.Length],new LyraAttributeSample[_attributes.Length]];
        _attributeOverrides=LyraCycleLayerPosePolicy.Load(profile,bank).AttributeOverrides;
        using var policy=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/aiming_layer_v1_policy.json"));
        foreach(var (path,n) in new[]{_definition.RelaxedAsset,_definition.IdleAsset}.Select((v,i)=>(v,i)))
        {
            var p=policy.RootElement.GetProperty("spaces").EnumerateArray().Single(r=>r.GetProperty("path").GetString()==path);
            var sampleProfile=p.GetProperty("profile").GetString()!;
            _sequenceBase[n]=resources.AimId(sampleProfile,0);_assets[n]=100000+_sequenceBase[n];
            if(_sequenceBase[n]+15>resources.Sequences.Length || p.GetProperty("useGrid").GetBoolean() ||
                p.GetProperty("meshBlend").GetBoolean() || p.GetProperty("matchSyncPhases").GetBoolean() ||
                p.GetProperty("perBoneOverrides").GetInt32()!=0)
                throw new NotSupportedException("Unbound Aiming samples or unsupported original BlendSpace policy.");
            _speed[n]=p.GetProperty("weightSpeed").GetSingle();_ease[n]=p.GetProperty("ease").GetBoolean();_legacy[n]=p.GetProperty("legacyLength").GetBoolean();
            var axes=p.GetProperty("axes");if(axes[0].GetProperty("wrap").GetBoolean() || axes[1].GetProperty("wrap").GetBoolean())throw new NotSupportedException("Aiming wrapping axis changed.");
            for(var axis=0;axis<2;axis++)
            {
                var filter=p.GetProperty("filters")[axis];_window[n,axis]=filter.GetProperty("time").GetSingle();
                _min[n,axis]=axes[axis].GetProperty("min").GetSingle();_max[n,axis]=axes[axis].GetProperty("max").GetSingle();
                if(_window[n,axis]>0 && (!filter.GetProperty("type").GetString()!.Contains("BSIT_SPRING_DAMPER") ||
                    filter.GetProperty("damping").GetSingle()!=1 || filter.GetProperty("maxSpeed").GetSingle()!=0))
                    throw new NotSupportedException("Changed Aiming spring damping policy.");
            }
            var vertices=new List<AlsBlendTriangleVertex>();
            foreach(var t in p.GetProperty("grid").GetProperty("triangles").EnumerateArray())for(var v=0;v<3;v++)
            {
                var point=t.GetProperty("vertices")[v];var edge=t.GetProperty("edgeInfo")[v];var normal=edge.GetProperty("normal");
                var triangles=edge.GetProperty("adjacentPerimeterTriangleIndices");var corners=edge.GetProperty("adjacentPerimeterVertexIndices");
                vertices.Add(new(t.GetProperty("sampleIndices")[v].GetInt32(),new(point.GetProperty("x").GetDouble(),point.GetProperty("y").GetDouble()),
                    new(normal.GetProperty("x").GetDouble(),normal.GetProperty("y").GetDouble()),edge.GetProperty("neighbourTriangleIndex").GetInt32(),triangles[0].GetInt32(),triangles[1].GetInt32(),corners[0].GetInt32(),corners[1].GetInt32()));
            }
            _grids[n]=new(vertices.ToArray(),15,new(axes[0].GetProperty("min").GetDouble(),axes[1].GetProperty("min").GetDouble()),new(axes[0].GetProperty("max").GetDouble(),axes[1].GetProperty("max").GetDouble()));
            _slots[n]=Enumerable.Range(0,15).Select(i=>$"aim_{sampleProfile}_{i}").ToArray();_samplers[n]=_slots[n].Select(bank.CreateSampler).ToArray();
            foreach(var slot in _slots[n])if(bank.Get(slot).FloatCurveNames.Count!=0)throw new NotSupportedException("Aiming additive source gained authored curves.");
            foreach(var s in p.GetProperty("samples").EnumerateArray())if(s.GetProperty("rate").GetSingle()!=1 || s.GetProperty("singleFrame").GetBoolean() || s.GetProperty("mirror").GetBoolean())throw new NotSupportedException("Changed Aiming sample rate/frame/mirror policy.");
        }
        Nodes=Enumerable.Range(0,2).Select(_=>new LyraAimingNodeState(false,0,0,0,0,0,0,0,-1,[],Marker:AlsAssetMarkerRecord.Invalid)).ToImmutableArray();
    }
    private static LyraAimingNodeState InitializeSource(LyraAimingNodeState state,LyraAimingExposedPins pins)
        =>state with{Initialized=true,X=pins.Yaw,Y=pins.Pitch,Time=0,Samples=[],FilterX=default,FilterY=default,
            Marker=state.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false},HasBeenFullWeight=false};
    internal bool InitializeSourceNode(int node,in LyraFullBody_AimingParameters parameters,LyraAimWeights weights)
    {
        if(_pending is not null)throw new InvalidOperationException("Aiming phase initialization needs an idle layer.");
        var index=node==79?0:node==74?1:-1;if(index<0)return false;
        var pins=_definition.Expose(weights,parameters.AimYaw,parameters.AimPitch);
        Nodes=Nodes.SetItem(index,InitializeSource(Nodes[index],pins));return true;
    }
    public LyraAimingCandidate Prepare(in LyraAimWeightInput weightInput,double yaw,double pitch,LyraAirVisit visit,
        LyraAimWeights? workerWeights=null)
    {
        if(_pending is not null)throw new InvalidOperationException("Aiming frame is pending.");
        if(!float.IsFinite(visit.Weight)||visit.Weight<0||weightInput.Delta>float.MaxValue)throw new ArgumentException("Invalid Aiming visit.");
        try
        {
            _externalWeights=workerWeights is not null;
            var weights=workerWeights is {} external?new LyraAimWeightCandidate(external):_weights.Prepare(weightInput);
            var pins=_definition.Expose(weights.Weights,yaw,pitch);var nodes=Nodes.ToArray();var active=new bool[2];
            var a=pins.Blend<1-1e-5f;var b=pins.Blend>1e-5f;
            for(var n=0;n<2;n++)
            {
                var s=nodes[n];if(!s.Initialized || visit.Initialize)s=InitializeSource(s,pins);
                active[n]=visit.Visited&&(n==0?a:b);
                if(active[n])
                {
                    var weight=a&&b?visit.Weight*(n==0?1-pins.Blend:pins.Blend):visit.Weight;
                    s=s with{X=pins.Yaw,Y=pins.Pitch,Alpha=1,Weight=weight,LodEnabled=true,
                        HasBeenFullWeight=s.HasBeenFullWeight||weight>=1-1e-5f};
                }
                nodes[n]=s;
            }
            _failed=_synced=_evaluated=false;_weighted=nodes.ToImmutableArray();
            return _pending=new(weights,pins,visit,(float)weightInput.Delta,_weighted,active.ToImmutableArray());
        }
        catch{_weights.Cancel();throw;}
    }
    public void Collect(LyraAimingCandidate c)
    {
        Validate(c);if(_collected || _synced)throw new InvalidOperationException("Duplicate or late Aiming source collection.");
        var nodes=c.Nodes.ToArray();var players=new List<AlsAssetSyncPlayer>();var samples=new List<AlsAssetSyncSample>();
        Span<AlsAimGridVertex> target=stackalloc AlsAimGridVertex[3];Span<AlsBlendSpaceSampleWeight> desired=stackalloc AlsBlendSpaceSampleWeight[3];
        Span<AlsBlendSpaceSampleWeight> previous=stackalloc AlsBlendSpaceSampleWeight[15];Span<AlsBlendSpaceSampleWeight> result=stackalloc AlsBlendSpaceSampleWeight[18];
        for(var n=0;n<2;n++)if(c.Active[n])
        {
            var node=nodes[n];
            var fx=AlsBlendSpaceSpringFilter.Advance(node.FilterX,node.X,c.Delta,_window[n,0],_min[n,0],_max[n,0]);
            var fy=AlsBlendSpaceSpringFilter.Advance(node.FilterY,node.Y,c.Delta,_window[n,1],_min[n,1],_max[n,1]);
            // TickRecord owns the transient triangulation cache; the node's
            // old CachedTriangulationIndex is not passed to that constructor.
            var count=_grids[n].Evaluate(new(fx.Output,fy.Output),-1,target,out _);
            for(var i=0;i<count;i++)desired[i]=new(target[i].Sample,target[i].Weight);
            for(var i=0;i<node.Samples.Length;i++)previous[i]=node.Samples[i].Weight;
            int total;
            if(_speed[n]>0)total=AlsBlendSpaceWeightSmoothing.Evaluate(previous[..node.Samples.Length],desired[..count],c.Delta,_speed[n],_ease[n],result);
            else{desired[..count].CopyTo(result);total=count;}
            var start=samples.Count;var values=ImmutableArray.CreateBuilder<LyraMainLeanSampleState>(total);
            for(var i=0;i<total;i++)
            {
                var id=_players[n]*32+result[i].SampleId;var seq=_sequenceBase[n]+result[i].SampleId;
                samples.Add(new(id,seq,result[i].Weight));values.Add(new(result[i],new(id,_resources.Sequences[seq].AnimationId,0,0,AlsAssetMarkerRecord.Invalid,0,0)));
            }
            nodes[n]=node with{Samples=values.MoveToImmutable(),FilterX=fx,FilterY=fy};
            players.Add(new(_players[n],_assets[n],_epoch,AlsAssetSyncKind.BlendSpace,node.Time,1,node.Weight,start,total,0,LegacyLength:_legacy[n]));
        }
        _weighted=nodes.ToImmutableArray();Players=players.ToArray();Samples=samples.ToArray();_collected=true;
    }
    public void Resolve(LyraAimingCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> players,ReadOnlySpan<AlsAssetSampleHistory> samples)
    {
        Validate(c);if(!_collected||_synced)throw new InvalidOperationException("Missing collection or duplicate Aiming Sync result.");var nodes=_weighted.ToArray();
        for(var n=0;n<2;n++)
        {
            var index=-1;for(var i=0;i<players.Length;i++)if(players[i].PlayerId==_players[n]){if(index>=0)throw new InvalidOperationException("Duplicate Aiming clock.");index=i;}
            if((index>=0)!=c.Active[n])throw new InvalidOperationException("Missing/hidden Aiming clock.");if(index<0)continue;
            var p=players[index];var node=nodes[n];if(p.Epoch!=_epoch||p.AssetId!=_assets[n]||p.SampleCount!=node.Samples.Length||p.SampleStart<0||p.SampleStart>samples.Length-p.SampleCount)throw new InvalidOperationException("Foreign Aiming clock.");
            if(!float.IsFinite(p.Time)||p.Time<0||p.Time>1||!float.IsFinite(p.DeltaPrevious)||p.DeltaPrevious<0||p.DeltaPrevious>1||!float.IsFinite(p.Delta))
                throw new InvalidOperationException("Nonfinite or out-of-range Aiming player history.");
            var ss=ImmutableArray.CreateBuilder<LyraMainLeanSampleState>(p.SampleCount);
            for(var i=0;i<p.SampleCount;i++)
            {
                var expected=node.Samples[i];var s=samples[p.SampleStart+i];var length=_resources.Sequences[_sequenceBase[n]+expected.Weight.SampleId].DurationSeconds;
                if(s.SampleId!=expected.Clock.SampleId||s.AnimationId!=expected.Clock.AnimationId||!float.IsFinite(s.Time)||s.Time<0||s.Time>length||
                    !float.IsFinite(s.PreviousTime)||s.PreviousTime<0||s.PreviousTime>length||!float.IsFinite(s.DeltaPrevious)||!float.IsFinite(s.Delta))
                    throw new InvalidOperationException("Foreign, nonfinite or out-of-range Aiming sample.");
                ss.Add(expected with{Clock=s});
            }
            nodes[n]=node with{Time=p.Time,Previous=p.DeltaPrevious,Delta=p.Delta,Samples=ss.MoveToImmutable()};
        }
        _resolved=nodes.ToImmutableArray();_synced=true;
    }
    public LyraAimingPoseView Evaluate(LyraAimingCandidate c,in LyraLayerPoseInput input)
    {
        Validate(c);_evaluated=false;
        try
        {
            if(!_synced||!c.Visit.Visited)throw new InvalidOperationException("Aiming evaluation precedes Sync or follows a hidden visit.");
            var bank=_resources.Bank;
            if(!ReferenceEquals(input.Layout,bank)||input.Curves.Length!=_curves.Length||input.Attributes.Length!=_attributes.Length||
                input.Curves.Overlaps(_curves)||input.Attributes.Overlaps(_attributes))throw new ArgumentException("Foreign or aliased Aiming input.");
            LyraPoseBuffers.Validate(input.Pose,_pose);
            for(var n=0;n<2;n++)if(c.Active[n])
            {
                Array.Clear(_aoAttributes[n]);Array.Fill(_bestAttributeWeights,-1);var first=true;
                foreach(var s in _resolved[n].Samples)
                {
                    _samplers[n][s.Weight.SampleId].Sample(s.Clock.Time,_scratch);
                    bank.Curves.Attributes.Sample(_slots[n][s.Weight.SampleId],s.Clock.Time,_sampleAttributes);
                    var weight=Math.Clamp(s.Weight.Weight,0,1);
                    for(var bone=0;bone<81;bone++)_ao[n][bone]=first?AlsPrecisePoseBlender.Scale(_scratch[bone],weight):AlsPrecisePoseBlender.Accumulate(_ao[n][bone],_scratch[bone],weight);
                    for(var id=0;id<_attributes.Length;id++)
                    {
                        var value=_sampleAttributes[id];if(!value.Present)continue;
                        if(_attributeOverrides[id]){if(weight>_bestAttributeWeights[id]){_aoAttributes[n][id]=value;_bestAttributeWeights[id]=weight;}}
                        else{var old=_aoAttributes[n][id];_aoAttributes[n][id]=LyraAttributeSample.Accumulate(old,value,weight);}
                    }
                    first=false;
                }
                // BlendPosesTogether normalizes multi-sample results;
                // UBlendSpace then normalizes again for every sample count.
                if(_resolved[n].Samples.Length>1)
                    for(var bone=0;bone<81;bone++)_ao[n][bone]=_ao[n][bone].Normalized();
                for(var bone=0;bone<81;bone++)_ao[n][bone]=_ao[n][bone].Normalized();
                AlsPrecisePoseBlender.MeshApplyFullWeightIsPc(input.Pose,_ao[n],bank.Parents,_rotations,_branches[n]);
                // AccumulateAdditivePose normalizes the converted locals;
                // RotationOffsetBlendSpace then normalizes the pose again.
                for(var bone=0;bone<81;bone++)_branches[n][bone]=_branches[n][bone].Normalized();
                for(var id=0;id<_attributes.Length;id++)
                {var a=input.Attributes[id];var b=_aoAttributes[n][id];_branchAttributes[n][id]=LyraAttributeSample.Add(a,b);}
            }
            input.Curves.CopyTo(_curves);_root=input.RootMotion;
            if(c.Active[0]&&c.Active[1])
            {
                var a=1-c.Pins.Blend;var b=1-a;
                for(var bone=0;bone<81;bone++)_pose[bone]=AlsPrecisePoseBlender.AccumulateIsPc(AlsPrecisePoseBlender.Scale(_branches[0][bone],a),_branches[1][bone],b).Normalized();
                // Both additive sources author no curves, so the original
                // FMath::Lerp(A,A,b) preserves each input value and union flags.
                for(var id=0;id<_attributes.Length;id++)_attributes[id]=LyraLayeredDataBlend.BlendIntegerUniform(_branchAttributes[0][id],_branchAttributes[1][id],b,_attributeOverrides[id]);
                _root=LyraRootMotionAttribute.BlendUniform(input.RootMotion,input.RootMotion,b,_rootOverride);
            }
            else{var n=c.Active[0]?0:1;_branches[n].CopyTo(_pose,0);_branchAttributes[n].CopyTo(_attributes,0);}
            _evaluated=true;return new(this,c);
        }
        catch{_failed=true;throw;}
    }
    private void Validate(LyraAimingCandidate c){if(!ReferenceEquals(c,_pending)||_failed)throw new InvalidOperationException("Stale or failed Aiming frame.");}
    public void ValidateCommit(LyraAimingCandidate c,bool updateOnly)
    {Validate(c);if(!_externalWeights)_weights.Validate(c.Weights);if(!_synced||(!updateOnly&&c.Visit.Visited&&!_evaluated))throw new InvalidOperationException("Incomplete Aiming frame commit.");}
    public void Commit(LyraAimingCandidate c,bool updateOnly=false){ValidateCommit(c,updateOnly);if(!_externalWeights)_weights.Commit(c.Weights);Nodes=_resolved;Clear();}
    public void Cancel(){_weights.Cancel();Clear();}
    private void Clear(){_pending=null;_collected=_synced=_evaluated=_failed=_externalWeights=false;Players=[];Samples=[];}
    private void Output(LyraAimingCandidate c){Validate(c);if(!_evaluated)throw new InvalidOperationException("Aiming output is unavailable.");}
    internal ReadOnlySpan<AlsPrecisePose> DiagnosticPose(LyraAimingCandidate c,int branch,bool additive)
    {Output(c);if((uint)branch>=2||!c.Active[branch])throw new InvalidOperationException("Inactive Aiming diagnostic branch.");return additive?_ao[branch]:_branches[branch];}
    internal ReadOnlySpan<AlsPrecisePose> Pose(LyraAimingCandidate c){Output(c);return _pose;}
    internal ReadOnlySpan<LyraCurveSample> Curves(LyraAimingCandidate c){Output(c);return _curves;}
    internal ReadOnlySpan<LyraAttributeSample> Attributes(LyraAimingCandidate c){Output(c);return _attributes;}
    internal LyraRootMotionAttribute Root(LyraAimingCandidate c){Output(c);return _root;}
}
