using System.Text.Json;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

public sealed record AlsRigSleepSettings(AlsSleepBodySettings[] Bodies, float Smoothing);
public static class AlsSleepSettingsCompiler
{
    public static AlsRigSleepSettings Compile(string json, AlsRagdollPhysicsDefinition definition)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 2 && root.GetProperty("sleepEnabled").GetBoolean() &&
            root.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal).SetEquals(
                ["schemaVersion", "sleepEnabled", "sleepSettings", "rigs", "referenceSha256"]) &&
            root.GetProperty("referenceSha256").GetString() is { Length: 64 }, "Compact native sleep settings required.");
        var cvars = root.GetProperty("sleepSettings");
        Require(cvars.GetProperty("p.Chaos.Solver.Sleep.PartialIslandSleep").GetDouble() == 0 &&
            cvars.GetProperty("p.Chaos.Solver.Sleep.AngularSleepThresholdSize").GetDouble() == 0, "Partial/size-scaled sleeping is unsupported.");
        var smoothing = cvars.GetProperty("p.Chaos.SmoothedPositionLerpRate").GetSingle();
        Require(float.IsFinite(smoothing) && smoothing is >= 0 and <= 1, "Invalid native smoothing rate.");
        var rigs = root.GetProperty("rigs").EnumerateArray().Where(r => r.GetProperty("mesh").GetString() == definition.Mesh).ToArray();
        Require(rigs.Length == 1 && rigs[0].GetProperty("physicsAsset").GetString() == definition.PhysicsAsset, "Sleep rig identity differs.");
        var rows = rigs[0].GetProperty("bodies"); Require(rows.GetArrayLength() == definition.Bodies.Length, "Sleep body count differs.");
        var result = new AlsSleepBodySettings[definition.Bodies.Length];
        for (var i = 0; i < result.Length; i++)
        {
            var row = rows[i]; Require(row.GetProperty("index").GetInt32() == i && row.GetProperty("bone").GetString() == definition.Bodies[i].Bone, "Sleep body binding differs.");
            var type = row.GetProperty("sleepType").GetInt32(); Require(type is 0 or 1, "Unsupported sleep type.");
            var multiplier = row.GetProperty("sleepThresholdMultiplier").GetSingle();
            var linear = row.GetProperty("sleepLinearThreshold").GetSingle(); var angular = row.GetProperty("sleepAngularThreshold").GetSingle();
            var counter = row.GetProperty("sleepCounterThreshold").GetInt32();
            Require(float.IsFinite(multiplier) && multiplier >= 0 && float.IsFinite(linear) && linear >= 0 &&
                float.IsFinite(angular) && angular >= 0 && counter >= 0 && float.IsFinite(linear * multiplier) && float.IsFinite(angular * multiplier), "Invalid sleep threshold.");
            result[i] = new(linear * multiplier, angular * multiplier, counter, type == 1);
        }
        return new(result, smoothing);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
