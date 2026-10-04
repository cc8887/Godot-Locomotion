using Godot;
using GodotAls.Assets;
using GodotAls.Core.Locomotion;
using GodotAls.Import;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraAlsSkinCandidate(LyraAlsCharacterBinding Owner, long Frame,
    ulong PhysicsTick, Transform3D Component, AlsLocalPose[] Skin);

// One model writer, after the complete logical Main pose. The imported FBX
// uses (X,-Y,Z) local axes; the physics provider uses (Y,Z,-X) Godot axes.
// A proper rotation joins those bases without changing the original skin.
internal sealed class LyraAlsCharacterBinding : IDisposable
{
    private readonly LyraLogicalSourceBank _bank;
    private readonly Node3D _actor;
    private readonly int[] _bones;
    private LyraAlsSkinCandidate? _pending;
    private long _frame;
    public Node3D Model { get; }
    public Skeleton3D Skeleton { get; }
    public Node3D Component { get; }
    public int PublishedFrames { get; private set; }
    public ReadOnlySpan<int> Bones => _bones;

    public LyraAlsCharacterBinding(Node3D actor, LyraLogicalSourceBank bank)
    {
        if (!actor.IsInsideTree()) throw new ArgumentException("ALS model needs its live actor.");
        _actor = actor; _bank = bank;
        var resource = ResourceLoader.Load<AlsAnimationSetResource>(AlsGodotImportCoordinator.CompiledResourcePath)
            ?? throw new InvalidOperationException("Compiled ALS asset set is missing.");
        var set = resource.LoadDefinition();
        var mesh = set.SkeletalMeshes.Single(m => m.ObjectPath ==
            "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin");
        var scene = ResourceLoader.Load<PackedScene>(AlsImportedResourceAuditor.ToResourcePath(mesh.ResourcePath))
            ?? throw new InvalidOperationException("ALS mannequin scene is missing.");
        Model = scene.Instantiate<Node3D>();
        try
        {
            Skeleton = AlsImportedResourceAuditor.FindFirst<Skeleton3D>(Model)
                ?? throw new InvalidOperationException("ALS mannequin has no skeleton.");
            AlsAnimationBinder.ValidateTargetSkeleton(Skeleton, set.Skeletons[mesh.SkeletonId], "Lyra Main");
            if (Skeleton.GetBoneCount() != 68 || bank.SkinLogicalIndices.Length != 68)
                throw new NotSupportedException("Changed ALS physical skin.");
            _bones = bank.SkinLogicalIndices.ToArray();
            for (int skin = 0; skin < 68; skin++)
                if (bank.Bone(Skeleton.GetBoneName(skin).ToString()) != _bones[skin] ||
                    Skeleton.GetBoneParent(skin) != bank.Parents[_bones[skin]])
                    throw new InvalidOperationException("ALS physical/logical bone identity differs.");
            DisableAnimation(Model);
            // The ALS root tracks move forward along local +Y. The imported
            // FBX and Rig basis currently place component +X behind the actor;
            // this quarter turn gives the mesh UE's -90 degree relative yaw,
            // so +Y faces the capsule's forward direction. Warping must consume
            // that real component rotation instead of compensating the mount.
            Model.RotateY(-Mathf.Pi / 2);
            actor.AddChild(Model);
            Component = new Node3D { Name = "LyraRigComponent", Basis = new(Vector3.Down, Vector3.Back, Vector3.Left) };
            Skeleton.AddChild(Component);
        }
        catch { Model.Free(); throw; }
    }
    private static void DisableAnimation(Node node)
    {
        if (node is AnimationMixer mixer) mixer.Active = false;
        if (node is SkeletonModifier3D) throw new NotSupportedException("ALS model has another skeleton writer.");
        foreach (var child in node.GetChildren()) DisableAnimation(child);
    }
    internal static AlsLocalPose Convert(in AlsPrecisePose pose) => new(
        new((float)(pose.Position.X * .01), (float)(-pose.Position.Y * .01), (float)(pose.Position.Z * .01)),
        new((float)-pose.Rotation.X, (float)pose.Rotation.Y, (float)-pose.Rotation.Z, (float)pose.Rotation.W), pose.Scale.ToSingle());
    public LyraAlsSkinCandidate Stage(long frame, ReadOnlySpan<AlsPrecisePose> pose)
    {
        Live();
        if (_pending is not null || frame <= _frame || pose.Length != 81)
            throw new InvalidOperationException("Invalid ALS skin frame.");
        var skin = new AlsLocalPose[68];
        for (int b = 0; b < 81; b++) pose[b].Validate(.001);
        for (int b = 0; b < skin.Length; b++)
        {
            skin[b] = Convert(pose[_bones[b]]);
            static bool Finite(System.Numerics.Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
            if (!Finite(skin[b].Position) || !Finite(skin[b].Scale) ||
                !float.IsFinite(skin[b].Rotation.X) || !float.IsFinite(skin[b].Rotation.Y) ||
                !float.IsFinite(skin[b].Rotation.Z) || !float.IsFinite(skin[b].Rotation.W))
                throw new InvalidOperationException("Pose exceeds Godot model precision.");
        }
        return _pending = new(this, frame, Engine.GetPhysicsFrames(), Component.GlobalTransform, skin);
    }
    public void ValidatePublish(LyraAlsSkinCandidate candidate)
    {
        Live();
        if (!ReferenceEquals(candidate, _pending) || !ReferenceEquals(candidate.Owner, this) ||
            candidate.PhysicsTick != Engine.GetPhysicsFrames() || candidate.Component != Component.GlobalTransform)
            throw new InvalidOperationException("Stale or changed ALS skin candidate.");
    }
    public void Publish(LyraAlsSkinCandidate candidate)
    {
        ValidatePublish(candidate);
        LyraUnarmedAimOffset.WritePose(Skeleton, candidate.Skin);
        _frame = candidate.Frame; _pending = null; PublishedFrames++;
    }
    public void Cancel() => _pending = null;
    private void Live()
    {
        if (!GodotThread.IsMainThread() || !Engine.IsInPhysicsFrame() ||
            !GodotObject.IsInstanceValid(_actor) || !GodotObject.IsInstanceValid(Model) ||
            !GodotObject.IsInstanceValid(Skeleton) || !GodotObject.IsInstanceValid(Component) ||
            !_actor.IsInsideTree() || !Model.IsInsideTree() || !Skeleton.IsInsideTree() || !Component.IsInsideTree() ||
            Model.GetParent()!=_actor || !Model.IsAncestorOf(Skeleton) || Component.GetParent()!=Skeleton ||
            _actor.IsQueuedForDeletion() || Model.IsQueuedForDeletion() || Skeleton.IsQueuedForDeletion() ||
            Component.IsQueuedForDeletion() || Skeleton.GetBoneCount() != 68)
            throw new InvalidOperationException("ALS skin belongs to its live actor in the main physics phase.");
    }
    public void Dispose() { Cancel(); if (GodotObject.IsInstanceValid(Model)) Model.Free(); }
}
