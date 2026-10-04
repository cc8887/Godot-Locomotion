using Godot;
namespace GodotAls.Animation.Lyra;

// Fixture receiver checks the actor-before-components part of the original
// interface. Production actors opt in by implementing the same typed contract.
public partial class LyraContextOracleActor:Node3D,ILyraContextEffectsReceiver
{
    internal event Action<LyraContextEffectsMessage>? Received;
    void ILyraContextEffectsReceiver.AnimMotionEffect(in LyraContextEffectsMessage message,LyraContextEffectsCatalog catalog,bool libraryLoaded)=>Received?.Invoke(message);
}
