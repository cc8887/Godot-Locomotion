using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed class AlsAimSamplingProfile
{
    private readonly int[] _animations;
    public int SkeletonId { get; }
    public int BaseAnimationId { get; }
    public string BindingDigest { get; }
    public ReadOnlySpan<int> AnimationIds => _animations;
    public AlsAimBlendSpace Runtime { get; }
    public const int PlayerCount = 7;
    public const int SampleCount = 17; // Two sequences and five independent three-sample evaluators.
    internal AlsAimSamplingProfile(int skeleton, int baseAnimation, int[] animations, string digest, AlsAimBlendSpace runtime)
    { SkeletonId = skeleton; BaseAnimationId = baseAnimation; _animations = (int[])animations.Clone(); BindingDigest = digest; Runtime = runtime; }
}

public static class AlsAimSamplingCompiler
{
    private const string RootPath = "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/";
    public static AlsAimSamplingProfile Compile(string json, AlsAimPoseDefinition graph, AlsAnimationSetDefinition set)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var path = RootPath + "AimOffsets/ALS_N_Look.ALS_N_Look";
        Require(Int(root, "schemaVersion") == 1 && Text(root, "source") == "UE UBlendSpace::FilterInput + UpdateBlendSamples; ALS V4 Aim" &&
            Text(root, "objectPath") == path, "Wrong Aim sampling provenance.");
        Require(Bool(root, "grid") && Scalar(root, "sampleWeightSpeed") == 0 && !Bool(root, "sampleWeightEase") &&
            Bool(root, "meshSpaceSamples") && !Bool(root, "allowMeshSpace") && !Bool(root, "scaleAnimation") && Int(root, "notifyMode") == 1 &&
            Text(root, "PerBoneBlendMode") == "ManualPerBoneOverride" && Text(root, "ManualPerBoneOverrides") == "" &&
            Text(root, "PerBoneBlendProfile") == "()" && Text(root, "AxisToScaleAnimation") == "BSA_None", "Unsupported Aim sampling policy.");
        var spaces = set.BlendSpaces.Concat(set.AimOffsets).Where(b => b.ObjectPath == path).ToArray();
        Require(spaces.Length == 1, "Missing Aim BlendSpace asset."); var space = spaces[0];
        var axes = root.GetProperty("axes"); Require(axes.GetArrayLength() == 1, "Aim must be one-dimensional."); var axis = axes[0];
        Require(Text(axis, "name") == "Pitch" && Scalar(axis, "min") == -90 && Scalar(axis, "max") == 90 && Int(axis, "divisions") == 4 &&
            !Bool(axis, "wrap") && Int(axis, "mode") == 5 && Scalar(axis, "seconds") == 0 && Scalar(axis, "damping") == 1 && Scalar(axis, "maxSpeed") == 0,
            "Aim range or filter changed.");
        Require(space.Parameters.Length >= 1 && space.Parameters[0] == new AlsBlendParameterDefinition("Pitch", -90, 90, 4) &&
            space.Samples.Length == 3, "Aim manifest axis/sample layout differs.");
        var rows = root.GetProperty("samples"); Require(rows.GetArrayLength() == 3, "Incomplete Aim sample closure.");
        var animations = new int[3]; var skeleton = -1; var baseAnimation = -1;
        string[] directions = ["F", "D", "U"]; float[] positions = [0, -90, 90];
        for (var i = 0; i < 3; i++)
        {
            var row = rows[i]; var sample = space.Samples[i]; var animation = set.Animations[sample.AnimationId];
            var expected = RootPath + "AimOffsets/ALS_N_Look_" + directions[i] + "_Sweep.ALS_N_Look_" + directions[i] + "_Sweep";
            Require(Int(row, "index") == i && Text(row, "path") == expected && animation.ObjectPath == expected &&
                Scalar(row, "x") == positions[i] && Scalar(row, "y") == 0 && sample.SampleValue.Length >= 2 &&
                sample.SampleValue[0] == positions[i] && sample.SampleValue[1] == 0 && Scalar(row, "rate") == 1 && sample.RateScale == 1 &&
                Scalar(row, "assetRate") == 1 && Scalar(row, "length") == 1 && animation.PlayLength == 1 && Int(row, "additiveType") == 2 && animation.AdditiveType == 2 &&
                Int(row, "baseType") == 3 && animation.AdditiveBasePoseType == 3 && Int(row, "baseFrame") == 0 && animation.AdditiveBasePoseFrame == 0 &&
                animation.AdditiveBasePoseAnimationId >= 0, "Aim mesh-space additive source differs.");
            if (i == 0) { skeleton = animation.SkeletonId; baseAnimation = animation.AdditiveBasePoseAnimationId; }
            Require(animation.SkeletonId == skeleton && animation.AdditiveBasePoseAnimationId == baseAnimation &&
                Text(row, "basePath") == set.Animations[baseAnimation].ObjectPath &&
                Text(row, "basePath") == RootPath + "Base/BasePoses/ALS_N_Pose.ALS_N_Pose", "Aim source reference differs.");
            animations[i] = animation.Id;
        }
        Require(graph.Evaluators.Length == AlsAimSamplingProfile.PlayerCount &&
            graph.Evaluators.ToArray().All(e => e.Source == (e.BlendSpace ? path : set.Animations[animations[0]].ObjectPath)), "Aim evaluator source closure changed.");
        var grid = root.GetProperty("gridSamples"); Require(grid.GetArrayLength() == 5, "Incomplete Aim grid.");
        var vertices = new AlsAimGridVertex[15];
        for (var point = 0; point < 5; point++)
        {
            var row = grid[point]; var indices = row.GetProperty("indices"); var weights = row.GetProperty("weights");
            Require(Scalar(row, "x") == -90 + point * 45 && Scalar(row, "y") == 0 && indices.GetArrayLength() == 3 && weights.GetArrayLength() == 3,
                "Aim grid order or layout changed.");
            for (var vertex = 0; vertex < 3; vertex++) vertices[point * 3 + vertex] = new(indices[vertex].GetInt32(), weights[vertex].GetSingle());
        }
        var runtime = new AlsAimBlendSpace(vertices);
        var staticRows = root.GetProperty("staticSamples"); Require(staticRows.GetArrayLength() == 253, "Missing Aim static oracle.");
        foreach (var row in staticRows.EnumerateArray()) Verify(row);
        var runs = root.GetProperty("runs"); Require(runs.GetArrayLength() == 3, "Missing Aim filter trajectories.");
        for (var run = 0; run < 3; run++)
        {
            var hz = 30 << run; Require(Int(runs[run], "hz") == hz && runs[run].GetProperty("frames").GetArrayLength() == hz * 4, "Wrong Aim native trajectory.");
            foreach (var row in runs[run].GetProperty("frames").EnumerateArray())
            { Require(Scalar(row, "delta") == 1f / hz, "Aim native delta differs."); Verify(row); }
        }
        var identity = graph.RootIndex + ":" + string.Join(',', graph.Evaluators.ToArray().Select(e => e.CompiledIndex));
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity + "\n" + json)));
        return new(skeleton, baseAnimation, animations, digest, runtime);

        void Verify(JsonElement row)
        {
            // No smoothing: the native filter must return the unmodified input.
            Require(Scalar(row, "x") == Scalar(row, "filteredX") && Scalar(row, "y") == 0 && Scalar(row, "filteredY") == 0, "Aim filter changed input.");
            Span<float> sampled = stackalloc float[3]; Span<int> sampledOrder = stackalloc int[3];
            var count = runtime.Evaluate(Scalar(row, "filteredX"), sampled, sampledOrder);
            var expected = row.GetProperty("weights"); var expectedOrder = row.GetProperty("order");
            Require(expected.GetArrayLength() == 3 && expectedOrder.GetArrayLength() == count, "Aim native sample count differs.");
            for (var i = 0; i < 3; i++) Require(MathF.Abs(sampled[i] - expected[i].GetSingle()) <= 2e-6f, "Aim native weight differs.");
            for (var i = 0; i < count; i++) Require(sampledOrder[i] == expectedOrder[i].GetInt32(), "Aim native sample order differs.");
        }
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static int Int(JsonElement value, string name) => value.GetProperty(name).GetInt32();
    private static float Scalar(JsonElement value, string name) => value.GetProperty(name).GetSingle();
    private static bool Bool(JsonElement value, string name) => value.GetProperty(name).GetBoolean();
    private static void Require(bool valid, string message) { if (!valid) throw new FormatException(message); }
}
