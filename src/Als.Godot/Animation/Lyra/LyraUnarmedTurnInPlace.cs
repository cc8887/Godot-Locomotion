using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraTurnCurveSample(float RemainingYaw, float Weight);
internal readonly record struct LyraTurnSelection(string Slot, bool Restart);
internal enum LyraIdleTurnState { Idle, Rotation, Recovery }

internal sealed class LyraUnarmedTurnInPlace
{
    private const string TimingPath = "res://assets/generated/lyra_als/unarmed_remaining_timing.json";
    private const string PistolTimingPath = "res://assets/generated/lyra_als/pistol_timing.json";
    private const string RifleTimingPath = "res://assets/generated/lyra_als/rifle_timing.json";
    private static readonly string[] Slots =
        ["turn_left", "turn_right", "crouch_turn_left", "crouch_turn_right"];
    private readonly Dictionary<string, (Curve Remaining, Curve Weight, double Length)> _curves;
    private bool _right;

    private LyraUnarmedTurnInPlace(
        Dictionary<string, (Curve Remaining, Curve Weight, double Length)> curves) => _curves = curves;

    public LyraIdleTurnState State { get; private set; }
    public int RotationCount { get; private set; }
    public int RecoveryCount { get; private set; }

    public static LyraUnarmedTurnInPlace Load(LyraUnarmedRemainingCatalog remaining,
        LyraPistolCatalog? pistol = null, LyraRifleCatalog? rifle = null)
    {
        var slots = Slots.ToHashSet(StringComparer.Ordinal);
        void AddWeaponSlots(string name, IReadOnlyDictionary<string, LyraClip> clips)
        {
            var profile = LyraLinkedLayerInventory.Load().Get(name);
            foreach (var crouched in new[] { false, true })
            foreach (var right in new[] { false, true })
            {
                var asset = profile.Asset(LyraLayerClipResolver.TurnProperty(right, crouched));
                var slot = clips.Values.Single(clip => clip.SourceObjectPath == asset).Slot;
                if (!slots.Add(slot))
                    throw new InvalidOperationException(name + " turn slots are not distinct.");
            }
        }
        if (pistol is not null) AddWeaponSlots("pistol", pistol.Clips);
        if (rifle is not null) AddWeaponSlots("rifle", rifle.Clips);
        var curves = new Dictionary<string, (Curve, Curve, double)>(StringComparer.Ordinal);
        void ReadTiming(string path, IReadOnlyDictionary<string, LyraClip> expected)
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(path));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidOperationException("Unsupported Lyra turn timing schema.");
            foreach (var row in root.GetProperty("clips").EnumerateArray())
            {
                var slot = row.GetProperty("slot").GetString()!;
                if (!slots.Contains(slot)) continue;
                if (!expected.TryGetValue(slot, out var clip) ||
                    row.GetProperty("source").GetString() != clip.SourceObjectPath ||
                    row.GetProperty("target").GetString() != clip.TargetObjectPath ||
                    !row.GetProperty("curvesUnchanged").GetBoolean() ||
                    clip.FloatCurveNames.Length != 2 ||
                    !clip.FloatCurveNames.Contains("RemainingTurnYaw", StringComparer.Ordinal) ||
                    !clip.FloatCurveNames.Contains("TurnYawWeight", StringComparer.Ordinal))
                    throw new InvalidOperationException($"Lyra turn curve binding differs: {slot}.");
                var source = row.GetProperty("sourceCurves");
                var target = row.GetProperty("targetCurves");
                if (source.GetArrayLength() != 2 || target.GetArrayLength() != 2)
                    throw new InvalidOperationException($"Lyra turn clip lacks two curves: {slot}.");
                var names = target.EnumerateArray().Select(item => item.GetProperty("name").GetString())
                    .ToArray();
                if (names.Distinct(StringComparer.Ordinal).Count() != 2 ||
                    !names.Contains("RemainingTurnYaw", StringComparer.Ordinal) ||
                    !names.Contains("TurnYawWeight", StringComparer.Ordinal) ||
                    !source.EnumerateArray().Select(item => item.GetProperty("name").GetString())
                        .Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)) ||
                    !curves.TryAdd(slot, (new Curve(target.EnumerateArray().Single(item =>
                            item.GetProperty("name").GetString() == "RemainingTurnYaw")),
                        new Curve(target.EnumerateArray().Single(item =>
                            item.GetProperty("name").GetString() == "TurnYawWeight")), clip.PlayLength)))
                    throw new InvalidOperationException($"Invalid Lyra turn curves: {slot}.");
            }
        }
        ReadTiming(TimingPath, remaining.Clips);
        if (pistol is not null) ReadTiming(PistolTimingPath, pistol.Clips);
        if (rifle is not null) ReadTiming(RifleTimingPath, rifle.Clips);
        if (curves.Count != slots.Count)
            throw new InvalidOperationException("Lyra turn curve inventory is incomplete.");
        return new LyraUnarmedTurnInPlace(curves);
    }

    public bool IsTurnSlot(string slot) => _curves.ContainsKey(slot);

    public LyraTurnCurveSample Sample(string slot, double time)
    {
        var curves = _curves[slot];
        return new(curves.Remaining.Sample(time), curves.Weight.Sample(time));
    }

    public LyraTurnSelection? Select(bool idle, float rootYawOffset, bool isCrouching,
        string previousSlot, double previousTime, LyraLinkedLayerRouter layers)
    {
        if (!idle)
        {
            State = LyraIdleTurnState.Idle;
            return null;
        }
        if (State == LyraIdleTurnState.Rotation && IsTurnSlot(previousSlot) &&
            Math.Abs(Sample(previousSlot, previousTime).Weight) <= 1e-6f)
        {
            State = LyraIdleTurnState.Recovery;
            RecoveryCount++;
        }
        else if (State == LyraIdleTurnState.Recovery)
        {
            if (Math.Abs(rootYawOffset) > 50f)
            {
                State = LyraIdleTurnState.Rotation;
                _right = rootYawOffset < 0;
                RotationCount++;
                return new(layers.ResolveTurnInPlace(_right, isCrouching), true);
            }
            if (IsTurnSlot(previousSlot) &&
                _curves[previousSlot].Length - previousTime <= 0.35)
                State = LyraIdleTurnState.Idle;
        }
        var restart = false;
        if (State == LyraIdleTurnState.Idle && Math.Abs(rootYawOffset) > 50f)
        {
            State = LyraIdleTurnState.Rotation;
            _right = rootYawOffset < 0;
            RotationCount++;
            restart = true;
        }
        if (State == LyraIdleTurnState.Idle) return null;
        return new(layers.ResolveTurnInPlace(_right, isCrouching), restart);
    }

    private sealed class Curve
    {
        private readonly (double Time, float Value)[] _keys;

        public Curve(JsonElement row)
        {
            _keys = row.GetProperty("keys").EnumerateArray().Select(key =>
            {
                if (key.GetProperty("interpolation").GetString() != "RCIM_Linear")
                    throw new InvalidOperationException("Lyra turn curve is not linear.");
                return (key.GetProperty("time").GetDouble(), key.GetProperty("value").GetSingle());
            }).ToArray();
            if (_keys.Length < 2 || _keys.Any(key =>
                    !double.IsFinite(key.Time) || !float.IsFinite(key.Value)))
                throw new InvalidOperationException("Invalid Lyra turn curve keys.");
            for (var index = 1; index < _keys.Length; index++)
                if (_keys[index].Time <= _keys[index - 1].Time)
                    throw new InvalidOperationException("Lyra turn curve keys are not ordered.");
        }

        public float Sample(double time)
        {
            if (!double.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
            if (time <= _keys[0].Time) return _keys[0].Value;
            if (time >= _keys[^1].Time) return _keys[^1].Value;
            var first = 1;
            var last = _keys.Length - 1;
            while (first < last)
            {
                var middle = first + (last - first) / 2;
                if (time > _keys[middle].Time) first = middle + 1;
                else last = middle;
            }
            var left = _keys[first - 1];
            var right = _keys[first];
            var alpha = (time - left.Time) / (right.Time - left.Time);
            return (float)(left.Value + (right.Value - left.Value) * alpha);
        }
    }
}
