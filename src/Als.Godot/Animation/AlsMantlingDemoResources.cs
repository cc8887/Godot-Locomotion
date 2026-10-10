using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Load on Main with the character's other immutable resources.
internal sealed class AlsMantlingDemoResources
{
    internal static readonly Lazy<AlsMantlingDemoResources> Shared = new(() => new());
    internal static string Read(string name) => Godot.FileAccess.GetFileAsString(GodotAls.Import.AlsAnimationResourcePack.ConfigRoot + "" + name + ".json");
    internal readonly string AnimationJson = Read("refactored_mantle_animation_inputs");
    internal readonly AlsMantlingMontageProfile Montages;
    internal readonly AlsMantlingMotionProfile Motion;
    internal readonly IReadOnlyDictionary<string, AlsMantlingCurveSource> MontageCurves;
    internal readonly string[] CurveNames;
    internal readonly IReadOnlyDictionary<string,AlsMantlingContactDefinition>? Contacts;
    private AlsMantlingDemoResources()
    {
        var roots = Read("refactored_mantle_root_tracks");
        Montages = AlsMantlingMontageCompiler.Compile(AnimationJson, roots, Read("refactored_mantle_curves"), AlsAnimationRuntimeOptions.SampleRootMotion);
        Motion = AlsMantlingMotionCompiler.Compile(Read("refactored_mantle_inputs"), roots, Read("refactored_mantle_blends"),
            AlsAnimationRuntimeOptions.MantleProgressControl);
        if(AlsAnimationRuntimeOptions.MantleContactControl)
        {
            var file=GodotAls.Import.AlsAnimationResourcePack.ConfigRoot+"refactored_mantle_contacts.json";
            if(!Godot.FileAccess.FileExists(file))
                file="res://assets/generated/mantle_contacts/"+AlsMantlingContactCompiler.Digest(AnimationJson)+"-"+
                    AlsMantlingContactCompiler.Digest(roots)+".json";
            if(!Godot.FileAccess.FileExists(file)) throw new InvalidOperationException("Bake dedicated mantle contacts for the selected final pose/root inputs: "+file);
            Contacts=AlsMantlingContactCompiler.Compile(Godot.FileAccess.GetFileAsString(file),AnimationJson,roots);
            Motion=Motion.WithContactProgress(Contacts,Montages.Definitions);
        }
        MontageCurves = AlsMantlingCurveCompiler.CompileMontages(Read("refactored_mantle_montage_curves"), AnimationJson);
        CurveNames = Montages.Curves.Values.Concat(MontageCurves.Values).SelectMany(c => c.Names.ToArray()).Distinct().ToArray();
    }
}
