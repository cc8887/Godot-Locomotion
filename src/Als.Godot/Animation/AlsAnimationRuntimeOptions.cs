using System.Collections.Frozen;
using Godot;

namespace GodotAls.Animation;

// Resolve once on Main, before any character or process group is constructed.
// Diagnostic scenes retain their explicit flags; the normal Demo uses the full chain.
internal static class AlsAnimationRuntimeOptions
{
    private static FrozenSet<string>? _flags;
    private static bool _demoConfigured;
    private static readonly string[] CompleteDemoFlags =
    [
        "--layered-frame", "--foot-ik-frame", "--based-foot-lock",
        "--refactored-pose-curves", "--refactored-movement-curves",
        "--refactored-foot-frame", "--foot-lock-gravity-twist",
        "--foot-lock-final-contact", "--foot-ground-clearance", "--foot-contact-toes",
    ];

    internal static bool Has(string flag) =>
        (_flags ??= OS.GetCmdlineUserArgs().ToFrozenSet(StringComparer.Ordinal)).Contains(flag);

    internal static void ConfigureDemo(bool actionPreview = false)
    {
        if (_demoConfigured) return;
        if (_flags is not null)
            throw new InvalidOperationException("Configure the Demo before constructing animation runtimes.");
        var arguments = OS.GetCmdlineUserArgs();
        _flags = (arguments.Contains("--legacy-animation")
            ? arguments : arguments.Concat(CompleteDemoFlags).Concat(actionPreview || arguments.Contains("--action-preview")
                ? [] : new[] { "--rolling-gameplay", "--montage-root-motion" })).ToFrozenSet(StringComparer.Ordinal);
        _demoConfigured = true;
    }
}
