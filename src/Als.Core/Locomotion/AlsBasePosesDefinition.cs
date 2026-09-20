namespace GodotAls.Core.Locomotion;

// NodeIndex is the native evaluator identity, independent of other players of
// the same animation asset. Evaluators are stored in authored N, CLF pin order.
public sealed record AlsBasePoseEvaluatorDefinition(int NodeIndex, int AnimationId,
    string AssetId, string AssetPath, float Length, float ExplicitTime, bool Loop,
    bool Teleport, string ReinitializationBehavior, string SyncGroupName,
    string SyncGroupRole, string SyncMethod, bool UseExplicitFrame = false,
    int ExplicitFrame = 0, float StartPosition = 0, bool IgnoreForRelevancyTest = false);

/// <summary>The supported V4 BasePoses graph and its authored initialization defaults.</summary>
public sealed class AlsBasePosesDefinition
{
    private readonly AlsBasePoseEvaluatorDefinition[] _evaluators;
    private readonly string[] _weightProperties;
    private readonly float[] _defaultDesiredAlphas;
    public string Source { get; }
    public int CompiledPropertyCount { get; }
    public int RootNodeIndex { get; }
    public int BlendNodeIndex { get; }
    public int SkeletonId { get; }
    public ReadOnlySpan<AlsBasePoseEvaluatorDefinition> Evaluators => _evaluators;
    public ReadOnlySpan<string> WeightProperties => _weightProperties;
    public ReadOnlySpan<float> DefaultDesiredAlphas => _defaultDesiredAlphas;
    public float AlphaScale { get; }
    public float AlphaBias { get; }
    public bool Additive { get; }
    public bool NormalizeAlphas { get; }

    public AlsBasePosesDefinition(string source, int compiledPropertyCount, int rootNodeIndex,
        int blendNodeIndex, int skeletonId, AlsBasePoseEvaluatorDefinition[] evaluators,
        string[] weightProperties, float[] defaultDesiredAlphas, float alphaScale = 1,
        float alphaBias = 0, bool additive = false, bool normalizeAlphas = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(evaluators);
        ArgumentNullException.ThrowIfNull(weightProperties);
        ArgumentNullException.ThrowIfNull(defaultDesiredAlphas);
        if (evaluators.Length != 2 || evaluators.Any(e => e is null) || skeletonId < 0 ||
            !weightProperties.SequenceEqual(new[] { "BasePose_N", "BasePose_CLF" }) ||
            !defaultDesiredAlphas.SequenceEqual(new[] { 0f, 0f }) ||
            alphaScale != 1 || alphaBias != 0 || additive || !normalizeAlphas)
            throw new ArgumentException("Unsupported BasePoses layout or blend policy.");
        var nodes = new[] { rootNodeIndex, blendNodeIndex, evaluators[0].NodeIndex, evaluators[1].NodeIndex };
        if (nodes.Distinct().Count() != 4 || nodes.Any(n => n < 0 || n >= compiledPropertyCount) ||
            evaluators.Select(e => e.AnimationId).Distinct().Count() != 2 ||
            evaluators.Any(e => e.AnimationId < 0 || string.IsNullOrEmpty(e.AssetId) || string.IsNullOrEmpty(e.AssetPath) ||
                !float.IsFinite(e.Length) || e.Length <= 0 || e.ExplicitTime != 0 || !e.Loop || !e.Teleport ||
                e.ReinitializationBehavior != "ExplicitTime" || e.SyncGroupName != "None" ||
                e.SyncGroupRole != "CanBeLeader" || e.SyncMethod != "DoNotSync" || e.UseExplicitFrame ||
                e.ExplicitFrame != 0 || e.StartPosition != 0 || e.IgnoreForRelevancyTest))
            throw new ArgumentException("Unsupported BasePoses evaluator identity or policy.");
        Source = source; CompiledPropertyCount = compiledPropertyCount;
        RootNodeIndex = rootNodeIndex; BlendNodeIndex = blendNodeIndex; SkeletonId = skeletonId;
        _evaluators = (AlsBasePoseEvaluatorDefinition[])evaluators.Clone();
        _weightProperties = (string[])weightProperties.Clone();
        _defaultDesiredAlphas = (float[])defaultDesiredAlphas.Clone();
        AlphaScale = alphaScale; AlphaBias = alphaBias; Additive = additive; NormalizeAlphas = normalizeAlphas;
    }
}
