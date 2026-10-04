using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

// One immutable CDO/physics-volume configuration shared by scene actors.
internal sealed class LyraCharacterMovementSettings
{
    private static readonly Lazy<LyraCharacterMovementSettings> Shared=new(Load);
    public static LyraCharacterMovementSettings Default=>Shared.Value;
    public string Sha256 {get;}
    public float Radius {get;}
    public float StandingHalfHeight {get;}
    public float CrouchedHalfHeight {get;}
    public float FloorAngle {get;}
    public float MaxAcceleration {get;}
    public float JumpVelocity {get;}
    public float GravityZ {get;}
    public float MaxSimulationTimeStep {get;}
    public int MaxSimulationIterations {get;}
    public bool CanCrouch {get;}
    public AlsCharacterVelocitySettings Standing {get;}
    public AlsCharacterVelocitySettings Crouching {get;}
    public AlsCharacterFallingSettings Falling {get;}
    private LyraCharacterMovementSettings(byte[] bytes)
    {
        Sha256=Convert.ToHexString(SHA256.HashData(bytes));
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=2)throw new InvalidDataException("Unknown native character motor schema.");
        var p=root.GetProperty("profile");
        if(p.GetProperty("movementClass").GetString()!="/Script/LyraGame.LyraCharacterMovementComponent"||
            p.GetProperty("JumpMaxHoldTime").GetSingle()!=0||p.GetProperty("JumpMaxCount").GetInt32()!=1)
            throw new NotSupportedException("Scene motor requires the exported Shooter movement and instantaneous single jump.");
        float F(string name)=>p.GetProperty(name).GetSingle();
        Radius=F("radius")*.01f;StandingHalfHeight=F("halfHeight")*.01f;
        CrouchedHalfHeight=F("CrouchedHalfHeight")*.01f;FloorAngle=Mathf.DegToRad(F("WalkableFloorAngle"));
        MaxAcceleration=F("MaxAcceleration");JumpVelocity=F("JumpZVelocity");GravityZ=F("gravityZ");
        MaxSimulationTimeStep=F("MaxSimulationTimeStep");MaxSimulationIterations=p.GetProperty("MaxSimulationIterations").GetInt32();
        CanCrouch=p.GetProperty("canCrouch").GetBoolean();
        AlsCharacterVelocitySettings V(float speed,float friction,float deceleration)=>new(speed,F("MinAnalogWalkSpeed"),friction,
            p.GetProperty("bUseSeparateBrakingFriction").GetBoolean(),F("BrakingFriction"),F("BrakingFrictionFactor"),deceleration,F("BrakingSubStepTime"));
        Standing=V(F("MaxWalkSpeed"),F("GroundFriction"),F("BrakingDecelerationWalking"));
        Crouching=Standing with{MaxSpeed=F("MaxWalkSpeedCrouched")};
        Falling=new(V(F("MaxWalkSpeed"),F("FallingLateralFriction"),F("BrakingDecelerationFalling")),MaxAcceleration,
            F("AirControl"),F("AirControlBoostMultiplier"),F("AirControlBoostVelocityThreshold"),GravityZ,F("terminalVelocity"));
        if(Radius<=0||CrouchedHalfHeight<Radius||StandingHalfHeight<CrouchedHalfHeight||MaxAcceleration<=0||GravityZ>=0)
            throw new InvalidDataException("Invalid native capsule/movement settings.");
    }
    private static LyraCharacterMovementSettings Load()=>new(File.ReadAllBytes(ProjectSettings.GlobalizePath(
        "res://assets/generated/lyra_als/character_motor_v2.json")));
    public AlsStopMovementSnapshot Snapshot(AlsDoubleVector velocity)=>new(velocity,Standing.SeparateBrakingFriction,
        Standing.BrakingFriction,Standing.Friction,Standing.BrakingFrictionFactor,Standing.BrakingDeceleration);
}
