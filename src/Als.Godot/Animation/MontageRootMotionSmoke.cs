using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace GodotAls.Animation;

public partial class MontageRootMotionSmoke : Node
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
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        var definition = AlsMovementGraphDefinition.Load(set, locomotion, pose);
        var reader = new AlsMontageRootMotionReader(definition);
        var roll = definition.AuthoredMontageAssets.Single(a => a.ActionDefinitionId == definition.RollDefinitionId);
        using var document = JsonDocument.Parse(Read("v4_montage_root_motion_native.json"));
        var assets = document.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var turns = assets.Take(8).Select(a => definition.TurnMontageAssets.Single(t =>
            set.Animations[t.AnimationId].ObjectPath == a.GetProperty("path").GetString())).ToArray();
        var frames = 0; var moving = 0; var retries = 0; var cases = 0; var maxPosition = 0f; var maxRotation = 0f;
        foreach (var scenario in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var bank = new AlsMontageRuntime(definition.TurnMontageAssets, definition.AuthoredMontageAssets);
            var delta = scenario.GetProperty("delta").GetSingle();
            foreach (var frame in scenario.GetProperty("frames").EnumerateArray())
            {
                var identity = new AlsFrameIdentity(frame.GetProperty("frame").GetInt64(), 1, 1);
                var label = $"{scenario.GetProperty("name").GetString()}:{identity.FrameId}";
                var native = frame.GetProperty("rootMotion");
                Require(bank.CommittedRootMotionInstance == native.GetProperty("ownerBeforeTick").GetInt64(), label + " before owner");
                Prepare(); var range = bank.RootMotionRange; var motion = reader.Read(range);
                bank.Discard(); Prepare(); retries++;
                Require(range == bank.RootMotionRange && motion == reader.Read(bank.RootMotionRange), label + " discarded motion changed");
                Require(range.HasMotion == native.GetProperty("hasMotion").GetBoolean(), label + " motion presence");
                Require(!range.HasMotion || range.Identity == identity, label + " source identity");
                var p = native.GetProperty("translationCm").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                var q = native.GetProperty("rotation").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                // Independent UE -> FBX reflection, then FBX -> Godot basis.
                var expectedPosition = new NVector3(p[1], p[2], -p[0]) * .01f;
                var expectedRotation = NQuaternion.Normalize(new(-q[1], -q[2], q[0], q[3]));
                var positionError = NVector3.Distance(motion.Translation, expectedPosition);
                var rotationError = MathF.Abs(1 - MathF.Abs(NQuaternion.Dot(motion.Rotation, expectedRotation)));
                maxPosition = MathF.Max(maxPosition, positionError); maxRotation = MathF.Max(maxRotation, rotationError);
                Require(positionError < .00001f && rotationError < .000001f,
                    $"{label} motion mismatch p={positionError:R} q={rotationError:R} actual={motion.Translation} native={expectedPosition}");
                if (range.HasMotion) moving++;
                bank.Commit(identity); frames++;
                void Prepare()
                {
                    bank.Begin(identity, delta);
                    Require(bank.CandidateRootMotionInstance == native.GetProperty("ownerAfterTick").GetInt64(), label + " tick owner");
                    foreach (var command in frame.GetProperty("commands").EnumerateArray())
                    {
                        var stop = command.GetProperty("stopInstance").GetInt64();
                        if (stop > 0) bank.StopInstance(stop, command.GetProperty("in").GetSingle(), AlsActionBlendOption.HermiteCubic);
                        else
                        {
                            var asset = command.GetProperty("asset").GetInt32();
                            var rate = command.GetProperty("rate").GetSingle(); var start = command.GetProperty("start").GetSingle();
                            if (asset == 8) bank.PlayAction(roll.ActionDefinitionId, rate, start, command.GetProperty("stopGroup").GetBoolean());
                            else
                            {
                                var turn = turns[asset];
                                bank.Play(new(turn.AnimationId, turn.Slot, rate, start, command.GetProperty("in").GetSingle(),
                                    command.GetProperty("out").GetSingle(), 1, command.GetProperty("trigger").GetSingle()));
                            }
                        }
                    }
                    Require(bank.CandidateRootMotionInstance == native.GetProperty("ownerAfterCommands").GetInt64(), label + " command owner");
                }
            }
            cases++;
        }
        Require(cases == 11 && frames == 2540 && moving == 945, "Incomplete native motion coverage.");
        GD.Print($"MONTAGE_ROOT_MOTION_OK cases={cases} frames={frames} motion_frames={moving} retries={retries} max_position_m={maxPosition:R} max_rotation={maxRotation:R} oracle=UE_RootMotionFromMontagesOnly collision=not_applied");
    }
    private static string Read(string file) => Godot.FileAccess.GetFileAsString("res://assets/config/" + file);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
