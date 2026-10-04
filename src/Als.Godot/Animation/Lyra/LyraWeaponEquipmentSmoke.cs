using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Events;
using GodotAls.Core.Locomotion;
using GodotAls.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraWeaponEquipmentSmoke:Node3D
{
    private LyraLocomotionResources _resources=null!;
    private LyraMontageCatalog _montages=null!;
    private readonly List<LyraSceneCharacter> _roles=[];
    private readonly List<LyraSceneCharacter> _lifetime=[];
    private readonly bool[] _lifetimeDone=new bool[3];
    private int _frame,_hz=60,_retry,_switches,_same,_captures;
    private bool _render;
    private double _maxWorldPosition;
    private double _maxWorldQuaternion;
    private int _worldBones;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Reject(Action action)
    {try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or ObjectDisposedException){return;}throw new InvalidOperationException("Invalid equipment operation accepted.");}
    private static void Exact(float actual,float expected,string label)=>
        Require(BitConverter.SingleToInt32Bits(actual)==BitConverter.SingleToInt32Bits(expected),$"{label}: {actual:R}/{expected:R}");
    public override void _Ready()
    {
        try
        {
            _render=OS.GetCmdlineUserArgs().Contains("--weapon-equipment-render");
            var rate=OS.GetCmdlineUserArgs().SingleOrDefault(a=>a.StartsWith("--weapon-equipment-hz="));
            if(rate is not null)_hz=int.Parse(rate.Split('=')[1]);Require(_hz is 30 or 60 or 120,"Invalid equipment rate.");Engine.PhysicsTicksPerSecond=_hz;
            RunNative(new());_resources=new(includeMontageActions:true);_montages=new();
            var floor=new StaticBody3D{CollisionLayer=5,CollisionMask=2};floor.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(30,.2f,30)},Position=new(0,-.1f,0)});AddChild(floor);
            if(_render)
            {
                floor.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(30,.2f,30)},Position=new(0,-.1f,0)});
                var camera=new Camera3D{Position=new(0,4.7f,8),Current=true};AddChild(camera);camera.LookAt(new(0,.9f,0));
                AddChild(new DirectionalLight3D{RotationDegrees=new(-55,-35,0),LightEnergy=1.5f});
            }
            for(int i=0;i<6;i++)_roles.Add(new(this,_resources,_montages,"WeaponRole"+i,new((i%3-1)*2,.92f,(i/3)*-2),new[]{"pistol","rifle","unarmed"}[i%3]));
            _roles[0].Animation.Binding.Model.Scale=Vector3.One*.7f;_roles[1].Animation.Binding.Model.Scale=Vector3.One*1.4f;
            for(int i=0;i<3;i++)
            {
                int index=i;var role=new LyraSceneCharacter(this,_resources,_montages,"LifetimeRole"+i,new((i-1)*3,.92f,-5),"pistol");_lifetime.Add(role);
                role.GameplayEvents.GameplayEventReceived+=(tag,payload,magnitude)=>
                {
                    Require(tag=="GameplayEvent.ReloadDone"&&role.Animation.Binding.PublishedFrames==_frame+1,"Lifetime signal preceded publication.");
                    Reject(()=>role.Animation.Prepare(default,0,default,0,false,default,default));
                    var old=role.Animation.Weapons.First!;long tick=old.LastTick;
                    if(index==0){role.Animation.Dispose();Require(!old.Live&&old.LastTick==tick,"Disposed actor advanced its weapon.");}
                    else if(index==1)role.Body.QueueFree();
                    else {Require(role.Animation.Rebind("rifle")&&!old.Live&&role.Animation.Weapons.First!.Definition.Kind=="rifle","Signal equipment replacement failed.");}
                    _lifetimeDone[index]=true;
                };
            }
        }
        catch(Exception e){Fail(e);}
    }
    private static void RunNative(LyraWeaponResources resources)
    {
        const string root="res://assets/generated/lyra_als/";
        byte[] Bytes(string path)=>Godot.FileAccess.GetFileAsBytes(root+path);
        using var requests=JsonDocument.Parse(Bytes("weapon_equipment_v1_requests.json"));
        using var capture=JsonDocument.Parse(Bytes("weapon_equipment_v1_native.json"));
        using var notifications=JsonDocument.Parse(Bytes("weapon_notify_v1_policy.json"));
        var native=capture.RootElement;Require(native.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Bytes("weapon_equipment_v1_requests.json")),"Changed equipment requests.");
        foreach(var d in native.GetProperty("dependencies").EnumerateObject())Require(d.Value.GetString()==LyraLogicalSourceBank.Sha(Bytes(d.Name)),"Stale equipment dependency.");
        var events=notifications.RootElement.GetProperty("events").EnumerateArray().ToArray();
        int frames=0,poses=0,bones=0,retries=0,received=0,attachments=0;double maxP=0,maxQ=0,maxS=0,attachP=0,attachQ=0,attachS=0;
        foreach(var (trace,index) in native.GetProperty("trace").GetProperty("traces").EnumerateArray().Select((t,i)=>(t,i)))
        {
            var input=requests.RootElement.GetProperty("traces")[index];string kind=trace.GetProperty("kind").GetString()!;
            var equipment=new LyraWeaponEquipmentDefinition(kind);var catalog=new LyraWeaponMontageCatalog(resources,kind);
            using var bank=new LyraWeaponMontageBank(catalog,(uint)index+101,1);
            foreach(var fixture in trace.GetProperty("attachments").EnumerateArray())
            {
                var socket=LyraLogicalSourceBank.ParsePose(fixture.GetProperty("socketWorld"));
                LyraLogicalSourceSmoke.Compare(JsonSerializer.SerializeToElement(new[]{fixture.GetProperty("meshWorld")}),
                    new[]{equipment.MeshWorld(socket)},ref attachP,ref attachQ,ref attachS,"weapon attachment",attachments++,kind);
            }
            foreach(var (target,frame) in trace.GetProperty("frames").EnumerateArray().Select((f,i)=>(f,i)))
            {
                var q=input.GetProperty("frames")[frame];var commands=q.GetProperty("notifies").EnumerateArray().Select(n=>
                {
                    string source=input.GetProperty("notifyAssets")[n.GetProperty("asset").GetInt32()].GetString()!;
                    var payload=events.Single(e=>e.GetProperty("asset").GetString()==source&&e.GetProperty("index").GetInt32()==n.GetProperty("index").GetInt32()).GetProperty("payload");
                    int asset=catalog.Paths.IndexOf(payload.GetProperty("MontageToPlay").GetProperty("path").GetString()!);Require(asset>=0,"Foreign native notify target.");
                    return new AlsMontageActionRequest(asset,payload.GetProperty("RateScale").GetSingle());
                }).ToArray();
                var identity=new AlsFrameIdentity(frame,(uint)index+101,1);var delta=q.GetProperty("delta").GetSingle();
                string? signature=null;LyraWeaponMontageCandidate? stale=null;
                for(int attempt=0;attempt<2;attempt++)
                {
                    var candidate=bank.Prepare(identity,delta,q.GetProperty("sample").GetBoolean(),commands);
                    if(stale is not null)Reject(()=>bank.Validate(stale));
                    var frozen=target.GetProperty("frozen");Require(frozen.GetArrayLength()==candidate.Evaluations.Length,"Wrong pre-tick frozen count.");
                    for(int e=0;e<candidate.Evaluations.Length;e++)
                    {
                        var actual=candidate.Evaluations[e];var expected=frozen[e];Require(actual.ActionDefinitionId==expected.GetProperty("asset").GetInt32(),"Wrong notify target asset.");
                        Exact(actual.MontagePosition,expected.GetProperty("position").GetSingle(),"pre-tick position");
                        Exact(actual.Weight,expected.GetProperty("weight").GetSingle(),"pre-tick weight");
                        Exact(actual.DeltaTimeRecord.PreviousPosition,expected.GetProperty("previous").GetSingle(),"pre-tick previous");
                        Exact(actual.DeltaTimeRecord.Delta,expected.GetProperty("delta").GetSingle(),"pre-tick delta");
                        var blend=actual.BlendSnapshot;
                        Exact(blend.Alpha,expected.GetProperty("alpha").GetSingle(),"pre-tick alpha");Exact(blend.BeginWeight,expected.GetProperty("begin").GetSingle(),"pre-tick begin");
                        Exact(blend.DesiredWeight,expected.GetProperty("desired").GetSingle(),"pre-tick desired");Exact(blend.StartAlpha,expected.GetProperty("startAlpha").GetSingle(),"pre-tick startAlpha");
                        Require((int)blend.Option==expected.GetProperty("option").GetInt32()&&!expected.GetProperty("profile").GetBoolean(),"Changed blend policy.");
                    }
                    Exact(candidate.Weights.SlotNodeWeight,target.GetProperty("slotWeight").GetSingle(),"pre-tick slot");
                    Exact(candidate.Weights.SourceWeight,target.GetProperty("sourceWeight").GetSingle(),"pre-tick source");
                    Exact(candidate.Weights.TotalNodeWeight,target.GetProperty("totalWeight").GetSingle(),"pre-tick total");
                    if(!candidate.Pose.IsEmpty)
                    {
                        var pose=candidate.Pose.ToArray();LyraLogicalSourceSmoke.Compare(target.GetProperty("pose"),catalog.PhysicalBones.Select(b=>pose[b]).ToArray(),ref maxP,ref maxQ,ref maxS,kind,frame,"before weapon tick");
                        if(attempt==1){poses++;bones+=7;}
                    }
                    foreach(var n in target.GetProperty("received").EnumerateArray())Require(!n.GetProperty("returnValue").GetBoolean()&&!n.GetProperty("following").GetBoolean()&&n.GetProperty("positionBeforeTick").GetSingle()==0,"Wrong original Received_Notify boundary.");
                    string current=JsonSerializer.Serialize(new{Pose=candidate.Pose.ToArray(),candidate.Evaluations,candidate.Weights});
                    if(attempt==0){signature=current;stale=candidate;bank.Cancel();retries++;}
                    else{Require(current==signature,"Equipment cancel/retry differs.");bank.Commit(candidate);}
                }
                frames++;received+=commands.Length;
            }
        }
        Require(frames==5040&&poses==4323&&bones==30261&&received==72&&attachments==36,"Missing native equipment coverage.");
        GD.Print($"LYRA_WEAPON_EQUIPMENT_NATIVE_GODOT_OK traces=9 frames={frames} poses={poses} bones={bones} notifies={received} retries={retries} attachments={attachments} positionCm={maxP:R} quaternion={maxQ:R} scale={maxS:R} attachPositionCm={attachP:R} attachQuaternion={attachQ:R} attachScale={attachS:R}");
    }
    private void RunConsumers()
    {
        var catalog=_resources.Catalog.Notifies;
        using var document=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/weapon_notify_v1_native.json"));
        var native=document.RootElement;var inputs=native.GetProperty("request").GetProperty("calls");var expected=native.GetProperty("trace").GetProperty("calls");
        int played=0,missing=0,retries=0;
        for(int i=0;i<inputs.GetArrayLength();i++)
        {
            var input=inputs[i];int kind=input.GetProperty("kind").GetInt32(),scenario=input.GetProperty("scenario").GetInt32();var name=new[]{"pistol","rifle","shotgun"}[kind];
            var actor=new Node3D();AddChild(actor);using var equipment=new LyraWeaponEquipment(actor,901);
            if(scenario!=1)equipment.Replace(equipment.CreateReplacement(name));
            if(scenario==2)equipment.Instances.Add([equipment.CreateReplacement(new[]{"pistol","rifle","shotgun"}[(kind+1)%3])!]);
            if(scenario==3)equipment.First!.RemoveAnimationInstance();
            var queue=new LyraNotifyQueueRuntime(catalog,901);var consumer=new LyraWeaponNotifyConsumer(actor,catalog,_montages,queue,equipment);
            var asset=catalog.Asset(input.GetProperty("asset").GetString()!);int policy=asset.Offset+input.GetProperty("index").GetInt32();var definition=catalog.Definitions[policy];
            Require(catalog.Event(policy).ObjectPath==expected[i].GetProperty("object").GetString(),"Original weapon object changed.");
            ImmutableArray<LyraMontageNotifyWindow> windows=[new(new(LyraNotifySourceOwner.Main,-1,1,0,1,AlsAssetNotifySourceKind.Montage,1),asset.Index,-1,
                definition.TriggerTimeSeconds-.0001f,definition.TriggerTimeSeconds+.0001f,definition.TriggerTimeSeconds-.0001f,definition.TriggerTimeSeconds+.0001f,1,true)];
            LyraNotifyQueueCandidate PrepareQueue()=>queue.PrepareWindows(new(0,901,1),1f/60,[],()=>{},montageWindows:windows);
            var q=PrepareQueue();var cancelled=consumer.Prepare(q);Require(cancelled.Commands.Length==1,"Original weapon Notify did not become a typed command.");
            Require(equipment.First?.PendingRequests is null or 0,"Prepare played a weapon.");consumer.Cancel();queue.Cancel();Reject(()=>consumer.BeginCommitted(cancelled));
            q=PrepareQueue();var current=consumer.Prepare(q);Require(current.Commands.SequenceEqual(cancelled.Commands),"Typed weapon retry differs.");
            Reject(()=>consumer.ValidateCommit(cancelled));consumer.ValidateCommit(current);queue.Commit(q);consumer.BeginCommitted(current);
            Require(consumer.DispatchAt(current,0)==expected[i].GetProperty("returnValue").GetBoolean(),"Original weapon return differs.");
            Reject(()=>consumer.DispatchAt(current,0));consumer.EndCommitted(current);Reject(()=>consumer.BeginCommitted(current));
            if(scenario is 0 or 2)
            {
                var weapons=expected[i].GetProperty("weapons");var command=current.Commands[0];
                Require(command.Montage==weapons[0].GetProperty("montage").GetString()&&command.Rate==weapons[0].GetProperty("rate").GetSingle()&&equipment.First!.PendingRequests==1,"Wrong first equipment/actor request.");
                if(scenario==2)Require(equipment.Instances[1][0].PendingRequests==0,"Notify fell through to second equipment.");
                played++;
            }
            else{Require(consumer.Missing==1,"Missing first AnimInstance was not preserved.");missing++;}
            consumer.Retire();queue.Retire();equipment.Dispose();actor.Free();retries++;
        }
        Require(played==18&&missing==18,"Incomplete original consumer cases.");
        GD.Print($"LYRA_WEAPON_NOTIFY_CONSUMER_GODOT_OK calls=36 played={played} missing={missing} retries={retries} originalFalseReturn=true firstOnly=true follower=false");
        var dynamicActor=new Node3D();AddChild(dynamicActor);using var dynamicEquipment=new LyraWeaponEquipment(dynamicActor,902);
        dynamicEquipment.Replace(dynamicEquipment.CreateReplacement("pistol"));var dynamicQueue=new LyraNotifyQueueRuntime(catalog,902);
        var dynamicConsumer=new LyraWeaponNotifyConsumer(dynamicActor,catalog,_montages,dynamicQueue,dynamicEquipment);
        var dynamicWindows=ImmutableArray.CreateBuilder<LyraMontageNotifyWindow>();
        foreach(var kind in new[]{"Pistol","Rifle"})
        {
            string name=$"AM_MM_{kind}_Fire";var asset=catalog.Asset($"/Game/Weapons/{kind}/Animations/{name}.{name}");var def=catalog.Definitions[asset.Offset];
            dynamicWindows.Add(new(new(LyraNotifySourceOwner.Main,-1,1,0,1,AlsAssetNotifySourceKind.Montage,dynamicWindows.Count+1),asset.Index,-1,
                def.TriggerTimeSeconds-.0001f,def.TriggerTimeSeconds+.0001f,def.TriggerTimeSeconds-.0001f,def.TriggerTimeSeconds+.0001f,1,true));
        }
        var dq=dynamicQueue.PrepareWindows(new(0,902,1),1f/60,[],()=>{},montageWindows:dynamicWindows.ToImmutable());var dc=dynamicConsumer.Prepare(dq);
        Require(dc.Commands.Length==2,"Missing dynamic receiver callbacks.");dynamicConsumer.ValidateCommit(dc);dynamicQueue.Commit(dq);dynamicConsumer.BeginCommitted(dc);
        dynamicConsumer.DispatchAt(dc,0);var firstActor=dynamicEquipment.First!;Require(firstActor.PendingRequests==1,"First dynamic callback was lost.");
        dynamicEquipment.Replace(dynamicEquipment.CreateReplacement("rifle"));dynamicConsumer.DispatchAt(dc,1);dynamicConsumer.EndCommitted(dc);
        Require(!firstActor.Live&&dynamicEquipment.First!.PendingRequests==1&&dynamicConsumer.Played==2&&dynamicConsumer.Missing==0,"Cached equipment receiver survived replacement.");
        dynamicConsumer.Retire();dynamicQueue.Retire();dynamicEquipment.Dispose();dynamicActor.Free();
        GD.Print("LYRA_WEAPON_FRESH_LOOKUP_GODOT_OK callbacks=2 replacement=true staleActorRetired=true");
    }
    public override void _PhysicsProcess(double delta)
    {
        try
        {
            if(_frame==0)RunConsumers();
            for(int i=0;i<_lifetime.Count;i++)
            {
                var role=_lifetime[i];if(_lifetimeDone[i]){if(i<2)role.Dispose();continue;}
                if(_frame==1)role.Animation.RequestWeaponAction(true);
                role.Advance(new(Vector2.Zero,0,0,false,false,false,false),(float)delta);
                if(_lifetimeDone[i]&&i==2)Require(role.Animation.Weapons.First!.LastTick==_frame,"Replacement missed the attached component tick.");
            }
            for(int i=0;i<_roles.Count;i++)
            {
                var role=_roles[i];var animation=role.Animation;var old=animation.Weapons.First;
                if(_frame==_hz*3||_frame==_hz*5)
                {
                    string profile=animation.Profile=="pistol"?"rifle":animation.Profile=="rifle"?"unarmed":"pistol";
                    Require(animation.Rebind(profile),"Equipment class did not change.");Require(old is null||!old.Live,"Old weapon actor survived replacement.");_switches++;
                }
                if(_frame==_hz*2){Require(!animation.Rebind(animation.Profile)&&ReferenceEquals(old,animation.Weapons.First),"Same equipment reset its bank.");_same++;}
                if(_frame==_hz/10||_frame==_hz*4)animation.RequestWeaponAction(false);
                if(_frame==_hz/5||_frame==_hz*6)animation.RequestWeaponAction(true);
                var observation=role.Move(new(new((float)Math.Sin(_frame*delta+i)*.25f,0),(float)(i*.25+_frame*delta*.12),.1f,
                    _frame>_hz&&_frame<_hz*2,_frame>_hz*4&&_frame<_hz*5,false,_frame==_hz*5/2,_frame==_hz/10||_frame==_hz*4),(float)delta);
                long publications=animation.Weapons.Publications,plays=animation.WeaponNotifies.Played;long tick=animation.Weapons.First?.LastTick??-1;
                var first=observation.Prepare(animation,(float)delta);animation.Cancel();
                Require(publications==animation.Weapons.Publications&&plays==animation.WeaponNotifies.Played&&tick==(animation.Weapons.First?.LastTick??-1),"Cancelled role advanced weapon.");
                Reject(()=>animation.Commit(first));var current=observation.Prepare(animation,(float)delta);
                Require(first.Output.Pose.SequenceEqual(current.Output.Pose)&&first.WeaponNotifies.Commands.SequenceEqual(current.WeaponNotifies.Commands),"Role weapon retry differs.");
                animation.Commit(current);_retry++;
                if(animation.Weapons.First is {} gun)
                {
                    Require(gun.LastTick==_frame&&gun.Model.PublishedFrames>0&&gun.LastPose is not null,"Attached weapon did not tick after committed role.");
                    var socket=gun.Definition.SocketWorld(animation.SourceBank,animation.CommittedOutput.Pose,LyraGodotRigCollision.NativeTransform(animation.Binding.Component.GlobalTransform));
                    var expected=LyraWeaponModelBinding.WorldTransform(gun.Definition.MeshWorld(socket));
                    var actual=gun.Model.Skeleton.GlobalTransform*new Transform3D(new Basis(Vector3.Down,Vector3.Back,Vector3.Left),Vector3.Zero);
                    Require(actual.IsEqualApprox(expected),"Gun model axes do not match final weapon_r.");
                    var source=gun.LastPose!.Pose.ToArray();var catalog=gun.Bank!.Catalog;
                    for(int b=0;b<gun.Model.Skeleton.GetBoneCount();b++)
                    {
                        int logical=Array.IndexOf(catalog.Names,gun.Model.Skeleton.GetBoneName(b).ToString());var atom=source[logical];
                        for(int parent=catalog.Parents[logical];parent>=0;parent=catalog.Parents[parent])atom=AlsPrecisePose.Compose(atom,source[parent]);
                        // GPU skinning multiplies component-to-world matrices.
                        // FTransform::Multiply would discard world scale shear
                        // and is not the renderer's expected bone matrix.
                        var wanted=LyraWeaponModelBinding.WorldTransform(gun.Definition.MeshWorld(socket))*LyraWeaponModelBinding.WorldTransform(atom)
                            *new Transform3D(new Basis(Vector3.Down,Vector3.Back,Vector3.Left).Inverse(),Vector3.Zero);
                        var physical=gun.Model.Skeleton.GlobalTransform*gun.Model.Skeleton.GetBoneGlobalPose(b);
                        _maxWorldPosition=Math.Max(_maxWorldPosition,physical.Origin.DistanceTo(wanted.Origin));
                        var a=physical.Basis.GetRotationQuaternion().Normalized();var w=wanted.Basis.GetRotationQuaternion().Normalized();
                        var qa=new AlsQuaternion(a.X,a.Y,a.Z,a.W);var qw=new AlsQuaternion(w.X,w.Y,w.Z,w.W);
                        _maxWorldQuaternion=Math.Max(_maxWorldQuaternion,Math.Sqrt((qa+qw*(AlsQuaternion.Dot(qa,qw)<0?1:-1)).LengthSquared));
                        // This is the float scene/skeleton boundary, after each
                        // local quaternion is quantized and composed by Godot.
                        // Native pose accuracy is independently held at 1e-10.
                        Require(_maxWorldPosition<=1e-4&&_maxWorldQuaternion<=1e-6&&physical.Basis.Scale.IsEqualApprox(wanted.Basis.Scale),
                            $"Published weapon bone differs frame={_frame} role={i} bone={catalog.Names[logical]} p={_maxWorldPosition:R} q={_maxWorldQuaternion:R} physical={physical} wanted={wanted}.");_worldBones++;
                    }
                }
            }
            _frame++;
            if(_render&&_frame is 30 or 90 or 270)_=Capture(_frame);
            if(_frame<_hz*8)return;
            long delivered=_roles.Sum(r=>r.Animation.WeaponNotifies.Played),publicationsTotal=_roles.Sum(r=>r.Animation.Weapons.Publications);
            Require(delivered>0&&publicationsTotal>0&&_switches==12&&_same==6&&(!_render||_captures==3),"Missing live weapon actions/rebind/render.");
            Require(_lifetimeDone.All(v=>v),"Missing signal disposal, queued deletion or equipment replacement.");
            GD.Print("LYRA_WEAPON_CALLBACK_LIFETIME_GODOT_OK disposed=1 queuedDeletion=1 rebind=1 reentrantPrepareRejected=3");
            GD.Print($"LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz={_hz} roles=6 frames={_frame} publications={publicationsTotal} played={delivered} retries={_retry} switches={_switches} same={_same} captures={_captures} worldBones={_worldBones} worldPositionM={_maxWorldPosition:R} worldQuaternion={_maxWorldQuaternion:R} finalSocket=true wholeMainNative=false");GetTree().Quit();
        }
        catch(Exception e){Fail(e);}
    }
    private async System.Threading.Tasks.Task Capture(int frame)
    {
        try{await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);var path=$"res://artifacts/lyra-analysis/weapon-equipment-render-{frame}.png";
            Require(GetViewport().GetTexture().GetImage().SavePng(path)==Error.Ok,"Weapon equipment capture failed.");_captures++;}
        catch(Exception e){Fail(e);}
    }
    private void Fail(Exception e){GD.PushError("Lyra weapon equipment failed: "+e);GetTree().Quit(1);}
    public override void _ExitTree(){foreach(var role in _lifetime)role.Dispose();foreach(var role in _roles)role.Dispose();_resources?.Dispose();}
}
