using System.Globalization;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsRefactoredLocomotionHostProfile
{
    public AlsRefactoredCharacterActionProfile Actions {get;}
    public AlsRefactoredLocomotionPoseProfile Pose {get;}
    public AlsRefactoredAirSettings Air {get;}
    internal AlsSequenceMontageCommand LandStanding {get;}
    internal AlsSequenceMontageCommand LandCrouching {get;}
    internal float StopDuration {get;}
    public AlsRefactoredLocomotionHostProfile(AlsRefactoredCharacterActionProfile actions,string airJson,string machines)
        :this(actions,new AlsRefactoredAirSettings(airJson,actions.Standing.Catalog,actions.Standing.MovementSettings),machines){}
    public AlsRefactoredLocomotionHostProfile(AlsRefactoredCharacterActionProfile actions,AlsRefactoredAirSettings air,string machines,
        AlsRefactoredLocomotionPoseProfile? pose=null)
    {
        if(actions.Grounded is null||air.CatalogDigest!=actions.CatalogDigest)throw new ArgumentException("Incomplete Locomotion Parent resources.");
        Actions=actions;Air=air;Pose=pose??new(actions.Standing.Catalog,actions.Standing.Triangles,machines,actions.CurveNames);
        if(Pose.CatalogDigest!=actions.CatalogDigest||!Pose.GroundedCurveNames.SequenceEqual(actions.CurveNames)||
            !Pose.BoneNames.SequenceEqual(actions.BoneNames)||!Pose.Parents.SequenceEqual(actions.Parents))throw new ArgumentException("Foreign Locomotion pose boundary.");
        var source=AlsRefactoredLocomotionResources.Source;var text=actions.Standing.Catalog.Read(source).GetProperty("nativeText").GetString()!;
        AlsYawOffsetCompiler.Graph Graph(string name)=>new(AlsNativeNestedGraph.Extract(text,source,source+":"+name),true);
        var graph=Graph("EventGraph");var call=graph.Nodes.Single(n=>n.Member=="PlayTransitionRightAnimation");
        graph.Function(call,"PlayTransitionRightAnimation","ALS.AlsAnimationInstance");
        var entry=graph.Nodes.Single(n=>n.Kind=="K2Node_Event");
        if(!entry.Body.Contains("MemberName=\"AnimNotify_LandToGrounded\"",StringComparison.Ordinal))throw new ArgumentException("Foreign landing notify.");
        graph.Links(call,"execute",(entry,"then"));Parent(graph,call);
        if(graph.Literal(call,"bFromStandingIdleOnly")!="false")throw new ArgumentException("Landing idle gate differs.");
        float Number(string pin)=>float.Parse(graph.Literal(call,pin),CultureInfo.InvariantCulture);
        AlsSequenceMontageCommand Command(int side)=>new(actions.Standing.QuickStop.Assets[side].AnimationId,AlsMontageSlot.Transition,
            Number("PlayRate"),Number("StartTime"),Number("BlendInDuration"),Number("BlendOutDuration"));
        LandStanding=Command(1);LandCrouching=Command(3);
        graph=Graph("StopTransitionAndTurnInPlaceAnimations");call=graph.Nodes.Single(n=>n.Kind=="K2Node_CallFunction"&&n.Member=="StopTransitionAndTurnInPlaceAnimations");
        graph.Function(call,"StopTransitionAndTurnInPlaceAnimations","ALS.AlsAnimationInstance");Parent(graph,call);
        graph.Links(call,"execute",(graph.Nodes.Single(n=>n.Kind=="K2Node_FunctionEntry"),"then"));
        StopDuration=float.Parse(graph.Literal(call,"BlendOutDuration"),CultureInfo.InvariantCulture);
    }
    private static void Parent(AlsYawOffsetCompiler.Graph graph,AlsYawOffsetCompiler.Node call)
    {
        var (parent,pin)=graph.Follow(call,"self");graph.Self(parent);
        if(parent.Member!="GetParent"||pin.Name!="ReturnValue"||call.Pins.Values.Single(p=>p.Name=="then").Links!=""||
            graph.Nodes.Count(n=>n.Kind!="EdGraphNode_Comment")!=3)throw new ArgumentException("Locomotion action closure differs.");
    }
    public AlsRefactoredLocomotionHost CreateRuntime(uint character,uint generation)=>new(this,character,generation);
}

public readonly record struct AlsRefactoredLocomotionHostInput(AlsRefactoredStandingHostInput Grounded,
    AlsRefactoredLocomotionMode Mode,bool JumpRequested,bool HasInput,bool RelativeLocation,float Prediction,
    float PreviousStanding,float PreviousCrouching,bool FromRoll=false);

/// <summary>One character transaction through Grounded, Transition, Locomotion
/// and original Parent callbacks. Prediction is gathered before workers;
/// deferred landing notifies are consumed after Parent PostUpdate.</summary>
public sealed class AlsRefactoredLocomotionHost : IAlsRefactoredLocomotionPoseSink
{
    private readonly AlsRefactoredLocomotionHostProfile _p;
    public AlsRefactoredCharacterActionRuntime Actions {get;}
    public AlsRefactoredLocomotionPoseRuntime Graph {get;}
    private AlsRefactoredLocomotionHostInput _input;
    private AlsPoseUpdateContext _context;
    private AlsGraphTraversalCounter _initialization;
    private bool _prepared,_visited,_post,_land;
    private long _attachParent;
    private float _teleportDistance;
    public AlsFrameIdentity CommittedIdentity=>Actions.CommittedIdentity;
    public AlsRefactoredInAirState Air=>Actions.MovementParent.AirCandidate;
    public AlsRefactoredMovementState Movement=>Actions.MovementParent.MovementCandidate;
    public ReadOnlySpan<AlsPrecisePose> Pose=>Graph.Pose;
    public ReadOnlySpan<AlsInertialCurve> Curves=>Graph.Curves;
    internal AlsRefactoredLocomotionHost(AlsRefactoredLocomotionHostProfile profile,uint character,uint generation)
    {_p=profile;Actions=profile.Actions.CreateRuntime(character,generation);Graph=new(profile.Pose,profile.Actions.Standing.Sync);}
    public void BeginGlobal(in AlsPoseUpdateContext context,in AlsRefactoredLocomotionHostInput input,
        AlsGraphTraversalCounter initialization,bool initialize=false)
    {
        if(_prepared||context.UpdateCounter is not{HasUpdated:true}||!initialization.HasUpdated||!Enum.IsDefined(input.Mode)||
            !float.IsFinite(input.PreviousStanding)||!float.IsFinite(input.PreviousCrouching))throw new ArgumentException("Invalid Locomotion host frame.");
        try
        {
            Actions.BeginGlobal(context,input.Grounded,initialize);
            Actions.MovementParent.PrepareInAir(context.Identity.FrameId,_p.Air,input.JumpRequested,input.Prediction,input.Grounded.Rest.GameWorld);
            _context=context;_input=input;_initialization=initialization;_prepared=true;
        }
        catch{Discard();throw;}
    }
    public void Prepare(in AlsPoseUpdateContext context,bool initialize=false,AlsGraphTraversalCounter? initialization=null,bool? fromRoll=null)
    {
        Validate(context);Actions.ValidateUpdate(context);if(_visited)throw new InvalidOperationException("Repeated Locomotion graph traversal.");
        if(initialization is {} value){if(!value.HasUpdated)throw new ArgumentException("Invalid initialization counter.");_initialization=value;}
        if(fromRoll is {} roll)_input=_input with{FromRoll=roll};
        try{Graph.Prepare(context,this,initialize);_visited=true;}catch{Discard();throw;}
    }
    public void Evaluate(in AlsPrecisePose component, long attachParent = 0, float teleportDistance = 0)
    {
        Actions.ValidateUpdate(_context);if(!_visited)throw new InvalidOperationException("Unvisited Locomotion graph.");
        try{_attachParent=attachParent;_teleportDistance=teleportDistance;Graph.Evaluate(component,attachParent,teleportDistance);}catch{Discard();throw;}
    }
    AlsRefactoredAirPoseInput IAlsRefactoredLocomotionPoseSink.Input
    {
        get
        {
            var air=Air;var rotate=Actions.RestParent.Candidate.Rotate;var g=_input.Grounded;
            return new(new(_input.Mode,g.Rest.Stance==AlsRefactoredRestStance.Crouching?AlsRefactoredGroundedStance.Crouching:AlsRefactoredGroundedStance.Standing,
                air.Jumped,_input.HasInput,rotate.Left,rotate.Right,_input.RelativeLocation,g.Movement.Speed,g.FootPlanted),
                air.VerticalVelocity,air.Prediction,air.JumpRate,Movement.Lean,g.Rest.Rotation==AlsRefactoredRestRotation.Aiming);
        }
    }
    public void Validate(in AlsPoseUpdateContext context)
    {
        if(!_prepared||context.Identity!=_context.Identity||context.Delta!=_context.Delta||Actions.Frame.Identity!=context.Identity)
            throw new ArgumentException("Foreign Locomotion Parent candidate.");
    }
    void IAlsRefactoredLocomotionPoseSink.Callback(string function,in AlsPoseUpdateContext context)
    {
        Actions.ValidateUpdate(context);
        switch(function)
        {
            case "InitializeLean":Actions.MovementParent.InitializeLean(context.Identity.FrameId);break;
            case "RefreshInAir":Actions.MovementParent.RefreshInAir(context.Identity.FrameId);break;
            case "RefreshRotateInPlace":Actions.RestParent.RefreshRotateInPlace(context.Identity.FrameId);break;
            default:throw new ArgumentException("Foreign Locomotion callback.");
        }
    }
    void IAlsRefactoredLocomotionPoseSink.StateCallback(in AlsRefactoredLocomotionCallback callback,in AlsPoseUpdateContext context)
    {
        Actions.ValidateUpdate(context);
        if(callback.State!=0||callback.Entry||callback.Function!="StopTransitionAndTurnInPlaceAnimations")throw new ArgumentException("Foreign Locomotion state callback.");
        Actions.Queue.QueueStop(_p.StopDuration);
    }
    void IAlsRefactoredLocomotionPoseSink.Notify(in AlsGroundedMachineEvent notification,in AlsPoseUpdateContext context)
    {Actions.ValidateUpdate(context);if(notification.NotifyIndex!=0)throw new ArgumentException("Foreign Locomotion notify.");_land=true;}
    void IAlsRefactoredLocomotionPoseSink.PrepareGrounded(in AlsPoseUpdateContext context,bool initialize)
    {
        Actions.Transition.Prepare(context,initialize);var source=Actions.Transition.SourceUpdate;
        if(Actions.Transition.InertializationRequest is {} request)Graph.RequestOuterInertia(context.Identity,request.Duration);
        if(source.Updated)Actions.Grounded!.Prepare(source.Context,_input.Grounded,_input.PreviousStanding,_input.PreviousCrouching,
            _input.FromRoll,_initialization,initialize);
    }
    void IAlsRefactoredLocomotionPoseSink.EvaluateGrounded(in AlsPrecisePose component)
    {
        var source=Actions.Transition.SourceUpdate;if(source.Updated)Actions.Grounded!.Evaluate(component,_attachParent,_teleportDistance);
        Actions.Transition.Evaluate(source.Updated?Actions.Grounded!.Pose:[],source.Updated?Actions.Grounded!.Curves:[]);
    }
    ReadOnlySpan<AlsPrecisePose> IAlsRefactoredLocomotionPoseSink.GroundedPose=>Actions.Transition.Pose;
    ReadOnlySpan<AlsInertialCurve> IAlsRefactoredLocomotionPoseSink.GroundedCurves=>Actions.Transition.Curves;
    public void PostUpdate()
    {
        Validate(_context);if(_post)throw new InvalidOperationException("Repeated Locomotion PostUpdate.");
        try
        {
            Actions.PostUpdateActions();
            if(_land)
            {
                var crouch=_input.Grounded.Rest.Stance==AlsRefactoredRestStance.Crouching;
                Actions.Queue.PlayImmediate(crouch?_p.LandCrouching:_p.LandStanding,
                    crouch?"Als.Stance.Crouching":"Als.Stance.Standing",_input.Grounded.MovingSmooth);
            }
            _post=true;
        }
        catch{Discard();throw;}
    }
    public void ValidateCommit(in AlsFrameIdentity identity)
    {Validate(_context);if(!_post||identity!=_context.Identity)throw new ArgumentException("Incomplete Locomotion transaction.");Actions.ValidateCommit(identity);if(_visited)Graph.ValidateCommit(identity);}
    public void Commit(in AlsFrameIdentity identity)
    {ValidateCommit(identity);if(_visited)Graph.Commit(identity);Actions.Commit(identity);Clear();}
    public void Discard(){Graph.Cancel();Actions.Discard();Clear();}
    private void Clear(){_prepared=_visited=_post=_land=false;}
}
