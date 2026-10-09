using System.Collections.Frozen;
using Godot;
using System.Text.Json;

namespace GodotAls.Animation;

// Resolve once on Main, before any character or process group is constructed.
// Diagnostic scenes retain their explicit flags; the normal Demo uses the full chain.
internal static class AlsAnimationRuntimeOptions
{
    private static FrozenSet<string>? _flags;
    private static bool _demoConfigured;
    private static bool? _sampleRootMotion;
    private static readonly Lazy<bool> RootMotionSetting = new(() =>
    {
        var args = OS.GetCmdlineUserArgs();
        var enabled = args.Contains("--enable-root-motion"); var disabled = args.Contains("--disable-root-motion");
        if (enabled && disabled) throw new ArgumentException("Select one root motion override.");
        if (enabled || disabled) return enabled;
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/als_animation_settings.json"));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1) throw new ArgumentException("Unsupported ALS animation settings.");
        return document.RootElement.GetProperty("enableRootMotion").GetBoolean();
    });
    internal static bool RootMotionEnabled => RootMotionSetting.Value;
    // Native diagnostic entries keep their authored sampling unless explicitly overridden.
    internal static bool SampleRootMotion => _sampleRootMotion ??= (!_demoConfigured && !OS.GetCmdlineUserArgs().Any(a => a is "--enable-root-motion" or "--disable-root-motion") || RootMotionEnabled);
    private static readonly string[] CompleteDemoFlags =
    [
        "--layered-frame", "--foot-ik-frame", "--based-foot-lock",
        "--refactored-pose-curves", "--refactored-movement-curves",
        "--refactored-foot-frame", "--foot-lock-gravity-twist",
        "--foot-lock-final-contact", "--foot-ground-clearance", "--foot-contact-toes",
        "--refactored-stance-hosts",
        "--refactored-layering-frame",
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
            ? arguments : arguments.Concat(arguments.Contains("--legacy-layering")
                ? CompleteDemoFlags.Where(flag=>flag!="--refactored-layering-frame") : CompleteDemoFlags).Concat(actionPreview || arguments.Contains("--action-preview")
                ? [] : RootMotionEnabled ? new[] { "--rolling-gameplay", "--montage-root-motion" } : new[] { "--rolling-gameplay" })).ToFrozenSet(StringComparer.Ordinal);
        _demoConfigured = true;
        GD.Print("ALS_ROOT_MOTION_POLICY enabled=" + RootMotionEnabled);
    }
}
