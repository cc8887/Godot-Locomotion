using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

// State belonging to ABP_Mannequin_Base survives replacement of every linked
// provider. A scope borrows a frame; retiring it cannot cancel another scope.
internal sealed class LyraMainGraphStateOwner
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly AlsAssetSyncSequence[] _sequences;
    private readonly int _leanPlayerBase, _leanAssetId, _leanSequenceBase;
    private object? _character, _frameWriter;
    internal bool HasStarted { get; private set; }
    internal bool IsClaimed => _character is not null;
    internal LyraLogicalSourceBank Layout=>_bank;
    internal LyraMainUpdateHost Update { get; } = new();
    internal LyraMainLeanCompositionHost Lean { get; }
    internal LyraMainGraphState RootState { get; private set; }
    internal double TurnYaw { get; private set; }
    internal (float Remaining, float Weight) Feedback { get; private set; }

    internal LyraMainGraphStateOwner(LyraLogicalSourceBank bank, AlsAssetSyncSequence[] sequences,
        int leanPlayerBase, int leanAssetId, int leanSequenceBase, long epoch)
    {
        _bank=bank; _sequences=sequences; _leanPlayerBase=leanPlayerBase;
        _leanAssetId=leanAssetId; _leanSequenceBase=leanSequenceBase;
        Lean=new(bank,sequences,leanPlayerBase,leanAssetId,leanSequenceBase,epoch);
    }

    internal void ValidateResources(LyraLogicalSourceBank bank, AlsAssetSyncSequence[] sequences,
        int leanPlayerBase, int leanAssetId, int leanSequenceBase)
    {
        if(!ReferenceEquals(bank,_bank)||!ReferenceEquals(sequences,_sequences)||
            leanPlayerBase!=_leanPlayerBase||leanAssetId!=_leanAssetId||leanSequenceBase!=_leanSequenceBase)
            throw new InvalidOperationException("Main state belongs to a different source address space.");
    }

    internal void ClaimCharacter(object character)
    {
        if(_character is not null&&!ReferenceEquals(character,_character))
            throw new InvalidOperationException("Main state belongs to another character.");
        _character=character;
    }

    internal void RequireIdle()
    {
        if(_frameWriter is not null||Update.HasPending)
            throw new InvalidOperationException("Main state has a pending character frame.");
    }

    internal LyraMainUpdateCandidate Prepare(object writer,in LyraMainUpdateInput input,float delta)
    {
        RequireIdle();
        var candidate=Update.Prepare(input,delta);
        _frameWriter=writer; HasStarted=true;
        return candidate;
    }

    internal void Validate(object writer,LyraMainUpdateCandidate candidate,int? mode=null)
    {
        if(!ReferenceEquals(_frameWriter,writer))
            throw new InvalidOperationException("Foreign Main frame writer.");
        Update.ValidateCommit(candidate,mode);
    }

    internal LyraMainUpdateCandidate ApplyRootYaw(object writer,LyraMainUpdateCandidate candidate,double yaw)
    { Validate(writer,candidate); return Update.ApplyGraphRootYaw(candidate,yaw); }

    internal void Commit(object writer,LyraMainUpdateCandidate candidate,LyraMainLeanCompositionResolved lean,
        int mode,LyraMainGraphState rootState)
    {
        Validate(writer,candidate,mode); Lean.ValidateCommit(lean);
        Lean.Commit(lean); Update.Commit(candidate,mode); RootState=rootState; _frameWriter=null;
    }

    // Enclosing Locomotion commits these only after validating all providers.
    internal void CommitFeedback(double turnYaw,(float Remaining,float Weight)? feedback)
    { RequireIdle(); TurnYaw=turnYaw; if(feedback is {} value)Feedback=value; }

    // An executable empty self root cuts off the complete source/cache branch.
    // Commit Main's worker observation without preparing or initializing Lean,
    // changing linked/root histories, or inventing an empty provider instance.
    internal void ValidateUntraversed(object writer,LyraMainUpdateCandidate candidate)
    {
        Validate(writer,candidate);
        if(Lean.HasPending)throw new InvalidOperationException("Untraversed Main frame contains source work.");
    }
    internal void CommitUntraversed(object writer,LyraMainUpdateCandidate candidate)
    {
        ValidateUntraversed(writer,candidate);
        Update.Commit(candidate);_frameWriter=null;
    }

    internal void Cancel(object writer)
    {
        if(!ReferenceEquals(_frameWriter,writer))return;
        Lean.Cancel(); Update.Cancel(); _frameWriter=null;
    }
}
