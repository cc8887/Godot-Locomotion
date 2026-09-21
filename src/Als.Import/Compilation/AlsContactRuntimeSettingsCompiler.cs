using System.Text.Json;
using GodotAls.Core.Physics;

namespace GodotAls.Import.Compilation;

public sealed record AlsContactRuntimeSettings(float MaxPushOutVelocity, float RestitutionThreshold,
    bool EnableInitialDepenetration, float[] BodyOverlapVelocities);

public static class AlsContactRuntimeSettingsCompiler
{
    public const string Observation = "Project isolated world after tick: collision solver settings; actual asset external particle overlap settings after creation; synthetic native constraint setup/activation, not geometry or trajectory parity";
    public static AlsContactRuntimeSettings Compile(string json, AlsRagdollPhysicsDefinition definition)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            Require(root.GetProperty("schemaVersion").GetInt32() == 1 && root.GetProperty("observation").GetString() == Observation,
                "Contact settings schema/observation differs.");
            var solver = root.GetProperty("solver"); var cvars = root.GetProperty("cvars");
            Require(!solver.GetProperty("splitImpulse").GetBoolean() &&
                solver.GetProperty("positionFrictionIterations").GetInt32() == 4 &&
                solver.GetProperty("velocityFrictionIterations").GetInt32() == 1 &&
                solver.GetProperty("positionShockIterations").GetInt32() == 3 &&
                solver.GetProperty("velocityShockIterations").GetInt32() == 2 &&
                cvars.GetProperty("p.Chaos.PBDCollisionSolver.RestitutionUsePreIntegrateVelocity").GetInt32() == 0,
                "Contact solver requires unsupported iteration/velocity semantics.");
            var enabled = cvars.GetProperty("p.Chaos.PBDCollisionSolver.EnableInitialDepenetration").GetInt32();
            Require(enabled is 0 or 1, "Invalid initial overlap toggle.");
            var push = solver.GetProperty("maxPushOutVelocity").GetSingle(); var restitution = solver.GetProperty("restitutionThreshold").GetSingle();
            Require(float.IsFinite(push) && push >= 0 && float.IsFinite(restitution) && restitution >= 0, "Invalid contact settings.");
            var rig = root.GetProperty("rigs").EnumerateArray().Single(r => r.GetProperty("mesh").GetString() == definition.Mesh);
            Require(rig.GetProperty("physicsAsset").GetString() == definition.PhysicsAsset, "Contact physics asset differs.");
            var rows = rig.GetProperty("bodies"); Require(rows.GetArrayLength() == definition.Bodies.Length, "Contact body count differs.");
            var velocities = new float[definition.Bodies.Length];
            foreach (var body in definition.Bodies)
            {
                var row = rows[body.Index];
                Require(row.GetProperty("index").GetInt32() == body.Index && row.GetProperty("bone").GetString() == body.Bone &&
                    row.GetProperty("override").GetBoolean() == body.Defaults.GetProperty("bOverrideMaxDepenetrationVelocity").GetBoolean() &&
                    row.GetProperty("authoredVelocity").GetSingle() == body.Defaults.GetProperty("maxDepenetrationVelocity").GetSingle(),
                    "Observed contact body/defaults differ.");
                velocities[body.Index] = row.GetProperty("particleVelocity").GetSingle();
                _ = AlsInitialOverlapSettings.Resolve(velocities[body.Index], 0);
            }
            // Legacy solver depenetrationVelocity is observed but is not used
            // by FPBDCollisionConstraint::Setup; use actual particle values.
            return new(push, restitution, enabled == 1, velocities);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or OverflowException or IndexOutOfRangeException)
        { throw new InvalidDataException("Malformed native contact runtime settings.", e); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
