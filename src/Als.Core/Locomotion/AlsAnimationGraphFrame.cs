using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

// Traversal counts belong to the animation instance, not to FrameId modulo an
// arbitrary number. A new character generation starts a new instance. Root
// reinitialization or a changed skeleton must be handled by the enclosing owner.
public readonly record struct AlsAnimationGraphFrame(AlsFrameIdentity Identity, AlsGraphTraversalCounter Initialization,
    AlsGraphTraversalCounter Bones, AlsGraphTraversalCounter Update, AlsGraphTraversalCounter Evaluation)
{
    public AlsAnimationGraphFrame Next(AlsFrameIdentity identity, ulong globalFrame)
    {
        if (identity.SlotGeneration==0 || globalFrame==ulong.MaxValue || Identity!=default &&
            (Identity.CharacterId!=identity.CharacterId || Identity.SlotGeneration!=identity.SlotGeneration || identity.FrameId<=Identity.FrameId ||
                globalFrame<Update.GlobalFrame)) throw new ArgumentException("Invalid animation graph traversal frame.");
        var initialize=Identity==default ? default(AlsGraphTraversalCounter).Next(globalFrame) : Initialization;
        var bones=Identity==default ? default(AlsGraphTraversalCounter).Next(globalFrame) : Bones;
        return new(identity,initialize,bones,Update.Next(globalFrame),Evaluation.Next(globalFrame));
    }
    public void Validate(AlsFrameIdentity identity)
    {
        if (Identity!=identity || !Initialization.HasUpdated || !Bones.HasUpdated || !Update.HasUpdated || !Evaluation.HasUpdated)
            throw new ArgumentException("Incomplete animation graph traversal frame.");
    }
}
