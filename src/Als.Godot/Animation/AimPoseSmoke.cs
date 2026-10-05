using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

public partial class AimPoseSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var definition = AlsMovementGraphDefinition.Load(set,
            AlsLocomotionProfileCompiler.Compile(Read("assets/config/p4_cycle_locomotion_profile.json"), set));
        var curveNames = definition.AimRawSources.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray())
            .Append("__not_authored__").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var source = new AlsAimAnimationSourceSampler(definition.AimSampling, definition.AimRawSources, set, curveNames);
        var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[curveNames.Length];
        var repeat = OS.GetCmdlineUserArgs().Contains("--aim-blend-native-repeat");
        using var document = JsonDocument.Parse(Read(repeat ? "artifacts/aim-blend-pose-editor-repeat.json"
            : "tests/Als.Core.Tests/Fixtures/P3/v4_aim_blend_pose_native.json"));
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("source").GetString() ==
            "UBlendSpace.GetAnimationPose(RAW, retargeted, asset root lock)", "Wrong native Aim pose oracle.");
        Require(root.GetProperty("samplingSha256").GetString()!.Equals(Convert.ToHexString(SHA256.HashData(
            Godot.FileAccess.GetFileAsBytes("res://assets/config/v4_aim_sampling.json"))), StringComparison.OrdinalIgnoreCase),
            "Aim sampling configuration changed.");
        var skeleton = definition.AimRawSources.GetSkeleton(definition.AimSampling.SkeletonId);
        var count = 0; var staticCount = 0; var frames = new Dictionary<int, int>(); var mixed = 0;
        var positionError = 0f; var rotationError = 0f; var scaleError = 0f;
        Span<float> weights = stackalloc float[3]; Span<int> order = stackalloc int[3];
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var pitch = row.GetProperty("pitch").GetSingle(); var time = row.GetProperty("normalizedTime").GetSingle();
            Require(row.GetProperty("source").GetString() == definition.AimPose.Evaluators[2].Source, "Native Aim asset differs.");
            Require(row.GetProperty("names").EnumerateArray().Select(n => n.GetString()!)
                .SequenceEqual(skeleton.LogicalBoneNames.ToArray(), StringComparer.OrdinalIgnoreCase), "Native Aim bone order differs.");
            source.Sample(new(pitch, time, true), pose, curves);
            var samples = definition.AimSampling.Runtime.Evaluate(pitch, weights, order);
            Require(row.GetProperty("samples").GetArrayLength() == samples, "Native combined pose sample count differs.");
            for (var sample = 0; sample < samples; sample++)
            {
                var native = row.GetProperty("samples")[sample];
                Require(native.GetProperty("index").GetInt32() == order[sample] &&
                    MathF.Abs(native.GetProperty("weight").GetSingle() - weights[order[sample]]) < .000002f &&
                    native.GetProperty("seconds").GetSingle() == Math.Clamp(time, 0, 1), "Native combined pose weight/order/time differs.");
            }
            if (samples > 1) mixed++;
            Require(row.GetProperty("pose").GetArrayLength() == 79, "Incomplete native combined pose.");
            for (var bone = 0; bone < 79; bone++)
            {
                var native = ConvertNative(row.GetProperty("pose")[bone]); var actual = pose[bone];
                var p = NVector3.Distance(native.Position, actual.Position);
                var q = MathF.Min((native.Rotation - actual.Rotation).Length(), (native.Rotation + actual.Rotation).Length());
                var s = NVector3.Distance(native.Scale, actual.Scale);
                positionError = MathF.Max(positionError, p); rotationError = MathF.Max(rotationError, q); scaleError = MathF.Max(scaleError, s);
                Require(p <= .00002f && q <= .00002f && s <= .00005f,
                    $"Native Aim blend differs: pitch={pitch:R} time={time:R} bone={skeleton.LogicalBoneNames[bone]} p={p:R} q={q:R} s={s:R}");
            }
            var nativeCurves = row.GetProperty("curves").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle(), StringComparer.OrdinalIgnoreCase);
            Require(nativeCurves.Count == curves.Count(c => c.Present), "Native Aim combined curve count differs.");
            for (var c = 0; c < curves.Length; c++)
            {
                var present = nativeCurves.TryGetValue(curveNames[c], out var value);
                Require(present == curves[c].Present && (!present || MathF.Abs(value - curves[c].Value) < .00002f), "Native Aim combined curve differs.");
            }
            var hz = row.GetProperty("hz").GetInt32();
            if (hz == 0) staticCount++;
            else { Require(row.GetProperty("frame").GetInt32() == frames.GetValueOrDefault(hz), "Native trajectory frame gap."); frames[hz] = frames.GetValueOrDefault(hz) + 1; }
            count++;
        }
        Require(count == 630 && staticCount == 210 && mixed > 300 && frames.Count == 3 && frames[30] == 60 && frames[60] == 120 && frames[120] == 240,
            "Native combined pose coverage incomplete.");
        GD.Print($"AIM_BLEND_POSE_NATIVE_OK poses={count} mixed={mixed} static={staticCount} frames=420 bones={count * 79} position_error={positionError:R} quaternion_error={rotationError:R} scale_error={scaleError:R} editor_repeat={repeat}");
        if (repeat) return;
        var single = new string[10]; var parallel = new string[10];
        for (var owner = 0; owner < 10; owner++) single[owner] = Trajectory(definition, set, curveNames, (uint)owner);
        Parallel.For(0, 10, owner => parallel[owner] = Trajectory(definition, set, curveNames, (uint)owner));
        Require(single.SequenceEqual(parallel), "Aim nested poses differ between single and parallel.");
        var history = new AlsAimFrameRuntime(definition.AimPose, 11, 1);
        var runtime = new AlsAimPoseRuntime(definition.AimPose, source, curveNames.Length, 11, 1);
        for (var frame = 1; frame <= 1000; frame++) Step(frame);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 1001; frame <= 11000; frame++) Step(frame);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Require(allocated == 0, "Aim nested frame update/evaluate/commit allocated memory.");
        GD.Print($"AIM_NESTED_POSE_RUNTIME_OK owners=10 single_frames=6000 parallel_frames=6000 retry=all_frames source_mask=127 hot_frames=10000 allocated_bytes={allocated} native_state_machine_oracle=separate_smoke final_graph=pending");
        void Step(int frame)
        {
            var input = Input(frame, 11); history.Prepare(input, (AlsRotationMode)(frame / 29 % 3), frame % 22 < 11, 1f / 60, frame, true);
            runtime.Evaluate(history.Candidate, pose, curves); history.Commit(input.Identity);
        }
    }

    private static string Trajectory(AlsMovementGraphDefinition definition, AlsAnimationSetDefinition set, string[] names, uint owner)
    {
        var history = new AlsAimFrameRuntime(definition.AimPose, owner, 1);
        var source = new AlsAimAnimationSourceSampler(definition.AimSampling, definition.AimRawSources, set, names);
        var runtime = new AlsAimPoseRuntime(definition.AimPose, source, names.Length, owner, 1);
        var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[names.Length];
        var retryPose = new AlsLocalPose[79]; var retryCurves = new AlsInertialCurve[names.Length];
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sourceMask = 0; var stackFrames = 0;
        Span<int> curveBits = stackalloc int[names.Length * 2];
        for (var frame = 1; frame <= 600; frame++)
        {
            var input = Input(frame, owner); var mode = (AlsRotationMode)(frame / 29 % 3); var hasInput = frame % 22 < 11;
            if (frame == 1) { mode = AlsRotationMode.Aiming; input = input with { SmoothedAngle = new(170, 35) }; }
            if (frame > 300) { mode = AlsRotationMode.Aiming; input = input with { SmoothedAngle = new(frame / 60 % 2 == 0 ? -170 : 170, 35) }; }
            history.Prepare(input, mode, hasInput, 1f / 60, frame, true); runtime.Evaluate(history.Candidate, pose, curves);
            sourceMask |= runtime.EvaluatedSourceMask; if (runtime.TransitionEvaluations > 1) stackFrames++;
            history.Cancel(); history.Prepare(input, mode, hasInput, 1f / 60, frame, true);
            runtime.Evaluate(history.Candidate, retryPose, retryCurves);
            Require(pose.SequenceEqual(retryPose) && curves.SequenceEqual(retryCurves), "Aim nested pose retry diverged.");
            history.Commit(input.Identity);
            digest.AppendData(MemoryMarshal.AsBytes(pose.AsSpan()));
            for (var c = 0; c < curves.Length; c++) { curveBits[c * 2] = BitConverter.SingleToInt32Bits(curves[c].Value); curveBits[c * 2 + 1] = curves[c].Present ? 1 : 0; }
            digest.AppendData(MemoryMarshal.AsBytes(curveBits));
        }
        Require(sourceMask == 127 && stackFrames > 0, $"Aim trajectory omitted a source or interrupted transition: owner={owner} mask={sourceMask} stacked={stackFrames}.");
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static AlsAimingInputState Input(int frame, uint owner)
    {
        var yaw = Math.Sin((frame + owner * 7) * .16) * 179;
        return new(new(frame, owner, 1), default, default, new(yaw, Math.Cos(frame * .11) * 89), default,
            .5, frame % 101 / 100.0, .5 - Math.Abs(yaw) / 360, .5 + Math.Abs(yaw) / 360, .5 + yaw / 360);
    }
    private static AlsLocalPose ConvertNative(JsonElement value)
    {
        var p = value.GetProperty("position"); var q = value.GetProperty("rotation"); var s = value.GetProperty("scale");
        return new(new(p[0].GetSingle() * .01f, -p[1].GetSingle() * .01f, p[2].GetSingle() * .01f),
            new(-q[0].GetSingle(), q[1].GetSingle(), -q[2].GetSingle(), q[3].GetSingle()), new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
    private static string Read(string path) => Godot.FileAccess.GetFileAsString("res://" + path);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
