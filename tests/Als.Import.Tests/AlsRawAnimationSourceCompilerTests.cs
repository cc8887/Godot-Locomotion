using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRawAnimationSourceCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Manifest = new(P3RepositoryFixtures.LoadAnimationSet);
    [Fact]
    public void ReferenceRotationsRetainNativeDoubleBitsAndCannotShareAfterSubFloatChanges()
    {
        var fixture = new Fixture();
        var reference = fixture.Index["skeletons"]![0]!["referencePose"]![1]!;
        reference["rotation"]![0] = 1e-9; reference["rotation"]![3] = 1.00000000001;
        fixture.Policy(0)["retargetTransforms"] = fixture.Index["skeletons"]![0]!["referencePose"]!.DeepClone();
        fixture.Policy(0)["rootLockFirstFrame"]!["rotation"]![3] = 1.00000000003;
        var first = fixture.Compile(); var q = first.GetSkeleton(0).PreciseReferencePose[1].Rotation;
        Assert.Equal(-1e-9, q.X); Assert.Equal(1.00000000001, q.W);
        Assert.Equal(q, first.GetSource(0).Policy.PreciseRetargetTransforms[1].Rotation);
        Assert.Equal(1.00000000003, first.GetSource(0).Policy.PreciseRootLockFirstFrame.Rotation.W);
        reference["rotation"]![3] = 1.00000000002;
        var second = fixture.Compile();
        Assert.Equal(first.GetSkeleton(0).ReferencePose.ToArray(), second.GetSkeleton(0).ReferencePose.ToArray());
        Assert.Throws<ArgumentException>(() => second.ReuseResourcesFrom(first));
    }
    [Fact]
    public void CompilesSparseReorderedPhysicalAndExplicitVirtualTracksWithoutInventingPresence()
    {
        var fixture = new Fixture(); var tracks = fixture.Asset(0)["tracks"]!.AsArray();
        tracks.RemoveAt(1); // Keep physical bones 2 and 0; physical 1 has no authored track.
        tracks.Add(Track("vb foot", 8));
        var bank = fixture.Compile(); var source = bank.GetSource(0); var data = source.PoseData;
        Assert.Equal(3, data.PhysicalBoneCount); Assert.Equal(4, data.LogicalBoneCount); Assert.Equal(1, data.VirtualBoneCount);
        Assert.Equal(new[] { true, false, true, true }, data.LogicalTrackPresence.ToArray());
        Assert.Equal(new[] { true }, data.VirtualTrackPresence.ToArray());
        Assert.Equal(default, data.GetPhysicalKey(0)[1]);
        Assert.Equal(new Vector3(22, -4, 6) * .01f, data.GetPhysicalKey(2)[2].Position);
        Assert.Equal(new Vector3(82, -4, 6) * .01f, data.GetVirtualKey(2)[0].Position);
        Assert.Equal(new[] { 0, 1, 2, -1 }, bank.GetSkeleton(0).LogicalToPhysical.ToArray());
        Assert.Equal(new AlsLogicalVirtualBone(3, 0, 2), bank.GetSkeleton(0).VirtualBones[0]);
    }

    [Fact]
    public void RetainsRawQuaternionBitsAndSignWithoutNormalization()
    {
        var fixture = new Fixture(); var track = fixture.Asset(0)["tracks"]![0]!;
        float[] raw = [.703233003616333f, .07391838729381561f, -.7032334804534912f, .0739060640335083f];
        track["rotations"] = Channel(raw);
        var q = fixture.Compile().GetSource(0).PoseData.GetPhysicalKey(0)[2].Rotation;
        Assert.Equal(BitConverter.SingleToInt32Bits(-raw[0]), BitConverter.SingleToInt32Bits(q.X));
        Assert.Equal(BitConverter.SingleToInt32Bits(raw[1]), BitConverter.SingleToInt32Bits(q.Y));
        Assert.Equal(BitConverter.SingleToInt32Bits(-raw[2]), BitConverter.SingleToInt32Bits(q.Z));
        Assert.Equal(BitConverter.SingleToInt32Bits(raw[3]), BitConverter.SingleToInt32Bits(q.W));
        track["rotations"] = Channel([0, 0, 0, -1.00001f]);
        Assert.Equal(-1.00001f, fixture.Compile().GetSource(0).PoseData.GetPhysicalKey(1)[2].Rotation.W);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExpandsSingletonChannelsAndNativeEmptyScale(bool emptyScale)
    {
        var fixture = new Fixture(); var track = fixture.Asset(0)["tracks"]![0]!;
        track["positions"] = Channel([10, 20, 30]); track["rotations"] = Channel([0, 0, 0, -1]);
        track["scales"] = emptyScale ? new JsonArray() : Channel([2, 3, 4]);
        var data = fixture.Compile().GetSource(0).PoseData;
        Assert.Equal(data.GetPhysicalKey(0)[2], data.GetPhysicalKey(2)[2]);
        Assert.Equal(emptyScale ? Vector3.One : new Vector3(2, 3, 4), data.GetPhysicalKey(1)[2].Scale);
    }

    [Fact]
    public void AcceptsAReferenceOnlySequenceWithNoAuthoredBoneTracks()
    {
        var fixture = new Fixture(); fixture.Asset(0)["tracks"] = new JsonArray();
        var source = fixture.Compile().GetSource(0);
        Assert.All(source.PoseData.LogicalTrackPresence.ToArray(), present => Assert.False(present));
        Assert.All(source.PoseData.GetPhysicalKey(0).ToArray(), pose => Assert.Equal(default, pose));
    }

    [Fact]
    public void PreservesRootLockConstantAndSequenceFloatLengthSeparatelyFromRawKeys()
    {
        var fixture = new Fixture(); var policy = fixture.Policy(0);
        policy["rootLockFirstFrame"] = Pose([123, 456, 789], [0, 0, 0, -1], [2, 3, 4]);
        var source = fixture.Compile().GetSource(0);
        Assert.Equal((double)(2f / 30), source.Policy.SequencePlayLength);
        Assert.Equal(2d / 30, source.PoseData.PlayLength);
        Assert.NotEqual(source.Policy.SequencePlayLength, source.PoseData.PlayLength);
        Assert.Equal(new Vector3(123, -456, 789) * .01f, source.Policy.RootLockFirstFrame.Position);
        Assert.Equal(-1, source.Policy.RootLockFirstFrame.Rotation.W);
        Assert.NotEqual(source.PoseData.GetPhysicalKey(0)[0], source.Policy.RootLockFirstFrame);
    }

    [Fact]
    public void PreservesStoredSourceReferenceSelectionAndMissingVirtualReferenceLength()
    {
        var fixture = new Fixture(); var policy = fixture.Policy(0);
        var reference = fixture.Index["skeletons"]![0]!["referencePose"]!.DeepClone().AsArray();
        reference.RemoveAt(3); reference[1]!["position"]![0] = 42;
        policy["retargetSourceAsset"] = "/Game/SourceMesh.SourceMesh";
        policy["retargetSourceAssetReferencePose"] = reference.DeepClone();
        policy["retargetTransforms"] = reference;
        policy["retargetTransformsSourceName"] = "/Game/A";
        var actual = fixture.Compile().GetSource(0).Policy;
        Assert.Equal(3, actual.RetargetTransforms.Length);
        Assert.Equal(3, actual.RetargetSourceAssetReferencePose.Length);
        Assert.Equal(42 * .01f, actual.RetargetTransforms[1].Position.X);
        Assert.Equal("/Game/SourceMesh.SourceMesh", actual.RetargetSourceAsset);
    }

    [Fact]
    public void PreservesNamedRetargetReferenceRatherThanReplacingItWithTargetRest()
    {
        var fixture = new Fixture(); fixture.Policy(0)["retargetSource"] = "Tall";
        fixture.Policy(0)["retargetTransformsSourceName"] = "tall";
        fixture.Policy(0)["retargetTransforms"]![1]!["position"]![0] = 999;
        var bank = fixture.Compile();
        Assert.NotEqual(bank.GetSkeleton(0).ReferencePose[1], bank.GetSource(0).Policy.RetargetTransforms[1]);
        Assert.Equal("Tall", bank.GetSource(0).Policy.RetargetSource);
    }

    [Fact]
    public void CompilesRecursiveBaseClosureAndRetainsRootsThatAlsoServeAsDependencies()
    {
        var fixture = new Fixture(); fixture.AddAsset(1); fixture.AddAsset(2);
        fixture.SetBase(0, 1); fixture.SetBase(1, 2);
        fixture.Roots = [0, 2]; fixture.UpdateRequest();
        var bank = fixture.Compile();
        Assert.Equal(new[] { 0, 2 }, bank.RootAnimationIds.ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, bank.Sources.ToArray().Select(s => s.PoseData.Identity.AnimationId));
        Assert.Equal(1, bank.GetSource(0).Policy.BaseAnimationId); Assert.Equal(2, bank.GetSource(1).Policy.BaseAnimationId);
        Assert.Equal(AlsRawAnimationBasePoseType.AnimFrame, bank.GetSource(1).Policy.BasePoseType);
        // Non-additive assets may still retain a valid RefPoseSeq and base-frame configuration.
        Assert.Equal(AlsRawAnimationAdditiveType.None, bank.GetSource(1).Policy.AdditiveType);
    }

    [Fact]
    public void CurveIdentityUsesNativeFNameAndExcludesDerivedRootTracks()
    {
        var fixture = new Fixture(); var animation = fixture.Set.Animations[0];
        fixture.Set.Animations[0] = animation with
        {
            Curves = [new(0, AlsCanonicalCurveKind.None, "Layering_Arm_L", AlsCurveProvenance.SourceCurve, []),
                new(1, AlsCanonicalCurveKind.RotationYawSpeedRadiansPerSecond, "RotationYawSpeed", AlsCurveProvenance.DerivedRootTrack, [])]
        };
        fixture.Policy(0)["floatCurveCount"] = 1;
        fixture.Policy(0)["floatCurveNames"] = new JsonArray("layering_arm_l");
        var policy = fixture.Compile().GetSource(0).Policy;
        Assert.Equal(new[] { "layering_arm_l" }, policy.FloatCurveNames.ToArray());
    }

    [Theory]
    [InlineData(1, 1, "AAT_LocalSpaceBase", "ABPT_RefPose")]
    [InlineData(2, 4, "AAT_RotationOffsetMeshSpace", "ABPT_LocalAnimFrame")]
    public void RetainsSupportedStepAndAdditivePoliciesWithoutClampingBaseFrame(int additive, int baseType, string additiveName, string baseName)
    {
        var fixture = new Fixture(); var animation = fixture.Set.Animations[0];
        fixture.Set.Animations[0] = animation with { Interpolation = 1, AdditiveType = additive, AdditiveBasePoseType = baseType, AdditiveBasePoseFrame = -3 };
        var policy = fixture.Policy(0); policy["interpolation"] = "Step"; policy["additiveType"] = additiveName;
        policy["basePoseType"] = baseName; policy["baseFrame"] = -3;
        var source = fixture.Compile().GetSource(0);
        Assert.Equal(AlsRawAnimationInterpolation.Step, source.PoseData.Interpolation);
        Assert.Equal(additive, (int)source.Policy.AdditiveType); Assert.Equal(baseType, (int)source.Policy.BasePoseType);
        Assert.Equal(-3, source.Policy.BaseFrame);
    }

    [Fact]
    public void ReturnedBankDoesNotShareMutableJsonOrRequestContainers()
    {
        var fixture = new Fixture(); var bank = fixture.Compile();
        var original = bank.GetSource(0).PoseData.GetPhysicalKey(1)[2];
        fixture.Roots[0] = 2; fixture.Asset(0)["tracks"]![0]!["positions"]![1]![0] = 9999;
        fixture.Policy(0)["retargetTransforms"]![0]!["position"]![0] = 10;
        Assert.Equal(new[] { 0 }, bank.RootAnimationIds.ToArray());
        Assert.Equal(original, bank.GetSource(0).PoseData.GetPhysicalKey(1)[2]);
        Assert.Equal(0, bank.GetSource(0).Policy.RetargetTransforms[0].Position.X);
        Assert.Throws<ArgumentOutOfRangeException>(() => bank.GetSource(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => bank.GetSkeleton(3));
    }

    [Theory]
    [InlineData("provenance")] [InlineData("schema")] [InlineData("manifest-digest")] [InlineData("binding-digest")]
    [InlineData("players")] [InlineData("samples")] [InlineData("missing-root")] [InlineData("duplicate-root")]
    [InlineData("root-path")] [InlineData("duplicate-source")] [InlineData("foreign-source")] [InlineData("foreign-file-source")]
    [InlineData("foreign-file-skeleton")] [InlineData("extra-resource")] [InlineData("missing-resource")]
    [InlineData("missing-skeleton")] [InlineData("duplicate-skeleton")] [InlineData("oracle-field")]
    [InlineData("absolute-file")] [InlineData("traversal-file")] [InlineData("wrong-file-asset")]
    [InlineData("unknown-owner")] [InlineData("fake-owner")]
    public void RejectsUnverifiedOrMismatchedExportClosure(string mutation)
    {
        var fixture = new Fixture(); var request = fixture.Index["request"]!;
        var rows = fixture.Index["assets"]!.AsArray(); var row = rows[0]!;
        switch (mutation)
        {
            case "provenance": fixture.Index["source"] = "GetAnimPoseAtTime"; break;
            case "schema": fixture.Index["schemaVersion"] = 2; break;
            case "manifest-digest": request["definitionDigest"] = "other"; break;
            case "binding-digest": request["bindingDigest"] = "other"; break;
            case "players": request["players"] = 2; break;
            case "samples": request["samples"] = 2; break;
            case "missing-root": request["rootAssets"] = new JsonArray(); break;
            case "duplicate-root": request["rootAssets"]!.AsArray().Add(request["rootAssets"]![0]!.DeepClone()); break;
            case "root-path": request["rootAssets"]![0]!["source"] = "/Game/B.B"; break;
            case "duplicate-source": rows.Add(row.DeepClone()); break;
            case "foreign-source": row["assetId"] = new string('f', 40); break;
            case "foreign-file-source": fixture.Asset(0)["source"] = "/Game/B.B"; break;
            case "foreign-file-skeleton": fixture.Asset(0)["skeletonSource"] = "/Game/Other.Other"; break;
            case "extra-resource": fixture.AddAsset(1); break;
            case "missing-resource": rows.Clear(); break;
            case "missing-skeleton": fixture.Index["skeletons"] = new JsonArray(); break;
            case "duplicate-skeleton": fixture.Index["skeletons"]!.AsArray().Add(fixture.Index["skeletons"]![0]!.DeepClone()); break;
            case "oracle-field": fixture.Asset(0)["pose"] = new JsonArray(); break;
            case "absolute-file": row["file"] = Path.GetFullPath("raw.json").Replace('\\', '/'); break;
            case "traversal-file": row["file"] = "raw_sequences/../raw.json"; break;
            case "wrong-file-asset": row["file"] = "raw_sequences/" + fixture.Set.Animations[1].StableId + ".json"; break;
            case "unknown-owner": row["dependencyOf"] = new JsonArray(new string('f', 40)); break;
            case "fake-owner": row["dependencyOf"] = new JsonArray(fixture.Set.Animations[0].StableId); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => fixture.Compile());
    }

    [Fact]
    public void RejectsHashMismatchBeforeParsingSourceBytes()
    {
        var fixture = new Fixture(); fixture.PrepareBytes(); var reads = 0;
        Assert.Throws<ArgumentException>(() => fixture.CompilePrepared(_ => { reads++; return Encoding.UTF8.GetBytes("not json"); }));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void RejectsUnsafePathsBeforeCallingTheExternalReader()
    {
        var fixture = new Fixture(); fixture.Index["assets"]![0]!["file"] = "../secret.json"; var reads = 0;
        Assert.Throws<ArgumentException>(() => fixture.CompilePrepared(_ => { reads++; return []; }));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void RejectsDuplicateJsonPropertiesInsteadOfUsingTheLastOccurrence()
    {
        var fixture = new Fixture(); var bytes = fixture.PrepareBytes();
        var json = fixture.Index.ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
        Assert.NotEqual(fixture.Index.ToJsonString(), json);
        Assert.Throws<ArgumentException>(() => AlsRawAnimationSourceCompiler.Compile(json, fixture.Set, "binding", 1, 1, fixture.Roots, path => bytes[path]));
    }

    [Theory]
    [InlineData("rate")] [InlineData("keys")] [InlineData("duration")] [InlineData("sequence-duration")]
    [InlineData("unknown-bone")] [InlineData("duplicate-bone")] [InlineData("duplicate-bone-case")]
    [InlineData("empty-position")] [InlineData("empty-rotation")] [InlineData("partial-channel")] [InlineData("component-count")]
    [InlineData("nonfinite")] [InlineData("zero-quaternion")] [InlineData("nonunit-quaternion")]
    [InlineData("bone-order")] [InlineData("parent")] [InlineData("mapping")] [InlineData("virtual-target")]
    [InlineData("reference-position")] [InlineData("reference-quaternion")] [InlineData("retarget-mode")]
    [InlineData("interpolation")] [InlineData("root-enable")] [InlineData("root-force")] [InlineData("root-mode")]
    [InlineData("root-normalize")] [InlineData("root-constant")] [InlineData("additive")] [InlineData("base-type")]
    [InlineData("base-frame")] [InlineData("base-source")] [InlineData("transform-curves")] [InlineData("attributes")]
    [InlineData("curve-count")] [InlineData("curve-name")] [InlineData("retarget-name")] [InlineData("retarget-reference")]
    public void RejectsUnimplementedOrSemanticallyChangedSourceData(string mutation)
    {
        var fixture = new Fixture(); var asset = fixture.Asset(0); var track = asset["tracks"]![0]!;
        var policy = fixture.Policy(0); var skeleton = fixture.Index["skeletons"]![0]!;
        switch (mutation)
        {
            case "rate": asset["frameRateNumerator"] = 60; break;
            case "keys": asset["sampledKeyCount"] = 4; break;
            case "duration": asset["playLength"] = 100; break;
            case "sequence-duration": policy["sequencePlayLength"] = 2d / 30; break;
            case "unknown-bone": track["bone"] = "other"; break;
            case "duplicate-bone": asset["tracks"]!.AsArray().Add(track.DeepClone()); break;
            case "duplicate-bone-case": asset["tracks"]![1]!["bone"] = "FOOT"; break;
            case "empty-position": track["positions"] = new JsonArray(); break;
            case "empty-rotation": track["rotations"] = new JsonArray(); break;
            case "partial-channel": track["rotations"] = Channel([0, 0, 0, 1], [0, 0, 0, 1]); break;
            case "component-count": track["positions"] = Channel([0, 0]); break;
            case "nonfinite": track["positions"]![0]![0] = 1e100; break;
            case "zero-quaternion": track["rotations"] = Channel([0, 0, 0, 0]); break;
            case "nonunit-quaternion": track["rotations"] = Channel([0, 0, 0, 2]); break;
            case "bone-order": skeleton["rawBoneNames"]![0] = "foot"; break;
            case "parent": skeleton["logicalParents"]![1] = -1; break;
            case "mapping": skeleton["logicalToPhysical"]![1] = 2; break;
            case "virtual-target": skeleton["virtualBones"]![0]!["target"] = 1; break;
            case "reference-position": skeleton["referencePose"]![1]!["position"]![0] = 999; break;
            case "reference-quaternion": skeleton["referencePose"]![1]!["rotation"] = Numbers([0, 0, 1, 0]); break;
            case "retarget-mode": skeleton["translationRetargetModes"]![0] = "AnimationScaled"; break;
            case "interpolation": policy["interpolation"] = "Step"; break;
            case "root-enable": policy["enableRootMotion"] = true; break;
            case "root-force": policy["forceRootLock"] = true; break;
            case "root-mode": policy["rootMotionRootLock"] = "Zero"; break;
            case "root-normalize": policy["useNormalizedRootMotionScale"] = true; break;
            case "root-constant": policy["rootLockFirstFrame"]!["rotation"] = Numbers([0, 0, 0, 0]); break;
            case "additive": policy["additiveType"] = "AAT_LocalSpaceBase"; break;
            case "base-type": policy["basePoseType"] = "ABPT_RefPose"; break;
            case "base-frame": policy["baseFrame"] = 1; break;
            case "base-source": policy["baseAsset"] = "/Game/B.B"; break;
            case "transform-curves": policy["transformCurveCount"] = 1; break;
            case "attributes": policy["animatedBoneAttributeCount"] = 1; break;
            case "curve-count": policy["floatCurveCount"] = 1; break;
            case "curve-name": policy["floatCurveCount"] = 1; policy["floatCurveNames"] = new JsonArray("Curve"); break;
            case "retarget-name": policy["retargetTransformsSourceName"] = "Other"; break;
            case "retarget-reference": policy["retargetTransforms"]![1]!["position"]![0] = 33; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => fixture.Compile());
    }

    [Theory]
    [InlineData("missing-base")] [InlineData("missing-owner")] [InlineData("wrong-owner")] [InlineData("duplicate-owner")]
    public void RejectsIncompleteDependencyClosure(string mutation)
    {
        var fixture = new Fixture(); fixture.AddAsset(1); fixture.SetBase(0, 1);
        var rows = fixture.Index["assets"]!.AsArray();
        switch (mutation)
        {
            case "missing-base": rows.RemoveAt(1); break;
            case "missing-owner": rows[1]!.AsObject().Remove("dependencyOf"); break;
            case "wrong-owner": rows[1]!["dependencyOf"] = new JsonArray(fixture.Set.Animations[2].StableId); break;
            case "duplicate-owner": rows[1]!["dependencyOf"]!.AsArray().Add(fixture.Set.Animations[0].StableId); break;
        }
        Assert.ThrowsAny<Exception>(() => fixture.Compile());
    }

    [Fact]
    public void ActualProductionSourceBankCoversRequestedPlayersAndNativeDependencies()
    {
        var root = RepositoryRoot.Find(); var set = P3RepositoryFixtures.LoadAnimationSet();
        var indexJson = File.ReadAllText(Path.Combine(root, "assets/config/v4_movement_source_inputs.json"));
        using var requestDocument = JsonDocument.Parse(indexJson);
        var request = requestDocument.RootElement.GetProperty("request");
        Assert.Equal(set.DefinitionDigest, request.GetProperty("definitionDigest").GetString());
        Assert.Equal("B3CDBA03FD845B98", request.GetProperty("bindingDigest").GetString());
        var roots = request.GetProperty("rootAssets").EnumerateArray().Select(row =>
            Array.FindIndex(set.Animations, a => a.StableId == row.GetProperty("assetId").GetString())).ToArray();
        var bank = AlsRawAnimationSourceCompiler.Compile(indexJson,
            set, request.GetProperty("bindingDigest").GetString()!, request.GetProperty("players").GetInt32(),
            request.GetProperty("samples").GetInt32(), roots, file => File.ReadAllBytes(Path.Combine(root, "assets/config", file)));
        Assert.Equal(75, bank.PlayerCount); Assert.Equal(109, bank.SampleCount); Assert.Equal(76, bank.RootAnimationIds.Length);
        Assert.True(bank.Sources.Length >= bank.RootAnimationIds.Length);
        foreach (var source in bank.Sources)
        {
            var data = source.PoseData; var skeleton = bank.GetSkeleton(data.Identity.SkeletonId);
            Assert.Equal(skeleton.LogicalBoneCount, data.LogicalBoneCount);
            Assert.Equal(skeleton.PhysicalBoneCount, data.PhysicalBoneCount);
            Assert.Equal(set.Animations[data.Identity.AnimationId].ObjectPath, data.Identity.AssetPath);
            if (source.Policy.BaseAnimationId >= 0) Assert.NotNull(bank.GetSource(source.Policy.BaseAnimationId));
        }
    }

    private sealed class Fixture
    {
        public AlsAnimationSetDefinition Set { get; }
        public JsonObject Index { get; }
        public int[] Roots { get; set; } = [0];
        private readonly Dictionary<int, JsonObject> _assets = [];
        public Fixture()
        {
            string[] names = ["root", "thigh", "foot", "VB foot"]; int[] parents = [-1, 0, 1, 0]; int[] mapping = [0, 1, 2, -1];
            float[][] positions = [[0, 0, 0], [10, 20, 30], [2, 3, 4], [0, 0, 0]];
            var bones = names.Select((name, i) => new AlsBoneDefinition(i, mapping[i], name, parents[i],
                parents[i] < 0 ? -1 : mapping[parents[i]], AlsCoordinateConverter.PositionCentimetersToMeters(new(positions[i][0], positions[i][1], positions[i][2])),
                Quaternion.Identity, Vector3.One)).ToArray();
            var skeleton = new AlsSkeletonDefinition(new string('d', 40), "/Game/Rig.Rig", "source", "target", bones, bones.Take(3).ToArray(),
                [new(3, 0, 2)], [], mapping, [0, 1, 2], new(0, 1, 2, 2));
            var template = Manifest.Value;
            var animations = Enumerable.Range(0, 3).Select(id => new AlsAnimationDefinition(id, new string((char)('a' + id), 40),
                ((char)('A' + id)).ToString(), "/Game/" + (char)('A' + id) + "." + (char)('A' + id), "res://" + id,
                0, 2f / 30, 30, 1, 3, false, 0, false, 0, false, false, 0, 0, 0, -1, [], [], [], [], false, false)).ToArray();
            Set = template with { Skeletons = [skeleton], Animations = animations, DefinitionDigest = "manifest" };
            var reference = new JsonArray(positions.Select(p => (JsonNode)Pose(p, [0, 0, 0, 1], [1, 1, 1])).ToArray());
            Index = new JsonObject
            {
                ["schemaVersion"] = 1, ["source"] = "AnimDataModel.BoneAnimationTracks.InternalTrackData",
                ["request"] = new JsonObject(), ["assets"] = new JsonArray(),
                ["skeletons"] = new JsonArray(new JsonObject
                {
                    ["source"] = skeleton.ObjectPath, ["rawBoneNames"] = new JsonArray("root", "thigh", "foot"),
                    ["rawParents"] = new JsonArray(-1, 0, 1), ["logicalBoneNames"] = new JsonArray("root", "thigh", "foot", "VB foot"),
                    ["logicalParents"] = new JsonArray(-1, 0, 1, 0), ["logicalToPhysical"] = new JsonArray(0, 1, 2, -1),
                    ["virtualBones"] = new JsonArray(new JsonObject { ["bone"] = 3, ["source"] = 0, ["target"] = 2 }),
                    ["referencePose"] = reference, ["translationRetargetModes"] = new JsonArray("Animation", "Skeleton", "Animation")
                })
            };
            UpdateRequest(); AddAsset(0);
        }
        public void UpdateRequest() => Index["request"] = new JsonObject
        {
            ["definitionDigest"] = Set.DefinitionDigest, ["bindingDigest"] = "binding", ["players"] = 1, ["samples"] = 1,
            ["rootAssets"] = new JsonArray(Roots.Select(id => (JsonNode)new JsonObject
                { ["assetId"] = Set.Animations[id].StableId, ["source"] = Set.Animations[id].ObjectPath }).ToArray())
        };
        public JsonObject Asset(int id) => _assets[id];
        public JsonObject Policy(int id) => _assets[id]["evaluation"]!.AsObject();
        public void AddAsset(int id)
        {
            var animation = Set.Animations[id];
            _assets.Add(id, new JsonObject
            {
                ["source"] = animation.ObjectPath, ["skeletonSource"] = Set.Skeletons[0].ObjectPath,
                ["frameRateNumerator"] = 30, ["frameRateDenominator"] = 1, ["sampledKeyCount"] = 3, ["playLength"] = 2d / 30,
                ["tracks"] = new JsonArray(Track("foot", 2), Track("thigh", 1), Track("root", 0)),
                ["evaluation"] = new JsonObject
                {
                    ["interpolation"] = "Linear", ["sequencePlayLength"] = (double)animation.PlayLength,
                    ["retargetSource"] = "None", ["retargetSourceAsset"] = null, ["retargetSourceAssetReferencePose"] = new JsonArray(),
                    ["retargetTransformsSourceName"] = "None", ["retargetTransforms"] = Index["skeletons"]![0]!["referencePose"]!.DeepClone(),
                    ["enableRootMotion"] = false, ["forceRootLock"] = false, ["rootMotionRootLock"] = "RefPose",
                    ["rootLockFirstFrame"] = Pose([0, 0, 0], [0, 0, 0, 1], [1, 1, 1]),
                    ["useNormalizedRootMotionScale"] = false, ["additiveType"] = "AAT_None", ["basePoseType"] = "ABPT_None",
                    ["baseAsset"] = null, ["baseFrame"] = 0, ["transformCurveCount"] = 0, ["animatedBoneAttributeCount"] = 0,
                    ["floatCurveCount"] = 0, ["floatCurveNames"] = new JsonArray()
                }
            });
            Index["assets"]!.AsArray().Add(new JsonObject
            {
                ["assetId"] = animation.StableId, ["source"] = animation.ObjectPath,
                ["file"] = "raw_sequences/" + animation.StableId + ".json", ["sha256"] = new string('0', 64)
            });
        }
        public void SetBase(int owner, int dependency)
        {
            Set.Animations[owner] = Set.Animations[owner] with { AdditiveBasePoseAnimationId = dependency, AdditiveBasePoseType = 3 };
            Policy(owner)["baseAsset"] = Set.Animations[dependency].ObjectPath; Policy(owner)["basePoseType"] = "ABPT_AnimFrame";
            var row = Index["assets"]!.AsArray().Single(a => a!["assetId"]!.GetValue<string>() == Set.Animations[dependency].StableId)!;
            row["dependencyOf"] ??= new JsonArray(); row["dependencyOf"]!.AsArray().Add(Set.Animations[owner].StableId);
        }
        public Dictionary<string, byte[]> PrepareBytes()
        {
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var row in Index["assets"]!.AsArray())
            {
                var id = Array.FindIndex(Set.Animations, a => a.StableId == row!["assetId"]!.GetValue<string>());
                if (id < 0 || !_assets.TryGetValue(id, out var asset)) continue;
                var value = Encoding.UTF8.GetBytes(asset.ToJsonString());
                row!["sha256"] = Convert.ToHexString(SHA256.HashData(value)); bytes[row["file"]!.GetValue<string>()] = value;
            }
            return bytes;
        }
        public AlsRawAnimationSourceBank Compile()
        { var bytes = PrepareBytes(); return CompilePrepared(file => bytes[file]); }
        public AlsRawAnimationSourceBank CompilePrepared(Func<string, byte[]> read) => AlsRawAnimationSourceCompiler.Compile(
            Index.ToJsonString(), Set, "binding", 1, 1, Roots, read);
    }
    private static JsonObject Track(string bone, int value) => new()
    {
        ["bone"] = bone, ["positions"] = Channel([value * 10, 0, 0], [value * 10 + 1, 2, 3], [value * 10 + 2, 4, 6]),
        ["rotations"] = Channel([0, 0, 0, 1]), ["scales"] = Channel([1, 1, 1])
    };
    private static JsonObject Pose(float[] position, float[] rotation, float[] scale) => new()
    { ["position"] = Numbers(position), ["rotation"] = Numbers(rotation), ["scale"] = Numbers(scale) };
    private static JsonArray Channel(params float[][] rows) => new(rows.Select(row => (JsonNode)Numbers(row)).ToArray());
    private static JsonArray Numbers(float[] values) => new(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
}
