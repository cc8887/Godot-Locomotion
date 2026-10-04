using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Actions;

namespace GodotAls.Animation.Lyra;

// Shared immutable original Notify templates. Attaching the component is an
// explicit gameplay choice; the inspected Shooter configuration has none.
internal sealed class LyraMotionWarpingProfile
{
    public ImmutableArray<AlsMotionWarpingWindow> Windows { get; }
    public LyraMotionWarpingProfile(LyraMontageCatalog catalog)
    {
        const string root="res://assets/generated/lyra_als/";
        var policyBytes=Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_policy.json");
        using var pd=JsonDocument.Parse(policyBytes);var policy=pd.RootElement;
        using var ud=JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(root+"motion_warping_v1_usage.json"));var usage=ud.RootElement;
        if(policy.GetProperty("schemaVersion").GetInt32()!=1||usage.GetProperty("schemaVersion").GetInt32()!=1||
            usage.GetProperty("policySha256").GetString()!=LyraLogicalSourceBank.Sha(policyBytes))throw new InvalidOperationException("Stale MotionWarping profile.");
        foreach(var d in policy.GetProperty("dependencies").EnumerateObject())
            if(d.Value.GetString()!=LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name)))throw new InvalidOperationException("Stale MotionWarping dependency.");
        var windows=ImmutableArray.CreateBuilder<AlsMotionWarpingWindow>();
        foreach(var w in policy.GetProperty("windows").EnumerateArray())
        {
            var p=w.GetProperty("properties");var path=w.GetProperty("montage").GetString()!;int asset=catalog.Paths.IndexOf(path);
            if(asset<0||!catalog.Definitions[asset].RootMotionEnabled||w.GetProperty("modifierClass").GetString()!="/Script/MotionWarping.RootMotionModifier_SkewWarp"||
                p.GetProperty("warp_target_name").GetString()!="Align"||!p.GetProperty("warp_translation").GetBoolean()||!p.GetProperty("ignore_z_axis").GetBoolean()||
                !p.GetProperty("warp_to_feet_location").GetBoolean()||!p.GetProperty("warp_rotation").GetBoolean()||p.GetProperty("subtract_remaining_root_motion").GetBoolean()||
                p.GetProperty("add_translation_easing_func").GetProperty("value").GetInt32()!=0||p.GetProperty("add_translation_easing_curve").ValueKind!=JsonValueKind.Null||
                p.GetProperty("rotation_method").GetProperty("value").GetInt32()!=0||p.GetProperty("rotation_type").GetProperty("value").GetInt32()!=0||
                p.GetProperty("warp_rotation_time_multiplier").GetSingle()!=1||p.GetProperty("warp_max_rotation_rate").GetSingle()!=0||p.GetProperty("max_speed_clamp_ratio").GetSingle()!=0||
                p.GetProperty("additional_rotation_offset").EnumerateArray().Any(v=>v.GetDouble()!=0))throw new NotSupportedException("Changed original Lyra SkewWarp template.");
            int provider=p.GetProperty("warp_point_anim_provider").GetProperty("value").GetInt32();
            if(provider is not(0 or 1))throw new NotSupportedException("Unsupported warp point provider.");
            var end=usage.GetProperty("trace").GetProperty("windowEndRoots").EnumerateArray().Single(v=>v.GetProperty("montage").GetString()==path);
            float finish=w.GetProperty("endTriggerTime").GetSingle();
            if(end.GetProperty("end").GetSingle()!=finish)throw new InvalidOperationException("Foreign warp point root time.");
            windows.Add(new(asset,MathF.Max(0,w.GetProperty("triggerTime").GetSingle()),finish,provider==1,
                LyraLogicalSourceBank.ParsePose(p.GetProperty("warp_point_anim_transform")),LyraLogicalSourceBank.ParsePose(end.GetProperty("root"))));
        }
        if(windows.Count!=4)throw new InvalidOperationException("Incomplete MotionWarping profile.");
        Windows=windows.ToImmutable();
    }
}
