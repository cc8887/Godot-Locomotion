using Godot;
using GodotAls.Import.Compilation;
using GodotAls.Import.Manifest;

namespace GodotAls.Import;

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

    public int ApplyToScene(PackedScene scene, AlsManifestAsset sourceAsset)
    {
        var root = scene.Instantiate();
        try
        {
            return ApplyToRoot(root, sourceAsset);
        }
        finally
        {
            root.Free();
        }
    }

    public int ApplyToRoot(Node root, AlsManifestAsset sourceAsset)
    {
        var dependencyMaterials = sourceAsset.Dependencies
            .Select(TryGetMaterialId)
            .Where(value => value >= 0)
            .ToArray();
        var applied = 0;
        foreach (var mesh in FindAll<MeshInstance3D>(root))
        {
            if (mesh.Mesh is null)
            {
                continue;
            }

            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var importedName = mesh.GetActiveMaterial(surface)?.ResourceName ?? string.Empty;
                var materialId = FindByName(importedName);
                if (materialId < 0 && dependencyMaterials.Length != 0)
                {
                    materialId = dependencyMaterials[Math.Min(surface, dependencyMaterials.Length - 1)];
                }

                if (materialId >= 0)
                {
                    mesh.SetSurfaceOverrideMaterial(surface, GetMaterial(materialId));
                    applied++;
                }
            }
        }

        return applied;
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

    private int TryGetMaterialId(string stableId)
    {
        try
        {
            return _definition.AssetIndex.GetMaterialId(stableId);
        }
        catch (KeyNotFoundException)
        {
            return -1;
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
