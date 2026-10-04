using Godot;
using GodotAls.Core.Locomotion;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;

namespace GodotAls.Animation.Lyra;

public partial class LyraPoseBuffersSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Lyra pose buffers failed: " + error); GetTree().Quit(1); }
    }

    private void Run()
    {
        using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true,
            includePistol: true, includeRifle: true);
        AddChild(rig.Root);
        var contracts = LyraLinkedLayerContracts.Load();
        ILyraItemAnimationLayers[] providers = [
            new LyraUnarmedAnimationLayers(rig.Catalog, rig.Auxiliary, rig.Remaining),
            new LyraPistolAnimationLayers(rig.Pistol!), new LyraRifleAnimationLayers(rig.Rifle!)];
        var pipeline = new LyraLayerPosePipeline(rig.Skeleton);
        var rootYaw = new LyraRootYawOffset(rig.Skeleton, LyraRootYawDefaults.Load());
        var input = new AlsLocalPose[68];
        var output = new AlsLocalPose[68];
        var actual = new AlsLocalPose[68];
        var frames = 0;
        var rejected = 0;
        foreach (var provider in providers)
        {
            var buffered = new LyraItemLayerInstance(rig, provider, contracts);
            var adapters = new LyraItemLayerInstance(rig, provider, contracts);
            foreach (var hz in new[] { 30, 60, 120 })
            {
                var slot = provider.ResolveStateClip(LyraLayerHook.FullBody_CycleState,
                    new(LyraCardinalDirection.Forward, LyraGait.Jog));
                rig.Player.Play(rig.QualifiedName(slot));
                for (var frame = 0; frame < hz * 2; frame++)
                {
                    rig.Player.Seek(frame / (double)hz % rig.SourceClip(slot).PlayLength, true);
                    LyraUnarmedAimOffset.CapturePose(rig.Skeleton, input);
                    var original = input.ToArray();
                    var evaluation = new LyraPoseEvaluation(frame % 5 == 0 ? LyraMotionPhase.Idle : LyraMotionPhase.Cycle,
                        frame % 3 == 0, frame % 4 < 2, 40 - frame % 80, frame % 90 - 45,
                        frame % 3 * 0.5f, frame % 4 * 0.5f, frame % 3 * 0.5f);
                    foreach (var item in new[] { buffered, adapters })
                        item.HipFire.UpdateWeight(evaluation.IsCrouching, evaluation.IsOnGround,
                            frame % 2 == 0, frame % 3 == 0 ? 0 : double.MaxValue, 0,
                            rootYaw.OffsetDegrees, true, 1d / hz);
                    rootYaw.QueueMode(LyraMotionPhase.Idle);
                    rootYaw.Update(frame % 90, 1d / hz, evaluation.IsCrouching);

                    // Deliberately make the visible skeleton differ from InputPose.
                    // Reading it anywhere inside a layer would corrupt this result.
                    var sentinel = input.ToArray();
                    for (var bone = 0; bone < sentinel.Length; bone++)
                        sentinel[bone] = sentinel[bone] with { Position = sentinel[bone].Position + new NVector3(1, 2, 3) };
                    LyraUnarmedAimOffset.WritePose(rig.Skeleton, sentinel);
                    pipeline.Evaluate(buffered, rootYaw, evaluation, input, output);
                    LyraUnarmedAimOffset.CapturePose(rig.Skeleton, actual);
                    if (!input.SequenceEqual(original) || !actual.SequenceEqual(sentinel))
                        throw new InvalidOperationException("A layer mutated its input or accessed the visible pose.");

                    // Compare against the existing Skeleton3D adapters, whose aim,
                    // mask and hand math retain their separate native/asset checks.
                    LyraUnarmedAimOffset.WritePose(rig.Skeleton, input);
                    if (evaluation.Phase != LyraMotionPhase.Idle) adapters.HipFire.Apply(evaluation.IsCrouching);
                    adapters.LeftHand.Apply(evaluation.LeftHandDisable);
                    adapters.Aiming.Apply(evaluation.AimYaw, evaluation.AimPitch, adapters.HipFire.AimOffsetBlendWeight);
                    adapters.Additives.Apply(evaluation.IsOnGround);
                    rootYaw.ApplyRootPose();
                    adapters.HandRetarget.Apply(evaluation.HandRetargetDisable);
                    adapters.RightHandIk.Apply(evaluation.RightHandDisable);
                    LyraUnarmedAimOffset.CapturePose(rig.Skeleton, actual);
                    AssertPose(actual, output);
                    adapters.RightHandIk.RestoreBase();
                    adapters.HandRetarget.RestoreBase();
                    rootYaw.RestoreBase();
                    adapters.Additives.RestoreBase();
                    adapters.Aiming.RestoreBase();
                    adapters.LeftHand.RestoreBase();
                    adapters.HipFire.RestoreBase();
                    LyraUnarmedAimOffset.CapturePose(rig.Skeleton, actual);
                    if (!actual.SequenceEqual(original)) throw new InvalidOperationException("Adapter restoration drifted.");

                    pipeline.Apply(buffered, rootYaw, evaluation);
                    LyraUnarmedAimOffset.CapturePose(rig.Skeleton, actual);
                    AssertPose(actual, output);
                    pipeline.RestoreBase();
                    pipeline.RestoreBase();
                    LyraUnarmedAimOffset.CapturePose(rig.Skeleton, actual);
                    if (!actual.SequenceEqual(original)) throw new InvalidOperationException("Final writer did not restore the source.");
                    frames++;
                }
            }
            var bypass = new LyraPoseEvaluation(LyraMotionPhase.Idle, false, true, 0, 0, 1, 1);
            var overlapping = new AlsLocalPose[69];
            rejected += Reject(() => pipeline.Evaluate(buffered, rootYaw, bypass, input, input));
            rejected += Reject(() => pipeline.Evaluate(buffered, rootYaw, bypass, input, new AlsLocalPose[67]));
            rejected += Reject(() => pipeline.Evaluate(buffered, rootYaw, bypass, overlapping.AsSpan(0, 68), overlapping.AsSpan(1, 68)));
            rejected += Reject(() => buffered.LeftHand.EvaluatePose(input, 1, input));
            rejected += Reject(() => buffered.HandRetarget.EvaluatePose(input, 1, input));
            rejected += Reject(() => buffered.HipFire.EvaluatePose(input, false, input));
            rejected += Reject(() => buffered.Aiming.EvaluatePose(input, 0, 0, 0, input));
            rejected += Reject(() => rootYaw.EvaluatePose(input, input));
            rejected += Reject(() => buffered.Additives.EvaluateAdditive(true, new AlsLocalPose[67]));
            rejected += Reject(() => pipeline.Evaluate(buffered, rootYaw, bypass with { HandRetargetDisable = float.NaN }, input, output));
        }
        // Current source additives are identity. Exercise the main graph's 0.65
        // factor with a nonidentity fixture so a lost alpha cannot hide behind it.
        AlsLocalPose[] basis = [new(new NVector3(2, 3, 4), NQuaternion.Identity, new NVector3(2, 2, 2))];
        AlsLocalPose[] additive = [new(new NVector3(10, 0, 0), NQuaternion.Identity, new NVector3(1, 0, 0))];
        var composed = new AlsLocalPose[1];
        LyraLayerPosePipeline.ApplyFullBodyAdditive(basis, additive, composed);
        if (composed[0].Position.X != 8.5f || composed[0].Scale.X != 3.3f ||
            composed[0].Rotation != NQuaternion.Identity)
            throw new InvalidOperationException("The original main ApplyAdditive alpha differs.");
        if (frames != 1260 || pipeline.AppliedFrames != frames || rejected != 30)
            throw new InvalidOperationException("Pose buffer coverage is incomplete.");
        GD.Print($"LYRA_POSE_BUFFERS_OK profiles=3 frames={frames} rejected={rejected} " +
            $"commits={pipeline.AppliedFrames} additiveAlpha={LyraLayerPosePipeline.FullBodyAdditiveAlpha} skeleton=68");
    }

    private static int Reject(Action operation)
    {
        try { operation(); }
        catch (ArgumentException) { return 1; }
        throw new InvalidOperationException("Invalid pose buffers were accepted.");
    }

    private static void AssertPose(AlsLocalPose[] actual, AlsLocalPose[] expected)
    {
        for (var bone = 0; bone < actual.Length; bone++)
        {
            var a = actual[bone]; var b = expected[bone];
            var sign = NQuaternion.Dot(a.Rotation, b.Rotation) < 0 ? -1 : 1;
            var q = a.Rotation - b.Rotation * sign;
            if (NVector3.Distance(a.Position, b.Position) > 2e-7f || a.Scale != b.Scale ||
                Math.Max(Math.Max(Math.Abs(q.X), Math.Abs(q.Y)), Math.Max(Math.Abs(q.Z), Math.Abs(q.W))) > 2e-6f)
                throw new InvalidOperationException($"Buffer/adapter mismatch at bone {bone}.");
        }
    }
}
