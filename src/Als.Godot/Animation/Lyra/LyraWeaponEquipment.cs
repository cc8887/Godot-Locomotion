using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraEquippedWeaponActor:IDisposable
{
    public Node3D Actor {get;}
    public LyraWeaponModelBinding Model {get;}
    public LyraWeaponMontageBank? Bank {get;private set;}
    public LyraWeaponEquipmentDefinition Definition {get;}
    public uint Generation {get;}
    private readonly uint _character;
    private readonly List<AlsMontageActionRequest> _requests=[];
    private bool _retired;
    public bool Live=>!_retired&&GodotObject.IsInstanceValid(Actor)&&Actor.IsInsideTree()&&!Actor.IsQueuedForDeletion();
    public int PendingRequests=>_requests.Count;
    public long LastTick {get;private set;}=-1;
    public LyraWeaponMontageCandidate? LastPose {get;private set;}
    public LyraEquippedWeaponActor(Node3D owner,LyraWeaponResources resources,string kind,uint character,uint generation)
    {
        _character=character;Generation=generation;Definition=new(kind);Actor=new Node3D{Name="LyraWeapon"+generation};owner.AddChild(Actor);
        try
        {
            Bank=new(new(resources,kind),character,generation);Model=new(Actor,resources,kind);Model.Model.Visible=false;
            LyraWeaponResourcesSmoke.PreviewMaterial(Model.Model,kind);
        }
        catch{Bank?.Dispose();Actor.Free();throw;}
    }
    public bool Receive(string montage,float rate)
    {
        if(!Live||Bank is null)return false;
        int asset=Bank.Catalog.Paths.IndexOf(montage);
        if(asset<0)return false; // Original Montage_Play rejects another skeleton.
        if(!float.IsFinite(rate))throw new ArgumentException("Nonfinite weapon notify rate.");
        _requests.Add(new(asset,rate));return true;
    }
    // This is the attached component's independent tick after the character
    // has published and dispatched its committed notifications. No zero-delta
    // pseudo frame or second character clock is used to deliver requests.
    public void Tick(long frame,float delta,in AlsPrecisePose meshWorld)
    {
        if(!Live||Bank is null)return;
        var candidate=Bank.Prepare(new(frame,_character,Generation),delta,beforeAdvance:_requests.ToArray());
        try
        {
            var skin=Model.Stage(frame,candidate.Pose,meshWorld);Bank.Validate(candidate);Model.ValidatePublish(skin);
            Bank.Commit(candidate);Model.Publish(skin);_requests.Clear();LastTick=frame;LastPose=candidate;Model.Model.Visible=true;
        }
        catch{Bank.Cancel();Model.Cancel();throw;}
    }
    internal void RemoveAnimationInstance(){Bank?.Dispose();Bank=null;_requests.Clear();}
    public void Dispose(){if(_retired)return;_retired=true;_requests.Clear();Bank?.Dispose();Model.Dispose();if(GodotObject.IsInstanceValid(Actor))Actor.Free();}
}

internal sealed class LyraWeaponEquipment(Node3D owner,uint character):IDisposable
{
    private readonly LyraWeaponResources _resources=new();
    private uint _generation;
    private bool _retired;
    // These arrays preserve the original first equipment / first spawned actor
    // lookup. A missing first AnimInstance must not fall back to a later actor.
    internal List<List<LyraEquippedWeaponActor>> Instances {get;}=[];
    public long Publications {get;private set;}
    public LyraEquippedWeaponActor? First=>Instances.FirstOrDefault()?.FirstOrDefault();
    public LyraEquippedWeaponActor? CreateReplacement(string profile)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        return profile=="unarmed"?null:new(owner,_resources,profile,character,checked(++_generation));
    }
    public void Replace(LyraEquippedWeaponActor? next)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        foreach(var instance in Instances)foreach(var actor in instance)actor.Dispose();Instances.Clear();
        if(next is not null)Instances.Add([next]);
    }
    public bool Receive(string montage,float rate)
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        return First?.Receive(montage,rate)??false;
    }
    public void ValidateLive()
    {
        ObjectDisposedException.ThrowIf(_retired,this);
        if(!GodotThread.IsMainThread()||!GodotObject.IsInstanceValid(owner)||!owner.IsInsideTree()||owner.IsQueuedForDeletion())
            throw new InvalidOperationException("Retired equipment owner.");
        foreach(var instance in Instances)foreach(var actor in instance)
            if(!actor.Live||actor.Actor.GetParent()!=owner)throw new InvalidOperationException("Equipment actor expired before role commit.");
    }
    public void Tick(long frame,float delta,LyraLogicalSourceBank characterBank,ReadOnlySpan<AlsPrecisePose> finalPose,in AlsPrecisePose component)
    {
        ValidateLive();
        foreach(var instance in Instances)foreach(var actor in instance)
        {
            if(actor.Bank is null)continue;
            var socket=actor.Definition.SocketWorld(characterBank,finalPose,component);
            actor.Tick(frame,delta,actor.Definition.MeshWorld(socket));Publications++;
        }
    }
    public void Dispose()
    {if(_retired)return;foreach(var instance in Instances)foreach(var actor in instance)actor.Dispose();Instances.Clear();_retired=true;}
}
