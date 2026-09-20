using GodotAls.Core.Contracts;

namespace GodotAls.Core.Locomotion;

public enum AlsRefactoredPoseCurveSite { Grounded, FallPose, FallFeet, JumpPose, JumpFeet, Land, StandingMovement, CrouchingMovement }
public sealed record AlsRefactoredPoseCurveWrite(AlsRefactoredPoseCurveSite Site, string SourceNode,
    string[] Names, float[] Values, bool UseGroundPrediction);

// These are graph-local writes. Call them at the authored node BEFORE its
// enclosing state/cache/slot/inertial blends, never stamp them on the final pose.
public sealed class AlsRefactoredPoseCurveRuntime
{
    private readonly (int[] Indices, float[] Values, bool Prediction)[] _sites;
    private readonly int _curveCount;
    private readonly bool[] _enabled = new bool[8];
    public AlsRefactoredPoseCurveRuntime(IReadOnlyList<AlsRefactoredPoseCurveWrite> definitions, ReadOnlySpan<string> layout,
        ReadOnlySpan<AlsRefactoredPoseCurveSite> sites = default)
    {
        _curveCount = layout.Length; _sites = new (int[], float[], bool)[8];
        if (sites.IsEmpty) Array.Fill(_enabled, true);
        else foreach (var requested in sites)
        {
            if ((uint)requested >= 8 || _enabled[(int)requested]) throw new ArgumentException("Invalid/duplicate curve binding site.");
            _enabled[(int)requested] = true;
        }
        var names = layout.ToArray(); var assigned = new bool[8];
        foreach (var definition in definitions)
        {
            var site = (int)definition.Site;
            if ((uint)site >= 8 || assigned[site] || definition.Names.Length == 0 || definition.Names.Length != definition.Values.Length)
                throw new ArgumentException("Invalid Refactored curve write definitions.");
            assigned[site] = true;
            // A nested cache only carries the names produced/read in that cache.
            // Other graph sites still require their own explicit layout binding.
            if (!_enabled[site]) continue;
            var indices = new int[definition.Names.Length];
            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = Array.IndexOf(names, definition.Names[i]);
                if (indices[i] < 0 || Array.LastIndexOf(names, definition.Names[i]) != indices[i] || !float.IsFinite(definition.Values[i]))
                    throw new ArgumentException("Missing/duplicate/nonfinite Refactored curve: " + definition.Names[i]);
            }
            _sites[site] = (indices, definition.Values.ToArray(), definition.UseGroundPrediction);
        }
        if (assigned.Any(value => !value)) throw new ArgumentException("Incomplete Refactored curve sites.");
    }
    public void Apply(AlsRefactoredPoseCurveSite site, Span<AlsInertialCurve> curves, float groundPrediction)
    {
        if ((uint)site >= _sites.Length || !_enabled[(int)site] || curves.Length != _curveCount || !float.IsFinite(groundPrediction))
            throw new ArgumentException("Invalid graph curve write inputs.");
        var write = _sites[(int)site];
        var alpha = write.Prediction ? groundPrediction : 1f;
        for (var i = 0; i < write.Indices.Length; i++)
        {
            var index = write.Indices[i];
            if (!float.IsFinite(curves[index].Value)) throw new ArgumentException("Nonfinite source curve.");
            curves[index] = AlsStandingCycleCurves.ModifyBlend(curves[index], write.Values[i], alpha);
        }
    }
}

public readonly record struct AlsRefactoredPoseCurveHistory(AlsFrameIdentity Identity, float Grounded, float InAir, float Moving)
{
    // Actual UAlsAnimationInstance::GetControlRigInput, from Update's cached
    // PoseState and the current InAirState prediction, not current rig curves.
    public float PelvisAmount(float groundPrediction)
    {
        if (!float.IsFinite(Grounded) || !float.IsFinite(InAir) || !float.IsFinite(Moving) || !float.IsFinite(groundPrediction))
            throw new ArgumentException("Invalid cached Refactored pose/prediction.");
        var value = Grounded + InAir * groundPrediction;
        if (!float.IsFinite(value)) throw new ArgumentException("Refactored pelvis input overflow.");
        return value > 0 ? value < 1 ? value : 1 : 0;
    }
    public AlsRefactoredFootRigInput Apply(in AlsRefactoredFootRigInput input, float groundPrediction)
    {
        if (input.PreviousIdentity != Identity) throw new ArgumentException("Pose history and foot input identities differ.");
        return input with { PelvisAmount = PelvisAmount(groundPrediction) };
    }
}

public sealed class AlsRefactoredPoseCurveReader
{
    private readonly int _grounded, _air, _moving, _count;
    public AlsRefactoredPoseCurveReader(ReadOnlySpan<string> curves)
    {
        var layout = curves.ToArray(); _count = layout.Length;
        _grounded = Find("PoseGrounded"); _air = Find("PoseInAir"); _moving = Find("PoseMoving");
        int Find(string name)
        {
            var index = Array.IndexOf(layout, name);
            return index >= 0 && Array.LastIndexOf(layout, name) == index ? index :
                throw new ArgumentException("Missing/duplicate pose history curve: " + name);
        }
    }
    public AlsRefactoredPoseCurveHistory Read(AlsFrameIdentity identity, ReadOnlySpan<AlsInertialCurve> committedCurves)
    {
        if (committedCurves.Length != _count) throw new ArgumentException("Pose history layout differs.");
        return new(identity, Value(committedCurves[_grounded]), Value(committedCurves[_air]), Value(committedCurves[_moving]));
    }
    private static float Value(AlsInertialCurve curve) => !curve.Present ? 0 :
        float.IsFinite(curve.Value) ? curve.Value : throw new ArgumentException("Nonfinite pose history curve.");
}
