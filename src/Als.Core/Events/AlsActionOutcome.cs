using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsActionOutcome(
    long RequestId,
    int ActionDefinitionId,
    long PlaybackEpoch,
    AlsActionResultCode ResultCode);
