using System.Security.Cryptography;
using System.Text;

namespace GodotAls.Import.Manifest;

public static class AlsStableAssetId
{
    public static string Create(string objectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectPath);
        if (!objectPath.StartsWith("/Game/", StringComparison.Ordinal) ||
            objectPath.Contains('\\') ||
            !HasCanonicalAssetSuffix(objectPath))
        {
            throw new ArgumentException("UE object path must be a canonical /Game path.", nameof(objectPath));
        }

        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(objectPath))).ToLowerInvariant();
    }

    private static bool HasCanonicalAssetSuffix(string objectPath)
    {
        var slashIndex = objectPath.LastIndexOf('/');
        var dotIndex = objectPath.LastIndexOf('.');
        return dotIndex > slashIndex + 1 && dotIndex < objectPath.Length - 1;
    }
}
