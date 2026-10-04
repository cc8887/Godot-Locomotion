using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Import;
using System.Text.RegularExpressions;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraWeaponSkinCandidate(LyraWeaponModelBinding Owner,long Frame,ulong Tick,Transform3D Transform,
    Transform3D TargetTransform,AlsLocalPose[] Pose);

// The FBX importer may reorder sibling bones. Bind by source bone identity,
// verify every parent, and leave the imported skin's bind matrices in place.
internal sealed class LyraWeaponModelBinding:IDisposable
{
    private readonly Node3D _actor;
    private readonly int[] _bones;
    private readonly int _logicalCount;
    private readonly Transform3D _skeletonToModel;
    private LyraWeaponSkinCandidate? _pending;
    private long _frame=-1;
    private bool _disposed;
    public Node3D Model {get;}
    public Skeleton3D Skeleton {get;}
    public int PublishedFrames {get;private set;}
    public LyraWeaponModelBinding(Node3D actor,LyraWeaponResources resources,string kind)
    {
        _actor=actor;var mesh=resources.Mesh(kind);var metadata=mesh.GetProperty("metadata");
        var names=metadata.GetProperty("logicalBoneNames").EnumerateArray().Select(p=>p.GetString()!).ToArray();
        _logicalCount=names.Length;
        var parents=metadata.GetProperty("logicalParents").EnumerateArray().Select(p=>p.GetInt32()).ToArray();
        Model=ResourceLoader.Load<PackedScene>(LyraWeaponResources.Root+mesh.GetProperty("file").GetString())?.Instantiate<Node3D>()
            ??throw new InvalidOperationException("Missing original imported weapon model.");
        try
        {
            Skeleton=AlsImportedResourceAuditor.FindFirst<Skeleton3D>(Model)??throw new InvalidOperationException("Weapon mesh has no skeleton.");
            // SK_Rifle and SKM_Shotgun omit the skeleton's unused Slide bone.
            // Read the physical identity from the hashed original mesh export,
            // rather than treating every skeleton bone as a skin bone.
            var fbx=System.Text.Encoding.UTF8.GetString(Godot.FileAccess.GetFileAsBytes(LyraWeaponResources.Root+mesh.GetProperty("file").GetString()));
            var physical=Regex.Matches(fbx,"Model: [0-9]+, \"Model::([^\"]+)\", \"(?:Root|LimbNode)\"")
                .Select(m=>m.Groups[1].Value).ToArray();
            if(physical.Length!=Skeleton.GetBoneCount()||physical.Distinct().Count()!=physical.Length||physical.Any(n=>!names.Contains(n)))
                throw new InvalidOperationException("Original weapon mesh/skeleton identities differ.");
            var logicalToSkin=names.Select(Skeleton.FindBone).ToArray();
            _bones=Enumerable.Range(0,Skeleton.GetBoneCount()).Select(b=>Array.IndexOf(names,Skeleton.GetBoneName(b).ToString())).ToArray();
            if(_bones.Any(b=>b<0)||_bones.Distinct().Count()!=physical.Length||physical.Any(n=>Skeleton.FindBone(n)<0))
                throw new InvalidOperationException("Imported weapon bone names differ from original FBX.");
            foreach(int b in _bones)
            {
                if(parents[b]>=0&&logicalToSkin[parents[b]]<0)throw new NotSupportedException("A physical weapon parent is missing from its mesh.");
                if(Skeleton.GetBoneParent(logicalToSkin[b])!=(parents[b]<0?-1:logicalToSkin[parents[b]]))throw new InvalidOperationException("Imported weapon parent differs: "+names[b]);
            }
            Disable(Model);actor.AddChild(Model);_skeletonToModel=SkeletonRelativeToModel();
        }
        catch {Model.Free();throw;}
    }
    private static void Disable(Node node)
    {if(node is AnimationMixer mixer)mixer.Active=false;if(node is SkeletonModifier3D)throw new NotSupportedException("Weapon has another skeleton writer.");foreach(var child in node.GetChildren())Disable(child);}
    public LyraWeaponSkinCandidate Stage(long frame,ReadOnlySpan<AlsPrecisePose> source,AlsPrecisePose? nativeMeshWorld=null)
    {
        Live();if(_pending is not null||frame<=_frame||source.Length!=_logicalCount)throw new InvalidOperationException("Invalid weapon skin frame.");
        var pose=new AlsLocalPose[_bones.Length];
        foreach(var atom in source)atom.Validate();
        for(int b=0;b<_bones.Length;b++)pose[b]=LyraAlsCharacterBinding.Convert(source[_bones[b]]);
        var target=nativeMeshWorld is {} world
            ?WorldTransform(world)*new Transform3D(new Basis(Vector3.Down,Vector3.Back,Vector3.Left).Inverse(),Vector3.Zero)*_skeletonToModel.AffineInverse()
            :Model.GlobalTransform;
        if(!target.IsFinite()||Math.Abs(target.Basis.Determinant())<1e-8f)throw new ArgumentException("Invalid weapon model target.");
        return _pending=new(this,frame,Engine.GetPhysicsFrames(),Model.GlobalTransform,target,pose);
    }
    public void ValidatePublish(LyraWeaponSkinCandidate candidate)
    {
        Live();if(!ReferenceEquals(candidate,_pending)||!ReferenceEquals(candidate.Owner,this)||candidate.Tick!=Engine.GetPhysicsFrames()||
            candidate.Transform!=Model.GlobalTransform||_skeletonToModel!=SkeletonRelativeToModel())
            throw new InvalidOperationException("Stale, foreign or changed weapon skin candidate.");
    }
    public void Publish(LyraWeaponSkinCandidate candidate)
    {ValidatePublish(candidate);Model.GlobalTransform=candidate.TargetTransform;LyraUnarmedAimOffset.WritePose(Skeleton,candidate.Pose);_frame=candidate.Frame;_pending=null;PublishedFrames++;}
    internal static Transform3D WorldTransform(in AlsPrecisePose native)
    {
        native.Validate();var q=native.Rotation;var scale=native.Scale;
        if(scale.X<=0||scale.Y<=0||scale.Z<=0)throw new ArgumentException("Weapon requires a positive world scale.");
        return new(new Basis(new Quaternion((float)-q.Y,(float)-q.Z,(float)q.X,(float)q.W))
            .ScaledLocal(new((float)scale.Y,(float)scale.Z,(float)scale.X)),LyraGodotRigCollision.Position(native.Position));
    }
    private Transform3D SkeletonRelativeToModel()
    {
        var result=Skeleton.Transform;
        for(Node? parent=Skeleton.GetParent();parent!=Model;parent=parent?.GetParent())
        {
            if(parent is not Node3D transform)throw new InvalidOperationException("Changed weapon skeleton hierarchy.");
            result=transform.Transform*result;
        }
        return result;
    }
    public void Cancel()=>_pending=null;
    private void Live()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(!GodotThread.IsMainThread()||!Engine.IsInPhysicsFrame()||!GodotObject.IsInstanceValid(_actor)||!_actor.IsInsideTree()||
            !GodotObject.IsInstanceValid(Model)||!Model.IsInsideTree()||Model.IsQueuedForDeletion()||Model.GetParent()!=_actor||
            !GodotObject.IsInstanceValid(Skeleton)||!Model.IsAncestorOf(Skeleton))throw new InvalidOperationException("Weapon binding is no longer live.");
    }
    public void Dispose(){if(_disposed)return;Cancel();_disposed=true;if(GodotObject.IsInstanceValid(Model))Model.Free();}
}
