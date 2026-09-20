using Godot;
using GodotAls.Core.Contracts;
using GodotAls.Import;

namespace GodotAls.Locomotion;

// Main owns the prop scene instances. Worker only supplies a completed value snapshot.
internal sealed class AlsOverlayPropRuntime : IDisposable
{
    private sealed record Entry(Node3D Root, Node3D Component, Transform3D ImportInverse, Skeleton3D? Skeleton, int[] Bones);
    private readonly Node3D _root = new() { Name = "HeldObjectRoot", ProcessThreadGroup = Node.ProcessThreadGroupEnum.MainThread };
    private readonly Entry?[] _entries = new Entry?[13];
    private readonly int _mainThread;
    private Entry? _visible;
    internal AlsOverlayPropFrame Committed { get; private set; }
    internal Node3D? VisibleComponent => _visible?.Component;
    internal Skeleton3D? VisibleSkeleton => _visible?.Skeleton;
    internal int InstanceCount { get; }

    public AlsOverlayPropRuntime(AlsP3Character owner, AlsP3RuntimeContext context)
    {
        _mainThread = context.MainManagedThreadId; EnsureMain(); owner.AddChild(_root);
        var profile = context.PropProfile!; var set = context.AnimationSet;
        var materials = new AlsMaterialBuilder(set);
        var cache = new Dictionary<(bool, int), Entry>();
        try
        {
            for (var i = 0; i < _entries.Length; i++)
            {
                var binding = profile.Get((GodotAls.Core.Locomotion.AlsOverlayKind)i);
                if (!binding.HasProp) continue;
                if (!cache.TryGetValue((binding.Skeletal, binding.MeshId), out var entry))
                {
                    var mesh = binding.Skeletal ? set.SkeletalMeshes[binding.MeshId] : null;
                    var staticMesh = binding.Skeletal ? null : set.StaticMeshes[binding.MeshId];
                    var model = ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(mesh?.ResourcePath ?? staticMesh!.ResourcePath)).Instantiate<Node3D>();
                    model.Visible = false; _root.AddChild(model);
                    var report = materials.ApplyToRoot(model, mesh?.StableId ?? staticMesh!.StableId, mesh?.MaterialIds ?? staticMesh!.MaterialIds);
                    if (report.UnresolvedCount != 0) throw new InvalidOperationException("Unresolved prop material.");
                    foreach (var node in Descendants(model))
                        if (node is AnimationPlayer player) player.Active = false;
                    var skeleton = binding.Skeletal ? Descendants(model).OfType<Skeleton3D>().Single() : null;
                    Node3D component = skeleton is not null ? skeleton : Descendants(model).OfType<MeshInstance3D>().Single();
                    // Remove the actual FBX scene-axis conversion once. Socket and raw keys
                    // already use FBX bone space; retaining that conversion rotates the prop twice.
                    var relative = model.GlobalTransform.AffineInverse() * component.GlobalTransform;
                    var bones = Array.Empty<int>();
                    if (binding.AnimationId >= 0)
                    {
                        var source = context.PropSources!.GetSource(binding.AnimationId);
                        var rawSkeleton = context.PropSources.GetSkeleton(source.PoseData.Identity.SkeletonId);
                        bones = rawSkeleton.LogicalBoneNames.ToArray().Select(name => skeleton!.FindBone(name)).ToArray();
                        if (bones.Any(b => b < 0) || bones.Distinct().Count() != skeleton!.GetBoneCount())
                            throw new InvalidOperationException("Prop skeleton differs from the native animation source.");
                    }
                    entry = new(model, component, relative.AffineInverse(), skeleton, bones);
                    cache.Add((binding.Skeletal, binding.MeshId), entry);
                }
                _entries[i] = entry;
            }
            InstanceCount = cache.Count;
        }
        catch { _root.Free(); throw; }
    }
    internal void Validate(in AlsOverlayPropFrame frame, in AlsFrameIdentity identity)
    {
        EnsureMain();
        if (frame.Identity != identity || (uint)frame.Overlay >= _entries.Length || frame.Identity.FrameId <= Committed.Identity.FrameId)
            throw new InvalidOperationException("Stale or foreign prop publication.");
        var entry = _entries[(int)frame.Overlay];
        if (frame.BoneCount != (entry?.Bones.Length ?? 0)) throw new InvalidOperationException("Incomplete prop pose.");
        AlsP3Presentation.ThrowIfNonFinite(frame.Attachment);
    }
    internal void Commit(in AlsOverlayPropFrame frame)
    {
        EnsureMain();
        var next = _entries[(int)frame.Overlay];
        if (_visible != next) { if (_visible is not null) _visible.Root.Visible = false; _visible = next; }
        if (next is not null)
        {
            next.Root.GlobalTransform = frame.Attachment * next.ImportInverse;
            for (var i = 0; i < frame.BoneCount; i++)
            {
                var pose = frame.Pose[i]; var bone = next.Bones[i];
                next.Skeleton!.SetBonePosePosition(bone, new(pose.Position.X, pose.Position.Y, pose.Position.Z));
                next.Skeleton.SetBonePoseRotation(bone, new(pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W));
                next.Skeleton.SetBonePoseScale(bone, new(pose.Scale.X, pose.Scale.Y, pose.Scale.Z));
            }
            next.Root.Visible = true;
        }
        Committed = frame;
    }
    internal void Clear()
    { EnsureMain(); if (_visible is not null) _visible.Root.Visible = false; _visible = null; Committed = default; }
    public void Dispose() { Clear(); if (GodotObject.IsInstanceValid(_root)) _root.Free(); }
    private void EnsureMain()
    { if (System.Environment.CurrentManagedThreadId != _mainThread) throw new InvalidOperationException("Prop nodes belong to Main."); }
    private static IEnumerable<Node> Descendants(Node node)
    { yield return node; foreach (var child in node.GetChildren()) foreach (var item in Descendants(child)) yield return item; }
}
