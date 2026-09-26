using GodotAls.Import.Compilation;

namespace GodotAls.Animation;

// Load on Main with the character's other immutable resources.
internal sealed class AlsMantlingDemoResources
{
    internal static readonly Lazy<AlsMantlingDemoResources> Shared = new(() => new());
    internal static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name + ".json");
    internal readonly string AnimationJson = Read("refactored_mantle_animation_inputs");
    internal readonly AlsMantlingMontageProfile Montages;
    internal readonly AlsMantlingMotionProfile Motion;
    internal readonly IReadOnlyDictionary<string, AlsMantlingCurveSource> MontageCurves;
    internal readonly string[] CurveNames;
    private AlsMantlingDemoResources()
    {
        var roots = Read("refactored_mantle_root_tracks");
        Montages = AlsMantlingMontageCompiler.Compile(AnimationJson, roots, Read("refactored_mantle_curves"));
        Motion = AlsMantlingMotionCompiler.Compile(Read("refactored_mantle_inputs"), roots, Read("refactored_mantle_blends"));
        MontageCurves = AlsMantlingCurveCompiler.CompileMontages(Read("refactored_mantle_montage_curves"), AnimationJson);
        CurveNames = Montages.Curves.Values.Concat(MontageCurves.Values).SelectMany(c => c.Names.ToArray()).Distinct().ToArray();
    }
}
