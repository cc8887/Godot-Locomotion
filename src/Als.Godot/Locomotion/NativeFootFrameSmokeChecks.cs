using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Events;

namespace GodotAls.Locomotion;

public partial class P3bFrameOrderSmoke
{
    private Dictionary<int, int>? _rootToOverlayHandles;
    private ulong _overlayIdentityResultDigest = AlsResultDigest.OffsetBasis;
    private void AppendResultUsingPreviousOccurrenceLayout(in AlsFrameResult result)
    {
        if (_rootToOverlayHandles is null)
        {
            var previousGraph = _context.MovementGraph!.WithSharedOverlaySources(_context.AnimationSet);
            var previous = previousGraph.Binding.CreateSourceOccurrenceView().Entries.ToArray();
            var current = _context.SourceBindings!.CreateSourceOccurrenceView().Entries;
            _rootToOverlayHandles = new Dictionary<int, int>();
            for (var i = 0; i < current.Length; i++)
            {
                var entry = current[i];
                var index = Array.FindIndex(previous, p => p.SourceKind == entry.SourceKind &&
                    p.SourceBindingIndex == entry.SourceBindingIndex && p.GraphSlotIndex == entry.GraphSlotIndex);
                if (index >= 0) Add(entry.OccurrenceHandleId, previous[index].OccurrenceHandleId);
            }
            // Dynamic montage notify identities follow the static layout. Match
            // their authored keys too; they are not source-entry array indices.
            var previousRanges = previousGraph.MontageNotifies.Ranges.ToArray();
            foreach (var range in _context.MovementGraph.MontageNotifies.Ranges)
            {
                var matches = previousRanges.Where(p => p.ActionDefinitionId == range.ActionDefinitionId &&
                    p.AnimationId == range.AnimationId && p.Slot == range.Slot && p.Direct == range.Direct).ToArray();
                Require(matches.Length == 1, "Dynamic notify identity lacks a unique previous-layout counterpart.");
                Add(range.Handle, matches[0].Handle);
            }
            void Add(int currentHandle, int previousHandle)
            {
                if (_rootToOverlayHandles.TryGetValue(currentHandle, out var existing))
                    Require(existing == previousHandle, "Conflicting physical identity mappings.");
                else _rootToOverlayHandles.Add(currentHandle, previousHandle);
            }
        }
        // Diagnostic copy only: compare all hashed behavior fields with the old
        // replay after rebasing physical handles by their semantic layout keys.
        var normalized = result; normalized.TypedEvents = new AlsEventBuffer();
        for (var i = 0; i < result.TypedEvents.Count; i++)
        {
            var e = result.TypedEvents[i];
            Require(normalized.TypedEvents.TryAdd(e with { OccurrenceHandleId = Map(e.OccurrenceHandleId) }), "Normalized event capacity differs.");
        }
        normalized.Sync = normalized.Sync with { LeaderOccurrenceHandleId = Map(normalized.Sync.LeaderOccurrenceHandleId) };
        normalized.ActionPlayback = normalized.ActionPlayback with { OccurrenceHandleId = Map(normalized.ActionPlayback.OccurrenceHandleId) };
        AlsResultDigest.Append(ref _overlayIdentityResultDigest, normalized);
        int Map(int handle)
        {
            if (handle < 0) return handle;
            if (!_rootToOverlayHandles.TryGetValue(handle, out var previousHandle))
                throw new InvalidOperationException($"Normal movement published an unbound/Ragdoll-only physical handle: {handle}.");
            return previousHandle;
        }
    }
    private sealed class NativeFootCoverageCommandSource : IAlsLocomotionCommandSource
    {
        private readonly AlsReplayInputAdapter _movement = AlsMotorReplay.CreateHarnessSequence();
        public AlsLocomotionCommand GetCommand(long frame)
        {
            if (frame <= 600) return _movement.GetCommand(frame);
            // Actual input-triggered standing/crouching turns, not injected
            // animation curves. The original first 600 movement frames remain.
            var yaw = frame <= 720 ? 1.6f : frame <= 840 ? -1.6f : .6f;
            return AlsLocomotionCommand.CreateDefault() with
            {
                ViewYaw = yaw, AimYaw = yaw,
                RequestedStance = frame > 840 ? AlsStance.Crouching : AlsStance.Standing,
            };
        }
    }
    private void ValidateNativeFootGather(in AlsP3FrameDiagnostics frame, in AlsFrameInput input, AlsCharacterMotor motor)
    {
        var scene = input.FootIk; var published = frame.FootProbeSource.NativeFootPose;
        var root = _active.FullMovementDiagnostics;
        Require(root.RootIdentity == frame.Identity && root.RootState.Initialized && root.RootState.ActiveChild == 0 &&
            root.RootState.FirstWeight == 1 && root.RootState.SecondWeight == 0,
            "Native movement did not commit its final normal root branch.");
        Require(root.Ragdoll.Traversal.Identity == frame.Identity && root.Ragdoll.Initialized && !root.Ragdoll.Updated &&
            root.Ragdoll.PlayerEpoch == 1 && !root.Ragdoll.PlayerTicked && root.Ragdoll.Time == 0 && root.Ragdoll.FlailRate == 0,
            "Hidden Ragdoll child was not initialized/committed or advanced while irrelevant.");
        var ragdollPlayer = _context.MovementGraph!.RootSharedSources.RagdollPlayerId;
        var sourceFrame = _active.StandingCycleSync;
        Require(sourceFrame.Epochs[ragdollPlayer] == root.Ragdoll.PlayerEpoch && sourceFrame.Times[ragdollPlayer] == root.Ragdoll.Time,
            "Hidden Ragdoll history is not part of the shared source transaction.");
        for (var i = 0; i < sourceFrame.PlayerCount; i++) Require(sourceFrame.Players[i].PlayerId != ragdollPlayer,
            "Hidden Ragdoll source ticked in the shared batch.");
        Require(published.Identity == frame.Identity, "Native feet were not part of the committed visual candidate.");
        Require(frame.FootPose.ModifierWriteTransactionCount == 0 && frame.FootPose.ModifierFootChainRebuildCount == 0,
            "Legacy modifier wrote over the native Foot IK output.");
        var component = AlsFootIkGodot.Transform(scene.ComponentToWorld);
        var left = component * new Vector3(scene.LeftComponent.Position.X, scene.LeftComponent.Position.Y, scene.LeftComponent.Position.Z);
        var right = component * new Vector3(scene.RightComponent.Position.X, scene.RightComponent.Position.Y, scene.RightComponent.Position.Z);
        left.Y = right.Y = scene.RootWorld.Y;
        Require(motor.LastLeftFootQueryWorldOrigin.DistanceSquaredTo(left) < 1e-8f &&
            motor.LastRightFootQueryWorldOrigin.DistanceSquaredTo(right) < 1e-8f,
            "Native trace did not project the previous IK feet onto root height.");
        if (_previousNativeFeet.Identity != default && _previousNativeFeet.Identity.CharacterId == input.Identity.CharacterId &&
            _previousNativeFeet.Identity.SlotGeneration == input.Identity.SlotGeneration)
        {
            Require(scene.PoseIdentity == _previousNativeFeet.Identity && motor.LastFootGatherConsumed &&
                scene.LeftComponent == _previousNativeFeet.LeftComponent && scene.RightComponent == _previousNativeFeet.RightComponent,
                "Native Gather did not use the prior committed final IK-foot transforms.");
        }
        else Require(scene.PoseIdentity == default && !motor.LastFootGatherConsumed,
            "Cold native feet consumed a retired-generation pose.");
        if (input.LeftFootHit.Valid == 1) Require(MathF.Abs(input.LeftFootHit.Position.X-left.X)<1e-4f && MathF.Abs(input.LeftFootHit.Position.Z-left.Z)<1e-4f,
            "Native left hit is not on its vertical trace.");
        if (input.RightFootHit.Valid == 1) Require(MathF.Abs(input.RightFootHit.Position.X-right.X)<1e-4f && MathF.Abs(input.RightFootHit.Position.Z-right.Z)<1e-4f,
            "Native right hit is not on its vertical trace.");
        if (AlsP3FrameStages.SplitFeet)
        {
            var stages = _active.SplitFootDiagnostics;
            Require(root.UsesRefactoredFeet && root.FootPoseIdentity == frame.Identity &&
                stages.Prepared == stages.Queried && stages.Prepared == stages.Resumed && !stages.Pending &&
                stages.Request.Identity == frame.Identity && stages.Response.Queries == stages.Request,
                "Refactored production skipped a stage or published stale/unresolved foot queries.");
            Require(frame.FootProbeSource.BasedFootLock == AlsBasedFootLockDiagnostics.From(root.RefactoredLocks),
                "Production lock diagnostics did not come from the committed Refactored owner.");
            Require(frame.Result.LeftFootPose.LockAmount == Math.Clamp(root.RefactoredLocks.Left.Amount, 0, 1) &&
                frame.Result.RightFootPose.LockAmount == Math.Clamp(root.RefactoredLocks.Right.Amount, 0, 1),
                "Published lock amounts differ from the committed Refactored locks.");
            Require(frame.Result.PelvisOffset == new System.Numerics.Vector3(0, root.RefactoredRig.PelvisOffset * .01f, 0),
                "Published pelvis diagnostics differ from the actual Refactored rig.");
            if (root.RefactoredLocks.Left.Amount > 0 || root.RefactoredLocks.Right.Amount > 0) _nativeLockFrames++;
            if (Math.Abs(root.RefactoredRig.LeftOffset.OffsetZ) > 1e-4 ||
                Math.Abs(root.RefactoredRig.RightOffset.OffsetZ) > 1e-4) _nativeOffsetFrames++;
            _nativeFootFrames++; _previousNativeFeet = published;
            return;
        }
        var state = frame.FootProbeSource.NativeFootState;
        Require(frame.FootProbeSource.BasedFootLock.Valid == (OS.GetCmdlineUserArgs().Contains("--based-foot-lock") ? 1 : 0),
            "Production foot policy differs from the requested source version.");
        Require(frame.Result.LeftFootPose.LockAmount == (float)Math.Clamp(state.LeftLock.Alpha, 0, 1) &&
            frame.Result.RightFootPose.LockAmount == (float)Math.Clamp(state.RightLock.Alpha, 0, 1),
            "Published lock amounts differ from the evaluated native properties.");
        if (frame.Identity.FrameId >= 600 && frame.Identity.FrameId % 30 == 0)
            GD.Print($"NATIVE_FOOT_TRACE frame={frame.Identity.FrameId} enabled={frame.Result.LeftFootIkWeight},{frame.Result.RightFootIkWeight} curve={frame.Result.LeftFootLockCurve},{frame.Result.RightFootLockCurve} lock={state.LeftLock.Alpha},{state.RightLock.Alpha} pelvis={state.Pelvis.Alpha} source={scene.PoseIdentity.FrameId}");
        _nativeMaxLockCurve = MathF.Max(_nativeMaxLockCurve, MathF.Max(frame.Result.LeftFootLockCurve, frame.Result.RightFootLockCurve));
        if (state.LeftLock.Alpha > 0 || state.RightLock.Alpha > 0) _nativeLockFrames++;
        if (state.LeftOffset.Location.LengthSquared > 1e-8 || state.RightOffset.Location.LengthSquared > 1e-8) _nativeOffsetFrames++;
        _nativeFootFrames++; _previousNativeFeet = published;
    }
}
