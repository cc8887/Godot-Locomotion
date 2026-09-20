using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

public partial class BasePosesSmoke : Node
{
    private float _maxPositionError, _maxRotationError;
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
        using var library = AlsAnimationLibraryBuilder.BuildP5a(set, definition.Binding);
        AddChild(library.Root);
        string[] curveNames = ["BasePose_N", "BasePose_CLF", "Layering_Arm_L", "NotAuthored"];
        var sampler = new AlsBasePosesSourceSampler(definition.BasePoses, set, library, curveNames,
            definition.BasePoseRetarget, definition.BasePoseSourceKeys);
        var layout = sampler.Layout;
        Require(layout.Names.Length == 79 && layout.PhysicalReferencePose.Length == 68 && layout.VirtualCount == 11,
            "BasePoses must use the complete 79-bone logical pose.");
        var samples = new[] { new AlsLocalPose[79], new AlsLocalPose[79] };
        var output = new AlsLocalPose[79]; var expected = new AlsLocalPose[79];
        var curves = new AlsInertialCurve[curveNames.Length];
        using var native = JsonDocument.Parse(Read("tests/Als.Core.Tests/Fixtures/P3/v4_logical_pose_native.json"));
        for (var source = 0; source < 2; source++)
        {
            var evaluator = definition.BasePoses.Evaluators[source];
            var keys = definition.BasePoseSourceKeys[source];
            Require(keys.SampledKeyCount == 2 && keys.PhysicalBoneCount == 68 &&
                keys.FrameRateNumerator == 30 && keys.FrameRateDenominator == 1,
                "BasePoses must retain all authored source keys, independent of the time-zero test oracle.");
            sampler.EvaluateEvaluator(evaluator, 0, samples[source], curves);
            Require(curves.All(c => !c.Present), "BasePoses must not fabricate BasePose or Layering curves.");
            var asset = native.RootElement.GetProperty("assets").EnumerateArray()
                .Single(a => a.GetProperty("source").GetString() == evaluator.AssetPath);
            Require(asset.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).SequenceEqual(layout.Names),
                "Native and imported logical bone orders differ.");
            Require(asset.GetProperty("logicalParents").EnumerateArray().Select(n => n.GetInt32()).SequenceEqual(layout.Parents),
                "Native and imported logical parents differ.");
            Require(asset.GetProperty("tracks").EnumerateArray().All(t => !t.GetString()!.StartsWith("VB ")) &&
                asset.GetProperty("curveNames").GetArrayLength() == 0, "BasePoses source track policy differs.");
            var poses = asset.GetProperty("pose").EnumerateArray().Select(ConvertNative).ToArray();
            Compare(poses, samples[source], layout.Names, "native " + evaluator.AssetPath);
            var raw = asset.GetProperty("rawPoseWithoutRetarget").EnumerateArray().Select(ConvertNative).ToArray();
            sampler.SampleKeyBeforeRetarget(evaluator, output);
            Compare(raw, output, layout.Names, "native raw " + evaluator.AssetPath);
        }
        var nativePositionError = _maxPositionError; var nativeRotationError = _maxRotationError;
        _maxPositionError = _maxRotationError = 0;

        var frames = 0; var retries = 0; var faults = 0; var zeroGlobal = 0;
        foreach (var rate in new[] { 30, 60, 120 })
        {
            var runtime = new AlsBasePosesRuntime(definition.BasePoses, layout.ReferencePose, curveNames);
            var sink = new Sink(sampler);
            var saved = new AlsLocalPose[79]; var savedCurves = new AlsInertialCurve[curves.Length];
            for (var frame = 1; frame <= rate * 4; frame++)
            {
                var identity = new AlsFrameIdentity(frame, 4, 2);
                var phase = (frame - 1) * 6 / (rate * 4);
                var weights = phase switch
                {
                    0 => NVector2.UnitX, 1 => new NVector2(.25f, .75f), 2 => NVector2.UnitY,
                    3 => NVector2.Zero, 4 => new NVector2(2, 6), _ => NVector2.UnitX,
                };
                var input = default(AlsLayeringInput) with
                { Identity = identity, BasePoseNormal = weights.X, BasePoseCrouching = weights.Y };
                var context = new AlsPoseUpdateContext(identity, frame % 17 == 0 ? 0 : 1, 1f / rate);
                var before = runtime.State;
                if (frame % 53 == 0 && phase != 3)
                {
                    sink.FailEvaluation = true;
                    try { Tick(runtime, sink, context, input, frame, rate, output, curves); throw new InvalidOperationException("Missing injected failure."); }
                    catch (SourceFailure) { faults++; }
                    Require(!runtime.HasCandidate && runtime.State == before, "Failed BasePoses leaked source history.");
                    sink.FailEvaluation = false;
                }
                Tick(runtime, sink, context, input, frame, rate, output, curves);
                var state = runtime.State;
                Expected(samples, layout.ReferencePose, state.CachedAlphas, expected);
                Compare(expected, output, layout.Names, "multiway");
                Require(curves.All(c => !c.Present), "BasePoses curves became present during mixing.");
                output.CopyTo(saved, 0); curves.CopyTo(savedCurves, 0);
                runtime.Cancel(); Require(runtime.State == before, "Cancelled BasePoses leaked source state.");
                Tick(runtime, sink, context, input, frame, rate, output, curves);
                Require(runtime.State == state && curves.SequenceEqual(savedCurves), "BasePoses same-frame retry differs.");
                Compare(saved, output, layout.Names, "retry");
                if (context.Weight == 0 && weights != NVector2.Zero)
                {
                    Require(sink.Updates > 0, "Global zero weight incorrectly skipped relevant evaluators."); zeroGlobal++;
                }
                runtime.Commit(); frames++; retries++;
            }
        }
        AlsBasePosesParallelCheck.Run(definition, set, library, curveNames);
        GD.Print($"BASE_POSES_LOGICAL_RUNTIME_OK frames={frames} retries={retries} faults={faults} zero_global={zeroGlobal} assets=2 keys_per_asset=2 physical=68 logical=79 virtual=11 curves=absent native_position_error={nativePositionError:R} native_quaternion_error={nativeRotationError:R} blend_position_error={_maxPositionError:R} blend_quaternion_error={_maxRotationError:R} owner=independent_component");
    }

    private static void Tick(AlsBasePosesRuntime runtime, Sink sink, in AlsPoseUpdateContext context,
        in AlsLayeringInput input, int frame, int rate, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
    {
        sink.Updates = 0; runtime.BeginCandidate(context.Identity, sink);
        if (frame == 1 || frame == rate * 2) runtime.Initialize(new((short)(frame == 1 ? 0 : 1), (ulong)frame));
        if (frame == 1 || frame == rate * 3) runtime.CacheBones(new((short)(frame == 1 ? 0 : 1), (ulong)frame));
        runtime.Update(context, input); runtime.Evaluate(pose, curves);
    }
    private static void Expected(AlsLocalPose[][] samples, AlsLocalPose[] reference, NVector2 alpha, AlsLocalPose[] output)
    {
        // Controlled cases have exactly zero, one full source, or .25/.75. Native numeric
        // operator boundary cases are checked separately in Core, not derived from this helper.
        if (alpha == NVector2.Zero) reference.CopyTo(output, 0);
        else if (alpha == NVector2.UnitX) samples[0].CopyTo(output, 0);
        else if (alpha == NVector2.UnitY) samples[1].CopyTo(output, 0);
        else
            for (var bone = 0; bone < output.Length; bone++)
            {
                var a = samples[0][bone]; var b = samples[1][bone];
                var q = NQuaternion.Dot(a.Rotation, b.Rotation) < 0 ? -b.Rotation : b.Rotation;
                output[bone] = new(a.Position * .25f + b.Position * .75f,
                    NQuaternion.Normalize(a.Rotation * .25f + q * .75f), a.Scale * .25f + b.Scale * .75f);
            }
    }
    private void Compare(AlsLocalPose[] expected, AlsLocalPose[] actual, string[] names, string stage)
    {
        for (var bone = 0; bone < expected.Length; bone++)
        {
            var p = NVector3.Distance(expected[bone].Position, actual[bone].Position);
            var q = MathF.Min((expected[bone].Rotation - actual[bone].Rotation).Length(), (expected[bone].Rotation + actual[bone].Rotation).Length());
            var s = NVector3.Distance(expected[bone].Scale, actual[bone].Scale);
            _maxPositionError = MathF.Max(_maxPositionError, p); _maxRotationError = MathF.Max(_maxRotationError, q);
            Require(p <= .00002f && q <= .00002f && s <= .00005f,
                $"{stage}: {names[bone]} pose differs: position={p:R}, quaternion={q:R}, scale={s:R}; expected={expected[bone]}, actual={actual[bone]}.");
        }
    }
    private static AlsLocalPose ConvertNative(JsonElement value)
    {
        var p = value.GetProperty("position"); var q = value.GetProperty("rotation"); var s = value.GetProperty("scale");
        return AlsFbxBonePoseSpace.FromCanonical(new(AlsCoordinateConverter.PositionCentimetersToMeters(new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())),
            AlsCoordinateConverter.Rotation(new(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle())),
            AlsCoordinateConverter.Scale(new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()))));
    }
    private sealed class SourceFailure : Exception;
    private sealed class Sink(AlsBasePosesSourceSampler source) : IAlsBasePosesSink
    {
        public int Updates;
        public bool FailEvaluation;
        public void InitializeEvaluator(in AlsBasePoseEvaluatorDefinition evaluator) => source.InitializeEvaluator(evaluator);
        public void CacheEvaluatorBones(in AlsBasePoseEvaluatorDefinition evaluator) => source.CacheEvaluatorBones(evaluator);
        public void UpdateEvaluator(in AlsBasePoseEvaluatorDefinition evaluator, in AlsBasePoseEvaluatorTick tick, in AlsPoseUpdateContext context)
        { source.UpdateEvaluator(evaluator, tick, context); Updates++; }
        public void EvaluateEvaluator(in AlsBasePoseEvaluatorDefinition evaluator, float seconds, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
        { source.EvaluateEvaluator(evaluator, seconds, pose, curves); if (FailEvaluation) throw new SourceFailure(); }
    }
    private static string Read(string path) => Godot.FileAccess.GetFileAsString("res://" + path);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
