using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Physics;

// Immutable candidate at a completed physics boundary. Kept separate from the
// actual exit: callers must validate the activation AND step again before use.
internal sealed record AlsRagdollRecoveryFrame(AlsFrameIdentity ActivationIdentity,
    long CompletedSteps, AlsRagdollExitDecision Decision, Transform3D SkeletonToWorld,
    AlsNamedPoseSnapshot Snapshot);
