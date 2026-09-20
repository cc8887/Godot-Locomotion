using System.Numerics;

namespace GodotAls.Core.Locomotion;

// The two translation policies actually authored on the V4 BasePoses skeleton.
// Other native policies require additional source-reference data and are rejected.
public enum AlsBasePoseTranslationRetargetMode { Animation, Skeleton }

public sealed class AlsBasePoseRetargetDefinition
{
    private readonly int[] _physicalToLogical;
    private readonly AlsBasePoseTranslationRetargetMode[] _modes;
    private readonly bool[] _tracked;
    public AlsBasePoseEvaluatorDefinition Evaluator { get; }
    public int LogicalCount { get; }
    public ReadOnlySpan<int> PhysicalToLogical => _physicalToLogical;
    public ReadOnlySpan<AlsBasePoseTranslationRetargetMode> Modes => _modes;
    public ReadOnlySpan<bool> Tracked => _tracked;

    public AlsBasePoseRetargetDefinition(AlsBasePoseEvaluatorDefinition evaluator, int logicalCount,
        ReadOnlySpan<int> physicalToLogical, ReadOnlySpan<AlsBasePoseTranslationRetargetMode> modes,
        ReadOnlySpan<bool> tracked)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        if (evaluator.NodeIndex < 0 || evaluator.AnimationId < 0 || string.IsNullOrEmpty(evaluator.AssetId) ||
            string.IsNullOrEmpty(evaluator.AssetPath) || logicalCount <= 0 || physicalToLogical.IsEmpty ||
            physicalToLogical.Length > logicalCount || modes.Length != physicalToLogical.Length || tracked.Length != modes.Length)
            throw new ArgumentException("Invalid BasePoses retarget identity or layout.");
        var previous = -1;
        for (var physical = 0; physical < physicalToLogical.Length; physical++)
        {
            var logical = physicalToLogical[physical];
            if (logical <= previous || logical >= logicalCount ||
                modes[physical] is not (AlsBasePoseTranslationRetargetMode.Animation or AlsBasePoseTranslationRetargetMode.Skeleton))
                throw new ArgumentException("Unsupported BasePoses retarget policy or physical mapping.");
            previous = logical;
        }
        Evaluator = evaluator; LogicalCount = logicalCount;
        _physicalToLogical = physicalToLogical.ToArray(); _modes = modes.ToArray(); _tracked = tracked.ToArray();
    }
}

/// <summary>Native FRetargetingScope's final pass for this BasePoses source. Apply only
/// after missing virtual bones have been generated from the unretargeted key.</summary>
public sealed class AlsBasePoseRetargetModel
{
    private readonly Vector3[] _translations;
    public AlsBasePoseRetargetDefinition Definition { get; }

    /// <param name="logicalReferencePose">Real target reference atoms in the same bone
    /// coordinate space as Apply's pose. No global-axis or identity fallback is used.</param>
    public AlsBasePoseRetargetModel(AlsBasePoseRetargetDefinition definition, ReadOnlySpan<AlsLocalPose> logicalReferencePose)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (logicalReferencePose.Length != definition.LogicalCount)
            throw new ArgumentException("BasePoses target reference layout differs.");
        foreach (var atom in logicalReferencePose) Validate(atom);
        Definition = definition; _translations = new Vector3[definition.PhysicalToLogical.Length];
        for (var physical = 0; physical < _translations.Length; physical++)
            _translations[physical] = logicalReferencePose[definition.PhysicalToLogical[physical]].Position;
    }

    public void Apply(Span<AlsLocalPose> logicalPose)
    {
        if (logicalPose.Length != Definition.LogicalCount)
            throw new ArgumentException("BasePoses retarget output layout differs.");
        // Validate every input before touching the caller's candidate, including atoms
        // whose translation is about to be replaced or whose policy is Animation.
        foreach (var atom in logicalPose) Validate(atom);
        for (var physical = 0; physical < _translations.Length; physical++)
            if (Definition.Tracked[physical] && Definition.Modes[physical] == AlsBasePoseTranslationRetargetMode.Skeleton)
            {
                var logical = Definition.PhysicalToLogical[physical];
                logicalPose[logical] = logicalPose[logical] with { Position = _translations[physical] };
            }
    }

    private static void Validate(in AlsLocalPose atom)
    {
        var length = atom.Rotation.LengthSquared();
        if (!Finite(atom.Position) || !Finite(atom.Scale) || !float.IsFinite(length) || MathF.Abs(length - 1) >= .01f)
            throw new ArgumentException("BasePoses retarget requires finite TRS with normalized rotations.");
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
