namespace GodotAls.Core.Physics;

// Two separate boundaries: solver marshalling resolves negative external-body
// overrides first; constraint Setup then combines the internal particle values.
public static class AlsInitialOverlapSettings
{
    // FPBDRigidsSolver::ProcessSinglePushedData_Internal, DynamicMisc path.
    public static float ResolveParticle(float externalVelocity, float solverVelocity)
    {
        if (!float.IsFinite(externalVelocity) || !float.IsFinite(solverVelocity) || solverVelocity < 0)
            throw new ArgumentException("Invalid external/solver overlap velocity.");
        return externalVelocity < 0 ? solverVelocity : externalVelocity;
    }

    public static float Resolve(float body0, float body1)
    {
        if (!float.IsFinite(body0) || !float.IsFinite(body1))
            throw new ArgumentException("Initial overlap velocities must be finite.");
        return MathF.Max(MathF.Max(body0, body1), 0);
    }
}
