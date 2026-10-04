using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

internal sealed class LyraCharacterFloorSettings
{
    private static readonly Lazy<LyraCharacterFloorSettings> Shared=new(Load);
    public static LyraCharacterFloorSettings Default=>Shared.Value;
    public string Sha256 {get;}
    public float Minimum {get;}
    public float Maximum {get;}
    public AlsCharacterFloorSettings Core=>new(Minimum,Maximum,EdgeReject,StepHeight,PerchRadiusThreshold,PerchAdditionalHeight,WalkableZ);
    public float Average=>Core.Average;
    public float EdgeReject {get;}
    public float StepHeight {get;}
    public float PerchRadiusThreshold {get;}
    public float PerchAdditionalHeight {get;}
    public float WalkableZ {get;}
    public int MaxJumpApexAttempts {get;}
    public float TraceDistance(bool walking)=>Core.TraceDistance(walking);
    private LyraCharacterFloorSettings(byte[] bytes)
    {
        Sha256=Convert.ToHexString(SHA256.HashData(bytes));
        using var doc=JsonDocument.Parse(bytes);var root=doc.RootElement;var p=root.GetProperty("profile");
        if(root.GetProperty("schemaVersion").GetInt32()!=1||
            !StringComparer.OrdinalIgnoreCase.Equals(root.GetProperty("baseMotorSha256").GetString(),LyraCharacterMovementSettings.Default.Sha256))
            throw new InvalidDataException("Floor configuration differs from the current native movement profile.");
        if(p.GetProperty("bUseFlatBaseForFloorChecks").GetBoolean()||!p.GetProperty("bAlwaysCheckFloor").GetBoolean())
            throw new NotSupportedException("The exported Shooter uses a round capsule and checks its floor each tick.");
        float F(string name)=>p.GetProperty(name).GetSingle();
        Minimum=F("minimumFloorDistance")*.01f;Maximum=F("maximumFloorDistance")*.01f;
        EdgeReject=F("sweepEdgeRejectDistance")*.01f;StepHeight=F("MaxStepHeight")*.01f;
        PerchRadiusThreshold=Math.Max(0,F("PerchRadiusThreshold"))*.01f;
        PerchAdditionalHeight=Math.Max(0,F("PerchAdditionalHeight"))*.01f;
        WalkableZ=Mathf.Cos(Mathf.DegToRad(F("WalkableFloorAngle")));
        MaxJumpApexAttempts=p.GetProperty("MaxJumpApexAttemptsPerSimulation").GetInt32();
        if(Minimum<=0||Maximum<Minimum||EdgeReject<0||StepHeight<0||WalkableZ<=0)
            throw new InvalidDataException("Invalid native floor configuration.");
    }
    private static LyraCharacterFloorSettings Load()=>new(File.ReadAllBytes(ProjectSettings.GlobalizePath(
        "res://assets/generated/lyra_als/character_floor_v1.json")));
}
