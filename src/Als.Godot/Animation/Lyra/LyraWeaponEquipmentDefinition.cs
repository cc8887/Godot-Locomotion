using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed class LyraWeaponEquipmentDefinition
{
    public string Kind {get;}
    public string Definition {get;}
    public string ActorClass {get;}
    public string Socket {get;}
    public AlsPrecisePose Attach {get;}
    public AlsPrecisePose ActorRelative {get;}
    public AlsPrecisePose MeshRelativeToActor {get;}
    public LyraWeaponEquipmentDefinition(string kind)
    {
        const string root="res://assets/generated/lyra_als/";
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"weapon_equipment_v1_policy.json"));
        var policy=document.RootElement;
        if(policy.GetProperty("schemaVersion").GetInt32()!=1||policy.GetProperty("phase").GetString()!="originalReceivedNotifyBeforeWeaponAdvance")
            throw new InvalidOperationException("Changed original equipment phase.");
        foreach(var d in policy.GetProperty("dependencies").EnumerateObject())
            if(d.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)))
                throw new InvalidOperationException("Stale original equipment contract: "+d.Name);
        var row=policy.GetProperty("definitions").GetProperty(kind);
        Kind=kind;Definition=row.GetProperty("definition").GetString()!;ActorClass=row.GetProperty("actorClass").GetString()!;
        Socket=row.GetProperty("socket").GetString()!;Attach=LyraLogicalSourceBank.ParsePose(row.GetProperty("attach"));
        ActorRelative=LyraLogicalSourceBank.ParsePose(row.GetProperty("actorRelative"));
        MeshRelativeToActor=LyraLogicalSourceBank.ParsePose(row.GetProperty("meshRelativeToActor"));
        if(Socket!="weapon_r"||!row.GetProperty("meshIsActorRoot").GetBoolean()||
            row.GetProperty("tickChain").GetArrayLength()!=1||row.GetProperty("tickChain").EnumerateArray().Any(e=>
                !e.GetProperty("parentTickPrerequisite").GetBoolean()||!e.GetProperty("childCanTick").GetBoolean()||!e.GetProperty("parentCanTick").GetBoolean()))
            throw new NotSupportedException("Changed original weapon component attachment/tick chain.");
        Attach.Validate();ActorRelative.Validate();MeshRelativeToActor.Validate();
    }
    public AlsPrecisePose MeshWorld(in AlsPrecisePose socket)=>
        AlsPrecisePose.Compose(MeshRelativeToActor,AlsPrecisePose.Compose(ActorRelative,socket));
    public AlsPrecisePose SocketWorld(LyraLogicalSourceBank bank,ReadOnlySpan<AlsPrecisePose> finalPose,in AlsPrecisePose component)
    {
        if(finalPose.Length!=bank.Parents.Length)throw new ArgumentException("Foreign final character layout.");
        int bone=bank.Bone(Socket);var result=finalPose[bone];
        for(int parent=bank.Parents[bone];parent>=0;parent=bank.Parents[parent])result=AlsPrecisePose.Compose(result,finalPose[parent]);
        return AlsPrecisePose.Compose(result,component);
    }
}
