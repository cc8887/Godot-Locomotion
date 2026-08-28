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
    AlsAnimationNotifyDefinition[] Notifies,
    AlsAnimationSyncMarkerDefinition[] SyncMarkers,
    bool Overlay,
    bool Prop)
{
    private AlsFloatCurveDefinition[] _curves = Curves.ToArray();
    private string[] _legacyCurveNames = LegacyCurveNames.ToArray();

    public AlsFloatCurveDefinition[] Curves
    {
        get => _curves;
        init => _curves = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }

    public string[] LegacyCurveNames
    {
        get => _legacyCurveNames;
        init => _legacyCurveNames = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public enum AlsCurveInterpolation : byte
{
    Constant,
    Linear,
    Cubic,
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
        get => _keys;
        init => _keys = value?.ToArray() ?? throw new ArgumentNullException(nameof(value));
    }
}

public sealed record AlsAnimationNotifyDefinition(string Name, float Time, float Duration, int SourceIndex);

public sealed record AlsAnimationSyncMarkerDefinition(string Name, float Time);

public sealed record AlsMontageDefinition(
    int Id, string StableId, string Name, string ObjectPath,
    AlsMontageSectionDefinition[] Sections, AlsMontageSlotDefinition[] Slots,
    float PlayLength, float BlendInTime, int BlendInOption, float BlendOutTime,
    int BlendOutOption, float BlendOutTriggerTime, bool EnableAutoBlendOut,
    bool Overlay, bool Prop);

public sealed record AlsMontageSectionDefinition(string Name, string NextSection, float StartTime);

public sealed record AlsMontageSlotDefinition(string SlotName, AlsMontageSegmentDefinition[] Segments);

public sealed record AlsMontageSegmentDefinition(
    int AnimationId, float StartPosition, float AnimationStartTime,
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
