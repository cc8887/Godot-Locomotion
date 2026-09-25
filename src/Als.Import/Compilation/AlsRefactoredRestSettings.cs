using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using static GodotAls.Import.Compilation.AlsRefactoredBoxOverlayCompiler;

namespace GodotAls.Import.Compilation;

public sealed record AlsRefactoredTurnAsset(string Binding, string Source, string Sequence, float PlayRate, float AnimatedAngle, bool ScalePlayRate);

/// <summary>Full precision original Parent rest settings and their animation identities.</summary>
public sealed class AlsRefactoredRestSettings
{
    public string CatalogDigest { get; }
    public float RotateYaw { get; }
    public float FirstPersonYaw { get; }
    public Vector2 ReferenceYawSpeed { get; }
    public Vector2 RotateRate { get; }
    public float TurnYaw { get; }
    public float TurnSpeed { get; }
    public Vector2 TurnDelay { get; }
    public float Turn180Yaw { get; }
    public float TurnBlend { get; }
    public float DynamicDistance { get; }
    public float DynamicBlend { get; }
    public float DynamicRate { get; }
    private readonly AlsRefactoredTurnAsset[] _turns;
    private readonly string[] _dynamic;
    public ReadOnlySpan<AlsRefactoredTurnAsset> Turns => _turns;
    public string DynamicSequence(bool crouching, bool left) => _dynamic[(crouching ? 2 : 0)+(left ? 0 : 1)];
    public AlsRefactoredRestSettings(string json, AlsRefactoredAnimationCatalog catalog)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        Expect(root,new {schemaVersion = 1, source = AlsRefactoredMovementSettings.Source, animationClass = "/ALS/ALS/Character/AB_Als.AB_Als_C"});
        CatalogDigest = catalog.IndexDigest;
        if (!root.GetProperty("catalogSha256").GetString()!.Equals(CatalogDigest,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Foreign rest settings catalog.");
        var native = catalog.Read(AlsRefactoredMovementSettings.Source).GetProperty("nativeText").GetString()!;
        var r = root.GetProperty("rotate"); var t = root.GetProperty("turn"); var d = root.GetProperty("dynamic");
        RotateYaw = Number(r,"yawThreshold",180); FirstPersonYaw = Number(r,"firstPersonYawThreshold",180);
        ReferenceYawSpeed = Pair(r,"referenceYawSpeed"); RotateRate = Pair(r,"playRate");
        TurnYaw = Number(t,"yawThreshold",180); TurnSpeed = Number(t,"yawSpeedThreshold"); TurnDelay = Pair(t,"activationDelay");
        Turn180Yaw = Number(t,"turn180Threshold",180); TurnBlend = Number(t,"blendDuration");
        DynamicDistance = Number(d,"distanceThreshold"); DynamicBlend = Number(d,"blendDuration"); DynamicRate = Number(d,"playRate");
        if (TurnYaw >= 180 || ReferenceYawSpeed.X >= ReferenceYawSpeed.Y || RotateRate.X <= 0 || RotateRate.Y < RotateRate.X)
            throw new ArgumentException("Unsupported rest range.");
        string[] fields = ["StandingTurn90Left","StandingTurn90Right","StandingTurn180Left","StandingTurn180Right",
            "CrouchingTurn90Left","CrouchingTurn90Right","CrouchingTurn180Left","CrouchingTurn180Right"];
        string[] bindings = ["standing_turn90_left","standing_turn90_right","standing_turn180_left","standing_turn180_right",
            "crouching_turn90_left","crouching_turn90_right","crouching_turn180_left","crouching_turn180_right"];
        var rows = t.GetProperty("assets").EnumerateArray().ToArray(); if (rows.Length != 8) throw new ArgumentException("Incomplete turn assets.");
        _turns = new AlsRefactoredTurnAsset[8];
        for (var i = 0; i < 8; i++)
        {
            var row = rows[i]; var source = row.GetProperty("source").GetString()!; var sequence = row.GetProperty("sequence").GetString()!;
            var angle = row.GetProperty("animatedTurnAngle").GetSingle(); var rate = Number(row,"playRate");
            if (row.GetProperty("binding").GetString() != bindings[i] || !source.StartsWith(AlsRefactoredMovementSettings.Source+":",StringComparison.Ordinal) ||
                !native.Contains(fields[i]+"=\"/Script/ALS.AlsTurnInPlaceSettings'"+source.Split(':')[^1]+"'\"",StringComparison.Ordinal) ||
                !float.IsFinite(angle) || MathF.Abs(angle) is < .0001f or > 180 || rate <= 0)
                throw new ArgumentException("Invalid turn binding/settings.");
            Expect(catalog.Read(sequence),new {@class = "AnimSequence"});
            var block = Regex.Match(native,"(?ms)^   Begin Object Name=\""+Regex.Escape(source.Split(':')[^1])+"\"[^\\n]*\\n(.*?)^   End Object");
            if (!block.Success || !block.Groups[1].Value.Contains("Sequence=\"/Script/Engine.AnimSequence'"+sequence+"'\"",StringComparison.Ordinal))
                throw new ArgumentException("Turn sequence differs from its original settings object.");
            _turns[i] = new(bindings[i],source,sequence,rate,angle,row.GetProperty("scalePlayRate").GetBoolean());
        }
        _dynamic = new string[4];
        for (var i = 0; i < 4; i++)
        {
            var key = (i < 2 ? "standing" : "crouching")+"_"+(i%2 == 0 ? "left" : "right");
            var field = (i < 2 ? "Standing" : "Crouching")+(i%2 == 0 ? "Left" : "Right")+"Sequence";
            var path = d.GetProperty("sequences").GetProperty(key).GetString()!;
            var dynamicText = Regex.Match(native,"(?m)^   DynamicTransitions=([^\\r\\n]+)").Value;
            if (!dynamicText.Contains(field+"=\"/Script/Engine.AnimSequence'"+path+"'\"",StringComparison.Ordinal)) throw new ArgumentException("Dynamic transition binding differs.");
            Expect(catalog.Read(path),new {@class = "AnimSequence"}); _dynamic[i] = path;
        }
    }
    private static float Number(JsonElement owner,string name,float max = float.MaxValue)
    {
        var value = owner.GetProperty(name).GetSingle();
        if (!float.IsFinite(value) || value < 0 || value > max) throw new ArgumentException("Invalid rest setting: "+name);
        return value;
    }
    private static Vector2 Pair(JsonElement owner,string name)
    {
        var pair = owner.GetProperty(name); if (pair.GetArrayLength() != 2) throw new ArgumentException("Invalid rest range.");
        var x = pair[0].GetSingle(); var y = pair[1].GetSingle();
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || y < 0) throw new ArgumentException("Invalid rest range.");
        return new(x,y);
    }
}
