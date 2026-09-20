using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Exercises failures inside the cached source and after a successful cache fill.
// The uncached stage is a composition oracle, not an independent UE pose oracle.
public partial class PostLayeringCacheSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private static void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var profile = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var definition = AlsMovementGraphDefinition.Load(set, locomotion, profile).WithSharedOverlaySources(set);
        var names = definition.AimRawSources.Sources.ToArray().SelectMany(s => s.Policy.FloatCurveNames.ToArray())
            .Append(definition.AimLayer.SpineCurve).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var skeleton = definition.AimRawSources.GetSkeleton(profile.SkeletonId);
        var spineCurve = Array.IndexOf(names, definition.AimLayer.SpineCurve);
        var frames = 0; var dual = 0; var sourceFaults = 0; var outputFaults = 0;
        foreach (var hz in new[] { 30, 60, 120 })
        {
            var cached = new AlsAimLayerFrameStage(definition, set, names, 11, 2);
            var reference = new AlsAimLayerFrameStage(definition, set, names, 11, 2);
            var source = new Source(skeleton.ReferencePose.ToArray(), new AlsInertialCurve[names.Length]);
            var feedback = new AlsInertialCurve[names.Length];
            var traversal = default(AlsAnimationGraphFrame);
            for (var frame = 1; frame <= hz * 2; frame++)
            {
                // Skipped character identities must not skip traversal counters.
                var id = new AlsFrameIdentity(frame * 32768L, 11, 2);
                var candidateTraversal = traversal.Next(id, (ulong)frame);
                feedback[spineCurve] = new((frame / 10 % 3) * .5f);
                var input = default(AlsLayeringInput) with { Identity = id, EnableAimOffset = frame % 7 == 0 ? 0 : .7 };
                var context = new AlsPoseUpdateContext(id, 1, 1f / hz, 1);
                var observation = new AlsAimingObservation(id, context.Delta, AlsRotationMode.LookingDirection, true,
                    new(0, 0, 0), new(35 * Math.Sin(frame * .1), 80 * Math.Cos(frame * .2), 0), 3, 0);
                source.Pose[0] = source.Pose[0] with { Position = new(frame * .003f, 0, 0) };
                var oldIdentity = cached.CommittedIdentity;
                var oldInput = cached.CommittedInput;
                var oldAim = cached.CommittedAim;
                reference.Prepare(context, input, observation, feedback,candidateTraversal,cachePostLayering:false);
                reference.Evaluate(source.Pose, source.Curves);

                Prepare(); EvaluateAndCompare(); cached.Cancel(); CheckHistory();
                Prepare(); source.Fault = 1;
                var threw = false;
                try { cached.Evaluate(source); }
                catch (SourceFailure) { sourceFaults++; threw = true; }
                Require(threw, "Expected a failure inside the Post Layering source.");
                CheckHistory();

                Prepare(); source.Fault = 2; threw = false;
                try { cached.Evaluate(source); }
                catch (ArgumentException) { outputFaults++; threw = true; }
                Require(threw, "Expected validation failure after filling the Post Layering cache.");
                CheckHistory();

                Prepare(); EvaluateAndCompare();
                if (cached.PostCacheReads == 2) dual++;
                cached.ValidateCommit(id); cached.Commit(id); reference.Commit(id);
                traversal = candidateTraversal; frames++;

                void Prepare()
                {
                    source.Fault = 0; source.Calls = 0;
                    cached.Prepare(context, input, observation, feedback, candidateTraversal);
                }
                void EvaluateAndCompare()
                {
                    cached.Evaluate(source);
                    Require(source.Calls == 1 && cached.PostSourceEvaluations == 1 && cached.PostCacheReads is 1 or 2,
                        "A root scope must evaluate Post Layering exactly once.");
                    Require(cached.Pose.SequenceEqual(reference.Pose) && cached.Curves.SequenceEqual(reference.Curves),
                        "Retry reused a partial/stale cache payload or changed the composition.");
                    AimFrameSmokeChecks.Same(reference.CandidateAim, cached.CandidateAim);
                }
                void CheckHistory()
                {
                    Require(cached.CommittedIdentity == oldIdentity && cached.CommittedInput == oldInput,
                        "Rejected cache evaluation committed its frame/input history.");
                    AimFrameSmokeChecks.Same(oldAim, cached.CommittedAim);
                }
            }
        }
        Require(dual > 0 && sourceFaults == frames && outputFaults == frames, "Incomplete cache failure coverage.");
        GD.Print($"POST_LAYERING_CACHE_OK frames={frames} dual_reads={dual} source_faults={sourceFaults} after_fill_faults={outputFaults} retries={frames} source_evaluations_per_scope=1");
    }

    private sealed class SourceFailure : Exception { }
    private sealed class Source(AlsLocalPose[] pose, AlsInertialCurve[] curves) : IAlsPostLayeringPoseSource
    {
        public AlsLocalPose[] Pose { get; } = pose;
        public AlsInertialCurve[] Curves { get; } = curves;
        public int Fault, Calls;
        public void EvaluatePostLayering(Span<AlsLocalPose> output, Span<AlsInertialCurve> outputCurves)
        {
            Calls++;
            Pose.CopyTo(output); Curves.CopyTo(outputCurves);
            if (Fault == 1)
            {
                output[0] = output[0] with { Position = new(999, 999, 999) };
                throw new SourceFailure();
            }
            if (Fault == 2) outputCurves[0] = new(float.NaN);
        }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
