using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsBasePoseSourceKeysCompilerTests
{
    private static readonly Lazy<AlsAnimationSetDefinition> Set = new(P3RepositoryFixtures.LoadAnimationSet);
    private static readonly Lazy<AlsBasePosesDefinition> Definition = new(() =>
        AlsBasePosesCompiler.Compile(Read("assets/config/v4_layering_inputs.json"), Set.Value,
            Array.FindIndex(Set.Value.Skeletons, s => s.AssetId == "b5b52715012cad50bf7a625ddf01e4335bb4fcf0")));

    [Fact]
    public void CompilesKeyMajorPhysicalOrderRatherThanExportTrackOrder()
    {
        var root = Synthetic(); var tables = Compile(root.ToJsonString()); Assert.Equal(2, tables.Length);
        for (var source = 0; source < tables.Length; source++)
        {
            var table = tables[source]; Assert.Equal(Definition.Value.Evaluators[source], table.Evaluator);
            Assert.Equal(68, table.PhysicalBoneCount); Assert.Equal(2, table.SampledKeyCount);
            Assert.Equal(30, table.FrameRateNumerator); Assert.Equal(1, table.FrameRateDenominator);
            Assert.Equal(1d / 30, table.PlayLength);
            for (var bone = 0; bone < 68; bone++)
            {
                Assert.Equal(new Vector3(bone, -2, 3) * .01f, table.GetKey(0)[bone].Position);
                Assert.Equal(new Vector3(bone + 100, -4, 5) * .01f, table.GetKey(1)[bone].Position);
                Assert.Equal(new Vector3(2, 3, 4), table.GetKey(1)[bone].Scale);
            }
        }
    }

    [Fact]
    public void ResolvesNativeFNameDisplayCaseWithoutChangingPhysicalBoneOrder()
    {
        var root = Synthetic();
        foreach (var asset in root["assets"]!.AsArray())
            foreach (var track in asset!["tracks"]!.AsArray())
                track!["bone"] = track["bone"]!.GetValue<string>().ToUpperInvariant();
        var tables = Compile(root.ToJsonString());
        for (var source = 0; source < 2; source++)
            for (var bone = 0; bone < 68; bone++)
                Assert.Equal(new Vector3(bone, -2, 3) * .01f, tables[source].GetKey(0)[bone].Position);
    }

    [Fact]
    public void RetainsRawQuaternionBitsAndSignWithoutNormalizationOrMatrixRoundTrip()
    {
        var root = Synthetic(); var track = root["assets"]![0]!["tracks"]![0]!;
        float[] raw = [.703233003616333f, .07391838729381561f, -.7032334804534912f, .0739060640335083f];
        track["rotations"] = Channel(raw);
        var bone = BoneIndex(track); var output = Compile(root.ToJsonString())[0].GetKey(0)[bone].Rotation;
        Assert.Equal(BitConverter.SingleToInt32Bits(-raw[0]), BitConverter.SingleToInt32Bits(output.X));
        Assert.Equal(BitConverter.SingleToInt32Bits(raw[1]), BitConverter.SingleToInt32Bits(output.Y));
        Assert.Equal(BitConverter.SingleToInt32Bits(-raw[2]), BitConverter.SingleToInt32Bits(output.Z));
        Assert.Equal(BitConverter.SingleToInt32Bits(raw[3]), BitConverter.SingleToInt32Bits(output.W));
        // A valid approximately-unit source must also retain its authored magnitude.
        track["rotations"] = Channel([0, 0, 0, -1.00001f]);
        Assert.Equal(-1.00001f, Compile(root.ToJsonString())[0].GetKey(0)[bone].Rotation.W);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RepeatsSingletonChannelsAndUsesNativeOneForMissingScale(bool emptyScale)
    {
        var root = Synthetic(); var track = root["assets"]![0]!["tracks"]![0]!; var bone = BoneIndex(track);
        track["positions"] = Channel([10, 20, 30]); track["rotations"] = Channel([0, 0, 0, -1]);
        track["scales"] = emptyScale ? new JsonArray() : Channel([2, 3, 4]);
        var table = Compile(root.ToJsonString())[0];
        Assert.Equal(table.GetKey(0)[bone], table.GetKey(1)[bone]);
        Assert.Equal(emptyScale ? Vector3.One : new Vector3(2, 3, 4), table.GetKey(0)[bone].Scale);
    }

    [Theory]
    [InlineData("schema")] [InlineData("provenance")] [InlineData("oracle-field")]
    [InlineData("asset-source")] [InlineData("duplicate-asset")] [InlineData("skeleton")]
    [InlineData("rate")] [InlineData("denominator")] [InlineData("key-count")] [InlineData("duration")]
    [InlineData("missing-track")] [InlineData("duplicate-track")] [InlineData("duplicate-track-case")] [InlineData("foreign-track")]
    [InlineData("virtual-track")] [InlineData("empty-position")] [InlineData("empty-rotation")]
    [InlineData("too-many-keys")] [InlineData("position-shape")] [InlineData("rotation-shape")]
    [InlineData("scale-shape")] [InlineData("position-overflow")] [InlineData("rotation-overflow")]
    [InlineData("scale-overflow")] [InlineData("zero-quaternion")] [InlineData("nonunit-quaternion")]
    [InlineData("quaternion-string")]
    public void RejectsForeignOrIncompleteSourceDataBeforePublishingTables(string mutation)
    {
        var root = Synthetic(); var asset = root["assets"]![0]!; var tracks = asset["tracks"]!.AsArray(); var track = tracks[0]!;
        switch (mutation)
        {
            case "schema": root["schemaVersion"] = 2; break;
            case "provenance": root["source"] = "AnimPoseExtensions.get_anim_pose_at_time"; break;
            case "oracle-field": asset["rawPoseWithoutRetarget"] = new JsonArray(); break;
            case "asset-source": asset["source"] = "/OtherPose"; break;
            case "duplicate-asset": root["assets"]![1] = asset.DeepClone(); break;
            case "skeleton": asset["skeletonSource"] = "/OtherSkeleton"; break;
            case "rate": asset["frameRateNumerator"] = 60; break;
            case "denominator": asset["frameRateDenominator"] = 0; break;
            case "key-count": asset["sampledKeyCount"] = 1; break;
            case "duration": asset["playLength"] = .05; break;
            case "missing-track": tracks.RemoveAt(0); break;
            case "duplicate-track": tracks[1] = track.DeepClone(); break;
            case "duplicate-track-case": tracks[1]!["bone"] = track["bone"]!.GetValue<string>().ToUpperInvariant(); break;
            case "foreign-track": track["bone"] = "ForeignBone"; break;
            case "virtual-track": track["bone"] = "VB Curves"; break;
            case "empty-position": track["positions"] = new JsonArray(); break;
            case "empty-rotation": track["rotations"] = new JsonArray(); break;
            case "too-many-keys": track["scales"] = Channel([1, 1, 1], [1, 1, 1], [1, 1, 1]); break;
            case "position-shape": track["positions"] = Channel([1, 2]); break;
            case "rotation-shape": track["rotations"] = Channel([0, 0, 1]); break;
            case "scale-shape": track["scales"] = Channel([1, 1, 1, 1]); break;
            case "position-overflow": track["positions"]![0]![0] = 1e100; break;
            case "rotation-overflow": track["rotations"]![0]![0] = 1e100; break;
            case "scale-overflow": track["scales"]![0]![0] = 1e100; break;
            case "zero-quaternion": track["rotations"] = Channel([0, 0, 0, 0]); break;
            case "nonunit-quaternion": track["rotations"] = Channel([0, 0, 0, 2]); break;
            case "quaternion-string": track["rotations"]![0]![0] = "NaN"; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.ThrowsAny<Exception>(() => Compile(root.ToJsonString()));
    }

    [Theory]
    [InlineData("asset-id")] [InlineData("skeleton")] [InlineData("additive")]
    [InlineData("root-lock")] [InlineData("interpolation")] [InlineData("rate")]
    public void RejectsManifestChanges(string mutation)
    {
        var original = Set.Value; var set = original with { Animations = (AlsAnimationDefinition[])original.Animations.Clone() };
        var index = Definition.Value.Evaluators[0].AnimationId; var asset = set.Animations[index];
        set.Animations[index] = mutation switch
        {
            "asset-id" => asset with { StableId = "ForeignAsset" },
            "skeleton" => asset with { SkeletonId = int.MaxValue },
            "additive" => asset with { AdditiveType = 1 },
            "root-lock" => asset with { ForceRootLock = true },
            "interpolation" => asset with { Interpolation = 1 },
            "rate" => asset with { FrameRateNumerator = 60 },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        Assert.ThrowsAny<Exception>(() => AlsBasePoseSourceKeysCompiler.Compile(Synthetic().ToJsonString(), set, Definition.Value));
    }

    [Fact]
    public void ActualOriginalKeysMatchNativeUnretargetedPhysicalAtomsIncludingNearSingularIkFootRotation()
    {
        var tables = Compile(Read("assets/config/v4_base_pose_source_keys.json"));
        using var native = JsonDocument.Parse(Read("tests/Als.Core.Tests/Fixtures/P3/v4_logical_pose_native.json"));
        var skeleton = Set.Value.Skeletons[Definition.Value.SkeletonId];
        foreach (var table in tables)
        {
            var asset = native.RootElement.GetProperty("assets").EnumerateArray()
                .Single(a => a.GetProperty("source").GetString() == table.Evaluator.AssetPath);
            var raw = asset.GetProperty("rawPoseWithoutRetarget");
            for (var physical = 0; physical < table.PhysicalBoneCount; physical++)
            {
                var nativeAtom = raw[skeleton.PhysicalToLogical[physical]];
                var expected = ConvertNative(nativeAtom); var actual = table.GetKey(0)[physical];
                Assert.InRange(Vector3.Distance(expected.Position, actual.Position), 0, .000002f);
                Assert.InRange(MathF.Min((expected.Rotation - actual.Rotation).Length(), (expected.Rotation + actual.Rotation).Length()), 0, .000002f);
                Assert.InRange(Vector3.Distance(expected.Scale, actual.Scale), 0, .000002f);
                // The native integer-key path promotes FQuat4f components to FQuat
                // without normalizing. Independently require exact binary32 values
                // through the native source API, JSON, and direct Y reflection.
                var nativeRotation = nativeAtom.GetProperty("rotation");
                Assert.Equal(BitConverter.SingleToInt32Bits(-nativeRotation[0].GetSingle()), BitConverter.SingleToInt32Bits(actual.Rotation.X));
                Assert.Equal(BitConverter.SingleToInt32Bits(nativeRotation[1].GetSingle()), BitConverter.SingleToInt32Bits(actual.Rotation.Y));
                Assert.Equal(BitConverter.SingleToInt32Bits(-nativeRotation[2].GetSingle()), BitConverter.SingleToInt32Bits(actual.Rotation.Z));
                Assert.Equal(BitConverter.SingleToInt32Bits(nativeRotation[3].GetSingle()), BitConverter.SingleToInt32Bits(actual.Rotation.W));
            }
        }
        var ik = Array.FindIndex(skeleton.PhysicalBones, b => b.Name == "ik_foot_r");
        var exact = tables[0].GetKey(0)[ik].Rotation;
        var lossyFbx = new Quaternion(.7031496573f, -.0747070625f, -.7031501344f, -.0746947304f);
        Assert.InRange(MathF.Min((exact - lossyFbx).Length(), (exact + lossyFbx).Length()), .0011f, .0012f);
    }

    private static JsonNode Synthetic()
    {
        var assets = new JsonArray(); var skeleton = Set.Value.Skeletons[Definition.Value.SkeletonId];
        foreach (var evaluator in Definition.Value.Evaluators)
        {
            var tracks = new JsonArray();
            for (var physical = 67; physical >= 0; physical--)
                tracks.Add(new JsonObject
                {
                    ["bone"] = skeleton.PhysicalBones[physical].Name,
                    ["positions"] = Channel([physical, 2, 3], [physical + 100, 4, 5]),
                    ["rotations"] = Channel([0, 0, 0, 1]), ["scales"] = Channel([2, 3, 4])
                });
            assets.Add(new JsonObject
            {
                ["source"] = evaluator.AssetPath, ["skeletonSource"] = skeleton.ObjectPath,
                ["frameRateNumerator"] = 30, ["frameRateDenominator"] = 1, ["sampledKeyCount"] = 2,
                ["playLength"] = 1d / 30, ["tracks"] = tracks
            });
        }
        return new JsonObject { ["schemaVersion"] = 1, ["source"] = "AnimDataModel.BoneAnimationTracks.InternalTrackData", ["assets"] = assets };
    }
    private static JsonArray Channel(params float[][] keys) => new(keys.Select(values =>
        (JsonNode)new JsonArray(values.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray())).ToArray());
    private static int BoneIndex(JsonNode track) => Array.FindIndex(Set.Value.Skeletons[Definition.Value.SkeletonId].PhysicalBones,
        b => b.Name == track["bone"]!.GetValue<string>());
    private static AlsLocalPose ConvertNative(JsonElement atom)
    {
        var p = atom.GetProperty("position"); var q = atom.GetProperty("rotation"); var s = atom.GetProperty("scale");
        return AlsFbxBonePoseSpace.FromCanonical(new(AlsCoordinateConverter.PositionCentimetersToMeters(new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())),
            AlsCoordinateConverter.Rotation(new(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle())),
            AlsCoordinateConverter.Scale(new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()))));
    }
    private static AlsSourcePoseKeyTable[] Compile(string json) => AlsBasePoseSourceKeysCompiler.Compile(json, Set.Value, Definition.Value);
    private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), path));
}
