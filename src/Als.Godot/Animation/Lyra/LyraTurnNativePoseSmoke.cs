using System.Security.Cryptography;
using System.Text.Json;
using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraTurnNativePoseSmoke : Node
{
    private const string OraclePath = "res://assets/generated/lyra_als/unarmed_turn_als_native_60hz.json";
    private const string CompressedOraclePath =
        "res://assets/generated/lyra_als/unarmed_turn_als_native_60hz_forced_compressed.json";
    private const string CatalogPath = "res://assets/generated/lyra_als/unarmed_remaining_catalog.json";
    private const string TimingPath = "res://assets/generated/lyra_als/unarmed_remaining_timing.json";

    public override void _Ready()
    {
        try
        {
            Run();
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private void Run()
    {
        var compressed = OS.GetCmdlineUserArgs().Contains("--lyra-turn-native-compressed");
        using var oracle = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(
            compressed ? CompressedOraclePath : OraclePath));
        var root = oracle.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("sampleHz").GetInt32() != 60 ||
            compressed && root.GetProperty("sourceMode").GetString() != "forced-compressed" ||
            Hash(CatalogPath) != root.GetProperty("catalogSha256").GetString() ||
            Hash(TimingPath) != root.GetProperty("timingSha256").GetString())
            throw new InvalidOperationException("Lyra native turn oracle has stale inputs.");
        using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true);
        AddChild(rig.Root);
        var turn = LyraUnarmedTurnInPlace.Load(rig.Remaining!);
        var names = root.GetProperty("logicalBoneNames");
        if (root.GetProperty("physicalBoneCount").GetInt32() != rig.Skeleton.GetBoneCount() ||
            names.GetArrayLength() != 79 || root.GetProperty("clips").GetArrayLength() != 4)
            throw new InvalidOperationException("Lyra native turn oracle has the wrong ALS layout.");
        for (var bone = 0; bone < rig.Skeleton.GetBoneCount(); bone++)
            if (!rig.Skeleton.GetBoneName(bone).ToString()
                    .Equals(names[bone].GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Lyra physical bone mismatch: {bone}.");

        var allFrames = 0;
        foreach (var clip in root.GetProperty("clips").EnumerateArray())
        {
            var slot = clip.GetProperty("slot").GetString()!;
            if (!turn.IsTurnSlot(slot) || !rig.Remaining!.Clips.TryGetValue(slot, out var binding) ||
                clip.GetProperty("source").GetString() != binding.SourceObjectPath ||
                clip.GetProperty("target").GetString() != binding.TargetObjectPath ||
                clip.GetProperty("fbxSha256").GetString() != binding.Sha256)
                throw new InvalidOperationException("Lyra native turn clip has a different binding.");
            var frames = clip.GetProperty("frames");
            if (frames.GetArrayLength() < 60)
                throw new InvalidOperationException($"Lyra native turn clip has too few frames: {slot}.");
            rig.Player.Play(rig.QualifiedName(slot));
            rig.Player.Advance(0);
            var maxPosition = 0f;
            var maxAngle = 0f;
            var maxScale = 0f;
            var maxCurve = 0f;
            var worstPosition = "";
            var worstAngle = "";
            var worstRotationValues = "";
            var maxKeyAngle = 0f;
            var maxMidpointAngle = 0f;
            var maxTemporalPosition = 0f;
            var maxTemporalAngleLeft = 0f;
            var maxTemporalAngleRight = 0f;
            var initialNativePositions = new Vector3[rig.Skeleton.GetBoneCount()];
            var initialFbxPositions = new Vector3[rig.Skeleton.GetBoneCount()];
            var initialNativeRotations = new Quaternion[rig.Skeleton.GetBoneCount()];
            var initialFbxRotations = new Quaternion[rig.Skeleton.GetBoneCount()];
            var frameIndex = 0;
            foreach (var frame in frames.EnumerateArray())
            {
                var seconds = frame.GetProperty("timeSeconds").GetDouble();
                rig.Player.Seek(seconds, true);
                var curves = frame.GetProperty("curves");
                var observed = turn.Sample(slot, seconds);
                maxCurve = Math.Max(maxCurve, Math.Abs(observed.RemainingYaw -
                    curves.GetProperty("RemainingTurnYaw").GetSingle()));
                maxCurve = Math.Max(maxCurve, Math.Abs(observed.Weight -
                    curves.GetProperty("TurnYawWeight").GetSingle()));
                var pose = frame.GetProperty("pose");
                if (pose.GetArrayLength() != names.GetArrayLength())
                    throw new InvalidOperationException("Lyra native turn pose is incomplete.");
                for (var bone = 0; bone < rig.Skeleton.GetBoneCount(); bone++)
                {
                    var native = pose[bone];
                    var position = native.GetProperty("position");
                    var rotation = native.GetProperty("rotation");
                    var scale = native.GetProperty("scale");
                    var expectedPosition = new Vector3(position[0].GetSingle() * 0.01f,
                        -position[1].GetSingle() * 0.01f, position[2].GetSingle() * 0.01f);
                    var expectedRotation = new Quaternion(-rotation[0].GetSingle(),
                        rotation[1].GetSingle(), -rotation[2].GetSingle(),
                        rotation[3].GetSingle()).Normalized();
                    var expectedScale = new Vector3(scale[0].GetSingle(), scale[1].GetSingle(),
                        scale[2].GetSingle());
                    var actual = rig.Skeleton.GetBonePose(bone);
                    var actualRotation = actual.Basis.GetRotationQuaternion();
                    if (frameIndex == 0)
                    {
                        initialNativePositions[bone] = expectedPosition;
                        initialFbxPositions[bone] = actual.Origin;
                        initialNativeRotations[bone] = expectedRotation;
                        initialFbxRotations[bone] = actualRotation;
                    }
                    else
                    {
                        maxTemporalPosition = Math.Max(maxTemporalPosition,
                            (expectedPosition - initialNativePositions[bone]).DistanceTo(
                                actual.Origin - initialFbxPositions[bone]));
                        maxTemporalAngleLeft = Math.Max(maxTemporalAngleLeft,
                            (initialNativeRotations[bone].Inverse() * expectedRotation).AngleTo(
                                initialFbxRotations[bone].Inverse() * actualRotation));
                        maxTemporalAngleRight = Math.Max(maxTemporalAngleRight,
                            (expectedRotation * initialNativeRotations[bone].Inverse()).AngleTo(
                                actualRotation * initialFbxRotations[bone].Inverse()));
                    }
                    var positionError = actual.Origin.DistanceTo(expectedPosition);
                    var angleError = actualRotation.AngleTo(expectedRotation);
                    var scaleError = actual.Basis.Scale.DistanceTo(expectedScale);
                    if (positionError > maxPosition)
                    {
                        maxPosition = positionError;
                        worstPosition = $"{names[bone].GetString()}@{seconds:0.000}";
                    }
                    if (angleError > maxAngle)
                    {
                        maxAngle = angleError;
                        worstAngle = $"{names[bone].GetString()}@{seconds:0.000}";
                        worstRotationValues = $"native={expectedRotation} fbx={actualRotation}";
                    }
                    if (Math.Abs(seconds * 30 - Math.Round(seconds * 30)) < 1e-4)
                        maxKeyAngle = Math.Max(maxKeyAngle, angleError);
                    else maxMidpointAngle = Math.Max(maxMidpointAngle, angleError);
                    maxScale = Math.Max(maxScale, scaleError);
                }
                frameIndex++;
                allFrames++;
            }
            GD.Print($"LYRA_TURN_NATIVE_CLIP mode={(compressed ? "compressed" : "raw")} " +
                $"slot={slot} frames={frames.GetArrayLength()} " +
                $"position={maxPosition:0.000000}m/{worstPosition} " +
                $"angle={maxAngle:0.000000}rad/{worstAngle} " +
                $"keyAngle={maxKeyAngle:0.000000} midpointAngle={maxMidpointAngle:0.000000} " +
                $"temporalPosition={maxTemporalPosition:0.000000} " +
                $"temporalAngleL={maxTemporalAngleLeft:0.000000} " +
                $"temporalAngleR={maxTemporalAngleRight:0.000000} " +
                $"scale={maxScale:0.000000} curve={maxCurve:0.000000} " +
                worstRotationValues);
            if (maxPosition > 0.007f || maxAngle > 0.09f ||
                maxTemporalPosition > 0.007f ||
                maxTemporalAngleLeft > 0.09f || maxTemporalAngleRight > 0.09f ||
                maxScale > 1e-5f || maxCurve > 1e-4f)
                throw new InvalidOperationException($"Lyra native turn pose differs: {slot}.");
        }
        if (allFrames != 478)
            throw new InvalidOperationException($"Lyra native turn frame count differs: {allFrames}.");
        GD.Print($"LYRA_TURN_NATIVE_OK mode={(compressed ? "compressed" : "raw")} " +
            $"clips=4 frames={allFrames} bones=68");
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(path)))
            .ToLowerInvariant();
}
