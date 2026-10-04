using System.Collections.Immutable;
using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Animation.Lyra;

internal readonly record struct LyraContextEffectSelection(ImmutableArray<string> Audio,ImmutableArray<string> Vfx);
internal readonly record struct LyraContextNotifyDefinition(string Effect,string Bone,bool Attached,bool Trace,int Channel,
    bool IgnoreActor,AlsDoubleVector EndOffset,AlsDoubleVector Location,AlsDoubleVector Rotation,AlsDoubleVector Scale,float Volume,float Pitch)
{
    internal static LyraContextNotifyDefinition Read(JsonElement p)=>new(p.GetProperty("effect").GetString()!,
        p.GetProperty("socketName").GetString()!,p.GetProperty("attached").GetBoolean(),p.GetProperty("performTrace").GetBoolean(),
        p.GetProperty("traceChannel").GetProperty("value").GetInt32(),p.GetProperty("ignoreActor").GetBoolean(),
        Vector(p.GetProperty("traceEndOffset")),Vector(p.GetProperty("locationOffset")),Vector(p.GetProperty("rotationOffset")),
        Vector(p.GetProperty("vfxScale")),p.GetProperty("volumeMultiplier").GetSingle(),p.GetProperty("pitchMultiplier").GetSingle());
    internal static AlsDoubleVector Vector(JsonElement p)=>new(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble());
}

// Immutable authored rows. Exact GameplayTag queries append every matching row,
// including duplicate SoundBase references; no priority or fallback heuristic.
internal sealed class LyraContextEffectsCatalog
{
    private sealed record Row(string Effect,ImmutableArray<string> Contexts,LyraContextEffectSelection Effects);
    private readonly Row[] _rows;
    private readonly HashSet<string> _tags;
    private readonly Dictionary<int,string> _surfaces;
    public string Sha256 {get;}
    public LyraContextEffectsCatalog()
    {
        const string root="res://assets/generated/lyra_als/";
        var bytes=Godot.FileAccess.GetFileAsBytes(root+"context_effects_v1_policy.json");Sha256=LyraLogicalSourceBank.Sha(bytes);
        using var doc=JsonDocument.Parse(bytes);var p=doc.RootElement;
        if(p.GetProperty("schemaVersion").GetInt32()!=1||!p.GetProperty("audioPlaybackDeferred").GetBoolean())
            throw new NotSupportedException("Unbound ContextEffects profile.");
        foreach(var d in p.GetProperty("dependencies").EnumerateObject())
            if(LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes(root+d.Name))!=d.Value.GetString())
                throw new InvalidOperationException("Stale ContextEffects resource: "+d.Name);
        _tags=p.GetProperty("tables").EnumerateArray().SelectMany(t=>t.GetProperty("tags").EnumerateArray()).Select(t=>t.GetString()!).ToHashSet(StringComparer.Ordinal);
        _surfaces=p.GetProperty("surfaceMap").EnumerateArray().ToDictionary(r=>r.GetProperty("surface").GetInt32(),r=>r.GetProperty("tag").GetString()!);
        _rows=p.GetProperty("rows").EnumerateArray().Select(r=>new Row(r.GetProperty("effect").GetString()!,Tags(r.GetProperty("contexts")),
            new(r.GetProperty("effects").EnumerateArray().Where(e=>e.GetProperty("audio").GetBoolean()).Select(e=>e.GetProperty("path").GetString()!).ToImmutableArray(),
                r.GetProperty("effects").EnumerateArray().Where(e=>e.GetProperty("vfx").GetBoolean()).Select(e=>e.GetProperty("path").GetString()!).ToImmutableArray()))).ToArray();
        if(_rows.Length!=6||_surfaces.Count!=4||_rows.Any(r=>r.Effects.Vfx.Length!=0||!_tags.Contains(r.Effect)||r.Contexts.Any(t=>!_tags.Contains(t))))
            throw new NotSupportedException("Changed original DefaultSkin effects.");
    }
    internal static ImmutableArray<string> Tags(JsonElement p)=>p.EnumerateArray().Select(t=>t.GetString()!).ToImmutableArray();
    public string? Surface(int surface)=>_surfaces.GetValueOrDefault(surface);
    public void ValidateContexts(ImmutableArray<string> contexts)
    {if(contexts.IsDefault||contexts.Any(t=>!_tags.Contains(t)))throw new ArgumentException("Unregistered ContextEffects tag.");}
    public LyraContextEffectSelection Select(string effect,ImmutableArray<string> contexts)
    {
        ValidateContexts(contexts);var audio=ImmutableArray.CreateBuilder<string>();var vfx=ImmutableArray.CreateBuilder<string>();
        foreach(var row in _rows)if(row.Effect==effect&&row.Contexts.IsEmpty==contexts.IsEmpty&&row.Contexts.All(contexts.Contains))
        {audio.AddRange(row.Effects.Audio);vfx.AddRange(row.Effects.Vfx);}
        return new(audio.ToImmutable(),vfx.ToImmutable());
    }
}
