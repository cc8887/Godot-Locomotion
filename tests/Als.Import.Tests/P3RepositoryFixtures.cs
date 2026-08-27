using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import.Tests;

internal static class P3RepositoryFixtures
{
    public static AlsAnimationSetDefinition LoadAnimationSet()
    {
        var path = Path.Combine(
            RepositoryRoot.Find(), "assets", "generated", "als_v4", "als_manifest.json");
        return AlsAnimationSetCompiler.Compile(AlsManifestSerializer.Load(path));
    }

    public static string ReadProfile() => File.ReadAllText(ProfilePath());

    public static string ProfilePath() => Path.Combine(
        RepositoryRoot.Find(), "assets", "config", "p3_locomotion_profile.json");

    public static string WithMissingJumpClip() => Mutate(root =>
        root["jumpStart"] = "ffffffffffffffffffffffffffffffffffffffff");

    public static string WithUnknownProperty() => Mutate(root => root["fallback"] = "idle");

    public static string WithMissingProperty(string propertyName) => Mutate(root => root.Remove(propertyName));

    public static string WithDuplicateStableId() => Mutate(root =>
        root["crouchingIdle"] = root["standingIdle"]!.DeepClone());

    public static string WithEmptyGrid(string propertyName) => Mutate(root =>
        root[propertyName] = new JsonArray());

    public static string WithUnknownSampleProperty() => Mutate(root =>
        root["standingSamples"]![0]!["fallback"] = 0);

    public static string WithInvalidSampleNumber() => Mutate(root =>
        root["standingSamples"]![0]!["x"] = 1e100);

    private static string Mutate(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(ReadProfile())!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }
}
