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
                return -1;
            }

            match = materialId;
        }

        return match;
    }
}
