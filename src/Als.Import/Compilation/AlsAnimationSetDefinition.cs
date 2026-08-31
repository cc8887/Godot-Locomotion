namespace GodotAls.Import.Compilation;

public sealed record AlsAnimationSetDefinition(
    AlsSkeletonDefinition[] Skeletons,
    AlsSkeletalMeshDefinition[] SkeletalMeshes,
    AlsStaticMeshDefinition[] StaticMeshes,
    AlsAnimationDefinition[] Animations,
    AlsMontageDefinition[] Montages,
    AlsBlendDefinition[] BlendSpaces,
    AlsBlendDefinition[] AimOffsets,
    AlsMaterialDefinition[] Materials,
    AlsTextureDefinition[] Textures,
    AlsPhysicsAssetDefinition[] PhysicsAssets,
    AlsGenericAssetDefinition[] Curves,
    AlsGenericAssetDefinition[] ConfigAssets,
    AlsAssetIndex AssetIndex,
    string DefinitionDigest);

public sealed record AlsSkeletalMeshDefinition(
    int Id, string StableId, string Name, string ObjectPath, string ResourcePath,
    int SkeletonId, int MaterialSlotCount, int[] MaterialIds, bool Overlay, bool Prop);

public sealed record AlsStaticMeshDefinition(
    int Id, string StableId, string Name, string ObjectPath, string ResourcePath,
    int MaterialSlotCount, int[] MaterialIds, bool Overlay, bool Prop);

public sealed record AlsAnimationDefinition(
    int Id,
    string StableId,
    string Name,
    string ObjectPath,
    string ResourcePath,
    int SkeletonId,
    float PlayLength,
    int FrameRateNumerator,
    int FrameRateDenominator,
    int SampledKeyCount,
    bool Loop,
    int Interpolation,
    bool RootMotionEnabled,
    int RootMotionRootLock,
    bool ForceRootLock,
    bool UseNormalizedRootMotionScale,
    int AdditiveType,
    int AdditiveBasePoseType,
    int AdditiveBasePoseFrame,
    int AdditiveBasePoseAnimationId,
    AlsFloatCurveDefinition[] Curves,
    string[] LegacyCurveNames,
    AlsCompiledTimelineEventDefinition[] Timeline,
    AlsAnimationSyncMarkerDefinition[] SyncMarkers,
    bool Overlay,
    bool Prop)
{
    private AlsFloatCurveDefinition[] _curves = Curves.ToArray();
    private string[] _legacyCurveNames = LegacyCurveNames.ToArray();
    private AlsCompiledTimelineEventDefinition[] _timeline = Timeline.ToArray();
    private AlsAnimationSyncMarkerDefinition[] _syncMarkers = SyncMarkers.ToArray();

    public AlsFloatCurveDefinition[] Curves
    {
        get => _curves.ToArray();
        init => _curves = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public string[] LegacyCurveNames
    {
        get => _legacyCurveNames.ToArray();
        init => _legacyCurveNames = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsCompiledTimelineEventDefinition[] Timeline
    {
        get => _timeline.ToArray();
        init => _timeline = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsAnimationSyncMarkerDefinition[] SyncMarkers
    {
        get => _syncMarkers.ToArray();
        init => _syncMarkers = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public enum AlsCurveInterpolation : byte
{
    Constant,
    Linear,
    Cubic,
}

public enum AlsCurveInfinityMode : byte
{
    Constant,
    Linear,
    Cycle,
    CycleWithOffset,
    Oscillate,
}

public enum AlsCurveProvenance : byte
{
    SourceCurve,
    DerivedRootTrack,
}

public enum AlsCanonicalCurveKind : byte
{
    None,
    RotationYawSpeedRadiansPerSecond,
}

public readonly record struct AlsFloatCurveKeyDefinition(
    float TimeSeconds,
    float Value,
    float ArriveTangent,
    float LeaveTangent,
    AlsCurveInterpolation Interpolation);

public sealed record AlsFloatCurveDefinition(
    int CurveId,
    AlsCanonicalCurveKind CanonicalKind,
    string SourceName,
    AlsCurveProvenance Provenance,
    AlsFloatCurveKeyDefinition[] Keys)
{
    private AlsFloatCurveKeyDefinition[] _keys = Keys.ToArray();

    public AlsFloatCurveKeyDefinition[] Keys
    {
        get => _keys.ToArray();
        init => _keys = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsCurveInfinityMode PreInfinity { get; init; } = AlsCurveInfinityMode.Constant;

    public AlsCurveInfinityMode PostInfinity { get; init; } = AlsCurveInfinityMode.Constant;
}

public enum AlsCompiledTimelineEventKind : byte
{
    Generic,
    Footstep,
    SetAction,
    SetGroundedEntry,
    EarlyBlendOut,
    RootMotionScale,
}

public enum AlsCompiledTimelineTickMode : byte
{
    Queued,
    BranchingPoint,
}

public enum AlsCompiledTimelineFoot : byte
{
    Unspecified,
    Left,
    Right,
}

public enum AlsCompiledTimelineAction : byte
{
    None,
    Rolling,
    Mantling,
    Ragdolling,
    GettingUp,
}

public enum AlsCompiledTimelineGroundedEntryMode : byte
{
    None,
    FromRoll,
}

public enum AlsCompiledTimelineLocomotionMode : byte
{
    Grounded,
    InAir,
    Mantling,
    Ragdoll,
    Recovering,
}

public enum AlsCompiledTimelineRotationMode : byte
{
    VelocityDirection,
    LookingDirection,
    Aiming,
}

public enum AlsCompiledTimelineStance : byte
{
    Standing,
    Crouching,
}

public readonly record struct AlsCompiledTimelinePayloadDefinition(
    AlsCompiledTimelineFoot Foot,
    AlsCompiledTimelineAction Action,
    AlsCompiledTimelineGroundedEntryMode GroundedEntryMode,
    float BlendOutSeconds,
    bool CheckInput,
    bool CheckLocomotionMode,
    AlsCompiledTimelineLocomotionMode LocomotionMode,
    bool CheckRotationMode,
    AlsCompiledTimelineRotationMode RotationMode,
    bool CheckStance,
    AlsCompiledTimelineStance Stance,
    float TranslationScale);

public sealed record AlsCompiledTimelineEventDefinition(
    int EventId,
    string StableEventId,
    AlsCompiledTimelineEventKind Kind,
    int SourceAssetId,
    string SourceClassPath,
    string DisplayName,
    float TimeSeconds,
    float DurationSeconds,
    float TriggerWeightThreshold,
    AlsCompiledTimelineTickMode TickMode,
    int SourceIndex,
    int TrackIndex,
    AlsCompiledTimelinePayloadDefinition Payload);

public sealed record AlsAnimationSyncMarkerDefinition(
    int MarkerId,
    string StableMarkerId,
    string Name,
    float TimeSeconds,
    int SourceIndex,
    int TrackIndex);

public sealed record AlsMontageDefinition(
    int Id, string StableId, string Name, string ObjectPath,
    AlsMontageSectionDefinition[] Sections, AlsMontageSlotDefinition[] Slots,
    float PlayLength, float BlendInTime, int BlendInOption, float BlendOutTime,
    int BlendOutOption, float BlendOutTriggerTime, bool EnableAutoBlendOut,
    AlsCompiledTimelineEventDefinition[] Timeline,
    bool Overlay, bool Prop)
{
    private AlsMontageSectionDefinition[] _sections = Sections.ToArray();
    private AlsMontageSlotDefinition[] _slots = Slots.ToArray();
    private AlsCompiledTimelineEventDefinition[] _timeline = Timeline.ToArray();

    public AlsMontageSectionDefinition[] Sections
    {
        get => _sections.ToArray();
        init => _sections = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsMontageSlotDefinition[] Slots
    {
        get => _slots.ToArray();
        init => _slots = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public AlsCompiledTimelineEventDefinition[] Timeline
    {
        get => _timeline.ToArray();
        init => _timeline = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public sealed record AlsMontageSectionDefinition(int SectionId, string Name, int NextSectionId, float StartTime);

public sealed record AlsMontageSlotDefinition(int SlotId, string SlotName, AlsMontageSegmentDefinition[] Segments)
{
    private AlsMontageSegmentDefinition[] _segments = Segments.ToArray();

    public AlsMontageSegmentDefinition[] Segments
    {
        get => _segments.ToArray();
        init => _segments = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public sealed record AlsMontageSegmentDefinition(
    int SegmentId, int AnimationId, float StartPosition, float AnimationStartTime,
    float AnimationEndTime, float PlayRate, int LoopCount);

public sealed record AlsBlendDefinition(
    int Id, string StableId, string Name, string ObjectPath,
    AlsBlendParameterDefinition[] Parameters, AlsBlendSampleDefinition[] Samples,
    bool Overlay, bool Prop);

public sealed record AlsBlendParameterDefinition(string Name, float Minimum, float Maximum, int GridDivisions);

public sealed record AlsBlendSampleDefinition(int AnimationId, float[] SampleValue, float RateScale);

public sealed record AlsMaterialDefinition(
    int Id, string StableId, string Name, string ObjectPath, int ParentMaterialId,
    int[] ReferencedTextureIds, AlsScalarParameterDefinition[] ScalarParameterOverrides,
    AlsVectorParameterDefinition[] VectorParameterOverrides,
    AlsTextureParameterDefinition[] TextureParameterOverrides, bool Overlay, bool Prop);

public sealed record AlsScalarParameterDefinition(string Name, int Association, int Index, float Value);

public sealed record AlsVectorParameterDefinition(string Name, int Association, int Index, float[] Value);

public sealed record AlsTextureParameterDefinition(string Name, int Association, int Index, int TextureId);

public sealed record AlsTextureDefinition(
    int Id, string StableId, string Name, string ObjectPath, string ResourcePath,
    int Width, int Height, string PixelFormatSource, bool Overlay, bool Prop);

public sealed record AlsPhysicsAssetDefinition(
    int Id, string StableId, string Name, string ObjectPath,
    AlsPhysicsBodyDefinition[] Bodies, AlsPhysicsConstraintDefinition[] Constraints,
    bool Overlay, bool Prop);

public sealed record AlsPhysicsBodyDefinition(string Bone, int PrimitiveCount);

public sealed record AlsPhysicsConstraintDefinition(string ChildBone, string ParentBone);

public sealed record AlsGenericAssetDefinition(
    int Id, string StableId, string Name, string ObjectPath,
    string AssetClass, int AssetRegistryTagCount, bool Overlay, bool Prop);
