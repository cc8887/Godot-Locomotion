using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation;

internal static class BaseLayerPoseChecks
{
    public static void Run(AlsBaseLayerProfile profile)
    {
        var hidden = 0; var modified = 0; var guards = 0; var faults = 0;
        foreach (var hz in new[] { 30, 60, 120 })
        {
            var tail = new AlsBaseLayerPoseRuntime(profile, 2, 1);
            var oracle = new AlsInertialization(2, 1, .01f, -NVector3.UnitX);
            var slot = new Slot();
            var raw = new[] { AlsLocalPose.Identity, AlsLocalPose.Identity };
            var expected = new AlsLocalPose[2]; var output = new AlsLocalPose[2];
            var inputCurve = new AlsInertialCurve[1]; var expectedCurve = new AlsInertialCurve[1]; var outputCurve = new AlsInertialCurve[1];
            for (var frame = 1; frame <= 18; frame++)
            {
                // All three regimes: passthrough, partial montage, complete override.
                var absent = frame is >= 8 and <= 10;
                var weights = absent ? new AlsSlotWeights(0, 1, 1) : frame is >= 4 and <= 7 ?
                    new AlsSlotWeights(.6f, .4f, .4f) : AlsSlotWeights.Passthrough;
                var context = new AlsPoseUpdateContext(new(frame, 19, 2), .8f, 1f / hz, .3f);
                var update = tail.Begin(context, weights);
                if (frame == 1)
                {
                    Reject(() => tail.Commit(context.Identity)); guards++;
                    Reject(() => tail.RequestInertialization(context.WithInertialization(999, true), .2f)); guards++;
                }
                if (absent)
                {
                    Require(!update.Updated, "Fully overriding Slot updated the hidden movement source.");
                    Reject(() => tail.RequestInertialization(context.WithInertialization(profile.InertializationNodeIndex, true), .2f)); guards++;
                    var before = tail.CommittedHistoryCount;
                    tail.Evaluate([], [], AlsLocalPose.Identity, 0, 0, output, outputCurve, slot);
                    tail.Commit(context.Identity);
                    Require(tail.CommittedHistoryCount == before && slot.SourceEmpty && output[1].Position.X == 10 &&
                        outputCurve[0] == new AlsInertialCurve(5), "Hidden source altered history or Slot output.");
                    hidden++; continue;
                }
                Require(update.Updated && update.Context.InertializationRequester == profile.InertializationNodeIndex &&
                    update.Context.SkippedUpdateHandler == profile.InertializationNodeIndex &&
                    update.Context.Weight == context.Weight * weights.SourceWeight && update.Context.RootMotionWeight == .3f,
                    "BaseLayer lost its source update context.");
                if (frame == 4) Require(!update.Context.IsActive, "Fading out source was not marked inactive.");
                // Discontinuous bone and disappearing curve exercise retained source history.
                raw[1] = raw[1] with { Position = new NVector3(frame < 3 ? 0 : frame < 11 ? 1 : -1, 0, 0) };
                inputCurve[0] = frame < 3 ? new(1) : default;
                oracle.Update(context.Delta);
                if (frame is 3 or 5 or 11)
                {
                    tail.RequestInertialization(update.Context, .3f); tail.RequestInertialization(update.Context, .2f);
                    oracle.Request(.2f);
                }
                oracle.Evaluate(raw, inputCurve, AlsLocalPose.Identity, 0, 0, expected, expectedCurve);
                if (!raw.AsSpan().SequenceEqual(expected)) modified++;
                // The independent oracle never sees the montage output (10 metres / curve 5).
                if (weights.SlotNodeWeight > 0) Slot.Compose(weights, expected, expectedCurve, expected, expectedCurve);
                tail.Evaluate(raw, inputCurve, AlsLocalPose.Identity, 0, 0, output, outputCurve, slot);
                Require(expected.AsSpan().SequenceEqual(output) && expectedCurve.AsSpan().SequenceEqual(outputCurve),
                    "BaseLayer order, skipped time or inertia history differs from the source-only oracle.");
                tail.Commit(context.Identity);
            }
            Reject(() => tail.Begin(new(new(18, 19, 2), 1, .01f), AlsSlotWeights.Passthrough)); guards++;
            Reject(() => tail.Begin(new(new(19, 20, 2), 1, .01f), AlsSlotWeights.Passthrough)); guards++;
            Reject(() => tail.Begin(new(new(19, 19, 3), 1, .01f), AlsSlotWeights.Passthrough)); guards++;
            var prior = tail.CommittedIdentity;
            var next = new AlsPoseUpdateContext(new(19, 19, 2), 1, 1f / hz);
            tail.Begin(next, new(0, 1, 1));
            Reject(() => tail.Evaluate([], [], AlsLocalPose.Identity, 0, 0, output, outputCurve));
            Require(tail.IsFaulted && tail.CommittedIdentity == prior, "Missing montage owner silently produced a pose.");
            tail.Discard(); faults++;
            tail.Begin(next, new(0, 1, 1)); slot.Invalid = true;
            Reject(() => tail.Evaluate([], [], AlsLocalPose.Identity, 0, 0, output, outputCurve, slot));
            Require(tail.IsFaulted && tail.CommittedIdentity == prior, "Invalid final Slot output became committable.");
            tail.Discard(); slot.Invalid = false; faults++;
            tail.Begin(next, new(0, 1, 1));
            tail.Evaluate([], [], AlsLocalPose.Identity, 0, 0, output, outputCurve, slot);
            tail.Commit(next.Identity);
        }
        Require(hidden == 9 && modified > 0, "BaseLayer lifecycle coverage missing.");
        GD.Print($"BASE_LAYER_POSE_CHECKS_OK hidden={hidden} changed_by_inertia={modified} guards={guards} faults={faults} history=source_only skipped_time=frozen");
        CheckTraversalLifecycle(profile);
    }

    private static void CheckTraversalLifecycle(AlsBaseLayerProfile profile)
    {
        var tail=new AlsBaseLayerPoseRuntime(profile,2,1);
        var traversal=default(AlsAnimationGraphFrame).Next(new(1,19,2),1);
        var raw=new[]{AlsLocalPose.Identity,AlsLocalPose.Identity};
        var output=new AlsLocalPose[2]; var curves=new AlsInertialCurve[1];
        tail.PrepareUnvisited(traversal); tail.Commit(traversal.Identity);
        Require(tail.CommittedIdentity==default && tail.CommittedHistoryCount==0,"Cold hidden initialization evaluated a pose.");
        Reject(()=>tail.PrepareUnvisited(traversal));
        Reject(()=>tail.PrepareUnvisited(traversal with {Identity=new(2,20,2)}));
        Reject(()=>tail.PrepareUnvisited(traversal with {Identity=new(2,19,3)}));
        Reject(()=>tail.Begin(new(traversal.Identity,1,.01f),AlsSlotWeights.Passthrough,traversal));
        traversal=traversal.Next(new(2,19,2),2);
        Visit();
        var poseIdentity=tail.CommittedIdentity; var history=tail.CommittedHistoryCount;
        Require(history>0,"Visited pose did not build inertia history.");
        traversal=traversal.Next(new(3,19,2),3);
        traversal=traversal with {Bones=traversal.Bones.Next(3)};
        tail.PrepareUnvisited(traversal); tail.Commit(traversal.Identity);
        Require(tail.CommittedHistoryCount==history && tail.CommittedIdentity==poseIdentity,"Hidden bone refresh reset inertia or evaluated a pose.");
        Reject(()=>tail.Begin(new(traversal.Identity,1,.01f),AlsSlotWeights.Passthrough,traversal));
        traversal=traversal.Next(new(4,19,2),4);
        traversal=traversal with {Initialization=new(traversal.Initialization.Counter,4)};
        tail.PrepareUnvisited(traversal); tail.Commit(traversal.Identity);
        Require(tail.CommittedHistoryCount==history,"Initialization global-frame change reset inertia despite equal counters.");
        traversal=traversal.Next(new(5,19,2),5);
        traversal=traversal with {Initialization=traversal.Initialization.Next(5)};
        tail.PrepareUnvisited(traversal); tail.Discard();
        Require(tail.CommittedHistoryCount==history,"Discarded initialization leaked inertia reset.");
        tail.PrepareUnvisited(traversal); tail.Commit(traversal.Identity);
        Require(tail.CommittedHistoryCount==0 && tail.CommittedIdentity==poseIdentity,"Explicit hidden initialization failed to reset inertia independently of pose identity.");
        traversal=traversal.Next(new(6,19,2),6); Visit();
        GD.Print("BASE_LAYER_TAIL_TRAVERSAL_OK cold_hidden=true stale_foreign_rejected=true bones_preserve=true init_counter_only=true reset_retry=true resume=true");
        void Visit()
        {
            tail.Begin(new(traversal.Identity,1,.01f),AlsSlotWeights.Passthrough,traversal);
            tail.Evaluate(raw,new AlsInertialCurve[1],AlsLocalPose.Identity,0,0,output,curves);
            tail.Commit(traversal.Identity);
        }
    }

    private sealed class Slot : IAlsBaseLayerSlotPoseSink
    {
        public bool SourceEmpty;
        public bool Invalid;
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> output, Span<AlsInertialCurve> curves)
        {
            SourceEmpty = source.IsEmpty; Compose(weights, source, sourceCurves, output, curves);
            if (Invalid) output[0] = output[0] with { Position = new NVector3(float.NaN, 0, 0) };
        }
        public void EvaluateSlot(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
        {
            SourceEmpty = source.IsEmpty; Compose(weights, source, sourceCurves, output, curves);
            if (Invalid) output[0] = output[0] with { Position = new AlsDoubleVector(float.NaN, 0, 0) };
        }

        public static void Compose(in AlsSlotWeights weights, ReadOnlySpan<AlsLocalPose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> output, Span<AlsInertialCurve> curves)
        {
            for (var i = 0; i < output.Length; i++)
            {
                var montage = AlsLocalPose.Identity with { Position = new NVector3(i * 10, 0, 0) };
                output[i] = source.IsEmpty ? montage : AlsPoseBlender.Normalize(AlsPoseBlender.BlendRaw(source[i], montage, weights.SlotNodeWeight));
            }
            curves[0] = sourceCurves.IsEmpty ? new(5) : AlsStandingCycleCurves.Lerp(sourceCurves[0], new(5), weights.SlotNodeWeight);
        }
        public static void Compose(in AlsSlotWeights weights, ReadOnlySpan<AlsPrecisePose> source,
            ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsPrecisePose> output, Span<AlsInertialCurve> curves)
        {
            for (var i = 0; i < output.Length; i++)
            {
                var montage = AlsPrecisePose.Identity with { Position = new AlsDoubleVector(i * 10, 0, 0) };
                output[i] = source.IsEmpty ? montage : AlsPrecisePoseBlender.Normalize(AlsPrecisePoseBlender.BlendRaw(source[i], montage, weights.SlotNodeWeight));
            }
            curves[0] = sourceCurves.IsEmpty ? new(5) : AlsStandingCycleCurves.Lerp(sourceCurves[0], new(5), weights.SlotNodeWeight);
        }
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("BaseLayer guard accepted invalid work.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
