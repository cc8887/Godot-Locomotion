using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;

namespace GodotAls.Animation;

// Value-only output of the physical montage tick, before Motor and graph inputs.
internal readonly record struct AlsPreparedRootMotion(AlsFrameIdentity Identity,
    AlsMontageRootMotionRange Source, AlsRootMotionDelta Delta);
