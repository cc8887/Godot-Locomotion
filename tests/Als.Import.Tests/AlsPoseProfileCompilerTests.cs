using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsPoseProfileCompilerTests
{
    [Fact]
    public void TwoParameterCompileRejectsAnIncompleteRuntimeProfile()
    {
        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsPoseProfileCompiler.Compile(
                ReadProfile(), P3RepositoryFixtures.LoadAnimationSet()));

        var issue = Assert.Single(exception.Issues);
        Assert.Equal("ALSPOSE051", issue.Code);
        Assert.Equal("$.locomotionProfile", issue.FieldPath);
        Assert.Contains("three-parameter", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryProfileCompilesExactAimTurnRotateMasksAndFeet()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(
            P3RepositoryFixtures.ReadProfile(), set);

        var profile = AlsPoseProfileCompiler.Compile(ReadProfile(), set, locomotion);

        Assert.Equal(1, profile.SchemaVersion);
        Assert.Equal(set.AssetIndex.GetSkeletonId("b5b52715012cad50bf7a625ddf01e4335bb4fcf0"), profile.SkeletonId);
        Assert.Equal(set.AssetIndex.GetAimOffsetId("b4bf2befd979de45f53f300dc0e60c702fc3a686"), profile.Aim.AimOffsetId);
        Assert.Collection(set.AimOffsets[profile.Aim.AimOffsetId].Parameters,
            parameter => Assert.Equal(new AlsBlendParameterDefinition("Pitch", -90f, 90f, 4), parameter),
            parameter => Assert.Equal(new AlsBlendParameterDefinition("None", 0f, 100f, 4), parameter),
            parameter => Assert.Equal(new AlsBlendParameterDefinition("None", 0f, 100f, 4), parameter));
        Assert.Equal(8, profile.Turns.Length);
        Assert.Equal(4, profile.Rotates.Length);
        Assert.All(profile.Turns, turn =>
        {
            Assert.Equal(1.2f, turn.BasePlayRate);
            Assert.Equal(0.2f, turn.BlendSeconds);
            Assert.Equal((byte)1, turn.ScaleAngle);
            var curve = set.Animations[turn.AnimationId].Curves.Single(value =>
                value.CanonicalKind == AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond);
            Assert.Equal(curve.CurveId, turn.CurveId);
        });
        Assert.Equal(8, profile.Turns.Select(value => (value.Stance, value.Direction, value.NominalDegrees)).Distinct().Count());
        Assert.Equal(4, profile.Rotates.Select(value => (value.Stance, value.Direction)).Distinct().Count());
        Assert.Equal(11, profile.Masks.Entries.Length);
        Assert.All(profile.Masks.Entries, entry => Assert.NotEmpty(entry.BoneIds));
        Assert.DoesNotContain(profile.Masks.Entries.SelectMany(value => value.BoneIds)
            .GroupBy(value => value), value => value.Count() > 1);
        Assert.True(profile.Feet.TraceUpMeters > 0f);
        Assert.True(profile.Feet.TraceDownMeters > 0f);
        Assert.Equal(0.08f, profile.Feet.PelvisUpHalfLifeSeconds);
        Assert.Equal(0.1f, profile.Feet.PelvisDownHalfLifeSeconds);
        Assert.Equal(1.2f, profile.Feet.MaximumLegReachMeters);
        Assert.Equal(90f * MathF.PI / 180f, profile.Feet.MaximumThighAngleRadians, 5);
        Assert.Equal(40f * MathF.PI / 180f, profile.Feet.MaximumFootAngleRadians, 5);
        Assert.Equal(1f, profile.Feet.PlatformTeleportDistanceMeters);
        Assert.Equal(45f * MathF.PI / 180f, profile.Feet.PlatformTeleportAngleRadians, 5);
        Assert.Equal(1e-4f, profile.Feet.LockWeightEpsilon);
        Assert.Equal(AlsCapsuleHalfHeightSource.CharacterController, profile.Feet.CapsuleHalfHeightSource);

        Assert.Equal(34, profile.FootCurves.Bindings.Length);
        Assert.Equal(34, profile.FootCurves.Bindings.Select(value => value.AnimationId).Distinct().Count());
        Assert.Equal(1f, profile.FootCurves.GroundedIkWeight);
        Assert.Equal(0f, profile.FootCurves.JumpStartIkWeight);
        Assert.Equal(0f, profile.FootCurves.FallLoopIkWeight);
        Assert.Equal(1f, profile.FootCurves.LandRecoveryIkWeight);
        Assert.All(profile.FootCurves.Bindings, binding =>
        {
            var animation = set.Animations[binding.AnimationId];
            if (binding.LeftLockCurveId >= 0)
            {
                Assert.Equal("FootLock_L", animation.Curves.Single(
                    value => value.CurveId == binding.LeftLockCurveId).SourceName);
            }
            else
            {
                Assert.Equal(0f, binding.LeftLockDefault);
            }
            if (binding.RightLockCurveId >= 0)
            {
                Assert.Equal("FootLock_R", animation.Curves.Single(
                    value => value.CurveId == binding.RightLockCurveId).SourceName);
            }
            else
            {
                Assert.Equal(0f, binding.RightLockDefault);
            }
        });
        Assert.Contains(profile.FootCurves.Bindings,
            value => value.LeftLockCurveId >= 0 || value.RightLockCurveId >= 0);
        Assert.Contains(profile.FootCurves.Bindings,
            value => value.LeftLockCurveId < 0 && value.RightLockCurveId < 0);
    }

    [Fact]
    public void FootCurveCompilerRejectsInventedCurveNamesAndUnknownStateDefaults()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(
            P3RepositoryFixtures.ReadProfile(), set);
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        root["feet"]!["curves"]!["leftLock"] = "Enable_FootIK";

        var exception = Assert.Throws<AlsCompilationException>(() =>
            AlsPoseProfileCompiler.Compile(root.ToJsonString(), set, locomotion));

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == "$.feet.curves.leftLock" &&
            issue.Message.Contains("reachable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeArraysAreDefensivelyCopied()
    {
        var profile = CompileRuntime(ReadProfile(), P3RepositoryFixtures.LoadAnimationSet());
        var turns = profile.Turns;
        var masks = profile.Masks.Entries;
        var bones = masks[0].BoneIds;

        turns[0] = default;
        masks[0] = masks[1];
        bones[0] = -100;

        Assert.NotEqual(default, profile.Turns[0]);
        Assert.NotEqual(profile.Masks.Entries[1], profile.Masks.Entries[0]);
        Assert.DoesNotContain(-100, profile.Masks.Entries[0].BoneIds);
    }

    [Theory]
    [InlineData("unknown", "$.unknown")]
    [InlineData("missingStableId", "$.aim.down")]
    [InlineData("missingBone", "$.masks[0].root")]
    [InlineData("nonCanonicalRoots", "$.masks[9].root")]
    [InlineData("asymmetricRoots", "$.masks")]
    [InlineData("duplicateMaskBone", "$.masks")]
    [InlineData("boundaryEscape", "$.masks[0].boundaries[0]")]
    public void InvalidProfileRelationshipsAreRejectedWithStablePaths(string mutation, string expectedPath)
    {
        var json = Mutate(root =>
        {
            switch (mutation)
            {
                case "unknown": root["unknown"] = true; break;
                case "missingStableId": root["aim"]!["down"] = new string('f', 40); break;
                case "missingBone": root["masks"]![0]!["root"] = "missing_bone"; break;
                case "nonCanonicalRoots":
                    root["masks"]![9]!["root"] = "ball_l";
                    root["masks"]![10]!["root"] = "ball_r";
                    root["feet"]!["leftFootRoot"] = "ball_l";
                    root["feet"]!["rightFootRoot"] = "ball_r";
                    break;
                case "asymmetricRoots": root["masks"]![10]!["root"] = "Foot_L"; break;
                case "duplicateMaskBone": root["masks"]![0]!["boundaries"] = new JsonArray("neck_01", "clavicle_r"); break;
                case "boundaryEscape": root["masks"]![0]!["boundaries"]![0] = "Foot_L"; break;
            }
        });

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(json, P3RepositoryFixtures.LoadAnimationSet()));

        Assert.Contains(exception.Issues, issue => issue.FieldPath.StartsWith(expectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateJsonPropertyIsRejected()
    {
        var json = ReadProfile().Replace(
            "\"schemaVersion\": 1,",
            "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,",
            StringComparison.Ordinal);

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(json, P3RepositoryFixtures.LoadAnimationSet()));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.schemaVersion" && issue.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void WrongSkeletonIsRejectedWithAssetStableId()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var downId = set.AssetIndex.GetAnimationId("c4bba06be47ce74153c2048d0de7b30a6465a84f");
        var animations = set.Animations.ToArray();
        animations[downId] = animations[downId] with { SkeletonId = 0 };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(ReadProfile(), set with { Animations = animations }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.aim.down" && issue.AssetId == animations[downId].StableId);
    }

    [Fact]
    public void IncompatibleAimAdditiveBaseIsRejected()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var downId = set.AssetIndex.GetAnimationId("c4bba06be47ce74153c2048d0de7b30a6465a84f");
        var animations = set.Animations.ToArray();
        animations[downId] = animations[downId] with { AdditiveBasePoseAnimationId = downId };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(ReadProfile(), set with { Animations = animations }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.aim.down" && issue.AssetId == animations[downId].StableId);
    }

    [Fact]
    public void MissingCanonicalCurveIsRejectedWithAnimationStableId()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var turnStableId = JsonNode.Parse(ReadProfile())!["turns"]![0]!["animation"]!.GetValue<string>();
        var animationId = set.AssetIndex.GetAnimationId(turnStableId);
        var animations = set.Animations.ToArray();
        animations[animationId] = animations[animationId] with { Curves = [] };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(ReadProfile(), set with { Animations = animations }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.turns[0].animation" && issue.AssetId == turnStableId);
    }

    [Fact]
    public void SwappedAimDownAndUpSlotsAreRejectedAtTheFirstWrongField()
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var down = root["aim"]!["down"]!.GetValue<string>();
        var up = root["aim"]!["up"]!.GetValue<string>();
        root["aim"]!["down"] = up;
        root["aim"]!["up"] = down;

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), P3RepositoryFixtures.LoadAnimationSet()));

        AssertObjectPathFailure(exception, "$.aim.down", AimDownPath, AimUpPath, up);
    }

    [Fact]
    public void DuplicateAimSweepIdIsRejectedExplicitly()
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var down = root["aim"]!["down"]!.GetValue<string>();
        root["aim"]!["up"] = down;

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), P3RepositoryFixtures.LoadAnimationSet()));

        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == "$.aim.up" && issue.AssetId == down && issue.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void AimOffsetObjectPathIsBoundToTheLookAsset()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var aimId = set.AssetIndex.GetAimOffsetId("b4bf2befd979de45f53f300dc0e60c702fc3a686");
        var aims = set.AimOffsets.ToArray();
        aims[aimId] = aims[aimId] with { ObjectPath = "/Game/Wrong/Aim.Aim" };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(ReadProfile(), set with { AimOffsets = aims }));

        AssertObjectPathFailure(exception, "$.aim.aimOffset", AimOffsetPath, "/Game/Wrong/Aim.Aim", aims[aimId].StableId);
    }

    [Theory]
    [InlineData("down", 0, AimDownPath)]
    [InlineData("forward", 1, AimForwardPath)]
    [InlineData("up", 2, AimUpPath)]
    public void EveryAimSweepRoleIsBoundToItsExactObjectPath(
        string role,
        int wrongRotateIndex,
        string expectedPath)
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var wrongId = root["rotates"]![wrongRotateIndex]!["animation"]!.GetValue<string>();
        root["aim"]![role] = wrongId;

        var set = P3RepositoryFixtures.LoadAnimationSet();
        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), set));

        var actualPath = set.Animations[set.AssetIndex.GetAnimationId(wrongId)].ObjectPath;
        AssertObjectPathFailure(exception, $"$.aim.{role}", expectedPath, actualPath, wrongId);
    }

    [Theory]
    [InlineData("down", 0, 1, AimDownPath)]
    [InlineData("forward", 1, 2, AimForwardPath)]
    [InlineData("up", 2, 0, AimUpPath)]
    public void AimOffsetSampleCoordinatesBindDownForwardAndUpAnimationIds(
        string role,
        int targetCoordinateIndex,
        int replacementCoordinateIndex,
        string expectedPath)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var aimId = set.AssetIndex.GetAimOffsetId("b4bf2befd979de45f53f300dc0e60c702fc3a686");
        var aims = set.AimOffsets.ToArray();
        var samples = aims[aimId].Samples.ToArray();
        var coordinates = new[] { new[] { -90f, 0f, 0f }, new[] { 0f, 0f, 0f }, new[] { 90f, 0f, 0f } };
        var targetIndex = Array.FindIndex(samples, value => value.SampleValue.SequenceEqual(coordinates[targetCoordinateIndex]));
        var replacementIndex = Array.FindIndex(samples, value => value.SampleValue.SequenceEqual(coordinates[replacementCoordinateIndex]));
        var actualAnimationId = samples[replacementIndex].AnimationId;
        samples[targetIndex] = samples[targetIndex] with { AnimationId = actualAnimationId };
        aims[aimId] = aims[aimId] with { Samples = samples };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(ReadProfile(), set with { AimOffsets = aims }));

        AssertSlotFailure(exception, $"$.aim.{role}", expectedPath, set.Animations[actualAnimationId].StableId);
        Assert.Contains(exception.Issues, issue => issue.Message.Contains("sample", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AimOffsetRejectsAFourthSampleAtItsExactSampleValuePath()
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var aimId = set.AssetIndex.GetAimOffsetId("b4bf2befd979de45f53f300dc0e60c702fc3a686");
        var aims = set.AimOffsets.ToArray();
        var extra = aims[aimId].Samples[0] with { SampleValue = [45f, 0f, 0f] };
        aims[aimId] = aims[aimId] with { Samples = [.. aims[aimId].Samples, extra] };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(ReadProfile(), set with { AimOffsets = aims }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.aim.aimOffset.samples[3].sampleValue");
    }

    [Theory]
    [InlineData(0, 7f)]
    [InlineData(1, float.NaN)]
    [InlineData(2, float.PositiveInfinity)]
    public void AimOffsetRejectsNonUnitOrNonFiniteRateScaleAtTheSampleIndex(int sampleIndex, float rateScale)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var aimId = set.AssetIndex.GetAimOffsetId("b4bf2befd979de45f53f300dc0e60c702fc3a686");
        var aims = set.AimOffsets.ToArray();
        var samples = aims[aimId].Samples.ToArray();
        samples[sampleIndex] = samples[sampleIndex] with { RateScale = rateScale };
        aims[aimId] = aims[aimId] with { Samples = samples };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(ReadProfile(), set with { AimOffsets = aims }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == $"$.aim.aimOffset.samples[{sampleIndex}].rateScale");
    }

    [Theory]
    [InlineData(0, "name")]
    [InlineData(0, "minimum")]
    [InlineData(0, "maximum")]
    [InlineData(0, "gridDivisions")]
    [InlineData(1, "name")]
    [InlineData(1, "minimum")]
    [InlineData(1, "maximum")]
    [InlineData(1, "gridDivisions")]
    [InlineData(2, "name")]
    [InlineData(2, "minimum")]
    [InlineData(2, "maximum")]
    [InlineData(2, "gridDivisions")]
    public void AimOffsetRejectsParameterAxisDriftAtTheExactField(int parameterIndex, string field)
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var aimId = set.AssetIndex.GetAimOffsetId("b4bf2befd979de45f53f300dc0e60c702fc3a686");
        var aims = set.AimOffsets.ToArray();
        var parameters = aims[aimId].Parameters.ToArray();
        var parameter = parameters[parameterIndex];
        parameters[parameterIndex] = field switch
        {
            "name" => parameter with { Name = "Wrong" },
            "minimum" => parameter with { Minimum = parameter.Minimum + 1f },
            "maximum" => parameter with { Maximum = parameter.Maximum - 1f },
            "gridDivisions" => parameter with { GridDivisions = parameter.GridDivisions + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        aims[aimId] = aims[aimId] with { Parameters = parameters };

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(ReadProfile(), set with { AimOffsets = aims }));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == $"$.aim.aimOffset.parameters[{parameterIndex}].{field}");
    }

    [Theory]
    [InlineData(0, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_L90.ALS_N_TurnIP_L90")]
    [InlineData(1, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_R90.ALS_N_TurnIP_R90")]
    [InlineData(2, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_L180.ALS_N_TurnIP_L180")]
    [InlineData(3, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_R180.ALS_N_TurnIP_R180")]
    [InlineData(4, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_L90.ALS_CLF_TurnIP_L90")]
    [InlineData(5, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_R90.ALS_CLF_TurnIP_R90")]
    [InlineData(6, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_L180.ALS_CLF_TurnIP_L180")]
    [InlineData(7, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_TurnIP_R180.ALS_CLF_TurnIP_R180")]
    public void EveryTurnRoleIsBoundToItsExactObjectPath(int index, string expectedPath)
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var turns = root["turns"]!.AsArray();
        var wrongId = root["rotates"]![index % 4]!["animation"]!.GetValue<string>();
        turns[index]!["animation"] = wrongId;

        var set = P3RepositoryFixtures.LoadAnimationSet();
        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), set));

        var actualPath = set.Animations[set.AssetIndex.GetAnimationId(wrongId)].ObjectPath;
        AssertObjectPathFailure(exception, $"$.turns[{index}].animation", expectedPath, actualPath, wrongId);
    }

    [Fact]
    public void SwappedStandingTurnLeftAndRightAreRejected()
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        SwapAnimationIds(root["turns"]!.AsArray(), 0, 1);
        var actualId = root["turns"]![0]!["animation"]!.GetValue<string>();

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), P3RepositoryFixtures.LoadAnimationSet()));

        AssertObjectPathFailure(exception, "$.turns[0].animation", TurnStandingLeft90Path,
            "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_R90.ALS_N_TurnIP_R90", actualId);
    }

    [Fact]
    public void DuplicateTurnAnimationIdIsRejectedExplicitly()
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var duplicateId = root["turns"]![0]!["animation"]!.GetValue<string>();
        root["turns"]![1]!["animation"] = duplicateId;

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), P3RepositoryFixtures.LoadAnimationSet()));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.turns[1].animation" &&
            issue.AssetId == duplicateId && issue.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_Rotate_L90.ALS_N_Rotate_L90")]
    [InlineData(1, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_Rotate_R90.ALS_N_Rotate_R90")]
    [InlineData(2, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_Rotate_L90.ALS_CLF_Rotate_L90")]
    [InlineData(3, "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_CLF_Rotate_R90.ALS_CLF_Rotate_R90")]
    public void EveryRotateRoleIsBoundToItsExactObjectPath(int index, string expectedPath)
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var wrongId = root["turns"]![index]!["animation"]!.GetValue<string>();
        root["rotates"]![index]!["animation"] = wrongId;

        var set = P3RepositoryFixtures.LoadAnimationSet();
        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), set));

        var actualPath = set.Animations[set.AssetIndex.GetAnimationId(wrongId)].ObjectPath;
        AssertObjectPathFailure(exception, $"$.rotates[{index}].animation", expectedPath, actualPath, wrongId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SwappedRotateDirectionAndStanceSlotsAreRejected(int secondIndex)
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        SwapAnimationIds(root["rotates"]!.AsArray(), 0, secondIndex);
        var wrongId = root["rotates"]![0]!["animation"]!.GetValue<string>();

        var set = P3RepositoryFixtures.LoadAnimationSet();
        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), set));
        var actualPath = set.Animations[set.AssetIndex.GetAnimationId(wrongId)].ObjectPath;
        AssertObjectPathFailure(exception, "$.rotates[0].animation",
            "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_Rotate_L90.ALS_N_Rotate_L90",
            actualPath, wrongId);
    }

    [Fact]
    public void DuplicateRotateAnimationIdIsRejectedExplicitly()
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        var duplicateId = root["rotates"]![0]!["animation"]!.GetValue<string>();
        root["rotates"]![1]!["animation"] = duplicateId;

        var exception = Assert.Throws<AlsCompilationException>(() =>
            CompileRuntime(root.ToJsonString(), P3RepositoryFixtures.LoadAnimationSet()));

        Assert.Contains(exception.Issues, issue => issue.FieldPath == "$.rotates[1].animation" &&
            issue.AssetId == duplicateId && issue.Message.Contains("Duplicate", StringComparison.Ordinal));
    }

    private static string ReadProfile() => File.ReadAllText(Path.Combine(
        RepositoryRoot.Find(), "assets", "config", "p4_pose_profile.json"));

    private static AlsPoseAnimationProfile CompileRuntime(
        string json,
        AlsAnimationSetDefinition set)
    {
        var locomotion = AlsLocomotionProfileCompiler.Compile(
            P3RepositoryFixtures.ReadProfile(), set);
        return AlsPoseProfileCompiler.Compile(json, set, locomotion);
    }

    private static string Mutate(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }

    private static void SwapAnimationIds(JsonArray values, int first, int second)
    {
        var firstId = values[first]!["animation"]!.GetValue<string>();
        var secondId = values[second]!["animation"]!.GetValue<string>();
        values[first]!["animation"] = secondId;
        values[second]!["animation"] = firstId;
    }

    private static void AssertSlotFailure(
        AlsCompilationException exception,
        string path,
        string expectedObjectPath,
        string actualStableId) =>
        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == path && issue.Expected == expectedObjectPath && issue.Actual == actualStableId);

    private static void AssertObjectPathFailure(
        AlsCompilationException exception,
        string path,
        string expectedObjectPath,
        string actualObjectPath,
        string assetId) =>
        Assert.Contains(exception.Issues, issue =>
            issue.FieldPath == path && issue.Expected == expectedObjectPath &&
            issue.Actual == actualObjectPath && issue.AssetId == assetId);

    private const string AimOffsetPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look.ALS_N_Look";
    private const string AimDownPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_D_Sweep.ALS_N_Look_D_Sweep";
    private const string AimForwardPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_F_Sweep.ALS_N_Look_F_Sweep";
    private const string AimUpPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_U_Sweep.ALS_N_Look_U_Sweep";
    private const string TurnStandingLeft90Path = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace/ALS_N_TurnIP_L90.ALS_N_TurnIP_L90";
}
