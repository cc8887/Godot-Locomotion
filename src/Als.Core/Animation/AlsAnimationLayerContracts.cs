using System.Collections.Immutable;

namespace GodotAls.Core.Animation;

public enum AlsAnimationLayerScalarType { Boolean, Int32, Int64, Single, Double }
public sealed record AlsAnimationLayerParameter(string Name, AlsAnimationLayerScalarType Type, bool ClassBound);
public sealed record AlsAnimationLayerSignature(string Name, string Group, bool Implemented,
    ImmutableArray<string> InputPoses, ImmutableArray<AlsAnimationLayerParameter> Parameters,
    float BlendInTime, float BlendOutTime, string BlendInProfile, string BlendOutProfile)
{
    // Group is instance ownership metadata, not part of function argument compatibility.
    public bool Accepts(AlsAnimationLayerSignature implementation) =>
        string.Equals(Name, implementation.Name, StringComparison.OrdinalIgnoreCase) &&
        InputPoses.SequenceEqual(implementation.InputPoses, StringComparer.OrdinalIgnoreCase) &&
        Parameters.Select(p => (p.Name.ToUpperInvariant(), p.Type))
            .SequenceEqual(implementation.Parameters.Select(p => (p.Name.ToUpperInvariant(), p.Type)));
}

public sealed record AlsAnimationLayerCallContract(string Node, string Function, string InterfaceClass,
    string? DefaultClass, ImmutableArray<string> InputPoses, bool ReceiveNotifies, bool PropagateNotifies);

public sealed class AlsAnimationLayerClassContract
{
    private readonly ImmutableDictionary<string, AlsAnimationLayerSignature> _functions;
    public string ClassPath { get; }
    public string Skeleton { get; }
    public ImmutableArray<AlsAnimationLayerSignature> Functions { get; }
    public ImmutableArray<AlsAnimationLayerCallContract> Calls { get; }
    public bool ReceiveNotifies { get; }
    public bool PropagateNotifies { get; }
    public bool UseMainMontageData { get; }

    public AlsAnimationLayerClassContract(string classPath, string skeleton,
        IEnumerable<AlsAnimationLayerSignature> functions, IEnumerable<AlsAnimationLayerCallContract> calls,
        bool receiveNotifies, bool propagateNotifies, bool useMainMontageData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(classPath);
        ClassPath = classPath; Skeleton = skeleton;
        Functions = functions.ToImmutableArray(); Calls = calls.ToImmutableArray();
        _functions = Functions.ToImmutableDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        if (Calls.Select(c => c.Node).Distinct(StringComparer.Ordinal).Count() != Calls.Length)
            throw new ArgumentException("Duplicate linked layer call site.");
        ReceiveNotifies = receiveNotifies; PropagateNotifies = propagateNotifies;
        UseMainMontageData = useMainMontageData;
    }
    public AlsAnimationLayerSignature Function(string name) => _functions.TryGetValue(name, out var function)
        ? function : throw new InvalidOperationException("Unknown animation function: " + name);
    public bool TryFunction(string name, out AlsAnimationLayerSignature? function) => _functions.TryGetValue(name, out function);
}

public sealed class AlsAnimationLayerCatalog
{
    private readonly ImmutableDictionary<string, AlsAnimationLayerClassContract> _classes;
    public ImmutableArray<AlsAnimationLayerClassContract> Classes { get; }
    public string SourceSha256 { get; }
    public AlsAnimationLayerCatalog(string sourceSha256, IEnumerable<AlsAnimationLayerClassContract> classes)
    {
        SourceSha256 = sourceSha256; Classes = classes.ToImmutableArray();
        _classes = Classes.ToImmutableDictionary(c => c.ClassPath, StringComparer.Ordinal);
        foreach (var owner in Classes)
        foreach (var call in owner.Calls)
        {
            if (call.DefaultClass is { } fallback && !_classes.ContainsKey(fallback))
                throw new InvalidOperationException("Missing default animation class: " + fallback);
            if (call.InterfaceClass.Length == 0) continue;
            var declaration = Class(call.InterfaceClass).Function(call.Function);
            if (!call.InputPoses.SequenceEqual(declaration.InputPoses, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Linked call input poses differ: " + call.Node);
        }
    }
    public AlsAnimationLayerClassContract Class(string classPath) => _classes.TryGetValue(classPath, out var value)
        ? value : throw new InvalidOperationException("Unknown animation class: " + classPath);

    public void ValidateImplementation(string interfaceClass, string implementationClass)
    {
        var implementation = Class(implementationClass);
        foreach (var declaration in Class(interfaceClass).Functions)
        {
            if (!implementation.TryFunction(declaration.Name, out var function) || !function!.Implemented) continue;
            if (!declaration.Accepts(function))
                throw new InvalidOperationException("Animation layer signature differs: " + declaration.Name);
        }
    }

    public AlsLinkedLayerBindings CreateBindings(string mainClass)
    {
        var main = Class(mainClass);
        return new(mainClass, main.Calls.Select(c => new AlsLinkedLayerCallSite(c.Node, c.Function, c.DefaultClass,
            c.InterfaceClass.Length != 0, c.ReceiveNotifies, c.PropagateNotifies)),
            Classes.Select(c => new AlsLinkedLayerClass(c.ClassPath,
                c.Functions.Select(f => new AlsLinkedLayerFunction(f.Name, f.Group, f.Implemented)).ToImmutableArray(),
                c.ReceiveNotifies, c.PropagateNotifies)));
    }
}
