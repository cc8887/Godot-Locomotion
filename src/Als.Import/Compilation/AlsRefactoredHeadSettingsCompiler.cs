using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Import.Compilation;

public static class AlsRefactoredHeadSettingsCompiler
{
    public static AlsRefactoredHeadSettings Compile(string inputsJson,string graphsJson)
    {
        using var document=JsonDocument.Parse(inputsJson);var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1 ||
            root.GetProperty("parentClass").GetString()!="/ALS/ALS/Character/AB_Als.AB_Als_C" ||
            !Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(graphsJson))).Equals(
                root.GetProperty("graphsSha256").GetString(),StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Head settings graph or parent differs.");
        var settings=root.GetProperty("settings");
        if(settings.GetProperty("source").GetString()!="/ALS/ALS/Data/AnimationInstance/AIS_Als_Default.AIS_Als_Default")
            throw new ArgumentException("Foreign Head settings asset.");
        var head=settings.GetProperty("head");
        string[] names=["pitch_angle_interpolation_half_life","yaw_angle_interpolation_half_life",
            "switch_look_sides_yaw_angle_interpolation_half_life","first_person_pitch_angle_interpolation_half_life",
            "first_person_yaw_angle_interpolation_half_life"];
        if(!head.EnumerateObject().Select(p=>p.Name).Order().SequenceEqual(names.Order()))
            throw new ArgumentException("Head settings field closure differs.");
        var values=names.Select(n=>head.GetProperty(n).GetSingle()).ToArray();
        if(values.Any(v=>!float.IsFinite(v)||v<0))throw new ArgumentException("Invalid Head half life.");
        return new(values[0],values[1],values[2],values[3],values[4]);
    }
}
