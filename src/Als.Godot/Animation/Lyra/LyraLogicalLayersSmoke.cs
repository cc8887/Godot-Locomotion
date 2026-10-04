using Godot;
using GodotAls.Core.Locomotion;
using System.Text.Json;

namespace GodotAls.Animation.Lyra;

public partial class LyraLogicalLayersSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Logical layers failed: " + error); GetTree().Quit(1); }
    }
    private void Run()
    {
        using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true, includePistol: true, includeRifle: true);
        AddChild(rig.Root);
        var bank = LyraLogicalSourceBank.Load();
        GD.Print($"LYRA_LOGICAL_RIG_BASIS skeleton={rig.Skeleton.Transform} rootRest={rig.Skeleton.GetBoneRest(0)} " +
            $"footGlobal={rig.Skeleton.GetBoneGlobalPose(rig.Skeleton.FindBone("foot_l"))} " +
            $"pelvisRest={rig.Skeleton.GetBoneRest(1)} global={rig.Skeleton.GlobalTransform} parent={((Node3D)rig.Skeleton.GetParent()).Transform}");
        var pipeline = new LyraLayerPosePipeline(rig.Skeleton, bank);
        var rootYaw = new LyraRootYawOffset(rig.Skeleton, LyraRootYawDefaults.Load());
        if (!OS.GetCmdlineUserArgs().Contains("--write-logical-hand-requests")) rootYaw.ConfigureLogical(bank);
        ILyraItemAnimationLayers[] providers = [new LyraUnarmedAnimationLayers(rig.Catalog, rig.Auxiliary, rig.Remaining),
            new LyraPistolAnimationLayers(rig.Pistol!), new LyraRifleAnimationLayers(rig.Rifle!)];
        var input = new AlsPrecisePose[81]; var output = new AlsPrecisePose[81];
        var visible = new AlsLocalPose[68]; var snapshot = new AlsLocalPose[68];
        var names = Enumerable.Range(0, 68).Select(i => rig.Skeleton.GetBoneName(i).ToString()).ToArray();
        var frames = 0; var mixed = 0; var changedWeapon = 0; var changedTarget = 0; var rejected = 0;
        var requests = new List<object>();
        foreach (var provider in providers)
        foreach (var hz in new[] { 30, 60, 120 })
        {
            var item = new LyraItemLayerInstance(rig, provider, LyraLinkedLayerContracts.Load(), bank);
            var cycle = provider.ResolveStateClip(LyraLayerHook.FullBody_CycleState, new(LyraCardinalDirection.Forward, LyraGait.Jog));
            var idle = provider.ResolveStateClip(LyraLayerHook.FullBody_IdleState, new(LyraCardinalDirection.Forward, LyraGait.Jog));
            var source = new LyraLogicalSourcePlayback(bank, slot => rig.SourceClip(slot).Loop, slot => rig.SourceClip(slot).PlayLength);
            source.Select(cycle, 0);
            for (var frame = 0; frame < hz * 2; frame++)
            {
                var slot = frame < hz ? cycle : idle;
                source.Select(slot, .2);
                var time = frame % hz / (double)hz % rig.SourceClip(slot).PlayLength;
                bank.CreateSampler(slot).Sample(time, input); input[0] = bank.Reference[0];
                var original = input.ToArray();
                var evaluation = new LyraPoseEvaluation(frame < hz ? LyraMotionPhase.Cycle : LyraMotionPhase.Idle,
                    frame % 3 == 0, frame % 4 < 2, 40 - frame % 80, frame % 90 - 45,
                    frame % 3 * .5f, frame % 3 * .5f, frame % 3 * .5f);
                item.HipFire.UpdateWeight(evaluation.IsCrouching, evaluation.IsOnGround, frame % 2 == 0,
                    frame % 3 == 0 ? 0 : double.MaxValue, 0, 0, true, 1d / hz);
                LyraUnarmedAimOffset.CapturePose(rig.Skeleton, snapshot);
                pipeline.Evaluate(item, rootYaw, evaluation, input, output);
                if (hz == 30 && frame is 0 or 7 or 15 or 29)
                    foreach (var disable in new[] { 0f, .25f, .5f, 1f })
                        requests.Add(new { profile = provider.Name.ToLowerInvariant(), slot, time,
                            retargetDisable = evaluation.HandRetargetDisable, rightDisable = disable, leftDisable = disable,
                            pose = pipeline.LogicalPreControlsPose.ToArray().Select(p => new {
                                position = new[] { p.Position.X, p.Position.Y, p.Position.Z },
                                rotation = new[] { p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W },
                                scale = new[] { p.Scale.X, p.Scale.Y, p.Scale.Z } }).ToArray() });
                LyraUnarmedAimOffset.CapturePose(rig.Skeleton, visible);
                if (!snapshot.SequenceEqual(visible) || !input.SequenceEqual(original))
                    throw new InvalidOperationException("Logical layer touched a visible pose or immutable input.");
                if ((output[68].Position - input[68].Position).LengthSquared > 1e-12 ||
                    Math.Abs(AlsQuaternion.Dot(output[68].Rotation.Normalized(), input[68].Rotation.Normalized())) < 1 - 1e-10) changedWeapon++;
                if ((output[80].Position - input[80].Position).LengthSquared > 1e-12 ||
                    Math.Abs(AlsQuaternion.Dot(output[80].Rotation.Normalized(), input[80].Rotation.Normalized())) < 1 - 1e-10) changedTarget++;
                pipeline.ApplyLogical(item, rootYaw, evaluation, source, time, 1d / hz);
                LyraUnarmedAimOffset.CapturePose(rig.Skeleton, visible);
                for (var bone = 0; bone < 81; bone++) pipeline.LogicalOutputPose[bone].Validate(1e-6);
                for (var bone = 0; bone < 68; bone++)
                {
                    var actual = LyraHandRetargetPoseLayer.ToNative(visible[bone]); var expected = pipeline.LogicalOutputPose[bone];
                    if (Math.Sqrt((actual.Position - expected.Position).LengthSquared) > 2e-5 ||
                        Math.Abs(AlsQuaternion.Dot(actual.Rotation.Normalized(), expected.Rotation.Normalized())) < 1 - 1e-12)
                        throw new InvalidOperationException("Logical final writer omitted a mapped skin bone.");
                }
                if (source.Time != time || source.SampledFrames != frame + 1 || source.ActiveOccurrences < 1)
                    throw new InvalidOperationException("Logical source did not consume the selected host time.");
                pipeline.RestoreBase(); pipeline.RestoreBase();
                LyraUnarmedAimOffset.CapturePose(rig.Skeleton, visible);
                if (!snapshot.SequenceEqual(visible)) throw new InvalidOperationException("Logical final writer failed to restore the source.");
                frames++;
            }
            mixed += source.BlendedFrames;
            foreach (var length in new[] { 68, 79, 80, 82 })
            {
                try { pipeline.Evaluate(item, rootYaw, default, new AlsPrecisePose[length], output); }
                catch (ArgumentException) { rejected++; continue; }
                throw new InvalidOperationException("Truncated logical pose accepted.");
            }
            try { pipeline.Evaluate(item, rootYaw, default, input, input); }
            catch (ArgumentException) { rejected++; continue; }
            throw new InvalidOperationException("Logical alias accepted.");
        }
        if (frames != 1260 || rejected != 45 || mixed == 0 || changedWeapon == 0 || changedTarget == 0 ||
            rig.Skeleton.GetBoneCount() != 68 || !names.SequenceEqual(Enumerable.Range(0, 68).Select(i => rig.Skeleton.GetBoneName(i).ToString())))
            throw new InvalidOperationException("Incomplete logical layer coverage.");
        GD.Print($"LYRA_LOGICAL_LAYERS_OK profiles=3 hz=30,60,120 frames={frames} logical=81 skin=68 " +
            $"blended={mixed} weaponChanged={changedWeapon} targetChanged={changedTarget} rejected={rejected} " +
            "inputIsolated=True restored=True leftIk=enabled");
        if (OS.GetCmdlineUserArgs().Contains("--write-logical-hand-requests"))
        {
            if (requests.Count != 48) throw new InvalidOperationException("Incomplete hand-chain request coverage.");
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/logical-hand-requests.json"),
                JsonSerializer.Serialize(new { schemaVersion = 1, calibrationSha256 = bank.CalibrationSha256, catalogSha256 = bank.CatalogSha256, rows = requests }));
            GD.Print("LYRA_LOGICAL_HAND_REQUESTS_OK cases=48 logical=81");
        }
    }
}
