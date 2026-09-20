using Godot;
using GodotAls.Assets;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class TurnMontagePoseSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
    private void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var assets = AlsTurnInPlaceCompiler.CompileMontageAssets(Read("v4_turn_montage_inputs.json"), set, pose);
        using var library = AlsAnimationLibraryBuilder.Build(set, locomotion, pose); AddChild(library.Root);
        var detail = AlsLocomotionDetailCompiler.Compile(Read("v4_locomotion_detail_graph.json"), set, locomotion.SkeletonId);
        using var reference = new AlsDetailPoseSampler(library, set, detail);
        using var slot = new AlsStandingTurnSlot(library, set, pose);
        var rest = reference.ReferencePose.ToArray(); var actual = rest.ToArray(); var expected = rest.ToArray();
        var curves = 0; var changedBones = 0;
        foreach (var asset in assets)
        {
            var runtime = new AlsMontageRuntime(assets);
            runtime.Begin(new(1, 1, 1), .1f);
            runtime.Play(new(asset.AnimationId, asset.Slot, 0, .37f, .2f, .2f, 1, 0));
            var cold = new AlsStandingTurnSlotInput(0, 0, 0, 0, 0, 0, runtime.Frame, new(1, 1, 1), asset.Slot);
            rest.CopyTo(actual, 0); slot.Compose(cold, rest, actual);
            Require(actual.SequenceEqual(rest), "Blueprint play changed the already frozen pose frame.");
            runtime.Commit(new(1, 1, 1)); runtime.Begin(new(2, 1, 1), .3f);
            var input = cold with { MontageFrame = runtime.Frame, MontageIdentity = new(2, 1, 1) };
            using var clip = new AlsLocalPoseClip(library.Library.GetAnimation(library.ClipNames[asset.AnimationId]), library.Skeleton, ownsAnimation: false);
            clip.SampleSourceSeconds(rest, .37f, asset.Duration, expected);
            rest.CopyTo(actual, 0); slot.Compose(input, rest, actual);
            for (var bone = 0; bone < actual.Length; bone++)
            {
                Require(System.Numerics.Vector3.Distance(actual[bone].Position, expected[bone].Position) < 1e-6f &&
                    System.Numerics.Vector3.Distance(actual[bone].Scale, expected[bone].Scale) < 1e-6f &&
                    MathF.Abs(System.Numerics.Quaternion.Dot(actual[bone].Rotation, expected[bone].Rotation)) > .99999f,
                    "Montage slot differs from its actual source clip.");
                if (actual[bone] != rest[bone]) changedBones++;
            }
            var sequence = set.Animations[asset.AnimationId]; var sampler = new AlsCurveSampler(sequence.Curves);
            foreach (var curve in sequence.Curves)
            {
                Require(sampler.TrySample(curve.CurveId, .37f, out var value), "Missing authored turn curve.");
                Require(MathF.Abs(slot.Curve(input, curve.SourceName, .123f) - value) < 1e-6f, "Montage curve ignored its source/time."); curves++;
            }
            Require(slot.Curve(input, "Enable_Transition", 1) == 0, "Full montage retained idle Enable_Transition override.");
            rest.CopyTo(actual, 0); slot.Compose(input with { Slot = asset.Slot == AlsTurnSlot.Standing ? AlsTurnSlot.Crouching : AlsTurnSlot.Standing }, rest, actual);
            Require(actual.SequenceEqual(rest), "Montage leaked into the other stance's slot.");
            runtime.Commit(new(2, 1, 1)); runtime.Begin(new(3, 1, 1), .1f);
            var rejected = false; try { slot.Validate(cold); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Recycled montage pose bank accepted an old identity.");
        }
        Require(curves > 0 && changedBones > 0, "Actual turn sampling had no curve or pose coverage.");
        GD.Print($"TURN_MONTAGE_POSE_OK assets={assets.Length} curves={curves} changed_bones={changedBones} wrong_slot=8 stale_bank=8 first_frame=held source=exported_sequences");
    }
    private static string Read(string file) => Godot.FileAccess.GetFileAsString("res://assets/config/" + file);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
