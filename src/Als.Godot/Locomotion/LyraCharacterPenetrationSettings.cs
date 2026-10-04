using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Locomotion;

internal sealed class LyraCharacterPenetrationSettings
{
    private static readonly Lazy<LyraCharacterPenetrationSettings> Shared=new(Load);
    public static LyraCharacterPenetrationSettings Default=>Shared.Value;
    public float Pullback {get;}
    public float Inflation {get;}
    public float InitialOverlapTolerance {get;}
    private readonly float _geometry,_geometryProxy,_pawn,_pawnProxy;
    public AlsCharacterPenetrationSettings Core=>new(Pullback,Inflation,InitialOverlapTolerance,_geometry,_geometryProxy,_pawn,_pawnProxy);
    public float Limit(bool pawn,bool proxy)=>Core.Limit(pawn,proxy);
    public string Sha256 {get;}
    private LyraCharacterPenetrationSettings(byte[] bytes)
    {
        Sha256=Convert.ToHexString(SHA256.HashData(bytes));using var doc=JsonDocument.Parse(bytes);
        var root=doc.RootElement;var p=root.GetProperty("profile");
        if(root.GetProperty("schemaVersion").GetInt32()!=1||!StringComparer.OrdinalIgnoreCase.Equals(
            root.GetProperty("baseMotorSha256").GetString(),LyraCharacterMovementSettings.Default.Sha256))throw new InvalidDataException("Recovery profile differs from motor.");
        float F(string name){float f=p.GetProperty(name).GetSingle();if(!float.IsFinite(f))throw new InvalidDataException("Nonfinite recovery setting.");return f;}
        Pullback=Math.Abs(F("p.PenetrationPullbackDistance"))*.01f;Inflation=F("p.PenetrationOverlapCheckInflation")*.01f;
        InitialOverlapTolerance=F("p.InitialOverlapTolerance");
        _geometry=F("MaxDepenetrationWithGeometry")*.01f;_geometryProxy=F("MaxDepenetrationWithGeometryAsProxy")*.01f;
        _pawn=F("MaxDepenetrationWithPawn")*.01f;_pawnProxy=F("MaxDepenetrationWithPawnAsProxy")*.01f;
        if(p.GetProperty("p.MoveIgnoreFirstBlockingOverlap").GetInt32()!=0||p.GetProperty("role").GetInt32()!=3||
            Inflation<0||_geometry<0||_geometryProxy<0||_pawn<0||_pawnProxy<0)throw new NotSupportedException("Unsupported original recovery policy.");
    }
    private static LyraCharacterPenetrationSettings Load()=>new(File.ReadAllBytes(ProjectSettings.GlobalizePath(
        "res://assets/generated/lyra_als/character_penetration_v1.json")));
}
