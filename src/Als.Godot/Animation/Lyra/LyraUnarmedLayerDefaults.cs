using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraUnarmedLayerDefaults(double StartPivotMinimumRate,
    double StartPivotMaximumRate, double CycleMinimumRate, double CycleMaximumRate,
    double StrideBlendDuration, double StrideBlendStartOffset)
{
    private const string ResourcePath = "res://assets/generated/lyra_als/unarmed_layer_defaults.json";

    public static LyraUnarmedLayerDefaults LoadWeapon(string profile)
    {
        if (profile is not ("pistol" or "rifle"))
            throw new ArgumentException("Unsupported Lyra weapon profile.", nameof(profile));
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(
            $"res://assets/generated/lyra_als/{profile}_layer_defaults.json"));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("class").GetString() != LyraLinkedLayerInventory.Load().Get(profile).ClassPath)
            throw new InvalidOperationException("Lyra weapon playback class differs: " + profile);
        var properties = root.GetProperty("properties");
        if (properties.GetProperty("LocomotionDistanceCurveName").GetString() != "Distance")
            throw new InvalidOperationException("Unsupported Lyra weapon distance curve: " + profile);
        var start = properties.GetProperty("PlayRateClampStartsPivots");
        var cycle = properties.GetProperty("PlayRateClampCycle");
        if (start.GetArrayLength() != 2 || cycle.GetArrayLength() != 2)
            throw new InvalidOperationException("Invalid Lyra weapon playback clamps: " + profile);
        var result = new LyraUnarmedLayerDefaults(start[0].GetDouble(), start[1].GetDouble(),
            cycle[0].GetDouble(), cycle[1].GetDouble(),
            properties.GetProperty("StrideWarpingBlendInDurationScaled").GetDouble(),
            properties.GetProperty("StrideWarpingBlendInStartOffset").GetDouble());
        Validate(result);
        return result;
    }

    public static LyraUnarmedLayerDefaults Load()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(ResourcePath));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported Lyra layer defaults schema.");
        var layers = root.GetProperty("layers");
        var baseLayer = layers.GetProperty("base");
        var unarmedLayer = layers.GetProperty("unarmed");
        if (baseLayer.GetProperty("class").GetString() !=
            "/Game/Characters/Heroes/Mannequin/Animations/LinkedLayers/ABP_ItemAnimLayersBase.ABP_ItemAnimLayersBase_C" ||
            unarmedLayer.GetProperty("class").GetString() !=
            "/Game/Characters/Heroes/Mannequin/Animations/Locomotion/Unarmed/ABP_UnarmedAnimLayers.ABP_UnarmedAnimLayers_C")
            throw new InvalidOperationException("Lyra layer class binding differs from Unarmed.");
        var properties = unarmedLayer.GetProperty("properties");
        if (properties.GetRawText() != baseLayer.GetProperty("properties").GetRawText() ||
            properties.GetProperty("LocomotionDistanceCurveName").GetString() != "Distance")
            throw new InvalidOperationException("Lyra base and Unarmed layer defaults differ.");
        var startPivot = properties.GetProperty("PlayRateClampStartsPivots");
        var cycle = properties.GetProperty("PlayRateClampCycle");
        if (startPivot.GetArrayLength() != 2 || cycle.GetArrayLength() != 2)
            throw new InvalidOperationException("Invalid Lyra play rate clamp.");
        var result = new LyraUnarmedLayerDefaults(startPivot[0].GetDouble(), startPivot[1].GetDouble(),
            cycle[0].GetDouble(), cycle[1].GetDouble(),
            properties.GetProperty("StrideWarpingBlendInDurationScaled").GetDouble(),
            properties.GetProperty("StrideWarpingBlendInStartOffset").GetDouble());
        const string pistolPath = "res://assets/generated/lyra_als/pistol_layer_defaults.json";
        if (Godot.FileAccess.FileExists(pistolPath))
        {
            using var pistol = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(pistolPath));
            var pistolRoot = pistol.RootElement;
            var pistolProperties = pistolRoot.GetProperty("properties");
            var pistolStart = pistolProperties.GetProperty("PlayRateClampStartsPivots");
            var pistolCycle = pistolProperties.GetProperty("PlayRateClampCycle");
            if (pistolRoot.GetProperty("schemaVersion").GetInt32() != 1 ||
                pistolRoot.GetProperty("class").GetString() !=
                LyraLinkedLayerInventory.Load().Get("pistol").ClassPath ||
                pistolStart.GetArrayLength() != 2 || pistolCycle.GetArrayLength() != 2 ||
                pistolProperties.GetProperty("LocomotionDistanceCurveName").GetString() != "Distance" ||
                result != new LyraUnarmedLayerDefaults(pistolStart[0].GetDouble(),
                    pistolStart[1].GetDouble(), pistolCycle[0].GetDouble(),
                    pistolCycle[1].GetDouble(),
                    pistolProperties.GetProperty("StrideWarpingBlendInDurationScaled").GetDouble(),
                    pistolProperties.GetProperty("StrideWarpingBlendInStartOffset").GetDouble()))
                throw new InvalidOperationException("Pistol layer playback defaults differ from Unarmed.");
        }
        Validate(result);
        return result;
    }

    private static void Validate(in LyraUnarmedLayerDefaults result)
    {
        if (!double.IsFinite(result.StartPivotMinimumRate) ||
            !double.IsFinite(result.StartPivotMaximumRate) ||
            !double.IsFinite(result.CycleMinimumRate) ||
            !double.IsFinite(result.CycleMaximumRate) ||
            !double.IsFinite(result.StrideBlendDuration) ||
            !double.IsFinite(result.StrideBlendStartOffset) ||
            result.StartPivotMinimumRate < 0 ||
            result.StartPivotMinimumRate >= result.StartPivotMaximumRate ||
            result.CycleMinimumRate < 0 || result.CycleMinimumRate >= result.CycleMaximumRate ||
            result.StrideBlendDuration <= 0 || result.StrideBlendStartOffset < 0)
            throw new InvalidOperationException("Invalid Lyra Unarmed layer defaults.");
    }
}
