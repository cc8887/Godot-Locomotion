using System.Text.Json;
using Godot;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraRootMotionRange(double PlayLength, NumericsVector3 TranslationCm,
    NumericsQuaternion Rotation, double PlanarDistanceCm)
{
    public double AveragePlanarSpeedCmPerSecond => PlanarDistanceCm / PlayLength;
}

internal readonly record struct LyraRootMotionClip(LyraRootMotionRange Source,
    LyraRootMotionRange Target);

internal sealed class LyraUnarmedRootMotion
{
    private const string ResourcePath = "res://assets/generated/lyra_als/unarmed_root_motion.json";
    private const string AuxiliaryResourcePath = "res://assets/generated/lyra_als/unarmed_aux_root_motion.json";
    private const string RemainingResourcePath = "res://assets/generated/lyra_als/unarmed_remaining_root_motion.json";
    private const string PistolResourcePath = "res://assets/generated/lyra_als/pistol_root_motion.json";
    private const string RifleResourcePath = "res://assets/generated/lyra_als/rifle_root_motion.json";
    private readonly Dictionary<string, LyraRootMotionClip> _clips;

    private LyraUnarmedRootMotion(Dictionary<string, LyraRootMotionClip> clips) => _clips = clips;

    public int Count => _clips.Count;
    public LyraRootMotionClip this[string slot] => _clips[slot];

    public static float MatchCycleRate(float displacementSpeedCmPerSecond,
        in LyraRootMotionRange sequence, float previousRate,
        in LyraUnarmedLayerDefaults defaults)
    {
        if (!float.IsFinite(displacementSpeedCmPerSecond) || displacementSpeedCmPerSecond < 0 ||
            !float.IsFinite(previousRate) || previousRate < 0)
            throw new ArgumentOutOfRangeException(nameof(displacementSpeedCmPerSecond));
        var rootDistance = (float)sequence.PlanarDistanceCm;
        var length = (float)sequence.PlayLength;
        if (MathF.Abs(rootDistance) <= 1e-8f || MathF.Abs(length) <= 1e-8f)
            return previousRate;
        var rate = displacementSpeedCmPerSecond / (rootDistance / length);
        return Math.Clamp(rate, (float)defaults.CycleMinimumRate,
            (float)defaults.CycleMaximumRate);
    }

    public static LyraUnarmedRootMotion Load(LyraUnarmedCatalog catalog,
        LyraUnarmedAuxCatalog? auxiliary = null, LyraUnarmedRemainingCatalog? remaining = null,
        LyraPistolCatalog? pistol = null, LyraRifleCatalog? rifle = null)
    {
        var clips = new Dictionary<string, LyraRootMotionClip>(StringComparer.Ordinal);
        void ReadCatalog(string path, IReadOnlyDictionary<string, LyraClip> expected)
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidOperationException("Unsupported Lyra root-motion schema.");
            var seen = 0;
            foreach (var row in root.GetProperty("clips").EnumerateArray())
            {
                var slot = row.GetProperty("slot").GetString()!;
                if (!expected.TryGetValue(slot, out var clip))
                    throw new InvalidOperationException($"Unknown Lyra root-motion slot: {slot}.");
                var source = ReadRange(row.GetProperty("source"), clip.SourceObjectPath, clip.PlayLength);
                var target = ReadRange(row.GetProperty("target"), clip.TargetObjectPath, clip.PlayLength);
                if (!clips.TryAdd(slot, new(source, target)))
                    throw new InvalidOperationException($"Duplicate Lyra root-motion slot: {slot}.");
                seen++;
            }
            if (seen != expected.Count)
                throw new InvalidOperationException("Lyra root-motion catalog is incomplete.");
        }
        ReadCatalog(ResourcePath, catalog.Clips);
        if (auxiliary is not null) ReadCatalog(AuxiliaryResourcePath, auxiliary.Clips);
        if (remaining is not null) ReadCatalog(RemainingResourcePath, remaining.Clips);
        if (pistol is not null) ReadCatalog(PistolResourcePath, pistol.Clips);
        if (rifle is not null) ReadCatalog(RifleResourcePath, rifle.Clips);
        return new LyraUnarmedRootMotion(clips);
    }

    private static LyraRootMotionRange ReadRange(JsonElement row, string expectedPath,
        double expectedLength)
    {
        if (row.GetProperty("source").GetString() != expectedPath)
            throw new InvalidOperationException($"Lyra root-motion asset mismatch: {expectedPath}.");
        var position = row.GetProperty("motion").GetProperty("position");
        var rotation = row.GetProperty("motion").GetProperty("rotation");
        if (position.GetArrayLength() != 3 || rotation.GetArrayLength() != 4)
            throw new InvalidOperationException("Invalid Lyra root-motion transform.");
        var translation = new NumericsVector3(position[0].GetSingle(), position[1].GetSingle(),
            position[2].GetSingle());
        var quaternion = new NumericsQuaternion(rotation[0].GetSingle(), rotation[1].GetSingle(),
            rotation[2].GetSingle(), rotation[3].GetSingle());
        var result = new LyraRootMotionRange(row.GetProperty("playLength").GetDouble(),
            translation, quaternion, row.GetProperty("planarDistanceCm").GetDouble());
        if (!double.IsFinite(result.PlayLength) ||
            Math.Abs(result.PlayLength - expectedLength) > 1e-4 ||
            !double.IsFinite(result.PlanarDistanceCm) || result.PlanarDistanceCm < 0 ||
            !float.IsFinite(translation.X) || !float.IsFinite(translation.Y) ||
            !float.IsFinite(translation.Z) || !float.IsFinite(quaternion.X) ||
            !float.IsFinite(quaternion.Y) || !float.IsFinite(quaternion.Z) ||
            !float.IsFinite(quaternion.W) ||
            Math.Abs(Math.Sqrt((double)translation.X * translation.X +
                (double)translation.Y * translation.Y) - result.PlanarDistanceCm) > 1e-3)
            throw new InvalidOperationException($"Invalid Lyra root-motion range: {expectedPath}.");
        return result;
    }
}
