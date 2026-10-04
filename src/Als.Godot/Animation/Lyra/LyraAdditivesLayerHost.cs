using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraAdditivesMachineRuntime : LyraLinkedMachineRuntime
{
    public LyraAdditivesMachineRuntime(LyraCompiledMachine definition,int curves)
        : base(definition,curves,"FullBodyAdditve_SM",3) {}
}
internal readonly struct LyraAdditivesPoseView
{
    private readonly LyraAdditivesLayerHost _owner;
    private readonly LyraIdleMachineCandidate _frame;
    internal LyraAdditivesPoseView(LyraAdditivesLayerHost owner,LyraIdleMachineCandidate frame){_owner=owner;_frame=frame;}
    public ReadOnlySpan<AlsPrecisePose> Pose=>_owner.Pose(_frame);
    public ReadOnlySpan<LyraCurveSample> Curves=>_owner.Curves(_frame);
    public ReadOnlySpan<LyraAttributeSample> Attributes=>_owner.Attributes(_frame);
    public LyraRootMotionAttribute RootMotion=>_owner.Root(_frame);
}

// Actual compiled handler9 reads IsOnGround. The older DSL literal false is
// not its executable contract. Register the real recovery occurrence before
// the character's one Sync; Evaluate consumes that committed tick candidate.
internal sealed class LyraAdditivesLayerHost
{
    private readonly AlsPrecisePose[] _pose=new AlsPrecisePose[81];
    private readonly AlsInertialCurve[] _machineCurves;
    private readonly LyraCurveSample[] _curves;
    private readonly LyraAttributeSample[] _attributes;
    private readonly Func<int,bool> _predicate;
    private readonly LyraStatePoseEvaluator _identity;
    private readonly LyraLogicalSourceSampler _sampler;
    private readonly LyraLogicalSourceDefinition _definition;
    private readonly AlsRawRootMotionIntervalSampler _rootSampler;
    private readonly LyraCompressedRootBank _rootBank;
    private readonly AlsAssetSyncSequence _sequence;
    private readonly int _asset,_player;
    private readonly long _epoch;
    private readonly AlsPrecisePose[] _recovery=new AlsPrecisePose[81];
    private readonly LyraCurveSample[] _recoveryCurves;
    private readonly LyraAttributeSample[] _recoveryAttributes;
    private LyraRootMotionAttribute _recoveryRoot,_root;
    private readonly bool _rootOverride=LyraRootMotionAttribute.LoadBlendPolicy();
    private LyraIdleOccurrence _source=new(-1,0,0,0,AlsAssetMarkerRecord.Invalid,0,0,false,-1,0,false),_nextSource,_resolved;
    private double _nextFalling,_nextAlpha;
    private long _stateVisited=-1,_nextStateVisited;
    private float _stateWeight,_nextStateWeight;
    private bool _hasSync;
    public double TimeFalling {get;private set;}
    public double LandAlpha {get;private set;}
    public LyraIdleOccurrence Source=>_source;
    internal void InitializeSource()
    {
        if(_pending is not null)throw new InvalidOperationException("Recovery source initialization needs an idle host.");
        _source=_source with{AssetId=_asset,Time=0,PublicTime=0,
            Marker=_source.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false},ResetPending=true};
    }
    public LyraIdleOccurrence PreparedSource=>_hasSync?_resolved:_nextSource;
    public double PreparedTimeFalling=>_pending is not null?_nextFalling:throw new InvalidOperationException("No additive candidate.");
    public double PreparedLandAlpha=>_pending is not null?_nextAlpha:throw new InvalidOperationException("No additive candidate.");
    public AlsAssetSyncPlayer[] Players {get;private set;}=[];
    public AlsAssetSyncSample[] Samples {get;private set;}=[];
    public int[] Groups {get;private set;}=[];
    private LyraIdleMachineCandidate? _pending;
    private bool _ground,_evaluated,_failed;
    public LyraAdditivesMachineRuntime Machine {get;}
    public LyraAdditivesLayerHost(LyraLocomotionResourceCatalog resources,LyraMainLayerGraphCatalog graphs,string profile,LyraCompiledMachine definition,int playerBase=700,long epoch=1)
    {
        var bank=resources.Bank;_asset=resources.RecoveryId(profile);_player=checked(playerBase+5);_epoch=epoch;
        if(playerBase<0 || epoch<=0)throw new ArgumentException("Invalid additive source identity.");
        _sequence=resources.Sequences[_asset];_definition=bank.Get(profile+"_jump_recovery_additive");_sampler=bank.CreateSampler(_definition.Slot);
        var graph=graphs.Graph(profile,LyraLayerHook.FullBodyAdditives);
        var nodes=graph.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("index").GetInt32());
        var settings=nodes[1].GetProperty("settings");
        if(graph.GetProperty("root").GetInt32()!=12 || nodes.Count!=8 || nodes[1].GetProperty("machineIndex").GetInt32()!=0 ||
            settings.GetProperty("maxTransitionsPerFrame").GetInt32()!=1 || !settings.GetProperty("bSkipFirstUpdateTransition").GetBoolean() ||
            !settings.GetProperty("bReinitializeOnBecomingRelevant").GetBoolean() || settings.GetProperty("bAllowConduitEntryStates").GetBoolean() ||
            nodes[2].GetProperty("links").GetArrayLength()!=0 || nodes[3].GetProperty("links").GetArrayLength()!=0 ||
            nodes[7].GetProperty("settings").GetProperty("refPoseType").GetString()!="EIT_Additive")
            throw new NotSupportedException("Changed original FullBodyAdditives closure.");
        if(definition.InitialState!=0 || !definition.States.Select(s=>s.Name).SequenceEqual(new[]{"Identity","AirIdentity","LandRecovery"}) ||
            definition.Edges.Count!=4 || !definition.Edges.Select(e=>(e.Previous,e.Next)).SequenceEqual(new[]{(0,1),(1,2),(2,0),(2,0)}) ||
            definition.Edges[0].Duration!=0 || definition.Edges.Skip(1).Any(e=>e.Duration!=.2f) ||
            definition.Edges.Any(e=>e.Inertial || e.BlendProfile!="") ||
            !definition.States.SelectMany(s=>s.Exits).Select(e=>e.Delegate).SequenceEqual(new[]{8,9,10,11}) ||
            definition.States.SelectMany(s=>s.Exits).Any(e=>!e.Desired || e.Automatic!=(e.Edge==3) || e.OnlyWhenActive || e.RequiredSyncGroup!="None"))
            throw new NotSupportedException("Changed additive machine rules or topology.");
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/additives_layer_v1_policy.json"));
        var policy=document.RootElement;
        if(policy.GetProperty("schemaVersion").GetInt32()!=1 || policy.GetProperty("stage").GetString()!="OriginalFullBodyAdditives" ||
            policy.GetProperty("skeleton").GetString()!="ALS81" || !policy.GetProperty("landingEdgeEnabled").GetBoolean() ||
            !policy.GetProperty("additiveContext").GetBoolean() || policy.GetProperty("automaticEdge").GetInt32()!=3)
            throw new NotSupportedException("Unsupported additive policy.");
        foreach(var dependency in policy.GetProperty("dependencies").EnumerateObject())
            if(dependency.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/"+dependency.Name)))
                throw new InvalidOperationException("Stale additive policy.");
        var rules=policy.GetProperty("rules").EnumerateArray().ToArray();
        if(rules.Length!=3 || !rules.Select(r=>r.GetProperty("predicate").GetString()).SequenceEqual(new[]{"NotIsOnGround","IsOnGround","NotIsOnGround"}) ||
            !rules.Select(r=>r.GetProperty("edge").GetInt32()).SequenceEqual(new[]{0,1,2}) ||
            !rules.Select(r=>r.GetProperty("delegate").GetInt32()).SequenceEqual(new[]{8,9,10}))throw new NotSupportedException("Changed original additive predicate bindings.");
        Machine=new(definition,bank.Curves.Names.Length);_machineCurves=new AlsInertialCurve[bank.Curves.Names.Length];
        _curves=new LyraCurveSample[bank.Curves.Names.Length];_attributes=new LyraAttributeSample[bank.Curves.Attributes.Layout.Length];
        _recoveryCurves=new LyraCurveSample[_curves.Length];_recoveryAttributes=new LyraAttributeSample[_attributes.Length];
        var resource=policy.GetProperty("resources").GetProperty(profile);
        if(resource.GetProperty("slot").GetString()!=_definition.Slot || resource.GetProperty("path").GetString()!=resources.Path(_asset) ||
            resource.GetProperty("sync").GetProperty("length").GetSingle()!=_sequence.DurationSeconds || resource.GetProperty("sync").GetProperty("markers").GetArrayLength()!=0)
            throw new InvalidOperationException("Stale recovery resource binding.");
        var rootResource=JsonSerializer.SerializeToElement(new{schemaVersion=1,assets=new Dictionary<string,JsonElement>{{resources.Path(_asset),resource.GetProperty("compressedRoot")}}});
        _rootBank=LyraCompressedRootBank.Load(rootResource,bank);
        _rootSampler=_rootBank.CreateSampler(_definition.Slot,bank.Reference[0],_definition.NormalizedRootMotionScale);
        _predicate=Rule;_identity=Identity;
    }
    public static bool Rule(int edge,bool isOnGround)=>edge switch{0 or 2=>!isOnGround,1=>isOnGround,_=>throw new ArgumentOutOfRangeException(nameof(edge))};
    private bool Rule(int edge)=>Rule(edge,_ground);
    private void Identity(int state,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
    {
        if(state==2)
        {_recovery.CopyTo(pose);for(var n=0;n<curves.Length;n++)curves[n]=new(_recoveryCurves[n].Value,_recoveryCurves[n].Present);}
        else if(state is 0 or 1){pose.Fill(new(AlsDoubleVector.Zero,AlsQuaternion.Identity,AlsDoubleVector.Zero));curves.Clear();}
        else throw new ArgumentOutOfRangeException(nameof(state));
    }
    public LyraIdleMachineCandidate Prepare(bool isOnGround,float delta,LyraAirVisit visit,bool falling=false,bool jumping=false,bool crouching=false,
        double? workerTimeFalling=null)
    {
        if(_pending is not null)throw new InvalidOperationException("Additive frame is pending.");
        _ground=isOnGround;_evaluated=_failed=_hasSync=false;Players=[];Samples=[];Groups=[];
        _nextFalling=workerTimeFalling??(falling?TimeFalling+(double)delta:jumping?0:TimeFalling);_nextAlpha=LandAlpha;
        if(!double.IsFinite(_nextFalling))throw new ArgumentException("Nonfinite worker falling time.");
        _nextSource=_source;_nextStateVisited=_stateVisited;_nextStateWeight=_stateWeight;
        var relevant=_source.AssetId>=0 && _source.Weight>0 && !visit.Initialize?new LyraIdleRelevant(true,_sequence.DurationSeconds,_source.Time,false,_source.PreviousValid,_source.Previous,_source.Delta):default;
        var c=_pending=Machine.Prepare(delta,visit,_predicate,relevant);
        if(c.ClearWeights.Contains(2))_nextSource=_nextSource with{Weight=0};
        if(c.Initializations[2]>0)
            _nextSource=_nextSource with{AssetId=_asset,Time=0,PublicTime=0,Marker=_nextSource.Marker with{PreviousIndex=-2,NextIndex=-2,Initialized=false},ResetPending=true};
        foreach(var u in c.Updates)if(u.State==2)
        {
            if((_stateVisited==c.Frame-1?_stateWeight:0)<=1e-5f && u.Weight>1e-5f)
            {
                var mapped=.1+(1-.1)*Math.Clamp(_nextFalling/.4,0,1);
                _nextAlpha=crouching?mapped*.5:mapped;
            }
            _nextStateVisited=c.Frame;_nextStateWeight=u.Weight;
            var alpha=(float)_nextAlpha;
            if(alpha>1e-5f)
            {
                _nextSource=_nextSource with{AssetId=_asset,Time=Math.Clamp(_nextSource.Time,0,_sequence.DurationSeconds),Weight=u.Weight*(alpha>=1-1e-5f?1:alpha),LastVisited=c.Frame,LastWeight=u.Weight,ResetPending=false};
                Players=[new(_player,_asset,_epoch,AlsAssetSyncKind.Sequence,_nextSource.Time,1,_nextSource.Weight,0,1,0,Looping:false,MarkerRecord:_nextSource.Marker,RequestedInertialization:u.Inertial)];
                Samples=[new(_player,_asset,1)];Groups=[-1];
            }
        }
        return c;
    }
    public void Resolve(LyraIdleMachineCandidate c,ReadOnlySpan<AlsAssetPlayerHistory> outputs)
    {
        Validate(c);if(_hasSync)throw new InvalidOperationException("Additive Sync already resolved.");
        var seen=false;_resolved=_nextSource;
        foreach(var o in outputs)if(o.PlayerId==_player)
        {
            if(Players.Length!=1 || seen || o.AssetId!=_asset || o.Epoch!=_epoch || o.SampleCount!=1 || !float.IsFinite(o.Time) || o.Time<0 || o.Time>_sequence.DurationSeconds || !float.IsFinite(o.DeltaPrevious) || !float.IsFinite(o.Delta))
                throw new InvalidOperationException("Foreign or duplicate additive source history.");
            seen=true;_resolved=_resolved with{Time=o.Time,PublicTime=o.Time,Previous=o.DeltaPrevious,Delta=o.Delta,PreviousValid=true,Marker=o.Marker};
        }
        if(seen!=(Players.Length==1))throw new InvalidOperationException("Missing additive Sync result.");_hasSync=true;
    }
    private void Validate(LyraIdleMachineCandidate frame)
    {if(!ReferenceEquals(frame,_pending) || _failed)throw new InvalidOperationException("Stale or failed additive candidate.");Machine.Validate(frame);}
    public LyraAdditivesPoseView Evaluate(LyraIdleMachineCandidate frame)
    {
        Validate(frame);_evaluated=false;
        try
        {
            if(!_hasSync)throw new InvalidOperationException("Additive evaluation precedes common Sync.");
            var identity=new AlsPrecisePose(AlsDoubleVector.Zero,AlsQuaternion.Identity,AlsDoubleVector.Zero);
            _recovery.AsSpan().Fill(identity);Array.Clear(_recoveryCurves);Array.Clear(_recoveryAttributes);_recoveryRoot=default;
            var alpha=(float)_nextAlpha;
            if(_resolved.AssetId>=0 && alpha>1e-5f)
            {
                _sampler.Sample(_resolved.Time,_recovery,_recoveryCurves,_recoveryAttributes);
                _recoveryRoot=LyraRootMotionAttribute.Sample(_definition,_rootSampler,_resolved.Previous,_resolved.Delta,false);
                if(alpha<1-1e-5f)
                {
                    var a=1-alpha;var b=1-a;
                    for(var n=0;n<81;n++)_recovery[n]=AlsPrecisePoseBlender.Accumulate(AlsPrecisePoseBlender.Scale(identity,a),_recovery[n],b).Normalized();
                    for(var n=0;n<_recoveryCurves.Length;n++)_recoveryCurves[n]=LyraCurveSample.Scale(_recoveryCurves[n],b);
                    for(var n=0;n<_recoveryAttributes.Length;n++)_recoveryAttributes[n]=LyraLayeredDataBlend.BlendIntegerUniform(default,_recoveryAttributes[n],b,false);
                    _recoveryRoot=LyraRootMotionAttribute.BlendUniform(default,_recoveryRoot,b,_rootOverride);
                }
            }
            Machine.Evaluate(frame,_identity,_pose,_machineCurves);Array.Clear(_curves);Array.Clear(_attributes);_root=default;
            var stack=Machine.PreparedStack(frame);
            void Copy(int state){if(state==2){_recoveryCurves.CopyTo(_curves,0);_recoveryAttributes.CopyTo(_attributes,0);_root=_recoveryRoot;}}
            if(stack.Count==0)Copy(stack.CurrentState);
            else for(var j=0;j<stack.Count;j++)
            {
                var e=stack.GetTransition(j);if(j==0)Copy(e.From);
                for(var n=0;n<_curves.Length;n++)
                    _curves[n]=LyraCurveSample.BlendStateMachine(_curves[n],e.To==2?_recoveryCurves[n]:default,e.Alpha);
                for(var n=0;n<_attributes.Length;n++)_attributes[n]=LyraLayeredDataBlend.BlendIntegerUniform(_attributes[n],e.To==2?_recoveryAttributes[n]:default,e.Alpha,false);
                _root=LyraRootMotionAttribute.BlendUniform(_root,e.To==2?_recoveryRoot:default,e.Alpha,_rootOverride);
            }
            _evaluated=true;return new(this,frame);
        }
        catch{_failed=true;throw;}
    }
    public void ValidateCommit(LyraIdleMachineCandidate frame,bool updateOnly)
    {Validate(frame);if(!_hasSync)throw new InvalidOperationException("Additive frame needs common Sync.");if(!updateOnly && frame.Visited && !_evaluated)throw new InvalidOperationException("Additive frame needs its current output.");}
    public void Commit(LyraIdleMachineCandidate frame,bool updateOnly)
    {ValidateCommit(frame,updateOnly);Machine.Commit(frame);_source=_resolved;TimeFalling=_nextFalling;LandAlpha=_nextAlpha;_stateVisited=_nextStateVisited;_stateWeight=_nextStateWeight;_pending=null;_evaluated=_hasSync=false;}
    public void Cancel(){Machine.Cancel();_pending=null;_evaluated=_failed=_hasSync=false;Players=[];Samples=[];Groups=[];}
    internal void ValidateOutput(LyraIdleMachineCandidate frame){Validate(frame);if(!_evaluated)throw new InvalidOperationException("Additive output is unavailable.");}
    internal ReadOnlySpan<AlsPrecisePose> Pose(LyraIdleMachineCandidate frame){ValidateOutput(frame);return _pose;}
    internal ReadOnlySpan<LyraCurveSample> Curves(LyraIdleMachineCandidate frame){ValidateOutput(frame);return _curves;}
    internal ReadOnlySpan<LyraAttributeSample> Attributes(LyraIdleMachineCandidate frame){ValidateOutput(frame);return _attributes;}
    internal LyraRootMotionAttribute Root(LyraIdleMachineCandidate frame){ValidateOutput(frame);return _root;}
}
