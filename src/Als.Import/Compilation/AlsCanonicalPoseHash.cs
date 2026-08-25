using System.Security.Cryptography;
using System.Text;

namespace GodotAls.Import.Compilation;

public static class AlsCanonicalPoseHash
{
    public static string Create(IReadOnlyList<AlsBoneDefinition> bones)
    {
        ArgumentNullException.ThrowIfNull(bones);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(bones.Count);
            foreach (var bone in bones)
            {
                WriteString(writer, bone.Name);
                writer.Write(bone.ParentPhysicalId);
                WriteFloat(writer, bone.Translation.X);
                WriteFloat(writer, bone.Translation.Y);
                WriteFloat(writer, bone.Translation.Z);
                WriteFloat(writer, bone.Rotation.X);
                WriteFloat(writer, bone.Rotation.Y);
                WriteFloat(writer, bone.Rotation.Z);
                WriteFloat(writer, bone.Rotation.W);
                WriteFloat(writer, bone.Scale.X);
                WriteFloat(writer, bone.Scale.Y);
                WriteFloat(writer, bone.Scale.Z);
            }
        }

        stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteFloat(BinaryWriter writer, float value)
    {
        var rounded = MathF.Round(value, 5, MidpointRounding.ToEven);
        writer.Write(rounded == 0f ? 0f : rounded);
    }
}
