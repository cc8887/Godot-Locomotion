using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Real resources and root/Ragdoll owners with controlled movement/snapshot input.
// This does not pretend to simulate the character's physical ragdoll bodies.
public partial class RagdollFrameSmoke : Node
{
    private sealed class InjectedPhysicsFailure : Exception { }
    private sealed class FailingContacts : IAlsIslandContacts
    {
        public void Gather(ReadOnlySpan<AlsPrecisePose> p,ReadOnlySpan<AlsProjectionVelocity> v,ReadOnlySpan<AlsIslandBody> b,double dt)
            => throw new InjectedPhysicsFailure();
        public void SolvePosition(Span<AlsProjectionDelta> b,int i,int count) { }
        public void SolveVelocity(Span<AlsProjectionVelocity> b,int i,int count,double dt) { }
    }
    private sealed record Result(string Digest, int Frames, int Flail, int Snapshot, int Blends, long Epoch);
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
    }
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
    private static void Run()
    {
        var set = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath).LoadDefinition();
        var locomotion = AlsLocomotionProfileCompiler.Compile(Read("p4_cycle_locomotion_profile.json"), set);
        var definition = AlsMovementGraphDefinition.Load(set, locomotion);
        if(OS.GetCmdlineUserArgs().Contains("--native-motor-reference"))
        { NativeFlailMotorReference.Run(definition,set); return; }
        var sharedMode = OS.GetCmdlineUserArgs().Contains("--shared-source");
        var motorPhysics = OS.GetCmdlineUserArgs().Contains("--motor-physics");
        if (sharedMode)
        {
            var overlay = definition.WithSharedRootSources(set).WithSharedOverlaySources(set);
            Require(overlay.Sources.Players.Length == 223 && overlay.Sources.RuntimeStamp == overlay.Binding.CreateCoreView().Sources.Stamp,
                "Selecting the Overlay view after Root changed its source/binding pair.");
            definition = overlay.WithSharedRootSources(set);
        }
        foreach (var hz in new[] { 30, 60, 120 })
        {
            var single = Enumerable.Range(0, 4).Select(i => Replay(hz, (uint)i, false)).ToArray();
            var parallel = new Result[4];
            Parallel.For(0, 4, i => parallel[i] = Replay(hz, (uint)i, true));
            for (var i = 0; i < 4; i++) Require(single[i] == parallel[i], "Single/parallel retry result differs.");
            GD.Print($"RAGDOLL_FRAME_ROOT_OK shared={sharedMode} hz={hz} owners=4 frames_per_owner={single[0].Frames} flail={single[0].Flail} snapshot={single[0].Snapshot} blends={single[0].Blends} epochs={single[0].Epoch} all_pose_curve_clock_bits_equal=true retry_every_frame=true digest={single[0].Digest}");
        }
        Result Replay(int hz, uint character, bool retry)
        {
            var skeleton = definition.RagdollRawSources.GetSkeleton(definition.RagdollPose.SkeletonId);
            var normalSource = definition.RawSources.Sources.ToArray().First(s => s.Policy.AdditiveType == AlsRawAnimationAdditiveType.None && s.PoseData.Identity.AnimationId != definition.RagdollPose.AnimationId);
            var names = normalSource.Policy.FloatCurveNames.ToArray().Concat(["Enable_FootIK_L", "Enable_FootIK_R", "Weight_Gait"])
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
            var snapshotRuntime = new AlsNamedPoseSnapshotRuntime(definition.RagdollPose.SnapshotName, "ALS_Mesh", character, 1,
                skeleton.RawBoneNames, skeleton.LogicalToPhysical, skeleton.ReferencePose);
            var binding = definition.RootSharedSources;
            var runtime = new AlsRagdollFrameRuntime(definition.RagdollFrame, snapshotRuntime, character, 1, skeleton.LogicalBoneCount, names.Length,
                sharedMode ? new(binding.Sources.RuntimeStamp, binding.RagdollPlayerId, binding.RagdollSampleId) : null);
            var collector = sharedMode ? new AlsRagdollSharedSourceCollector(binding, runtime) : null;
            var sourceUpdates = new AlsLocomotionSourceUpdate[AlsCycleSyncFrame.PlayerCapacity];
            var sampleUpdates = new AlsLocomotionSampleUpdate[AlsCycleSyncFrame.SampleCapacity]; var activities = new bool[sourceUpdates.Length];
            var sharedCommitted = default(AlsCycleSyncFrame); var sharedCandidate = default(AlsCycleSyncFrame);
            var jumpPlayer = binding.Sources.RuntimePlayers.Single(p => p.Domain == AlsLocomotionSourceDomain.Jump &&
                binding.Sources.RuntimeSamples[p.SampleStart].AnimationId == definition.RagdollPose.AnimationId);
            var independentClocks = 0;
            using var source = new AlsRagdollAnimationSource(definition, set, names);
            var root = new AlsRootPoseRuntime(definition.RootPose, skeleton.LogicalBoneCount, names.Length);
            using var normal = new AlsMovementAnimationSource(normalSource, definition.RawSources, set, names, false);
            var normalPose = new AlsLocalPose[skeleton.LogicalBoneCount]; var normalCurves = new AlsInertialCurve[names.Length];
            var previousPose = skeleton.ReferencePose.ToArray();
            var flailHandoff = new AlsLocalPose[skeleton.LogicalBoneCount];
            var preciseHandoff = new AlsPrecisePose[skeleton.LogicalBoneCount];
            var physics = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs.json"),
                AlsPhysicsAssetCompiler.MeshRoot + "Mannequin.Mannequin");
            var settings = AlsPhysicsJointCompiler.Compile(Read("v4_physics_joint_reference.json"), physics);
            var nativePose = new AlsNativeMotorPose(physics, skeleton.LogicalBoneNames, skeleton.LogicalParents);
            var nativeLocals = new AlsPrecisePose[physics.Bones.Length];
            var motors = new AlsRagdollMotorInputs(physics, settings, 1.5f, 1.5f);
            var drives = new AlsIslandAngularDrive[motors.OutputCount];
            var targets = settings.Select(s=>s.AngularDrive.Target).ToArray();
            AlsAnimatedJointInputs? animated = null;
            if (motorPhysics)
            {
                var adjusted=AlsPhysicsJointFrameCompiler.Compile(Read("v4_physics_joint_frame_inputs.json"),physics);
                var inertia=AlsBodyInertiaCompiler.Compile(Read("v4_physics_inertia_reference.json"),adjusted,settings);
                using var reference=JsonDocument.Parse(Read("v4_physics_awake_solver_reference.json"));
                var solver=reference.RootElement.GetProperty("cases")[0].GetProperty("solverSettings");
                var bodies=adjusted.Bodies.Select(b=>new AlsIslandBody(b.MassLocal,b.PhysicsType==1?default:
                    new((float)(1/b.MassKg),new AlsDoubleVector(inertia[b.Index].ConditionedInverseInertia)),
                    b.Defaults.GetProperty("linearDamping").GetDouble(),b.Defaults.GetProperty("angularDamping").GetDouble())).ToArray();
                var joints=adjusted.Joints.Select(j=>AlsCachedJointSettingsCompiler.IslandJoint(j.ParentBody,j.ChildBody,
                    j.ParentFrame,j.ChildFrame,settings[j.Index].NativeSettings,solver)).ToArray();
                var states=adjusted.Bodies.Select(b=>new AlsIslandBodyState(b.ReferenceComponent,
                    b.PhysicsType==1?default:new(new(100,0,400),default))).ToArray();
                animated=new(physics,settings,skeleton.LogicalBoneNames,skeleton.LogicalParents,
                    new AlsJointIsland(bodies,joints,states),1.5f,1.5f);
            }
            var physicalSteps=0; var physicalBits=new double[13];
            var failingContacts=new FailingContacts();
            var traversal = default(AlsAnimationGraphFrame); var previousState = AlsMovementStateInput.Grounded;
            AlsNamedPoseSnapshot? snapshot = null;
            var flail = 0; var snapshots = 0; var blends = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<int> curveBits = stackalloc int[2]; Span<long> clockBits = stackalloc long[4];
            for (var frame = 1; frame <= hz * 4; frame++)
            {
                var time = (float)(frame - 1) / hz;
                var movement = time >= .2f && time < 1.2f || time >= 1.35f && time < 1.65f || time >= 2.3f && time < 3.3f
                    ? AlsMovementStateInput.Ragdoll : AlsMovementStateInput.Grounded;
                var identity = new AlsFrameIdentity(frame, character, 1); traversal = traversal.Next(identity, (ulong)frame);
                if (previousState == AlsMovementStateInput.Ragdoll && movement != previousState)
                {
                    // Capture before changing movement/physics ownership. A real
                    // scene adapter will supply this immutable payload from bodies.
                    var physical = new AlsLocalPose[skeleton.PhysicalBoneCount];
                    for (var i = 0; i < physical.Length; i++) physical[i] = previousPose[skeleton.PhysicalToLogical[i]];
                    snapshot = new(identity, definition.RagdollPose.SnapshotName, "ALS_Mesh", skeleton.RawBoneNames, physical);
                }
                var context = new AlsPoseUpdateContext(identity, 1, 1f / hz);
                var velocity = animated?.PelvisVelocity ?? new AlsDoubleVector(100 + character * 20, 0, 400 + (frame % 7) * 30);
                normal.Sample(skeleton.ReferencePose, time % normal.Length, normalPose);
                for (var i = 0; i < names.Length; i++) normalCurves[i] = normal.Curve(time % normal.Length, names[i]);
                PrepareAndEvaluate();
                var candidate = runtime.Candidate;
                var expectedFlail = candidate.PlayerTicked && root.Visits(1) && candidate.Machine.CurrentState == 0
                    ? runtime.Pose.ToArray() : null;
                var expectedPrecise = expectedFlail is not null ? runtime.PreciseFlailPose.ToArray() : null;
                if (retry)
                {
                    var committed = runtime.Committed; var committedRoot = root.CommittedState;
                    var expectedPose = root.Pose.ToArray(); var expectedCurves = root.Curves.ToArray();
                    runtime.Cancel(); root.Cancel(); collector?.Discard();
                    Require(runtime.Committed.Diagnostics == committed.Diagnostics && root.CommittedState == committedRoot, "Cancel changed committed root/clock.");
                    PrepareAndEvaluate();
                    Require(runtime.Candidate.Diagnostics == candidate.Diagnostics && root.Pose.SequenceEqual(expectedPose) && root.Curves.SequenceEqual(expectedCurves), "Retry changed pose or playback history.");
                }
                if (candidate.PlayerTicked) flail++;
                if (sharedMode && candidate.PlayerTicked && sharedCandidate.Times[jumpPlayer.PlayerId] != candidate.Time) independentClocks++;
                if (root.Visits(1) && candidate.Machine.CurrentState == 1) snapshots++;
                if (root.Visits(0) && root.Visits(1)) blends++;
                Require(root.Visits(1) || !candidate.PlayerTicked, "Hidden root child advanced its player.");
                if (root.Visits(1)) Require(candidate.Machine.CurrentState == (movement == AlsMovementStateInput.Ragdoll ? 0 : 1), "Internal state ignored zero-duration transition.");
                hash.AppendData(MemoryMarshal.AsBytes(root.Pose));
                foreach (var curve in root.Curves)
                {
                    curveBits[0] = curve.Present ? 1 : 0; curveBits[1] = BitConverter.SingleToInt32Bits(curve.Value);
                    hash.AppendData(MemoryMarshal.AsBytes(curveBits));
                }
                clockBits[0] = candidate.PlayerEpoch; clockBits[1] = candidate.Machine.CurrentState;
                clockBits[2] = BitConverter.SingleToInt32Bits(candidate.Time); clockBits[3] = BitConverter.DoubleToInt64Bits(candidate.FlailRate);
                hash.AppendData(MemoryMarshal.AsBytes(clockBits)); root.Pose.CopyTo(previousPose);
                root.ValidateCommit(identity); runtime.ValidateCommit(identity); root.Commit(identity); runtime.Commit(identity);
                Require(runtime.TryCopyCommittedFlail(identity, flailHandoff) == (expectedFlail is not null),
                    "Flail handoff availability does not match the committed source state.");
                Require(runtime.TryCopyCommittedPreciseFlail(identity,preciseHandoff)==(expectedPrecise is not null),
                    "Precise Flail handoff availability differs.");
                if(expectedPrecise is not null) Require(preciseHandoff.AsSpan().SequenceEqual(expectedPrecise),"Precise Flail publication changed source values.");
                if (expectedFlail is not null) Require(flailHandoff.AsSpan().SequenceEqual(expectedFlail),
                    "Flail handoff contains root blend or snapshot output.");
                if (expectedFlail is not null)
                {
                    nativePose.Convert(flailHandoff,nativeLocals);
                    motors.Evaluate(nativeLocals,targets,velocity,drives);
                    foreach (var drive in drives)
                    {
                        Require(double.IsFinite(drive.Target.LengthSquared) && Math.Abs(drive.Target.LengthSquared-1)<.001,
                            "Invalid actual Flail motor target.");
                        targets[drive.Joint]=drive.Target;
                    }
                    if (animated is not null)
                    {
                        var stepDrives=animated.Prepare(preciseHandoff);
                        if(retry)
                        {
                            var before=Enumerable.Range(0,animated.Island.BodyCount).Select(animated.Island.BodyAt).ToArray();
                            var beforeJoints=Enumerable.Range(0,animated.Island.JointCount).Select(animated.Island.JointDefinitionAt).ToArray();
                            var expectedDrives=stepDrives.ToArray(); var failed=false;
                            try { animated.Island.Step(AlsPhysicsStepTime.FromEngineSeconds(1.0/hz),default,
                                contacts:failingContacts,angularDrives:stepDrives); }
                            catch(InjectedPhysicsFailure) { failed=true; }
                            Require(failed,"Expected injected physical failure.");
                            for(var i=0;i<before.Length;i++) Require(animated.Island.BodyAt(i)==before[i],"Failed physics published a body.");
                            for(var i=0;i<beforeJoints.Length;i++) Require(animated.Island.JointDefinitionAt(i)==beforeJoints[i],"Failed physics published a drive.");
                            stepDrives=animated.Prepare(preciseHandoff);
                            Require(stepDrives.SequenceEqual(expectedDrives),"Retry read uncommitted targets or pelvis velocity.");
                        }
                        animated.Island.Step(AlsPhysicsStepTime.FromEngineSeconds(1.0/hz),default,angularDrives:stepDrives);
                        physicalSteps++;
                        foreach(var drive in stepDrives)
                            Require(animated.Island.JointDefinitionAt(drive.Joint).Angular.DriveTarget==drive.Target,
                                "Successful physics step did not publish animated target.");
                        for(var i=0;i<animated.Island.BodyCount;i++)
                        {
                            var b=animated.Island.BodyAt(i); var p=b.Actor.Position; var q=b.Actor.Rotation;
                            physicalBits[0]=p.X; physicalBits[1]=p.Y; physicalBits[2]=p.Z;
                            physicalBits[3]=q.X; physicalBits[4]=q.Y; physicalBits[5]=q.Z; physicalBits[6]=q.W;
                            physicalBits[7]=b.Velocity.Linear.X; physicalBits[8]=b.Velocity.Linear.Y; physicalBits[9]=b.Velocity.Linear.Z;
                            physicalBits[10]=b.Velocity.Angular.X; physicalBits[11]=b.Velocity.Angular.Y; physicalBits[12]=b.Velocity.Angular.Z;
                            Require(physicalBits.All(double.IsFinite),"Nonfinite animated physics output.");
                            hash.AppendData(MemoryMarshal.AsBytes(physicalBits.AsSpan()));
                        }
                    }
                }
                sharedCommitted = sharedCandidate; collector?.Discard();
                previousState = movement;
                void PrepareAndEvaluate()
                {
                    root.Prepare(movement, context, traversal);
                    runtime.Prepare(movement, velocity, root.Visits(1), root.Visits(1) ? root.ChildContext(1) : context.WithWeight(0), traversal);
                    if (collector is not null)
                    {
                        sharedCandidate = sharedCommitted;
                        // Concurrent original Jump Flail identity exercises the
                        // same asset with its authored independent group/rate.
                        if (sharedCandidate.Epochs[jumpPlayer.PlayerId] == 0) sharedCandidate.Epochs[jumpPlayer.PlayerId] = 1;
                        sourceUpdates[0] = new(jumpPlayer.PlayerId, sharedCandidate.Epochs[jumpPlayer.PlayerId], sharedCandidate.Times[jumpPlayer.PlayerId], .25f, 0, 1);
                        sampleUpdates[0] = new(jumpPlayer.SampleStart, 1, 1); activities[0] = true;
                        var playerCount = 1; var sampleCount = 1;
                        collector.Collect(identity, ref sharedCandidate, sourceUpdates, sampleUpdates, activities, ref playerCount, ref sampleCount);
                        Require(!runtime.Candidate.PlayerTicked, "Shared-mode Ragdoll advanced a private clock before batch completion.");
                        sharedCandidate = AlsSharedSourceBatch.Evaluate(definition.RootSharedBinding.CreateCoreView(), sharedCandidate,
                            sourceUpdates.AsSpan(0, playerCount), sampleUpdates.AsSpan(0, sampleCount), activities.AsSpan(0, playerCount),
                            1, default, 1, context.Delta, 1, runtime.SourceRequest.Rate);
                        collector.Complete(identity, sharedCandidate);
                    }
                    if (root.Visits(1)) runtime.Evaluate(source, snapshot);
                    root.Evaluate(root.Visits(0) ? normalPose : [], root.Visits(0) ? normalCurves : [],
                        root.Visits(1) ? runtime.Pose : [], root.Visits(1) ? runtime.Curves : []);
                }
            }
            Require(flail > hz && snapshots > 0 && blends > 0 && runtime.Committed.PlayerEpoch >= 3, "Incomplete Ragdoll lifecycle coverage.");
            Require(!sharedMode || independentClocks > hz, "Jump and Ragdoll aliased their Flail clock.");
            Require(!motorPhysics || physicalSteps==flail,"Missing animated physical steps.");
            if(motorPhysics && !retry && character==0) GD.Print($"FLAIL_PHYSICS_STEPS_OK hz={hz} steps={physicalSteps} gravity=false contacts=false");
            return new(Convert.ToHexString(hash.GetHashAndReset()), hz * 4, flail, snapshots, blends, runtime.Committed.PlayerEpoch);
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
