using GodotAls.Core.Contracts;

namespace GodotAls.Core.Actions;

public readonly record struct AlsMantlingBranchInputs(bool HasInput,string LocomotionMode,string RotationMode,string Stance);
public readonly record struct AlsMantlingBranchDefinition(int ActionDefinitionId,float ActionStart,float ActionEnd,
    float EarlyStart,float EarlyEnd,float BlendSeconds,bool CheckInput,bool CheckMode,bool CheckRotation,bool CheckStance,
    string ActionTag,string ModeTag,string RotationTag,string StanceTag);
public readonly record struct AlsMantlingBranchEvent(long InstanceId,int StateIndex,bool Begin,float Position);

/// <summary>Candidate-only effects for the two ALS native branching states. Single terminal
/// section, positive playback, no callback-driven jumps. Bound to one physical montage bank.</summary>
public sealed class AlsMantlingBranchingRuntime
{
    private readonly Dictionary<int,AlsMantlingBranchDefinition> _definitions;
    private readonly record struct Active(int Definition,byte State);
    private Dictionary<long,Active> _committed=new(),_candidate=new();
    private readonly List<AlsMantlingBranchEvent> _events=new(16);
    private AlsMantlingBranchInputs _inputs;
    private string _action="",_capturedAction="",_nextAction="";
    private bool _prepared;
    private bool _attached;
    private AlsFrameIdentity _identity;
    public string CommittedAction=>_action;
    public string CandidateAction=>_prepared?_nextAction:throw new InvalidOperationException("No branch candidate.");
    public ReadOnlySpan<AlsMantlingBranchEvent> CandidateEvents=>_prepared?System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_events):throw new InvalidOperationException("No branch candidate.");
    public AlsMantlingBranchingRuntime(ReadOnlySpan<AlsMantlingBranchDefinition> definitions)
    {
        _definitions=new();
        foreach(var d in definitions)
        {
            if(d.ActionDefinitionId<0||!float.IsFinite(d.ActionStart)||!float.IsFinite(d.ActionEnd)||
                !float.IsFinite(d.EarlyStart)||!float.IsFinite(d.EarlyEnd)||!float.IsFinite(d.BlendSeconds)||d.BlendSeconds<0||
                d.ActionEnd<=d.ActionStart||d.EarlyStart<=d.ActionEnd||d.EarlyEnd<=d.EarlyStart||
                string.IsNullOrWhiteSpace(d.ActionTag)||string.IsNullOrWhiteSpace(d.ModeTag)||
                string.IsNullOrWhiteSpace(d.RotationTag)||string.IsNullOrWhiteSpace(d.StanceTag)||!_definitions.TryAdd(d.ActionDefinitionId,d))
                throw new ArgumentException("Unsupported mantle branching-state layout.");
        }
        _inputs=new(false,"","","");
    }
    public void Capture(AlsMantlingBranchInputs inputs,string? currentAction=null)
    {
        if(_prepared)throw new InvalidOperationException("Capture branch inputs before montage Begin.");
        if(inputs.LocomotionMode is null||inputs.RotationMode is null||inputs.Stance is null)throw new ArgumentException("Missing captured gameplay tags.");
        _inputs=inputs;_capturedAction=currentAction??_action;
    }
    internal void Attach(ReadOnlySpan<AlsAuthoredMontageAsset> assets)
    {
        if(_attached)throw new ArgumentException("A branching owner cannot be shared by montage banks.");
        foreach(var definition in _definitions.Values)
        {
            var found=false;
            foreach(var asset in assets)
                if(asset.ActionDefinitionId==definition.ActionDefinitionId)
                {
                    if(asset.Slot!=AlsMontageSlot.PostLocomotion||asset.RootMotionEnabled||definition.EarlyEnd>asset.Duration||
                        asset.Lifecycle.BlendOutTriggerSeconds!=0)
                        throw new ArgumentException("Branching definition differs from its mantle montage.");
                    found=true;break;
                }
            if(!found)throw new ArgumentException("Branching action is not in the montage bank.");
        }
        _attached=true;
    }
    internal void Begin(AlsFrameIdentity identity)
    {
        if(_prepared)throw new InvalidOperationException("Branch candidate already prepared.");
        _identity=identity;_candidate.Clear();foreach(var item in _committed)_candidate.Add(item.Key,item.Value);
        _nextAction=_capturedAction;_events.Clear();_prepared=true;
    }
    internal int CopyCrossedMarkers(int action,float previous,float end,Span<float> output)
    {
        if(!_definitions.TryGetValue(action,out var d))return 0;
        Span<float> markers=stackalloc float[]{d.ActionStart,d.ActionEnd,d.EarlyStart,d.EarlyEnd};
        var count=0;foreach(var marker in markers)if(previous<marker&&marker<end)output[count++]=marker;
        return count;
    }
    internal float Advance(in AlsMontageInstance instance,float previous,float end,bool terminated)
    {
        if(!_definitions.TryGetValue(instance.ActionDefinitionId,out var definition))return -1;
        var id=instance.InstanceId;_candidate.TryGetValue(id,out var saved);var active=saved.State;
        if(!instance.Interrupted&&previous!=end)
        {
            if(end<previous)throw new ArgumentException("Mantle branching requires forward playback.");
            Span<float> markers=stackalloc float[]{definition.ActionStart,definition.ActionEnd,definition.EarlyStart,definition.EarlyEnd};
            for(var marker=0;marker<markers.Length;marker++)
            {
                var time=markers[marker];if(time<=previous||time>end)continue;
                Update(time);
                // Native UpdateActive runs first; strict start / inclusive end means
                // the marker itself supplies Begin at start and End at end.
                var state=marker/2;
                if((marker&1)==0)Enter(state,time);else Exit(state,time);
            }
            if(!markers.Contains(end))Update(end);
        }
        if(terminated)
        {
            Exit(1,end);Exit(0,end);_candidate.Remove(id);return -1;
        }
        if(active==0)_candidate.Remove(id);else _candidate[id]=new(instance.ActionDefinitionId,active);
        // Native Tick is once after all movement substeps and after termination,
        // not at the early-state Begin marker. Interrupted fades retain states but do not tick.
        return !instance.Interrupted&&(active&2)!=0&&
            (definition.CheckInput&&_inputs.HasInput||definition.CheckMode&&Equal(_inputs.LocomotionMode,definition.ModeTag)||
             definition.CheckRotation&&Equal(_inputs.RotationMode,definition.RotationTag)||definition.CheckStance&&Equal(_inputs.Stance,definition.StanceTag))
            ?definition.BlendSeconds:-1;

        void Update(float position)
        {
            var action=position>definition.ActionStart&&position<=definition.ActionEnd;
            var early=position>definition.EarlyStart&&position<=definition.EarlyEnd;
            if(!early)Exit(1,position);if(!action)Exit(0,position);
            if(action&&(active&1)==0)Enter(0,position);if(early&&(active&2)==0)Enter(1,position);
        }
        void Enter(int state,float position)
        {
            active|=(byte)(1<<state);_events.Add(new(id,state,true,position));
            if(state==0)_nextAction=definition.ActionTag;
        }
        void Exit(int state,float position)
        {
            if((active&(1<<state))==0)return;
            active&=(byte)~(1<<state);_events.Add(new(id,state,false,position));
            if(state==0&&Equal(_nextAction,definition.ActionTag))_nextAction="";
        }
    }
    internal void ValidateCommit(AlsFrameIdentity identity)
    {if(!_prepared||identity!=_identity)throw new InvalidOperationException("Foreign branch commit.");}
    internal void Commit(){(_committed,_candidate)=(_candidate,_committed);_action=_capturedAction=_nextAction;_prepared=false;}
    internal void Discard(){_prepared=false;_candidate.Clear();_events.Clear();}
    internal void Clear()
    {
        _action=_capturedAction;
        foreach(var item in _committed.Values)
            if((item.State&1)!=0&&Equal(_definitions[item.Definition].ActionTag,_action))_action="";
        _capturedAction=_action;_committed.Clear();_candidate.Clear();_events.Clear();
    }
    private static bool Equal(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
}
