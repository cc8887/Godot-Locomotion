using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Pose;
using Json.Schema;

namespace GodotAls.Core.Tests;

public sealed class AlsPoseGoldenTests
{
    private static readonly string FixtureDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "P4");
    private static readonly Lazy<JsonSchema> PoseSchema = new(() =>
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return JsonSchema.FromText(File.ReadAllText(Path.Combine(repositoryRoot, "tools", "schemas", "als_pose_trace.schema.json")));
    });

    [Fact]
    public void Fixtures_cover_the_locked_twenty_five_case_matrix()
    {
        var traces = LoadAll();

        Assert.Equal(
            ["trace_p4_aim.json", "trace_p4_feet.json", "trace_p4_platform.json", "trace_p4_rotate.json", "trace_p4_turn.json"],
            traces.Select(item => Path.GetFileName(item.Path)).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(25, traces.Sum(item => item.Trace.Cases.Count));
        Assert.Equal(5, CountCategory(traces, "Aim"));
        Assert.Equal(8, CountCategory(traces, "Turn"));
        Assert.Equal(4, CountCategory(traces, "Rotate"));
        Assert.Equal(3, CountCategory(traces, "Feet"));
        Assert.Equal(5, CountCategory(traces, "Platform"));
    }

    [Fact]
    public void Fixtures_lock_each_case_descriptor_and_source_path()
    {
        var actual = Directory.GetFiles(FixtureDirectory, "*.json")
            .SelectMany(path =>
            {
                var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                return root["cases"]!.AsArray().Select(item =>
                {
                    var value = item!.AsObject();
                    var expected = value["portExpected"]!.AsObject();
                    var source = value["source"]!.AsObject();
                    return string.Join('|',
                        Path.GetFileName(path), value["caseId"], value["category"], value["stance"],
                        value["direction"], value["phase"], expected["turnNominalDegrees"],
                        expected["leftReleaseReason"], expected["rightReleaseReason"],
                        source["animationObjectPath"], source["curveSourceObjectPath"],
                        string.Join(',', source["curveNames"]!.AsArray().Select(value => value!.GetValue<string>())));
                });
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedDescriptors.Order(StringComparer.Ordinal), actual);
    }

    [Fact]
    public void Loader_rejects_each_schema_valid_descriptor_tuple_mutation()
    {
        var path = FixturePath("trace_p4_aim.json");
        var mutations = new Action<JsonObject>[]
        {
            root => FirstCase(root)["caseId"] = "aim_replacement",
            root => FirstCase(root)["category"] = "Turn",
            root => FirstCase(root)["stance"] = "Crouching",
            root => FirstCase(root)["direction"] = "Up",
            root => FirstCase(root)["phase"] = "playing",
            root => FirstExpected(root)["turnNominalDegrees"] = 90,
            root => FirstExpected(root)["leftReleaseReason"] = "RayMiss",
            root => FirstExpected(root)["rightReleaseReason"] = "RayMiss",
            root => FirstCase(root)["source"]!["animationObjectPath"] = "/ALS/ALS/Animations/View/BS_Als_Other.BS_Als_Other",
            root => FirstCase(root)["source"]!["curveSourceObjectPath"] = "/ALS/ALS/Animations/View/BS_Als_Other.BS_Als_Other",
            root => FirstCase(root)["source"]!["curveNames"] = new JsonArray("RotationYawSpeed"),
        };

        foreach (var apply in mutations)
        {
            using var mutation = Mutate(path, apply);
            AssertSchemaValid(mutation.Path);
            Assert.Throws<FormatException>(() => AlsPoseTrace.Load(mutation.Path));
        }
    }

    [Fact]
    public void Production_core_replays_every_port_oracle_without_issues()
    {
        foreach (var (_, trace) in LoadAll())
        {
            Assert.Empty(AlsPoseTrace.Compare(trace));
        }
    }

    [Fact]
    public void Cross_engine_state_ids_direction_phase_and_release_reasons_are_exact()
    {
        foreach (var file in new[]
                 {
                     "trace_p4_aim.json", "trace_p4_turn.json", "trace_p4_rotate.json",
                     "trace_p4_feet.json", "trace_p4_platform.json",
                 })
        foreach (var item in LoadJson(file)["cases"]!.AsArray())
        {
            var traceCase = item!.AsObject();
            var native = traceCase["nativeActual"]!.AsObject();
            var expected = traceCase["portExpected"]!.AsObject();
            var category = traceCase["category"]!.GetValue<string>();

            if (category == "Aim")
            {
                Assert.Equal(expected["aimRelativeYaw"]!.GetValue<float>(), native["viewYaw"]!.GetValue<float>());
                Assert.Equal(expected["aimRelativePitch"]!.GetValue<float>(), native["viewPitch"]!.GetValue<float>());
            }
            else if (category == "Turn")
            {
                Assert.Equal(expected["turnAnimationId"]!.ToJsonString(), native["turnAnimationId"]!.ToJsonString());
                Assert.Equal(expected["turnCurveId"]!.ToJsonString(), native["turnCurveId"]!.ToJsonString());
                Assert.Equal(expected["turnDirection"]!.ToJsonString(), native["turnDirection"]!.ToJsonString());
                Assert.Equal(expected["turnActive"]!.ToJsonString(), native["turnActive"]!.ToJsonString());
                Assert.Equal(expected["turnPhase"]!.GetValue<float>(), native["turnPhase"]!.GetValue<float>());
            }
            else if (category == "Rotate")
            {
                Assert.Equal(expected["rotateAnimationId"]!.ToJsonString(), native["rotateAnimationId"]!.ToJsonString());
                Assert.Equal(expected["rotateCurveId"]!.ToJsonString(), native["rotateCurveId"]!.ToJsonString());
                Assert.Equal(expected["rotateDirection"]!.ToJsonString(), native["rotateDirection"]!.ToJsonString());
                Assert.Equal(expected["rotateActive"]!.ToJsonString(), native["rotateActive"]!.ToJsonString());
                Assert.Equal(expected["rotatePhase"]!.GetValue<float>(), native["rotatePhase"]!.GetValue<float>());
            }

            else if (category is "Feet" or "Platform")
            {
                Assert.Equal(expected["leftFoot"]!["platformId"]!.ToJsonString(), native["leftFootPlatformId"]!.ToJsonString());
                Assert.Equal(expected["rightFoot"]!["platformId"]!.ToJsonString(), native["rightFootPlatformId"]!.ToJsonString());
                Assert.Equal(expected["leftReleaseReason"]!.ToJsonString(), native["leftFootReleaseReason"]!.ToJsonString());
                Assert.Equal(expected["rightReleaseReason"]!.ToJsonString(), native["rightFootReleaseReason"]!.ToJsonString());
            }
        }
    }

    [Fact]
    public void Comparer_rejects_every_schema_valid_native_common_field_mutation()
    {
        var mutations = new (string File, string CaseId, string ExpectedField, Action<JsonObject> Apply)[]
        {
            ("trace_p4_aim.json", "aim_center", "aimRelativeYaw.native", root => Native(root, 0)["viewYaw"] = 0.25f),
            ("trace_p4_aim.json", "aim_center", "aimRelativePitch.native", root => Native(root, 0)["viewPitch"] = 0.25f),
            ("trace_p4_turn.json", "turn_standing_left_90", "turnAnimationId.native", root => Native(root, 0)["turnAnimationId"] = 999),
            ("trace_p4_turn.json", "turn_standing_left_90", "turnCurveId.native", root => Native(root, 0)["turnCurveId"] = 999),
            ("trace_p4_turn.json", "turn_standing_left_90", "turnPhase.native", root => Native(root, 0)["turnPhase"] = 0.25f),
            ("trace_p4_turn.json", "turn_standing_left_90", "turnNominalDegrees.native", root => Native(root, 0)["turnNominalDegrees"] = 180),
            ("trace_p4_turn.json", "turn_standing_left_90", "turnDirection.native", root => Native(root, 0)["turnDirection"] = -1),
            ("trace_p4_turn.json", "turn_standing_left_90", "turnActive.native", root => Native(root, 0)["turnActive"] = 0),
            ("trace_p4_rotate.json", "rotate_standing_left", "rotateAnimationId.native", root => Native(root, 0)["rotateAnimationId"] = 999),
            ("trace_p4_rotate.json", "rotate_standing_left", "rotateCurveId.native", root => Native(root, 0)["rotateCurveId"] = 999),
            ("trace_p4_rotate.json", "rotate_standing_left", "rotatePhase.native", root => Native(root, 0)["rotatePhase"] = 0.25f),
            ("trace_p4_rotate.json", "rotate_standing_left", "rotateDirection.native", root => Native(root, 0)["rotateDirection"] = -1),
            ("trace_p4_rotate.json", "rotate_standing_left", "rotateActive.native", root => Native(root, 0)["rotateActive"] = 0),
            ("trace_p4_platform.json", "platform_translate", "leftFoot.platformId.native", root => Native(root, 0)["leftFootPlatformId"] = 2),
            ("trace_p4_platform.json", "platform_translate", "rightFoot.platformId.native", root => Native(root, 0)["rightFootPlatformId"] = 2),
            ("trace_p4_platform.json", "platform_translate", "leftReleaseReason.native", root =>
            {
                Native(root, 0)["leftFootReleaseReason"] = "WeightLost";
                Native(root, 0)["leftFootLockAmount"] = 0f;
            }),
            ("trace_p4_platform.json", "platform_translate", "rightReleaseReason.native", root =>
            {
                Native(root, 0)["rightFootReleaseReason"] = "WeightLost";
                Native(root, 0)["rightFootLockAmount"] = 0f;
            }),
        };

        foreach (var item in mutations)
        {
            using var mutation = Mutate(FixturePath(item.File), item.Apply);
            AssertSchemaValid(mutation.Path);
            Assert.Contains(
                AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)),
                issue => issue.CaseId == item.CaseId && issue.Field == item.ExpectedField);
        }
    }

    [Fact]
    public void Native_aim_and_rotate_cases_expose_the_actual_runtime_asset_player()
    {
        foreach (var file in new[] { "trace_p4_aim.json", "trace_p4_rotate.json" })
        foreach (var item in LoadJson(file)["cases"]!.AsArray())
        {
            var traceCase = item!.AsObject();
            var source = traceCase["source"]!.AsObject();
            var native = traceCase["nativeActual"]!.AsObject();
            Assert.Equal(source["animationObjectPath"]!.GetValue<string>(),
                native["runtimeSourceAnimationObjectPath"]!.GetValue<string>());
            Assert.InRange(native["runtimeSourceTimeSeconds"]!.GetValue<float>(), 0f, float.MaxValue);
            Assert.InRange(native["runtimePreviousSourceTimeSeconds"]!.GetValue<float>(), 0f, float.MaxValue);
            Assert.Equal(traceCase["stimulus"]!["previousYawCurve"]!.ToJsonString(),
                native["runtimePreviousYawCurve"]!.ToJsonString());
            Assert.Equal(traceCase["stimulus"]!["currentYawCurve"]!.ToJsonString(),
                native["runtimeCurrentYawCurve"]!.ToJsonString());
        }
    }

    [Fact]
    public void Native_runtime_player_owner_and_node_identity_are_case_locked()
    {
        var expected = new Dictionary<string, (string ClassPath, string NodeName, int Ordinal)>(StringComparer.Ordinal)
        {
            ["aim_center"] = ("/ALS/ALS/Character/AnimationInstances/AB_Als_Head.AB_Als_Head_C", "AnimGraphNode_BlendSpaceEvaluator", 5),
            ["aim_up"] = ("/ALS/ALS/Character/AnimationInstances/AB_Als_Head.AB_Als_Head_C", "AnimGraphNode_BlendSpaceEvaluator", 5),
            ["aim_down"] = ("/ALS/ALS/Character/AnimationInstances/AB_Als_Head.AB_Als_Head_C", "AnimGraphNode_BlendSpaceEvaluator", 5),
            ["aim_left"] = ("/ALS/ALS/Character/AnimationInstances/AB_Als_Head.AB_Als_Head_C", "AnimGraphNode_BlendSpaceEvaluator", 5),
            ["aim_right"] = ("/ALS/ALS/Character/AnimationInstances/AB_Als_Head.AB_Als_Head_C", "AnimGraphNode_BlendSpaceEvaluator", 5),
            ["rotate_standing_left"] = ("/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_Standing.AB_Als_Standing_C", "AnimGraphNode_SequencePlayer_18", 12),
            ["rotate_standing_right"] = ("/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_Standing.AB_Als_Standing_C", "AnimGraphNode_SequencePlayer_19", 9),
            ["rotate_crouching_left"] = ("/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_Crouching.AB_Als_Crouching_C", "AnimGraphNode_SequencePlayer_6", 20),
            ["rotate_crouching_right"] = ("/ALS/ALS/Character/AnimationInstances/Stances/AB_Als_Crouching.AB_Als_Crouching_C", "AnimGraphNode_SequencePlayer_7", 17),
        };

        foreach (var file in new[] { "trace_p4_aim.json", "trace_p4_rotate.json" })
        foreach (var item in LoadJson(file)["cases"]!.AsArray())
        {
            var caseId = item!["caseId"]!.GetValue<string>();
            var native = item["nativeActual"]!;
            var locked = expected[caseId];
            Assert.Equal(locked.ClassPath, native["runtimeInstanceClassPath"]!.GetValue<string>());
            Assert.Equal(locked.NodeName, native["runtimeNodePropertyName"]!.GetValue<string>());
            Assert.Equal(locked.Ordinal, native["runtimeNodePropertyOrdinal"]!.GetValue<int>());
        }
    }

    [Fact]
    public void Native_turn_rotate_ids_are_resolved_from_the_observed_runtime_source()
    {
        var expectedIds = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["A_Als_Turn_90_Left"] = 101, ["A_Als_Turn_90_Right"] = 100,
            ["A_Als_Turn_180_Left"] = 103, ["A_Als_Turn_180_Right"] = 102,
            ["A_Als_Crouch_Turn_90_Left"] = 105, ["A_Als_Crouch_Turn_90_Right"] = 104,
            ["A_Als_Crouch_Turn_180_Left"] = 107, ["A_Als_Crouch_Turn_180_Right"] = 106,
            ["A_Als_Rotate_90_Left"] = 109, ["A_Als_Rotate_90_Right"] = 108,
            ["A_Als_Crouch_Rotate_90_Left"] = 111, ["A_Als_Crouch_Rotate_90_Right"] = 110,
        };
        foreach (var file in new[] { "trace_p4_turn.json", "trace_p4_rotate.json" })
        foreach (var item in LoadJson(file)["cases"]!.AsArray())
        {
            var native = item!["nativeActual"]!;
            var sourcePath = native["runtimeSourceAnimationObjectPath"]!.GetValue<string>();
            var assetName = sourcePath[(sourcePath.LastIndexOf('/') + 1)..sourcePath.LastIndexOf('.')];
            var animationId = item["category"]!.GetValue<string>() == "Turn"
                ? native["turnAnimationId"]!.GetValue<int>()
                : native["rotateAnimationId"]!.GetValue<int>();
            Assert.Equal(expectedIds[assetName], animationId);
        }
    }

    [Fact]
    public void Platform_transitions_capture_adjacent_native_frames()
    {
        var platform = LoadJson("trace_p4_platform.json");
        foreach (var caseId in new[] { "platform_base_change", "platform_teleport", "platform_release" })
        {
            var traceCase = Case(platform, caseId);
            Assert.Equal(1, traceCase["stimulus"]!["transitionFrameDelta"]!.GetValue<int>());
            Assert.Equal(1, traceCase["nativeActual"]!["transitionFrameDelta"]!.GetValue<int>());
        }
    }

    [Fact]
    public void Native_observations_are_evidence_not_core_expectations()
    {
        foreach (var path in Directory.GetFiles(FixtureDirectory, "*.json"))
        {
            var baseline = AlsPoseTrace.Compare(AlsPoseTrace.Load(path));
            var isAim = Path.GetFileName(path).Equals("trace_p4_aim.json", StringComparison.Ordinal);
            using var mutation = Mutate(path, root =>
            {
                var native = FirstCase(root)["nativeActual"]!.AsObject();
                if (!isAim)
                {
                    native["viewYaw"] = 2.75;
                    native["viewPitch"] = -1.25;
                }
                native["spineYaw"] = 0.375;
                native["headBlendAmount"] = 0.625;
                native["turnUpdated"] = 0;
                native["rotateLeft"] = 1;
                native["rotateRight"] = 0;
                native["playRate"] = 1.75;
                native["turnAccumulatedYaw"] = 0.125;
                native["rotateAccumulatedYaw"] = -0.25;
                native["sourceSelectionVerified"] = 1;
                native["rotationYawSpeed"] = 12.5;
                native["feetValid"] = 1;
                native["movementBaseChanged"] = 0;
                native["hasRelativeBaseLocation"] = 0;
                native["hasRelativeBaseRotation"] = 0;
                native["movementBaseDeltaYaw"] = 0.125;
                native["previousLeftFootLockAmount"] = 0.75;
                native["previousRightFootLockAmount"] = 0.5;
                native["leftFootLockAmount"] = 0.625;
                native["rightFootLockAmount"] = 0.25;
                SetTransform(native["pelvis"]!.AsObject(), new Vector3(2f, 3f, 4f), Quaternion.Identity);
                SetTransform(native["leftFoot"]!.AsObject(), new Vector3(5f, 6f, 7f), Quaternion.Identity);
                SetTransform(native["rightFoot"]!.AsObject(), new Vector3(8f, 9f, 10f), Quaternion.Identity);
                SetTransform(native["pelvisComponent"]!.AsObject(), new Vector3(11f, 12f, 13f), Quaternion.Identity);
                SetTransform(native["leftFootComponent"]!.AsObject(), new Vector3(14f, 15f, 16f), Quaternion.Identity);
                SetTransform(native["rightFootComponent"]!.AsObject(), new Vector3(17f, 18f, 19f), Quaternion.Identity);
                SetTransform(native["head"]!.AsObject(), new Vector3(20f, 21f, 22f), Quaternion.Identity);
                SetTransform(native["spine"]!.AsObject(), new Vector3(23f, 24f, 25f), Quaternion.Identity);
                SetTransform(native["upperBody"]!.AsObject(), new Vector3(26f, 27f, 28f), Quaternion.Identity);
                SetTransform(native["headComponent"]!.AsObject(), new Vector3(29f, 30f, 31f), Quaternion.Identity);
                SetTransform(native["spineComponent"]!.AsObject(), new Vector3(32f, 33f, 34f), Quaternion.Identity);
                SetTransform(native["upperBodyComponent"]!.AsObject(), new Vector3(35f, 36f, 37f), Quaternion.Identity);
            });

            Assert.Equal(baseline, AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)));
        }
    }

    [Fact]
    public void Every_independent_native_scalar_vector_and_transform_is_isolated_from_compare()
    {
        var path = FixturePath("trace_p4_aim.json");
        var floatFields = new[]
        {
            "spineYaw", "headBlendAmount", "playRate",
            "turnAccumulatedYaw", "rotateAccumulatedYaw", "rotationYawSpeed", "movementBaseDeltaYaw",
            "previousLeftFootLockAmount", "previousRightFootLockAmount", "leftFootLockAmount", "rightFootLockAmount",
            "runtimeSourceWeight",
        };
        foreach (var field in floatFields)
        {
            using var mutation = Mutate(path, root => FirstCase(root)["nativeActual"]![field] =
                field.EndsWith("Phase", StringComparison.Ordinal) || field.EndsWith("Amount", StringComparison.Ordinal)
                    ? 0.375f
                    : 0.375f + FirstCase(root)["nativeActual"]![field]!.GetValue<float>());
            AssertSchemaValid(mutation.Path);
            Assert.Empty(AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)));
        }

        var integerFields = new[]
        {
            "turnUpdated", "rotateLeft", "rotateRight", "feetValid",
            "movementBaseChanged", "hasRelativeBaseLocation", "hasRelativeBaseRotation",
        };
        foreach (var field in integerFields)
        {
            using var mutation = Mutate(path, root =>
            {
                var native = FirstCase(root)["nativeActual"]!.AsObject();
                var current = native[field]!.GetValue<int>();
                native[field] = current == 0 ? 1 : 0;
            });
            AssertSchemaValid(mutation.Path);
            Assert.Empty(AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)));
        }

        foreach (var field in new[] { "runtimeInstanceClassPath", "runtimeNodePropertyName" })
        {
            using var mutation = Mutate(path, root => FirstCase(root)["nativeActual"]![field] = "changed_runtime_identity");
            AssertSchemaValid(mutation.Path);
            Assert.Empty(AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)));
        }
        using (var mutation = Mutate(path, root => FirstCase(root)["nativeActual"]!["runtimeNodePropertyOrdinal"] = 999))
        {
            AssertSchemaValid(mutation.Path);
            Assert.Empty(AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)));
        }

        foreach (var field in new[] { "leftLockWorldPosition", "rightLockWorldPosition", "leftLockBasePosition", "rightLockBasePosition" })
        {
            using var mutation = Mutate(path, root =>
            {
                var value = FirstCase(root)["nativeActual"]![field]!.AsObject();
                value["x"] = value["x"]!.GetValue<float>() + 0.125f;
            });
            AssertSchemaValid(mutation.Path);
            Assert.Empty(AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)));
        }

        foreach (var field in new[]
                 {
                     "pelvis", "leftFoot", "rightFoot", "pelvisComponent", "leftFootComponent", "rightFootComponent",
                     "head", "spine", "upperBody", "headComponent", "spineComponent", "upperBodyComponent",
                 })
        {
            using var mutation = Mutate(path, root =>
            {
                var transform = FirstCase(root)["nativeActual"]![field]!.AsObject();
                var position = transform["position"]!.AsObject();
                position["x"] = position["x"]!.GetValue<float>() + 0.125f;
            });
            AssertSchemaValid(mutation.Path);
            Assert.Empty(AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)));
        }
    }

    [Fact]
    public void Every_native_same_frame_field_rejects_independent_mutation()
    {
        var platformPath = FixturePath("trace_p4_platform.json");
        var platformMutations = new Action<JsonObject>[]
        {
            root => FirstCase(root)["nativeActual"]!["movementBaseId"] = 2,
            root => FirstCase(root)["nativeActual"]!["movementBaseColliderId"] = 20,
            root => OffsetX(FirstCase(root)["nativeActual"]!["movementBasePosition"]!.AsObject()),
            root => RotateQuaternion(FirstCase(root)["nativeActual"]!["movementBaseRotation"]!.AsObject()),
            root => FirstCase(root)["nativeActual"]!["previousMovementBaseId"] = 2,
            root => FirstCase(root)["nativeActual"]!["previousMovementBaseColliderId"] = 20,
            root => OffsetX(FirstCase(root)["nativeActual"]!["previousMovementBasePosition"]!.AsObject()),
            root => RotateQuaternion(FirstCase(root)["nativeActual"]!["previousMovementBaseRotation"]!.AsObject()),
            root => FirstCase(root)["nativeActual"]!["actualFloorNormal"] = VectorObject(Vector3.UnitX),
            root => FirstCase(root)["nativeActual"]!["actualLeftFootHit"]!["colliderId"] = 20,
            root => FirstCase(root)["nativeActual"]!["actualRightFootHit"]!["colliderId"] = 20,
            root => OffsetX(FirstCase(root)["nativeActual"]!["previousLeftLockBasePosition"]!.AsObject()),
            root => OffsetX(FirstCase(root)["nativeActual"]!["previousRightLockBasePosition"]!.AsObject()),
        };
        foreach (var apply in platformMutations)
        {
            using var mutation = Mutate(platformPath, apply);
            AssertSchemaValid(mutation.Path);
            Assert.Contains("same captured frame", Assert.Throws<FormatException>(() => AlsPoseTrace.Load(mutation.Path)).Message);
        }

        var feetPath = FixturePath("trace_p4_feet.json");
        foreach (var field in new[] { "previousLeftLockWorldPosition", "previousRightLockWorldPosition" })
        {
            using var mutation = Mutate(feetPath, root => OffsetX(FirstCase(root)["nativeActual"]![field]!.AsObject()));
            AssertSchemaValid(mutation.Path);
            Assert.Contains("same captured frame", Assert.Throws<FormatException>(() => AlsPoseTrace.Load(mutation.Path)).Message);
        }
    }

    [Fact]
    public void Native_observations_are_non_degenerate_for_every_case_family()
    {
        var aim = LoadJson("trace_p4_aim.json");
        Assert.True(Case(aim, "aim_up")["nativeActual"]!["viewPitch"]!.GetValue<float>() > 0.25f);
        Assert.True(Case(aim, "aim_down")["nativeActual"]!["viewPitch"]!.GetValue<float>() < -0.25f);
        Assert.True(Case(aim, "aim_left")["nativeActual"]!["viewYaw"]!.GetValue<float>() > 0.25f);
        Assert.True(Case(aim, "aim_right")["nativeActual"]!["viewYaw"]!.GetValue<float>() < -0.25f);
        var aimCenter = Case(aim, "aim_center")["nativeActual"]!;
        foreach (var direction in new[] { "aim_up", "aim_down", "aim_left", "aim_right" })
        foreach (var bone in new[] { "headComponent", "spineComponent", "upperBodyComponent" })
        {
            var aimed = Case(aim, direction)["nativeActual"]!;
            Assert.True(QuaternionAngle(
                aimCenter[bone]!["rotation"]!.AsObject(),
                aimed[bone]!["rotation"]!.AsObject()) > 0.02f,
                $"{direction}.{bone} did not move from the native center pose.");
        }

        var rotate = LoadJson("trace_p4_rotate.json");
        Assert.Equal(1, Case(rotate, "rotate_standing_left")["nativeActual"]!["rotateLeft"]!.GetValue<int>());
        Assert.Equal(1, Case(rotate, "rotate_standing_right")["nativeActual"]!["rotateRight"]!.GetValue<int>());

        var turn = LoadJson("trace_p4_turn.json");
        Assert.Contains(turn["cases"]!.AsArray(), item =>
            MathF.Abs(item!["nativeActual"]!["playRate"]!.GetValue<float>() - 1f) > 1e-4f);

        foreach (var file in new[] { "trace_p4_aim.json", "trace_p4_feet.json", "trace_p4_platform.json", "trace_p4_rotate.json", "trace_p4_turn.json" })
        {
            var first = FirstCase(LoadJson(file));
            Assert.NotEqual(
                first["nativeActual"]!["pelvis"]!["position"]!.ToJsonString(),
                first["portExpected"]!["pelvisOffset"]!.ToJsonString());
        }
    }

    [Fact]
    public void Native_observations_lock_real_turn_feet_and_platform_evidence()
    {
        foreach (var (_, trace) in LoadAll())
        {
            Assert.Equal(
                "/ALS/ALS/Character/AnimationInstances/Overlays/AB_Als_Rifle.AB_Als_Rifle_C",
                LoadJson($"trace_p4_{trace.Name}.json")["sources"]!["aimOverlayAnimationBlueprint"]!.GetValue<string>());
        }

        var turn = LoadJson("trace_p4_turn.json");
        foreach (var item in turn["cases"]!.AsArray())
        {
            var native = item!["nativeActual"]!;
            Assert.Equal(1, native["turnActive"]!.GetValue<int>());
            Assert.InRange(native["turnPhase"]!.GetValue<float>(), 0.05f, 0.8f);
            Assert.True(MathF.Abs(native["rotationYawSpeed"]!.GetValue<float>()) > 1e-4f);
        }

        var rotate = LoadJson("trace_p4_rotate.json");
        foreach (var item in rotate["cases"]!.AsArray())
        {
            var native = item!["nativeActual"]!;
            var speed = native["rotationYawSpeed"]!.GetValue<float>();
            var accumulatedYaw = native["rotateAccumulatedYaw"]!.GetValue<float>();
            Assert.True(MathF.Abs(speed) > 1e-4f);
            Assert.InRange(native["rotatePhase"]!.GetValue<float>(), 0.001f, 0.999f);
            Assert.True(MathF.Abs(accumulatedYaw) > 1e-6f);
            Assert.Equal(-MathF.Sign(speed), MathF.Sign(accumulatedYaw));
        }

        var feet = LoadJson("trace_p4_feet.json");
        var flat = Case(feet, "feet_flat")["nativeActual"]!;
        var slope = Case(feet, "feet_slope")["nativeActual"]!;
        var stairs = Case(feet, "feet_stairs")["nativeActual"]!;
        Assert.True(QuaternionAngle(
            flat["leftFootComponent"]!["rotation"]!.AsObject(),
            slope["leftFootComponent"]!["rotation"]!.AsObject()) > 0.05f);
        Assert.True(MathF.Abs(
            stairs["leftFootComponent"]!["position"]!["y"]!.GetValue<float>() -
            stairs["rightFootComponent"]!["position"]!["y"]!.GetValue<float>()) >= 0.05f);
        Assert.True(MathF.Abs(
            flat["pelvisComponent"]!["position"]!["y"]!.GetValue<float>() -
            stairs["pelvisComponent"]!["position"]!["y"]!.GetValue<float>()) >= 0.01f);

        var platform = LoadJson("trace_p4_platform.json");
        var translated = Case(platform, "platform_translate")["nativeActual"]!;
        Assert.Equal(1, translated["hasRelativeBaseLocation"]!.GetValue<int>());
        Assert.True(Vector3.Distance(
            ReadVector(translated["previousLeftLockBasePosition"]!.AsObject()),
            ReadVector(translated["leftLockBasePosition"]!.AsObject())) < 0.01f);
        var lockWorldDelta = Vector3.Distance(
            ReadVector(translated["previousLeftLockWorldPosition"]!.AsObject()),
            ReadVector(translated["leftLockWorldPosition"]!.AsObject()));
        Assert.InRange(lockWorldDelta, 0.009f, 0.011f);

        var rotated = Case(platform, "platform_rotate")["nativeActual"]!;
        Assert.Equal(1, rotated["hasRelativeBaseLocation"]!.GetValue<int>());
        Assert.Equal(0, rotated["hasRelativeBaseRotation"]!.GetValue<int>());
        Assert.True(rotated["movementBaseDeltaYaw"]!.GetValue<float>() > 0f);
        Assert.True(Vector3.Distance(
            ReadVector(rotated["previousLeftLockBasePosition"]!.AsObject()),
            ReadVector(rotated["leftLockBasePosition"]!.AsObject())) < 0.01f);
        var rotatedWorldDelta = Vector3.Distance(
            ReadVector(rotated["previousLeftLockWorldPosition"]!.AsObject()),
            ReadVector(rotated["leftLockWorldPosition"]!.AsObject()));
        Assert.InRange(rotatedWorldDelta, 0.002f, 0.004f);
        var baseChanged = Case(platform, "platform_base_change")["nativeActual"]!;
        Assert.NotEqual(baseChanged["previousMovementBaseId"]!.GetValue<int>(),
            baseChanged["movementBaseId"]!.GetValue<int>());
        Assert.NotEqual(baseChanged["previousMovementBaseColliderId"]!.GetValue<long>(),
            baseChanged["movementBaseColliderId"]!.GetValue<long>());
        Assert.Equal(1, baseChanged["movementBaseChanged"]!.GetValue<int>());
        var released = Case(platform, "platform_release")["nativeActual"]!;
        Assert.True(released["previousMovementBaseId"]!.GetValue<int>() >= 0);
        Assert.Equal(-1, released["movementBaseId"]!.GetValue<int>());
        Assert.True(released["leftFootLockAmount"]!.GetValue<float>() > 0f);
        Assert.True(released["rightFootLockAmount"]!.GetValue<float>() > 0f);
        Assert.Equal("PlatformRemoved", released["leftFootReleaseReason"]!.GetValue<string>());
    }

    [Fact]
    public void Turn_and_rotate_curves_are_adjacent_samples_from_the_runtime_selected_source()
    {
        foreach (var file in new[] { "trace_p4_turn.json", "trace_p4_rotate.json" })
        foreach (var item in LoadJson(file)["cases"]!.AsArray())
        {
            var traceCase = item!.AsObject();
            var source = traceCase["source"]!.AsObject();
            var stimulus = traceCase["stimulus"]!.AsObject();
            var native = traceCase["nativeActual"]!.AsObject();
            var previous = stimulus["previousYawCurve"]!.GetValue<float>();
            var current = stimulus["currentYawCurve"]!.GetValue<float>();
            var runtime = native["rotationYawSpeed"]!.GetValue<float>();

            Assert.Equal(source["animationObjectPath"]!.GetValue<string>(),
                source["curveSourceObjectPath"]!.GetValue<string>());
            Assert.Equal(1, native["sourceSelectionVerified"]!.GetValue<int>());
            Assert.True(MathF.Abs(current - previous) > 1e-5f);
            Assert.Equal(MathF.Sign(runtime), MathF.Sign(current));
            Assert.True(file.Contains("turn", StringComparison.Ordinal)
                ? native["turnPhase"]!.GetValue<float>() > 0f
                : native["rotatePhase"]!.GetValue<float>() > 0f);
        }
    }

    [Fact]
    public void Every_discrete_port_oracle_field_is_compared_exactly()
    {
        var mutations = new (string File, string Field, Action<JsonObject> Apply)[]
        {
            ("trace_p4_turn.json", "turnAnimationId", root => FirstExpected(root)["turnAnimationId"] = FirstExpected(root)["turnAnimationId"]!.GetValue<int>() + 1),
            ("trace_p4_turn.json", "turnCurveId", root => FirstExpected(root)["turnCurveId"] = FirstExpected(root)["turnCurveId"]!.GetValue<int>() + 1),
            ("trace_p4_turn.json", "turnNominalDegrees", root => FirstExpected(root)["turnNominalDegrees"] = 180),
            ("trace_p4_turn.json", "turnDirection", root => FirstExpected(root)["turnDirection"] = -1),
            ("trace_p4_turn.json", "turnActive", root => FirstExpected(root)["turnActive"] = 0),
            ("trace_p4_rotate.json", "rotateAnimationId", root => FirstExpected(root)["rotateAnimationId"] = FirstExpected(root)["rotateAnimationId"]!.GetValue<int>() + 1),
            ("trace_p4_rotate.json", "rotateCurveId", root => FirstExpected(root)["rotateCurveId"] = FirstExpected(root)["rotateCurveId"]!.GetValue<int>() + 1),
            ("trace_p4_rotate.json", "rotateDirection", root => FirstExpected(root)["rotateDirection"] = -1),
            ("trace_p4_rotate.json", "rotateActive", root => FirstExpected(root)["rotateActive"] = 0),
            ("trace_p4_feet.json", "leftFoot.platformId", root => FirstExpected(root)["leftFoot"]!["platformId"] = 0),
            ("trace_p4_feet.json", "rightFoot.platformId", root => FirstExpected(root)["rightFoot"]!["platformId"] = 0),
            ("trace_p4_platform.json", "leftReleaseReason", root => Case(root, "platform_base_change")["portExpected"]!["leftReleaseReason"] = "Teleported"),
            ("trace_p4_platform.json", "rightReleaseReason", root => Case(root, "platform_base_change")["portExpected"]!["rightReleaseReason"] = "Teleported"),
        };

        foreach (var mutationCase in mutations)
        {
            using var mutation = Mutate(FixturePath(mutationCase.File), mutationCase.Apply);
            var issues = AlsPoseTrace.Compare(LoadWithoutDescriptorLock(mutation.Path));
            Assert.True(issues.Any(issue => issue.Field == mutationCase.Field),
                $"Mutation was not observed for {mutationCase.Field}.");
        }
    }

    [Fact]
    public void Transform_comparison_uses_only_the_declared_position_tolerance()
    {
        var path = FixturePath("trace_p4_feet.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var tolerance = root["tolerances"]!["positionMeters"]!.GetValue<float>();

        foreach (var field in new[] { "pelvisOffset", "leftFoot", "rightFoot" })
        {
            JsonObject Position(JsonObject document) => field == "pelvisOffset"
                ? FirstExpected(document)[field]!.AsObject()
                : FirstExpected(document)[field]!["position"]!.AsObject();
            var issueField = field == "pelvisOffset" ? field : $"{field}.position";
            using var inside = Mutate(path, document =>
            {
                var position = Position(document);
                position["x"] = position["x"]!.GetValue<float>() + (tolerance * 0.5f);
            });
            using var outside = Mutate(path, document =>
            {
                var position = Position(document);
                position["x"] = position["x"]!.GetValue<float>() + (tolerance * 2f);
            });

            Assert.DoesNotContain(AlsPoseTrace.Compare(AlsPoseTrace.Load(inside.Path)), issue => issue.Field == issueField);
            Assert.Contains(AlsPoseTrace.Compare(AlsPoseTrace.Load(outside.Path)), issue => issue.Field == issueField);
        }
    }

    [Fact]
    public void Rotation_comparison_uses_quaternion_angular_distance_and_declared_tolerance()
    {
        var path = FixturePath("trace_p4_feet.json");
        var root = LoadJson("trace_p4_feet.json");
        var tolerance = root["tolerances"]!["rotationRadians"]!.GetValue<float>();

        foreach (var side in new[] { "leftFoot", "rightFoot" })
        {
            using var inside = Mutate(path, document => RotateExpectedFoot(document, side, tolerance * 0.5f));
            using var outside = Mutate(path, document => RotateExpectedFoot(document, side, tolerance * 2f));

            Assert.DoesNotContain(AlsPoseTrace.Compare(AlsPoseTrace.Load(inside.Path)), issue => issue.Field == $"{side}.rotation");
            Assert.Contains(AlsPoseTrace.Compare(AlsPoseTrace.Load(outside.Path)), issue => issue.Field == $"{side}.rotation");
        }
    }

    [Fact]
    public void Loader_rejects_non_unit_and_non_canonical_quaternions()
    {
        var path = FixturePath("trace_p4_feet.json");
        using var nonUnit = Mutate(path, root =>
        {
            var rotation = FirstCase(root)["stimulus"]!["platformRotation"]!.AsObject();
            rotation["w"] = 2f;
        });
        using var nonCanonical = Mutate(path, root =>
        {
            var rotation = FirstCase(root)["stimulus"]!["platformRotation"]!.AsObject();
            rotation["x"] = 0f;
            rotation["y"] = 0f;
            rotation["z"] = 0f;
            rotation["w"] = -1f;
        });

        Assert.Contains("unit quaternion", Assert.Throws<FormatException>(() => AlsPoseTrace.Load(nonUnit.Path)).Message);
        Assert.Contains("canonical quaternion sign", Assert.Throws<FormatException>(() => AlsPoseTrace.Load(nonCanonical.Path)).Message);
    }

    [Fact]
    public void Every_non_transform_oracle_number_is_compared_exactly()
    {
        var fields = new[]
        {
            "aimRelativeYaw", "aimRelativePitch", "headWeight", "spineWeight", "upperBodyWeight", "spineResidualYaw",
            "turnPhase", "turnPlayRate", "turnYawDelta", "rotatePhase", "rotatePlayRate", "rotateYawDelta",
        };
        foreach (var field in fields)
        {
            var path = FixturePath(field.StartsWith("turn", StringComparison.Ordinal)
                ? "trace_p4_turn.json"
                : field.StartsWith("rotate", StringComparison.Ordinal)
                    ? "trace_p4_rotate.json"
                    : "trace_p4_aim.json");
            using var mutation = Mutate(path, root =>
            {
                var expected = FirstCase(root)["portExpected"]!.AsObject();
                var value = expected[field]!.GetValue<float>();
                expected[field] = value == 1f ? MathF.BitDecrement(value) : MathF.BitIncrement(value);
            });
            Assert.Contains(AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)), issue => issue.Field == field);
        }

        foreach (var side in new[] { "leftFoot", "rightFoot" })
        {
            var path = FixturePath("trace_p4_platform.json");
            using var mutation = Mutate(path, root =>
            {
                var foot = FirstCase(root)["portExpected"]![side]!.AsObject();
                var value = foot["lockAmount"]!.GetValue<float>();
                foot["lockAmount"] = MathF.BitDecrement(value);
            });
            Assert.Contains(AlsPoseTrace.Compare(AlsPoseTrace.Load(mutation.Path)), issue => issue.Field == $"{side}.lockAmount");
        }
    }

    [Fact]
    public void Fixture_units_and_axes_keep_meter_scale_and_godot_forward()
    {
        var feet = LoadJson("trace_p4_feet.json");
        var flat = Case(feet, "feet_flat")["stimulus"]!.AsObject();
        var characterPosition = ReadVector(flat["characterPosition"]!.AsObject());
        var leftFootOrigin = ReadVector(flat["leftFootOrigin"]!.AsObject());
        Assert.InRange(characterPosition.Y, 0.015f, 0.025f);
        Assert.InRange(MathF.Abs(flat["leftFootOrigin"]!["x"]!.GetValue<float>()), 0.15f, 0.25f);
        Assert.InRange(leftFootOrigin.Y - characterPosition.Y, 0.1f, 0.15f);
        Assert.Equal(100L, flat["leftFootHit"]!["colliderId"]!.GetValue<long>());

        var platform = LoadJson("trace_p4_platform.json");
        var teleport = Case(platform, "platform_teleport")["stimulus"]!.AsObject();
        var current = ReadVector(teleport["platformPosition"]!.AsObject());
        var previous = ReadVector(teleport["previousPlatformPosition"]!.AsObject());
        Assert.True(Vector3.Distance(current, previous) > 1f);

    }

    [Fact]
    public void Replay_velocity_and_acceleration_use_negative_godot_z_forward()
    {
        var path = FixturePath("trace_p4_platform.json");
        using var mutation = Mutate(path, root =>
        {
            var stimulus = FirstCase(root)["stimulus"]!.AsObject();
            stimulus["speed"] = 2.5f;
            stimulus["acceleration"] = 7.5f;
        });
        var trace = AlsPoseTrace.Load(mutation.Path);
        var createInput = typeof(AlsPoseTrace).GetMethod(
            "CreateInput",
            BindingFlags.Static | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("AlsPoseTrace.CreateInput is unavailable.");

        var input = (AlsFrameInput)createInput.Invoke(null, [trace.Cases[0].Stimulus])!;

        Assert.Equal(new Vector3(0f, 0f, -2.5f), input.ActualVelocity);
        Assert.Equal(new Vector3(0f, 0f, -7.5f), input.ActualAcceleration);
    }

    [Fact]
    public void Native_observations_include_pose_phase_yaw_release_and_public_tick_evidence()
    {
        var required = new[]
        {
            "head", "spine", "upperBody", "headComponent", "spineComponent", "upperBodyComponent",
            "rotatePhase", "turnAccumulatedYaw", "rotateAccumulatedYaw",
            "leftFootReleaseReason", "rightFootReleaseReason",
            "runtimeInstanceClassPath", "runtimeNodePropertyName", "runtimeNodePropertyOrdinal", "runtimeSourceWeight",
            "footHitProvenance", "releaseReasonProvenance",
            "animationUpdateCount", "animationEvaluationCount", "publicAnimationTickCount",
        };

        foreach (var path in Directory.GetFiles(FixtureDirectory, "*.json"))
        foreach (var item in LoadJson(Path.GetFileName(path))["cases"]!.AsArray())
        {
            var native = item!["nativeActual"]!.AsObject();
            foreach (var property in required)
            {
                Assert.True(native.ContainsKey(property), $"{item["caseId"]}: nativeActual.{property} is missing.");
            }
            Assert.Equal(1, native["animationUpdateCount"]!.GetValue<int>());
            Assert.Equal(1, native["animationEvaluationCount"]!.GetValue<int>());
            Assert.Equal(1, native["animationPostUpdateCount"]!.GetValue<int>());
            Assert.Equal(1, native["publicAnimationTickCount"]!.GetValue<int>());
            Assert.Equal("derived_transition", native["footHitProvenance"]!.GetValue<string>());
            Assert.Equal("derived_transition", native["releaseReasonProvenance"]!.GetValue<string>());
            if (item["category"]!.GetValue<string>() is "Aim" or "Turn" or "Rotate")
            {
                Assert.NotEmpty(native["runtimeInstanceClassPath"]!.GetValue<string>());
                Assert.NotEmpty(native["runtimeNodePropertyName"]!.GetValue<string>());
                Assert.True(native["runtimeNodePropertyOrdinal"]!.GetValue<int>() >= 0);
                Assert.True(native["runtimeSourceWeight"]!.GetValue<float>() > 0f);
            }
        }
    }

    [Fact]
    public void Native_local_and_component_pose_evidence_remain_independent()
    {
        foreach (var file in new[] { "trace_p4_aim.json", "trace_p4_feet.json", "trace_p4_platform.json" })
        {
            foreach (var item in LoadJson(file)["cases"]!.AsArray())
            {
                var native = item!["nativeActual"]!;
                Assert.NotEqual(native["leftFoot"]!.ToJsonString(), native["leftFootComponent"]!.ToJsonString());
                Assert.NotEqual(native["rightFoot"]!.ToJsonString(), native["rightFootComponent"]!.ToJsonString());
            }
        }

        foreach (var item in LoadJson("trace_p4_aim.json")["cases"]!.AsArray())
        {
            var native = item!["nativeActual"]!;
            Assert.NotEqual(native["head"]!.ToJsonString(), native["headComponent"]!.ToJsonString());
            Assert.NotEqual(native["spine"]!.ToJsonString(), native["spineComponent"]!.ToJsonString());
            Assert.NotEqual(native["upperBody"]!.ToJsonString(), native["upperBodyComponent"]!.ToJsonString());
        }
    }

    [Fact]
    public void Platform_stimulus_is_the_same_native_frame_and_lock_history()
    {
        var platform = LoadJson("trace_p4_platform.json");
        foreach (var item in platform["cases"]!.AsArray())
        {
            var stimulus = item!["stimulus"]!.AsObject();
            var native = item["nativeActual"]!.AsObject();
            Assert.Equal(stimulus["platformId"]!.ToJsonString(), native["movementBaseId"]!.ToJsonString());
            Assert.Equal(stimulus["platformColliderId"]!.ToJsonString(), native["movementBaseColliderId"]!.ToJsonString());
            Assert.Equal(stimulus["platformPosition"]!.ToJsonString(), native["movementBasePosition"]!.ToJsonString());
            Assert.Equal(stimulus["platformRotation"]!.ToJsonString(), native["movementBaseRotation"]!.ToJsonString());
            Assert.Equal(stimulus["previousPlatformId"]!.ToJsonString(), native["previousMovementBaseId"]!.ToJsonString());
            Assert.Equal(stimulus["previousPlatformColliderId"]!.ToJsonString(), native["previousMovementBaseColliderId"]!.ToJsonString());
            Assert.Equal(stimulus["previousPlatformPosition"]!.ToJsonString(), native["previousMovementBasePosition"]!.ToJsonString());
            Assert.Equal(stimulus["previousPlatformRotation"]!.ToJsonString(), native["previousMovementBaseRotation"]!.ToJsonString());
            var previousPlatformId = stimulus["previousPlatformId"]!.GetValue<int>();
            Assert.Equal(
                stimulus["initialLeftLock"]!["localPosition"]!.ToJsonString(),
                native[previousPlatformId >= 0
                    ? "previousLeftLockBasePosition"
                    : "previousLeftLockWorldPosition"]!.ToJsonString());
            Assert.Equal(
                stimulus["initialRightLock"]!["localPosition"]!.ToJsonString(),
                native[previousPlatformId >= 0
                    ? "previousRightLockBasePosition"
                    : "previousRightLockWorldPosition"]!.ToJsonString());
        }
    }

    [Fact]
    public void Platform_release_records_the_adjacent_native_release_onset()
    {
        var released = Case(LoadJson("trace_p4_platform.json"), "platform_release")["nativeActual"]!;

        Assert.True(released["previousLeftFootLockAmount"]!.GetValue<float>() > 0f);
        Assert.True(released["previousRightFootLockAmount"]!.GetValue<float>() > 0f);
        Assert.True(released["leftFootLockAmount"]!.GetValue<float>() > 0f);
        Assert.True(released["rightFootLockAmount"]!.GetValue<float>() > 0f);
        Assert.Equal("PlatformRemoved", released["leftFootReleaseReason"]!.GetValue<string>());
        Assert.Equal("PlatformRemoved", released["rightFootReleaseReason"]!.GetValue<string>());
    }

    [Fact]
    public void Platform_removal_signal_is_explicit_and_case_locked()
    {
        foreach (var path in Directory.GetFiles(FixtureDirectory, "*.json"))
        foreach (var item in LoadJson(Path.GetFileName(path))["cases"]!.AsArray())
        {
            var traceCase = item!.AsObject();
            var stimulus = traceCase["stimulus"]!;
            var signals = stimulus["platformRemovalSignals"]!;
            var removed = traceCase["caseId"]!.GetValue<string>() == "platform_release";
            Assert.Equal(removed ? 1 : 0, signals["leftRemoved"]!.GetValue<int>());
            Assert.Equal(removed ? 1 : 0, signals["rightRemoved"]!.GetValue<int>());
            Assert.Equal(removed ? stimulus["previousPlatformId"]!.GetValue<int>() : -1,
                signals["leftPlatformId"]!.GetValue<int>());
            Assert.Equal(removed ? stimulus["previousPlatformColliderId"]!.GetValue<long>() : -1,
                signals["leftColliderId"]!.GetValue<long>());
            Assert.Equal(removed ? stimulus["previousPlatformId"]!.GetValue<int>() : -1,
                signals["rightPlatformId"]!.GetValue<int>());
            Assert.Equal(removed ? stimulus["previousPlatformColliderId"]!.GetValue<long>() : -1,
                signals["rightColliderId"]!.GetValue<long>());
        }

        using var mutation = Mutate(FixturePath("trace_p4_platform.json"), root =>
            Case(root, "platform_release")["stimulus"]!["platformRemovalSignals"]!["leftColliderId"] = 999L);
        Assert.Contains("platform removal signal", Assert.Throws<FormatException>(() =>
            AlsPoseTrace.Load(mutation.Path)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_fixture_properties_are_rejected()
    {
        var path = FixturePath("trace_p4_platform.json");
        using var mutation = Mutate(path, root => root["unexpected"] = true);

        var exception = Assert.Throws<FormatException>(() => AlsPoseTrace.Load(mutation.Path));

        Assert.Contains("Unknown property", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Oracle_writer_is_deterministic_and_replays_production_core()
    {
        var source = FixturePath("trace_p4_rotate.json");
        var root = JsonNode.Parse(File.ReadAllText(source))!.AsObject();
        FirstCase(root)["portExpected"]!["rotateAnimationId"] = -1;
        using var raw = new TemporaryJson(Path.GetFileName(source), root.ToJsonString());
        var firstDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(raw.Path)!, "first");
        var secondDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(raw.Path)!, "second");
        var first = System.IO.Path.Combine(firstDirectory, Path.GetFileName(source));
        var second = System.IO.Path.Combine(secondDirectory, Path.GetFileName(source));

        AlsPoseTrace.WritePortOracle(raw.Path, first);
        AlsPoseTrace.WritePortOracle(raw.Path, second);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Empty(AlsPoseTrace.Compare(AlsPoseTrace.Load(first)));
    }

    private static (string Path, AlsPoseTrace Trace)[] LoadAll() =>
        Directory.GetFiles(FixtureDirectory, "*.json")
            .Order(StringComparer.Ordinal)
            .Select(path => (path, AlsPoseTrace.Load(path)))
            .ToArray();

    private static int CountCategory(
        IEnumerable<(string Path, AlsPoseTrace Trace)> traces,
        string category) =>
        traces.SelectMany(item => item.Trace.Cases)
            .Count(item => item.Category == category);

    private static string FixturePath(string name) => Path.Combine(FixtureDirectory, name);

    private static void AssertSchemaValid(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(PoseSchema.Value.Evaluate(document.RootElement).IsValid,
            $"Mutation must remain schema-valid: {Path.GetFileName(path)}");
    }

    private static JsonObject FirstCase(JsonObject root) =>
        root["cases"]!.AsArray()[0]!.AsObject();

    private static JsonObject FirstExpected(JsonObject root) =>
        FirstCase(root)["portExpected"]!.AsObject();

    private static JsonObject Native(JsonObject root, int caseIndex) =>
        root["cases"]!.AsArray()[caseIndex]!["nativeActual"]!.AsObject();

    private static JsonObject LoadJson(string name) =>
        JsonNode.Parse(File.ReadAllText(FixturePath(name)))!.AsObject();

    private static JsonObject Case(JsonObject root, string caseId) =>
        root["cases"]!.AsArray().Select(item => item!.AsObject())
            .Single(item => item["caseId"]!.GetValue<string>() == caseId);

    private static Vector3 ReadVector(JsonObject value) => new(
        value["x"]!.GetValue<float>(), value["y"]!.GetValue<float>(), value["z"]!.GetValue<float>());

    private static JsonObject VectorObject(Vector3 value) => new()
    {
        ["x"] = value.X,
        ["y"] = value.Y,
        ["z"] = value.Z,
    };

    private static void OffsetX(JsonObject value) =>
        value["x"] = value["x"]!.GetValue<float>() + 0.125f;

    private static void RotateQuaternion(JsonObject value)
    {
        var changed = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.125f);
        value["x"] = changed.X;
        value["y"] = changed.Y;
        value["z"] = changed.Z;
        value["w"] = changed.W;
    }

    private static float QuaternionAngle(JsonObject left, JsonObject right)
    {
        var a = Quaternion.Normalize(new Quaternion(
            left["x"]!.GetValue<float>(), left["y"]!.GetValue<float>(),
            left["z"]!.GetValue<float>(), left["w"]!.GetValue<float>()));
        var b = Quaternion.Normalize(new Quaternion(
            right["x"]!.GetValue<float>(), right["y"]!.GetValue<float>(),
            right["z"]!.GetValue<float>(), right["w"]!.GetValue<float>()));
        return 2f * MathF.Acos(System.Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f));
    }

    private static void SetTransform(JsonObject transform, Vector3 position, Quaternion rotation)
    {
        transform["position"] = new JsonObject { ["x"] = position.X, ["y"] = position.Y, ["z"] = position.Z };
        transform["rotation"] = new JsonObject { ["x"] = rotation.X, ["y"] = rotation.Y, ["z"] = rotation.Z, ["w"] = rotation.W };
    }

    private static void RotateExpectedFoot(JsonObject root, string side, float angle)
    {
        var rotation = FirstExpected(root)[side]!["rotation"]!.AsObject();
        var original = Quaternion.Normalize(new Quaternion(
            rotation["x"]!.GetValue<float>(), rotation["y"]!.GetValue<float>(),
            rotation["z"]!.GetValue<float>(), rotation["w"]!.GetValue<float>()));
        var changed = Quaternion.Normalize(Quaternion.Concatenate(original, Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle)));
        if (changed.W < 0f) changed = new Quaternion(-changed.X, -changed.Y, -changed.Z, -changed.W);
        rotation["x"] = changed.X;
        rotation["y"] = changed.Y;
        rotation["z"] = changed.Z;
        rotation["w"] = changed.W;
    }

    private static AlsPoseTrace LoadWithoutDescriptorLock(string path)
    {
        var load = typeof(AlsPoseTrace).GetMethod(
            "Load",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(bool)],
            modifiers: null) ?? throw new InvalidOperationException("AlsPoseTrace private loader is unavailable.");
        return (AlsPoseTrace)load.Invoke(null, [path, false])!;
    }

    private static readonly string[] ExpectedDescriptors = CreateExpectedDescriptors();

    private static string[] CreateExpectedDescriptors()
    {
        const string idle = "/ALS/ALS/Animations/Base/A_Als_Idle.A_Als_Idle";
        var values = new List<string>();
        void Add(string file, string id, string category, string stance, string direction, string phase,
            int nominal = 0, string release = "None", string? animation = null,
            string curves = "FootLock_L,FootLock_R")
        {
            animation ??= idle;
            values.Add($"trace_p4_{file}.json|{id}|{category}|{stance}|{direction}|{phase}|{nominal}|{release}|{release}|{animation}|{animation}|{curves}");
        }

        const string look = "/ALS/ALS/Animations/View/BS_Als_Look.BS_Als_Look";
        Add("aim", "aim_center", "Aim", "Standing", "Center", "steady", animation: look, curves: "Layering_Head");
        Add("aim", "aim_up", "Aim", "Standing", "Up", "steady", animation: look, curves: "Layering_Head");
        Add("aim", "aim_down", "Aim", "Standing", "Down", "steady", animation: look, curves: "Layering_Head");
        Add("aim", "aim_left", "Aim", "Standing", "Left", "steady", animation: look, curves: "Layering_Head");
        Add("aim", "aim_right", "Aim", "Standing", "Right", "steady", animation: look, curves: "Layering_Head");
        foreach (var stance in new[] { "Standing", "Crouching" })
        foreach (var direction in new[] { "Left", "Right" })
        foreach (var nominal in new[] { 90, 180 })
        {
            var asset = $"/ALS/ALS/Animations/TurnInPlace/A_Als_{(stance == "Crouching" ? "Crouch_" : string.Empty)}Turn_{nominal}_{direction}.A_Als_{(stance == "Crouching" ? "Crouch_" : string.Empty)}Turn_{nominal}_{direction}";
            Add("turn", $"turn_{stance.ToLowerInvariant()}_{direction.ToLowerInvariant()}_{nominal}", "Turn", stance, direction, "playing", nominal, animation: asset, curves: "RotationYawSpeed");
        }
        foreach (var stance in new[] { "Standing", "Crouching" })
        foreach (var direction in new[] { "Left", "Right" })
        {
            var asset = $"/ALS/ALS/Animations/RotateInPlace/A_Als_{(stance == "Crouching" ? "Crouch_" : string.Empty)}Rotate_90_{direction}.A_Als_{(stance == "Crouching" ? "Crouch_" : string.Empty)}Rotate_90_{direction}";
            Add("rotate", $"rotate_{stance.ToLowerInvariant()}_{direction.ToLowerInvariant()}", "Rotate", stance, direction, "playing", animation: asset, curves: "RotationYawSpeed");
        }
        Add("feet", "feet_flat", "Feet", "Standing", "None", "steady");
        Add("feet", "feet_slope", "Feet", "Standing", "None", "steady");
        Add("feet", "feet_stairs", "Feet", "Standing", "None", "steady");
        Add("platform", "platform_translate", "Platform", "Standing", "None", "hold");
        Add("platform", "platform_rotate", "Platform", "Standing", "None", "hold");
        Add("platform", "platform_base_change", "Platform", "Standing", "None", "release", release: "BaseChanged");
        Add("platform", "platform_teleport", "Platform", "Standing", "None", "release", release: "Teleported");
        Add("platform", "platform_release", "Platform", "Standing", "None", "release", release: "PlatformRemoved");
        return values.ToArray();
    }

    private static TemporaryJson Mutate(string path, Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        mutation(root);
        return new TemporaryJson(Path.GetFileName(path), root.ToJsonString());
    }

    private sealed class TemporaryJson : IDisposable
    {
        private readonly string _directory;

        public TemporaryJson(string fileName, string json)
        {
            _directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"godotals-p4-golden-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, fileName);
            File.WriteAllText(Path, json);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
