using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainRigPoseHostSmoke : Node
{
    private static void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); }
    private sealed class NoGround : IAlsFootGroundQuery
    { public AlsFootGroundHit Sweep(int leg, in AlsFootTraceQuery query) => default; }
    private sealed class RigPlane(double height) : ILyraFootPlantRigCollision
    {
        public int Calls;
        public LyraRigSweepHit Sweep(in LyraRigSweepRequest q)
        {
            Require(q.TraceChannel == 2 && q.Radius > 0, "Changed original Rig trace binding."); Calls++;
            double distance = q.End.Z - q.Start.Z;
            if (Math.Abs(distance) < 1e-8) return default;
            double t = (height + q.Radius - q.Start.Z) / distance;
            return t is >= 0 and <= 1 ? new(true, q.Start + (q.End - q.Start) * t - new AlsDoubleVector(0, 0, q.Radius), new(0, 0, 1)) : default;
        }
    }
    private sealed class FailedRig : ILyraFootPlantRigCollision
    { public LyraRigSweepHit Sweep(in LyraRigSweepRequest q) => throw new InvalidOperationException("Injected late Rig collision failure."); }
    private static string State(LyraMainPoseHost h) => JsonSerializer.Serialize(new { Main = LyraMainLocomotionHostSmoke.Snapshot(h.Main), h.InertiaState,
        h.RigState, h.Main.Layers.SkeletalHistory, h.Main.Layers.AimingNodes, h.Main.Layers.AdditivesState });
    private static string Pose(LyraMainPoseView v) => JsonSerializer.Serialize(new { Pose = v.Pose.ToArray(), Curves = v.Curves.ToArray(), Attributes = v.Attributes.ToArray(), v.RootMotion });
    public override void _Ready()
    { try { Run(); GetTree().Quit(); } catch (Exception e) { GD.PushError("Main Rig pose host failed: " + e); GetTree().Quit(1); } }
    private static void Run()
    {
        using var resources = new LyraLocomotionResources(includeMontageActions: true); var bank = resources.Catalog.Bank; var catalog = new LyraMontageCatalog();
        using var observations = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_als_locomotion_v1_requests.json"));
        using var requests = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/main_slots_v1_requests.json"));
        int frames = 0, poses = 0, retries = 0, changed = 0, partial = 0, disabled = 0, hidden = 0, covered = 0, faults = 0, rejected = 0, repeated = 0, sweeps = 0;
        void Reject(Action action)
        { try { action(); } catch (InvalidOperationException) { rejected++; return; } throw new InvalidOperationException("Invalid Main Rig operation accepted."); }
        foreach (var trace in requests.RootElement.GetProperty("traces").EnumerateArray()
            .GroupBy(t => (t.GetProperty("profile").GetString(), t.GetProperty("hz").GetInt32())).Select(g => g.First()))
        {
            var profile = trace.GetProperty("profile").GetString()!; int hz = trace.GetProperty("hz").GetInt32();
            var runtime = catalog.CreateRuntime(); var baselineRuntime = catalog.CreateRuntime();
            using var host = new LyraMainPoseHost(resources, profile, 700, 1700, 7, runtime, catalog, enableFinalFootPlant: true);
            using var baseline = new LyraMainPoseHost(resources, profile, 700, 1700, 7, baselineRuntime, catalog);
            host.EnterRootPhases(0);baseline.EnterRootPhases(0);
            var referenceRig = new LyraFootPlantRigPoseHost(bank, 7);
            var authored = observations.RootElement.GetProperty("traces").EnumerateArray().First(t => t.GetProperty("profile").GetString() == profile && t.GetProperty("hz").GetInt32() == hz).GetProperty("frames");
            int fi = 0;
            foreach (var q in trace.GetProperty("frames").EnumerateArray().Take(hz * 12))
            {
                var f = authored[fi % authored.GetArrayLength()]; var input = LyraMainUpdateSmoke.ReadInput(f.GetProperty("observation"));
                var delta = q.GetProperty("delta").GetSingle(); var id = new AlsFrameIdentity(fi, 0, 1);
                bool visited = q.GetProperty("visited").GetBoolean() && fi % 89 != 41;
                var visit = new LyraLocomotionMachineVisit(visited, q.GetProperty("weight").GetSingle(), q.GetProperty("initialize").GetBoolean(), q.GetProperty("active").GetBoolean());
                AlsDoubleVector V(JsonElement v) => new(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble());
                var cp = f.GetProperty("componentInput"); var rotation = cp.GetProperty("rotation"); var relative = f.GetProperty("relativeRotation"); var movement = f.GetProperty("movement");
                var component = new AlsPrecisePose(V(cp.GetProperty("position")), new(rotation[0].GetDouble(), rotation[1].GetDouble(), rotation[2].GetDouble(), rotation[3].GetDouble()), AlsDoubleVector.One);
                var r = new AlsQuaternion(relative[0].GetDouble(), relative[1].GetDouble(), relative[2].GetDouble(), relative[3].GetDouble());
                var m = new AlsStopMovementSnapshot(V(movement.GetProperty("lastUpdateVelocity")), movement.GetProperty("separate").GetBoolean(), movement.GetProperty("brakingFriction").GetSingle(), movement.GetProperty("groundFriction").GetSingle(), movement.GetProperty("factor").GetSingle(), movement.GetProperty("deceleration").GetSingle());
                var character = new AlsFootCharacterInput(component, input.Observation.Ground, input.Observation.Ground, new(0, 0, 0), new(0, 0, 1), input.Observation.Velocity);
                var settings = new LyraMainSkeletalSettings(false, fi / Math.Max(1, hz / 2) % 4 == 2);
                var ground = new NoGround(); var plane = new RigPlane(Math.Sin(fi * delta) * 6);
                string? first = null; var before = State(host);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    runtime.Begin(id, delta); baselineRuntime.Begin(id, delta);
                    var c = host.Prepare(input, delta, visit, character, r, m, f.GetProperty("groundDistance").GetDouble(), 99, settings, montageFrame: runtime.Frame);
                    var b = baseline.Prepare(input, delta, visit, character, r, m, f.GetProperty("groundDistance").GetDouble(), 99, settings, montageFrame: baselineRuntime.Frame);
                    var rc = referenceRig.Prepare(7, fi + 1, delta, visited, visit.Initialize, b.Main.Macro.State.Crouching, b.Main.Macro.State.HasVelocity,
                        LyraFootPlantRigUpdateHost.ResolveEnabled(baseline.Main.Layers.CommittedCurve("DisableLegIK"), settings.UseFootPlacement));
                    Require(c.Rig is not null && c.Rig.Update.Updated == rc.Update.Updated, "Main73 uses a different alpha/input binding.");
                    string signature = JsonSerializer.Serialize(c.Rig!.Update.Updated);
                    if (visited)
                    {
                        var basePose = baseline.Evaluate(b, ground); var final = host.Evaluate(c, ground, plane);
                        var expected = referenceRig.Evaluate(rc, new(bank, basePose.Pose, basePose.Curves, basePose.Attributes, basePose.RootMotion), plane);
                        Require(final.Pose.SequenceEqual(expected.Pose) && final.Curves.SequenceEqual(expected.Curves) && final.Attributes.SequenceEqual(expected.Attributes) && final.RootMotion == expected.RootMotion, "Main73 is not after the complete Slot/inertia/provider output.");
                        var saved = Pose(final); signature += saved;
                        if (attempt == 0 && fi % 101 == 0)
                        {
                            Reject(() => host.Evaluate(c, ground)); Reject(() => host.Commit(c)); host.Cancel();
                            Require(State(host) == before, "Missing-provider failure committed Rig or Main history.");
                            c = host.Prepare(input, delta, visit, character, r, m, f.GetProperty("groundDistance").GetDouble(), 99, settings, montageFrame: runtime.Frame);
                            final = host.Evaluate(c, ground, plane); Require(Pose(final) == saved, "Missing-provider cancellation retry changed.");
                        }
                        if (attempt == 0 && fi % 97 == 0 && c.Rig!.Update.Updated.Alpha > 1e-5f)
                        {
                            Reject(() => host.Evaluate(c, ground, new FailedRig())); Reject(() => host.Commit(c)); host.Cancel();
                            Require(State(host) == before, "Late Rig failure committed preceding Main/inertia history."); faults++;
                            c = host.Prepare(input, delta, visit, character, r, m, f.GetProperty("groundDistance").GetDouble(), 99, settings, montageFrame: runtime.Frame);
                            final = host.Evaluate(c, ground, plane); Require(Pose(final) == saved, "Late Rig retry changed output.");
                        }
                        if (attempt == 1 && fi % 103 == 0)
                        {
                            // Existing Main inertia captures another evaluation
                            // with zero pending delta. Compare the same repeated
                            // enclosing path; Rig starts from its prepared owner.
                            var old = final; basePose = baseline.Evaluate(b, ground);
                            final = host.Evaluate(c, ground, plane); Reject(() => { _ = old.Pose.Length; });
                            expected = referenceRig.Evaluate(rc, new(bank, basePose.Pose, basePose.Curves, basePose.Attributes, basePose.RootMotion), plane);
                            Require(final.Pose.SequenceEqual(expected.Pose) && final.Curves.SequenceEqual(expected.Curves) && final.Attributes.SequenceEqual(expected.Attributes) && final.RootMotion == expected.RootMotion, "Repeated enclosing path differs at Main73.");
                            Require(host.InertiaPendingDelta == 0 && baseline.InertiaPendingDelta == 0, "Repeated Main advanced inertia time."); repeated++;
                        }
                        if (attempt == 1)
                        {
                            poses++; changed += !final.Pose.SequenceEqual(basePose.Pose) ? 1 : 0;
                            partial += c.Rig!.Update.Updated.Alpha is > 1e-5f and < .99999f ? 1 : 0;
                            disabled += c.Rig.Update.Updated.Alpha <= 1e-5f ? 1 : 0;
                            covered += !c.Main.Machine.Visited ? 1 : 0;
                        }
                        var feedback = final.Curves.ToArray(); feedback[bank.Curves.Index("DisableLegIK")] = new(fi / Math.Max(1, hz / 3) % 7 == 3 ? 1 : 0, true);
                        host.StageFinalFeedback(c, diagnosticCurves: feedback); baseline.StageFinalFeedback(b, diagnosticCurves: feedback);
                    }
                    else { Reject(() => host.Evaluate(c, ground, plane)); if (attempt == 1) hidden++; }
                    foreach (var command in q.GetProperty("commands").EnumerateArray())
                    {
                        foreach (var bankRuntime in new[] { runtime, baselineRuntime })
                        {
                            int asset = command.GetProperty("asset").GetInt32();
                            if (command.GetProperty("stop").GetBoolean())
                            {
                                bool explicitStop = command.TryGetProperty("instanceStop", out var stop) && stop.GetBoolean();
                                var active = bankRuntime.Candidate.ToArray().LastOrDefault(v => v.MontageId == asset && (explicitStop || v.OwnsActiveActionLookup));
                                if (active.InstanceId > 0) Require(bankRuntime.StopInstance(active.InstanceId, command.GetProperty("blend").GetSingle(), catalog.Definitions[asset].Lifecycle.BlendOutOption), "Stop rejected.");
                            }
                            else Require(bankRuntime.PlayAction(asset, command.GetProperty("rate").GetSingle(), command.GetProperty("start").GetSingle(), stopGroup: command.GetProperty("stopGroup").GetBoolean()), "Play rejected.");
                        }
                    }
                    host.ValidateCommit(c, !visited); baseline.ValidateCommit(b, !visited); referenceRig.ValidateCommit(rc, !visited);
                    runtime.ValidateCommit(id); baselineRuntime.ValidateCommit(id);
                    if (attempt == 0)
                    { first = signature; host.Cancel(); baseline.Cancel(); referenceRig.Cancel(); runtime.Discard(); baselineRuntime.Discard(); Require(State(host) == before, "Cancelled Main/Rig history changed."); retries++; }
                    else
                    {
                        Require(first == signature, "Unified Main/Rig retry changed output.");
                        host.Commit(c, !visited); baseline.Commit(b, !visited); referenceRig.Commit(rc, !visited); runtime.Commit(id); baselineRuntime.Commit(id);
                        Require(host.RigState == referenceRig.State && LyraMainLocomotionHostSmoke.Snapshot(host.Main) == LyraMainLocomotionHostSmoke.Snapshot(baseline.Main), "Rig changed enclosing source/Sync histories.");
                    }
                }
                sweeps += plane.Calls; frames++; fi++;
            }
        }
        Require(frames == 7560 && retries == frames && poses > 0 && hidden > 0 && covered > 0 && changed > 0 && partial > 0 && disabled > 0 && faults > 0 && repeated > 0 && sweeps > 0, "Incomplete Main73 host coverage.");
        GD.Print($"LYRA_MAIN_RIG_HOST_GODOT_OK frames={frames} poses={poses} retry={retries} changed={changed} partial={partial} disabled={disabled} hidden={hidden} coveredLocomotion={covered} faults={faults} rejected={rejected} repeated={repeated} sweeps={sweeps} profiles=3 hz=30,60,120 commonSync=true fullChannels=true collision=analytic nativeWholeMain=false production=false");
    }
}
