using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

public partial class RawAnimationSourceSmoke : Node
{
    [Export] public bool EvaluateAdditive { get; set; }
    private delegate AlsRawPoseKeySelection SampleSource(double seconds, bool retarget, bool extract,
        bool ignoreLock, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves);
    private float _positionError, _rotationError, _scaleError, _curveError;
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("assets/config/p4_cycle_locomotion_profile.json"), set);
        var definition = AlsMovementGraphDefinition.Load(set, locomotion);
        var aim = OS.GetCmdlineUserArgs().Contains("--aim-source");
        var overlay = OS.GetCmdlineUserArgs().Contains("--overlay-source");
        var ragdoll = OS.GetCmdlineUserArgs().Contains("--ragdoll-source");
        var aliases = OS.GetCmdlineUserArgs().Contains("--refactored-source-curves");
        Require((aim ? 1 : 0) + (overlay ? 1 : 0) + (ragdoll ? 1 : 0) <= 1 && !(ragdoll && EvaluateAdditive), "Choose one supported source closure.");
        var bank = ragdoll ? definition.RagdollRawSources : overlay ? definition.OverlayRawSources : aim ? definition.AimRawSources : definition.RawSources;
        var sources = bank.Sources.ToArray().Where(s => !EvaluateAdditive || s.Policy.AdditiveType != AlsRawAnimationAdditiveType.None).ToArray();
        var names = bank.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray())
            .Concat(aliases ? AlsRefactoredV4SourceCurves.TargetNames : [])
            .Append("__not_authored__").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var aimSampler = aim && EvaluateAdditive ? new AlsAimAnimationSourceSampler(definition.AimSampling, bank, set, names) : null;
        var aimPose = new AlsLocalPose[79]; var aimCurves = new AlsInertialCurve[names.Length]; var aimVerified = 0;
        using var oracle = JsonDocument.Parse(Read(ragdoll ? "tests/Als.Import.Tests/Fixtures/Ragdoll/native_source_poses.json" : "tests/Als.Core.Tests/Fixtures/P3/" +
            (overlay ? EvaluateAdditive ? "v4_overlay_additive_source_pose_native.json" : "v4_overlay_source_pose_native.json" :
                aim ? EvaluateAdditive ? "v4_aim_additive_source_pose_native.json" : "v4_aim_source_pose_native.json" :
                EvaluateAdditive ? "v4_additive_source_pose_native.json" : "v4_movement_source_pose_native.json")));
        if (EvaluateAdditive)
            Require(oracle.RootElement.GetProperty("sourceIndexSha256").GetString()!.Equals(
                Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes("res://assets/config/" +
                    (overlay ? "v4_overlay_source_inputs.json" : aim ? "v4_aim_source_inputs.json" : "v4_movement_source_inputs.json")))),
                StringComparison.OrdinalIgnoreCase), "Additive native oracle targets a different source bank.");
        var assets = oracle.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Require(assets.Length == sources.Length && assets.Select(a => a.GetProperty("assetId").GetString())
            .Distinct().Count() == sources.Length, "Native oracle source closure differs.");
        var samples = 0; var bones = 0; var curveValues = 0; var productionSamples = 0;
        var aliasValues = 0; var aliasNonzero = 0; var aliasNegative = 0;
        if (aim)
        {
            foreach (var source in bank.Sources) Require(ReferenceEquals(source, definition.RawSources.GetSource(source.PoseData.Identity.AnimationId)),
                "Aim source duplicated an already loaded immutable movement resource.");
            Require(ReferenceEquals(bank.GetSkeleton(definition.AimSampling.SkeletonId), definition.RawSources.GetSkeleton(definition.AimSampling.SkeletonId)),
                "Aim source duplicated the logical skeleton.");
        }
        foreach (var source in sources)
        {
            var skeleton = bank.GetSkeleton(source.PoseData.Identity.SkeletonId);
            Require(skeleton.LogicalBoneCount == 79 && skeleton.PhysicalBoneCount == 68, "Incomplete movement skeleton.");
            var sampleSource = CreateSampler(source, bank, set, names, EvaluateAdditive);
            using var production = new AlsMovementAnimationSource(source, bank, set, names, rawBonePose: false);
            var productionPose = new AlsLocalPose[skeleton.LogicalBoneCount];
            var pose = new AlsLocalPose[skeleton.LogicalBoneCount]; var curves = new AlsInertialCurve[names.Length];
            var asset = assets.Single(a => a.GetProperty("assetId").GetString() == source.PoseData.Identity.AssetId);
            Require(asset.GetProperty("source").GetString() == source.PoseData.Identity.AssetPath, "Native source identity differs.");
            foreach (var sample in asset.GetProperty("samples").EnumerateArray())
            {
                Require(sample.GetProperty("names").EnumerateArray().Select(n => n.GetString()!)
                    .SequenceEqual(skeleton.LogicalBoneNames.ToArray(), StringComparer.OrdinalIgnoreCase), "Native logical bone order differs.");
                var time = sample.GetProperty("timeSeconds").GetDouble();
                if (EvaluateAdditive) Require(sample.GetProperty("evaluatedAdditive").GetBoolean(), "Native additive conversion was skipped.");
                sampleSource(time, sample.GetProperty("shouldRetarget").GetBoolean(),
                    sample.GetProperty("extractRootMotion").GetBoolean(), sample.GetProperty("ignoreRootLock").GetBoolean(), pose, curves);
                var stage = $"{source.PoseData.Identity.AssetPath} {sample.GetProperty("context").GetString()} time={time:R}";
                var expected = sample.GetProperty("pose"); Require(expected.GetArrayLength() == pose.Length, "Incomplete native pose.");
                var checkProduction = (EvaluateAdditive || source.Policy.AdditiveType == AlsRawAnimationAdditiveType.None) &&
                    sample.GetProperty("shouldRetarget").GetBoolean() && !sample.GetProperty("extractRootMotion").GetBoolean() &&
                    !sample.GetProperty("ignoreRootLock").GetBoolean();
                if (checkProduction)
                {
                    // Exercise curve-first access, then pose reuse at the same caller-owned time.
                    _ = production.Curve(time, "__not_authored__");
                    production.Sample(skeleton.ReferencePose, time, productionPose); productionSamples++;
                }
                var checkAim = checkProduction && aimSampler is not null;
                if (checkAim)
                {
                    var sampleIndex = definition.AimSampling.AnimationIds.IndexOf(source.PoseData.Identity.AnimationId);
                    Require(sampleIndex >= 0, "Native Aim source is not in its evaluator closure.");
                    var pitch = sampleIndex == 1 ? -90 : sampleIndex == 2 ? 90 : 0;
                    aimSampler!.Sample(new(pitch, (float)time, true), aimPose, aimCurves); aimVerified++;
                }
                for (var bone = 0; bone < pose.Length; bone++)
                {
                    var native = ConvertNative(expected[bone]); var actual = pose[bone];
                    var p = NVector3.Distance(native.Position, actual.Position);
                    var q = MathF.Min((native.Rotation - actual.Rotation).Length(), (native.Rotation + actual.Rotation).Length());
                    var s = NVector3.Distance(native.Scale, actual.Scale);
                    _positionError = MathF.Max(_positionError, p); _rotationError = MathF.Max(_rotationError, q); _scaleError = MathF.Max(_scaleError, s);
                    Require(p <= .00002f && q <= .00002f && s <= .00005f,
                        $"{stage} bone={skeleton.LogicalBoneNames[bone]} p={p:R} q={q:R} s={s:R}; expected={native}; actual={actual}");
                    if (checkProduction)
                    {
                        var composed = productionPose[bone];
                        Require(NVector3.Distance(native.Position, composed.Position) <= .00002f &&
                            MathF.Min((native.Rotation - composed.Rotation).Length(), (native.Rotation + composed.Rotation).Length()) <= .00002f &&
                            NVector3.Distance(native.Scale, composed.Scale) <= .00005f, $"{stage} production source differs at bone {bone}.");
                    }
                    if (checkAim)
                    {
                        var composed = aimPose[bone];
                        Require(NVector3.Distance(native.Position, composed.Position) <= .00002f &&
                            MathF.Min((native.Rotation - composed.Rotation).Length(), (native.Rotation + composed.Rotation).Length()) <= .00002f &&
                            NVector3.Distance(native.Scale, composed.Scale) <= .00005f, $"{stage} Aim evaluator source differs at bone {bone}.");
                    }
                    bones++;
                }
                var nativeCurves = sample.GetProperty("curves").EnumerateObject()
                    .ToDictionary(c => c.Name, c => c.Value.GetSingle(), StringComparer.OrdinalIgnoreCase);
                if (aliases)
                    foreach (var (original, target) in new[] { ("Mask_LandPrediction", "GroundPredictionBlock"), ("FootLock_L", "FootLeftLock"), ("FootLock_R", "FootRightLock") })
                        if (nativeCurves.TryGetValue(original, out var authored))
                        {
                            nativeCurves.Add(target, authored); aliasValues++;
                            if (authored != 0) aliasNonzero++;
                            if (authored < 0) aliasNegative++;
                        }
                Require(nativeCurves.Count == curves.Count(c => c.Present), $"{stage} authored curve presence differs.");
                for (var index = 0; index < names.Length; index++)
                {
                    var present = nativeCurves.TryGetValue(names[index], out var value);
                    Require(curves[index].Present == present, $"{stage} curve={names[index]} presence differs.");
                    if (checkAim) Require(aimCurves[index].Present == present && (!present || MathF.Abs(aimCurves[index].Value - value) <= .00002f),
                        $"{stage} Aim evaluator curve presence/value differs.");
                    if (checkProduction)
                    {
                        var composed = production.Curve(time, names[index]);
                        Require(composed.Present == present && (!present || MathF.Abs(composed.Value - value) <= .00002f),
                            $"{stage} production curve={names[index]} differs.");
                    }
                    if (!present) continue;
                    var error = MathF.Abs(curves[index].Value - value); _curveError = MathF.Max(_curveError, error);
                    Require(error <= .00002f, $"{stage} curve={names[index]} expected={value:R} actual={curves[index].Value:R}");
                    curveValues++;
                }
                samples++;
            }
        }
        Require(samples >= sources.Length * 20, "Every source needs five times and four extraction contexts.");
        Require(productionSamples > 0, "Production source wrapper was not checked against native poses.");
        GD.Print($"MOVEMENT_SOURCE_WRAPPER_NATIVE_OK additive={EvaluateAdditive} native_poses={productionSamples} logical_bones=79 curve_presence=true access_order=curve_then_pose");
        if (aimSampler is not null)
        {
            Require(aimVerified == 39, "Aim evaluator endpoint native coverage is incomplete.");
            for (var i = 0; i < 1000; i++) aimSampler.Sample(new(i % 241 - 120, i % 101 / 100f, i % 2 == 0), aimPose, aimCurves);
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++) aimSampler.Sample(new(i % 241 - 120, i % 101 / 100f, i % 2 == 0), aimPose, aimCurves);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            Require(allocated == 0, "Aim evaluator hot sampling allocated memory.");
            GD.Print($"AIM_EVALUATOR_SOURCE_OK native_endpoint_poses={aimVerified} source_assets=3 logical_bones=79 sampling_calls=10000 allocated_bytes={allocated} mixed_pose_oracle=pending final_graph=pending");
        }
        VerifyNoAlloc(bank, set, names, EvaluateAdditive);
        VerifyParallel(bank, set, names, EvaluateAdditive);
        if (aliases)
        {
            Require(aliasValues > 0 && aliasNonzero > 0, "Native fixture never exercised authored source aliases.");
            GD.Print($"REFACTORED_SOURCE_CURVES_NATIVE_OK precision=float additive={EvaluateAdditive} values={aliasValues} nonzero={aliasNonzero} negative={aliasNegative} expected=native_original_curves missing=absent production_wrapper=verified");
        }
        var tag = EvaluateAdditive ? "ANIMATION_ADDITIVE_SOURCE" : "RAW_ANIMATION_SOURCE";
        var evaluation = EvaluateAdditive ? "raw_with_additive" : "raw_before_additive";
        GD.Print($"{tag}_NATIVE_OK roots={bank.RootAnimationIds.Length} assets={sources.Length} players={bank.PlayerCount} samples={bank.SampleCount} native_poses={samples} bones={bones} curve_values={curveValues} position_error={_positionError:R} quaternion_error={_rotationError:R} scale_error={_scaleError:R} curve_error={_curveError:R} evaluation={evaluation} owner=independent_component");
    }

    private static void VerifyNoAlloc(AlsRawAnimationSourceBank bank, AlsAnimationSetDefinition set,
        string[] names, bool additive)
    {
        var sources = bank.Sources.ToArray();
        var samplers = sources.Select(s => CreateSampler(s, bank, set, names, additive)).ToArray();
        var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[names.Length];
        for (var frame = 0; frame < 64; frame++)
            for (var source = 0; source < sources.Length; source++)
                samplers[source](sources[source].PoseData.PlayLength * frame / 63, frame % 2 == 0,
                    frame % 3 == 0, frame % 5 == 0, pose, curves);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 64; frame++)
            for (var source = 0; source < sources.Length; source++)
                samplers[source](sources[source].PoseData.PlayLength * frame / 63, frame % 2 == 0,
                    frame % 3 == 0, frame % 5 == 0, pose, curves);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"Raw source hot sampling allocated {allocated} bytes.");
        GD.Print($"ANIMATION_SOURCE_ALLOCATION_OK additive={additive} samples={64 * sources.Length} allocated_bytes={allocated}");
    }

    private static void VerifyParallel(AlsRawAnimationSourceBank bank, AlsAnimationSetDefinition set, string[] names, bool evaluateAdditive)
    {
        const int owners = 4, frames = 120;
        // All owners share immutable source objects; each owns every mutable sampler scratch buffer.
        var sources = bank.Sources.ToArray();
        var sequential = Enumerable.Range(0, owners).Select(owner => Trajectory(owner, bank, set, sources, names, frames, evaluateAdditive)).ToArray();
        using var start = new Barrier(owners);
        var tasks = Enumerable.Range(0, owners).Select(owner => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait();
            return (Thread: System.Environment.CurrentManagedThreadId, Digest: Trajectory(owner, bank, set, sources, names, frames, evaluateAdditive));
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        Task.WaitAll(tasks);
        Require(tasks.Select(t => t.Result.Thread).Distinct().Count() == owners, "Parallel check did not use distinct owners.");
        for (var owner = 0; owner < owners; owner++) Require(tasks[owner].Result.Digest == sequential[owner], "Shared source bank changed owner trajectory.");
        var tag = evaluateAdditive ? "ANIMATION_ADDITIVE_SOURCE" : "RAW_ANIMATION_SOURCE";
        GD.Print($"{tag}_PARALLEL_OK owners={owners} single_samples={owners * frames * sources.Length} parallel_samples={owners * frames * sources.Length} all_pose_and_curve_bits_equal=true");
    }

    private static string Trajectory(int owner, AlsRawAnimationSourceBank bank, AlsAnimationSetDefinition set,
        AlsRawAnimationSourceDefinition[] sources, string[] names, int frames, bool evaluateAdditive)
    {
        var samplers = sources.Select(s => CreateSampler(s, bank, set, names, evaluateAdditive)).ToArray();
        var pose = new AlsLocalPose[79]; var curves = new AlsInertialCurve[names.Length];
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[(79 * 10 + names.Length * 2) * sizeof(int)];
        var components = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(bytes);
        for (var frame = 0; frame < frames; frame++)
            for (var source = 0; source < sources.Length; source++)
            {
                var time = sources[source].PoseData.PlayLength * ((frame * 13 + owner * 7) % 127) / 126;
                samplers[source](time, frame % 3 != 0, frame % 4 == 0, frame % 5 == 0, pose, curves);
                var index = 0;
                foreach (var p in pose)
                {
                    components[index++] = BitConverter.SingleToInt32Bits(p.Position.X); components[index++] = BitConverter.SingleToInt32Bits(p.Position.Y); components[index++] = BitConverter.SingleToInt32Bits(p.Position.Z);
                    components[index++] = BitConverter.SingleToInt32Bits(p.Rotation.X); components[index++] = BitConverter.SingleToInt32Bits(p.Rotation.Y); components[index++] = BitConverter.SingleToInt32Bits(p.Rotation.Z); components[index++] = BitConverter.SingleToInt32Bits(p.Rotation.W);
                    components[index++] = BitConverter.SingleToInt32Bits(p.Scale.X); components[index++] = BitConverter.SingleToInt32Bits(p.Scale.Y); components[index++] = BitConverter.SingleToInt32Bits(p.Scale.Z);
                }
                foreach (var c in curves) { components[index++] = BitConverter.SingleToInt32Bits(c.Value); components[index++] = c.Present ? 1 : 0; }
                digest.AppendData(bytes);
            }
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static SampleSource CreateSampler(AlsRawAnimationSourceDefinition source, AlsRawAnimationSourceBank bank,
        AlsAnimationSetDefinition set, string[] names, bool additive) => additive
        ? new AlsAnimationPoseSourceSampler(source, bank, set, names).Sample
        : new AlsRawAnimationSourceSampler(source, bank.GetSkeleton(source.PoseData.Identity.SkeletonId), set, names).Sample;

    private static AlsLocalPose ConvertNative(JsonElement value)
    {
        // Independent direct coordinate relation, without an Euler or matrix round trip.
        var p = value.GetProperty("position"); var q = value.GetProperty("rotation"); var s = value.GetProperty("scale");
        return new(new(p[0].GetSingle() * .01f, -p[1].GetSingle() * .01f, p[2].GetSingle() * .01f),
            new(-q[0].GetSingle(), q[1].GetSingle(), -q[2].GetSingle(), q[3].GetSingle()),
            new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
    private static string Read(string path) => Godot.FileAccess.GetFileAsString("res://" + path);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
