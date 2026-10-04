using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraMainLeftHandCandidate(LyraMainLocomotionCandidate Main,LyraLeftHandLayerCandidate Left);

// Actual Main boundary immediately after LocomotionSM's linked input-pose
// call. Later caches/upper-body/Slots/Aiming/inertia/IK are still downstream.
internal sealed class LyraMainLeftHandHost
{
    private readonly LyraLogicalSourceBank _bank;
    private LyraMainLeftHandCandidate? _pending;
    private LyraLeftHandPoseView _output;
    private LyraAdditivesPoseView _additiveOutput;
    private readonly bool _includeAdditives;
    private bool _evaluated;
    public LyraMainLocomotionHost Main {get;}
    public LyraItemLayerGraphInstance Layers=>Main.Layers;
    public LyraMainLeftHandHost(LyraLocomotionResources resources,string profile,int playerBase=0,int leanPlayerBase=1000,long epoch=1,bool includeAdditives=false)
    {_bank=resources.Catalog.Bank;Main=resources.CreateMainHost(profile,playerBase,leanPlayerBase,epoch);_includeAdditives=includeAdditives;}
    public LyraMainLeftHandCandidate Prepare(in LyraMainUpdateInput input,float delta,LyraLocomotionMachineVisit visit,
        double hipWeight,AlsPrecisePose component,AlsQuaternion relativeRotation,AlsStopMovementSnapshot movement,double groundDistance,
        bool melee=false,bool pivotNotify=false)
    {
        if(_pending is not null)throw new InvalidOperationException("Main left-hand frame is pending.");_evaluated=false;
        try
        {
            var main=Main.Prepare(input,delta,visit,hipWeight,component,relativeRotation,movement,groundDistance,melee,pivotNotify,
                updateLeftHand:true,updateAdditives:_includeAdditives);
            return _pending=new(main,main.LeftHand!);
        }
        catch{Cancel();throw;}
    }
    private void Validate(LyraMainLeftHandCandidate c)
    {if(!ReferenceEquals(c,_pending))throw new InvalidOperationException("Stale Main left-hand frame.");}
    public void Evaluate(LyraMainLeftHandCandidate c)
    {
        Validate(c);_evaluated=false;
        try
        {
            Main.Evaluate(c.Main);
            var input=new LyraLayerPoseInput(_bank,Main.Pose,Main.Curves,Main.Attributes,Main.RootMotion);
            _output=Layers.EvaluateLeftHand(c.Main.Sources,c.Left,Layers.Call(LyraLayerHook.LeftHandPose_OverrideState),input);_evaluated=true;
            if(c.Main.Additives is {} additive)
                _additiveOutput=Layers.EvaluateAdditives(c.Main.Sources,additive,Layers.Call(LyraLayerHook.FullBodyAdditives));
        }
        catch{Cancel();throw;}
    }
    private void Output(){if(_pending is null || !_evaluated)throw new InvalidOperationException("Main left-hand output is unavailable.");}
    public ReadOnlySpan<AlsPrecisePose> Pose {get{Output();return _output.Pose;}}
    public ReadOnlySpan<LyraCurveSample> Curves {get{Output();return _output.Curves;}}
    public ReadOnlySpan<LyraAttributeSample> Attributes {get{Output();return _output.Attributes;}}
    public LyraRootMotionAttribute RootMotion {get{Output();return _output.RootMotion;}}
    // Separate additive output for its original later Main call site. It is
    // not prematurely applied to the current LeftHand absolute boundary.
    public LyraAdditivesPoseView Additives {get{Output();if(!_includeAdditives)throw new InvalidOperationException("Main additive entry is disabled.");return _additiveOutput;}}
    public void StageFinalFeedback(LyraMainLeftHandCandidate c,ReadOnlySpan<LyraCurveSample> finalCurves,
        ReadOnlySpan<LyraNamedCurveSample> controlCurves=default)
    {Validate(c);Output();Main.StageFinalFeedback(c.Main,finalCurves,controlCurves);}
    public void ValidateCommit(LyraMainLeftHandCandidate c,bool updateOnly=false){Validate(c);Main.ValidateCommit(c.Main,updateOnly);}
    public void Commit(LyraMainLeftHandCandidate c,bool updateOnly=false)
    {ValidateCommit(c,updateOnly);Main.Commit(c.Main,updateOnly);_pending=null;_evaluated=false;}
    public void Cancel(){Main.Cancel();_pending=null;_evaluated=false;}
}
