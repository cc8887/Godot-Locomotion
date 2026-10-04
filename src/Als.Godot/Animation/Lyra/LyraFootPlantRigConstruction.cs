using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal sealed record LyraRigElementTransforms(AlsPrecisePose Local, AlsPrecisePose Global,
    AlsPrecisePose InitialLocal, AlsPrecisePose InitialGlobal, AlsPrecisePose? OffsetLocal,
    AlsPrecisePose? OffsetGlobal, AlsPrecisePose? InitialOffsetLocal, AlsPrecisePose? InitialOffsetGlobal);

internal sealed record LyraFootPlantRigConstructionResult(AlsDoubleVector LeftFootOffset,
    AlsDoubleVector RightFootOffset, float ThighLength, float CalfLength,
    IReadOnlyDictionary<string, LyraRigElementTransforms> Hierarchy);

// Construction is executed before importing the animation pose. Main73 explicitly
// keeps the original Rig reference pose (bSetRefPoseFromSkeleton=false).
// This implements its single-parent control-offset dependency, not Forwards Solve.
internal sealed class LyraFootPlantRigConstruction
{
    private readonly LyraFootPlantRigHierarchy _reference;

    public LyraFootPlantRigConstruction(JsonElement graph, JsonElement program)
    {
        CheckProgram(program);
        using var settings = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(
            "res://assets/generated/lyra_als/rig_control_settings_v1.json"));
        _reference = new(graph, program.GetProperty("initial").GetProperty("hierarchy"),
            settings.RootElement.GetProperty("controls"));
    }

    public LyraFootPlantRigConstructionResult Execute()
    {
        var hierarchy = _reference.Clone();
        var left = hierarchy.Get("foot_l").Position; var right = hierarchy.Get("foot_r").Position;
        float thigh = (float)Math.Sqrt((hierarchy.Get("calf_l").Position - hierarchy.Get("thigh_l").Position).LengthSquared);
        float calf = (float)Math.Sqrt((hierarchy.Get("foot_l").Position - hierarchy.Get("calf_l").Position).LengthSquared);
        void SetControlOffset(string name, AlsPrecisePose value)
        {
            hierarchy.Set(name, value, offset: true);
            hierarchy.Set(name, value, initial: true, offset: true);
        }
        var pelvis = hierarchy.Get("pelvis"); SetControlOffset("BodyCtrl", pelvis); SetControlOffset("PelvisCtrl", pelvis);
        SetControlOffset("ChestCtrl", hierarchy.Get("spine_03"));
        return new(new(left.X, left.Y, 0), new(right.X, right.Y, 0), thigh, calf, hierarchy.Snapshot());
    }

    internal static AlsPrecisePose Pose(JsonElement row)
    {
        AlsDoubleVector V(string name) { var a = row.GetProperty(name); return new(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()); }
        var q = row.GetProperty("q"); var result = new AlsPrecisePose(V("p"),
            new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()), V("s"));
        result.Validate(); return result;
    }
    private static void CheckProgram(JsonElement program)
    {
        if (program.GetProperty("instructions").GetArrayLength() != 436 ||
            !program.GetProperty("entries").EnumerateArray().Any(e => e.GetProperty("name").GetString() == "Construction" && e.GetProperty("instruction").GetInt32() == 401))
            throw new NotSupportedException("Changed FootPlant Construction entry.");
        var rows = program.GetProperty("instructions");
        foreach (var (index, src, dest, variable) in new[] {
            (404, "Translation.X", "X", "LeftFootOffset"), (405, "Translation.Y", "Y", "LeftFootOffset"),
            (408, "Translation.X", "X", "RightFootOffset"), (409, "Translation.Y", "Y", "RightFootOffset") })
        {
            var operands = rows[index].GetProperty("operands");
            if (operands.GetArrayLength() != 2 || operands[0].GetProperty("path").GetString() != src ||
                operands[1].GetProperty("path").GetString() != dest || operands[1].GetProperty("name").GetString() != variable)
                throw new NotSupportedException("Changed FootPlant Construction copy path: " + index);
        }
        var literal = program.GetProperty("literal");
        foreach (var (name, bone) in new[] {
            ("RigVMModel___RigUnit_GetTransform_1_1_2_Item__Const", "foot_l"),
            ("RigVMModel___RigUnit_GetTransform_2_1_Item__Const", "foot_r"),
            ("RigVMModel___TwoBoneIKSimplePerItem_ItemB__Const", "calf_l"),
            ("RigVMModel___TwoBoneIKSimplePerItem_ItemA__Const", "thigh_l"),
            ("RigVMModel___RigUnit_GetTransform_1_1_1_2_Item__Const", "pelvis"),
            ("RigVMModel___RigUnit_GetTransform_1_1_1_1_1_Item__Const", "spine_03") })
            if (literal.GetProperty(name).GetProperty("Type").GetString() != "Bone" ||
                literal.GetProperty(name).GetProperty("Name").GetString() != bone)
                throw new NotSupportedException("Changed Construction bone: " + name);
        if (literal.GetProperty("RigVMModel___SetTransform_4_2_1_2_Space__Const").GetString() != "GlobalSpace" ||
            literal.GetProperty("RigVMModel___SetTransform_4_2_1_2_bInitial__Const").GetBoolean())
            throw new NotSupportedException("Changed Construction transform space.");
    }
}
