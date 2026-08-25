using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public static class AlsAnimationTrackPathContract
{
    private const string Prefix = "Skeleton3D:";

    public static string GetBoneName(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith(Prefix, StringComparison.Ordinal) ||
            path.Length == Prefix.Length)
        {
            throw Invalid(path);
        }

        var boneName = path[Prefix.Length..];
        if (boneName.Contains(':', StringComparison.Ordinal) ||
            boneName.Contains('/', StringComparison.Ordinal))
        {
            throw Invalid(path);
        }

        return boneName;
    }

    private static AlsCompilationException Invalid(string? path) => new([
        new AlsValidationIssue(
            "ALSANIMATION001",
            null,
            "$.trackPath",
            "Animation track path does not match the fixed Godot importer contract.",
            "Skeleton3D:<bone>",
            path ?? "<null>"),
    ]);
}
