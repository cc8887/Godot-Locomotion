using Godot;
using GodotAls.Animation;
using GodotAls.Assets;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using GodotAls.Import.Compilation;

namespace GodotAls.Locomotion;

public partial class P4PoseSmoke : Node
{
    private const string P3ProfilePath = "res://assets/config/p3_locomotion_profile.json";
    private const string P4ProfilePath = "res://assets/config/p4_pose_profile.json";
    private const double DeltaTime = 1.0 / 60.0;

    private static readonly string[] ProtectedBoneNames =
    [
        "root", "pelvis", "thigh_l", "thigh_r", "foot_l", "foot_r",
    ];

    public override void _Ready()
    {
        try
        {
            var evidence = RunSmoke();
            GD.Print(
                $"P4_POSE_OK aim={evidence.AimDigest:X16} turn={evidence.TurnDigest:X16} " +
                $"rotate={evidence.RotateDigest:X16} rollback={evidence.RollbackCount} " +
                $"alloc={evidence.AllocatedBytes}B");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private Evidence RunSmoke()
    {
        var resource = ResourceLoader.Load<AlsAnimationSetResource>(
            AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS animation set could not be loaded.");
        var definition = resource.LoadDefinition();
        var locomotionProfile = AlsLocomotionProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(P3ProfilePath)), definition);
        var poseProfile = AlsPoseProfileCompiler.Compile(
            File.ReadAllText(ProjectSettings.GlobalizePath(P4ProfilePath)), definition);
        var settings = AlsLocomotionSettings.Load(
            Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));

        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(graph, settings);
        controller.Warmup();
        using var modifier = new AlsComponentPoseModifier(
            graph.TargetSkeleton,
            (Node3D)library.Root,
            library,
            definition,
            poseProfile);

        var result = ValidResult();
        var disabled = AlsP4AnimationInput.Disabled;
        controller.Apply(in result, in disabled, DeltaTime);
        var basePose = PoseSnapshot.Capture(graph.TargetSkeleton, (Node3D)library.Root);
        var protectedIds = ProtectedBoneNames.Select(graph.TargetSkeleton.FindBone).ToArray();
        Require(protectedIds.All(id => id >= 0), "protected Mannequin bones are missing");

        var cases = new[]
        {
            new PoseCase("center", 0.5f, 0f, 1f, 0f),
            new PoseCase("up", 0.5f, 0f, 0f, 1f),
            new PoseCase("down", 0.5f, 1f, 0f, 0f),
            new PoseCase("left", 0f, 0f, 1f, 0f),
            new PoseCase("right", 1f, 0f, 1f, 0f),
        };
        var digests = new ulong[cases.Length];
        for (var index = 0; index < cases.Length; index++)
        {
            basePose.Restore(graph.TargetSkeleton, (Node3D)library.Root);
            var before = PoseSnapshot.Capture(graph.TargetSkeleton, (Node3D)library.Root);
            var input = cases[index].ToInput();
            var output = default(AlsPoseModifierOutput);
            Require(modifier.TryApply(in input, ref output, out var reason),
                $"{cases[index].Name} pose failed: {reason}");
            Require(reason == AlsP4ReasonCode.None && output.PoseDigest != 0,
                $"{cases[index].Name} pose did not publish a valid digest");
            Require(output.AdditiveBaseAnimationId == poseProfile.Aim.AdditiveBasePoseAnimationId,
                "modifier did not use the validated additive base animation");
            Require(output.WriteTransactionCount == 1,
                "modifier did not publish exactly one Skeleton write transaction");
            Require(output.DeterministicElapsedTicks > 0,
                "modifier did not publish deterministic operation ticks");
            if (index == 0)
            {
                VerifyValidatedBaseDelta(
                    before,
                    graph.TargetSkeleton,
                    library,
                    definition,
                    poseProfile);
            }
            VerifyProtectedBonesBitEqual(before, graph.TargetSkeleton, protectedIds, cases[index].Name);
            VerifyOnlyCompiledAimMasksChanged(
                before, graph.TargetSkeleton, definition.Skeletons[poseProfile.SkeletonId], poseProfile,
                cases[index].Name);
            digests[index] = output.PoseDigest;

            basePose.Restore(graph.TargetSkeleton, (Node3D)library.Root);
            var repeated = default(AlsPoseModifierOutput);
            Require(modifier.TryApply(in input, ref repeated, out reason) &&
                    repeated.PoseDigest == digests[index] &&
                    repeated.DeterministicElapsedTicks == output.DeterministicElapsedTicks,
                $"{cases[index].Name} pose digest was not stable: " +
                $"first={digests[index]:X16} repeated={repeated.PoseDigest:X16} reason={reason}");
        }
        Require(digests.Distinct().Count() == cases.Length,
            "center/up/down/left/right did not produce five distinct pose digests");

        VerifyArmLocalMeshEndpoints(
            modifier,
            basePose,
            graph.TargetSkeleton,
            (Node3D)library.Root,
            library,
            definition,
            poseProfile,
            cases[3].ToInput() with { UpperBodyWeight = 0.5f });
        VerifyTopologyMutationRejected(
            modifier,
            basePose,
            graph.TargetSkeleton,
            (Node3D)library.Root,
            cases[0].ToInput());
        var turnDigest = RunAction(
            controller, modifier, graph.TargetSkeleton, (Node3D)library.Root, result,
            AlsP4AnimationInput.Turn(
                poseProfile.Turns[0].AnimationId,
                poseProfile.Turns[0].BasePlayRate,
                definition.Animations[poseProfile.Turns[0].AnimationId].PlayLength * 0.5f,
                0f, 0f, 0f, 0f));
        var rotateDigest = RunAction(
            controller, modifier, graph.TargetSkeleton, (Node3D)library.Root, result,
            AlsP4AnimationInput.Rotate(
                poseProfile.Rotates[0].AnimationId,
                1.15f,
                definition.Animations[poseProfile.Rotates[0].AnimationId].PlayLength * 0.5f,
                0f, 0f, 0f, 0f));
        Require(turnDigest != 0 && rotateDigest != 0 && turnDigest != rotateDigest,
            "Turn/Rotate pose evidence was missing or identical");

        basePose.Restore(graph.TargetSkeleton, (Node3D)library.Root);
        var rollbackPose = PoseSnapshot.Capture(graph.TargetSkeleton, (Node3D)library.Root);
        var rollbackOutput = new AlsPoseModifierOutput
        {
            PoseDigest = 0x1122334455667788UL,
            DeterministicElapsedTicks = 31337,
            WriteTransactionCount = 17,
        };
        var rollbackInput = cases[0].ToInput() with
        {
            InjectFailure = AlsPoseModifierFailureStage.AfterAim,
        };
        Require(!modifier.TryApply(in rollbackInput, ref rollbackOutput, out var rollbackReason),
            "injected post-Aim failure unexpectedly succeeded");
        Require(rollbackReason == AlsP4ReasonCode.InvalidRuntimeState,
            "injected post-Aim failure returned the wrong bounded reason");
        Require(rollbackOutput.PoseDigest == 0x1122334455667788UL &&
                rollbackOutput.DeterministicElapsedTicks == 31337 &&
                rollbackOutput.WriteTransactionCount == 17,
            "failed modifier transaction published a new output");
        rollbackPose.RequireExact(graph.TargetSkeleton, (Node3D)library.Root, "post-Aim rollback");

        var zeroInput = cases[0].ToInput() with
        {
            HeadWeight = 0f,
            SpineWeight = 0f,
            UpperBodyWeight = 0f,
        };
        var zeroOutput = default(AlsPoseModifierOutput);
        for (var index = 0; index < 100; index++)
        {
            Require(modifier.TryApply(in zeroInput, ref zeroOutput, out _),
                "modifier allocation warmup failed");
        }
        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++)
        {
            if (!modifier.TryApply(in zeroInput, ref zeroOutput, out _))
            {
                throw new InvalidOperationException("steady modifier evaluation failed");
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        Require(allocated == 0, $"steady modifier path allocated {allocated} B");

        VerifyWriteFailureTransactions(
            basePose,
            graph.TargetSkeleton,
            (Node3D)library.Root,
            library,
            definition,
            poseProfile,
            cases[0].ToInput());

        ulong aimDigest = 14695981039346656037UL;
        foreach (var digest in digests)
        {
            Append(ref aimDigest, digest);
        }
        return new Evidence(aimDigest, turnDigest, rotateDigest, 1, allocated);
    }

    private static ulong RunAction(
        AlsLocomotionAnimationController controller,
        AlsComponentPoseModifier modifier,
        Skeleton3D skeleton,
        Node3D visualRoot,
        AlsFrameResult result,
        AlsP4AnimationInput action)
    {
        for (var frame = 0; frame < 20; frame++)
        {
            controller.Apply(in result, in action, DeltaTime);
        }
        var input = new AlsPoseModifierInput(
            0.5f, 0f, 1f, 0f, 1f, 1f, 1f, 0f,
            AlsPoseModifierFailureStage.None);
        var output = default(AlsPoseModifierOutput);
        Require(modifier.TryApply(in input, ref output, out var reason),
            $"action modifier failed: {reason}");
        return output.PoseDigest ^ ComputePoseDigest(skeleton, visualRoot.GlobalTransform);
    }

    private static void VerifyProtectedBonesBitEqual(
        PoseSnapshot before,
        Skeleton3D skeleton,
        int[] protectedIds,
        string label)
    {
        foreach (var boneId in protectedIds)
        {
            before.RequireBoneExact(skeleton, boneId, $"{label} protected bone");
        }
    }

    private static void VerifyValidatedBaseDelta(
        PoseSnapshot before,
        Skeleton3D skeleton,
        AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition definition,
        AlsPoseAnimationProfile profile)
    {
        var upperBody = profile.Masks.Entries.Single(
            entry => entry.Kind == AlsPoseMaskKind.UpperBody);
        var skeletonDefinition = definition.Skeletons[profile.SkeletonId];
        var physicalBoneId = upperBody.BoneIds
            .Select(logicalId => skeletonDefinition.LogicalToPhysical[logicalId])
            .Where(id => id >= 0)
            .OrderByDescending(id => BoneDepth(skeleton, id))
            .First();
        Require(skeleton.GetBoneParent(physicalBoneId) >= 0,
            "UpperBody direct reference has no affected parent chain");

        var forwardDefinition = definition.Animations[profile.Aim.ForwardAnimationId];
        var downDefinition = definition.Animations[profile.Aim.DownAnimationId];
        var baseDefinition = definition.Animations[profile.Aim.AdditiveBasePoseAnimationId];
        var baseTime = (double)downDefinition.AdditiveBasePoseFrame *
            baseDefinition.FrameRateDenominator / baseDefinition.FrameRateNumerator;
        var currentComponent = BuildSnapshotComponent(before, skeleton, physicalBoneId);
        var baseComponent = SampleComponent(
            library, skeleton, profile.Aim.AdditiveBasePoseAnimationId,
            physicalBoneId, baseTime);
        var forwardComponent = SampleComponent(
            library, skeleton, profile.Aim.ForwardAnimationId,
            physicalBoneId, forwardDefinition.PlayLength * 0.5);
        var expected = forwardComponent * baseComponent.AffineInverse() * currentComponent;
        var actual = BuildCurrentComponent(skeleton, physicalBoneId);
        RequireTransformNear(actual, expected, 1e-5f,
            "mesh-space Aim delta was not relative to the validated additive base frame");
    }

    private static void VerifyArmLocalMeshEndpoints(
        AlsComponentPoseModifier modifier,
        PoseSnapshot basePose,
        Skeleton3D skeleton,
        Node3D visualRoot,
        AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition definition,
        AlsPoseAnimationProfile profile,
        in AlsPoseModifierInput input)
    {
        var skeletonDefinition = definition.Skeletons[profile.SkeletonId];
        var armMask = profile.Masks.Entries.Single(
            entry => entry.Kind == AlsPoseMaskKind.LeftArm);
        var forwardDefinition = definition.Animations[profile.Aim.ForwardAnimationId];
        var downDefinition = definition.Animations[profile.Aim.DownAnimationId];
        var baseDefinition = definition.Animations[profile.Aim.AdditiveBasePoseAnimationId];
        var baseTime = (double)downDefinition.AdditiveBasePoseFrame *
            baseDefinition.FrameRateDenominator / baseDefinition.FrameRateNumerator;
        var armBoneId = skeletonDefinition.LogicalToPhysical[armMask.RootBoneId];
        Require(armBoneId >= 0, "LeftArm root has no physical bone");
        var armParentId = skeleton.GetBoneParent(armBoneId);
        Require(armParentId >= 0, "LeftArm root has no physical parent");
        var baseLocal = SampleLocal(
            library, skeleton, profile.Aim.AdditiveBasePoseAnimationId, armBoneId, baseTime);
        var forwardLocal = SampleLocal(
            library, skeleton, profile.Aim.ForwardAnimationId, armBoneId,
            forwardDefinition.PlayLength * input.AimPhase);

        basePose.Restore(skeleton, visualRoot);
        var perturbedRotation = (new Quaternion(Vector3.Up, 0.23f) *
            skeleton.GetBonePoseRotation(armParentId)).Normalized();
        skeleton.SetBonePoseRotation(armParentId, perturbedRotation);
        var underlyingPose = PoseSnapshot.Capture(skeleton, visualRoot);
        var originalLocal = skeleton.GetBoneRest(armBoneId) *
            underlyingPose.GetBonePose(armBoneId);
        var localDelta = forwardLocal * baseLocal.AffineInverse();
        var expectedFullLocal = Transform3D.Identity.InterpolateWith(
            localDelta, input.UpperBodyWeight) * originalLocal;

        underlyingPose.Restore(skeleton, visualRoot);
        var fullLocal = input with { ArmLocalWeight = 1f };
        var localOutput = default(AlsPoseModifierOutput);
        Require(modifier.TryApply(in fullLocal, ref localOutput, out var reason) &&
                reason == AlsP4ReasonCode.None,
            $"full-local Arm modifier failed: {reason}");
        var actualFullLocal = skeleton.GetBoneRest(armBoneId) * CurrentBonePose(skeleton, armBoneId);
        RequireTransformNear(actualFullLocal, expectedFullLocal, 1e-5f,
            "full-local Arm did not match the independent local-delta oracle");
        var fullLocalComponent = BuildCurrentComponent(skeleton, armBoneId);

        underlyingPose.Restore(skeleton, visualRoot);
        var fullMesh = input with { ArmLocalWeight = 0f };
        var meshOutput = default(AlsPoseModifierOutput);
        Require(modifier.TryApply(in fullMesh, ref meshOutput, out reason) &&
                reason == AlsP4ReasonCode.None,
            $"full-mesh Arm modifier failed: {reason}");
        var originalComponent = BuildSnapshotComponent(underlyingPose, skeleton, armBoneId);
        var baseComponent = SampleComponent(
            library, skeleton, profile.Aim.AdditiveBasePoseAnimationId, armBoneId, baseTime);
        var forwardComponent = SampleComponent(
            library, skeleton, profile.Aim.ForwardAnimationId, armBoneId,
            forwardDefinition.PlayLength * input.AimPhase);
        var componentDelta = forwardComponent * baseComponent.AffineInverse();
        var expectedFullMesh = Transform3D.Identity.InterpolateWith(
            componentDelta, input.UpperBodyWeight) * originalComponent;
        var actualFullMesh = BuildCurrentComponent(skeleton, armBoneId);
        RequireTransformNear(actualFullMesh, expectedFullMesh, 1e-5f,
            "full-mesh Arm did not match the independent component-delta oracle");
        Require(!TransformNear(fullLocalComponent, actualFullMesh, 1e-5f),
            $"full-local and full-mesh Arm endpoints were not distinct: " +
            $"bone={skeletonDefinition.PhysicalBones[armBoneId].Name} " +
            $"local={fullLocalComponent} mesh={actualFullMesh}");
    }

    private static void VerifyTopologyMutationRejected(
        AlsComponentPoseModifier modifier,
        PoseSnapshot basePose,
        Skeleton3D skeleton,
        Node3D visualRoot,
        in AlsPoseModifierInput input)
    {
        basePose.Restore(skeleton, visualRoot);
        var before = PoseSnapshot.Capture(skeleton, visualRoot);
        var boneId = skeleton.FindBone("hand_l");
        var replacementParent = skeleton.FindBone("root");
        Require(boneId >= 0 && replacementParent >= 0, "topology mutation fixture bones are missing");
        var originalParent = skeleton.GetBoneParent(boneId);
        var output = SentinelOutput();
        try
        {
            skeleton.SetBoneParent(boneId, replacementParent);
            Require(!modifier.TryApply(in input, ref output, out var reason),
                "same-count Skeleton topology mutation unexpectedly succeeded");
            Require(reason == AlsP4ReasonCode.InvalidRuntimeState,
                $"topology mutation returned unstable reason: {reason}");
            RequireOutputExact(output, SentinelOutput(), "topology mutation");
            before.RequireExact(skeleton, visualRoot, "topology mutation");
        }
        finally
        {
            skeleton.SetBoneParent(boneId, originalParent);
        }
    }

    private static void VerifyWriteFailureTransactions(
        PoseSnapshot basePose,
        Skeleton3D skeleton,
        Node3D visualRoot,
        AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition definition,
        AlsPoseAnimationProfile profile,
        in AlsPoseModifierInput input)
    {
        var writer = new FaultingSkeletonPoseWriter(skeleton);
        using var modifier = new AlsComponentPoseModifier(
            skeleton, visualRoot, library, definition, profile, writer);
        for (var setter = 1; setter <= 3; setter++)
        {
            basePose.Restore(skeleton, visualRoot);
            var before = PoseSnapshot.Capture(skeleton, visualRoot);
            writer.Configure(setter, persistent: false);
            var expected = SentinelOutput();
            var output = expected;
            Require(!modifier.TryApply(in input, ref output, out var reason),
                $"one-shot setter {setter} failure unexpectedly succeeded");
            Require(reason == AlsP4ReasonCode.InvalidRuntimeState,
                $"one-shot setter {setter} returned unstable reason: {reason}");
            RequireOutputExact(output, expected, $"one-shot setter {setter}");
            before.RequireExact(skeleton, visualRoot, $"one-shot setter {setter} rollback");
        }

        basePose.Restore(skeleton, visualRoot);
        var frozenBefore = PoseSnapshot.Capture(skeleton, visualRoot);
        writer.Configure(2, persistent: true);
        var frozenExpected = SentinelOutput();
        var frozenOutput = frozenExpected;
        var escaped = false;
        var success = false;
        var frozenReason = AlsP4ReasonCode.None;
        try
        {
            success = modifier.TryApply(in input, ref frozenOutput, out frozenReason);
        }
        catch
        {
            escaped = true;
        }
        Require(!escaped, "persistent setter failure escaped TryApply");
        Require(!success && frozenReason == AlsP4ReasonCode.PoseRestoreFailed,
            $"persistent setter failure did not publish frozen reason: {frozenReason}");
        RequireOutputExact(frozenOutput, frozenExpected, "persistent setter failure");
        Require(!frozenBefore.IsExact(skeleton, visualRoot),
            "persistent setter failure did not leave an observable frozen partial pose");
        basePose.Restore(skeleton, visualRoot);
    }

    private static Transform3D SampleComponent(
        AlsAnimationLibraryBuildResult library,
        Skeleton3D skeleton,
        int animationId,
        int boneId,
        double time)
    {
        Require(library.ClipNames.TryGetValue(animationId, out var clipName),
            $"direct reference clip is missing: {animationId}");
        var animation = library.Library.GetAnimation(clipName)
            ?? throw new InvalidOperationException($"direct reference animation is missing: {animationId}");
        var chain = BuildChain(skeleton, boneId);
        var component = Transform3D.Identity;
        foreach (var current in chain)
        {
            var position = Vector3.Zero;
            var rotation = Quaternion.Identity;
            var scale = Vector3.One;
            for (var track = 0; track < animation.GetTrackCount(); track++)
            {
                using var path = animation.TrackGetPath(track);
                var text = path.ToString();
                var separator = text.LastIndexOf(':');
                if (separator < 0 || skeleton.FindBone(text[(separator + 1)..]) != current)
                {
                    continue;
                }
                switch (animation.TrackGetType(track))
                {
                    case Godot.Animation.TrackType.Position3D:
                        position = animation.PositionTrackInterpolate(track, time);
                        break;
                    case Godot.Animation.TrackType.Rotation3D:
                        rotation = animation.RotationTrackInterpolate(track, time);
                        break;
                    case Godot.Animation.TrackType.Scale3D:
                        scale = animation.ScaleTrackInterpolate(track, time);
                        break;
                }
            }
            var pose = new Transform3D(
                new Basis(rotation.Normalized()).Scaled(scale),
                position);
            component *= skeleton.GetBoneRest(current) * pose;
        }
        return component;
    }

    private static Transform3D SampleLocal(
        AlsAnimationLibraryBuildResult library,
        Skeleton3D skeleton,
        int animationId,
        int boneId,
        double time)
    {
        Require(library.ClipNames.TryGetValue(animationId, out var clipName),
            $"local oracle clip is missing: {animationId}");
        var animation = library.Library.GetAnimation(clipName)
            ?? throw new InvalidOperationException($"local oracle animation is missing: {animationId}");
        var position = Vector3.Zero;
        var rotation = Quaternion.Identity;
        var scale = Vector3.One;
        for (var track = 0; track < animation.GetTrackCount(); track++)
        {
            using var path = animation.TrackGetPath(track);
            var text = path.ToString();
            var separator = text.LastIndexOf(':');
            if (separator < 0 || skeleton.FindBone(text[(separator + 1)..]) != boneId)
            {
                continue;
            }
            switch (animation.TrackGetType(track))
            {
                case Godot.Animation.TrackType.Position3D:
                    position = animation.PositionTrackInterpolate(track, time);
                    break;
                case Godot.Animation.TrackType.Rotation3D:
                    rotation = animation.RotationTrackInterpolate(track, time);
                    break;
                case Godot.Animation.TrackType.Scale3D:
                    scale = animation.ScaleTrackInterpolate(track, time);
                    break;
            }
        }
        return skeleton.GetBoneRest(boneId) * new Transform3D(
            new Basis(rotation.Normalized()).Scaled(scale), position);
    }

    private static Transform3D CurrentBonePose(Skeleton3D skeleton, int boneId) => new(
        new Basis(skeleton.GetBonePoseRotation(boneId).Normalized()).Scaled(
            skeleton.GetBonePoseScale(boneId)),
        skeleton.GetBonePosePosition(boneId));

    private static Transform3D BuildSnapshotComponent(
        PoseSnapshot snapshot,
        Skeleton3D skeleton,
        int boneId)
    {
        var component = Transform3D.Identity;
        foreach (var current in BuildChain(skeleton, boneId))
        {
            var pose = snapshot.GetBonePose(current);
            component *= skeleton.GetBoneRest(current) * pose;
        }
        return component;
    }

    private static Transform3D BuildCurrentComponent(Skeleton3D skeleton, int boneId)
    {
        var component = Transform3D.Identity;
        foreach (var current in BuildChain(skeleton, boneId))
        {
            var pose = new Transform3D(
                new Basis(skeleton.GetBonePoseRotation(current).Normalized()).Scaled(
                    skeleton.GetBonePoseScale(current)),
                skeleton.GetBonePosePosition(current));
            component *= skeleton.GetBoneRest(current) * pose;
        }
        return component;
    }

    private static int[] BuildChain(Skeleton3D skeleton, int boneId)
    {
        var count = 0;
        for (var current = boneId; current >= 0; current = skeleton.GetBoneParent(current))
        {
            count++;
        }
        var chain = new int[count];
        var index = count;
        for (var current = boneId; current >= 0; current = skeleton.GetBoneParent(current))
        {
            chain[--index] = current;
        }
        return chain;
    }

    private static int BoneDepth(Skeleton3D skeleton, int boneId)
    {
        var depth = 0;
        for (var current = boneId; current >= 0; current = skeleton.GetBoneParent(current))
        {
            depth++;
        }
        return depth;
    }

    private static void RequireTransformNear(
        in Transform3D actual,
        in Transform3D expected,
        float tolerance,
        string message)
    {
        if (actual.Origin.DistanceTo(expected.Origin) > tolerance ||
            actual.Basis.X.DistanceTo(expected.Basis.X) > tolerance ||
            actual.Basis.Y.DistanceTo(expected.Basis.Y) > tolerance ||
            actual.Basis.Z.DistanceTo(expected.Basis.Z) > tolerance)
        {
            throw new InvalidOperationException(
                $"{message}: actual={actual} expected={expected}");
        }
    }

    private static bool TransformNear(
        in Transform3D left,
        in Transform3D right,
        float tolerance) =>
        left.Origin.DistanceTo(right.Origin) <= tolerance &&
        left.Basis.X.DistanceTo(right.Basis.X) <= tolerance &&
        left.Basis.Y.DistanceTo(right.Basis.Y) <= tolerance &&
        left.Basis.Z.DistanceTo(right.Basis.Z) <= tolerance;

    private static void VerifyOnlyCompiledAimMasksChanged(
        PoseSnapshot before,
        Skeleton3D skeleton,
        AlsSkeletonDefinition skeletonDefinition,
        AlsPoseAnimationProfile profile,
        string label)
    {
        var allowed = new bool[skeleton.GetBoneCount()];
        foreach (var mask in profile.Masks.Entries)
        {
            if (mask.Kind is not (AlsPoseMaskKind.UpperBody or AlsPoseMaskKind.Head or
                AlsPoseMaskKind.LeftArm or AlsPoseMaskKind.RightArm or
                AlsPoseMaskKind.LeftHand or AlsPoseMaskKind.RightHand))
            {
                continue;
            }
            foreach (var logicalId in mask.BoneIds)
            {
                var physicalId = skeletonDefinition.LogicalToPhysical[logicalId];
                if (physicalId >= 0)
                {
                    allowed[physicalId] = true;
                }
            }
        }

        var changed = 0;
        for (var boneId = 0; boneId < allowed.Length; boneId++)
        {
            if (!before.IsBoneExact(skeleton, boneId))
            {
                Require(allowed[boneId], $"{label} changed bone outside compiled Aim masks: {boneId}");
                changed++;
            }
        }
        Require(changed > 0, $"{label} did not change any compiled Aim bone");
    }

    private static AlsFrameResult ValidResult() => new()
    {
        ResolvedLocomotionState = AlsLocomotionState.Grounded,
        RequestedDriveMode = AlsDriveMode.MotorDriven,
        ActualGait = AlsGait.Running,
        ActualStance = AlsStance.Standing,
        ActualRotationMode = AlsRotationMode.LookingDirection,
        AnimationState = AlsAnimationState.Grounded,
        BlendCoordinates = System.Numerics.Vector2.UnitY,
        Stride = 1f,
        PlayRate = 1f,
        Lean = System.Numerics.Vector2.Zero,
    };

    private static ulong ComputePoseDigest(Skeleton3D skeleton, in Transform3D root)
    {
        var digest = 14695981039346656037UL;
        for (var boneId = 0; boneId < skeleton.GetBoneCount(); boneId++)
        {
            Append(ref digest, skeleton.GetBonePosePosition(boneId));
            Append(ref digest, skeleton.GetBonePoseRotation(boneId));
            Append(ref digest, skeleton.GetBonePoseScale(boneId));
        }
        Append(ref digest, root.Origin);
        return digest;
    }

    private static void Append(ref ulong digest, ulong value)
    {
        const ulong prime = 1099511628211UL;
        for (var shift = 0; shift < 64; shift += 8)
        {
            digest ^= (byte)(value >> shift);
            digest *= prime;
        }
    }

    private static void Append(ref ulong digest, float value) =>
        Append(ref digest, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void Append(ref ulong digest, Vector3 value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
    }

    private static void Append(ref ulong digest, Quaternion value)
    {
        Append(ref digest, value.X);
        Append(ref digest, value.Y);
        Append(ref digest, value.Z);
        Append(ref digest, value.W);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static AlsPoseModifierOutput SentinelOutput() => new()
    {
        PoseDigest = 0x1122334455667788UL,
        DeterministicElapsedTicks = 31337,
        WriteTransactionCount = 17,
        AffectedBoneCount = 19,
        ArmLocalWeight = 0.25f,
        ArmMeshWeight = 0.75f,
        AdditiveBaseAnimationId = 911,
    };

    private static void RequireOutputExact(
        in AlsPoseModifierOutput actual,
        in AlsPoseModifierOutput expected,
        string label)
    {
        Require(actual.PoseDigest == expected.PoseDigest &&
                actual.DeterministicElapsedTicks == expected.DeterministicElapsedTicks &&
                actual.WriteTransactionCount == expected.WriteTransactionCount &&
                actual.AffectedBoneCount == expected.AffectedBoneCount &&
                BitConverter.SingleToInt32Bits(actual.ArmLocalWeight) ==
                BitConverter.SingleToInt32Bits(expected.ArmLocalWeight) &&
                BitConverter.SingleToInt32Bits(actual.ArmMeshWeight) ==
                BitConverter.SingleToInt32Bits(expected.ArmMeshWeight) &&
                actual.AdditiveBaseAnimationId == expected.AdditiveBaseAnimationId,
            $"{label} changed failed-transaction output");
    }

    private readonly record struct PoseCase(
        string Name,
        float Phase,
        float Down,
        float Forward,
        float Up)
    {
        public AlsPoseModifierInput ToInput() => new(
            Phase, Down, Forward, Up,
            1f, 1f, 1f, 0f,
            AlsPoseModifierFailureStage.None);
    }

    private readonly record struct Evidence(
        ulong AimDigest,
        ulong TurnDigest,
        ulong RotateDigest,
        int RollbackCount,
        long AllocatedBytes);

    private sealed class PoseSnapshot
    {
        private readonly Vector3[] _positions;
        private readonly Quaternion[] _rotations;
        private readonly Vector3[] _scales;
        private readonly Transform3D _root;

        private PoseSnapshot(
            Vector3[] positions,
            Quaternion[] rotations,
            Vector3[] scales,
            in Transform3D root)
        {
            _positions = positions;
            _rotations = rotations;
            _scales = scales;
            _root = root;
        }

        public static PoseSnapshot Capture(Skeleton3D skeleton, Node3D root)
        {
            var count = skeleton.GetBoneCount();
            var positions = new Vector3[count];
            var rotations = new Quaternion[count];
            var scales = new Vector3[count];
            for (var index = 0; index < count; index++)
            {
                positions[index] = skeleton.GetBonePosePosition(index);
                rotations[index] = skeleton.GetBonePoseRotation(index);
                scales[index] = skeleton.GetBonePoseScale(index);
            }
            return new PoseSnapshot(positions, rotations, scales, root.GlobalTransform);
        }

        public void Restore(Skeleton3D skeleton, Node3D root)
        {
            root.GlobalTransform = _root;
            for (var index = 0; index < _positions.Length; index++)
            {
                skeleton.SetBonePosePosition(index, _positions[index]);
                skeleton.SetBonePoseRotation(index, _rotations[index]);
                skeleton.SetBonePoseScale(index, _scales[index]);
            }
        }

        public bool IsBoneExact(Skeleton3D skeleton, int boneId) =>
            skeleton.GetBonePosePosition(boneId) == _positions[boneId] &&
            skeleton.GetBonePoseRotation(boneId) == _rotations[boneId] &&
            skeleton.GetBonePoseScale(boneId) == _scales[boneId];

        public bool IsExact(Skeleton3D skeleton, Node3D root)
        {
            if (root.GlobalTransform != _root)
            {
                return false;
            }
            for (var index = 0; index < _positions.Length; index++)
            {
                if (!IsBoneExact(skeleton, index))
                {
                    return false;
                }
            }
            return true;
        }

        public Transform3D GetBonePose(int boneId) => new(
            new Basis(_rotations[boneId].Normalized()).Scaled(_scales[boneId]),
            _positions[boneId]);

        public void RequireBoneExact(Skeleton3D skeleton, int boneId, string label) =>
            Require(IsBoneExact(skeleton, boneId), $"{label} changed: {boneId}");

        public void RequireExact(Skeleton3D skeleton, Node3D root, string label)
        {
            Require(root.GlobalTransform == _root, $"{label} did not restore visual root");
            for (var index = 0; index < _positions.Length; index++)
            {
                RequireBoneExact(skeleton, index, label);
            }
        }
    }

    private sealed class FaultingSkeletonPoseWriter : IAlsSkeletonPoseWriter
    {
        private readonly Skeleton3D _skeleton;
        private int _throwAt;
        private int _writeCount;
        private bool _persistent;
        private bool _triggered;

        public FaultingSkeletonPoseWriter(Skeleton3D skeleton) => _skeleton = skeleton;

        public void Configure(int throwAt, bool persistent)
        {
            _throwAt = throwAt;
            _writeCount = 0;
            _persistent = persistent;
            _triggered = false;
        }

        public void SetBonePosePosition(int boneId, in Vector3 value)
        {
            BeforeWrite();
            _skeleton.SetBonePosePosition(boneId, value);
            AfterWrite();
        }

        public void SetBonePoseRotation(int boneId, in Quaternion value)
        {
            BeforeWrite();
            _skeleton.SetBonePoseRotation(boneId, value);
            AfterWrite();
        }

        public void SetBonePoseScale(int boneId, in Vector3 value)
        {
            BeforeWrite();
            _skeleton.SetBonePoseScale(boneId, value);
            AfterWrite();
        }

        private void BeforeWrite()
        {
            if (_persistent && _triggered)
            {
                throw new InvalidOperationException("injected persistent Skeleton setter failure");
            }
        }

        private void AfterWrite()
        {
            _writeCount++;
            if (_writeCount != _throwAt)
            {
                return;
            }
            _triggered = true;
            throw new InvalidOperationException("injected Skeleton setter interruption");
        }
    }
}
