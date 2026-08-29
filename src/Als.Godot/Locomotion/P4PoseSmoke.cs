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
            var allocationMode = IsControlledAllocationEnvironment()
                ? "controlled"
                : "uncontrolled";
            var allocationEvidence = allocationMode == "controlled"
                ? "alloc=0B "
                : string.Empty;
            GD.Print(
                $"P4_POSE_OK aim={evidence.AimDigest:X16} turn={evidence.TurnDigest:X16} " +
                $"rotate={evidence.RotateDigest:X16} rollback={evidence.RollbackCount} " +
                $"{allocationEvidence}allocation_mode={allocationMode}");
            if (allocationMode == "uncontrolled")
            {
                GD.Print(
                    $"P4_POSE_ALLOCATION_UNCONTROLLED zero={evidence.AllocatedBytes}B " +
                    $"active={evidence.ActiveAllocatedBytes}B " +
                    "set_DOTNET_TieredCompilation_and_COMPlus_TieredCompilation_to_0_for_evidence=1");
            }
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
            File.ReadAllText(ProjectSettings.GlobalizePath(P4ProfilePath)),
            definition,
            locomotionProfile);
        var settings = AlsLocomotionSettings.Load(
            Godot.FileAccess.GetFileAsString("res://assets/config/p3_locomotion_settings.json"));

        using var library = AlsAnimationLibraryBuilder.Build(
            definition, locomotionProfile, poseProfile);
        AddChild(library.Root);
        using var graph = AlsLocomotionGraphBuilder.Build(
            library, locomotionProfile, poseProfile, definition);
        using var controller = new AlsLocomotionAnimationController(
            graph, settings, poseProfile, definition);
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
        VerifyNonCommutingLocalDeltaOracle();

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
            Require(output.OperationTicks > 0,
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
                    repeated.OperationTicks == output.OperationTicks,
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
        VerifySkeletonIdentityMutationsRejected(
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
        var rollbackExpected = SentinelOutput();
        var rollbackOutput = rollbackExpected;
        var rollbackInput = cases[0].ToInput() with
        {
            InjectFailure = AlsPoseModifierFailureStage.AfterAim,
        };
        Require(!modifier.TryApply(in rollbackInput, ref rollbackOutput, out var rollbackReason),
            "injected post-Aim failure unexpectedly succeeded");
        Require(rollbackReason == AlsP4ReasonCode.InvalidRuntimeState,
            "injected post-Aim failure returned the wrong bounded reason");
        RequireOutputExact(rollbackOutput, rollbackExpected, "post-Aim rollback");
        rollbackPose.RequireExact(graph.TargetSkeleton, (Node3D)library.Root, "post-Aim rollback");

        VerifyFootPlacementTransaction(
            modifier,
            basePose,
            graph.TargetSkeleton,
            (Node3D)library.Root,
            poseProfile,
            zeroAim: cases[0].ToInput() with
            {
                HeadWeight = 0f,
                SpineWeight = 0f,
                UpperBodyWeight = 0f,
            });

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
        var nameValidationsBeforeSteady = modifier.NameValidationCount;
        var allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var steadyStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var index = 0; index < 10_000; index++)
        {
            if (!modifier.TryApply(in zeroInput, ref zeroOutput, out _))
            {
                throw new InvalidOperationException("steady modifier evaluation failed");
            }
        }
        var steadyElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(steadyStartedAt);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        if (IsControlledAllocationEnvironment())
        {
            Require(allocated == 0, $"steady modifier path allocated {allocated} B");
        }
        Require(modifier.NameValidationCount == nameValidationsBeforeSteady,
            "steady modifier path entered the name-validation cold path");
        GD.Print(
            $"P4_POSE_TOPOLOGY_PERF iterations=10000 elapsed_ms={steadyElapsed.TotalMilliseconds:F3} " +
            $"bones={graph.TargetSkeleton.GetBoneCount()} alloc={allocated}B");

        basePose.Restore(graph.TargetSkeleton, (Node3D)library.Root);
        var activeInput = cases[1].ToInput() with { ArmLocalWeight = 0.37f };
        var activeOutput = default(AlsPoseModifierOutput);
        for (var index = 0; index < 100; index++)
        {
            Require(modifier.TryApply(in activeInput, ref activeOutput, out var activeReason),
                $"active Aim allocation warmup failed: {activeReason}");
        }
        var activeNameValidations = modifier.NameValidationCount;
        var activeAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var activeStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var index = 0; index < 10_000; index++)
        {
            if (!modifier.TryApply(in activeInput, ref activeOutput, out _))
            {
                throw new InvalidOperationException("active Aim steady evaluation failed");
            }
        }
        var activeElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(activeStartedAt);
        var activeAllocated = GC.GetAllocatedBytesForCurrentThread() - activeAllocationBefore;
        if (IsControlledAllocationEnvironment())
        {
            Require(activeAllocated == 0, $"active Aim path allocated {activeAllocated} B");
        }
        Require(modifier.NameValidationCount == activeNameValidations,
            "active Aim path entered the name-validation cold path");
        Require(activeOutput.PoseDigest != 0 && activeOutput.OperationTicks > 0 &&
                activeOutput.WriteTransactionCount == 1,
            "active Aim path did not execute the production modifier stages");
        GD.Print(
            $"P4_POSE_ACTIVE_PERF iterations=10000 elapsed_ms={activeElapsed.TotalMilliseconds:F3} " +
            $"bones={graph.TargetSkeleton.GetBoneCount()} affected={activeOutput.AffectedBoneCount} " +
            $"alloc={activeAllocated}B writes=1");
        basePose.Restore(graph.TargetSkeleton, (Node3D)library.Root);

        VerifyWriteFailureTransactions(
            basePose,
            graph.TargetSkeleton,
            (Node3D)library.Root,
            library,
            definition,
            poseProfile,
            cases[0].ToInput());
        VerifyFootWriteFailureTransactions(
            basePose,
            graph.TargetSkeleton,
            (Node3D)library.Root,
            library,
            definition,
            poseProfile,
            zeroInput);
        VerifyNonInvertibleTransactions(
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
        return new Evidence(
            aimDigest,
            turnDigest,
            rotateDigest,
            3,
            allocated,
            activeAllocated);
    }

    private static void VerifyFootPlacementTransaction(
        AlsComponentPoseModifier modifier,
        PoseSnapshot basePose,
        Skeleton3D skeleton,
        Node3D visualRoot,
        AlsPoseAnimationProfile profile,
        AlsPoseModifierInput zeroAim)
    {
        basePose.Restore(skeleton, visualRoot);
        var before = PoseSnapshot.Capture(skeleton, visualRoot);
        var leftWorld = skeleton.GlobalTransform *
            BuildCurrentComponent(skeleton, profile.FootRig.Left.FootBoneId);
        var rightWorld = skeleton.GlobalTransform *
            BuildCurrentComponent(skeleton, profile.FootRig.Right.FootBoneId);
        var characterRotation = visualRoot.GlobalTransform.Basis.Orthonormalized()
            .GetRotationQuaternion();
        var reachableTargetDelta = Vector3.Right * 0.03f + Vector3.Down * 0.02f;
        var leftTargetPosition = leftWorld.Origin + reachableTargetDelta;
        var rightTargetPosition = rightWorld.Origin + reachableTargetDelta;
        var input = zeroAim with
        {
            PelvisOffset = new System.Numerics.Vector3(0f, -0.04f, 0f),
            LeftFootPose = new AlsFootPoseOutput(
                ToNumerics(leftTargetPosition),
                ToNumerics(characterRotation), 0f, -1),
            RightFootPose = new AlsFootPoseOutput(
                ToNumerics(rightTargetPosition),
                ToNumerics(characterRotation), 0f, -1),
            LeftFootIkWeight = 1f,
            RightFootIkWeight = 1f,
        };
        var output = default(AlsPoseModifierOutput);
        Require(modifier.TryApply(in input, ref output, out var reason),
            $"foot placement modifier failed: {reason}");
        Require(output.WriteTransactionCount == 1,
            "foot placement used more than one Skeleton write transaction");
        Require(!before.IsBoneExact(skeleton, profile.FootRig.PelvisBoneId),
            "pelvis correction did not change pelvis pose");
        Require(skeleton.GetBonePoseRotation(profile.FootRig.PelvisBoneId) ==
                before.GetBoneRotation(profile.FootRig.PelvisBoneId) &&
                skeleton.GetBonePoseScale(profile.FootRig.PelvisBoneId) ==
                before.GetBoneScale(profile.FootRig.PelvisBoneId),
            "pelvis correction changed pelvis rotation or scale");
        RequireLegChannelsPreserved(before, skeleton, profile.FootRig.Left, "left");
        RequireLegChannelsPreserved(before, skeleton, profile.FootRig.Right, "right");
        foreach (var untouchedBoneId in new[] { 51, 54, 57, 60 })
        {
            before.RequireBoneExact(skeleton, untouchedBoneId, "foot twist/ball branch");
        }
        var solvedLeft = skeleton.GlobalTransform *
            BuildCurrentComponent(skeleton, profile.FootRig.Left.FootBoneId);
        var solvedRight = skeleton.GlobalTransform *
            BuildCurrentComponent(skeleton, profile.FootRig.Right.FootBoneId);
        Require(solvedLeft.Origin.DistanceTo(leftTargetPosition) <= 0.015f,
            $"left foot missed calibrated target: distance={solvedLeft.Origin.DistanceTo(leftTargetPosition)} " +
            $"before={leftWorld.Origin} target={leftTargetPosition} solved={solvedLeft.Origin}");
        Require(solvedRight.Origin.DistanceTo(rightTargetPosition) <= 0.015f,
            $"right foot missed calibrated target: {solvedRight.Origin.DistanceTo(rightTargetPosition)}");

        foreach (var stage in new[]
                 {
                     AlsPoseModifierFailureStage.AfterPelvis,
                     AlsPoseModifierFailureStage.AfterLeftFoot,
                 })
        {
            basePose.Restore(skeleton, visualRoot);
            var expectedPose = PoseSnapshot.Capture(skeleton, visualRoot);
            var expectedOutput = SentinelOutput();
            var failedOutput = expectedOutput;
            var failedInput = input with { InjectFailure = stage };
            Require(!modifier.TryApply(in failedInput, ref failedOutput, out reason) &&
                    reason == AlsP4ReasonCode.InvalidRuntimeState,
                $"{stage} injection did not fail with the bounded reason");
            RequireOutputExact(failedOutput, expectedOutput, stage.ToString());
            expectedPose.RequireExact(skeleton, visualRoot, stage.ToString());
        }

        basePose.Restore(skeleton, visualRoot);
    }

    private static void RequireLegChannelsPreserved(
        PoseSnapshot before,
        Skeleton3D skeleton,
        AlsCompiledLegChain leg,
        string label)
    {
        foreach (var boneId in new[] { leg.ThighBoneId, leg.KneeBoneId, leg.FootBoneId })
        {
            Require(skeleton.GetBonePosePosition(boneId) == before.GetBonePosition(boneId),
                $"{label} leg changed local translation: {boneId}");
            Require(skeleton.GetBonePoseScale(boneId) == before.GetBoneScale(boneId),
                $"{label} leg changed local scale: {boneId}");
        }
    }

    private static System.Numerics.Vector3 ToNumerics(in Vector3 value) =>
        new(value.X, value.Y, value.Z);

    private static System.Numerics.Quaternion ToNumerics(in Quaternion value) =>
        new(value.X, value.Y, value.Z, value.W);

    private static bool IsControlledAllocationEnvironment() =>
        string.Equals(
            System.Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            "0",
            StringComparison.Ordinal) &&
        string.Equals(
            System.Environment.GetEnvironmentVariable("COMPlus_TieredCompilation"),
            "0",
            StringComparison.Ordinal);

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
        var armRotation = (skeleton.GetBonePoseRotation(armBoneId) *
            new Quaternion(new Vector3(0.31f, 0.73f, 0.19f).Normalized(), 0.37f)).Normalized();
        skeleton.SetBonePoseRotation(armBoneId, armRotation);
        skeleton.SetBonePosePosition(
            armBoneId,
            skeleton.GetBonePosePosition(armBoneId) + new Vector3(0.013f, -0.009f, 0.017f));
        var underlyingPose = PoseSnapshot.Capture(skeleton, visualRoot);
        var originalLocal = underlyingPose.GetBonePose(armBoneId);
        var localDelta = baseLocal.AffineInverse() * forwardLocal;
        var weightedLocalDelta = Transform3D.Identity.InterpolateWith(
            localDelta, input.UpperBodyWeight);
        var expectedFullLocal = originalLocal * weightedLocalDelta;

        underlyingPose.Restore(skeleton, visualRoot);
        var fullLocal = input with { ArmLocalWeight = 1f };
        var localOutput = default(AlsPoseModifierOutput);
        Require(modifier.TryApply(in fullLocal, ref localOutput, out var reason) &&
                reason == AlsP4ReasonCode.None,
            $"full-local Arm modifier failed: {reason}");
        var actualFullLocal = CurrentBonePose(skeleton, armBoneId);
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

    private static void VerifyNonCommutingLocalDeltaOracle()
    {
        var baseLocal = new Transform3D(
            new Basis(new Quaternion(new Vector3(0.23f, 0.71f, 0.41f).Normalized(), 0.47f)),
            new Vector3(0.31f, -0.17f, 0.23f));
        var authoredDelta = new Transform3D(
            new Basis(new Quaternion(new Vector3(0.67f, 0.19f, 0.53f).Normalized(), -0.38f)),
            new Vector3(-0.11f, 0.29f, 0.07f));
        var aimLocal = baseLocal * authoredDelta;
        var currentLocal = new Transform3D(
            new Basis(new Quaternion(new Vector3(0.13f, 0.83f, 0.37f).Normalized(), 0.61f)),
            new Vector3(0.43f, 0.05f, -0.27f));
        const float weight = 0.63f;
        var expected = currentLocal * Transform3D.Identity.InterpolateWith(authoredDelta, weight);
        var legacy = Transform3D.Identity.InterpolateWith(
            aimLocal * baseLocal.AffineInverse(), weight) * currentLocal;
        Require(!TransformNear(expected, legacy, 1e-4f),
            "synthetic B/D/C fixture does not distinguish local multiplication order");
        Require(AlsComponentPoseModifier.TryComposeLocalResult(
                in currentLocal, in baseLocal, in aimLocal, weight, out var actual),
            "synthetic local composition unexpectedly failed");
        RequireTransformNear(actual, expected, 1e-5f,
            "local-space delta composition did not match the independent B^-1*A oracle");
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

    private static void VerifyFootWriteFailureTransactions(
        PoseSnapshot basePose,
        Skeleton3D skeleton,
        Node3D visualRoot,
        AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition definition,
        AlsPoseAnimationProfile profile,
        in AlsPoseModifierInput zeroAim)
    {
        basePose.Restore(skeleton, visualRoot);
        var leftWorld = skeleton.GlobalTransform *
            BuildCurrentComponent(skeleton, profile.FootRig.Left.FootBoneId);
        var rightWorld = skeleton.GlobalTransform *
            BuildCurrentComponent(skeleton, profile.FootRig.Right.FootBoneId);
        var characterRotation = visualRoot.GlobalTransform.Basis.Orthonormalized()
            .GetRotationQuaternion();
        var input = zeroAim with
        {
            PelvisOffset = new System.Numerics.Vector3(0f, -0.04f, 0f),
            LeftFootPose = new AlsFootPoseOutput(
                ToNumerics(leftWorld.Origin + Vector3.Down * 0.08f),
                ToNumerics(characterRotation), 0f, -1),
            RightFootPose = new AlsFootPoseOutput(
                ToNumerics(rightWorld.Origin + Vector3.Down * 0.08f),
                ToNumerics(characterRotation), 0f, -1),
            LeftFootIkWeight = 1f,
            RightFootIkWeight = 1f,
        };
        var writer = new FaultingSkeletonPoseWriter(skeleton);
        using var modifier = new AlsComponentPoseModifier(
            skeleton, visualRoot, library, definition, profile, writer);
        foreach (var setter in new[] { 1, 4, 13 })
        {
            basePose.Restore(skeleton, visualRoot);
            var before = PoseSnapshot.Capture(skeleton, visualRoot);
            writer.Configure(setter, persistent: false);
            var expected = SentinelOutput();
            var output = expected;
            Require(!modifier.TryApply(in input, ref output, out var reason),
                $"foot setter {setter} failure unexpectedly succeeded");
            Require(reason == AlsP4ReasonCode.InvalidRuntimeState,
                $"foot setter {setter} returned unstable reason: {reason}");
            RequireOutputExact(output, expected, $"foot setter {setter}");
            before.RequireExact(skeleton, visualRoot, $"foot setter {setter} rollback");
        }
        basePose.Restore(skeleton, visualRoot);
    }

    private static void VerifyNonInvertibleTransactions(
        PoseSnapshot basePose,
        Skeleton3D skeleton,
        Node3D visualRoot,
        AlsAnimationLibraryBuildResult library,
        AlsAnimationSetDefinition definition,
        AlsPoseAnimationProfile profile,
        in AlsPoseModifierInput input)
    {
        var fixtures = new[]
        {
            AlsPoseAffineTestFixture.SingularBase,
            AlsPoseAffineTestFixture.NearSingularBase,
            AlsPoseAffineTestFixture.SingularAim,
            AlsPoseAffineTestFixture.NearSingularAim,
        };
        foreach (var fixture in fixtures)
        {
            basePose.Restore(skeleton, visualRoot);
            var before = PoseSnapshot.Capture(skeleton, visualRoot);
            var writer = new FaultingSkeletonPoseWriter(skeleton);
            writer.Configure(int.MaxValue, persistent: false);
            using var modifier = new AlsComponentPoseModifier(
                skeleton, visualRoot, library, definition, profile, writer, fixture);
            var expected = SentinelOutput();
            var output = expected;
            Require(!modifier.TryApply(in input, ref output, out var reason),
                $"{fixture} affine fixture unexpectedly succeeded");
            Require(reason == AlsP4ReasonCode.NonFiniteInput,
                $"{fixture} returned unstable reason: {reason}");
            Require(writer.WriteCount == 0, $"{fixture} wrote Skeleton state before rejection");
            RequireOutputExact(output, expected, fixture.ToString());
            before.RequireExact(skeleton, visualRoot, fixture.ToString());
        }

        var guardedWriter = new FaultingSkeletonPoseWriter(skeleton);
        guardedWriter.Configure(int.MaxValue, persistent: false);
        using var guardedModifier = new AlsComponentPoseModifier(
            skeleton, visualRoot, library, definition, profile, guardedWriter);
        var skeletonDefinition = definition.Skeletons[profile.SkeletonId];
        var armMask = profile.Masks.Entries.Single(entry => entry.Kind == AlsPoseMaskKind.LeftArm);
        var armId = skeletonDefinition.LogicalToPhysical[armMask.RootBoneId];
        var parentId = skeleton.GetBoneParent(armId);
        Require(parentId >= 0, "singular current-parent fixture has no parent");
        foreach (var scale in new[] { Vector3.Zero, new Vector3(1f, 1e-7f, 1f) })
        {
            basePose.Restore(skeleton, visualRoot);
            skeleton.SetBonePoseScale(parentId, scale);
            var before = PoseSnapshot.Capture(skeleton, visualRoot);
            guardedWriter.Configure(int.MaxValue, persistent: false);
            var expected = SentinelOutput();
            var output = expected;
            Require(!guardedModifier.TryApply(in input, ref output, out var reason),
                "singular/near-singular current parent unexpectedly succeeded");
            Require(reason == AlsP4ReasonCode.NonFiniteInput && guardedWriter.WriteCount == 0,
                $"current parent rejection was not zero-write/stable: {reason}");
            RequireOutputExact(output, expected, "current parent affine failure");
            before.RequireExact(skeleton, visualRoot, "current parent affine failure");
        }

        var restId = skeleton.FindBone("hand_l");
        Require(restId >= 0, "singular rest fixture bone is missing");
        var originalRest = skeleton.GetBoneRest(restId);
        foreach (var basis in new[]
        {
            Basis.FromScale(Vector3.Zero),
            Basis.FromScale(new Vector3(1f, 1e-7f, 1f)),
        })
        {
            basePose.Restore(skeleton, visualRoot);
            guardedWriter.Configure(int.MaxValue, persistent: false);
            var expected = SentinelOutput();
            var output = expected;
            try
            {
                skeleton.SetBoneRest(restId, new Transform3D(basis, originalRest.Origin));
                var before = PoseSnapshot.Capture(skeleton, visualRoot);
                Require(!guardedModifier.TryApply(in input, ref output, out var reason),
                    "singular/near-singular rest unexpectedly succeeded");
                Require(reason == AlsP4ReasonCode.InvalidRuntimeState && guardedWriter.WriteCount == 0,
                    $"rest rejection was not zero-write/stable: {reason}");
                RequireOutputExact(output, expected, "rest affine failure");
                before.RequireExact(skeleton, visualRoot, "rest affine failure");
            }
            finally
            {
                skeleton.SetBoneRest(restId, originalRest);
            }
        }
        basePose.Restore(skeleton, visualRoot);
    }

    private static void VerifySkeletonIdentityMutationsRejected(
        AlsComponentPoseModifier modifier,
        PoseSnapshot basePose,
        Skeleton3D skeleton,
        Node3D visualRoot,
        in AlsPoseModifierInput input)
    {
        var boneId = skeleton.FindBone("hand_l");
        Require(boneId >= 0, "identity mutation fixture bone is missing");
        var originalName = skeleton.GetBoneName(boneId);
        const string mutatedName = "hand_l_task9_mutated";
        basePose.Restore(skeleton, visualRoot);
        var beforeName = PoseSnapshot.Capture(skeleton, visualRoot);
        var expected = SentinelOutput();
        var output = expected;
        var nameValidationsBeforeMutation = modifier.NameValidationCount;
        try
        {
            skeleton.SetBoneName(boneId, mutatedName);
            Require(!modifier.TryApply(in input, ref output, out var reason),
                "same-count Skeleton name mutation unexpectedly succeeded");
            Require(reason == AlsP4ReasonCode.InvalidRuntimeState,
                $"name mutation returned unstable reason: {reason}");
            RequireOutputExact(output, expected, "name mutation");
            beforeName.RequireExact(skeleton, visualRoot, "name mutation");
            Require(modifier.NameValidationCount == nameValidationsBeforeMutation + 1,
                "name mutation did not trigger exactly one cold validation");
        }
        finally
        {
            skeleton.SetBoneName(boneId, originalName);
        }
        basePose.Restore(skeleton, visualRoot);
        output = default;
        Require(modifier.TryApply(in input, ref output, out var recoveredNameReason),
            $"modifier did not recover after name restoration: {recoveredNameReason}");
        Require(modifier.NameValidationCount == nameValidationsBeforeMutation + 2,
            "name restoration did not trigger exactly one recovery validation");

        basePose.Restore(skeleton, visualRoot);
        var originalRest = skeleton.GetBoneRest(boneId);
        var versionBeforeRestMutation = skeleton.GetVersion();
        var mutatedRest = originalRest;
        mutatedRest.Origin += new Vector3(0.000001f, 0f, 0f);
        var beforeRest = PoseSnapshot.Capture(skeleton, visualRoot);
        expected = SentinelOutput();
        output = expected;
        try
        {
            skeleton.SetBoneRest(boneId, mutatedRest);
            var restMutationChangedVersion = skeleton.GetVersion() != versionBeforeRestMutation;
            Require(!modifier.TryApply(in input, ref output, out var reason),
                "same-count Skeleton rest mutation unexpectedly succeeded");
            Require(reason == AlsP4ReasonCode.InvalidRuntimeState,
                $"rest mutation returned unstable reason: {reason}");
            RequireOutputExact(output, expected, "rest mutation");
            beforeRest.RequireExact(skeleton, visualRoot, "rest mutation");
            GD.Print(
                $"P4_POSE_REST_VERSION changed={(restMutationChangedVersion ? 1 : 0)}");
        }
        finally
        {
            skeleton.SetBoneRest(boneId, originalRest);
        }
        basePose.Restore(skeleton, visualRoot);
        output = default;
        Require(modifier.TryApply(in input, ref output, out var recoveredRestReason),
            $"modifier did not recover after rest restoration: {recoveredRestReason}");
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
            var rest = skeleton.GetBoneRest(current);
            var position = rest.Origin;
            var rotation = rest.Basis.Orthonormalized()
                .GetRotationQuaternion().Normalized();
            var scale = rest.Basis.Scale;
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
            component *= pose;
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
        var rest = skeleton.GetBoneRest(boneId);
        var position = rest.Origin;
        var rotation = rest.Basis.Orthonormalized()
            .GetRotationQuaternion().Normalized();
        var scale = rest.Basis.Scale;
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
        return new Transform3D(
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
            component *= pose;
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
            component *= pose;
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
        OperationTicks = 31337,
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
                actual.OperationTicks == expected.OperationTicks &&
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
        long AllocatedBytes,
        long ActiveAllocatedBytes);

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

        public Vector3 GetBonePosition(int boneId) => _positions[boneId];

        public Vector3 GetBoneScale(int boneId) => _scales[boneId];

        public Quaternion GetBoneRotation(int boneId) => _rotations[boneId];

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

        public int WriteCount => _writeCount;

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
