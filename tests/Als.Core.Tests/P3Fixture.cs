namespace GodotAls.Core.Tests;

internal static class P3Fixture
{
    public static string Path(string fileName) => System.IO.Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "P3",
        fileName);

    public static string RepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "GodotALS.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate the GodotALS repository root.");
        }

        return parts.Aggregate(directory.FullName, System.IO.Path.Combine);
    }
}
