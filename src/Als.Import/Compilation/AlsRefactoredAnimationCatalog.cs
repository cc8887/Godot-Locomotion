using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Diagnostics;
using GodotAls.Core.Resources;
using GodotAls.Import.Resources;

namespace GodotAls.Import.Compilation;

public sealed record AlsRefactoredAnimationAsset(string Source,string Class,string File,string Sha256);

/// <summary>Original resource catalog, independent of the legacy V4 IDs/layout.
/// Payloads are loaded and verified on demand. A bounded import scope shares
/// intermediate resources; the catalog itself retains no large raw arrays.</summary>
public sealed class AlsRefactoredAnimationCatalog
{
    private readonly Func<string,byte[]> _read;
    private readonly bool _sampleRootMotion;
    private readonly JsonElement _skeletons;
    private ImportResourceScope? _boundScope;
    public IReadOnlyDictionary<string,AlsRefactoredAnimationAsset> Assets { get; }
    public string IndexDigest { get; }
    public AlsRefactoredAnimationCatalog(string indexJson,Func<string,byte[]> read,bool sampleRootMotion=true)
    {
        ArgumentNullException.ThrowIfNull(read);_read=read;
        _sampleRootMotion=sampleRootMotion;
        IndexDigest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(indexJson)));
        using var document=JsonDocument.Parse(indexJson);var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||root.GetProperty("parentClass").GetString()!="/ALS/ALS/Character/AB_Als.AB_Als_C")
            throw new ArgumentException("Foreign Refactored animation index.");
        var assets=new Dictionary<string,AlsRefactoredAnimationAsset>(StringComparer.Ordinal);
        foreach(var row in root.GetProperty("assets").EnumerateArray())
        {
            string Text(string name)=>row.GetProperty(name).GetString()??throw new ArgumentException("Missing resource identity.");
            var entry=new AlsRefactoredAnimationAsset(Text("source"),Text("class"),Text("file"),Text("sha256"));
            var expected="refactored_animation_sources/"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Source))).ToLowerInvariant()+".json";
            if(!entry.Source.StartsWith("/ALS/ALS/",StringComparison.Ordinal)||string.IsNullOrWhiteSpace(entry.Class)||entry.File!=expected||
                entry.Sha256.Length!=64||!entry.Sha256.All(Uri.IsHexDigit)||!assets.TryAdd(entry.Source,entry))
                throw new ArgumentException("Invalid or duplicate Refactored resource binding.");
        }
        var counts=root.GetProperty("counts").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetInt32(),StringComparer.Ordinal);
        var actual=assets.Values.GroupBy(a=>a.Class).ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
        if(counts.Count!=actual.Count||actual.Any(p=>!counts.TryGetValue(p.Key,out var count)||count!=p.Value)||
            !assets.ContainsKey("/ALS/ALS/Character/AB_Als.AB_Als")||!assets.ContainsKey(root.GetProperty("settings").GetString()!))
            throw new ArgumentException("Incomplete Refactored resource index.");
        _skeletons=root.GetProperty("skeletons").Clone();
        foreach(var skeleton in _skeletons.EnumerateObject())
            if(skeleton.Name!=skeleton.Value.GetProperty("source").GetString())throw new ArgumentException("Skeleton owner differs.");
        Assets=new ReadOnlyDictionary<string,AlsRefactoredAnimationAsset>(assets);
        if (ImportResourceScope.Current is { } scope) Bind(scope);
    }

    private void Bind(ImportResourceScope scope)
    {
        if (_boundScope == scope) return;
        foreach (var entry in Assets.Values)
            scope.Bind(PayloadId(entry.Source), entry.Sha256 + ":" + entry.Class, context => Load(entry.Source, context));
        foreach (var skeleton in _skeletons.EnumerateObject())
            scope.Bind(SkeletonId(skeleton.Name), skeleton.Value.GetRawText(), _ => skeleton.Value);
        foreach (var entry in Assets.Values.Where(e => e.Class == "AnimSequence"))
        {
            var source = entry.Source;
            scope.Bind(PoseId(source, "additive"), "pose-v1", context => CompileAdditivePoseCore(source, context));
            scope.Bind(PoseId(source, "absolute"), "pose-v1", context => CompileAbsolutePoseCore(source, context));
            scope.Bind(PoseId(source, "absolute-curves"), "pose-v1", context => CompileAbsolutePoseWithCurvesCore(source, context));
        }
        _boundScope = scope;
    }
    public JsonElement Read(string source)
    {
        if (!Assets.ContainsKey(source)) throw new ArgumentException("Unbound Refactored source.");
        if (ImportResourceScope.Current is not { } scope) return Load(source, null);
        Bind(scope);
        return scope.Read<JsonElement>(PayloadId(source));
    }

    private static ResourceId PayloadId(string source) => new("source-json:" + source);
    private static ResourceId SkeletonId(string source) => new("source-skeleton:" + source);
    private ResourceId PoseId(string source, string kind) => new("source-pose:" + kind + ":root=" + _sampleRootMotion + ":" + source);
    private JsonElement Read(string source, ResourceLoadContext? context) => context is null ? Read(source) : context.Read<JsonElement>(PayloadId(source));
    private JsonElement Skeleton(string source, ResourceLoadContext? context) => context is null ? _skeletons.GetProperty(source) : context.Read<JsonElement>(SkeletonId(source));

    private JsonElement Load(string source, ResourceLoadContext? context)
    {
        if(!Assets.TryGetValue(source,out var entry))throw new ArgumentException("Unbound Refactored source.");
        using var trace = AlsPerformanceTrace.Begin("refactored.catalog_read", entry.File);
        byte[] bytes;
        using (var readTrace = AlsPerformanceTrace.Begin("refactored.read"))
        {
            bytes=_read(entry.File);
            context?.ReportBytes(bytes.LongLength);
            readTrace?.Complete(1, bytes.LongLength);
        }
        using (var hashTrace = AlsPerformanceTrace.Begin("refactored.payload_hash"))
        {
            if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Refactored source digest differs.");
            hashTrace?.Complete(1, bytes.LongLength);
        }
        using var decodeTrace = AlsPerformanceTrace.Begin("refactored.decode");
        using var document=JsonDocument.Parse(bytes);var root=document.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||root.GetProperty("source").GetString()!=source||
            root.GetProperty("class").GetString()!=entry.Class||string.IsNullOrWhiteSpace(root.GetProperty("nativeText").GetString()))
            throw new ArgumentException("Refactored source identity differs.");
        var payload = root.Clone();
        decodeTrace?.Complete(1, bytes.LongLength);
        trace?.Complete(1, bytes.LongLength);
        return payload;
    }
    public AlsRefactoredAdditiveSource CompileAdditivePose(string source)
    {
        if (ImportResourceScope.Current is not { } scope || !Assets.TryGetValue(source, out var entry) || entry.Class != "AnimSequence")
            return CompileAdditivePoseCore(source, null);
        Bind(scope);
        return scope.Read<AlsRefactoredAdditiveSource>(PoseId(source, "additive"));
    }

    private AlsRefactoredAdditiveSource CompileAdditivePoseCore(string source, ResourceLoadContext? context)
    {
        var target=Read(source, context);var policy=target.GetProperty("evaluation");
        var type=policy.GetProperty("additiveType").GetString();
        if(target.GetProperty("class").GetString()!="AnimSequence"||
            type is not ("AAT_LocalSpaceBase" or "AAT_RotationOffsetMeshSpace")||
            policy.GetProperty("basePoseType").GetString()!="ABPT_AnimFrame")
            throw new ArgumentException("Unsupported original additive source policy.");
        var basePath=policy.GetProperty("baseAsset").GetString()??throw new ArgumentException("Missing additive base.");
        var basis=Read(basePath, context);
        if(basis.GetProperty("class").GetString()!="AnimSequence")throw new ArgumentException("Invalid additive base class.");
        var skeleton=target.GetProperty("raw").GetProperty("skeletonSource").GetString()!;
        var rows=source==basePath?new[]{target}:new[]{target,basis};
        var json=JsonSerializer.Serialize(new {schemaVersion=1,
            skeletons=new Dictionary<string,object>{{skeleton,new {metadata=Skeleton(skeleton, context)}}},
            sequences=rows.Select(p=>new {raw=p.GetProperty("raw"),evaluation=p.GetProperty("evaluation"),curves=p.GetProperty("curves")})});
        var poses=AlsMantlingPoseCompiler.CompileCatalogAdditiveTargets(json,rows.Select(p=>p.GetProperty("source").GetString()!).ToArray())
            .ToDictionary(p=>p.Key,p=>p.Value.WithRootMotionSampling(_sampleRootMotion));
        var curves=AlsMantlingCurveCompiler.CompileEmbedded(json);
        // UE GetSequencePose divides by sampled keys, not frame intervals.
        var baseTime=basis.GetProperty("evaluation").GetProperty("sequencePlayLength").GetDouble()*
            Math.Clamp((double)policy.GetProperty("baseFrame").GetInt32()/poses[basePath].Data.SampledKeyCount,0,1);
        return new(poses[source],poses[basePath],curves[source],curves[basePath],baseTime,type=="AAT_RotationOffsetMeshSpace");
    }
    // Absolute sequences only; additive deltas require CompileAdditivePose.
    // The returned single-source scope has local animation ID zero. Map that ID
    // explicitly when assembling a shared playback/resource bank.
    public AlsMantlingPoseSource CompileAbsolutePose(string source)
    {
        if (ImportResourceScope.Current is not { } scope || !Assets.TryGetValue(source, out var entry) || entry.Class != "AnimSequence")
            return CompileAbsolutePoseCore(source, null);
        Bind(scope);
        return scope.Read<AlsMantlingPoseSource>(PoseId(source, "absolute"));
    }

    private AlsMantlingPoseSource CompileAbsolutePoseCore(string source, ResourceLoadContext? context)
    {
        var payload=Read(source, context);
        if(payload.GetProperty("class").GetString()!="AnimSequence"||payload.GetProperty("evaluation").GetProperty("additiveType").GetString()!="AAT_None")
            throw new ArgumentException("Expected an absolute original animation sequence.");
        var raw=payload.GetProperty("raw");var skeleton=raw.GetProperty("skeletonSource").GetString()!;
        var json=JsonSerializer.Serialize(new {schemaVersion=1,
            skeletons=new Dictionary<string,object>{{skeleton,new {metadata=Skeleton(skeleton, context)}}},
            sequences=new[]{new {raw,evaluation=payload.GetProperty("evaluation")}}});
        return AlsMantlingPoseCompiler.CompileAbsoluteSequences(json,[source])[source].WithRootMotionSampling(_sampleRootMotion);
    }
    public (AlsMantlingPoseSource Pose,AlsMantlingCurveSource Curves) CompileAbsolutePoseWithCurves(string source)
    {
        if (ImportResourceScope.Current is not { } scope || !Assets.TryGetValue(source, out var entry) || entry.Class != "AnimSequence")
            return CompileAbsolutePoseWithCurvesCore(source, null);
        Bind(scope);
        return scope.Read<(AlsMantlingPoseSource, AlsMantlingCurveSource)>(PoseId(source, "absolute-curves"));
    }

    private (AlsMantlingPoseSource Pose,AlsMantlingCurveSource Curves) CompileAbsolutePoseWithCurvesCore(string source, ResourceLoadContext? context)
    {
        var payload=Read(source, context);
        if(payload.GetProperty("class").GetString()!="AnimSequence"||payload.GetProperty("evaluation").GetProperty("additiveType").GetString()!="AAT_None")
            throw new ArgumentException("Expected an absolute original animation sequence.");
        var raw=payload.GetProperty("raw");var skeleton=raw.GetProperty("skeletonSource").GetString()!;
        var json=JsonSerializer.Serialize(new {schemaVersion=1,
            skeletons=new Dictionary<string,object>{{skeleton,new {metadata=Skeleton(skeleton, context)}}},
            sequences=new[]{new {raw,evaluation=payload.GetProperty("evaluation"),curves=payload.GetProperty("curves")}}});
        return (AlsMantlingPoseCompiler.CompileAbsoluteSequences(json,[source])[source].WithRootMotionSampling(_sampleRootMotion),AlsMantlingCurveCompiler.CompileEmbedded(json)[source]);
    }
}
