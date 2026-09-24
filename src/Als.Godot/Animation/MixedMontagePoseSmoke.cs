using Godot;
using GodotAls.Assets;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

public partial class MixedMontagePoseSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"),set);
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"),set,locomotion);
        var definition = AlsMovementGraphDefinition.Load(set,locomotion,pose);
        var roll = definition.AuthoredMontageAssets.Single(a => a.ActionDefinitionId == definition.RollDefinitionId);
        using var library = AlsAnimationLibraryBuilder.BuildP5a(set,definition.Binding); AddChild(library.Root);
        var detail = AlsLocomotionDetailCompiler.Compile(Read("v4_locomotion_detail_graph.json"),set,locomotion.SkeletonId);
        using var reference = new AlsDetailPoseSampler(library,set,detail);
        using var turnSlot = new AlsStandingTurnSlot(library,set,pose);
        using var actionSlot = new AlsStandingTurnSlot(library,set,null,[roll.AnimationId]);
        var rest = reference.ReferencePose.ToArray(); var actual = rest.ToArray(); var expected = rest.ToArray(); var sampled = rest.ToArray();
        using var rollClip = new AlsLocalPoseClip(library.Library.GetAnimation(library.ClipNames[roll.AnimationId]),library.Skeleton,ownsAnimation:false);
        var curve = set.Animations[roll.AnimationId].Curves.First(); var sampler = new AlsCurveSampler(set.Animations[roll.AnimationId].Curves);
        var montage = new AlsMontageRuntime(definition.TurnMontageAssets,definition.AuthoredMontageAssets);
        var actions = new AlsMontageActionRuntime(montage,definition.ActionPolicies);
        var policy = definition.ActionPolicies.Single(p => p.DefinitionId == definition.RollDefinitionId);
        var accepted = 0; var replaced = 0; var completed = 0;
        var fades = 0; var changed = 0; var curveFrames = 0; var retries = 0;
        for (var frame = 1; frame <= 240; frame++)
        {
            var identity = new AlsFrameIdentity(frame,1,1);
            Prepare(); Evaluate(); var candidate = montage.Candidate.ToArray(); var savedPose = actual.ToArray();
            var savedOwners = actions.CandidateOwners.ToArray(); var savedOutcomes = actions.Outcomes;
            actions.Discard(); Prepare(); Evaluate(); retries++;
            Require(candidate.SequenceEqual(montage.Candidate.ToArray()) && savedPose.SequenceEqual(actual),"Mixed montage retry differs.");
            Require(savedOwners.SequenceEqual(actions.CandidateOwners.ToArray()) && savedOutcomes.Count == actions.Outcomes.Count,
                "Mixed action owner retry differs.");
            for(var i=0;i<actions.Outcomes.Count;i++)
            {
                var outcome = actions.Outcomes[i]; Require(outcome == savedOutcomes[i],"Action outcome retry differs.");
                if(outcome.ResultCode==AlsActionResultCode.Accepted)accepted++;
                if(outcome.ResultCode==AlsActionResultCode.InterruptedByReplacement)replaced++;
                if(outcome.ResultCode==AlsActionResultCode.Completed)completed++;
            }
            actions.Commit(identity);
            void Prepare()
            {
                actions.Begin(identity,.01f);
                actions.ApplyRequest(frame is 1 or 8 or 15 or 22 ?
                    new(frame,AlsActionCommand.Start,roll.ActionDefinitionId,policy.StartSectionId,100,1) : AlsActionRequest.None);
                if (frame == 1)
                {
                    var turn = definition.TurnMontageAssets[0];
                    montage.Play(new(turn.AnimationId,turn.Slot,1.2f,0,.2f,.2f,1,0));
                }
                actions.Complete();
            }
            void Evaluate()
            {
                var turn = new AlsStandingTurnSlotInput(0,0,0,0,0,0,montage.Frame,identity,AlsTurnSlot.Standing);
                rest.CopyTo(actual,0); turnSlot.Compose(turn,rest,actual); actual.CopyTo(expected,0);
                var input = turn with { Slot = AlsMontageSlot.BaseLayer };
                actionSlot.Compose(input,rest,actual);
                var weights = montage.SlotWeights(AlsMontageSlot.BaseLayer); var first = true; var count = 0;
                var expectedCurve = 0f;
                foreach (var entry in montage.Evaluation)
                {
                    if (entry.Slot != AlsMontageSlot.BaseLayer) continue;
                    rollClip.SampleSourceSeconds(rest,entry.Position,set.Animations[roll.AnimationId].PlayLength,sampled);
                    var w = entry.Weight / MathF.Max(weights.TotalNodeWeight,1);
                    for (var bone = 0; bone < actual.Length; bone++)
                        sampled[bone] = AlsPoseBlender.Scale(sampled[bone],w);
                    // Accumulate independently sampled live instances against the turn source.
                    if (first)
                    {
                        for (var bone = 0; bone < expected.Length; bone++) expected[bone] = AlsPoseBlender.Scale(expected[bone],
                            weights.SourceWeight > AlsPoseBlender.WeightThreshold ? weights.SourceWeight : 0);
                        first = false;
                    }
                    for (var bone = 0; bone < expected.Length; bone++) expected[bone] = AlsPoseBlender.Accumulate(expected[bone],sampled[bone],1);
                    sampler.TrySample(curve.CurveId,entry.Position,out var value); expectedCurve += value*w; count++;
                }
                if (!first)
                {
                    for (var bone = 0; bone < expected.Length; bone++) expected[bone] = AlsPoseBlender.Normalize(expected[bone]);
                    expectedCurve += weights.SourceWeight > AlsPoseBlender.WeightThreshold ? .123f*weights.SourceWeight : 0;
                    Require(MathF.Abs(actionSlot.Curve(input,curve.SourceName,.123f)-expectedCurve) < 1e-6f,"Mixed action curves differ."); curveFrames++;
                }
                if (count > 1) fades++;
                for (var bone = 0; bone < actual.Length; bone++)
                {
                    Require(System.Numerics.Vector3.Distance(actual[bone].Position,expected[bone].Position)<1e-5f &&
                        MathF.Abs(System.Numerics.Quaternion.Dot(actual[bone].Rotation,expected[bone].Rotation))>.99999f,"Mixed action pose differs.");
                    if (actual[bone] != rest[bone]) changed++;
                }
            }
        }
        Require(fades>0 && changed>0 && curveFrames>0,"No real mixed montage pose coverage.");
        Require(accepted==4 && replaced==3 && completed==1,"Real Roll action outcomes differ.");
        GD.Print($"MIXED_MONTAGE_POSE_OK frames=240 retries={retries} overlap_samples={fades} changed_bones={changed} curve_samples={curveFrames} accepted={accepted} replaced={replaced} completed={completed} ownership=shared requests=connected source=real_roll_and_turn root_motion=not_applied gameplay=not_connected demo=not_connected");
    }
    private static string Read(string file) => Godot.FileAccess.GetFileAsString("res://assets/config/"+file);
    private static void Require(bool condition,string message) { if (!condition) throw new InvalidOperationException(message); }
}
