using System.Numerics;
using System.Text.Json;

namespace GodotAls.Import.Compilation;

public sealed record AlsMovementInputStateDefaults(Vector4 VelocityBlend, Vector2 Lean, Vector3 RelativeAcceleration,
    float DiagonalScale, float StandingPlayRate, float Speed, bool ShouldMove);

public static class AlsMovementInputStateCompiler
{
    public static AlsMovementInputStateDefaults Compile(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        Require(root.GetProperty("source").GetString() == AlsMovementInputCurveCompiler.Source &&
            root.GetProperty("groundedInputSchemaVersion").GetInt32() == 1, "Wrong state default source.");
        var defaults = root.GetProperty("movementInputStateDefaults");
        Require(defaults.EnumerateObject().Count() == 7, "Incomplete input state defaults.");
        var velocity = Struct("VelocityBlend", "/Game/AdvancedLocomotionV4/Data/Structs/VelocityBlend.VelocityBlend", 4);
        var lean = Struct("LeanAmount", "/Game/AdvancedLocomotionV4/Data/Structs/LeanAmount.LeanAmount", 2);
        var acceleration = Struct("RelativeAccelerationAmount", "/Script/CoreUObject.Vector", 3);
        Require(defaults.GetProperty("ShouldMove").GetProperty("propertyClass").GetString() == "BoolProperty", "Wrong ShouldMove default type.");
        return new(new(Number(velocity, "f"), Number(velocity, "b"), Number(velocity, "l"), Number(velocity, "r")),
            new(Number(lean, "lR"), Number(lean, "fB")), new(Number(acceleration, "y"), Number(acceleration, "z"), -Number(acceleration, "x")),
            Scalar("DiagonalScaleAmount"), Scalar("StandingPlayRate"), Scalar("Speed") * .01f, defaults.GetProperty("ShouldMove").GetProperty("value").GetBoolean());

        JsonElement Struct(string name, string path, int fields)
        {
            var value = defaults.GetProperty(name);
            Require(value.GetProperty("propertyClass").GetString() == "StructProperty" && value.GetProperty("struct").GetString() == path,
                "Wrong input state structure: " + name);
            var contents = value.GetProperty("value"); Require(contents.EnumerateObject().Count() == fields, "Wrong state structure fields.");
            return contents;
        }
        float Scalar(string name)
        {
            var value = defaults.GetProperty(name);
            Require(value.GetProperty("propertyClass").GetString() == "DoubleProperty", "Wrong numeric state default type.");
            return Number(value, "value");
        }
    }
    private static float Number(JsonElement value, string name)
    { var result = value.GetProperty(name).GetSingle(); Require(float.IsFinite(result), "Non-finite input state default."); return result; }
    private static void Require(bool value, string message) { if (!value) throw new FormatException(message); }
}
