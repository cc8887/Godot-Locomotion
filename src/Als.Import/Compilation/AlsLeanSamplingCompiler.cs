using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public sealed record AlsLeanSamplingProfile(int SkeletonId, int PlayerId, int SampleStart,
    int BaseAnimationId, AlsLeanBlendSpace Runtime);

public static class AlsLeanSamplingCompiler
{
    public static AlsLeanSamplingProfile Compile(string json, AlsLocomotionSourceProfile sources,
        AlsAnimationSetDefinition set, AlsLocomotionSourceDomain domain)
        => CompileNative(json, sources, set, domain, null);

    public static AlsLeanSamplingProfile CompileFalling(string json, AlsLocomotionSourceProfile sources,
        AlsAnimationSetDefinition set, int compiledNodeIndex)
    {
        Require(compiledNodeIndex is 260 or 282, "Falling Lean requires its Fall or Jump node identity.");
        return CompileNative(json, sources, set, AlsLocomotionSourceDomain.MainMovement, compiledNodeIndex);
    }

    private static AlsLeanSamplingProfile CompileNative(string json, AlsLocomotionSourceProfile sources,
        AlsAnimationSetDefinition set, AlsLocomotionSourceDomain domain, int? compiledNodeIndex)
    {
        var falling = compiledNodeIndex.HasValue; var divisions = falling ? 2 : 4;
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            Text(root, "source") == (falling ? "UE UBlendSpace::FilterInput + UpdateBlendSamples; ALS V4 Falling Lean" :
                "UE UBlendSpace::FilterInput + UpdateBlendSamples; ALS V4 Lean"), "Wrong native Lean inventory.");
        var spaces = set.BlendSpaces.Where(s => s.ObjectPath == Text(root, "objectPath")).ToArray();
        Require(spaces.Length == 1, "Missing or ambiguous Lean asset."); var space = spaces[0];
        Require(space.Name == (falling ? "ALS_N_Lean_Falling" : "ALS_N_Lean") && space.Samples.Length == 5,
            "Wrong Lean asset/sample identity.");
        var sourceSamples = sources.Samples;
        var players = sources.Players.Where(p => p.Domain == domain && p.Kind == AlsLocomotionSourceKind.BlendSpace &&
            (!compiledNodeIndex.HasValue || p.CompiledNodeIndex == compiledNodeIndex.Value) &&
            p.SampleCount == 5 && sourceSamples[p.SampleStart].AnimationId == space.Samples[0].AnimationId).ToArray();
        Require(players.Length == 1, "Missing or ambiguous Lean player identity."); var player = players[0];
        Require(root.GetProperty("grid").GetBoolean() && root.GetProperty("sampleWeightSpeed").GetSingle() == 0 &&
            !root.GetProperty("meshSpaceSamples").GetBoolean() && !root.GetProperty("allowMeshSpace").GetBoolean() &&
            Text(root, "PerBoneBlendMode") == "ManualPerBoneOverride" && Text(root, "ManualPerBoneOverrides") == "" &&
            Text(root, "PerBoneBlendProfile") == "()" &&
            Text(root, "AxisToScaleAnimation") == "BSA_None" && root.GetProperty("notifyMode").GetInt32() == 1,
            "Unsupported Lean interpolation/notification policy.");
        var axes = root.GetProperty("axes"); Require(axes.GetArrayLength() == 2, "Incomplete Lean axes.");
        for (var i = 0; i < 2; i++)
        {
            var axis = axes[i]; var parameter = space.Parameters[i];
            Require(Text(axis, "name") == (i == 0 ? "LR" : "FB") && Text(axis, "name") == parameter.Name &&
                axis.GetProperty("min").GetSingle() == -1 && parameter.Minimum == -1 &&
                axis.GetProperty("max").GetSingle() == 1 && parameter.Maximum == 1 &&
                axis.GetProperty("divisions").GetInt32() == divisions && parameter.GridDivisions == divisions &&
                !axis.GetProperty("wrap").GetBoolean() && axis.GetProperty("seconds").GetSingle() == 0 &&
                axis.GetProperty("mode").GetInt32() == 5 && axis.GetProperty("damping").GetSingle() == 1 &&
                axis.GetProperty("maxSpeed").GetSingle() == 0, "Unsupported Lean axis filter/range.");
        }
        var samples = root.GetProperty("samples"); Require(samples.GetArrayLength() == 5, "Incomplete Lean samples.");
        var baseId = -1;
        for (var i = 0; i < 5; i++)
        {
            var native = samples[i]; var source = sourceSamples[player.SampleStart + i]; var animation = set.Animations[source.AnimationId];
            Require(native.GetProperty("index").GetInt32() == i && source.SourceIndex == i &&
                space.Samples[i].AnimationId == animation.Id && Text(native, "path") == animation.ObjectPath &&
                native.GetProperty("x").GetSingle() == source.X && native.GetProperty("y").GetSingle() == source.Y &&
                native.GetProperty("rate").GetSingle() == source.SampleRateScale && native.GetProperty("assetRate").GetSingle() == source.AssetRateScale &&
                native.GetProperty("length").GetSingle() == source.DurationSeconds && animation.SkeletonId == sources.SkeletonId &&
                native.GetProperty("additiveType").GetInt32() == 1 && animation.AdditiveType == 1 &&
                native.GetProperty("baseType").GetInt32() == 3 && animation.AdditiveBasePoseType == 3 &&
                native.GetProperty("baseFrame").GetInt32() == 0 && animation.AdditiveBasePoseFrame == 0 &&
                source.AdditiveBaseAnimationId == animation.AdditiveBasePoseAnimationId && source.AdditiveBaseAnimationId >= 0,
                "Lean additive source metadata differs.");
            if (i == 0) baseId = source.AdditiveBaseAnimationId;
            Require(source.AdditiveBaseAnimationId == baseId && Text(native, "basePath") == set.Animations[baseId].ObjectPath,
                "Lean additive samples have a different reference pose.");
        }
        var width = divisions + 1; var pointCount = width * width; var step = 2d / divisions;
        var grid = root.GetProperty("gridSamples"); Require(grid.GetArrayLength() == pointCount, "Incomplete Lean grid.");
        var vertices = new AlsLeanGridVertex[pointCount * 3];
        for (var i = 0; i < pointCount; i++)
        {
            var point = grid[i]; var indices = point.GetProperty("indices"); var weights = point.GetProperty("weights");
            Require(point.GetProperty("x").GetDouble() == -1 + i % width * step && point.GetProperty("y").GetDouble() == -1 + i / width * step &&
                indices.GetArrayLength() == 3 && weights.GetArrayLength() == 3, "Wrong Lean grid order/layout.");
            for (var v = 0; v < 3; v++) vertices[i * 3 + v] = new(indices[v].GetInt32(), weights[v].GetSingle());
        }
        try { return new(sources.SkeletonId, player.PlayerId, player.SampleStart, baseId, new(vertices, divisions)); }
        catch (ArgumentException error) { throw new FormatException("Invalid native Lean grid.", error); }
    }
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static void Require(bool condition, string message) { if (!condition) throw new FormatException(message); }
}
