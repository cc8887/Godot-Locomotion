namespace GodotAls.Import.Tests;

internal static class RepositoryRoot
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (File.Exists(Path.Combine(directory.FullName, "GodotALS.sln")) &&
                (Directory.Exists(gitPath) || File.Exists(gitPath)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Godot ALS repository root.");
    }
}
