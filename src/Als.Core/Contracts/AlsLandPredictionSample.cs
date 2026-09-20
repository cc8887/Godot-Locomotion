using System.Numerics;
using System.Runtime.InteropServices;

namespace GodotAls.Core.Contracts;

// A main-thread capsule query snapshot. No physics object crosses the Worker boundary.
[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsLandPredictionSample(byte Queried, byte BlockingHit, byte StartedPenetrating,
    byte Walkable, float Time, float SafeTime, Vector3 Position, Vector3 Normal, long ColliderId,
    Vector3 TraceStart, Vector3 TraceMotion, float Radius, float HalfHeight);
