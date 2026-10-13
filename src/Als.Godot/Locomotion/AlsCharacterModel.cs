using System.Text.Json;
using Godot;

namespace GodotAls.Locomotion;

internal static class AlsCharacterModel
{
    internal static string? SelectDemoScene()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(
            "res://assets/config/als_character_model.json"));
        var config = document.RootElement;
        var overrides = OS.GetCmdlineUserArgs().Where(a => a.StartsWith("--als-model=", StringComparison.Ordinal)).ToArray();
        if (overrides.Length > 1) throw new ArgumentException("Select one ALS character model.");
        var name = overrides.Length == 0 ? config.GetProperty("defaultModel").GetString()! : overrides[0][12..];
        return SelectScene(config, name);
    }

    internal static string ConfiguredScene(string name)
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString(
            "res://assets/config/als_character_model.json"));
        return SelectScene(document.RootElement, name)
            ?? throw new ArgumentException("Configured presentation requires a model scene.");
    }

    private static string? SelectScene(JsonElement config, string name)
    {
        if (config.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported ALS character model configuration.");
        if (name == "mannequin") return null;
        if (!config.GetProperty("models").TryGetProperty(name, out var entry))
            throw new ArgumentException("Unknown ALS character model: " + name);
        var path = entry.GetString()!;
        if (!path.StartsWith("res://", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid ALS character model scene.");
        return path;
    }

    // Replace presentation resources before the rig is warmed or assigned to a worker.
    internal static AlsCharacterModelBinding? Install(Node targetRoot, Skeleton3D target, string? scenePath)
    {
        if (scenePath is null) return null;
        using var packed = ResourceLoader.Load<PackedScene>(scenePath)
            ?? throw new InvalidOperationException("ALS character model scene is missing: " + scenePath);
        var model = packed.Instantiate();
        try
        {
            var nodes = Descendants(model).ToArray();
            var skeletons = nodes.OfType<Skeleton3D>().ToArray();
            if (skeletons.Length != 1) throw new InvalidOperationException("Character model requires one skeleton.");
            var source = skeletons[0];
            if (nodes.Any(node => node is AnimationPlayer or AnimationTree or SkeletonModifier3D))
                throw new InvalidOperationException("Character presentation must use the existing animation owner.");
            var retarget = source.HasMeta("als_pose_binding") &&
                source.GetMeta("als_pose_binding").AsString() == "preserve-proportions-v1";
            ValidateSkeleton(source, target, retarget);
            var meshes = nodes.OfType<MeshInstance3D>().ToArray();
            using var skeletonPath = new NodePath("..");
            if (meshes.Length == 0 || meshes.Any(m => m.GetParent() != source || m.Mesh is null || m.Skin is null || m.Skeleton != skeletonPath))
                throw new InvalidOperationException("Character model requires directly bound skeletal meshes.");
            foreach (var mesh in meshes)
            {
                for (var bind = 0; bind < mesh.Skin.GetBindCount(); bind++)
                {
                    using var name = mesh.Skin.GetBindName(bind);
                    var bone = name.IsEmpty ? mesh.Skin.GetBindBone(bind) : target.FindBone(name);
                    if (bone < 0 || bone >= target.GetBoneCount())
                        throw new InvalidOperationException("Character model has an unresolved skin binding.");
                }
            }
            // Capture source skin before replacing it, then measure the target
            // through the same observer after attaching its verified skeleton.
            var originalSoles = retarget ? new AlsSoleGeometryObserver(targetRoot, referenceOnly: true) : null;
            var sourceLeft = originalSoles?.ReferenceContactMarker(target, true) ?? default;
            var sourceRight = originalSoles?.ReferenceContactMarker(target, false) ?? default;
            AlsCharacterModelBinding? binding = null;
            foreach (var mesh in Descendants(targetRoot).OfType<MeshInstance3D>().ToArray()) mesh.Free();
            if (retarget)
            {
                source.GetParent().RemoveChild(source);
                source.Owner = null;
                source.Name = "AlsModelSkeleton";
                source.Transform = target.Transform;
                target.GetParent().AddChild(source);
                var targetSoles = new AlsSoleGeometryObserver(source.GetParent(), referenceOnly: true);
                source.SetMeta("als_retarget_foot_markers", new Godot.Collections.Dictionary<string, Vector3>
                {
                    ["source_l"] = sourceLeft, ["source_r"] = sourceRight,
                    ["target_l"] = targetSoles.ReferenceContactMarker(source, true),
                    ["target_r"] = targetSoles.ReferenceContactMarker(source, false)
                });
                binding = new AlsCharacterModelBinding(target, source);
            }
            else
                foreach (var mesh in meshes)
                {
                    source.RemoveChild(mesh);
                    mesh.Owner = null;
                    target.AddChild(mesh);
                }
            GD.Print($"ALS_CHARACTER_MODEL_SELECTED scene={scenePath} bones={target.GetBoneCount()} meshes={meshes.Length} retarget={retarget}");
            return binding;
        }
        finally { model.Free(); }
    }

    internal static void ValidateSkeleton(Skeleton3D source, Skeleton3D target, bool retarget = false)
    {
        if (source.GetBoneCount() != target.GetBoneCount())
            throw new InvalidOperationException("Character model bone count differs from ALS.");
        for (var bone = 0; bone < target.GetBoneCount(); bone++)
        {
            if (source.GetBoneName(bone) != target.GetBoneName(bone) ||
                source.GetBoneParent(bone) != target.GetBoneParent(bone) ||
                (retarget ?
                    !source.GetBoneRest(bone).Basis.IsEqualApprox(target.GetBoneRest(bone).Basis) :
                    !source.GetBoneRest(bone).IsEqualApprox(target.GetBoneRest(bone))))
                throw new InvalidOperationException("Character model reference skeleton differs from ALS at bone " + bone);
        }
    }

    internal static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
