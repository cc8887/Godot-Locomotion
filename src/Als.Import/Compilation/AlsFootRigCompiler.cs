using System.Text.Json;
using GodotAls.Core.Locomotion;
using Graph = GodotAls.Import.Compilation.AlsFootEnvironmentCompiler.Graph;

namespace GodotAls.Import.Compilation;

public sealed record AlsFootRigDefinition(AlsRefactoredLegRigDefinition Leg, string MovingCurve,
    string LeftIkCurve, string RightIkCurve, AlsDoubleVector LeftPrimary, AlsDoubleVector LeftSecondary,
    AlsDoubleVector RightPrimary, AlsDoubleVector RightSecondary)
{
    // PrepareForExecution uses the imported INITIAL component pose, not the
    // animated/current pose and not the displayed FootHeight pin default.
    public AlsFootRigSkeleton Bind(IReadOnlyList<string> names, ReadOnlySpan<int> parents,
        ReadOnlySpan<AlsPrecisePose> reference, bool useFootIkBones)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count != parents.Length || names.Count != reference.Length)
            throw new InvalidDataException("Rig skeleton sizes differ.");
        // FRigElementKey uses FName identity. FBX display casing (Pelvis versus
        // pelvis) must not prevent binding, or permit ambiguous duplicate bones.
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Count; i++)
        {
            if (!indices.TryAdd(names[i], i) || parents[i] < -1 || parents[i] >= i)
                throw new InvalidDataException("Rig skeleton requires unique, parent-first bones.");
            reference[i].Validate();
        }
        int Bone(string name) => indices.TryGetValue(name, out var index) ? index :
            throw new InvalidDataException("Missing rig bone: " + name);
        var pelvis = Bone("pelvis");
        var left = new AlsRigLegBones(pelvis, Bone("thigh_l"), Bone("calf_l"), Bone("foot_l"), LeftPrimary, LeftSecondary);
        var right = new AlsRigLegBones(pelvis, Bone("thigh_r"), Bone("calf_r"), Bone("foot_r"), RightPrimary, RightSecondary);
        ValidateChain(left, parents); ValidateChain(right, parents);
        // Native ChainLength recurses to the ancestor, then adds each distance
        // as double before rounding the accumulated result to float at each edge.
        var chain = new List<int>();
        for (var index = left.Foot; index != left.Thigh; index = parents[index]) chain.Add(index);
        float length = 0;
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var child = chain[i];
            length = (float)(length + Math.Sqrt((reference[child].Position - reference[parents[child]].Position).LengthSquared));
        }
        var height = (float)reference[left.Foot].Position.Z;
        if (!float.IsFinite(length) || length <= 0 || !float.IsFinite(height) || height < 0)
            throw new InvalidDataException("Invalid reference foot height/chain length.");
        return new(left, right, Bone(useFootIkBones ? "ik_foot_root" : "VB foot_root"), height, length);
    }

    private static void ValidateChain(AlsRigLegBones bones, ReadOnlySpan<int> parents)
    {
        if (bones.Pelvis >= bones.Thigh || bones.Thigh >= bones.Calf || bones.Calf >= bones.Foot ||
            !Descends(bones.Thigh, bones.Pelvis, parents) || !Descends(bones.Calf, bones.Thigh, parents) ||
            !Descends(bones.Foot, bones.Calf, parents)) throw new InvalidDataException("Invalid rig leg ancestry.");
    }
    private static bool Descends(int bone, int ancestor, ReadOnlySpan<int> parents)
    {
        for (var p = parents[bone]; p >= 0; p = parents[p]) if (p == ancestor) return true;
        return false;
    }
}

public readonly record struct AlsFootRigSkeleton(AlsRigLegBones Left, AlsRigLegBones Right,
    int FootRoot, float FootHeight, float LegLength);

// Compile the exact authored foot function and its initialization/dispatch.
// This does not assert V4 curve equivalence or own the full animation frame.
public static class AlsFootRigCompiler
{
    public static AlsRefactoredFootAnimationFrame CreateAnimationFrame(string rigJson, string environmentJson,
        string[] bones, ReadOnlySpan<int> parents, ReadOnlySpan<AlsPrecisePose> localFbxReference, ReadOnlySpan<string> curves,
        bool captureLockTrace = false, AlsBasedFootLockSettings? lockSettings = null, AlsFootSupportGeometry? supportGeometry = null)
    {
        var nativeReference = new AlsPrecisePose[localFbxReference.Length];
        AlsRefactoredFootAnimationFrame.ToNativeComponents(localFbxReference, parents, nativeReference);
        var rig = CreateFrame(rigJson, environmentJson, bones, parents, nativeReference, curves, true, supportGeometry);
        return new(rig, bones, parents, localFbxReference, curves, captureLockTrace, lockSettings);
    }

    public static AlsRefactoredFootRigFrame CreateFrame(string rigJson, string environmentJson,
        IReadOnlyList<string> bones, ReadOnlySpan<int> parents, ReadOnlySpan<AlsPrecisePose> reference,
        ReadOnlySpan<string> curves, bool useFootIkBones, AlsFootSupportGeometry? supportGeometry = null)
    {
        var rig = Compile(rigJson);
        var environment = AlsFootEnvironmentCompiler.Compile(environmentJson);
        var skeleton = rig.Bind(bones, parents, reference, useFootIkBones);
        Require(rig.LeftIkCurve == environment.LeftIkCurve && rig.RightIkCurve == environment.RightIkCurve,
            "Foot query/IK curve contracts differ.");
        return new(new(rig.Leg, environment.Pelvis,
            new(environment.TraceUpward, environment.TraceDownward, skeleton.FootHeight, environment.WalkableAngle),
            environment.EnableThreshold, skeleton.Left, skeleton.Right, skeleton.LegLength,
            rig.LeftIkCurve, rig.RightIkCurve, rig.MovingCurve), parents, reference, curves, supportGeometry);
    }

    public static AlsFootRigDefinition Compile(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Require(root.GetProperty("schemaVersion").GetInt32() == 1 &&
            root.GetProperty("source").GetString() == "/ALS/ALS/Character/CR_Als.CR_Als", "Unexpected rig source/schema.");
        var model = new Graph(root.GetProperty("rigModel"), "1011708A536374053BB1ECC20E8D9F120F294370D022D0381FC3D7271ADC5040");
        var feet = new Graph(root.GetProperty("feet"), "93E2499B178894BBD2F982D8E673F9CC3325F5E75ABBA560B0DE32066329CCDB");
        var leg = new Graph(root.GetProperty("applyFoot"), "05FECA1C88B53C54FDC1F2125A1899125EB842F9C0E08880B19F5E29AC3E7BE9");
        foreach (var function in new[] { "RefreshDiagonalScaling", "RefreshSpineRotation", "RefreshFootOffset", "RefreshPelvisOffset", "RefreshFootIk", "RefreshHandIk" })
            model.Reference(function, function);
        Bone(model, "GetTransform", "Item", "foot_l");
        model.Value("GetTransform", "bInitial", "true"); model.Value("GetTransform", "Space", "GlobalSpace");
        Bone(model, "AlsRigUnit_ChainLength", "AncestorItem", "thigh_l");
        Bone(model, "AlsRigUnit_ChainLength", "DescendantItem", "foot_l");
        model.Value("AlsRigUnit_ChainLength", "bInitial", "true");
        Bone(model, "If_2", "True", "ik_foot_root"); Bone(model, "If_2", "False", "VB foot_root");
        feet.Reference("ApplyFootIk", "ApplyFootIk"); feet.Reference("ApplyFootIk_1", "ApplyFootIk");
        foreach (var (node, side) in new[] { ("ApplyFootIk", "l"), ("ApplyFootIk_1", "r") })
        {
            Bone(feet, node, "PelvisItem", "pelvis"); Bone(feet, node, "ThighItem", "thigh_" + side);
            Bone(feet, node, "CalfItem", "calf_" + side); Bone(feet, node, "FootItem", "foot_" + side);
        }
        feet.Value("GetCurveValue_2", "Curve", "FootLeftIk"); feet.Value("GetCurveValue_2_1", "Curve", "FootRightIk");
        leg.Value("GetCurveValue", "Curve", "PoseMoving");
        const string ik = "TwoBoneIKSimplePerItem", offset = "AlsRigUnit_ApplyFootOffsetLocation", rotation = "AlsRigUnit_ApplyFootOffsetRotation";
        leg.Value(ik, "bEnableStretch", "false"); leg.Value(ik, "bPropagateToChildren", "true");
        leg.Value(ik, "PoleVectorKind", "Location"); leg.Value(ik, "PoleVectorSpace.Name", "None");
        Require(leg.Number(ik, "ItemALength", 0) == 0 && leg.Number(ik, "ItemBLength", 0) == 0, "Changed reference-length policy.");
        foreach (var axis in new[] { "X", "Y", "Z" }) Require(leg.Number(ik, "Effector.Scale3D." + axis) == 1, "Changed effector scale.");
        leg.Value("Set Transform", "Space", "GlobalSpace"); leg.Value("Set Transform", "bInitial", "False");
        leg.Value("Set Transform", "bPropagateToChildren", "True");
        var definition = new AlsRefactoredLegRigDefinition(
            F(leg.Number("MathVectorScale_2", "Factor")), F(leg.Number("AlsRigVMFunction_ExponentialDecayVector", "HalfLife")),
            F(leg.Number(offset, "OffsetInterpolationFrequency")), F(leg.Number(offset, "OffsetInterpolationDampingRatio")),
            F(leg.Number(offset, "OffsetInterpolationTargetVelocityAmount")), F(leg.Number(offset, "MaxLegStretchRatio")),
            Range(leg, "Interpolate_2"), Interval(leg, rotation, "Swing1LimitAngle"), Range(leg, "Interpolate"), Range(leg, "Interpolate_1"),
            Interval(leg, rotation, "TwistLimitAngle"), F(leg.Number(rotation, "OffsetInterpolationHalfLife")), F(leg.Number(ik, "SecondaryAxisWeight")));
        Require(definition.PoleHalfLife >= 0 && definition.OffsetFrequency >= 0 && definition.OffsetDamping >= 0 &&
            definition.MaximumStretch >= 0 && definition.RotationHalfLife >= 0, "Invalid rig time/length parameters.");
        return new(definition, "PoseMoving", "FootLeftIk", "FootRightIk",
            Vector(feet, "ApplyFootIk", "PrimaryAxis"), Vector(feet, "ApplyFootIk", "SecondaryAxis"),
            Vector(feet, "ApplyFootIk_1", "PrimaryAxis"), Vector(feet, "ApplyFootIk_1", "SecondaryAxis"));
    }

    private static void Bone(Graph graph, string node, string pin, string name)
    { graph.Value(node, pin + ".Type", "Bone"); graph.Value(node, pin + ".Name", name); }
    private static AlsDoubleVector Vector(Graph g, string n, string p) => new(g.Number(n, p + ".X"), g.Number(n, p + ".Y"), g.Number(n, p + ".Z"));
    private static AlsRigDoubleRange Range(Graph g, string n) => new(g.Number(n, "A"), g.Number(n, "B"));
    private static AlsFootRotationInterval Interval(Graph g, string n, string p) => new(F(g.Number(n, p + ".Min")), F(g.Number(n, p + ".Max")));
    private static float F(double value)
    { Require(float.IsFinite((float)value), "Rig parameter exceeds float range."); return (float)value; }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
