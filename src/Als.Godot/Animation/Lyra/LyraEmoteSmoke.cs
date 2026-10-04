using System.Text.Json;
using Godot;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraEmoteSmoke:Node
{
    private int _checks;
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public override void _Ready()
    {
        try
        {
            const string root="res://assets/generated/lyra_als/";
            var catalog=new LyraMontageCatalog();var policy=new LyraEmotePolicy(catalog);
            int traces=0,frames=0,retries=0,activations=0,ends=0,clears=0;
            foreach(var fixture in new[]{"emote_v1","emote_edges_v1"})
            {
            using var requests=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+fixture+"_requests.json"));
            using var native=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+fixture+"_native.json"));
            var reference=native.RootElement;
            Require(reference.GetProperty("requestSha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+fixture+"_requests.json"))&&
                reference.GetProperty("policySha256").GetString()==LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+"emote_v1_policy.json")),"Stale original Emote reference.");
            int other=catalog.Paths.IndexOf(requests.RootElement.GetProperty("otherMontage").GetString()!);
            int fixtureTrace=0;
            foreach(var trace in requests.RootElement.GetProperty("traces").EnumerateArray())
            {
                var bank=catalog.CreateRuntime();var bus=new LyraCharacterMovementDelegate((uint)traces+1,1);int observer=0;
                bus.BindUnique(new object(),_=>observer++);
                var plays=new List<AlsMontageActionRequest>();var stops=new List<AlsMontageStopRequest>();var afterStops=new List<long>();
                LyraEmoteAbility emote=null!;
                emote=new(policy.Asset,policy.BlendOut,bus,plays.Add,stops.Add,asset=>
                {
                    var instance=bank.ActiveActionInstance(asset);if(instance==0)return;
                    afterStops.Add(instance);bank.StopInstance(instance,policy.BlendOut,catalog.Definitions[asset].Lifecycle.BlendOutOption);
                });
                int index=0;
                foreach(var f in trace.GetProperty("frames").EnumerateArray())
                {
                    string label=$"Emote/{trace.GetProperty("hz")}/{trace.GetProperty("mode")}/{index}";
                    var expected=reference.GetProperty("trace").GetProperty("traces")[fixtureTrace].GetProperty("frames")[index];
                    bool crouched=f.GetProperty("crouched").GetBoolean(),wants=crouched;
                    if(f.GetProperty("activate").GetBoolean())
                    {bool accepted=emote.Activate(crouched);Require(accepted==expected.GetProperty("activationAccepted").GetBoolean(),label+" activation");if(accepted&&crouched)wants=false;}
                    if(f.GetProperty("cancel").GetBoolean())emote.CancelAbility();
                    if(f.GetProperty("interrupt").GetBoolean())plays.Add(new(other,1));
                    var id=new AlsFrameIdentity(index,(uint)traces+1,1);float delta=f.GetProperty("delta").GetSingle();
                    bank.BeginWithActionRequests(id,delta,plays.ToArray(),stops.ToArray(),emote.BindMontageCallbacks);
                    var motion=bank.RootMotionRange;emote.ObserveInstance(emote.CaptureInstance(bank));
                    var immediate=bank.ImmediateMontageEvents;bank.DeliverImmediateMontageEvents();
                    bool committed=false;
                    Check("afterAdvance");
                    if(f.GetProperty("applyUncrouch").GetBoolean()&&!wants)crouched=false;
                    if(f.GetProperty("movement").GetBoolean())
                    {
                        var v=f.GetProperty("oldVelocity");bus.Broadcast(new(id,delta,default,new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble()),crouched));
                        Reject(new(id,delta,default,default,false));
                        Reject(new(new(index,(uint)traces+100,1),delta,default,default,false));
                        Reject(new(new(index+1,(uint)traces+1,2),delta,default,default,false));
                        void Reject(LyraCharacterMovementEvent movement)
                        {bool rejected=false;try{bus.Broadcast(movement);}catch(InvalidOperationException){rejected=true;}Require(rejected,label+" invalid movement callback accepted");}
                    }
                    Check("afterMovement");
                    var active=emote.Active;var endCount=emote.Ends;var calls=observer;var binding=emote.Bound;
                    bank.Discard();bank.BeginWithActionRequests(id,delta,plays.ToArray(),stops.ToArray(),emote.BindMontageCallbacks);
                    bank.AcknowledgeImmediateMontageEvents(immediate);
                    Require(bank.RootMotionRange==motion,label+" raw movement changed on retry");
                    foreach(var stop in afterStops)Require(bank.StopInstance(stop,policy.BlendOut,catalog.Definitions[policy.Asset].Lifecycle.BlendOutOption),label+" post-movement replay");
                    Require(emote.Active==active&&emote.Ends==endCount&&observer==calls&&emote.Bound==binding,label+" animation retry published gameplay");
                    Check("afterMovement");retries++;
                    bank.Commit(id);committed=true;bank.DispatchMontageEventCallbacks();Check("afterDispatch");
                    Require(emote.Ends==expected.GetProperty("ends").GetInt32(),label+" end count");
                    plays.Clear();stops.Clear();afterStops.Clear();index++;frames++;
                    void Check(string phase)
                    {
                        var e=expected.GetProperty(phase);var states=committed?bank.Committed.ToArray():bank.Candidate.ToArray();
                        var instance=committed?states.FirstOrDefault(x=>x.ActionDefinitionId==policy.Asset&&x.OwnsActiveActionLookup).InstanceId:bank.ActiveActionInstance(policy.Asset);
                        var m=states.FirstOrDefault(x=>x.InstanceId==instance&&instance>0);
                        void B(string key,bool value){Require(e.GetProperty(key).GetBoolean()==value,$"{label}/{phase}/{key} actual={value} expected={e.GetProperty(key)}");_checks++;}
                        void N(string key,double value,double tolerance=0){Require(Math.Abs(e.GetProperty(key).GetDouble()-value)<=tolerance,$"{label}/{phase}/{key} actual={value:R} expected={e.GetProperty(key)}");_checks++;}
                        B("active",emote.Active);B("bound",emote.Bound);B("animatingAbility",emote.AnimatingAbility);
                        B("currentMontage",instance>0);B("stopped",instance==0);B("lookup",instance>0);B("playing",instance>0&&m.Playing);
                        B("rootOwner",(committed?bank.CommittedRootMotionInstance:bank.CandidateRootMotionInstance)>0);B("crouched",crouched);B("wantsCrouch",wants);
                        N("listeners",bus.Count);N("observerCalls",observer);N("rootScale",1);
                        N("position",instance>0?m.Position:0,1e-7);N("previous",instance>0?m.DeltaTimeRecord.PreviousPosition:0,1e-7);
                        N("weight",instance>0?m.Blend.CurrentWeight:0,1e-7);
                    }
                }
                activations+=emote.Activations;ends+=emote.Ends;clears+=emote.MovementClears;emote.Retire();
                bool rejected=false;try{emote.Activate(false);}catch(ObjectDisposedException){rejected=true;}Require(rejected,"Retired Emote activated.");
                bus.Retire();rejected=false;try{bus.Broadcast(new(new(index,(uint)traces+1,1),1,default,default,false));}catch(ObjectDisposedException){rejected=true;}Require(rejected,"Retired role dispatched movement.");traces++;fixtureTrace++;
            }
            }
            GD.Print($"LYRA_EMOTE_NATIVE_GODOT_OK traces={traces} frames={frames} retries={retries} checks={_checks} activations={activations} ends={ends} clears={clears}");GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(1);}
    }
}
