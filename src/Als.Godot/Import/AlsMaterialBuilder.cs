using Godot;
using GodotAls.Import.Compilation;

namespace GodotAls.Import;

public sealed record AlsMaterialDiagnostic(
    string Severity,
    string Code,
    string AssetId,
    string MeshName,
    int SurfaceIndex,
    string ImportedMaterialName,
    string SelectedMaterialName);

public sealed record AlsMaterialApplyReport(
    int AppliedCount,
    int SurfaceCount,
    int UnresolvedCount,
    IReadOnlyList<AlsMaterialDiagnostic> Diagnostics);

public sealed class AlsMaterialBuilder
{
    private readonly AlsAnimationSetDefinition _definition;
    private readonly StandardMaterial3D?[] _materials;

    public AlsMaterialBuilder(AlsAnimationSetDefinition definition)
    {
        _definition = definition;
        _materials = new StandardMaterial3D?[definition.Materials.Length];
    }

    public StandardMaterial3D GetMaterial(int materialId)
    {
        var cached = _materials[materialId];
        if (cached is not null)
        {
            return cached;
        }

        var source = _definition.Materials[materialId];
        var material = source.ParentMaterialId >= 0
            ? (StandardMaterial3D)GetMaterial(source.ParentMaterialId).Duplicate(true)
            : new StandardMaterial3D();
        material.ResourceName = source.Name;
        ApplyParameters(material, source);
        _materials[materialId] = material;
        return material;
    }

    public AlsMaterialApplyReport ApplyToScene(
        PackedScene scene,
        string assetId,
        IReadOnlyList<int> materialIds)
    {
        var root = scene.Instantiate();
        try
        {
            return ApplyToRoot(root, assetId, materialIds);
        }
        finally
        {
            root.Free();
        }
    }

    public AlsMaterialApplyReport ApplyToRoot(
        Node root,
        string assetId,
        IReadOnlyList<int> materialIds)
    {
        var diagnostics = new List<AlsMaterialDiagnostic>();
        var applied = 0;
        var surfaceCount = 0;
        foreach (var mesh in FindAll<MeshInstance3D>(root))
        {
            if (mesh.Mesh is null)
            {
                continue;
            }

            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                surfaceCount++;
                var importedName = mesh.GetActiveMaterial(surface)?.ResourceName ?? string.Empty;
                var materialId = FindByName(importedName);
                if (materialId < 0 && surface < materialIds.Count)
                {
                    materialId = materialIds[surface];
                    diagnostics.Add(new AlsMaterialDiagnostic(
                        "warning",
                        "ALSMATERIAL001",
                        assetId,
                        mesh.Name,
                        surface,
                        importedName,
                        _definition.Materials[materialId].Name));
                }

                if (materialId >= 0)
                {
                    mesh.SetSurfaceOverrideMaterial(surface, GetMaterial(materialId));
                    applied++;
                }
                else
                {
                    diagnostics.Add(new AlsMaterialDiagnostic(
                        "error",
                        "ALSMATERIAL002",
                        assetId,
                        mesh.Name,
                        surface,
                        importedName,
                        string.Empty));
                }
            }
        }

        return new AlsMaterialApplyReport(
            applied,
            surfaceCount,
            diagnostics.Count(value => value.Severity == "error"),
            diagnostics);
    }

    private void ApplyParameters(StandardMaterial3D material, AlsMaterialDefinition source)
    {
        foreach (var parameter in source.ScalarParameterOverrides)
        {
            if (parameter.Name.Contains("roughness", StringComparison.OrdinalIgnoreCase))
            {
                material.Roughness = Math.Clamp(parameter.Value, 0f, 1f);
            }
            else if (parameter.Name.Contains("metallic", StringComparison.OrdinalIgnoreCase))
            {
                material.Metallic = Math.Clamp(parameter.Value, 0f, 1f);
            }
        }

        var color = source.VectorParameterOverrides.FirstOrDefault(value =>
            value.Name.Contains("color", StringComparison.OrdinalIgnoreCase) ||
            value.Name.Contains("tint", StringComparison.OrdinalIgnoreCase));
        if (color is not null)
        {
            material.AlbedoColor = new Color(color.Value[0], color.Value[1], color.Value[2], color.Value[3]);
        }

        var textureId = source.TextureParameterOverrides.FirstOrDefault()?.TextureId
            ?? source.ReferencedTextureIds.FirstOrDefault(-1);
        if (textureId >= 0)
        {
            var texturePath = AlsImportedResourceAuditor.ToResourcePath(_definition.Textures[textureId].ResourcePath);
            material.AlbedoTexture = ResourceLoader.Load<Texture2D>(texturePath);
        }
    }

    private int FindByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return -1;
        }

        for (var index = 0; index < _definition.Materials.Length; index++)
        {
            if (string.Equals(_definition.Materials[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static IEnumerable<T> FindAll<T>(Node root) where T : Node
    {
        if (root is T match)
        {
            yield return match;
        }

        foreach (var child in root.GetChildren())
        {
            foreach (var descendant in FindAll<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
