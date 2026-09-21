using System.Text.Json;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

public static class AlsContactDetectorCompiler
{
    // This observation describes the non-MACD path even though the project
    // allows MACD. No claim that MACD or CCD is implemented by this compiler.
    public static AlsContactDetectorSettings Compile(string json)
    {
        try
        {
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            if(root.GetProperty("schemaVersion").GetInt32()!=1||
                root.GetProperty("observation").GetString()!="Actual isolated project world detector settings after tick; native FParticlePairMidPhase Init/GenerateCollisions observed before narrow phase; synthetic particle bounds and PreV; non-MACD, no geometry/trajectory parity")
                throw new InvalidDataException("Detector observation/schema differs.");
            var d=root.GetProperty("detector");var v=root.GetProperty("cvars");
            var result=new AlsContactDetectorSettings(d.GetProperty("boundsExpansion").GetDouble(),
                v.GetProperty("p.Chaos.Collision.CullDistanceReferenceSize").GetSingle(),
                v.GetProperty("p.Chaos.Collision.MinCullDistanceScale").GetSingle(),
                d.GetProperty("velocityInflation").GetDouble(),d.GetProperty("maximumVelocityExpansion").GetDouble());
            result.Validate();return result;
        }
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {throw new InvalidDataException("Invalid observed contact detector settings.",e);}
    }
}
