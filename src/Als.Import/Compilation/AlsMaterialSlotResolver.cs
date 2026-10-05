using System.Globalization;

namespace GodotAls.Import.Compilation;

public static class AlsMaterialSlotResolver
{
    public static int Resolve(
        IReadOnlyList<string> materialNames,
        IReadOnlyList<int> allowedMaterialIds,
        string importedName)
    {
        ArgumentNullException.ThrowIfNull(materialNames);
        ArgumentNullException.ThrowIfNull(allowedMaterialIds);

        if (string.IsNullOrWhiteSpace(importedName))
        {
            return -1;
        }

        var match = ResolveExact(materialNames, allowedMaterialIds, importedName, out var ambiguous);
        if (match >= 0 || ambiguous)
        {
            return match;
        }

        var suffixSeparator = importedName.LastIndexOf('_');
        if (suffixSeparator <= 0 || suffixSeparator == importedName.Length - 1 ||
            !int.TryParse(importedName.AsSpan(suffixSeparator + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out _))
        {
            return -1;
        }

        return ResolveExact(materialNames, allowedMaterialIds, importedName[..suffixSeparator], out _);
    }

    private static int ResolveExact(
        IReadOnlyList<string> materialNames,
        IReadOnlyList<int> allowedMaterialIds,
        string importedName,
        out bool ambiguous)
    {
        ambiguous = false;
        var match = -1;
        foreach (var materialId in allowedMaterialIds)
        {
            if ((uint)materialId >= (uint)materialNames.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(allowedMaterialIds),
                    $"Material ID {materialId} is outside the compiled material table.");
            }

            if (!string.Equals(materialNames[materialId], importedName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match >= 0 && match != materialId)
            {
                ambiguous = true;
                return -1;
            }

            match = materialId;
        }

        return match;
    }
}
