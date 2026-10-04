using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

public partial class LyraLogicalHandChainSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Logical native hand chain failed: " + error); GetTree().Quit(1); }
    }
    private void Run()
    {
        using var rig = LyraBoundRig.Build(includeAuxiliary: true, includeRemaining: true, includePistol: true, includeRifle: true);
        AddChild(rig.Root);
        var bank = LyraLogicalSourceBank.Load();
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.Root + "hand_chain_native.json"));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("calibrationSha256").GetString() != bank.CalibrationSha256 ||
            root.GetProperty("catalogSha256").GetString() != bank.CatalogSha256)
            throw new InvalidOperationException("Stale logical hand chain oracle.");
        ILyraItemAnimationLayers[] providers = [new LyraUnarmedAnimationLayers(rig.Catalog, rig.Auxiliary, rig.Remaining),
            new LyraPistolAnimationLayers(rig.Pistol!), new LyraRifleAnimationLayers(rig.Rifle!)];
        var items = providers.ToDictionary(v => v.Name.ToLowerInvariant(), v => new LyraItemLayerInstance(rig, v, LyraLinkedLayerContracts.Load(), bank));
        var a = new AlsPrecisePose[81]; var b = new AlsPrecisePose[81];
        double maxPosition = 0, maxRotation = 0, maxScale = 0;
        var cases = 0; var partial = 0;
        foreach (var row in root.GetProperty("rows").EnumerateArray())
        {
            var item = items[row.GetProperty("profile").GetString()!]; var input = row.GetProperty("pose");
            for (var bone = 0; bone < 81; bone++) a[bone] = LyraLogicalSourceBank.ParsePose(input[bone]);
            var original = a.ToArray(); var expected = row.GetProperty("native").GetProperty("stages");
            item.HandRetarget.EvaluatePose(a, row.GetProperty("retargetDisable").GetSingle(), b);
            if (!a.SequenceEqual(original)) throw new InvalidOperationException("Hand chain mutated its source.");
            Compare(expected[0], b, cases, "hand", ref maxPosition, ref maxRotation, ref maxScale);
            item.LeftHandIk!.CopyTarget(b, a);
            Compare(expected[1], a, cases, "copy", ref maxPosition, ref maxRotation, ref maxScale);
            item.RightHandIk.EvaluatePose(a, row.GetProperty("rightDisable").GetSingle(), b);
            Compare(expected[2], b, cases, "right", ref maxPosition, ref maxRotation, ref maxScale);
            item.LeftHandIk.EvaluatePose(b, row.GetProperty("leftDisable").GetSingle(), a);
            Compare(expected[3], a, cases, "left", ref maxPosition, ref maxRotation, ref maxScale);
            if (item.HandRetarget.Alpha != row.GetProperty("retargetAlpha").GetSingle() ||
                item.RightHandIk.Alpha != row.GetProperty("rightAlpha").GetSingle() ||
                item.LeftHandIk.Alpha != row.GetProperty("leftAlpha").GetSingle())
                throw new InvalidOperationException("Original curve/CDO gate differs.");
            if (item.LeftHandIk.Alpha is > 0 and < 1) partial++;
            cases++;
        }
        if (cases != 48 || partial != 16) throw new InvalidOperationException("Incomplete hand-chain coverage.");
        GD.Print($"LYRA_LOGICAL_HAND_CHAIN_OK cases={cases} stages=4 logical=81 partial={partial} " +
            $"positionCm={maxPosition} quaternion={maxRotation} scale={maxScale} native=oneFCSPose left=takeRotation");
    }
    private static void Compare(JsonElement expected, ReadOnlySpan<AlsPrecisePose> actual, int sample, string stage,
        ref double maxPosition, ref double maxRotation, ref double maxScale)
    {
        for (var bone = 0; bone < 81; bone++)
        {
            var target = LyraLogicalSourceBank.ParsePose(expected[bone]); var value = actual[bone];
            var p = Math.Sqrt((value.Position - target.Position).LengthSquared);
            var sign = AlsQuaternion.Dot(value.Rotation, target.Rotation) < 0 ? -1 : 1;
            var q = Math.Sqrt((value.Rotation.Normalized() + target.Rotation.Normalized() * -sign).LengthSquared);
            var s = Math.Sqrt((value.Scale - target.Scale).LengthSquared);
            maxPosition = Math.Max(maxPosition, p); maxRotation = Math.Max(maxRotation, q); maxScale = Math.Max(maxScale, s);
            if (p > 1e-8 || q > 1e-10 || s > 1e-12)
                throw new InvalidOperationException($"Native hand chain differs sample={sample} stage={stage} bone={bone}: p={p} q={q} s={s}.");
        }
    }
}
