namespace GodotAls.Animation.Lyra;

// The compiled Lyra interface places all fourteen hooks in ItemAnimLayers.
// These operators hold subgraph state within one linked-instance lifetime.
internal sealed class LyraItemLayerInstance
{
    public LyraItemLayerInstance(LyraBoundRig rig, ILyraItemAnimationLayers provider,
        LyraLinkedLayerContracts contracts, LyraLogicalSourceBank? logical = null,
        LyraItemLayerGraphInstance? graphExecution = null)
    {
        Contract = contracts.Get(provider.AnimationClassPath);
        if (graphExecution is not null && graphExecution.ClassPath != Contract.ClassPath)
            throw new InvalidOperationException("Layer operators and graph execution have different provider owners.");
        GraphExecution = graphExecution;
        Aiming = provider.CreateAimingLayer(rig);
        HipFire = provider.CreateHipFireLayer(rig);
        LeftHand = provider.CreateLeftHandPoseLayer(rig);
        Additives = provider.CreateFullBodyAdditivesLayer(rig);
        HandRetarget = provider.CreateHandRetargetLayer(rig);
        RightHandIk = provider.CreateRightHandIkLayer(rig);
        PlaybackDefaults = provider.PlaybackDefaults;
        if (logical is not null)
        {
            HipFire.ConfigureLogical(logical); LeftHand.ConfigureLogical(logical);
            Aiming.ConfigureLogical(logical); HandRetarget.ConfigureLogical(logical);
            RightHandIk.ConfigureLogical(logical);
            LeftHandIk = new(logical, LyraLinkedLayerInventory.Load().GetByClass(provider.AnimationClassPath).DisableHandIk);
        }
        // UE starts from the class defaults and ORs the call-site flags for the group.
        ReceiveNotifies = Contract.ReceiveNotifies || contracts.CallSites.Any(v => v.ReceiveNotifies);
        PropagateNotifies = Contract.PropagateNotifies || contracts.CallSites.Any(v => v.PropagateNotifies);
    }

    public LyraLinkedLayerClassContract Contract { get; }
    public LyraItemLayerGraphInstance? GraphExecution { get; }
    public LyraUnarmedLayerDefaults PlaybackDefaults { get; }
    public ILyraAimingLayer Aiming { get; }
    public LyraHipFirePoseLayer HipFire { get; }
    public LyraLeftHandPoseLayer LeftHand { get; }
    public LyraFullBodyAdditivesLayer Additives { get; }
    public LyraHandRetargetPoseLayer HandRetarget { get; }
    public LyraRightHandIkPoseLayer RightHandIk { get; }
    public LyraLeftHandIkPoseLayer? LeftHandIk { get; }
    public bool ReceiveNotifies { get; }
    public bool PropagateNotifies { get; }
}
