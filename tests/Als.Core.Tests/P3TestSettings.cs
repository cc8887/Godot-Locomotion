using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

internal static class P3TestSettings
{
    public static readonly AlsLocomotionSettings Reference = AlsLocomotionSettings.Load(
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "P3",
            "p3_locomotion_settings.json")));
}
