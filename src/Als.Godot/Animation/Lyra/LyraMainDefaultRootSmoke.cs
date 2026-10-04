using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainDefaultRootSmoke:Node
{
    public override void _Ready()
    {try{Run();GetTree().Quit();}catch(Exception e){GD.PushError("Default Main root failed: "+e);GetTree().Quit(1);}}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Reject(Action a){try{a();}catch(InvalidOperationException){return;}throw new InvalidOperationException("Invalid default Main operation accepted.");}
    private void Run()
    {
        using var resources=new LyraLocomotionResources();
        using var requests=JsonDocument.Parse(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/default-main-v5-requests.json")));
        using var native=JsonDocument.Parse(File.ReadAllBytes(ProjectSettings.GlobalizePath("res://artifacts/lyra-analysis/default-main-v5-native.json")));
        var q=requests.RootElement.GetProperty("traces");var traces=native.RootElement.GetProperty("traces");
        var comparison=new LyraMainNativeComparison();int frames=0,retries=0,poses=0,fields=0;
        for(var t=3;t<6;t++)
        {
            var owner=resources.CreateMainOwner();var character=new object();var host=new LyraMainDefaultRootFrameHost(resources,owner,character,31);
            var initialLean=owner.Lean.LeanStates;var initialRotation=owner.Lean.Rotation;var initialRoot=owner.RootState;
            var fs=q[t].GetProperty("frames");var rows=traces[t].GetProperty("frames");
            for(var n=0;n<fs.GetArrayLength();n++)
            {
                var f=fs[n];var row=rows[n];var properties=f.GetProperty("mainProperties");var ground=f.GetProperty("ground").GetBoolean();
                var physical=row.GetProperty("physicalInput");var rotation=physical.GetProperty("rotation");
                var input=new LyraMainUpdateInput(new(Vector(f.GetProperty("location")),
                    new(rotation[0].GetDouble(),rotation[1].GetDouble(),rotation[2].GetDouble(),false,false,false),Vector(f.GetProperty("velocity")),Vector(f.GetProperty("acceleration")),
                    ground,f.GetProperty("crouching").GetBoolean(),ground?1:3,properties.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    properties.GetProperty("GameplayTag_IsFiring").GetBoolean(),0),physical.GetProperty("aimPitch").GetDouble(),-980,false,false,true,0);
                var before=owner.Update.State;var tailBefore=owner.Update.Tail;
                var delta=f.GetProperty("delta").GetSingle();var evaluate=f.GetProperty("evaluate").GetBoolean();
                for(var attempt=0;attempt<2;attempt++)
                {
                    var candidate=host.Prepare(input,delta);comparison.Identity=$"{q[t].GetProperty("hz")}/{n}/{attempt}";
                    Require(candidate.Identity.CharacterId==31&&candidate.Identity.FrameId==n,"Default Main identity skipped a cancelled frame.");
                    Require(owner.Update.State==before&&owner.Update.Tail==tailBefore,"Preparation published Main worker state.");
                    Reject(()=>host.Prepare(input,delta));
                    Compare(candidate.Macro,row.GetProperty("mainUpdated"),comparison,ref fields);
                    if(evaluate)
                    {
                        var output=host.Evaluate(candidate);
                        Require(output.Pose.SequenceEqual(resources.Catalog.Bank.Reference)&&output.Curves.ToArray().All(c=>!c.Present)&&
                            output.Attributes.ToArray().All(a=>!a.Present)&&!output.RootMotion.Present,"Default Main pre-Rig pose data differs.");
                        var again=host.Evaluate(candidate);Require(again.Pose.SequenceEqual(output.Pose),"Repeated default pose changed.");
                        if(attempt==1)poses++;
                    }
                    else Reject(()=>host.Commit(candidate));
                    if(attempt==0)
                    {host.Cancel();Require(owner.Update.State==before&&owner.Update.Tail==tailBefore,"Cancellation changed Main worker state.");Reject(()=>host.Evaluate(candidate));retries++;}
                    else
                    {host.Commit(candidate,!evaluate);Require(owner.Update.State==candidate.Macro.State&&owner.Update.Tail==candidate.Macro.Tail,"Default Main worker did not commit.");Reject(()=>host.Commit(candidate));frames++;}
                    Require(owner.Lean.LeanStates==initialLean&&owner.Lean.Rotation==initialRotation&&owner.RootState==initialRoot&&!owner.Lean.HasPending,
                        "Default Main initialized or advanced an unvisited source/graph history.");
                }
            }
            Reject(()=>new LyraMainDefaultRootFrameHost(resources,owner,new object()));
            host.Cancel();
        }
        Require(frames==1260&&retries==1260,"Incomplete default Main three-frequency coverage.");
        GD.Print($"LYRA_MAIN_DEFAULT_ROOT_OK frames={frames} retry={retries} poses={poses} fields={fields} linked=0 sources=0 preRig=true ordinaryUnlink=false");
    }
    private static AlsDoubleVector Vector(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
    private static void Compare(LyraMainUpdateCandidate macro,JsonElement expected,LyraMainNativeComparison comparison,ref int count)
    {
        var s=macro.State;var t=macro.Tail;var checkedFields=0;
        void Field(string name,object value)
        {
            var e=expected.GetProperty(name);object native=value switch
            {
                AlsDoubleVector=>Vector(e),
                double=>expected.TryGetProperty(name+"Bits",out var bits)?BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64(bits.GetString(),16))):e.GetDouble(),
                bool=>e.GetBoolean(),int=>e.GetInt32(),_=>throw new InvalidOperationException("Unsupported worker field type.")
            };
            comparison.Compare(name,value,native);checkedFields++;
        }
        Field("WorldLocation",s.Location);Field("WorldVelocity",s.Velocity);Field("LocalVelocity2D",s.LocalVelocity);Field("LocalAcceleration2D",s.LocalAcceleration);Field("PivotDirection2D",s.Pivot);
        Field("DisplacementSinceLastUpdate",s.Displacement);Field("DisplacementSpeed",s.DisplacementSpeed);Field("YawDeltaSinceLastUpdate",s.Rotation.YawDelta);Field("YawDeltaSpeed",s.Rotation.YawSpeed);Field("AdditiveLeanAngle",s.Rotation.LeanAngle);
        Field("LocalVelocityDirectionAngle",s.DirectionAngle);Field("LocalVelocityDirectionAngleWithOffset",s.DirectionAngleWithOffset);Field("LocalVelocityDirection",s.Direction);Field("LocalVelocityDirectionNoOffset",s.DirectionNoOffset);
        Field("HasVelocity",s.HasVelocity);Field("HasAcceleration",s.HasAcceleration);Field("CardinalDirectionFromAcceleration",s.AccelerationDirection);Field("IsRunningIntoWall",s.Wall);
        Field("IsOnGround",s.Ground);Field("IsCrouching",s.Crouching);Field("CrouchStateChange",s.CrouchChanged);Field("ADSStateChanged",s.AdsChanged);Field("WasADSLastUpdate",s.WasAds);Field("TimeSinceFiredWeapon",s.TimeSinceFired);
        Field("IsJumping",s.Jumping);Field("IsFalling",s.Falling);Field("IsFirstUpdate",s.First);Field("RootYawOffset",s.RootYaw);Field("CardinalDirectionDeadZone",s.DeadZone);
        Field("GameplayTag_IsADS",s.Ads);Field("GameplayTag_IsFiring",s.Firing);
        Field("UpperbodyDynamicAdditiveWeight",t.UpperbodyWeight);Field("AimYaw",t.AimYaw);Field("AimPitch",t.AimPitch);Field("TimeToJumpApex",t.TimeToApex);
        count+=checkedFields;
    }
}
