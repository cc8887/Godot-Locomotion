using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Animation;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace Als.P5aOracle;

internal static class Program
{
    private const string RuntimeMarkerPrefix =
        "P5A_ORACLE_DIGESTS layout=f2336240d749284b bindings=40f33e59692dfd38 graph=44403c2869d8f615 plan=";

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            throw new ArgumentException("Exactly one P5A Oracle mode is required.");
        }

        return args[0] switch
        {
            "--write-build-manifest" => RunBuildManifest(args),
            "--write-native-plan" => RunWriteNativePlan(args),
            "--write-canonical-pair" => RunWriteCanonicalPair(args),
            "--verify-fixture" => RunVerifyFixture(args),
            _ => throw new ArgumentException($"Unknown P5A Oracle mode: {args[0]}"),
        };
    }

    private static int RunBuildManifest(string[] args)
    {
        RequireExactArguments(args, 7,
            "--write-build-manifest", "--repository-root", "--output", "--sdk-version");
        WriteBuildManifest(AbsoluteDirectory(args[2], "repository root"),
            AbsoluteOutput(args[4]), args[6]);
        return 0;
    }

    private static int RunWriteNativePlan(string[] args)
    {
        RequireExactArguments(args, 5, "--write-native-plan", "--repository-root", "--output");
        var root = AbsoluteDirectory(args[2], "repository root");
        var output = AbsoluteOutput(args[4]);
        var snapshot = CompileSnapshot(root);
        var bytes = P5aPlanBuilder.Build(snapshot);
        AtomicWriteNew(output, bytes);
        Console.WriteLine(RuntimeMarkerPrefix + LowerSha256(bytes));
        return 0;
    }

    private static int RunWriteCanonicalPair(string[] args)
    {
        RequireExactArguments(args, 11, "--write-canonical-pair", "--repository-root",
            "--trace-plan", "--raw", "--native-canonical", "--port-canonical");
        var root = AbsoluteDirectory(args[2], "repository root");
        var plan = AbsoluteInput(args[4], "trace plan");
        var raw = AbsoluteInput(args[6], "raw trace");
        var native = AbsoluteOutput(args[8]);
        var port = AbsoluteOutput(args[10]);
        var snapshot = CompileSnapshot(root);
        HandleWriteCanonicalPair(raw, plan, native, port, snapshot);
        Console.WriteLine(RuntimeMarkerPrefix + LowerSha256(File.ReadAllBytes(plan)));
        return 0;
    }

    private static int RunVerifyFixture(string[] args)
    {
        RequireExactArguments(args, 5, "--verify-fixture", "--repository-root", "--fixture");
        var root = AbsoluteDirectory(args[2], "repository root");
        var fixture = AbsoluteInput(args[4], "fixture");
        var stagingRoot = RequireVerifierStagingRoot(root);
        var plan = Path.Combine(stagingRoot, $"p5a-verify-{Guid.NewGuid():N}.json");
        var snapshot = CompileSnapshot(root);
        var bytes = P5aPlanBuilder.Build(snapshot);
        try
        {
            AtomicWriteNew(plan, bytes);
            HandleVerifyFixture(fixture, plan, snapshot);
            Console.WriteLine(RuntimeMarkerPrefix + LowerSha256(bytes));
            return 0;
        }
        finally
        {
            try
            {
                if (File.Exists(plan))
                {
                    File.Delete(plan);
                }
            }
            catch
            {
            }
        }
    }

    private static void HandleWriteCanonicalPair(
        string raw,
        string plan,
        string native,
        string port,
        AlsP5CoreRuntimeBindingSnapshot snapshot) =>
        WriteCanonicalPairCore(raw, plan, native, port, snapshot);

    private static void WriteCanonicalPairCore(
        string raw,
        string plan,
        string native,
        string port,
        AlsP5CoreRuntimeBindingSnapshot snapshot)
    {
        var layout = snapshot.CreateOccurrenceLayoutView();
        var bindings = snapshot.CreateCoreView();
        AlsP5aTrace.WriteCanonicalPair(raw, plan, native, port, in layout, in bindings, snapshot.GraphDigest);
    }

    private static void HandleVerifyFixture(
        string fixture,
        string plan,
        AlsP5CoreRuntimeBindingSnapshot snapshot) =>
        VerifyFixtureCore(fixture, plan, snapshot);

    private static void VerifyFixtureCore(
        string fixture,
        string plan,
        AlsP5CoreRuntimeBindingSnapshot snapshot)
    {
        var layout = snapshot.CreateOccurrenceLayoutView();
        var bindings = snapshot.CreateCoreView();
        AlsP5aTrace.VerifyFixture(fixture, plan, in layout, in bindings, snapshot.GraphDigest);
    }

    private static AlsP5CoreRuntimeBindingSnapshot CompileSnapshot(string root)
    {
        var manifest = AlsManifestSerializer.Load(Path.Combine(
            root, "assets", "generated", "als_v4", "als_manifest.json"));
        var animationSet = AlsAnimationSetCompiler.Compile(manifest);
        var locomotion = AlsLocomotionProfileCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "assets", "config", "p3_locomotion_profile.json")),
            animationSet);
        var pose = AlsPoseProfileCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "assets", "config", "p4_pose_profile.json")),
            animationSet, locomotion);
        var p5a = AlsP5aAnimationRuntimeProfileCompiler.Compile(
            File.ReadAllText(Path.Combine(root, "assets", "config", "p5a_animation_runtime.json")),
            animationSet);
        var layout = AlsP5OccurrenceLayoutCompiler.Compile(locomotion, pose, p5a);
        return AlsP5CoreRuntimeBindingCompiler.Compile(
            animationSet, locomotion, pose, p5a, layout);
    }

    private static string RequireVerifierStagingRoot(string repositoryRoot)
    {
        var value = Environment.GetEnvironmentVariable("GODOTALS_P5A_STAGING_ROOT");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "GODOTALS_P5A_STAGING_ROOT is required for fixture verification.");
        }
        if (!Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException("GODOTALS_P5A_STAGING_ROOT must be an absolute verifier staging directory.");
        }
        var full = Path.GetFullPath(value);
        var repository = Path.GetFullPath(repositoryRoot) + Path.DirectorySeparatorChar;
        if (full.Equals(Path.GetFullPath(repositoryRoot), StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(repository, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "GODOTALS_P5A_STAGING_ROOT has unsafe repository containment; it must be outside the repository.");
        }
        if (!Directory.Exists(full))
        {
            throw new InvalidOperationException(
                "GODOTALS_P5A_STAGING_ROOT must identify an existing verifier staging directory.");
        }
        if (Directory.EnumerateFileSystemEntries(full).Any())
        {
            throw new InvalidOperationException(
                "GODOTALS_P5A_STAGING_ROOT verifier staging directory is non-empty.");
        }
        return full;
    }

    private static void RequireExactArguments(string[] args, int count, params string[] names)
    {
        if (args.Length != count)
        {
            throw new ArgumentException("P5A Oracle arguments do not match the selected mode.");
        }
        for (var index = 0; index < names.Length; index++)
        {
            var argumentIndex = index == 0 ? 0 : index * 2 - 1;
            if (args[argumentIndex] != names[index])
            {
                throw new ArgumentException($"P5A Oracle expected argument {names[index]} at position {argumentIndex}.");
            }
        }
    }

    private static string AbsoluteDirectory(string path, string label)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"P5A Oracle {label} must be absolute.");
        }
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"P5A Oracle {label} does not exist: {full}");
        }
        return full;
    }

    private static string AbsoluteInput(string path, string label)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"P5A Oracle {label} path must be absolute.");
        }
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"P5A Oracle {label} does not exist.", full);
        }
        return full;
    }

    private static string AbsoluteOutput(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("P5A Oracle output path must be absolute.");
        }
        return Path.GetFullPath(path);
    }

    private static void AtomicWriteNew(string path, byte[] bytes)
    {
        var parent = Path.GetDirectoryName(path) ??
            throw new ArgumentException("P5A Oracle output has no parent directory.");
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException($"P5A Oracle output directory does not exist: {parent}");
        }
        var temporary = Path.Combine(parent, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void WriteBuildManifest(string root, string output, string sdkVersion)
    {
        var sourcePaths = ClosedSourcePaths(root);
        var sourceHash = SourceTreeHash(root, sourcePaths);
        var outputDirectory = Path.GetDirectoryName(output) ??
            throw new ArgumentException("Build manifest output has no parent directory.");
        var lines = new[]
        {
            "p5a_oracle_build_v1",
            "configuration=Release",
            "targetFramework=net8.0",
            $"sdkVersion={sdkVersion}",
            $"sourceTreeSha256={sourceHash}",
            $"executableSha256={FileSha256(Path.Combine(outputDirectory, "Als.P5aOracle.exe"))}",
            $"oracleAssemblySha256={FileSha256(Path.Combine(outputDirectory, "Als.P5aOracle.dll"))}",
            $"importAssemblySha256={FileSha256(Path.Combine(outputDirectory, "Als.Import.dll"))}",
            $"coreAssemblySha256={FileSha256(Path.Combine(outputDirectory, "Als.Core.dll"))}",
            $"depsSha256={FileSha256(Path.Combine(outputDirectory, "Als.P5aOracle.deps.json"))}",
            $"runtimeConfigSha256={FileSha256(Path.Combine(outputDirectory, "Als.P5aOracle.runtimeconfig.json"))}",
        };
        var bytes = new UTF8Encoding(false).GetBytes(string.Join('\n', lines) + "\n");
        var temporary = output + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string[] ClosedSourcePaths(string root)
    {
        var paths = new List<string>
        {
            ".editorconfig", "global.json", "Directory.Build.props",
            "tools/Als.P5aOracle/Als.P5aOracle.csproj", "tools/Als.P5aOracle/Program.cs",
            "src/Als.Import/Als.Import.csproj", "src/Als.Core/Als.Core.csproj",
        };
        AddSourceTree(paths, root, "src/Als.Import");
        AddSourceTree(paths, root, "src/Als.Core");
        paths.Sort(StringComparer.Ordinal);
        return paths.ToArray();
    }

    private static void AddSourceTree(List<string> paths, string root, string relativeDirectory)
    {
        var directory = Path.Combine(root, relativeDirectory);
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var segments = relative.Split('/');
            if (!segments.Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                                         segment.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            {
                paths.Add(relative);
            }
        }
    }

    private static string SourceTreeHash(string root, IEnumerable<string> relativePaths)
    {
        using var stream = new MemoryStream();
        foreach (var relative in relativePaths.Order(StringComparer.Ordinal))
        {
            var normalized = relative.Replace('\\', '/');
            var pathBytes = Encoding.UTF8.GetBytes(normalized);
            var hashBytes = Encoding.ASCII.GetBytes(FileSha256(Path.Combine(root, relative)));
            stream.Write(pathBytes);
            stream.WriteByte(0);
            stream.Write(hashBytes);
            stream.WriteByte((byte)'\n');
        }
        return LowerSha256(stream.ToArray());
    }

    private static string FileSha256(string path) => LowerSha256(File.ReadAllBytes(path));

    private static string LowerSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal static class P5aPlanBuilder
{
    internal static byte[] Build(AlsP5CoreRuntimeBindingSnapshot snapshot)
    {
        if (snapshot.AnimationSetDefinitionDigest !=
            "152e79130c55ebd7f13cd3efbe40a30c21d52c81af863ab1e1926f2da86b5129" ||
            snapshot.LayoutDigest != 0xf2336240d749284bUL ||
            snapshot.Version != 2 || snapshot.Digest != 0x40f33e59692dfd38UL ||
            snapshot.GraphDigest != 0x44403c2869d8f615UL)
        {
            throw new InvalidDataException("P5A snapshot does not match the frozen native plan inputs.");
        }
        var bytes = AlsP5aTrace.BuildNativePlan();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (bytes.Length != 986747 ||
            hash != "7909b939803f60a657adf19867f379cb6fbc93c8001df1c37e05f3a603aa6f0b")
        {
            throw new InvalidDataException(
                $"P5A frozen plan bytes are not canonical (length={bytes.Length}, sha256={hash}).");
        }
        return bytes;
    }
}
