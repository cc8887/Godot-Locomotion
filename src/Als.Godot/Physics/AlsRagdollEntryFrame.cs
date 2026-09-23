using Godot;
using GodotAls.Core.Contracts;

namespace GodotAls.Physics;

// Main-thread entry metadata for the same committed logical FBX pose copied by
// the character. Character velocity is not a derived per-bone/COM velocity and
// must not silently replace the native kinematic-to-dynamic velocity policy.
internal readonly record struct AlsRagdollEntryFrame(AlsFrameIdentity Identity,
    Transform3D CharacterToWorld, Transform3D SkeletonToWorld, Vector3 CharacterVelocity);
