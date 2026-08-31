using System.Runtime.InteropServices;
using GodotAls.Core.Contracts;

namespace GodotAls.Core.Events;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct AlsP5FailureRecord(
    AlsFrameIdentity Identity,
    AlsP5FailureCode Code,
    ulong LastCommittedResultDigest,
    uint AttemptOrdinal);
