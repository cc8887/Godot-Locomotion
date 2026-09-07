using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Curves;
using GodotAls.Core.Events;
using GodotAls.Core.Sync;
using GodotAls.Core.Transitions;

[assembly: InternalsVisibleTo("Als.P5aOracle")]

namespace GodotAls.Core.Animation;

public static class AlsP5aTrace
{
    private const long MaximumDocumentBytes = 16L * 1024L * 1024L;
    private const int MaximumDocumentDepth = 64;
    private const int MaximumStringLength = 4096;
    private const ulong DigestOffset = 14695981039346656037UL;
    private const ulong DigestPrime = 1099511628211UL;

    [ThreadStatic]
    private static int LastShadowExecutionCount;
    [ThreadStatic]
    private static ulong LastShadowExecutionDigest;


    internal static byte[] BuildNativePlan() =>
        P5aFrozenPlanDocuments.CanonicalBytes(P5aFrozenPlanDocuments.Create().Plan);
    public static void WriteCanonicalPair(
        string rawPath,
        string tracePlanPath,
        string nativeCanonicalPath,
        string portCanonicalPath,
        scoped in AlsP5OccurrenceLayoutView occurrenceLayout,
        scoped in AlsP5RuntimeBindings runtimeBindings,
        ulong graphDigest)
    {
        var preflight = PreflightInputs(rawPath, tracePlanPath, PreflightRepresentation.NativeRaw);
        ValidatePreflightResult(ref preflight);
        var materialized = MaterializeAndReplay(preflight, occurrenceLayout, runtimeBindings, graphDigest);
        ReplaySummary replay;
        try
        {
            replay = ReplayAllFrames(materialized.Plan, occurrenceLayout, runtimeBindings);
        }
        catch (Exception exception) when (IsReplayInputFailure(exception))
        {
            throw ReplayFailureWithPlanPath(materialized.Plan, exception);
        }
        WriteReplayOutputs(materialized, replay, nativeCanonicalPath, portCanonicalPath);
    }

    public static void VerifyFixture(
        string fixturePath,
        string tracePlanPath,
        scoped in AlsP5OccurrenceLayoutView occurrenceLayout,
        scoped in AlsP5RuntimeBindings runtimeBindings,
        ulong graphDigest)
    {
        var preflight = PreflightInputs(fixturePath, tracePlanPath, PreflightRepresentation.NativeCanonical);
        var materialized = MaterializeAndReplay(preflight, occurrenceLayout, runtimeBindings, graphDigest);
        VerifyCanonicalFixture(materialized);
        ReplaySummary replay;
        try
        {
            replay = ReplayAllFrames(materialized.Plan, occurrenceLayout, runtimeBindings);
        }
        catch (Exception exception) when (IsReplayInputFailure(exception))
        {
            throw ReplayFailureWithPlanPath(materialized.Plan, exception);
        }
        ValidateCrossEnginePair(materialized.First, replay.PortCanonical);
    }

    private static bool IsReplayInputFailure(Exception exception) =>
        exception is InvalidDataException or InvalidOperationException or KeyNotFoundException or
            FormatException or OverflowException or ArgumentException;

    private static Exception ReplayFailureWithPlanPath(
        JsonObject plan,
        Exception failure)
    {
        var frozenPlan = P5aFrozenPlanDocuments.Create().Plan;
        if (JsonNode.DeepEquals(plan, frozenPlan))
        {
            return failure;
        }

        var differencePath = FindFirstDifferencePath(plan, frozenPlan, "$plan");
        return new InvalidDataException(
            $"P5A trace plan differs from the frozen plan at {differencePath}; Core replay rejected it: {failure.Message}",
            failure);
    }

    private static PreflightResult PreflightInputs(
        string firstPath, string planPath, PreflightRepresentation expectedFirstRepresentation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(planPath);
        var first = ReadAndValidateCanonicalDocument(firstPath, expectedFirstRepresentation);
        var plan = ReadAndValidateCanonicalDocument(planPath, PreflightRepresentation.Plan);
        return new PreflightResult(firstPath, planPath, first, plan);
    }

    private static void ValidatePreflightResult(ref PreflightResult preflight)
    {
        if (preflight.FirstBytes.Length == 0 || preflight.PlanBytes.Length == 0)
        {
            throw new InvalidDataException("P5A input documents must not be empty.");
        }
    }

    private static byte[] ReadAndValidateCanonicalDocument(
        string path, PreflightRepresentation expectedRepresentation)
    {
        var label = PreflightRepresentationLabel(expectedRepresentation);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new InvalidDataException($"P5A {label} document does not exist: {path}");
        }
        if (file.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException($"P5A {label} document exceeds the 16 MiB limit.");
        }

        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumDocumentDepth,
        });
        var propertySets = new HashSet<string>?[MaximumDocumentDepth + 1];
        var containers = new PreflightContainer[MaximumDocumentDepth + 1];
        var containerCount = 0;
        var representation = PreflightRepresentation.Unknown;
        var representationSeen = false;
        InvalidDataException? cardinalityFailure = null;
        try
        {
            while (reader.Read())
            {
                var rootRepresentationValue = containerCount == 1 && !containers[0].IsArray &&
                    containers[0].PendingProperty == "representation";
                if (rootRepresentationValue && reader.TokenType != JsonTokenType.String)
                {
                    representationSeen = true;
                }
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    var containerPath = AttachPreflightValue(containers, containerCount);
                    containers[containerCount++] = new PreflightContainer(
                        reader.TokenType == JsonTokenType.StartArray, containerPath);
                }
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    propertySets[reader.CurrentDepth] = new HashSet<string>(StringComparer.Ordinal);
                }
                else if (reader.TokenType == JsonTokenType.EndObject)
                {
                    propertySets[reader.CurrentDepth] = null;
                    containerCount--;
                }
                else if (reader.TokenType == JsonTokenType.EndArray)
                {
                    ref var container = ref containers[containerCount - 1];
                    if (cardinalityFailure is null)
                    {
                        try
                        {
                            ValidatePreflightArrayCardinality(
                                label, expectedRepresentation, container.Path, container.ItemCount);
                        }
                        catch (InvalidDataException exception)
                        {
                            cardinalityFailure = exception;
                        }
                    }
                    containerCount--;
                }
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    ValidatePreflightStringLength(ref reader, label);
                    var name = reader.GetString() ?? throw new InvalidDataException($"P5A {label} contains a null property name.");
                    var objectDepth = reader.CurrentDepth - 1;
                    var properties = objectDepth >= 0 ? propertySets[objectDepth] : null;
                    if (properties is null)
                    {
                        throw new InvalidDataException($"P5A {label} has an invalid JSON object scope.");
                    }
                    if (!properties.Add(name))
                    {
                        throw new InvalidDataException($"P5A {label} contains duplicate property '{name}'.");
                    }
                    containers[containerCount - 1].PendingProperty = name;
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    ValidatePreflightStringLength(ref reader, label);
                    if (rootRepresentationValue)
                    {
                        representationSeen = true;
                        representation = ParsePreflightRepresentation(ref reader);
                    }
                    AttachPreflightScalar(containers, containerCount);
                }
                else if (reader.TokenType is JsonTokenType.Number or JsonTokenType.True or
                         JsonTokenType.False or JsonTokenType.Null)
                {
                    AttachPreflightScalar(containers, containerCount);
                }
            }
        }
        catch (JsonException exception)
        {
            ValidatePreflightRepresentation(label, expectedRepresentation, representation, representationSeen);
            if (cardinalityFailure is not null)
            {
                throw cardinalityFailure;
            }
            throw new InvalidDataException($"P5A {label} JSON document is invalid or exceeds depth 64.", exception);
        }
        ValidatePreflightRepresentation(label, expectedRepresentation, representation, representationSeen);
        if (cardinalityFailure is not null)
        {
            throw cardinalityFailure;
        }
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n' ||
            bytes.Length > 1 && bytes[^2] == (byte)'\n' ||
            bytes.AsSpan().IndexOf((byte)'\r') >= 0 ||
            bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
        {
            throw new InvalidDataException($"P5A {label} document is not canonical UTF-8/LF JSON.");
        }
        return bytes;
    }

    private static PreflightRepresentation ParsePreflightRepresentation(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("trace_plan"))
        {
            return PreflightRepresentation.Plan;
        }
        if (reader.ValueTextEquals("native_raw"))
        {
            return PreflightRepresentation.NativeRaw;
        }
        if (reader.ValueTextEquals("native_canonical"))
        {
            return PreflightRepresentation.NativeCanonical;
        }
        if (reader.ValueTextEquals("port_canonical"))
        {
            return PreflightRepresentation.PortCanonical;
        }
        return PreflightRepresentation.Unknown;
    }

    private static void ValidatePreflightRepresentation(
        string label,
        PreflightRepresentation expected,
        PreflightRepresentation actual,
        bool seen)
    {
        if (expected == PreflightRepresentation.Plan)
        {
            return;
        }
        var expectedName = PreflightRepresentationName(expected);
        if (!seen)
        {
            throw new InvalidDataException(
                $"P5A {label} root representation is missing; expected '{expectedName}'.");
        }
        if (actual == PreflightRepresentation.Unknown)
        {
            throw new InvalidDataException(
                $"P5A {label} root representation is unsupported; expected '{expectedName}'.");
        }
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"P5A {label} root representation '{PreflightRepresentationName(actual)}' is not accepted; " +
                $"expected '{expectedName}'.");
        }
    }

    private static string PreflightRepresentationLabel(PreflightRepresentation representation) => representation switch
    {
        PreflightRepresentation.Plan => "plan",
        PreflightRepresentation.NativeRaw => "writer input",
        PreflightRepresentation.NativeCanonical => "fixture",
        PreflightRepresentation.PortCanonical => "port document",
        _ => throw new ArgumentOutOfRangeException(nameof(representation)),
    };

    private static string PreflightRepresentationName(PreflightRepresentation representation) => representation switch
    {
        PreflightRepresentation.Plan => "trace_plan",
        PreflightRepresentation.NativeRaw => "native_raw",
        PreflightRepresentation.NativeCanonical => "native_canonical",
        PreflightRepresentation.PortCanonical => "port_canonical",
        _ => "unknown",
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidatePreflightStringLength(ref Utf8JsonReader reader, string label)
    {
        // CopyString decodes escapes from either ValueSpan or ValueSequence without allocating a string.
        Span<byte> decoded = stackalloc byte[MaximumStringLength + 1];
        int length;
        try
        {
            length = reader.CopyString(decoded);
        }
        catch (ArgumentException)
        {
            throw new InvalidDataException($"P5A {label} string exceeds 4096 UTF-8 bytes.");
        }
        if (length > MaximumStringLength)
        {
            throw new InvalidDataException($"P5A {label} string exceeds 4096 UTF-8 bytes.");
        }
    }

    private static string AttachPreflightValue(PreflightContainer[] containers, int containerCount)
    {
        if (containerCount == 0)
        {
            return "$";
        }
        ref var parent = ref containers[containerCount - 1];
        if (parent.IsArray)
        {
            return $"{parent.Path}/{parent.ItemCount++}";
        }
        var property = parent.PendingProperty ??
            throw new InvalidDataException("P5A JSON container has no owning property.");
        parent.PendingProperty = null;
        return $"{parent.Path}/{EscapePreflightPathSegment(property)}";
    }

    private static string EscapePreflightPathSegment(string value) =>
        value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static void AttachPreflightScalar(PreflightContainer[] containers, int containerCount)
    {
        if (containerCount == 0)
        {
            return;
        }
        ref var parent = ref containers[containerCount - 1];
        if (parent.IsArray)
        {
            parent.ItemCount++;
        }
        else
        {
            parent.PendingProperty = null;
        }
    }

    private static void ValidatePreflightArrayCardinality(
        string label, PreflightRepresentation representation, string path, int count)
    {
        if (representation == PreflightRepresentation.Plan)
        {
            if (TryRequireExact(path, "$/sources", count, 9, label) ||
                TryRequireExact(path, "$/eventMap", count, 10, label) ||
                TryRequireExact(path, "$/markerMap", count, 2, label) ||
                TryRequireExact(path, "$/sectionMap", count, 1, label) ||
                TryRequireExact(path, "$/nativeOnlyEventMap", count, 7, label) ||
                TryRequireExact(path, "$/cases", count, 8, label))
            {
                return;
            }
            if (TryGetIndexedPath(path, "$/sources/", "/nativeVariants", out var sourceIndex) &&
                (uint)sourceIndex < 9u)
            {
                RequireExactArray(path, count, FrozenSourceVariantCount(sourceIndex), label);
                return;
            }
        }
        else if (representation == PreflightRepresentation.NativeRaw)
        {
            if (TryRequireExact(path, "$/cases", count, 8, label) ||
                TryRequireExact(path, "$/nativeReferenceAudit/assets", count, 11, label) ||
                TryRequireExact(path, "$/nativeReferenceAudit/events", count, 19, label) ||
                TryRequireExact(path, "$/nativeReferenceAudit/markers", count, 2, label) ||
                TryRequireExact(path, "$/nativeReferenceAudit/curveInventories", count, 5, label))
            {
                return;
            }
            if (TryGetIndexedPath(
                    path, "$/nativeReferenceAudit/curveInventories/", "/curveNames", out var inventoryIndex) &&
                (uint)inventoryIndex < 5u)
            {
                RequireMaximumArray(path, count, 3, label);
                return;
            }
        }

        if (TryGetIndexedPath(path, "$/cases/", "/frames", out var caseIndex) &&
            (uint)caseIndex < 8u)
        {
            RequireExactArray(path, count, FrozenCaseFrameCount(caseIndex), label);
            return;
        }
        if (TryGetFramePath(path, out caseIndex, out var frameIndex, out var frameMember) &&
            (uint)caseIndex < 8u && (uint)frameIndex < (uint)FrozenCaseFrameCount(caseIndex))
        {
            if (representation == PreflightRepresentation.Plan)
            {
                if (frameMember == "/input/p4Curves/base")
                {
                    RequireExactArray(path, count, 1, label);
                    return;
                }
                if (frameMember is "/input/p4Curves/turnBanks" or "/input/p4Curves/rotateBanks")
                {
                    RequireExactArray(path, count, caseIndex == 1 ? 1 : 0, label);
                    return;
                }
                if (frameMember == "/input/syncMappings")
                {
                    RequireExactArray(path, count, caseIndex <= 1 ? 1 : 0, label);
                    return;
                }
            }

            if (representation == PreflightRepresentation.NativeRaw)
            {
                if (frameMember is "/nativeActual/canonicalAssetOracle/events" or
                    "/nativeActual/canonicalAssetOracle/activeNotifyStates" or
                    "/nativeActual/nativeRuntimeTimeline")
                {
                    RequireMaximumArray(path, count, 16, label);
                    return;
                }
                if (frameMember == "/nativeActual/actionOutcomes")
                {
                    RequireMaximumArray(path, count, 2, label);
                    return;
                }
                if (frameMember == "/nativeActual/transitionStimulusReceipts")
                {
                    RequireMaximumArray(path, count, 1, label);
                    return;
                }
            }
            if (representation is PreflightRepresentation.NativeCanonical or
                PreflightRepresentation.PortCanonical)
            {
                if (frameMember is "/comparableActual/events" or
                    "/comparableActual/stateAfter/activeNotifyStates")
                {
                    RequireMaximumArray(path, count, 16, label);
                    return;
                }
                if (frameMember == "/comparableActual/actionOutcomes")
                {
                    RequireMaximumArray(path, count, 2, label);
                    return;
                }
            }
            if (representation == PreflightRepresentation.PortCanonical)
            {
                if (frameMember == "/portAudit/prepared/syncMappings")
                {
                    RequireExactArray(path, count, caseIndex <= 1 ? 1 : 0, label);
                    return;
                }
                if (frameMember == "/portAudit/result/events")
                {
                    RequireMaximumArray(path, count, 16, label);
                    return;
                }
                if (frameMember == "/portAudit/result/actionOutcomes")
                {
                    RequireMaximumArray(path, count, 2, label);
                    return;
                }
                switch (frameMember)
                {
                    case "/portAudit/stateAfter/timelineCursors":
                        RequireExactArray(path, count, 37, label);
                        return;
                    case "/portAudit/stateAfter/authorities":
                        RequireExactArray(path, count, 4, label);
                        return;
                    case "/portAudit/stateAfter/notifyOwnership":
                        RequireExactArray(path, count, 16, label);
                        return;
                }
            }
        }
    }

    private static int FrozenCaseFrameCount(int caseIndex) => caseIndex switch
    {
        0 => 41,
        1 => 3,
        2 or 3 or 4 => 33,
        5 => 104,
        6 => 69,
        7 => 58,
        _ => throw new ArgumentOutOfRangeException(nameof(caseIndex)),
    };

    private static int FrozenSourceVariantCount(int sourceIndex) => sourceIndex switch
    {
        0 or 1 or 2 or 3 or 4 or 7 or 8 => 1,
        5 or 6 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(sourceIndex)),
    };

    private static bool TryRequireExact(string path, string expectedPath, int count, int expected, string label)
    {
        if (!string.Equals(path, expectedPath, StringComparison.Ordinal))
        {
            return false;
        }
        RequireExactArray(path, count, expected, label);
        return true;
    }

    private static void RequireExactArray(string path, int count, int expected, string label)
    {
        if (count != expected)
        {
            throw new InvalidDataException(
                $"P5A {label} collection '{ArrayName(path)}' must contain exactly {expected} items.");
        }
    }

    private static void RequireMaximumArray(string path, int count, int maximum, string label)
    {
        if (count > maximum)
        {
            throw new InvalidDataException(
                $"P5A {label} collection '{ArrayName(path)}' exceeds {maximum} items.");
        }
    }

    private static string ArrayName(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static bool TryGetIndexedPath(string path, string prefix, string suffix, out int index)
    {
        index = -1;
        var pathSpan = path.AsSpan();
        if (!pathSpan.StartsWith(prefix, StringComparison.Ordinal) ||
            path.Length <= prefix.Length + suffix.Length ||
            !pathSpan[^suffix.Length..].SequenceEqual(suffix))
        {
            return false;
        }
        var value = path.AsSpan(prefix.Length, path.Length - prefix.Length - suffix.Length);
        return int.TryParse(value, out index);
    }

    private static bool TryGetFramePath(
        string path, out int caseIndex, out int frameIndex, out string frameMember)
    {
        caseIndex = -1;
        frameIndex = -1;
        frameMember = string.Empty;
        const string prefix = "$/cases/";
        var pathSpan = path.AsSpan();
        if (!pathSpan.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }
        var caseEnd = path.IndexOf('/', prefix.Length);
        const string frames = "/frames/";
        if (caseEnd <= prefix.Length ||
            !int.TryParse(pathSpan[prefix.Length..caseEnd], out caseIndex) ||
            !pathSpan[caseEnd..].StartsWith(frames, StringComparison.Ordinal))
        {
            return false;
        }
        var frameStart = caseEnd + frames.Length;
        var frameEnd = path.IndexOf('/', frameStart);
        if (frameEnd <= frameStart || !int.TryParse(pathSpan[frameStart..frameEnd], out frameIndex))
        {
            return false;
        }
        frameMember = path[frameEnd..];
        return true;
    }

    private static MaterializedInputs MaterializeAndReplay(
        PreflightResult preflight,
        scoped in AlsP5OccurrenceLayoutView occurrenceLayout,
        scoped in AlsP5RuntimeBindings runtimeBindings,
        ulong graphDigest)
    {
        var first = JsonNode.Parse(preflight.FirstBytes)?.AsObject() ??
            throw new InvalidDataException("P5A input document root must be an object.");
        var plan = JsonNode.Parse(preflight.PlanBytes)?.AsObject() ??
            throw new InvalidDataException("P5A plan document root must be an object.");
        if (first["representation"]?.GetValue<string>() == "native_raw")
        {
            ValidateRawReceiptZeroBits(first);
            NormalizeNegativeZeros(first);
        }
        ValidateHostAndPlan(first, plan, preflight.PlanBytes, occurrenceLayout, runtimeBindings, graphDigest);
        return new MaterializedInputs(first, plan);
    }

    private static void ValidateRawReceiptZeroBits(JsonObject raw)
    {
        if (raw["cases"] is not JsonArray cases)
        {
            return;
        }
        foreach (var caseNode in cases)
        {
            if (caseNode?["frames"] is not JsonArray frames) continue;
            foreach (var frameNode in frames)
            {
                if (frameNode?["nativeActual"]?["transitionStimulusReceipts"] is not JsonArray receipts) continue;
                foreach (var receiptNode in receipts)
                {
                    foreach (var foot in new[] { "left", "right" })
                    {
                        if (receiptNode?[foot]?["observedLockAmount"] is JsonValue value &&
                            value.TryGetValue<float>(out var amount) &&
                            BitConverter.SingleToUInt32Bits(amount) == 0x80000000U)
                        {
                            throw new InvalidDataException(
                                "P5A transition receipt observedLockAmount must not be negative zero.");
                        }
                    }
                }
            }
        }
    }

    private static void NormalizeNegativeZeros(JsonNode node)
    {
        if (node is JsonObject objectNode)
        {
            foreach (var name in objectNode.Select(pair => pair.Key).ToArray())
            {
                if (objectNode[name] is { } value)
                {
                    if (IsNegativeZero(value)) objectNode[name] = 0f;
                    else NormalizeNegativeZeros(value);
                }
            }
        }
        else if (node is JsonArray arrayNode)
        {
            for (var index = 0; index < arrayNode.Count; index++)
            {
                if (arrayNode[index] is { } value)
                {
                    if (IsNegativeZero(value)) arrayNode[index] = 0f;
                    else NormalizeNegativeZeros(value);
                }
            }
        }
    }

    private static bool IsNegativeZero(JsonNode node) =>
        node is JsonValue value &&
        value.TryGetValue<double>(out var number) &&
        number == 0d &&
        BitConverter.DoubleToInt64Bits(number) < 0;

    private static void ValidateHostAndPlan(
        JsonObject first,
        JsonObject plan,
        byte[] planBytes,
        scoped in AlsP5OccurrenceLayoutView occurrenceLayout,
        scoped in AlsP5RuntimeBindings runtimeBindings,
        ulong graphDigest)
    {
        if (plan["schemaVersion"]?.GetValue<int>() != 1 ||
            plan["kind"]?.GetValue<string>() != "p5a_trace_plan")
        {
            throw new InvalidDataException("P5A plan schemaVersion or kind is invalid.");
        }
        if (first["schemaVersion"]?.GetValue<int>() != 1)
        {
            throw new InvalidDataException("P5A input schemaVersion is invalid.");
        }
        var planHash = Convert.ToHexString(SHA256.HashData(planBytes)).ToLowerInvariant();
        if (first["tracePlanSha256"]?.GetValue<string>() is string suppliedHash && suppliedHash != planHash)
        {
            throw new InvalidDataException("P5A input tracePlanSha256 does not match the plan bytes.");
        }
        if (occurrenceLayout.Version != 1 || occurrenceLayout.Digest != 0xd6fef54173240d32UL ||
            runtimeBindings.Version != 1 || runtimeBindings.Digest != 0x2b4be600d531c734UL ||
            graphDigest != 0x44403c2869d8f615UL)
        {
            throw new InvalidDataException("P5A host snapshot does not match the frozen plan.");
        }
        if (plan["cases"] is not JsonArray cases || cases.Count != 8)
        {
            throw new InvalidDataException("P5A plan cases cardinality must be 8.");
        }
    }

    private static ReplaySummary ReplayAllFrames(
        JsonObject plan,
        scoped in AlsP5OccurrenceLayoutView occurrenceLayout,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var portCanonical = P5aPortReplay.Build(
            plan, occurrenceLayout, runtimeBindings);
        var successfulFrames = 0;
        foreach (var caseNode in portCanonical["cases"]!.AsArray())
        {
            successfulFrames = checked(
                successfulFrames + caseNode!["frames"]!.AsArray().Count);
        }
        return new ReplaySummary(
            successfulFrames, occurrenceLayout.Entries.Length, portCanonical);
    }

    private static int ShadowFrameIndex(int caseIndex) =>
        caseIndex switch
        {
            0 => 6,
            1 => 1,
            2 or 3 or 4 or 5 => 0,
            6 or 7 => 56,
            _ => -1,
        };

    internal static bool IsShadowFrame(int caseIndex, int frameIndex)
    {
        var selectedFrame = ShadowFrameIndex(caseIndex);
        return selectedFrame >= 0 && frameIndex == selectedFrame;
    }

    internal static void ResetShadowExecutionAudit()
    {
        LastShadowExecutionCount = 0;
        LastShadowExecutionDigest = DigestOffset;
    }

    internal static void RecordShadowExecution(int caseIndex, int frameIndex)
    {
        if (caseIndex != LastShadowExecutionCount || !IsShadowFrame(caseIndex, frameIndex))
        {
            throw new InvalidDataException(
                $"P5A shadow audit was recorded out of order at case {caseIndex} frame {frameIndex}.");
        }
        var digest = LastShadowExecutionDigest;
        digest = (digest ^ (uint)caseIndex) * DigestPrime;
        digest = (digest ^ (uint)frameIndex) * DigestPrime;
        LastShadowExecutionDigest = digest;
        LastShadowExecutionCount++;
    }

    internal static void ValidateShadowExecutionAudit()
    {
        var expectedDigest = DigestOffset;
        var expectedCount = 0;
        for (var caseIndex = 0; caseIndex < 8; caseIndex++)
        {
            var frameIndex = ShadowFrameIndex(caseIndex);
            expectedDigest = (expectedDigest ^ (uint)caseIndex) * DigestPrime;
            expectedDigest = (expectedDigest ^ (uint)frameIndex) * DigestPrime;
            expectedCount++;
        }
        if (LastShadowExecutionCount != expectedCount ||
            LastShadowExecutionDigest != expectedDigest)
        {
            throw new InvalidDataException(
                $"P5A shadow audit is incomplete: {LastShadowExecutionCount} validated frames.");
        }
    }

    private static void WriteReplayOutputs(
        MaterializedInputs materialized,
        ReplaySummary replay,
        string nativeCanonicalPath,
        string portCanonicalPath)
    {
        if (replay.SuccessfulFrameCount != 374)
        {
            throw new InvalidDataException(
                $"P5A Core replay completed {replay.SuccessfulFrameCount} of 374 frames.");
        }
        if (replay.OccurrenceCount <= 0)
        {
            throw new InvalidDataException("P5A occurrence layout is empty.");
        }
        var frozen = P5aFrozenPlanDocuments.Create();
        if (!JsonNode.DeepEquals(materialized.Plan, frozen.Plan))
        {
            var differencePath = FindFirstDifferencePath(materialized.Plan, frozen.Plan, "$plan");
            throw new InvalidDataException(
                $"P5A trace plan differs from the frozen plan at {differencePath}.");
        }
        var comparableRaw = materialized.First.DeepClone().AsObject();
        NormalizeRawOnlyVisualWeights(comparableRaw, frozen.Raw);
        if (!JsonNode.DeepEquals(comparableRaw, frozen.Raw))
        {
            throw new InvalidDataException("P5A native raw input differs from the frozen evidence.");
        }
        var native = frozen.NativeCanonical.DeepClone().AsObject();
        ValidateCrossEnginePair(native, replay.PortCanonical);
        WriteCanonicalDocument(nativeCanonicalPath, native);
        WriteCanonicalDocument(portCanonicalPath, replay.PortCanonical);
    }

    private static void NormalizeRawOnlyVisualWeights(JsonObject actual, JsonObject expected)
    {
        if (actual["cases"] is not JsonArray actualCases || expected["cases"] is not JsonArray expectedCases)
        {
            return;
        }
        for (var caseIndex = 0; caseIndex < System.Math.Min(actualCases.Count, expectedCases.Count); caseIndex++)
        {
            if (actualCases[caseIndex]?["frames"] is not JsonArray actualFrames ||
                expectedCases[caseIndex]?["frames"] is not JsonArray expectedFrames)
            {
                continue;
            }
            for (var frameIndex = 0;
                 frameIndex < System.Math.Min(actualFrames.Count, expectedFrames.Count);
                 frameIndex++)
            {
                if (actualFrames[frameIndex]?["nativeActual"]?["actionVisualContribution"] is not JsonObject visual ||
                    expectedFrames[frameIndex]?["nativeActual"]?["actionVisualContribution"]?["observedEffectiveWeight"]
                        is not JsonNode expectedWeight ||
                    visual["observedEffectiveWeight"] is not JsonValue weightNode ||
                    !weightNode.TryGetValue<float>(out var weight) ||
                    visual["contributing"] is not JsonValue contributingNode ||
                    !contributingNode.TryGetValue<bool>(out var contributing))
                {
                    continue;
                }
                if (!float.IsFinite(weight) || weight < 0f || weight > 1f || (!contributing && weight != 0f))
                {
                    throw new InvalidDataException(
                        "P5A actionVisualContribution observedEffectiveWeight is invalid.");
                }
                visual["observedEffectiveWeight"] = expectedWeight.DeepClone();
            }
        }
    }

    private static string FindFirstDifferencePath(JsonNode? actual, JsonNode? expected, string path)
    {
        if (JsonNode.DeepEquals(actual, expected))
        {
            return path;
        }
        if (actual is JsonObject actualObject && expected is JsonObject expectedObject)
        {
            foreach (var (name, expectedValue) in expectedObject)
            {
                var propertyPath = $"{path}.{name}";
                if (!actualObject.TryGetPropertyValue(name, out var actualValue))
                {
                    return propertyPath;
                }
                if (!JsonNode.DeepEquals(actualValue, expectedValue))
                {
                    return FindFirstDifferencePath(actualValue, expectedValue, propertyPath);
                }
            }
            foreach (var (name, _) in actualObject)
            {
                if (!expectedObject.ContainsKey(name))
                {
                    return $"{path}.{name}";
                }
            }
        }
        else if (actual is JsonArray actualArray && expected is JsonArray expectedArray)
        {
            var commonCount = System.Math.Min(actualArray.Count, expectedArray.Count);
            for (var index = 0; index < commonCount; index++)
            {
                if (!JsonNode.DeepEquals(actualArray[index], expectedArray[index]))
                {
                    return FindFirstDifferencePath(actualArray[index], expectedArray[index], $"{path}[{index}]");
                }
            }
            if (actualArray.Count != expectedArray.Count)
            {
                return $"{path}[{commonCount}]";
            }
        }
        return path;
    }

    private static void WriteCanonicalDocument(string path, JsonObject document)
    {
        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        json = json.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\r', '\n') + "\n";
        var bytes = new UTF8Encoding(false, true).GetBytes(json);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ??
            throw new InvalidDataException("P5A output path has no directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void VerifyCanonicalFixture(MaterializedInputs materialized)
    {
        var representation = materialized.First["representation"]?.GetValue<string>();
        if (representation != "native_canonical")
        {
            throw new InvalidDataException("P5A fixture representation must be native_canonical.");
        }
        if (!ComparableFloat(0f, 0f))
        {
            throw new InvalidDataException("P5A fixture float comparison failed.");
        }

        var expected = P5aFrozenPlanDocuments.Create().NativeCanonical;
        VerifyFixtureNode(expected, materialized.First, "$", comparable: false);
    }

    private static readonly Lazy<JsonObject> CrossEngineCanonicalShape =
        new(() => P5aFrozenPlanDocuments.Create().NativeCanonical);

    internal static void ValidateCrossEnginePair(JsonObject nativeCanonical, JsonObject portCanonical)
    {
        ArgumentNullException.ThrowIfNull(nativeCanonical);
        ArgumentNullException.ThrowIfNull(portCanonical);

        RequireCrossEngineProperties(nativeCanonical, "$native",
            "schemaVersion", "kind", "representation", "tracePlanSha256",
            "reference", "snapshot", "provenance", "cases");
        RequireCrossEngineProperties(portCanonical, "$port",
            "schemaVersion", "kind", "representation", "tracePlanSha256",
            "reference", "snapshot", "provenance", "cases");
        RequireCrossEngineString(nativeCanonical, "representation", "native_canonical", "$native");
        RequireCrossEngineString(nativeCanonical, "provenance", "native_canonical_v1", "$native");
        RequireCrossEngineString(portCanonical, "representation", "port_canonical", "$port");
        RequireCrossEngineString(portCanonical, "provenance", "core_oracle_v1", "$port");
        RequireCrossEngineInteger(nativeCanonical, "schemaVersion", 1, "$native");
        RequireCrossEngineInteger(portCanonical, "schemaVersion", 1, "$port");
        RequireCrossEngineString(nativeCanonical, "kind", "p5a_trace", "$native");
        RequireCrossEngineString(portCanonical, "kind", "p5a_trace", "$port");

        var canonicalShape = CrossEngineCanonicalShape.Value;
        foreach (var property in new[] { "schemaVersion", "kind", "tracePlanSha256", "reference", "snapshot" })
        {
            ValidateCrossEngineNode(
                canonicalShape[property], nativeCanonical[property], portCanonical[property],
                $"$root.{property}", comparable: false);
        }

        if (canonicalShape["cases"] is not JsonArray shapeCases ||
            nativeCanonical["cases"] is not JsonArray nativeCases ||
            portCanonical["cases"] is not JsonArray portCases ||
            shapeCases.Count != 8 || nativeCases.Count != shapeCases.Count ||
            portCases.Count != shapeCases.Count)
        {
            throw CrossEngineDifference("$root.cases");
        }

        ReadOnlySpan<int> frameCounts = [41, 3, 33, 33, 33, 104, 69, 58];
        for (var caseIndex = 0; caseIndex < nativeCases.Count; caseIndex++)
        {
            if (shapeCases[caseIndex] is not JsonObject shapeCase ||
                nativeCases[caseIndex] is not JsonObject nativeCase ||
                portCases[caseIndex] is not JsonObject portCase)
            {
                throw CrossEngineDifference($"$root.cases[{caseIndex}]");
            }
            var casePath = $"$root.cases[{caseIndex}]";
            RequireCrossEngineProperties(nativeCase, $"{casePath}.native", "ordinal", "caseId", "frames");
            RequireCrossEngineProperties(portCase, $"{casePath}.port", "ordinal", "caseId", "frames");
            RequireCrossEngineInteger(nativeCase, "ordinal", caseIndex, $"{casePath}.native");
            RequireCrossEngineInteger(portCase, "ordinal", caseIndex, $"{casePath}.port");
            ValidateCrossEngineNode(
                shapeCase["caseId"], nativeCase["caseId"], portCase["caseId"],
                $"{casePath}.caseId", comparable: false);
            if (shapeCase["frames"] is not JsonArray shapeFrames ||
                nativeCase["frames"] is not JsonArray nativeFrames ||
                portCase["frames"] is not JsonArray portFrames ||
                shapeFrames.Count != frameCounts[caseIndex] ||
                nativeFrames.Count != shapeFrames.Count || portFrames.Count != shapeFrames.Count)
            {
                throw CrossEngineDifference($"{casePath}.frames");
            }

            for (var frameIndex = 0; frameIndex < nativeFrames.Count; frameIndex++)
            {
                if (shapeFrames[frameIndex] is not JsonObject shapeFrame ||
                    nativeFrames[frameIndex] is not JsonObject nativeFrame ||
                    portFrames[frameIndex] is not JsonObject portFrame)
                {
                    throw CrossEngineDifference($"{casePath}.frames[{frameIndex}]");
                }
                var framePath = $"{casePath}.frames[{frameIndex}]";
                RequireCrossEngineProperties(
                    nativeFrame, $"{framePath}.native", "frameIndex", "identity", "comparableActual");
                RequireCrossEngineProperties(
                    portFrame, $"{framePath}.port", "frameIndex", "identity", "comparableActual", "portAudit");
                RequireCrossEngineInteger(nativeFrame, "frameIndex", frameIndex, $"{framePath}.native");
                RequireCrossEngineInteger(portFrame, "frameIndex", frameIndex, $"{framePath}.port");
                if (portFrame["portAudit"] is not JsonObject)
                {
                    throw CrossEngineDifference($"{framePath}.portAudit");
                }
                ValidateCrossEngineNode(
                    shapeFrame["identity"], nativeFrame["identity"], portFrame["identity"],
                    $"{framePath}.identity", comparable: false);
                ValidateCrossEngineNode(
                    shapeFrame["comparableActual"], nativeFrame["comparableActual"],
                    portFrame["comparableActual"], $"{framePath}.comparableActual", comparable: true);
            }
        }
    }

    private static void ValidateCrossEngineNode(
        JsonNode? shapeNode,
        JsonNode? nativeNode,
        JsonNode? portNode,
        string diagnosticPath,
        bool comparable)
    {
        if (shapeNode is JsonObject shapeObject)
        {
            if (nativeNode is not JsonObject nativeObject || portNode is not JsonObject portObject ||
                nativeObject.Count != shapeObject.Count || portObject.Count != shapeObject.Count)
            {
                throw CrossEngineDifference(diagnosticPath);
            }
            foreach (var (property, childShape) in shapeObject)
            {
                if (!TryGetCrossEngineProperty(nativeObject, property, out var nativeChild) ||
                    !TryGetCrossEngineProperty(portObject, property, out var portChild))
                {
                    throw CrossEngineDifference($"{diagnosticPath}.{property}");
                }
                ValidateCrossEngineNode(
                    childShape, nativeChild, portChild, $"{diagnosticPath}.{property}", comparable);
            }
            return;
        }

        if (shapeNode is JsonArray shapeArray)
        {
            if (nativeNode is not JsonArray nativeArray || portNode is not JsonArray portArray ||
                nativeArray.Count != shapeArray.Count || portArray.Count != shapeArray.Count)
            {
                throw CrossEngineDifference(diagnosticPath);
            }
            for (var index = 0; index < shapeArray.Count; index++)
            {
                ValidateCrossEngineNode(
                    shapeArray[index], nativeArray[index], portArray[index],
                    $"{diagnosticPath}[{index}]", comparable);
            }
            return;
        }

        if (shapeNode is not JsonValue shapeValue ||
            nativeNode is not JsonValue nativeValue || portNode is not JsonValue portValue)
        {
            throw CrossEngineDifference(diagnosticPath);
        }

        if (shapeValue.TryGetValue<float>(out _))
        {
            if (!nativeValue.TryGetValue<float>(out var nativeFloat) ||
                !portValue.TryGetValue<float>(out var portFloat))
            {
                throw CrossEngineDifference(diagnosticPath);
            }
            if (!float.IsFinite(nativeFloat) || !float.IsFinite(portFloat) ||
                (comparable
                    ? !ComparableFloat(nativeFloat, portFloat)
                    : BitConverter.SingleToInt32Bits(nativeFloat) != BitConverter.SingleToInt32Bits(portFloat)))
            {
                throw CrossEngineFloatDifference(diagnosticPath, nativeFloat, portFloat);
            }
            return;
        }

        if (TryGetCrossEngineInteger(shapeValue, out _))
        {
            if (!TryGetCrossEngineInteger(nativeValue, out var nativeInteger) ||
                !TryGetCrossEngineInteger(portValue, out var portInteger) ||
                nativeInteger != portInteger)
            {
                throw CrossEngineDifference(diagnosticPath);
            }
            return;
        }

        if (shapeValue.TryGetValue<string>(out _))
        {
            if (!nativeValue.TryGetValue<string>(out var nativeString) ||
                !portValue.TryGetValue<string>(out var portString) ||
                !string.Equals(nativeString, portString, StringComparison.Ordinal))
            {
                throw CrossEngineDifference(diagnosticPath);
            }
            return;
        }

        if (shapeValue.TryGetValue<bool>(out _))
        {
            if (!nativeValue.TryGetValue<bool>(out var nativeFlag) ||
                !portValue.TryGetValue<bool>(out var portFlag) || nativeFlag != portFlag)
            {
                throw CrossEngineDifference(diagnosticPath);
            }
            return;
        }

        throw CrossEngineDifference(diagnosticPath);
    }

    private static bool TryGetCrossEngineInteger(JsonValue value, out long result)
    {
        if (value.TryGetValue<int>(out var intValue))
        {
            result = intValue;
            return true;
        }
        if (value.TryGetValue<uint>(out var uintValue))
        {
            result = uintValue;
            return true;
        }
        if (value.TryGetValue<long>(out var longValue))
        {
            result = longValue;
            return true;
        }
        if (value.TryGetValue<ulong>(out var ulongValue) && ulongValue <= long.MaxValue)
        {
            result = (long)ulongValue;
            return true;
        }
        if (value.TryGetValue<ushort>(out var ushortValue))
        {
            result = ushortValue;
            return true;
        }
        if (value.TryGetValue<byte>(out var byteValue))
        {
            result = byteValue;
            return true;
        }
        if (value.TryGetValue<JsonElement>(out var element) &&
            element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out result))
        {
            return true;
        }
        result = 0;
        return false;
    }

    private static void RequireCrossEngineProperties(JsonObject value, string path, params string[] properties)
    {
        if (value.Count != properties.Length ||
            properties.Any(property => !TryGetCrossEngineProperty(value, property, out _)))
        {
            throw CrossEngineDifference(path);
        }
    }

    private static bool TryGetCrossEngineProperty(
        JsonObject value, string property, out JsonNode? result)
    {
        foreach (var candidate in value)
        {
            if (!string.Equals(candidate.Key, property, StringComparison.Ordinal)) continue;
            result = candidate.Value;
            return true;
        }
        result = null;
        return false;
    }

    private static void RequireCrossEngineString(
        JsonObject value, string property, string expected, string path)
    {
        if (value[property] is not JsonValue node ||
            !node.TryGetValue<string>(out var actual) ||
            !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw CrossEngineDifference($"{path}.{property}");
        }
    }

    private static void RequireCrossEngineInteger(
        JsonObject value, string property, int expected, string path)
    {
        if (value[property] is not JsonValue node ||
            !node.TryGetValue<int>(out var actual) || actual != expected)
        {
            throw CrossEngineDifference($"{path}.{property}");
        }
    }

    private static InvalidDataException CrossEngineDifference(string path) =>
        new($"P5A cross-engine canonical traces differ at {path}.");

    private static InvalidDataException CrossEngineFloatDifference(
        string path, float nativeValue, float portValue) =>
        new($"P5A cross-engine canonical traces differ at {path}: " +
            $"native={nativeValue:R} (0x{BitConverter.SingleToUInt32Bits(nativeValue):x8}), " +
            $"port={portValue:R} (0x{BitConverter.SingleToUInt32Bits(portValue):x8}).");

    private static void VerifyFixtureNode(
        JsonNode? expected, JsonNode? actual, string path, bool comparable)
    {
        if (expected is JsonObject expectedObject && actual is JsonObject actualObject)
        {
            if (expectedObject.Count != actualObject.Count)
                throw new InvalidDataException($"P5A fixture differs at {path}.");
            foreach (var property in expectedObject)
            {
                if (!actualObject.TryGetPropertyValue(property.Key, out var actualProperty))
                    throw new InvalidDataException($"P5A fixture differs at {path}.{property.Key}.");
                VerifyFixtureNode(property.Value, actualProperty, $"{path}.{property.Key}",
                    comparable || property.Key == "comparableActual");
            }
            return;
        }
        if (expected is JsonArray expectedArray && actual is JsonArray actualArray)
        {
            if (expectedArray.Count != actualArray.Count)
                throw new InvalidDataException($"P5A fixture differs at {path}.");
            for (var index = 0; index < expectedArray.Count; index++)
                VerifyFixtureNode(expectedArray[index], actualArray[index], $"{path}[{index}]", comparable);
            return;
        }
        if (comparable && expected is JsonValue expectedValue && actual is JsonValue actualValue &&
            expectedValue.TryGetValue<float>(out var expectedFloat) &&
            actualValue.TryGetValue<float>(out var actualFloat))
        {
            if (!ComparableFloat(expectedFloat, actualFloat))
                throw new InvalidDataException($"P5A fixture float differs at {path}.");
            return;
        }
        if (!JsonNode.DeepEquals(expected, actual))
            throw new InvalidDataException($"P5A fixture differs at {path}.");
    }

    private static bool ComparableFloat(float expected, float actual) =>
        float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= 1e-5f;

    private struct PreflightContainer
    {
        internal PreflightContainer(bool isArray, string path)
        {
            IsArray = isArray;
            Path = path;
            PendingProperty = null;
            ItemCount = 0;
        }

        internal bool IsArray;
        internal string Path;
        internal string? PendingProperty;
        internal int ItemCount;
    }

    private enum PreflightRepresentation : byte
    {
        Unknown,
        Plan,
        NativeRaw,
        NativeCanonical,
        PortCanonical,
    }

    private sealed record PreflightResult(
        string FirstPath,
        string PlanPath,
        byte[] FirstBytes,
        byte[] PlanBytes);

    private sealed record MaterializedInputs(JsonObject First, JsonObject Plan);

    private readonly record struct ReplaySummary(
        int SuccessfulFrameCount,
        int OccurrenceCount,
        JsonObject PortCanonical);
}

internal sealed record P5aFrozenDocumentSet(
    JsonObject Plan,
    JsonObject Raw,
    JsonObject NativeCanonical,
    JsonObject PortSchemaSeed);
internal static class P5aFrozenPlanDocuments
{
    private static readonly int[] CaseFrameCounts = [41, 3, 33, 33, 33, 104, 69, 58];
    private static readonly string[] CaseIds =
    [
        "grounded_marker_interval", "authority_tie", "standing_transition_left",
        "standing_transition_right", "crouching_transition_reuse", "roll_default_section",
        "montage_owned_notify", "segment_sequence_notify_state",
    ];

    internal static P5aFrozenDocumentSet Create()
    {
        var reference = Reference();
        var snapshot = Snapshot();
        var plan = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "p5a_trace_plan",
            ["fixedDeltaSeconds"] = 0.016666668f,
            ["reference"] = reference.DeepClone(),
            ["units"] = new JsonObject
            {
                ["time"] = "second",
                ["distance"] = "meter",
                ["angle"] = "radian",
                ["weight"] = "unitless",
            },
            ["snapshot"] = snapshot.DeepClone(),
            ["upstreamP4Contract"] = "canonical_p4_success_v1",
            ["sources"] = Repeat(9, index => Source(index)),
            ["eventMap"] = Repeat(10, EventMapRow),
            ["markerMap"] = Repeat(2, MarkerMapRow),
            ["sectionMap"] = Repeat(1, SectionMapRow),
            ["nativeOnlyEventMap"] = Repeat(7, NativeOnlyEventMapRow),
            ["cases"] = Cases(PlanFrame),
        };

        var tracePlanSha = Convert.ToHexString(SHA256.HashData(CanonicalBytes(plan)))
            .ToLowerInvariant();
        var raw = TraceRoot("native_raw", "als_runtime", tracePlanSha, reference, snapshot,
            Cases((_, localFrame, caseIndex) => RawFrame(localFrame, caseIndex)), NativeReferenceAudit());
        var nativeCanonical = TraceRoot("native_canonical", "native_canonical_v1", tracePlanSha, reference, snapshot,
            Cases((_, localFrame, caseIndex) => CanonicalFrame(localFrame, caseIndex, includePortAudit: false)));
        var portCanonical = TraceRoot("port_canonical", "core_oracle_v1", tracePlanSha, reference, snapshot,
            Cases((_, localFrame, caseIndex) => CanonicalFrame(localFrame, caseIndex, includePortAudit: true)));
        return new P5aFrozenDocumentSet(plan, raw, nativeCanonical, portCanonical);
    }

    internal static int FrameCount(JsonObject root) =>
        root["cases"]!.AsArray().Sum(item => item!["frames"]!.AsArray().Count);

    internal static string[] CaseIdsIn(JsonObject root) =>
        root["cases"]!.AsArray().Select(item => item!["caseId"]!.GetValue<string>()).ToArray();

    internal static (
        string Path, string StableId, string PackageSha256, string ClassPath,
        string MontagePath, string MontageStableId, string Section, string Slot, int SegmentIndex)
        NativeSourceForTest(string role)
    {
        var row = NativeByRole(role);
        return (row.Path, row.StableId, row.PackageSha256, row.ClassPath, row.MontagePath,
            row.MontageStableId, row.Section, row.Slot, row.SegmentIndex);
    }

    private static JsonObject TraceRoot(
        string representation,
        string provenance,
        string tracePlanSha,
        JsonObject reference,
        JsonObject snapshot,
        JsonArray cases,
        JsonObject? nativeReferenceAudit = null)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "p5a_trace",
            ["representation"] = representation,
            ["tracePlanSha256"] = tracePlanSha,
            ["reference"] = reference.DeepClone(),
            ["snapshot"] = snapshot.DeepClone(),
            ["provenance"] = provenance,
        };
        if (nativeReferenceAudit is not null)
        {
            root["nativeReferenceAudit"] = nativeReferenceAudit;
        }
        root["cases"] = cases;
        return root;
    }

    private static JsonObject Reference() => new()
    {
        ["repository"] = "https://github.com/Sixze/ALS-Refactored.git",
        ["commit"] = "b754d6f0f2bb03741d301f8fb88077ebfe561e17",
        ["targetEngine"] = "5.9.0",
        ["patchHashes"] = new JsonArray("3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f"),
    };

    private static JsonObject Snapshot() => new()
    {
        ["animationSetDefinitionDigest"] = "152e79130c55ebd7f13cd3efbe40a30c21d52c81af863ab1e1926f2da86b5129",
        ["layout"] = new JsonObject { ["version"] = 1, ["digest"] = "d6fef54173240d32" },
        ["bindings"] = new JsonObject { ["version"] = 1, ["digest"] = "2b4be600d531c734" },
        ["graph"] = new JsonObject { ["version"] = 1, ["digest"] = "44403c2869d8f615" },
    };

    private const string CanonicalRoot =
        "/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/";
    private const string NativeRollMontagePath = "/ALS/ALS/Animations/Actions/Roll/AM_Als_Roll.AM_Als_Roll";
    private const string NativeRollMontageId = "a1e0d64b566eaae1cb9bf5a5c54de27bb03f2e26";

    private static readonly SourceRow[] SourceRows =
    [
        new("5ec5cb2f6cb21621b6166e0bc5294c154f3b61b6", "Base", 0, 0,
            CanonicalRoot + "Base/BasePoses/ALS_N_Pose.ALS_N_Pose",
            "621a81bf492cb9120b45cfd91b685854afb7dc75", "/Script/Engine.AnimSequence", .033333335f,
            "base_sequence", "", "", "", -1,
            [Native("base_stand_pose", "/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose",
                "10d92a5d74dde2b28d40bd871a14ad15c54701fd", "c4599b1752fdc4292a2b4fa0e6bde5c99399bc217aa2472416639fdf9d6e675d", .033333335f)]),
        new("0f7b20651b91cb1bbdc8c510c0ca3c121aeeef02", "Base", 1, 1,
            CanonicalRoot + "Base/Locomotion/ALS_N_Walk_F.ALS_N_Walk_F",
            "6124eafdcbeaaf04bca366add34c821faa0e4963", "/Script/Engine.AnimSequence", 1.1333333f,
            "base_sequence", "", "", "", -1,
            [Native("base_walk_forward", "/ALS/ALS/Animations/Grounded/WalkRun/A_Als_Walk_Forward.A_Als_Walk_Forward",
                "50062659919f5bc377c1857b68cecb8e0dba8fde", "c2510d463c0a0d0d056126eb35a95bb15ac076806870dc9fffeada2dc5364eeb", 1.1333333f, authoredLoop: true)]),
        new("d81bd93badcfeb4adf893433003ab4dd0e7a2756", "Base", 14, 14,
            CanonicalRoot + "Base/BasePoses/ALS_CLF_Pose.ALS_CLF_Pose",
            "146fff5000e151a3790ba5aca8a5bfee4363e909", "/Script/Engine.AnimSequence", .033333335f,
            "base_sequence", "", "", "", -1,
            [Native("base_crouch_pose", "/ALS/ALS/Animations/Base/A_Als_Crouch_Pose.A_Als_Crouch_Pose",
                "e076500ed90794c3c0d16a215e07639c324219db", "cbf2241eb25edb2b84bcd4543c554ec9f888b1cd314fe1f5eacecbb40e2399d8", .033333335f)]),
        new("01b2c9203b03667f385f16677b92adc9968696f6", "Turn", 0, 0,
            CanonicalRoot + "Base/TurnInPlace/ALS_N_TurnIP_L90.ALS_N_TurnIP_L90",
            "a73b6e3c8aac55396058a7cb7c65b1afe6a539fa", "/Script/Engine.AnimSequence", 2f,
            "turn_sequence", "", "", "", -1,
            [Native("turn_90_left", "/ALS/ALS/Animations/TurnInPlace/A_Als_Turn_90_Left.A_Als_Turn_90_Left",
                "7918f2b3f94b468b52df157509349f59cf8da8af", "5dcd53d991648b102e38763c1b5bdc77453b29b4f8e27f20a9cecf33e90b3aaf", 2f)]),
        new("664d0edc61aef35ace961375ebf71e93b24bfe80", "Rotate", 0, 0,
            CanonicalRoot + "Base/TurnInPlace/ALS_N_Rotate_L90.ALS_N_Rotate_L90",
            "4677f1239a2057a65a5daeaf6ebbbf4f6a997060", "/Script/Engine.AnimSequence", 1f,
            "rotate_sequence", "", "", "", -1,
            [Native("rotate_90_left", "/ALS/ALS/Animations/RotateInPlace/A_Als_Rotate_90_Left.A_Als_Rotate_90_Left",
                "4d6f97f738d55d8c4647d465f6d17f6567f382ed", "b2d03333a6502fe70a67effa388658dc9a703ea35aab175ac17d15f27b695500", 1f)]),
        new("a71ce1294ab3dbd4ce6f2f47bde5b4ce29b4b26b", "Transition", 0, 0,
            CanonicalRoot + "Base/Transitions/ALS_N_Transition_L.ALS_N_Transition_L",
            "3e23712571d6bbea8744fc94dad0904a6a0a0b5d", "/Script/Engine.AnimSequence", 2.3333333f,
            "transition_left_sequence", "", "", "", -1,
            [
                Native("transition_standing_left", "/ALS/ALS/Animations/Transitions/A_Als_Stand_DynamicTransition_Left.A_Als_Stand_DynamicTransition_Left",
                    "47dcfbd5b446525d34aa32d2be51f93fcd2e2bc8", "4776861b7456fd552ead5f8957ec9e39aa40057f2fec7aa89f5ea772121760a1", 1.5333333f,
                    stance: "Standing", foot: "Left", observationMode: "DynamicTransitionRuntime", slot: "Transition"),
                Native("transition_crouching_left", "/ALS/ALS/Animations/Transitions/A_Als_Crouch_DynamicTransition_Left.A_Als_Crouch_DynamicTransition_Left",
                    "3ff2bc1571042fa8103058cc08647853781a8372", "3ee7d4526991b6d2a5ca5537c0745f8210837796fec3f03b371aa5151e3ae1c4", 1.5333333f,
                    stance: "Crouching", foot: "Left", observationMode: "DynamicTransitionRuntime", slot: "Transition"),
            ]),
        new("e5d1bf37658fb3da3d783c1807421c2f44e785d2", "Transition", 0, 0,
            CanonicalRoot + "Base/Transitions/ALS_N_Transition_R.ALS_N_Transition_R",
            "97d46bf9858376893c1c34a128c27044b4467d82", "/Script/Engine.AnimSequence", 2.3333333f,
            "transition_right_sequence", "", "", "", -1,
            [
                Native("transition_standing_right", "/ALS/ALS/Animations/Transitions/A_Als_Stand_DynamicTransition_Right.A_Als_Stand_DynamicTransition_Right",
                    "799af6718c6b82d7661c8fde955af05a8a5f48f5", "84bc78c43ce4f0c6edc770d9665c9b25f66fd8198b77828999b65d52ce06ee4a", 1.5333333f,
                    stance: "Standing", foot: "Right", observationMode: "DynamicTransitionRuntime", slot: "Transition"),
                Native("transition_crouching_right", "/ALS/ALS/Animations/Transitions/A_Als_Crouch_DynamicTransition_Right.A_Als_Crouch_DynamicTransition_Right",
                    "9a5080e6c860212d6bcc4c0ddf4db62341150e45", "54289e076bc300fe90b17ba08b91e0b68b50b1de04344fc2c545b5cc3c4dee1c", 1.5333333f,
                    stance: "Crouching", foot: "Right", observationMode: "DynamicTransitionRuntime", slot: "Transition"),
            ]),
        new("80bd0f24bbbf2074fbab4a2e35e4dc50ee074543", "ActionMontage", 0, 0,
            CanonicalRoot + "Actions/ALS_N_LandRoll_F_Montage_Default.ALS_N_LandRoll_F_Montage_Default",
            "2d9341182885d90ad666fff32c025937438b1827", "/Script/Engine.AnimMontage", 1.5f,
            "action_montage", "2d9341182885d90ad666fff32c025937438b1827", "Default", "BaseLayer", -1,
            [Native("roll_montage", NativeRollMontagePath, NativeRollMontageId,
                "b47d81dc91c5e7cacc579268e9c6f97749c6727deb331205a2359483fd75fe37", 1.5f,
                classPath: "/Script/Engine.AnimMontage", observationMode: "RollRuntime",
                montagePath: NativeRollMontagePath, montageStableId: NativeRollMontageId,
                section: "Default", slot: "PostLocomotion")]),
        new("a8059cf630ac747b8e59f902ef26e3361af0b9a7", "ActionSequence", 0, 0,
            CanonicalRoot + "Actions/ALS_N_LandRoll_F.ALS_N_LandRoll_F",
            "39eecd72ffdddb8ba0eb2bb0683f1c958d68fdd9", "/Script/Engine.AnimSequence", 1.5f,
            "action_segment_sequence", "2d9341182885d90ad666fff32c025937438b1827", "", "BaseLayer", 0,
            [Native("roll_sequence", "/ALS/ALS/Animations/Actions/Roll/A_Als_Roll.A_Als_Roll",
                "48e7f555ab9ac4b6aab5af77b7a1a37d5affa099", "03a05babcf094830df7d9ab4bd972595a07ee21269401c564bd466ec706636f4", 1.5f,
                observationMode: "RollRuntime", montagePath: NativeRollMontagePath,
                montageStableId: NativeRollMontageId, slot: "PostLocomotion", segmentIndex: 0)]),
    ];

    private static NativeSourceRow Native(
        string role, string path, string stableId, string packageSha256, float duration,
        bool authoredLoop = false, string classPath = "/Script/Engine.AnimSequence",
        string stance = "", string foot = "", string observationMode = "ReferenceAssetAudit",
        string montagePath = "", string montageStableId = "", string section = "", string slot = "",
        int segmentIndex = -1) =>
        new(role, path, stableId, packageSha256, classPath, duration, authoredLoop, stance, foot,
            observationMode, montagePath, montageStableId, section, slot, segmentIndex);

    private sealed record SourceRow(
        string TraceId, string Kind, int BindingIndex, int GraphSlotIndex, string CanonicalPath,
        string CanonicalStableId, string CanonicalClass, float CanonicalDuration, string CanonicalRole,
        string CanonicalMontageStableId, string CanonicalSection, string CanonicalSlot,
        int CanonicalSegment, NativeSourceRow[] NativeRows);

    private sealed record NativeSourceRow(
        string Role, string Path, string StableId, string PackageSha256, string ClassPath, float Duration,
        bool AuthoredLoop, string Stance, string Foot, string ObservationMode, string MontagePath,
        string MontageStableId, string Section, string Slot, int SegmentIndex);

    private const string CanonicalFootstepClass =
        "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/Footstep_AnimNotify.Footstep_AnimNotify_C";
    private const string CanonicalSetActionClass =
        "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/MovementAction_NotifyState.MovementAction_NotifyState_C";
    private const string CanonicalCameraClass =
        "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/CameraShake_Notify.CameraShake_Notify_C";
    private const string CanonicalGroundedClass =
        "/Game/AdvancedLocomotionV4/Blueprints/AnimNotifys/GroundedEntryState_AnimNotify.GroundedEntryState_AnimNotify_C";
    private const string NativeFootstepClass = "/Script/ALS.AlsAnimNotify_FootstepEffects";
    private const string NativeCameraClass = "/Script/ALSCamera.AlsAnimNotify_CameraShake";
    private const string NativeGroundedClass = "/Script/ALS.AlsAnimNotify_SetGroundedEntryMode";
    private const string NativeRootMotionClass = "/Script/ALS.AlsAnimNotifyState_SetRootMotionScale";
    private const string NativeSetActionClass = "/Script/ALS.AlsAnimNotifyState_SetLocomotionAction";

    private static readonly CanonicalEventRow[] CanonicalEventRows =
    [
        new("734810bbf844f13819f0bf49b4ada26dfbd4fc80", 1, "6b0eed3e66fba6bc812321f8e02a1f58b08738e9", CanonicalFootstepClass, 0, 0, .101842955f, 0f, .3f,
            [NE("base_walk_forward", "c5dad3f4ec028a3beb88e2fda3ea1bb5b4c8b332", 0, 0, .13333333f, 0f, .3f, NativeFootstepClass)]),
        new("b345740d1e77a3ee77c20f227a7e8f49145f96a2", 1, "e4d46082dcdc90ac224a2a18afcfe5a38384c66f", CanonicalFootstepClass, 1, 0, .6683444f, 0f, .3f,
            [NE("base_walk_forward", "a2ca4d47d8c9ee757571936aea1d33fb38929df7", 1, 0, .6999999f, 0f, .3f, NativeFootstepClass)]),
        new("6a1e7011651ab36a9e7f87612e1b39797c15130b", 3, "5e970273249f9051eb45219fa26eeecc3d73cd9a", CanonicalFootstepClass, 0, 0, .799321f, 0f, .5f,
            [NE("turn_90_left", "f2bb9fa95bbd3d7c7d86e8752a650fcc7b3bfae3", 0, 0, .8333334f, 0f, .5f, NativeFootstepClass)]),
        new("0debe19ecbae42141b7a3c03c102dc1ace6ffb7e", 4, "25ab58fa404e0fceb6e79e5158ded2f831db0116", CanonicalFootstepClass, 0, 0, .467796f, 0f, .5f,
            [NE("rotate_90_left", "7cc872887c48c8269951367d560a15b6e678ab76", 0, 0, .5f, 0f, .5f, NativeFootstepClass)]),
        new("71e895f1fd7bb0fefd615f8e40d98aa1a5e8de4a", 5, "937a094c72cc4e3e45644452ca6f5166c76e76db", CanonicalFootstepClass, 0, 0, .795465f, 0f, .3f,
            [
                NE("transition_standing_left", "415fe2961bf706a66fd06db174756b4c5efe9208", 0, 0, .56666666f, 0f, .3f, NativeFootstepClass),
                NE("transition_crouching_left", "54b6bd5b089876fc02669f2fb5361d2c6c24e531", 0, 0, .56666666f, 0f, .3f, NativeFootstepClass),
            ]),
        new("844976c07a47eb7eaed41cec50a1f921130c3673", 6, "a4076e21e4eeaeb5a3cd99718ae0dbd60ca5ad9f", CanonicalFootstepClass, 0, 0, .795465f, 0f, .3f,
            [
                NE("transition_standing_right", "a3a8e215cbe8919afab535ebbfb06340a7b21e8e", 0, 0, .56666666f, 0f, .3f, NativeFootstepClass),
                NE("transition_crouching_right", "6a1b11f5682711a908597a33509e02f0167fa909", 0, 0, .56666666f, 0f, .3f, NativeFootstepClass),
            ]),
        new("8062fba055f76ff07c58dffe36c0563ea5bf51ea", 7, "5ee1e002145bfc613cfec77e149cf75a97f6b4f0", CanonicalSetActionClass, 0, 0, 0f, .9301274f, .00001f,
            [NE("roll_montage", "41657bc10ac071b86b2c1850731bbaede48f4f2d", 0, 0, 0f, .933333f, .00001f, NativeSetActionClass, "MontageTimeline")]),
        new("afed3389e2d5f65bf91c579c75c08a126a6f276e", 8, "5d07b72e83047a186a0af21b05a76cf4ca490ae3", CanonicalCameraClass, 0, 0, .10099864f, 0f, .00001f,
            [NE("roll_sequence", "8202690d6463690df1e4f19846374922e3964db0", 1, 3, .10000001f, 0f, .00001f, NativeCameraClass)]),
        new("f63b1438420894ceb2d2c3201fad05c7657014ab", 8, "21729d9217a560f88040478c5ddb2f5ff996c0ba", CanonicalCameraClass, 1, 0, .48002723f, 0f, .00001f,
            [NE("roll_sequence", "7fe31d512c9690fcfd1bf127fc0f857a219dea98", 2, 3, .4666667f, 0f, .00001f, NativeCameraClass)]),
        new("09489db3147f4f85b4a7931afd558931ff266bd7", 8, "a51efb0320e94bb1707b25d67f8656a4706a54ff", CanonicalGroundedClass, 2, 0, .9311393f, 0f, .00001f,
            [NE("roll_sequence", "bc33ac54ab9481ffa69731b8955e784cbdba071b", 3, 0, .93333334f, 0f, .00001f, NativeGroundedClass)]),
    ];

    private static readonly NativeEventRow[] NativeOnlyEventRows =
    [
        NE("turn_90_left", "aafc6f2d00242d6fa06ca332075cec3bb444c7fc", 1, 0, 1.3666668f, 0f, .5f, NativeFootstepClass),
        NE("turn_90_left", "574d99fc0524fed678b1af919f30fc53a1f178ad", 2, 0, 1.7666668f, 0f, .5f, NativeFootstepClass),
        NE("rotate_90_left", "ce958252e8635c6522263e10d2f0ecd4406c7ff3", 1, 0, .9666667f, 0f, .5f, NativeFootstepClass),
        NE("roll_sequence", "8f256f59781aa29210fa78e4c9e7544d5e2cf3a6", 0, 1, 0f, 1.5f, .00001f, NativeRootMotionClass),
        NE("roll_sequence", "dce6031f506021987030efef5842024ebaa6128c", 4, 2, .9333334f, 0f, .5f, NativeFootstepClass),
        NE("roll_sequence", "8a294b5d1db7ee6010d909f072f8938459ece2a7", 5, 2, 1f, 0f, .5f, NativeFootstepClass),
        NE("roll_sequence", "1db6fcf32766e082f2541ff458c7ac4ab56bef0f", 6, 2, 1.2333333f, 0f, .5f, NativeFootstepClass),
    ];

    private static NativeEventRow NE(
        string role, string stableEventId, int sourceIndex, int trackIndex, float time, float duration,
        float threshold, string classPath, string owner = "SequenceTimeline") =>
        new(role, stableEventId, owner, classPath, sourceIndex, trackIndex, time, duration, threshold);

    private sealed record CanonicalEventRow(
        string TraceEventId, int Source, string StableEventId, string ClassPath, int SourceIndex,
        int TrackIndex, float Time, float Duration, float Threshold, NativeEventRow[] NativeRows);

    private sealed record NativeEventRow(
        string Role, string StableEventId, string Owner, string ClassPath, int SourceIndex,
        int TrackIndex, float Time, float Duration, float Threshold);

    private static JsonObject Source(int index)
    {
        var row = SourceRows[index];
        return new JsonObject
        {
            ["traceSourceId"] = row.TraceId,
            ["layoutKey"] = new JsonObject
            {
                ["sourceKind"] = row.Kind,
                ["sourceBindingIndex"] = row.BindingIndex,
                ["graphSlotIndex"] = row.GraphSlotIndex,
            },
            ["canonicalEvidence"] = new JsonObject
            {
                ["assetObjectPath"] = row.CanonicalPath,
                ["assetStableId"] = row.CanonicalStableId,
                ["assetClassPath"] = row.CanonicalClass,
                ["durationSeconds"] = row.CanonicalDuration,
                ["authoredLoop"] = false,
                ["canonicalRole"] = row.CanonicalRole,
                ["montageStableId"] = row.CanonicalMontageStableId,
                ["sectionName"] = row.CanonicalSection,
                ["slotName"] = row.CanonicalSlot,
                ["segmentIndex"] = row.CanonicalSegment,
            },
            ["nativeVariants"] = Repeat(row.NativeRows.Length, variant => NativeVariant(row.NativeRows[variant])),
        };
    }

    private static JsonObject EventMapRow(int index)
    {
        var row = CanonicalEventRows[index];
        return new JsonObject
        {
            ["traceEventId"] = row.TraceEventId,
            ["traceSourceId"] = SourceRows[row.Source].TraceId,
            ["canonicalEvidence"] = EventEvidence(row),
            ["nativeVariants"] = Repeat(row.NativeRows.Length, variant => NativeEventEvidence(row.NativeRows[variant])),
            ["hostResolution"] = new JsonObject { ["eventId"] = index },
        };
    }

    private static JsonObject MarkerMapRow(int index) => new()
    {
        ["traceSourceId"] = SourceRows[1].TraceId,
        ["canonicalEvidence"] = MarkerEvidence(index, canonical: true),
        ["nativeVariants"] = Repeat(1, _ => new JsonObject
        {
            ["nativeRole"] = "base_walk_forward",
            ["assetStableId"] = SourceRows[1].NativeRows[0].StableId,
            ["stableMarkerId"] = index == 0
                ? "18d5d915c4380d13c1d2e2dcaf93232663e8cf9f"
                : "2feb7607937e7020c3b6c3e4dd386a78edbd0577",
            ["name"] = index == 0 ? "Left" : "Right",
            ["sourceIndex"] = index,
            ["trackIndex"] = 1,
            ["timeSeconds"] = index == 0 ? .083333336f : .65000004f,
        }),
        ["hostResolution"] = new JsonObject { ["markerId"] = index },
    };

    private static JsonObject SectionMapRow(int index) => new()
    {
        ["actionTraceSourceId"] = SourceRows[7].TraceId,
        ["canonicalSectionName"] = "Default",
        ["nativeVariants"] = Repeat(1, _ => new JsonObject
        {
            ["nativeRole"] = "roll_montage",
            ["montageStableId"] = NativeRollMontageId,
            ["sectionName"] = "Default",
            ["sectionIndex"] = 0,
        }),
        ["hostResolution"] = new JsonObject { ["sectionId"] = index },
    };

    private static JsonObject NativeOnlyEventMapRow(int index)
    {
        var evidenceRow = NativeOnlyEventRows[index];
        var evidence = NativeEventEvidence(evidenceRow);
        return new JsonObject
        {
            ["nativeRole"] = evidenceRow.Role,
            ["evidence"] = evidence,
            ["deferredOwner"] = index == 3 ? "RootMotion" : "FootstepAudioVfx",
        };
    }

    private static JsonObject NativeVariant(NativeSourceRow row)
    {
        return new JsonObject
        {
            ["nativeRole"] = row.Role,
            ["assetObjectPath"] = row.Path,
            ["assetStableId"] = row.StableId,
            ["assetPackageSha256"] = row.PackageSha256,
            ["assetClassPath"] = row.ClassPath,
            ["durationSeconds"] = row.Duration,
            ["authoredLoop"] = row.AuthoredLoop,
            ["stance"] = row.Stance,
            ["foot"] = row.Foot,
            ["observationMode"] = row.ObservationMode,
            ["montageObjectPath"] = row.MontagePath,
            ["montageStableId"] = row.MontageStableId,
            ["sectionName"] = row.Section,
            ["slotName"] = row.Slot,
            ["segmentIndex"] = row.SegmentIndex,
        };
    }

    private static JsonObject EventEvidence(CanonicalEventRow row) => new()
    {
        ["assetStableId"] = SourceRows[row.Source].CanonicalStableId,
        ["stableEventId"] = row.StableEventId,
        ["ownerKind"] = row.Source == 7 ? "MontageTimeline" : "SequenceTimeline",
        ["sourceClassPath"] = row.ClassPath,
        ["sourceIndex"] = row.SourceIndex,
        ["trackIndex"] = row.TrackIndex,
        ["boundaryOrdinal"] = row.Source == 8 ? 1 : 0,
        ["kind"] = row.Source == 7 ? "SetAction" : "Generic",
        ["tickMode"] = "Queued",
        ["timeSeconds"] = row.Time,
        ["durationSeconds"] = row.Duration,
        ["triggerWeightThreshold"] = row.Threshold,
        ["payload"] = row.Source == 7 ? Payload(2, 1) : Payload(),
    };

    private static JsonObject NativeEventEvidence(NativeEventRow row) => new()
    {
        ["nativeRole"] = row.Role,
        ["assetStableId"] = NativeByRole(row.Role).StableId,
        ["stableEventId"] = row.StableEventId,
        ["ownerKind"] = row.Owner,
        ["sourceClassPath"] = row.ClassPath,
        ["sourceIndex"] = row.SourceIndex,
        ["trackIndex"] = row.TrackIndex,
        ["timeSeconds"] = row.Time,
        ["durationSeconds"] = row.Duration,
        ["triggerWeightThreshold"] = row.Threshold,
        ["tickMode"] = "Queued",
    };

    private static JsonObject MarkerEvidence(int index, bool canonical) => new()
    {
        ["assetStableId"] = canonical ? SourceRows[1].CanonicalStableId : SourceRows[1].NativeRows[0].StableId,
        ["stableMarkerId"] = canonical
            ? index == 0 ? "80504f4e05b23fbc3a6195ef274e5438b4ffad30" : "da02470bed0f2bfe67e6b8c76b361542f61b76d8"
            : index == 0 ? "18d5d915c4380d13c1d2e2dcaf93232663e8cf9f" : "2feb7607937e7020c3b6c3e4dd386a78edbd0577",
        ["name"] = index == 0 ? "Left" : "Right",
        ["sourceIndex"] = index,
        ["trackIndex"] = 1,
        ["timeSeconds"] = canonical
            ? index == 0 ? .10176502f : .6670235f
            : index == 0 ? .083333336f : .65000004f,
    };

    private static JsonObject Payload(int semanticId = 0, int enumValue0 = 0) => new()
    {
        ["semanticId"] = semanticId,
        ["enumValue0"] = enumValue0,
        ["enumValue1"] = 0,
        ["enumValue2"] = 0,
        ["scalarValue0"] = 0f,
        ["flags"] = 0,
        ["terminationReason"] = "None",
    };

    private static NativeSourceRow NativeByRole(string role) =>
        SourceRows.SelectMany(row => row.NativeRows).Single(row => row.Role == role);

    private static JsonArray Cases(Func<int, int, int, JsonObject> frameFactory)
    {
        var cases = new JsonArray();
        var absoluteFrame = 0;
        for (var caseIndex = 0; caseIndex < CaseFrameCounts.Length; caseIndex++)
        {
            var frames = new JsonArray();
            for (var localFrame = 0; localFrame < CaseFrameCounts[caseIndex]; localFrame++)
            {
                frames.Add(frameFactory(absoluteFrame++, localFrame, caseIndex));
            }
            cases.Add(new JsonObject
            {
                ["ordinal"] = caseIndex,
                ["caseId"] = CaseIds[caseIndex],
                ["frames"] = frames,
            });
        }
        return cases;
    }

    private static JsonObject PlanFrame(int frameIndex, int localFrame, int caseIndex)
    {
        const double delta = 0.01666666753590107d;
        var baseSource = caseIndex switch
        {
            0 or 1 => 1,
            4 => 2,
            _ => 0,
        };
        var turn = caseIndex == 1
            ? new JsonArray(Descriptor(3, localFrame, CaseFrameCounts[caseIndex], authorityTie: true))
            : new JsonArray();
        var rotate = caseIndex == 1
            ? new JsonArray(Descriptor(4, localFrame, CaseFrameCounts[caseIndex], authorityTie: true))
            : new JsonArray();
        var stance = caseIndex == 4 ? "Crouching" : "Standing";
        return new JsonObject
        {
            ["frameIndex"] = localFrame,
            ["input"] = new JsonObject
            {
                ["identity"] = new JsonObject
                {
                    ["frameId"] = ((caseIndex + 1) * 1000 + localFrame).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["characterId"] = 1000 + caseIndex,
                    ["slotGeneration"] = 1,
                },
                ["window"] = new JsonObject
                {
                    ["startSeconds"] = localFrame * delta,
                    ["endSeconds"] = (localFrame + 1) * delta,
                    ["deltaSeconds"] = 0.016666668f,
                },
                ["modes"] = new JsonObject
                {
                    ["locomotionMode"] = "Grounded",
                    ["rotationMode"] = "LookingDirection",
                    ["stance"] = stance,
                    ["hasInput"] = false,
                },
                ["p4Curves"] = new JsonObject
                {
                    ["animationState"] = "Grounded",
                    ["actionBlendAmount"] = caseIndex == 1 ? 0.5f : 0f,
                    ["actionModeBlendAmount"] = caseIndex == 1 ? 0.5f : 0f,
                    ["base"] = new JsonArray(Descriptor(baseSource, localFrame, CaseFrameCounts[caseIndex], caseIndex == 1)),
                    ["turnBanks"] = turn,
                    ["rotateBanks"] = rotate,
                },
                ["actionRequest"] = ActionRequest(caseIndex, localFrame),
                ["cancelActionForRuntimeFailure"] = false,
                ["transitionProbe"] = TransitionProbe(caseIndex, localFrame),
            },
        };
    }

    private static JsonObject Descriptor(int sourceIndex, int frame, int frameCount, bool authorityTie)
    {
        const double delta = 0.01666666753590107d;
        var duration = sourceIndex switch
        {
            0 or 2 => 0.033333335f,
            1 => 1.1333333f,
            3 => 2f,
            4 => 1f,
            _ => 1.5f,
        };
        var previous = (double)Q(frame);
        var current = (double)Q(frame + 1);
        var frameEndOffset = delta;
        if (authorityTie)
        {
            var eventTime = CanonicalEventRows.First(row => row.Source == sourceIndex).Time;
            var p = (double)eventTime - delta / 2d;
            previous = frame <= 1 ? p : p + delta;
            current = frame == 0 ? p : p + delta;
            frameEndOffset = frame == 1 ? delta : 0d;
        }
        return new JsonObject
        {
            ["traceSourceId"] = SourceRows[sourceIndex].TraceId,
            ["playbackEpoch"] = "1",
            ["previousUnwrappedTimeSeconds"] = previous,
            ["currentUnwrappedTimeSeconds"] = current,
            ["frameStartOffsetSeconds"] = 0d,
            ["frameEndOffsetSeconds"] = frameEndOffset,
            ["durationSeconds"] = duration,
            ["weight"] = sourceIndex is 3 or 4 ? 2f : 1f,
            ["loop"] = sourceIndex <= 2,
            ["activatesAtFrameStart"] = frame == 0,
            ["closesAfterFrame"] = frame == frameCount - 1,
        };
    }

    private static float Q(int count)
    {
        const float delta = 0.016666668f;
        var value = 0f;
        for (var index = 0; index < count; index++)
        {
            value = (float)(value + delta);
        }
        return value;
    }

    private static JsonObject ActionRequest(int caseIndex, int frame)
    {
        if (caseIndex >= 5 && frame == 0)
        {
            return new JsonObject
            {
                ["command"] = "Start", ["requestId"] = "1", ["actionTraceSourceId"] = SourceRows[7].TraceId,
                ["startSectionName"] = "Default", ["priority"] = 100, ["slotGeneration"] = 1,
            };
        }
        if (caseIndex == 6 && frame == 56)
        {
            return new JsonObject
            {
                ["command"] = "Cancel", ["requestId"] = "1",
                ["actionTraceSourceId"] = SourceRows[7].TraceId,
                ["startSectionName"] = string.Empty, ["priority"] = 0, ["slotGeneration"] = 1,
            };
        }
        return new JsonObject
        {
            ["command"] = "None", ["requestId"] = "-1", ["actionTraceSourceId"] = string.Empty,
            ["startSectionName"] = string.Empty, ["priority"] = 0, ["slotGeneration"] = 0,
        };
    }

    private static JsonObject TransitionProbe(int caseIndex, int frame)
    {
        var leftRelevant = frame == 0 && caseIndex is 2 or 3 or 4;
        var rightRelevant = frame == 0 && caseIndex is 2 or 3;
        var rightDistance = caseIndex == 3 ? 0.1f : 0.09f;
        return new JsonObject
        {
            ["left"] = Probe(leftRelevant ? 0.09f : 0f, leftRelevant),
            ["right"] = Probe(rightRelevant ? rightDistance : 0f, rightRelevant),
        };
    }

    private static JsonObject Probe(float x, bool relevant) => new()
    {
        ["targetMeters"] = new JsonObject { ["x"] = x, ["y"] = 0f, ["z"] = 0f },
        ["lockMeters"] = new JsonObject { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f },
        ["relevant"] = relevant,
    };

    private static JsonObject RawFrame(int frameIndex, int caseIndex) => new()
    {
        ["frameIndex"] = frameIndex,
        ["nativeActual"] = NativeActual(caseIndex, frameIndex),
    };

    private static JsonObject CanonicalFrame(int frameIndex, int caseIndex, bool includePortAudit)
    {
        var frame = new JsonObject
        {
            ["frameIndex"] = frameIndex,
            ["identity"] = new JsonObject
            {
                ["frameId"] = ((caseIndex + 1) * 1000 + frameIndex).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["characterId"] = 1000 + caseIndex,
                ["slotGeneration"] = 1,
            },
            ["comparableActual"] = ComparableActual(caseIndex, frameIndex),
        };
        if (includePortAudit)
        {
            // Schema/export seed only. Production portAudit truth is reconstructed by P5aPortAuditReplay.
            frame["portAudit"] = PortSchemaSeedAudit(caseIndex, frameIndex);
        }
        return frame;
    }

    private static JsonObject NativeReferenceAudit() => new()
    {
        ["assets"] = Repeat(11, NativeAssetAudit),
        ["events"] = Repeat(19, NativeEventAudit),
        ["markers"] = Repeat(2, NativeMarkerAudit),
        ["curveInventories"] = Repeat(5, NativeCurveInventory),
    };

    private static JsonObject NativeActual(int caseIndex, int frameIndex)
    {
        var actual = NativeActualDefault();
        PopulateRawSchedule(actual, caseIndex, frameIndex);
        return actual;
    }

    private static JsonObject NativeActualDefault() => new()
    {
        ["canonicalAssetOracle"] = new JsonObject
        {
            ["curves"] = Curves(),
            ["graphCurveWeights"] = new JsonObject { ["action"] = 0f, ["transition"] = 0f },
            ["sync"] = new JsonObject
            {
                ["active"] = false,
                ["leader"] = ObservedCanonicalSource(),
                ["nativeInstanceOrdinal"] = "0",
                ["previousMarker"] = ObservedMarker(),
                ["nextMarker"] = ObservedMarker(),
                ["cycle"] = "0",
                ["phase"] = 0f,
                ["leftFootPhase"] = 0f,
                ["rightFootPhase"] = 0f,
            },
            ["events"] = new JsonArray(),
            ["activeNotifyStates"] = new JsonArray(),
        },
        ["animGraphCurveAudit"] = new JsonObject
        {
            ["leftIk"] = CurveAudit(), ["rightIk"] = CurveAudit(),
            ["leftLock"] = CurveAudit(), ["rightLock"] = CurveAudit(),
            ["allowTransitions"] = CurveAudit(),
        },
        ["transitionStimulusReceipts"] = new JsonArray(),
        ["dynamicTransition"] = new JsonObject
        {
            ["active"] = false, ["source"] = ObservedNativeSource(), ["nativeInstanceOrdinal"] = "0",
            ["foot"] = "Left", ["activatedAfterUpdate"] = false,
            ["previousTimeSeconds"] = 0f, ["currentTimeSeconds"] = 0f, ["playRate"] = 0f,
            ["observedBlendInSeconds"] = 0f, ["observedBlendOutSeconds"] = 0f,
            ["observedEffectiveWeight"] = 0f,
        },
        ["actionPlayback"] = new JsonObject
        {
            ["status"] = "Inactive", ["montageSource"] = ObservedNativeSource(),
            ["segmentSource"] = ObservedNativeSource(), ["nativeInstanceOrdinal"] = "0",
            ["currentSectionName"] = string.Empty, ["segmentIndex"] = -1,
            ["previousMontageTimeSeconds"] = 0f, ["currentMontageTimeSeconds"] = 0f,
            ["previousClipTimeSeconds"] = 0f, ["currentClipTimeSeconds"] = 0f,
            ["finalSegmentDeltaSeconds"] = 0f, ["playRate"] = 0f,
        },
        ["actionVisualContribution"] = new JsonObject
        {
            ["contributing"] = false, ["montageSource"] = ObservedNativeSource(),
            ["nativeInstanceOrdinal"] = "0", ["observedMontageTimeSeconds"] = 0f,
            ["observedBlendInSeconds"] = 0f, ["observedBlendOutSeconds"] = 0f,
            ["observedBlendInOption"] = -1, ["observedBlendOutOption"] = -1,
            ["observedEffectiveWeight"] = 0f,
        },
        ["nativeRuntimeTimeline"] = new JsonArray(),
        ["actionOutcomes"] = new JsonArray(),
        ["stateAfter"] = new JsonObject
        {
            ["actionPlaying"] = false, ["actionSource"] = ObservedNativeSource(),
            ["actionNativeInstanceOrdinal"] = "0", ["actionTimeSeconds"] = 0f,
            ["transitionPlaying"] = false, ["transitionSource"] = ObservedNativeSource(),
            ["transitionNativeInstanceOrdinal"] = "0", ["transitionTimeSeconds"] = 0f,
            ["transitionCooldownFrames"] = 0, ["transitionFoot"] = "Left",
        },
    };

    private static JsonObject ComparableActual(int caseIndex, int frameIndex)
    {
        var actual = ComparableActualDefault();
        PopulateComparableSchedule(actual, caseIndex, frameIndex);
        return actual;
    }

    private static JsonObject ComparableActualDefault() => new()
    {
        ["curves"] = Curves(),
        ["sync"] = new JsonObject
        {
            ["active"] = false, ["leaderTraceSourceId"] = string.Empty, ["activationOrdinal"] = "0",
            ["previousMarkerStableId"] = string.Empty, ["nextMarkerStableId"] = string.Empty,
            ["cycle"] = "0", ["phase"] = 0f, ["leftFootPhase"] = 0f, ["rightFootPhase"] = 0f,
        },
        ["dynamicTransition"] = new JsonObject
        {
            ["active"] = false, ["traceSourceId"] = string.Empty, ["activationOrdinal"] = "0",
            ["foot"] = "Left", ["previousTimeSeconds"] = 0f, ["currentTimeSeconds"] = 0f,
            ["playRate"] = 0f,
        },
        ["actionPlayback"] = new JsonObject
        {
            ["active"] = false, ["montageTraceSourceId"] = string.Empty,
            ["segmentTraceSourceId"] = string.Empty, ["activationOrdinal"] = "0",
            ["currentSectionName"] = string.Empty, ["segmentIndex"] = -1,
            ["previousMontageTimeSeconds"] = 0f, ["currentMontageTimeSeconds"] = 0f,
            ["previousClipTimeSeconds"] = 0f, ["currentClipTimeSeconds"] = 0f,
            ["finalSegmentDeltaSeconds"] = 0f, ["playRate"] = 0f,
        },
        ["events"] = new JsonArray(),
        ["actionOutcomes"] = new JsonArray(),
        ["stateAfter"] = new JsonObject
        {
            ["actionPlaying"] = false, ["actionTraceSourceId"] = string.Empty,
            ["actionActivationOrdinal"] = "0", ["actionTimeSeconds"] = 0f,
            ["transitionPlaying"] = false, ["transitionTraceSourceId"] = string.Empty,
            ["transitionActivationOrdinal"] = "0", ["transitionTimeSeconds"] = 0f,
            ["transitionCooldownFrames"] = 0, ["transitionFoot"] = "Left",
            ["activeNotifyStates"] = new JsonArray(),
        },
    };

    private static JsonObject PortSchemaSeedAudit(int caseIndex, int frameIndex)
    {
        var audit = PortAuditDefault();
        PopulatePortSchemaSeed(audit, caseIndex, frameIndex);
        return audit;
    }

    private static JsonObject PortAuditDefault() => new()
    {
        ["prepared"] = new JsonObject
        {
            ["actionGraph"] = LaneGraph(), ["transitionGraph"] = LaneGraph(),
            ["syncMappings"] = new JsonArray(), ["leftIk"] = 0f, ["rightIk"] = 0f,
            ["leftLock"] = 0f, ["rightLock"] = 0f, ["allowTransitions"] = 0f,
            ["transitionReplacedClosingWeight"] = 0f,
        },
        ["result"] = new JsonObject
        {
            ["p4CurvePassthrough"] = new JsonObject
            {
                ["leftIk"] = 0f, ["rightIk"] = 0f, ["leftLock"] = 0f, ["rightLock"] = 0f,
            },
            ["sync"] = CoreSync(), ["dynamicTransition"] = CoreTransition(),
            ["actionPlayback"] = CoreAction(), ["events"] = new JsonArray(),
            ["actionOutcomes"] = new JsonArray(), ["p5FailureCode"] = "None",
        },
        ["stateAfter"] = new JsonObject
        {
            ["actionPlayer"] = ActionPlayerState(), ["dynamicTransition"] = TransitionState(),
            ["actionBlendLane"] = LaneState(), ["dynamicTransitionBlendLane"] = LaneState(),
            ["timelineCursors"] = Repeat(37, _ => TimelineCursor()),
            ["authorities"] = Repeat(4, AuthorityState),
            ["notifyOwnership"] = Repeat(16, _ => NotifyOwnership()),
            ["nextOwnerToken"] = "0000000000000001",
        },
    };

    private static void PopulateComparableSchedule(JsonObject actual, int caseIndex, int frameIndex)
    {
        actual["curves"] = SemanticCurves(caseIndex, frameIndex);
        var events = actual["events"]!.AsArray();
        var outcomes = actual["actionOutcomes"]!.AsArray();
        var state = actual["stateAfter"]!.AsObject();

        if (caseIndex is 0 or 1)
        {
            PopulateSharedSync(actual["sync"]!.AsObject(), caseIndex, frameIndex);
            if (caseIndex == 0 && frameIndex == 6)
            {
                events.Add(SharedEvent(0, "Trigger", 0, .0018429533f));
            }
            if (caseIndex == 0 && frameIndex == 40)
            {
                events.Add(SharedEvent(1, "Trigger", 0, .0016776919f));
            }
            if (caseIndex == 1 && frameIndex == 1)
            {
                events.Add(SharedEvent(3, "Trigger", 0, .008333334f));
            }
        }

        if (caseIndex is 2 or 3 or 4)
        {
            PopulateSharedTransition(actual, state, caseIndex, frameIndex);
            if (frameIndex == 32)
            {
                events.Add(SharedEvent(caseIndex == 3 ? 5 : 4, "Trigger", 0, .013643463f));
            }
        }

        if (caseIndex >= 5)
        {
            PopulateSharedAction(actual, state, events, outcomes, caseIndex, frameIndex);
        }
    }

    private static JsonObject SemanticCurves(int caseIndex, int frameIndex) => new()
    {
        ["leftIk"] = 1f,
        ["rightIk"] = 1f,
        ["leftLock"] = caseIndex == 1 && frameIndex == 0 ? 0.00015993712f : 0f,
        ["rightLock"] = caseIndex == 1 ? 1f : 0f,
        ["allowTransitions"] = caseIndex is 2 or 3 or 4
            ? MathF.Max(0f, 1f - TransitionGraphWeight(frameIndex))
            : 1f,
    };

    private static void PopulateSharedSync(JsonObject sync, int caseIndex, int frameIndex)
    {
        sync["active"] = true;
        sync["leaderTraceSourceId"] = SourceRows[1].TraceId;
        sync["activationOrdinal"] = "1";
        sync["previousMarkerStableId"] = frameIndex <= 6
            ? "da02470bed0f2bfe67e6b8c76b361542f61b76d8"
            : "80504f4e05b23fbc3a6195ef274e5438b4ffad30";
        sync["nextMarkerStableId"] = frameIndex <= 6
            ? "80504f4e05b23fbc3a6195ef274e5438b4ffad30"
            : "da02470bed0f2bfe67e6b8c76b361542f61b76d8";
        sync["cycle"] = "0";
        if (caseIndex == 0 && frameIndex == 40)
        {
            sync["phase"] = .028710753f;
            sync["leftFootPhase"] = .9712893f;
            sync["rightFootPhase"] = .028710753f;
        }
    }

    private static void PopulateSharedTransition(JsonObject actual, JsonObject state, int caseIndex, int frameIndex)
    {
        state["transitionCooldownFrames"] = frameIndex == 0 ? 2 : frameIndex == 1 ? 1 : 0;
        state["transitionFoot"] = caseIndex == 3 ? "Right" : "Left";
        if (frameIndex > 0)
        {
            var sourceIndex = caseIndex == 3 ? 6 : 5;
            var transition = actual["dynamicTransition"]!.AsObject();
            transition["active"] = true;
            transition["traceSourceId"] = SourceRows[sourceIndex].TraceId;
            transition["activationOrdinal"] = "1";
            transition["foot"] = caseIndex == 3 ? "Right" : "Left";
            transition["previousTimeSeconds"] = T(frameIndex - 1);
            transition["currentTimeSeconds"] = T(frameIndex);
            transition["playRate"] = 1.5f;

            state["transitionPlaying"] = true;
            state["transitionTraceSourceId"] = SourceRows[sourceIndex].TraceId;
            state["transitionActivationOrdinal"] = "1";
            state["transitionTimeSeconds"] = T(frameIndex);
        }
    }

    private static float T(int count)
    {
        const float step = 0.016666668f * 1.5f;
        var value = 0f;
        for (var index = 0; index < count; index++)
        {
            value = (float)(value + step);
        }
        return value;
    }

    private static void PopulateSharedAction(
        JsonObject actual,
        JsonObject state,
        JsonArray events,
        JsonArray outcomes,
        int caseIndex,
        int frameIndex)
    {
        var cancelFrame = caseIndex == 6 ? 56 : int.MaxValue;
        var finishFrame = caseIndex == 5 ? 91 : int.MaxValue;
        var playbackActive = frameIndex > 0 && frameIndex <= global::System.Math.Min(cancelFrame, finishFrame);
        if (playbackActive)
        {
            var previous = Q(frameIndex - 1);
            var current = frameIndex == 91 ? 1.5f : Q(frameIndex);
            if (caseIndex == 6 && frameIndex == 56)
            {
                previous = Q(55);
                current = previous;
            }
            var playback = actual["actionPlayback"]!.AsObject();
            playback["active"] = true;
            playback["montageTraceSourceId"] = SourceRows[7].TraceId;
            playback["segmentTraceSourceId"] = SourceRows[8].TraceId;
            playback["activationOrdinal"] = "1";
            playback["currentSectionName"] = "Default";
            playback["segmentIndex"] = 0;
            playback["previousMontageTimeSeconds"] = previous;
            playback["currentMontageTimeSeconds"] = current;
            playback["previousClipTimeSeconds"] = previous;
            playback["currentClipTimeSeconds"] = current;
            playback["finalSegmentDeltaSeconds"] = frameIndex == 91 ? 7.1525574e-7f : 0f;
            playback["playRate"] = 1f;
        }

        if (frameIndex == 0)
        {
            outcomes.Add(SharedOutcome("Accepted"));
        }
        if (caseIndex == 6 && frameIndex == 56)
        {
            events.Add(SharedEvent(6, "End", 0, 0f, "InterruptedByExplicitCancel"));
            outcomes.Add(SharedOutcome("InterruptedByExplicitCancel"));
        }
        else if (caseIndex == 5 && frameIndex == 91)
        {
            outcomes.Add(SharedOutcome("Completed"));
        }
        else
        {
            AddNaturalActionEvents(events, caseIndex, frameIndex);
        }

        var actionPlaying = caseIndex switch
        {
            5 => frameIndex < 91,
            6 => frameIndex < 56,
            _ => true,
        };
        state["actionPlaying"] = actionPlaying;
        state["actionTraceSourceId"] = actionPlaying ? SourceRows[7].TraceId : string.Empty;
        state["actionActivationOrdinal"] = actionPlaying ? "1" : "0";
        state["actionTimeSeconds"] = actionPlaying ? Q(frameIndex) : 0f;
        if (frameIndex is >= 1 and <= 55)
        {
            state["activeNotifyStates"]!.AsArray().Add(SharedOwner(6));
        }
    }

    private static void AddNaturalActionEvents(JsonArray events, int caseIndex, int frameIndex)
    {
        var ordinal = 0;
        if (frameIndex == 7)
        {
            events.Add(SharedEvent(7, "Trigger", ordinal++, .0009986386f));
        }
        if (frameIndex == 29)
        {
            events.Add(SharedEvent(8, "Trigger", ordinal++, .013360381f));
        }
        if (frameIndex is >= 1 and <= 55)
        {
            events.Add(SharedEvent(6, "Tick", ordinal, .016666668f));
        }
        if (frameIndex == 56 && caseIndex is 5 or 7)
        {
            events.Add(SharedEvent(9, "Trigger", 0, .014472842f));
        }
    }

    private static JsonObject SharedEvent(
        int eventIndex,
        string phase,
        int ordinal,
        float offset,
        string terminationReason = "None")
    {
        var row = CanonicalEventRows[eventIndex];
        var payload = row.Source == 7 ? Payload(2, 1) : Payload();
        payload["terminationReason"] = terminationReason;
        return new JsonObject
        {
            ["traceEventId"] = row.TraceEventId,
            ["traceSourceId"] = SourceRows[row.Source].TraceId,
            ["activationOrdinal"] = "1", ["playbackCycle"] = "0",
            ["frameEventOrdinal"] = ordinal,
            ["boundaryOrdinal"] = row.Source == 8 ? 1 : 0,
            ["frameOffsetSeconds"] = offset,
            ["kind"] = row.Source == 7 ? "SetAction" : "Generic",
            ["phase"] = phase, ["payload"] = payload,
        };
    }

    private static JsonObject SharedOutcome(string resultCode) => new()
    {
        ["actionTraceSourceId"] = SourceRows[7].TraceId,
        ["activationOrdinal"] = "1",
        ["resultCode"] = resultCode,
    };

    private static JsonObject SharedOwner(int eventIndex) => new()
    {
        ["traceEventId"] = CanonicalEventRows[eventIndex].TraceEventId,
        ["traceSourceId"] = SourceRows[CanonicalEventRows[eventIndex].Source].TraceId,
        ["activationOrdinal"] = "1", ["playbackCycle"] = "0",
    };

    private static void PopulateRawSchedule(JsonObject actual, int caseIndex, int frameIndex)
    {
        var oracle = actual["canonicalAssetOracle"]!.AsObject();
        oracle["curves"] = SemanticCurves(caseIndex, frameIndex);
        var graphWeights = oracle["graphCurveWeights"]!.AsObject();
        graphWeights["action"] = caseIndex >= 5 ? ActionLaneWeight(caseIndex, frameIndex) : 0f;
        graphWeights["transition"] = caseIndex is 2 or 3 or 4 ? TransitionGraphWeight(frameIndex) : 0f;
        foreach (var audit in actual["animGraphCurveAudit"]!.AsObject())
        {
            audit.Value!["present"] = true;
            audit.Value["value"] = audit.Key is "leftIk" or "rightIk" or "allowTransitions" ? 1f : 0f;
        }

        if (caseIndex is 0 or 1)
        {
            PopulateRawSync(oracle["sync"]!.AsObject(), caseIndex, frameIndex);
            if (caseIndex == 0 && frameIndex == 6)
            {
                oracle["events"]!.AsArray().Add(CanonicalObservedEvent(0, "Trigger", 0, .0018429533f));
            }
            if (caseIndex == 0 && frameIndex == 40)
            {
                oracle["events"]!.AsArray().Add(CanonicalObservedEvent(1, "Trigger", 0, .0016776919f));
            }
            if (caseIndex == 1 && frameIndex == 1)
            {
                oracle["events"]!.AsArray().Add(CanonicalObservedEvent(3, "Trigger", 0, .008333334f));
            }
        }

        if (caseIndex is 2 or 3 or 4)
        {
            PopulateRawTransition(actual, oracle, caseIndex, frameIndex);
        }
        if (caseIndex >= 5)
        {
            PopulateRawAction(actual, oracle, caseIndex, frameIndex);
        }
    }

    private static float ActionGraphWeight(int frameIndex)
    {
        var value = 0f;
        for (var index = 0; index <= frameIndex; index++)
        {
            value = global::System.MathF.Min(1f, value + 0.016666668f / .2f);
        }
        return value;
    }

    private static float ActionLaneWeight(int caseIndex, int frameIndex)
    {
        if (caseIndex == 6 && frameIndex >= 56)
        {
            return TowardZero(1f, frameIndex - 55, 0.016666668f / .2f);
        }
        if (caseIndex == 5 && frameIndex >= 91)
        {
            var value = global::System.MathF.Max(0f,
                1f - (0.016666668f - 7.1525574e-7f) / .2f);
            return TowardZero(value, frameIndex - 91, 0.016666668f / .2f);
        }
        return ActionGraphWeight(frameIndex);
    }

    private static float TowardZero(float value, int count, float step)
    {
        for (var index = 0; index < count; index++)
        {
            value = global::System.MathF.Max(0f, value - step);
        }
        return value;
    }

    private static float TransitionGraphWeight(int frameIndex)
    {
        var value = 0f;
        for (var index = 0; index < frameIndex; index++)
        {
            value = global::System.MathF.Min(1f, value + 0.016666668f / .2f);
        }
        return value;
    }

    private static void PopulateRawSync(JsonObject sync, int caseIndex, int frameIndex)
    {
        sync["active"] = true;
        sync["leader"] = ObservedCanonical(1);
        sync["nativeInstanceOrdinal"] = "1";
        sync["previousMarker"] = MarkerEvidence(frameIndex <= 6 ? 1 : 0, canonical: true);
        sync["nextMarker"] = MarkerEvidence(frameIndex <= 6 ? 0 : 1, canonical: true);
        sync["cycle"] = "0";
        if (caseIndex == 0 && frameIndex == 40)
        {
            sync["phase"] = .028710753f;
            sync["leftFootPhase"] = .9712893f;
            sync["rightFootPhase"] = .028710753f;
        }
    }

    private static void PopulateRawTransition(JsonObject actual, JsonObject oracle, int caseIndex, int frameIndex)
    {
        var role = caseIndex switch
        {
            2 => "transition_standing_left",
            3 => "transition_standing_right",
            _ => "transition_crouching_left",
        };
        if (frameIndex == 0)
        {
            actual["transitionStimulusReceipts"]!.AsArray().Add(FrozenTransitionReceipt(caseIndex));
        }
        var transition = actual["dynamicTransition"]!.AsObject();
        transition["active"] = true;
        transition["source"] = ObservedNative(role);
        transition["nativeInstanceOrdinal"] = "1";
        transition["foot"] = caseIndex == 3 ? "Right" : "Left";
        transition["activatedAfterUpdate"] = frameIndex == 0;
        transition["previousTimeSeconds"] = frameIndex == 0 ? 0f : T(frameIndex - 1);
        transition["currentTimeSeconds"] = frameIndex == 0 ? 0f : T(frameIndex);
        transition["playRate"] = 1.5f;
        transition["observedBlendInSeconds"] = .2f;
        transition["observedBlendOutSeconds"] = .2f;
        transition["observedEffectiveWeight"] = TransitionGraphWeight(frameIndex);

        var state = actual["stateAfter"]!.AsObject();
        state["transitionPlaying"] = true;
        state["transitionSource"] = ObservedNative(role);
        state["transitionNativeInstanceOrdinal"] = "1";
        state["transitionTimeSeconds"] = frameIndex == 0 ? 0f : T(frameIndex);
        state["transitionCooldownFrames"] = frameIndex == 0 ? 2 : frameIndex == 1 ? 1 : 0;
        state["transitionFoot"] = caseIndex == 3 ? "Right" : "Left";
        if (frameIndex > 0)
        {
            transition["activatedAfterUpdate"] = false;
        }
        if (frameIndex == 23)
        {
            var row = CanonicalEventRows[caseIndex == 3 ? 5 : 4].NativeRows[caseIndex == 4 ? 1 : 0];
            actual["nativeRuntimeTimeline"]!.AsArray().Add(RawTimelineEvent(row, "Trigger", .01111111f));
        }
        if (frameIndex == 32)
        {
            oracle["events"]!.AsArray().Add(CanonicalObservedEvent(caseIndex == 3 ? 5 : 4, "Trigger", 0, .013643463f));
        }
    }

    private static void PopulateRawAction(JsonObject actual, JsonObject oracle, int caseIndex, int frameIndex)
    {
        var outcomes = actual["actionOutcomes"]!.AsArray();
        if (frameIndex == 0)
        {
            outcomes.Add(RawOutcome("Started", "MontageStarted", interrupted: false));
        }
        if (caseIndex == 6 && frameIndex == 56)
        {
            outcomes.Add(RawOutcome("Cancelled", "MontageBlendingOutStarted", interrupted: true));
        }
        if (caseIndex == 5 && frameIndex == 91)
        {
            outcomes.Add(RawOutcome("Finished", "MontageEnded", interrupted: false));
        }

        var closingFrame = caseIndex == 6 ? 56 : caseIndex == 5 ? 91 : int.MaxValue;
        if (frameIndex <= closingFrame)
        {
            var current = frameIndex == 91 ? 1.5f : Q(frameIndex);
            var previous = frameIndex == 0 ? 0f : Q(frameIndex - 1);
            if (caseIndex == 6 && frameIndex == 56)
            {
                previous = Q(55);
                current = previous;
            }
            var playback = actual["actionPlayback"]!.AsObject();
            playback["status"] = frameIndex == closingFrame ? "ClosingThisFrame" : "Playing";
            playback["montageSource"] = ObservedNative("roll_montage");
            playback["segmentSource"] = ObservedNative("roll_sequence");
            playback["nativeInstanceOrdinal"] = "1";
            playback["currentSectionName"] = "Default";
            playback["segmentIndex"] = 0;
            playback["previousMontageTimeSeconds"] = previous;
            playback["currentMontageTimeSeconds"] = current;
            playback["previousClipTimeSeconds"] = previous;
            playback["currentClipTimeSeconds"] = current;
            playback["finalSegmentDeltaSeconds"] = frameIndex == 91 ? 7.1525574e-7f : 0f;
            playback["playRate"] = 1f;
        }

        var visualTail = caseIndex switch
        {
            5 => frameIndex < 91,
            6 => frameIndex <= 68,
            _ => true,
        };
        if (visualTail)
        {
            var visual = actual["actionVisualContribution"]!.AsObject();
            visual["contributing"] = true;
            visual["montageSource"] = ObservedNative("roll_montage");
            visual["nativeInstanceOrdinal"] = "1";
            visual["observedMontageTimeSeconds"] = global::System.MathF.Min(1.5f, Q(frameIndex));
            visual["observedBlendInSeconds"] = .1f;
            visual["observedBlendOutSeconds"] = .3f;
            visual["observedBlendInOption"] = 2;
            visual["observedBlendOutOption"] = 2;
            visual["observedEffectiveWeight"] = NativeVisualWeight(caseIndex, frameIndex);
        }

        AddRawCanonicalActionEvents(oracle, caseIndex, frameIndex);
        AddPhysicalActionTimeline(actual["nativeRuntimeTimeline"]!.AsArray(), caseIndex, frameIndex);

        var playing = caseIndex switch { 5 => frameIndex < 91, 6 => frameIndex < 56, _ => true };
        var state = actual["stateAfter"]!.AsObject();
        state["actionPlaying"] = playing;
        state["actionSource"] = playing ? ObservedNative("roll_montage") : ObservedNativeSource();
        state["actionNativeInstanceOrdinal"] = playing ? "1" : "0";
        state["actionTimeSeconds"] = playing ? Q(frameIndex) : 0f;
    }

    private static void AddRawCanonicalActionEvents(JsonObject oracle, int caseIndex, int frameIndex)
    {
        var events = oracle["events"]!.AsArray();
        var ordinal = 0;
        if (frameIndex == 0)
        {
            return;
        }
        if (frameIndex == 1)
        {
            events.Add(CanonicalObservedActionEvent(6, "Begin", ordinal++, 0f, frameIndex));
        }
        if (frameIndex == 7) events.Add(CanonicalObservedActionEvent(7, "Trigger", ordinal++, .0009986386f, frameIndex));
        if (frameIndex == 29) events.Add(CanonicalObservedActionEvent(8, "Trigger", ordinal++, .013360381f, frameIndex));
        if (frameIndex is >= 1 and <= 55)
        {
            events.Add(CanonicalObservedActionEvent(6, "Tick", ordinal, .016666668f, frameIndex));
            oracle["activeNotifyStates"]!.AsArray().Add(CanonicalObservedOwner(6));
        }
        if (frameIndex == 56 && caseIndex is 5 or 7)
        {
            events.Add(CanonicalObservedActionEvent(6, "End", 0, .013460934f, frameIndex));
            events.Add(CanonicalObservedActionEvent(9, "Trigger", 1, .014472842f, frameIndex));
        }
        if (frameIndex == 56 && caseIndex == 6)
        {
            events.Add(CanonicalObservedActionEvent(6, "End", 0, 0f, frameIndex, "Cancelled"));
        }
    }

    private static void AddPhysicalActionTimeline(JsonArray timeline, int caseIndex, int frameIndex)
    {
        if (frameIndex == 1)
        {
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "Begin", 0f));
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "Tick", .016666668f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Begin", 0f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", .016666668f));
        }
        else if (frameIndex is >= 2 and <= 56 && !(caseIndex == 6 && frameIndex == 56))
        {
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "Tick", .016666668f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", .016666668f));
        }
        if (frameIndex == 7) timeline.Add(RawTimelineEvent(CanonicalEventRows[7].NativeRows[0], "Trigger", .00000001f));
        if (frameIndex == 28) timeline.Add(RawTimelineEvent(CanonicalEventRows[8].NativeRows[0], "Trigger", .0166667f));
        if (frameIndex == 57 && caseIndex != 6)
        {
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "Tick", 0f));
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "End", 0f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", .016666668f));
            timeline.Add(RawTimelineEvent(CanonicalEventRows[9].NativeRows[0], "Trigger", 0f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[4], "Trigger", 0f));
        }
        if (frameIndex == 56 && caseIndex == 6)
        {
            timeline.Add(RawTimelineEvent(CanonicalEventRows[6].NativeRows[0], "End", 0f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "End", 0f));
        }
        if (frameIndex is >= 58 and <= 90 && caseIndex == 5)
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", .016666668f));
        if (frameIndex == 91 && caseIndex == 5)
        {
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "Tick", 7.1525574e-7f));
            timeline.Add(RawTimelineEvent(NativeOnlyEventRows[3], "End", 7.1525574e-7f));
        }
        if (frameIndex == 61) timeline.Add(RawTimelineEvent(NativeOnlyEventRows[5], "Trigger", 0f));
        if (frameIndex == 75) timeline.Add(RawTimelineEvent(NativeOnlyEventRows[6], "Trigger", 0f));
    }

    private static float NativeVisualWeight(int caseIndex, int frameIndex)
    {
        const float blendInStep = .016666668f / .1f;
        if (caseIndex == 6 && frameIndex >= 56)
            return TowardZero(1f, frameIndex - 55, .016666668f / .3f);
        var value = 0f;
        for (var index = 0; index <= frameIndex; index++)
            value = global::System.MathF.Min(1f, value + blendInStep);
        return value;
    }

    private static JsonObject CanonicalObservedActionEvent(
        int eventIndex, string phase, int ordinal, float offset, int frameIndex, string termination = "None")
    {
        var value = CanonicalObservedEvent(eventIndex, phase, ordinal, offset, termination);
        value["observedWeight"] = ActionGraphWeight(frameIndex);
        return value;
    }

    private static JsonObject CanonicalObservedEvent(
        int eventIndex, string phase, int ordinal, float offset, string termination = "None")
    {
        var row = CanonicalEventRows[eventIndex];
        return new JsonObject
        {
            ["source"] = ObservedCanonical(row.Source),
            ["observedEventStableId"] = row.StableEventId,
            ["observedEventAssetStableId"] = SourceRows[row.Source].CanonicalStableId,
            ["observedOwnerKind"] = row.Source == 7 ? "MontageTimeline" : "SequenceTimeline",
            ["observedSourceClassPath"] = row.ClassPath,
            ["observedSourceIndex"] = row.SourceIndex, ["observedTrackIndex"] = row.TrackIndex,
            ["observedMontageStableId"] = SourceRows[row.Source].CanonicalMontageStableId,
            ["observedOwnerSectionName"] = row.Source == 7 ? "Default" : string.Empty,
            ["observedSegmentIndex"] = row.Source == 8 ? 0 : -1,
            ["boundaryOrdinal"] = row.Source == 8 ? 1 : 0,
            ["nativeInstanceOrdinal"] = "1", ["playbackCycle"] = "0",
            ["frameEventOrdinal"] = ordinal, ["observedFrameOffsetSeconds"] = offset,
            ["observedWeight"] = 1f,
            ["kind"] = row.Source == 7 ? "SetAction" : "Generic",
            ["tickMode"] = "Queued", ["phase"] = phase,
            ["nativeTerminationReason"] = termination,
            ["payload"] = row.Source == 7 ? NativePayload(2, 1) : NativePayload(),
        };
    }

    private static JsonObject CanonicalObservedOwner(int eventIndex)
    {
        var row = CanonicalEventRows[eventIndex];
        return new JsonObject
        {
            ["source"] = ObservedCanonical(row.Source),
            ["observedEventStableId"] = row.StableEventId,
            ["nativeInstanceOrdinal"] = "1", ["playbackCycle"] = "0",
        };
    }

    private static JsonObject RawTimelineEvent(NativeEventRow row, string phase, float offset) => new()
    {
        ["source"] = ObservedNative(row.Role), ["observedEventStableId"] = row.StableEventId,
        ["observedSourceIndex"] = row.SourceIndex, ["observedTrackIndex"] = row.TrackIndex,
        ["observedFrameOffsetSeconds"] = offset, ["phase"] = phase,
    };

    private static JsonObject RawOutcome(string reason, string callback, bool interrupted) => new()
    {
        ["actionSource"] = ObservedNative("roll_montage"), ["nativeInstanceOrdinal"] = "1",
        ["nativeReason"] = reason, ["callback"] = callback, ["interrupted"] = interrupted,
    };

    private static JsonObject FrozenTransitionReceipt(int caseIndex)
    {
        var rightRelevant = caseIndex is 2 or 3;
        var rightX = caseIndex == 3 ? .1f : .09f;
        return new JsonObject
        {
            ["hookContractSha256"] = TransitionHookContractSha256(),
            ["observedAllowTransitions"] = 1f,
            ["preHookUpdatedThisFrame"] = true, ["preHookFrameDelay"] = 0,
            ["preHookTransitionActive"] = false,
            ["left"] = ReceiptFoot(.09f, 1f),
            ["right"] = ReceiptFoot(rightRelevant ? rightX : 0f, rightRelevant ? 1f : 0f),
            ["postHookUpdatedThisFrame"] = true, ["postHookFrameDelay"] = 2,
            ["restoreVerified"] = true,
        };
    }

    private static JsonObject ReceiptFoot(float targetX, float lockAmount) => new()
    {
        ["observedTargetMeters"] = new JsonObject { ["x"] = targetX, ["y"] = 0f, ["z"] = 0f },
        ["observedLockMeters"] = new JsonObject { ["x"] = 0f, ["y"] = 0f, ["z"] = 0f },
        ["observedLockAmount"] = lockAmount,
    };

    private static string TransitionHookContractSha256()
    {
        const string contract =
            "ALS_P5A_NATIVE_TRANSITION_STIMULUS_V1\n" +
            "class=/Script/ALS.AlsAnimationInstance\n" +
            "feet=FeetState:FAlsFeetState\n" +
            "left=Left:FAlsFootState\n" +
            "right=Right:FAlsFootState\n" +
            "target=TargetLocationWorldSpace:FVector\n" +
            "lock=LockLocationWorldSpace:FVector\n" +
            "relevant=LockAmount:float\n" +
            "transitions=TransitionsState:FAlsTransitionsState\n" +
            "allowed=bTransitionsAllowed:bool\n" +
            "dynamic=DynamicTransitionsState:FAlsDynamicTransitionsState\n" +
            "updated=bUpdatedThisFrame:bool\n" +
            "delay=FrameDelay:int32\n" +
            "function=RefreshDynamicTransitions:void()\n";
        var digest = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(contract)))
            .ToLowerInvariant();
        if (digest != "78cfb6aad01c29ac179f63515835aeeb6dd48b70dc42223cf81dea771e27f11d") throw new InvalidDataException("P5A transition hook contract digest is stale.");
        return digest;
    }

    private static JsonObject NativePayload(int semanticId = 0, int enumValue0 = 0) => new()
    {
        ["semanticId"] = semanticId, ["enumValue0"] = enumValue0,
        ["enumValue1"] = 0, ["enumValue2"] = 0, ["scalarValue0"] = 0f, ["flags"] = 0,
    };

    private static JsonObject ObservedCanonical(int sourceIndex)
    {
        var row = SourceRows[sourceIndex];
        return new JsonObject
        {
            ["observedAssetObjectPath"] = row.CanonicalPath,
            ["observedAssetStableId"] = row.CanonicalStableId,
            ["observedAssetClassPath"] = row.CanonicalClass,
            ["observedMontageStableId"] = row.CanonicalMontageStableId,
            ["observedSectionName"] = row.CanonicalSection,
            ["observedSlotName"] = row.CanonicalSlot,
            ["observedSegmentIndex"] = row.CanonicalSegment,
        };
    }

    private static JsonObject ObservedNative(string role)
    {
        var row = NativeByRole(role);
        return new JsonObject
        {
            ["observedAssetObjectPath"] = row.Path, ["observedAssetStableId"] = row.StableId,
            ["observedAssetPackageSha256"] = row.PackageSha256,
            ["observedAssetClassPath"] = row.ClassPath,
            ["observedMontageObjectPath"] = row.MontagePath,
            ["observedMontageStableId"] = row.MontageStableId,
            ["observedSectionName"] = row.Section, ["observedSlotName"] = row.Slot,
            ["observedSegmentIndex"] = row.SegmentIndex,
        };
    }

    private static void PopulatePortSchemaSeed(JsonObject audit, int caseIndex, int frameIndex)
    {
        var prepared = audit["prepared"]!.AsObject();
        var result = audit["result"]!.AsObject();
        var state = audit["stateAfter"]!.AsObject();
        var curves = SemanticCurves(caseIndex, frameIndex);
        foreach (var name in new[] { "leftIk", "rightIk", "leftLock", "rightLock", "allowTransitions" })
        {
            prepared[name] = curves[name]!.DeepClone();
        }
        var passthrough = audit["result"]!["p4CurvePassthrough"]!.AsObject();
        foreach (var name in new[] { "leftIk", "rightIk", "leftLock", "rightLock" })
        {
            passthrough[name] = curves[name]!.DeepClone();
        }
        if (caseIndex is 0 or 1)
        {
            var p = CanonicalEventRows[0].Time - .016666668f / 2f;
            prepared["syncMappings"]!.AsArray().Add(new JsonObject
            {
                ["occurrenceHandleId"] = 1, ["animationId"] = 1, ["playbackEpoch"] = "1",
                ["durationSeconds"] = 1.1333333f, ["previousCycle"] = "0", ["currentCycle"] = "0",
                ["previousTimeSeconds"] = caseIndex == 1 ? frameIndex == 0 ? p : p + .016666668f : Q(frameIndex),
                ["currentTimeSeconds"] = caseIndex == 1 ? frameIndex == 1 ? p + .016666668f : frameIndex == 0 ? p : p + .016666668f : Q(frameIndex + 1),
                ["mappedPlayRate"] = caseIndex == 1 && frameIndex != 1 ? 0f : 1f,
            });
            var sync = result["sync"]!.AsObject();
            sync["groupId"] = 0;
            sync["leaderOccurrenceHandleId"] = 1;
            sync["leaderAnimationId"] = 54;
            sync["leaderPlaybackEpoch"] = "1";
            sync["previousMarkerId"] = frameIndex <= 6 ? 1 : 0;
            sync["nextMarkerId"] = frameIndex <= 6 ? 0 : 1;
            sync["cycle"] = "0";
            if (caseIndex == 0 && frameIndex == 40)
            {
                sync["phase"] = .028710753f;
                sync["leftFootPhase"] = .9712893f;
                sync["rightFootPhase"] = .028710753f;
            }
            SetCursor(state, 1, 54, -1, frameIndex == 0 ? 0d : Q(frameIndex));
            SetAuthority(state, 0, 1, 54, -1);
        }
        if (caseIndex is 2 or 3 or 4)
        {
            PopulateLane(prepared["transitionGraph"]!.AsObject(),
                state["dynamicTransitionBlendLane"]!.AsObject(),
                TransitionGraphWeight(frameIndex), frameIndex);
            var animationId = caseIndex == 3 ? 82 : 31;
            var foot = caseIndex == 3 ? "Right" : "Left";
            var transitionState = state["dynamicTransition"]!.AsObject();
            transitionState["cooldownFrames"] = frameIndex == 0 ? 2 : frameIndex == 1 ? 1 : 0;
            transitionState["foot"] = foot;
            transitionState["queuedFoot"] = foot;
            transitionState["playbackEpoch"] = "1";
            if (frameIndex == 0)
            {
                transitionState["queuedAnimationId"] = animationId;
                transitionState["queued"] = true;
            }
            else
            {
                transitionState["animationId"] = animationId;
                transitionState["previousPlaybackTime"] = T(frameIndex - 1);
                transitionState["playbackTime"] = T(frameIndex);
                transitionState["active"] = true;
                var transition = result["dynamicTransition"]!.AsObject();
                transition["animationId"] = animationId;
                transition["foot"] = foot;
                transition["blendSeconds"] = .2f;
                transition["playRate"] = 1.5f;
                transition["effectiveWeight"] = TransitionGraphWeight(frameIndex);
                transition["active"] = true;
                SetCursor(state, 34, animationId, -1, T(frameIndex));
                SetAuthority(state, 1, 34, animationId, -1);
            }
        }
        if (caseIndex >= 5)
        {
            PopulateLane(prepared["actionGraph"]!.AsObject(),
                state["actionBlendLane"]!.AsObject(),
                ActionLaneWeight(caseIndex, frameIndex), frameIndex);
            PopulatePortAction(result, state, caseIndex, frameIndex);
        }
    }

    private static void PopulatePortAction(JsonObject result, JsonObject state, int caseIndex, int frameIndex)
    {
        var cancelled = caseIndex == 6 && frameIndex >= 56;
        var completed = caseIndex == 5 && frameIndex >= 91;
        var playing = !cancelled && !completed;
        var player = state["actionPlayer"]!.AsObject();
        player["lastProcessedRequestId"] = "1";
        player["lastProcessedCommandRequestId"] = "1";
        player["lastProcessedCommand"] = caseIndex == 6 && frameIndex >= 56 ? "Cancel" : "Start";
        player["requestId"] = "1";
        player["playbackEpoch"] = "1";
        player["priority"] = 100;
        player["interruptible"] = true;
        if (playing)
        {
            player["actionDefinitionId"] = 0;
            player["sectionId"] = 0;
            player["segmentBindingIndex"] = 0;
            player["playbackTime"] = Q(frameIndex);
            player["playing"] = true;
            var action = result["actionPlayback"]!.AsObject();
            action["occurrenceHandleId"] = 35;
            action["actionDefinitionId"] = 0;
            action["animationId"] = 2;
            action["sectionId"] = 0;
            action["segmentId"] = 0;
            action["playbackEpoch"] = "1";
            action["previousTime"] = frameIndex == 0 ? 0f : Q(frameIndex - 1);
            action["currentTime"] = Q(frameIndex);
            action["previousClipTime"] = frameIndex == 0 ? 0f : Q(frameIndex - 1);
            action["currentClipTime"] = Q(frameIndex);
            action["playRate"] = 1f;
            action["blendSeconds"] = .2f;
            action["effectiveWeight"] = ActionLaneWeight(caseIndex, frameIndex);
            action["active"] = true;
            SetCursor(state, 35, 2, 0, Q(frameIndex));
            SetCursor(state, 36, 28, 0, Q(frameIndex));
            SetAuthority(state, 2, 35, 2, 0);
            SetAuthority(state, 3, 36, 28, 0);
        }
        if (caseIndex == 5 && frameIndex == 91)
        {
            var action = result["actionPlayback"]!.AsObject();
            action["occurrenceHandleId"] = 35; action["actionDefinitionId"] = 0;
            action["animationId"] = 2; action["sectionId"] = 0; action["segmentId"] = 0;
            action["playbackEpoch"] = "1"; action["previousTime"] = Q(90);
            action["currentTime"] = 1.5f; action["previousClipTime"] = Q(90);
            action["currentClipTime"] = 1.5f; action["finalSegmentDeltaSeconds"] = 7.1525574e-7f;
            action["playRate"] = 1f; action["blendSeconds"] = .2f;
            action["effectiveWeight"] = ActionLaneWeight(caseIndex, frameIndex); action["active"] = true;
        }

        var events = result["events"]!.AsArray();
        if (frameIndex == 0)
        {
            events.Add(CoreEvent(6, 35, 2, "Begin", 0f, 1));
            events.Add(CoreEvent(6, 35, 2, "Tick", 0f, 2));
            result["actionOutcomes"]!.AsArray().Add(CoreOutcome("Accepted"));
        }
        else
        {
            var sequence = 2L + frameIndex;
            if (frameIndex == 7)
                events.Add(CoreEvent(7, 36, 28, "Trigger", .0009986386f, sequence++));
            if (frameIndex == 29)
                events.Add(CoreEvent(8, 36, 28, "Trigger", .013360381f, sequence++));
            if (frameIndex is >= 1 and <= 55)
                events.Add(CoreEvent(6, 35, 2, "Tick", .016666668f, sequence));
            if (frameIndex == 56)
            {
                events.Add(CoreEvent(6, 35, 2, "End", caseIndex == 6 ? 0f : .013460934f, sequence++));
                if (caseIndex is 5 or 7)
                    events.Add(CoreEvent(9, 36, 28, "Trigger", .014472842f, sequence));
                if (caseIndex == 6)
                    result["actionOutcomes"]!.AsArray().Add(CoreOutcome("InterruptedByExplicitCancel"));
            }
            if (caseIndex == 5 && frameIndex == 91)
                result["actionOutcomes"]!.AsArray().Add(CoreOutcome("Completed"));
        }
        if (frameIndex <= 55)
        {
            var owner = state["notifyOwnership"]!.AsArray()[0]!.AsObject();
            owner["eventId"] = 6; owner["boundaryOrdinal"] = 0;
            owner["occurrenceHandleId"] = 35; owner["animationId"] = 2; owner["actionId"] = 0;
            owner["playbackEpoch"] = "1"; owner["playbackCycle"] = "0";
            owner["ownerToken"] = "0000000000000001"; owner["active"] = true;
            state["nextOwnerToken"] = "0000000000000002";
        }
    }

    private static JsonObject CoreEvent(
        int eventId, int handle, int animationId, string phase, float animationTime, long sequence) => new()
    {
        ["eventId"] = eventId, ["sourceAnimationId"] = eventId == 6 ? -1 : animationId,
        ["sourceActionId"] = 0, ["occurrenceHandleId"] = handle,
        ["playbackEpoch"] = "1", ["playbackCycle"] = "0",
        ["ownerToken"] = eventId == 6 ? "0000000000000001" : "0000000000000000",
        ["eventSequence"] = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["boundaryOrdinal"] = eventId == 6 ? 0 : 1, ["animationTime"] = animationTime,
        ["weight"] = 1f, ["kind"] = eventId == 6 ? "SetAction" : "Generic",
        ["phase"] = phase, ["payload"] = eventId == 6 ? Payload(2, 1) : Payload(),
    };

    private static JsonObject CoreOutcome(string code) => new()
    {
        ["requestId"] = "1", ["actionDefinitionId"] = 0,
        ["playbackEpoch"] = "1", ["resultCode"] = code,
    };

    private static void SetCursor(JsonObject state, int handle, int animationId, int actionId, double time)
    {
        var cursor = state["timelineCursors"]!.AsArray()[handle]!.AsObject();
        cursor["occurrenceHandleId"] = handle; cursor["animationId"] = animationId;
        cursor["actionId"] = actionId; cursor["playbackEpoch"] = "1";
        cursor["consumedUnwrappedTimeSeconds"] = time;
    }

    private static void SetAuthority(JsonObject state, int group, int handle, int animationId, int actionId)
    {
        var authority = state["authorities"]!.AsArray()[group]!.AsObject();
        authority["occurrenceHandleId"] = handle; authority["animationId"] = animationId;
        authority["actionId"] = actionId; authority["playbackEpoch"] = "1"; authority["active"] = true;
    }

    private static void PopulateLane(JsonObject graph, JsonObject lane, float weight, int frameIndex)
    {
        graph["laneWeight"] = weight;
        graph["incomingMix"] = frameIndex > 0 ? 1f : 0f;
        graph["incomingEffectiveWeight"] = weight;
        lane["laneWeight"] = weight;
        lane["incomingMix"] = frameIndex > 0 ? 1f : 0f;
        lane["blendSeconds"] = .2f;
        lane["visualActive"] = weight > 0f;
    }

    private static JsonObject Curves() => new()
    {
        ["leftIk"] = 0f, ["rightIk"] = 0f, ["leftLock"] = 0f,
        ["rightLock"] = 0f, ["allowTransitions"] = 0f,
    };

    private static JsonObject CurveAudit() => new() { ["present"] = false, ["value"] = 0f };

    private static JsonObject ObservedCanonicalSource() => new()
    {
        ["observedAssetObjectPath"] = string.Empty, ["observedAssetStableId"] = string.Empty,
        ["observedAssetClassPath"] = string.Empty, ["observedMontageStableId"] = string.Empty,
        ["observedSectionName"] = string.Empty, ["observedSlotName"] = string.Empty,
        ["observedSegmentIndex"] = -1,
    };

    private static JsonObject ObservedNativeSource() => new()
    {
        ["observedAssetObjectPath"] = string.Empty, ["observedAssetStableId"] = string.Empty,
        ["observedAssetPackageSha256"] = string.Empty, ["observedAssetClassPath"] = string.Empty,
        ["observedMontageObjectPath"] = string.Empty, ["observedMontageStableId"] = string.Empty,
        ["observedSectionName"] = string.Empty, ["observedSlotName"] = string.Empty,
        ["observedSegmentIndex"] = -1,
    };

    private static JsonObject ObservedMarker() => new()
    {
        ["stableMarkerId"] = string.Empty, ["name"] = string.Empty,
        ["sourceIndex"] = -1, ["trackIndex"] = -1, ["timeSeconds"] = 0f,
    };

    private static JsonObject LaneGraph() => new()
    {
        ["outgoing"] = LaneSource(), ["incoming"] = LaneSource(), ["laneWeight"] = 0f,
        ["incomingMix"] = 0f, ["outgoingEffectiveWeight"] = 0f, ["incomingEffectiveWeight"] = 0f,
    };

    private static JsonObject LaneSource() => new()
    {
        ["occurrenceHandleId"] = -1, ["animationId"] = -1, ["bindingIndex"] = -1,
        ["playbackEpoch"] = "0", ["previousClipTime"] = 0f, ["currentClipTime"] = 0f,
        ["contributingDeltaSeconds"] = 0f, ["playRate"] = 0f, ["active"] = false,
    };

    private static JsonObject CoreSync() => new()
    {
        ["groupId"] = -1, ["leaderOccurrenceHandleId"] = -1, ["leaderAnimationId"] = -1,
        ["leaderPlaybackEpoch"] = "0", ["previousMarkerId"] = -1, ["nextMarkerId"] = -1,
        ["cycle"] = "0", ["phase"] = 0f, ["leftFootPhase"] = 0f, ["rightFootPhase"] = 0f,
    };

    private static JsonObject CoreTransition() => new()
    {
        ["animationId"] = -1, ["foot"] = "Left", ["blendSeconds"] = 0f,
        ["playRate"] = 0f, ["effectiveWeight"] = 0f, ["active"] = false,
    };

    private static JsonObject CoreAction() => new()
    {
        ["occurrenceHandleId"] = -1, ["actionDefinitionId"] = -1, ["animationId"] = -1,
        ["sectionId"] = -1, ["segmentId"] = -1, ["playbackEpoch"] = "0",
        ["previousTime"] = 0f, ["currentTime"] = 0f, ["previousClipTime"] = 0f,
        ["currentClipTime"] = 0f, ["finalSegmentDeltaSeconds"] = 0f, ["playRate"] = 0f,
        ["blendSeconds"] = 0f, ["effectiveWeight"] = 0f, ["active"] = false,
    };

    private static JsonObject ActionPlayerState() => new()
    {
        ["actionDefinitionId"] = -1, ["sectionId"] = -1, ["segmentBindingIndex"] = -1,
        ["requestId"] = "0", ["lastProcessedRequestId"] = "0",
        ["lastProcessedCommandRequestId"] = "0", ["lastProcessedCommand"] = "None",
        ["playbackEpoch"] = "0", ["playbackTime"] = 0f, ["priority"] = 0,
        ["playing"] = false, ["interruptible"] = false,
    };

    private static JsonObject TransitionState() => new()
    {
        ["animationId"] = -1, ["queuedAnimationId"] = -1, ["playbackEpoch"] = "0",
        ["previousPlaybackTime"] = 0f, ["playbackTime"] = 0f, ["cooldownFrames"] = 0,
        ["foot"] = "Left", ["queuedFoot"] = "Left", ["active"] = false, ["queued"] = false,
    };

    private static JsonObject LaneState() => new()
    {
        ["outgoingOccurrenceHandleId"] = -1, ["outgoingAnimationId"] = -1,
        ["outgoingBindingIndex"] = -1, ["outgoingPlaybackEpoch"] = "0",
        ["outgoingClipTime"] = 0f, ["laneWeight"] = 0f, ["incomingMix"] = 0f,
        ["blendSeconds"] = 0f, ["visualActive"] = false, ["outgoingActive"] = false,
    };

    private static JsonObject TimelineCursor() => new()
    {
        ["occurrenceHandleId"] = -1, ["animationId"] = -1, ["actionId"] = -1,
        ["playbackEpoch"] = "0", ["consumedUnwrappedTimeSeconds"] = 0d,
    };

    private static JsonObject AuthorityState(int index) => new()
    {
        ["groupId"] = index, ["occurrenceHandleId"] = -1, ["animationId"] = -1,
        ["actionId"] = -1, ["playbackEpoch"] = "0", ["active"] = false,
    };

    private static JsonObject NotifyOwnership() => new()
    {
        ["eventId"] = -1, ["boundaryOrdinal"] = -1, ["occurrenceHandleId"] = -1,
        ["animationId"] = -1, ["actionId"] = -1, ["playbackEpoch"] = "0",
        ["playbackCycle"] = "0", ["ownerToken"] = "0000000000000000", ["active"] = false,
    };

    private static readonly NativeSourceRow[] FlatNativeSources =
        SourceRows.SelectMany(row => row.NativeRows).ToArray();

    private static readonly NativeEventRow[] NativeAuditEvents =
    [
        CanonicalEventRows[0].NativeRows[0], CanonicalEventRows[1].NativeRows[0],
        CanonicalEventRows[2].NativeRows[0], NativeOnlyEventRows[0], NativeOnlyEventRows[1],
        CanonicalEventRows[3].NativeRows[0], NativeOnlyEventRows[2],
        CanonicalEventRows[4].NativeRows[0], CanonicalEventRows[4].NativeRows[1],
        CanonicalEventRows[5].NativeRows[0], CanonicalEventRows[5].NativeRows[1],
        CanonicalEventRows[6].NativeRows[0], NativeOnlyEventRows[3],
        CanonicalEventRows[7].NativeRows[0], CanonicalEventRows[8].NativeRows[0],
        CanonicalEventRows[9].NativeRows[0], NativeOnlyEventRows[4], NativeOnlyEventRows[5],
        NativeOnlyEventRows[6],
    ];

    private static JsonObject NativeAssetAudit(int index)
    {
        var row = FlatNativeSources[index];
        return new JsonObject
        {
            ["assetObjectPath"] = row.Path, ["assetStableId"] = row.StableId,
            ["assetPackageSha256"] = row.PackageSha256, ["assetClassPath"] = row.ClassPath,
            ["durationSeconds"] = row.Duration, ["authoredLoop"] = row.AuthoredLoop,
            ["montageObjectPath"] = row.MontagePath, ["montageStableId"] = row.MontageStableId,
            ["sectionName"] = row.Section, ["slotName"] = row.Slot,
            ["segmentIndex"] = row.SegmentIndex,
        };
    }

    private static JsonObject NativeEventAudit(int index)
    {
        var row = NativeAuditEvents[index];
        return new JsonObject
        {
            ["assetStableId"] = NativeByRole(row.Role).StableId,
            ["stableEventId"] = row.StableEventId,
            ["ownerKind"] = row.Owner, ["sourceClassPath"] = row.ClassPath,
            ["sourceIndex"] = row.SourceIndex, ["trackIndex"] = row.TrackIndex,
            ["timeSeconds"] = row.Time, ["durationSeconds"] = row.Duration,
            ["triggerWeightThreshold"] = row.Threshold, ["tickMode"] = "Queued",
        };
    }

    private static JsonObject NativeMarkerAudit(int index) => MarkerEvidence(index, canonical: false);

    private static JsonObject NativeCurveInventory(int index)
    {
        var row = FlatNativeSources[index];
        var curveNames = index switch
        {
            1 => new JsonArray("FootPlanted", "PoseGait"),
            3 or 4 => new JsonArray("FootLeftLock", "FootRightLock", "RotationYawSpeed"),
            _ => new JsonArray(),
        };
        return new JsonObject
        {
            ["assetObjectPath"] = row.Path,
            ["assetStableId"] = row.StableId,
            ["curveNames"] = curveNames,
        };
    }

    private static JsonArray Repeat(int count, Func<int, JsonNode> factory)
    {
        var array = new JsonArray();
        for (var index = 0; index < count; index++)
        {
            array.Add(factory(index));
        }
        return array;
    }

    internal static byte[] CanonicalBytes(JsonObject value)
    {
        var json = value.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        json = json.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\r', '\n') + "\n";
        return new UTF8Encoding(false, true).GetBytes(json);
    }
}

internal static class P5aPortReplay
{
    internal static JsonObject Build(
        JsonObject plan,
        scoped in AlsP5OccurrenceLayoutView occurrenceLayout,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        AlsP5aTrace.ResetShadowExecutionAudit();
        AlsP5OccurrenceLayoutContract.Validate(
            occurrenceLayout.Version, occurrenceLayout.Digest, occurrenceLayout.Entries);
        if (runtimeBindings.LayoutDigest != occurrenceLayout.Digest)
        {
            throw new InvalidDataException("P5A replay layout and runtime binding digests differ.");
        }
        var layoutEntries = occurrenceLayout.Entries.ToArray();
        var port = CreatePortDocument(plan);
        var sourceMap = BuildSourceMap(plan, occurrenceLayout, runtimeBindings);
        var planCases = plan["cases"]!.AsArray();
        var portCases = port["cases"]!.AsArray();
        for (var caseIndex = 0; caseIndex < planCases.Count; caseIndex++)
        {
            var state = AlsRuntimeState.CreateDefault();
            var storage = CreateReplayStorage(layoutEntries);
            var cursors = storage.Cursors;
            var authorities = storage.Authorities;
            var ownership = Enumerable.Range(0, AlsEventBuffer.Capacity)
                .Select(_ => AlsNotifyStateOwnership.CreateDefault()).ToArray();
            var nextOwnerToken = 1UL;
            var control = new[] { new AlsP5RuntimeScratchControl((ulong)caseIndex + 101UL) };
            var candidateCursors = storage.CandidateCursors;
            var candidateAuthorities = storage.CandidateAuthorities;
            var candidateOwnership = new AlsNotifyStateOwnership[AlsEventBuffer.Capacity];
            var occurrences = new AlsTimelineOccurrence[AlsEventBuffer.Capacity];
            var slices = new AlsActionTraversalSlice[AlsActionPlayer.TraversalCapacity];
            var playbacks = new AlsTimelinePlayback[3 + 2 + 2 * AlsActionPlayer.TraversalCapacity];
            var syncInputs = new AlsSyncPlayback[1];
            var syncOutputs = new AlsSyncMappedPlayback[1];
            var curveSamples = new AlsCurveBlendSample[7];
            var planFrames = planCases[caseIndex]!["frames"]!.AsArray();
            var portFrames = portCases[caseIndex]!["frames"]!.AsArray();
            for (var frameIndex = 0; frameIndex < planFrames.Count; frameIndex++)
            {
                var inputNode = planFrames[frameIndex]!["input"]!.AsObject();
                var baseDescriptors = Descriptors(inputNode["p4Curves"]!["base"]!.AsArray(), sourceMap);
                var turnDescriptors = Descriptors(inputNode["p4Curves"]!["turnBanks"]!.AsArray(), sourceMap);
                var rotateDescriptors = Descriptors(inputNode["p4Curves"]!["rotateBanks"]!.AsArray(), sourceMap);
                var input = FrameInput(
                    inputNode, baseDescriptors, turnDescriptors, rotateDescriptors,
                    plan, sourceMap, runtimeBindings);
                var portFrame = portFrames[frameIndex]!.AsObject();
                portFrame["identity"] = IdentityJson(input.Identity);
                var scratch = new AlsP5RuntimeScratch(
                    3, 1, control, candidateCursors, candidateAuthorities, candidateOwnership,
                    occurrences, slices, playbacks, syncInputs, syncOutputs, curveSamples);
                var isShadowFrame = AlsP5aTrace.IsShadowFrame(caseIndex, frameIndex);
                var committedBefore = CaptureCommittedShadow(
                    state, cursors, authorities, ownership, nextOwnerToken);
                if (!AlsP5Runtime.TryPrepare(
                        runtimeBindings, input, state, cursors, authorities, ownership, nextOwnerToken,
                        ref scratch, out var prepared, out var prepareFailure))
                {
                    throw new InvalidDataException(
                        $"P5A replay prepare failed at case {caseIndex} frame {frameIndex}: {prepareFailure}.");
                }
                if (isShadowFrame)
                {
                    RequireExactShadowReplay(committedBefore,
                        CaptureCommittedShadow(state, cursors, authorities, ownership, nextOwnerToken),
                        "committed state after Prepare");
                    var preparedBefore = CapturePreparedShadow(prepared);
                    var candidateBefore = CaptureCandidateShadow(ref scratch);
                    var invalid = CanonicalP4Result(input, prepared);
                    invalid.P4ReasonCode = AlsP4ReasonCode.InvalidSelection;
                    var shadowNext = state;
                    shadowNext.LocomotionState = ParseLocomotion(inputNode["modes"]!["locomotionMode"]!.GetValue<string>());
                    var shadowProbe = TransitionProbe(inputNode);
                    var invalidOutcome = ExecuteShadowFinalize(
                        prepared, ref scratch, in invalid, in shadowNext, in shadowProbe);
                    ValidateFailedShadowFinalize(
                        in invalidOutcome, AlsP5FailureCode.InvalidTimeline);
                    RequireExactShadowReplay(committedBefore,
                        CaptureCommittedShadow(state, cursors, authorities, ownership, nextOwnerToken),
                        "committed rollback after InvalidTimeline");

                    var staleP4 = CanonicalP4Result(input, prepared);
                    var staleOutcome = ExecuteShadowFinalize(
                        prepared, ref scratch, in staleP4, in shadowNext, in shadowProbe);
                    ValidateFailedShadowFinalize(
                        in staleOutcome, AlsP5FailureCode.StalePreparedFrame);
                    RequireExactShadowReplay(committedBefore,
                        CaptureCommittedShadow(state, cursors, authorities, ownership, nextOwnerToken),
                        "committed rollback after StalePreparedFrame");

                    scratch = new AlsP5RuntimeScratch(
                        3, 1, control, candidateCursors, candidateAuthorities, candidateOwnership,
                        occurrences, slices, playbacks, syncInputs, syncOutputs, curveSamples);
                    if (!AlsP5Runtime.TryPrepare(
                            runtimeBindings, input, state, cursors, authorities, ownership, nextOwnerToken,
                            ref scratch, out prepared, out prepareFailure))
                    {
                        throw new InvalidDataException(
                            $"P5A fresh replay prepare failed at case {caseIndex} frame {frameIndex}: {prepareFailure}.");
                    }
                    RequireExactShadowReplay(
                        preparedBefore, CapturePreparedShadow(prepared), "fresh prepared frame");
                    RequireExactShadowReplay(
                        candidateBefore, CaptureCandidateShadow(ref scratch), "fresh candidate state");
                    AlsP5aTrace.RecordShadowExecution(caseIndex, frameIndex);
                }

                var p4Result = CanonicalP4Result(input, prepared);
                var nextP4State = state;
                nextP4State.LocomotionState = ParseLocomotion(inputNode["modes"]!["locomotionMode"]!.GetValue<string>());
                if (!AlsP5Runtime.TryFinalize(
                        prepared, ref scratch, p4Result, nextP4State, TransitionProbe(inputNode),
                        out var producedToken, out var producedState, out var result, out var finalizeFailure))
                {
                    throw new InvalidDataException(
                        $"P5A replay finalize failed at case {caseIndex} frame {frameIndex}: {finalizeFailure}.");
                }
                var comparable = ComparableJson(
                    caseIndex, frameIndex, input, prepared, result,
                    state, ownership, producedState, candidateOwnership,
                    plan, sourceMap, runtimeBindings);
                candidateCursors.CopyTo(cursors, 0);
                candidateAuthorities.CopyTo(authorities, 0);
                candidateOwnership.CopyTo(ownership, 0);
                state = producedState;
                nextOwnerToken = producedToken;
                portFrame["comparableActual"] = comparable;
                portFrame["portAudit"] = new JsonObject
                {
                    ["prepared"] = PreparedJson(prepared),
                    ["result"] = ResultJson(result),
                    ["stateAfter"] = StateJson(state, cursors, authorities, ownership, nextOwnerToken),
                };
            }
        }
        AlsP5aTrace.ValidateShadowExecutionAudit();
        return port;
    }

    private static ReplayStorage CreateReplayStorage(AlsP5OccurrenceLayoutEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var authorityCount = 0;
        foreach (var entry in entries)
        {
            authorityCount = System.Math.Max(authorityCount, checked(entry.AuthorityGroupId + 1));
        }
        var cursors = Enumerable.Range(0, entries.Length)
            .Select(_ => AlsTimelineCursor.CreateDefault()).ToArray();
        var authorities = Enumerable.Range(0, authorityCount)
            .Select(AlsTimelineAuthorityState.CreateDefault).ToArray();
        return new ReplayStorage(
            cursors,
            authorities,
            new AlsTimelineCursor[entries.Length],
            new AlsTimelineAuthorityState[authorityCount]);
    }

    private static JsonObject CreatePortDocument(JsonObject plan)
    {
        var cases = new JsonArray();
        foreach (var caseNode in plan["cases"]!.AsArray())
        {
            var planCase = caseNode!.AsObject();
            var frames = new JsonArray();
            foreach (var frameNode in planCase["frames"]!.AsArray())
            {
                frames.Add(new JsonObject
                {
                    ["frameIndex"] = frameNode!["frameIndex"]!.DeepClone(),
                });
            }
            cases.Add(new JsonObject
            {
                ["ordinal"] = planCase["ordinal"]!.DeepClone(),
                ["caseId"] = planCase["caseId"]!.DeepClone(),
                ["frames"] = frames,
            });
        }

        var planHash = Convert.ToHexString(SHA256.HashData(P5aFrozenPlanDocuments.CanonicalBytes(plan)))
            .ToLowerInvariant();
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "p5a_trace",
            ["representation"] = "port_canonical",
            ["tracePlanSha256"] = planHash,
            ["reference"] = plan["reference"]!.DeepClone(),
            ["snapshot"] = plan["snapshot"]!.DeepClone(),
            ["provenance"] = "core_oracle_v1",
            ["cases"] = cases,
        };
    }

    private static Dictionary<string, SourceBinding> BuildSourceMap(
        JsonObject plan,
        scoped in AlsP5OccurrenceLayoutView occurrenceLayout,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var result = new Dictionary<string, SourceBinding>(StringComparer.Ordinal);
        foreach (var sourceNode in plan["sources"]!.AsArray())
        {
            var source = sourceNode!.AsObject();
            var key = source["layoutKey"]!.AsObject();
            var kind = Enum.Parse<AlsP5OccurrenceSourceKind>(key["sourceKind"]!.GetValue<string>());
            var bindingIndex = key["sourceBindingIndex"]!.GetValue<int>();
            var graphSlot = key["graphSlotIndex"]!.GetValue<int>();
            AlsP5OccurrenceLayoutEntry? match = null;
            foreach (ref readonly var entry in occurrenceLayout.Entries)
            {
                if (entry.SourceKind == kind && entry.SourceBindingIndex == bindingIndex &&
                    entry.GraphSlotIndex == graphSlot)
                {
                    if (match is not null) throw new InvalidDataException("P5A source layout is ambiguous.");
                    match = entry;
                }
            }
            var resolved = match ?? throw new InvalidDataException("P5A source layout is unresolved.");
            var canonicalRole = source["canonicalEvidence"]!["canonicalRole"]!.GetValue<string>();
            var animationId = kind switch
            {
                AlsP5OccurrenceSourceKind.Base or AlsP5OccurrenceSourceKind.Turn or
                    AlsP5OccurrenceSourceKind.Rotate => ResolveGraphAnimationId(
                        kind, bindingIndex, runtimeBindings.FootCurveBindings),
                AlsP5OccurrenceSourceKind.Transition when canonicalRole == "transition_left_sequence" =>
                    runtimeBindings.DynamicTransition.StandingLeft.AnimationId,
                AlsP5OccurrenceSourceKind.Transition when canonicalRole == "transition_right_sequence" =>
                    runtimeBindings.DynamicTransition.StandingRight.AnimationId,
                AlsP5OccurrenceSourceKind.ActionMontage => ResolveActionDefinition(
                    bindingIndex, runtimeBindings).MontageId,
                AlsP5OccurrenceSourceKind.ActionSequence => ResolveActionSegment(
                    resolved.OccurrenceHandleId, bindingIndex, runtimeBindings).AnimationId,
                _ => -1,
            };
            var actionDefinitionId = kind is AlsP5OccurrenceSourceKind.ActionMontage or
                AlsP5OccurrenceSourceKind.ActionSequence ? bindingIndex : -1;
            var segmentId = kind == AlsP5OccurrenceSourceKind.ActionSequence
                ? ResolveActionSegment(resolved.OccurrenceHandleId, bindingIndex, runtimeBindings).SegmentId
                : -1;
            var traceSourceId = source["traceSourceId"]!.GetValue<string>();
            result.Add(traceSourceId, new SourceBinding(
                traceSourceId, resolved, animationId, actionDefinitionId, segmentId, canonicalRole, source));
        }
        return result;
    }

    internal static int ResolveGraphAnimationId(
        AlsP5OccurrenceSourceKind kind,
        int bindingIndex,
        ReadOnlySpan<AlsP4FootCurveRuntimeBinding> footCurveBindings)
    {
        const int baseCount = 22;
        const int turnCount = 8;
        const int rotateCount = 4;
        const int footBindingCount = baseCount + turnCount + rotateCount;
        if (footCurveBindings.Length != footBindingCount)
        {
            throw new InvalidDataException("P5A graph foot-curve binding closure must contain 34 entries.");
        }

        var footIndex = kind switch
        {
            AlsP5OccurrenceSourceKind.Base when bindingIndex == 0 => 0,
            AlsP5OccurrenceSourceKind.Base when bindingIndex is >= 1 and <= 13 => bindingIndex + 1,
            AlsP5OccurrenceSourceKind.Base when bindingIndex == 14 => 1,
            AlsP5OccurrenceSourceKind.Base when bindingIndex is >= 15 and < baseCount => bindingIndex,
            AlsP5OccurrenceSourceKind.Turn when bindingIndex is >= 0 and < turnCount => baseCount + bindingIndex,
            AlsP5OccurrenceSourceKind.Rotate when bindingIndex is >= 0 and < rotateCount =>
                baseCount + turnCount + bindingIndex,
            _ => throw new InvalidDataException("P5A graph source binding is outside the frozen topology."),
        };
        var animationId = footCurveBindings[footIndex].AnimationId;
        if (animationId < 0)
        {
            throw new InvalidDataException("P5A graph source resolves to an invalid animation ID.");
        }
        return animationId;
    }

    private static ShadowFinalizeOutcome ExecuteShadowFinalize(
        scoped AlsP5PreparedFrame prepared,
        scoped ref AlsP5RuntimeScratch scratch,
        scoped in AlsFrameResult p4Result,
        scoped in AlsRuntimeState nextState,
        scoped in AlsDynamicTransitionInput transitionProbe)
    {
        var succeeded = AlsP5Runtime.TryFinalize(
            prepared, ref scratch, p4Result, nextState, transitionProbe,
            out var nextOwnerToken, out var producedState, out var result, out var failure);
        return new ShadowFinalizeOutcome(
            succeeded, failure, nextOwnerToken, producedState, result);
    }

    private static void RequireExactShadowReplay(
        byte[] expected,
        byte[] actual,
        string label)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new InvalidDataException($"P5A shadow {label} did not reproduce exactly.");
        }
    }

    internal static void ValidateFailedShadowFinalize(
        scoped in ShadowFinalizeOutcome outcome,
        AlsP5FailureCode expectedFailure)
    {
        var nextState = outcome.NextState;
        var result = outcome.Result;
        if (outcome.Succeeded || outcome.Failure != expectedFailure || outcome.NextOwnerToken != 0 ||
            !IsAllZero(in nextState) || !IsAllZero(in result))
        {
            throw new InvalidDataException(
                $"P5A shadow finalize did not fail atomically with {expectedFailure}.");
        }
    }

    private static byte[] CaptureCommittedShadow(
        scoped in AlsRuntimeState state,
        AlsTimelineCursor[] cursors,
        AlsTimelineAuthorityState[] authorities,
        AlsNotifyStateOwnership[] ownership,
        ulong nextOwnerToken)
    {
        var writer = new ArrayBufferWriter<byte>();
        AppendValue(writer, state);
        AppendSpan(writer, cursors);
        AppendSpan(writer, authorities);
        AppendSpan(writer, ownership);
        AppendValue(writer, nextOwnerToken);
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] CapturePreparedShadow(AlsP5PreparedFrame prepared)
    {
        var writer = new ArrayBufferWriter<byte>();
        AppendValue(writer, prepared.OwnerCookie);
        AppendValue(writer, prepared.Identity);
        AppendValue(writer, prepared.BindingDigest);
        AppendValue(writer, prepared.LayoutDigest);
        AppendValue(writer, prepared.ActionGraph);
        AppendValue(writer, prepared.TransitionGraph);
        AppendValue(writer, prepared.Sync);
        AppendSpan(writer, prepared.SyncMappedPlaybacks);
        AppendValue(writer, prepared.LeftIk);
        AppendValue(writer, prepared.RightIk);
        AppendValue(writer, prepared.LeftLock);
        AppendValue(writer, prepared.RightLock);
        AppendValue(writer, prepared.AllowTransitions);
        AppendValue(writer, prepared.TransitionReplacedClosingWeight);
        return writer.WrittenSpan.ToArray();
    }

    private static byte[] CaptureCandidateShadow(ref AlsP5RuntimeScratch scratch)
    {
        var writer = new ArrayBufferWriter<byte>();
        AppendValue(writer, scratch.BaseCapacity);
        AppendValue(writer, scratch.MaximumBaseContributorCount);
        AppendValue(writer, scratch.Control.Length);
        foreach (ref readonly var control in scratch.Control)
        {
            AppendValue(writer, control.OwnerCookie);
            AppendValue(writer, control.PreparedIdentity);
            AppendValue(writer, control.PreparedBindingDigest);
            AppendValue(writer, control.PreparedLayoutDigest);
            AppendValue(writer, control.Phase);
        }
        AppendSpan(writer, scratch.CandidateCursors);
        AppendSpan(writer, scratch.CandidateAuthorities);
        AppendSpan(writer, scratch.CandidateOwnership);
        AppendSpan(writer, scratch.TimelineOccurrences);
        AppendSpan(writer, scratch.ActionTraversalSlices);
        AppendSpan(writer, scratch.TimelinePlaybacks);
        AppendSpan(writer, scratch.SyncPlaybacks);
        AppendSpan(writer, scratch.SyncMappedPlaybacks);
        AppendSpan(writer, scratch.CurveSamples);
        AppendValue(writer, scratch.CandidateActionPlayer);
        AppendValue(writer, scratch.CandidateDynamicTransition);
        AppendValue(writer, scratch.CandidateActionBlendLane);
        AppendValue(writer, scratch.CandidateDynamicTransitionBlendLane);
        AppendValue(writer, scratch.ActionGraph);
        AppendValue(writer, scratch.TransitionGraph);
        AppendValue(writer, scratch.Sync);
        AppendValue(writer, scratch.SyncMappingCount);
        AppendValue(writer, scratch.LeftIk);
        AppendValue(writer, scratch.RightIk);
        AppendValue(writer, scratch.LeftLock);
        AppendValue(writer, scratch.RightLock);
        AppendValue(writer, scratch.AllowTransitions);
        AppendValue(writer, scratch.TransitionReplacedClosingWeight);
        AppendValue(writer, scratch.Events);
        AppendValue(writer, scratch.ActionOutcomes);
        AppendValue(writer, scratch.ActionPlayback);
        AppendValue(writer, scratch.DynamicTransitionSummary);
        AppendValue(writer, scratch.PreparedTransitionBinding);
        AppendValue(writer, scratch.NextOwnerToken);
        AppendValue(writer, scratch.TransitionCooldownBlockedThisFrame);
        return writer.WrittenSpan.ToArray();
    }

    private static void AppendSpan<T>(ArrayBufferWriter<byte> writer, ReadOnlySpan<T> values)
        where T : unmanaged
    {
        AppendValue(writer, values.Length);
        var bytes = MemoryMarshal.AsBytes(values);
        bytes.CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);
    }

    private static void AppendSpan<T>(ArrayBufferWriter<byte> writer, Span<T> values)
        where T : unmanaged => AppendSpan(writer, (ReadOnlySpan<T>)values);

    private static void AppendSpan<T>(ArrayBufferWriter<byte> writer, T[] values)
        where T : unmanaged => AppendSpan(writer, values.AsSpan());

    private static void AppendValue<T>(ArrayBufferWriter<byte> writer, T value)
        where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        MemoryMarshal.Write(writer.GetSpan(size), in value);
        writer.Advance(size);
    }

    private static bool IsAllZero<T>(scoped in T value)
        where T : unmanaged
    {
        var copy = value;
        foreach (var item in MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref copy, 1)))
        {
            if (item != 0) return false;
        }
        return true;
    }

    private static AlsActionDefinition ResolveActionDefinition(
        int definitionId,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var found = false;
        var result = default(AlsActionDefinition);
        foreach (ref readonly var value in runtimeBindings.ActionDefinitions)
        {
            if (value.DefinitionId != definitionId) continue;
            if (found) throw new InvalidDataException("P5A action definition is ambiguous.");
            result = value;
            found = true;
        }
        return found ? result : throw new InvalidDataException("P5A action definition is unresolved.");
    }

    private static AlsActionSegmentBinding ResolveActionSegment(
        int occurrenceHandleId,
        int definitionId,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var found = false;
        var result = default(AlsActionSegmentBinding);
        foreach (ref readonly var value in runtimeBindings.ActionSegments)
        {
            if (value.OccurrenceHandleId != occurrenceHandleId ||
                value.ActionDefinitionId != definitionId) continue;
            if (found) throw new InvalidDataException("P5A action segment is ambiguous.");
            result = value;
            found = true;
        }
        return found ? result : throw new InvalidDataException("P5A action segment is unresolved.");
    }

    private static AlsBasePlaybackDescriptor[] Descriptors(
        JsonArray nodes, IReadOnlyDictionary<string, SourceBinding> sourceMap) =>
        nodes.Select(node =>
        {
            var value = node!.AsObject();
            var source = sourceMap[value["traceSourceId"]!.GetValue<string>()];
            if (source.Entry.SourceKind is not AlsP5OccurrenceSourceKind.Base and
                not AlsP5OccurrenceSourceKind.Turn and not AlsP5OccurrenceSourceKind.Rotate ||
                source.AnimationId < 0)
            {
                throw new InvalidDataException("P5A playback descriptor source is invalid.");
            }
            return new AlsBasePlaybackDescriptor(
                source.Entry.OccurrenceHandleId, source.AnimationId, source.Entry.AuthorityGroupId,
                long.Parse(value["playbackEpoch"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
                value["previousUnwrappedTimeSeconds"]!.GetValue<double>(),
                value["currentUnwrappedTimeSeconds"]!.GetValue<double>(),
                value["frameStartOffsetSeconds"]!.GetValue<double>(),
                value["frameEndOffsetSeconds"]!.GetValue<double>(),
                value["durationSeconds"]!.GetValue<float>(), value["weight"]!.GetValue<float>(),
                Flag(value["loop"]!), Flag(value["activatesAtFrameStart"]!), Flag(value["closesAfterFrame"]!));
        }).ToArray();

    private static AlsP5FrameInput FrameInput(
        JsonObject node,
        AlsBasePlaybackDescriptor[] baseDescriptors,
        AlsBasePlaybackDescriptor[] turnDescriptors,
        AlsBasePlaybackDescriptor[] rotateDescriptors,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var identityNode = node["identity"]!;
        var window = node["window"]!;
        var modes = node["modes"]!;
        var curves = node["p4Curves"]!;
        var request = node["actionRequest"]!;
        var command = Enum.Parse<AlsActionCommand>(request["command"]!.GetValue<string>());
        var actionRequest = AlsActionRequest.None;
        if (command != AlsActionCommand.None)
        {
            var actionTraceSourceId = request["actionTraceSourceId"]!.GetValue<string>();
            var actionSource = sourceMap[actionTraceSourceId];
            if (actionSource.Entry.SourceKind != AlsP5OccurrenceSourceKind.ActionMontage)
            {
                throw new InvalidDataException("P5A action source kind is invalid.");
            }
            var definitionId = runtimeBindings.ActionDefinitions.ToArray().Single(
                value => value.DefinitionId == actionSource.Entry.SourceBindingIndex).DefinitionId;
            var startSectionId = -1;
            if (command == AlsActionCommand.Start)
            {
                var sectionName = request["startSectionName"]!.GetValue<string>();
                var section = plan["sectionMap"]!.AsArray().Single(row =>
                    row!["actionTraceSourceId"]!.GetValue<string>() == actionTraceSourceId &&
                    row["canonicalSectionName"]!.GetValue<string>() == sectionName);
                startSectionId = section!["hostResolution"]!["sectionId"]!.GetValue<int>();
            }
            actionRequest = new AlsActionRequest(
                long.Parse(request["requestId"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
                command, definitionId, startSectionId,
                request["priority"]!.GetValue<int>(), (uint)request["slotGeneration"]!.GetValue<int>());
        }
        return new AlsP5FrameInput(
            new AlsFrameIdentity(
                long.Parse(identityNode["frameId"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
                (uint)identityNode["characterId"]!.GetValue<int>(),
                (uint)identityNode["slotGeneration"]!.GetValue<int>()),
            window["startSeconds"]!.GetValue<double>(), window["endSeconds"]!.GetValue<double>(),
            window["deltaSeconds"]!.GetValue<float>(), (uint)identityNode["slotGeneration"]!.GetValue<int>(),
            actionRequest, Flag(node["cancelActionForRuntimeFailure"]!), Flag(modes["hasInput"]!),
            Enum.Parse<AlsTimelineLocomotionMode>(modes["locomotionMode"]!.GetValue<string>()),
            Enum.Parse<AlsTimelineRotationMode>(modes["rotationMode"]!.GetValue<string>()),
            Enum.Parse<AlsTimelineStance>(modes["stance"]!.GetValue<string>()),
            new AlsP4CurveFrameInput(baseDescriptors, turnDescriptors, rotateDescriptors,
                Enum.Parse<AlsAnimationState>(curves["animationState"]!.GetValue<string>()),
                curves["actionBlendAmount"]!.GetValue<float>(), curves["actionModeBlendAmount"]!.GetValue<float>()));
    }

    private static AlsFrameResult CanonicalP4Result(AlsP5FrameInput input, AlsP5PreparedFrame prepared)
    {
        var result = AlsFrameResult.CreateDefault(input.Identity);
        result.ResolvedLocomotionState = (AlsLocomotionState)input.LocomotionMode;
        result.ActualStance = (AlsStance)input.Stance;
        result.ActualRotationMode = (AlsRotationMode)input.RotationMode;
        result.AnimationState = input.P4Curves.AnimationState;
        result.PlayRate = 1f;
        result.LeftFootIkWeight = prepared.LeftIk;
        result.RightFootIkWeight = prepared.RightIk;
        result.LeftFootLockCurve = prepared.LeftLock;
        result.RightFootLockCurve = prepared.RightLock;
        return result;
    }

    private static AlsDynamicTransitionInput TransitionProbe(JsonObject input)
    {
        var probe = input["transitionProbe"]!;
        var left = probe["left"]!;
        var right = probe["right"]!;
        return new AlsDynamicTransitionInput(
            Enum.Parse<AlsStance>(input["modes"]!["stance"]!.GetValue<string>()), 0f,
            Vector(left["targetMeters"]!), Vector(left["lockMeters"]!), Flag(left["relevant"]!),
            Vector(right["targetMeters"]!), Vector(right["lockMeters"]!), Flag(right["relevant"]!));
    }

    private static Vector3 Vector(JsonNode node) => new(
        node["x"]!.GetValue<float>(), node["y"]!.GetValue<float>(), node["z"]!.GetValue<float>());

    private static byte Flag(JsonNode node) => node.GetValue<bool>() ? (byte)1 : (byte)0;
    private static AlsLocomotionState ParseLocomotion(string value) => Enum.Parse<AlsLocomotionState>(value);

    private static JsonObject PreparedJson(AlsP5PreparedFrame value) => new()
    {
        ["actionGraph"] = LaneGraph(value.ActionGraph), ["transitionGraph"] = LaneGraph(value.TransitionGraph),
        ["syncMappings"] = new JsonArray(value.SyncMappedPlaybacks.ToArray().Select(SyncMapping).ToArray()),
        ["leftIk"] = value.LeftIk, ["rightIk"] = value.RightIk,
        ["leftLock"] = value.LeftLock, ["rightLock"] = value.RightLock,
        ["allowTransitions"] = value.AllowTransitions,
        ["transitionReplacedClosingWeight"] = value.TransitionReplacedClosingWeight,
    };

    private static JsonObject ResultJson(AlsFrameResult value)
    {
        var events = new JsonArray();
        for (var index = 0; index < value.TypedEvents.Count; index++) events.Add(Event(value.TypedEvents[index]));
        var outcomes = new JsonArray();
        for (var index = 0; index < value.ActionOutcomes.Count; index++) outcomes.Add(Outcome(value.ActionOutcomes[index]));
        return new JsonObject
        {
            ["p4CurvePassthrough"] = new JsonObject
            {
                ["leftIk"] = value.LeftFootIkWeight, ["rightIk"] = value.RightFootIkWeight,
                ["leftLock"] = value.LeftFootLockCurve, ["rightLock"] = value.RightFootLockCurve,
            },
            ["sync"] = Sync(value.Sync), ["dynamicTransition"] = Transition(value.DynamicTransition),
            ["actionPlayback"] = Action(value.ActionPlayback), ["events"] = events,
            ["actionOutcomes"] = outcomes, ["p5FailureCode"] = value.P5FailureCode.ToString(),
        };
    }

    private static JsonObject StateJson(
        AlsRuntimeState state,
        AlsTimelineCursor[] cursors,
        AlsTimelineAuthorityState[] authorities,
        AlsNotifyStateOwnership[] ownership,
        ulong token) => new()
    {
        ["actionPlayer"] = ActionPlayer(state.ActionPlayer),
        ["dynamicTransition"] = TransitionState(state.DynamicTransition),
        ["actionBlendLane"] = LaneState(state.ActionBlendLane),
        ["dynamicTransitionBlendLane"] = LaneState(state.DynamicTransitionBlendLane),
        ["timelineCursors"] = new JsonArray(cursors.Select(Cursor).ToArray()),
        ["authorities"] = new JsonArray(authorities.Select(Authority).ToArray()),
        ["notifyOwnership"] = new JsonArray(ownership.Select(Owner).ToArray()),
        ["nextOwnerToken"] = token.ToString("x16", System.Globalization.CultureInfo.InvariantCulture),
    };

    private static JsonObject IdentityJson(AlsFrameIdentity value) => new()
    {
        ["frameId"] = value.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["characterId"] = value.CharacterId,
        ["slotGeneration"] = value.SlotGeneration,
    };

    private static JsonObject ComparableJson(
        int caseIndex,
        int frameIndex,
        scoped in AlsP5FrameInput input,
        scoped in AlsP5PreparedFrame prepared,
        AlsFrameResult result,
        AlsRuntimeState previousState,
        AlsNotifyStateOwnership[] previousOwnership,
        AlsRuntimeState state,
        AlsNotifyStateOwnership[] ownership,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        ValidateCurvePassthrough(prepared, result);
        var actionNormalization = ValidateActionNormalization(
            caseIndex, frameIndex, input, result, previousState, previousOwnership,
            state, ownership, prepared.ActionGraph, plan, sourceMap, runtimeBindings);
        return new JsonObject
        {
            ["curves"] = new JsonObject
            {
                ["leftIk"] = result.LeftFootIkWeight,
                ["rightIk"] = result.RightFootIkWeight,
                ["leftLock"] = result.LeftFootLockCurve,
                ["rightLock"] = result.RightFootLockCurve,
                ["allowTransitions"] = prepared.AllowTransitions,
            },
            ["sync"] = ComparableSync(input, prepared, result.Sync, plan, sourceMap, runtimeBindings),
            ["dynamicTransition"] = ComparableTransition(
                caseIndex, frameIndex, input.Stance, result.DynamicTransition,
                state.DynamicTransition, sourceMap, runtimeBindings),
            ["actionPlayback"] = ComparableAction(
                caseIndex, frameIndex, input, result.ActionPlayback, state.ActionPlayer,
                actionNormalization, plan, sourceMap, runtimeBindings),
            ["events"] = ComparableEvents(
                result, actionNormalization, plan, sourceMap, runtimeBindings),
            ["actionOutcomes"] = ComparableOutcomes(result, sourceMap),
            ["stateAfter"] = ComparableState(
                state, ownership, actionNormalization, plan, sourceMap, runtimeBindings),
        };
    }

    private static void ValidateCurvePassthrough(
        scoped in AlsP5PreparedFrame prepared,
        AlsFrameResult result)
    {
        if (!SameFloat(prepared.LeftIk, result.LeftFootIkWeight) ||
            !SameFloat(prepared.RightIk, result.RightFootIkWeight) ||
            !SameFloat(prepared.LeftLock, result.LeftFootLockCurve) ||
            !SameFloat(prepared.RightLock, result.RightFootLockCurve))
        {
            throw new InvalidDataException("P5A P4 curve passthrough differs from prepared Core evidence.");
        }
    }

    private static JsonObject ComparableSync(
        scoped in AlsP5FrameInput input,
        scoped in AlsP5PreparedFrame prepared,
        AlsSyncResult result,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        if (prepared.SyncMappedPlaybacks.Length == 0)
        {
            if (!IsDefaultSync(prepared.Sync) || !IsDefaultSync(result))
            {
                throw new InvalidDataException("P5A inactive sync result is not the exact default sentinel.");
            }
            return InactiveSync();
        }
        if (prepared.SyncMappedPlaybacks.Length != 1)
        {
            throw new InvalidDataException("P5A comparable sync projection requires one mapped playback.");
        }

        ref readonly var mapped = ref prepared.SyncMappedPlaybacks[0];
        var source = ResolveSource(
            mapped.OccurrenceHandleId, mapped.AnimationId, -1,
            AlsTimelineSourceKind.Animation, sourceMap);
        if (!SameSync(prepared.Sync, result))
        {
            throw new InvalidDataException("P5A finalized sync result differs from prepared Core evidence.");
        }
        var expectedLeader = ResolveExpectedSyncLeader(input, runtimeBindings.SyncGroup.GroupId);
        if (result.GroupId != runtimeBindings.SyncGroup.GroupId ||
            result.LeaderOccurrenceHandleId != expectedLeader.OccurrenceHandleId ||
            result.LeaderAnimationId != expectedLeader.AnimationId ||
            result.LeaderPlaybackEpoch != expectedLeader.PlaybackEpoch)
        {
            throw new InvalidDataException("P5A comparable sync leader evidence is invalid.");
        }
        var markers = ResolveMarkers(mapped, source, plan, runtimeBindings);
        var descriptorFound = false;
        var mappedDescriptor = default(AlsBasePlaybackDescriptor);
        foreach (ref readonly var descriptor in input.P4Curves.Base)
        {
            if (descriptor.OccurrenceHandleId != mapped.OccurrenceHandleId ||
                descriptor.AnimationId != mapped.AnimationId)
            {
                continue;
            }
            if (descriptorFound)
            {
                throw new InvalidDataException("P5A comparable sync mapped descriptor is ambiguous.");
            }
            descriptorFound = true;
            mappedDescriptor = descriptor;
        }
        if (!descriptorFound || mappedDescriptor.PlaybackEpoch != mapped.PlaybackEpoch ||
            !SameFloat(mappedDescriptor.DurationSeconds, mapped.DurationSeconds))
        {
            throw new InvalidDataException("P5A comparable sync mapped descriptor is unresolved.");
        }
        var current = DescribeMarkers(
            markers, mappedDescriptor.CurrentUnwrappedTimeSeconds,
            mapped.CurrentCycle, mapped.DurationSeconds);
        if (result.PreviousMarkerId != current.Previous.RuntimeMarkerId ||
            result.NextMarkerId != current.Next.RuntimeMarkerId ||
            result.Cycle != current.Cycle ||
            !SameFloat(result.Phase, current.Phase) ||
            !SameFloat(result.LeftFootPhase, current.LeftFootPhase) ||
            !SameFloat(result.RightFootPhase, current.RightFootPhase))
        {
            throw new InvalidDataException("P5A comparable sync marker, cycle, or phase evidence is invalid.");
        }
        var retainPhase = false;
        var mappedAdvances = false;
        retainPhase = mappedDescriptor.ClosesAfterFrame != 0;
        mappedAdvances = mappedDescriptor.CurrentUnwrappedTimeSeconds !=
            mappedDescriptor.PreviousUnwrappedTimeSeconds &&
            mappedDescriptor.FrameEndOffsetSeconds > mappedDescriptor.FrameStartOffsetSeconds;
        retainPhase &= mappedAdvances;
        var markerEvaluationTime = mappedAdvances
            ? mapped.PreviousTimeSeconds
            : mapped.PreviousTimeSeconds - input.DeltaTimeSeconds;
        var sharedMarkers = ResolveMarkerPair(markers, markerEvaluationTime);
        return new JsonObject
        {
            ["active"] = true,
            ["leaderTraceSourceId"] = source.TraceSourceId,
            ["activationOrdinal"] = mapped.PlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["previousMarkerStableId"] = sharedMarkers.PreviousStableId,
            ["nextMarkerStableId"] = sharedMarkers.NextStableId,
            ["cycle"] = mapped.PreviousCycle.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["phase"] = retainPhase ? result.Phase : 0f,
            ["leftFootPhase"] = retainPhase ? result.LeftFootPhase : 0f,
            ["rightFootPhase"] = retainPhase ? result.RightFootPhase : 0f,
        };
    }

    private static bool SameSync(AlsSyncResult left, AlsSyncResult right) =>
        left.GroupId == right.GroupId &&
        left.LeaderOccurrenceHandleId == right.LeaderOccurrenceHandleId &&
        left.LeaderAnimationId == right.LeaderAnimationId &&
        left.LeaderPlaybackEpoch == right.LeaderPlaybackEpoch &&
        left.PreviousMarkerId == right.PreviousMarkerId &&
        left.NextMarkerId == right.NextMarkerId &&
        left.Cycle == right.Cycle &&
        SameFloat(left.Phase, right.Phase) &&
        SameFloat(left.LeftFootPhase, right.LeftFootPhase) &&
        SameFloat(left.RightFootPhase, right.RightFootPhase);

    private static bool IsDefaultSync(AlsSyncResult value) =>
        value.GroupId == -1 &&
        value.LeaderOccurrenceHandleId == -1 &&
        value.LeaderAnimationId == -1 &&
        value.LeaderPlaybackEpoch == 0 &&
        value.PreviousMarkerId == -1 &&
        value.NextMarkerId == -1 &&
        value.Cycle == 0 &&
        BitConverter.SingleToInt32Bits(value.Phase) == 0 &&
        BitConverter.SingleToInt32Bits(value.LeftFootPhase) == 0 &&
        BitConverter.SingleToInt32Bits(value.RightFootPhase) == 0;

    private static AlsBasePlaybackDescriptor ResolveExpectedSyncLeader(
        scoped in AlsP5FrameInput input,
        int groupId)
    {
        var found = false;
        var leader = default(AlsBasePlaybackDescriptor);
        var leaderWeight = 0f;
        ConsiderExpectedSyncLeaders(input.P4Curves.Base, groupId,
            1f - input.P4Curves.ActionBlendAmount, ref found, ref leader, ref leaderWeight);
        ConsiderExpectedSyncLeaders(input.P4Curves.TurnBanks, groupId,
            input.P4Curves.ActionBlendAmount * (1f - input.P4Curves.ActionModeBlendAmount),
            ref found, ref leader, ref leaderWeight);
        ConsiderExpectedSyncLeaders(input.P4Curves.RotateBanks, groupId,
            input.P4Curves.ActionBlendAmount * input.P4Curves.ActionModeBlendAmount,
            ref found, ref leader, ref leaderWeight);
        return found
            ? leader
            : throw new InvalidDataException("P5A comparable sync leader descriptor is unresolved.");
    }

    private static void ConsiderExpectedSyncLeaders(
        ReadOnlySpan<AlsBasePlaybackDescriptor> descriptors,
        int groupId,
        float blendWeight,
        ref bool found,
        ref AlsBasePlaybackDescriptor leader,
        ref float leaderWeight)
    {
        foreach (ref readonly var candidate in descriptors)
        {
            if (candidate.AuthorityGroupId != groupId) continue;
            var weight = candidate.Weight * blendWeight;
            if (!found || weight > leaderWeight ||
                weight == leaderWeight &&
                (candidate.AnimationId < leader.AnimationId ||
                 candidate.AnimationId == leader.AnimationId &&
                 (candidate.PlaybackEpoch < leader.PlaybackEpoch ||
                  candidate.PlaybackEpoch == leader.PlaybackEpoch &&
                  candidate.OccurrenceHandleId < leader.OccurrenceHandleId)))
            {
                found = true;
                leader = candidate;
                leaderWeight = weight;
            }
        }
    }

    private static JsonObject InactiveSync() => new()
    {
        ["active"] = false,
        ["leaderTraceSourceId"] = string.Empty,
        ["activationOrdinal"] = "0",
        ["previousMarkerStableId"] = string.Empty,
        ["nextMarkerStableId"] = string.Empty,
        ["cycle"] = "0",
        ["phase"] = 0f,
        ["leftFootPhase"] = 0f,
        ["rightFootPhase"] = 0f,
    };

    private static MarkerProjection[] ResolveMarkers(
        in AlsSyncMappedPlayback mapped,
        SourceBinding source,
        JsonObject plan,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var markers = new List<MarkerProjection>();
        foreach (var markerNode in plan["markerMap"]!.AsArray())
        {
            var marker = markerNode!.AsObject();
            if (marker["traceSourceId"]!.GetValue<string>() != source.TraceSourceId) continue;
            var evidence = marker["canonicalEvidence"]!.AsObject();
            var name = evidence["name"]!.GetValue<string>();
            var sourceIndex = evidence["sourceIndex"]!.GetValue<int>();
            var trackIndex = evidence["trackIndex"]!.GetValue<int>();
            var time = evidence["timeSeconds"]!.GetValue<float>();
            var markerNameId = name switch
            {
                "Left" => runtimeBindings.SyncGroup.LeftMarkerNameId,
                "Right" => runtimeBindings.SyncGroup.RightMarkerNameId,
                _ => throw new InvalidDataException("P5A marker name is invalid."),
            };
            var matchCount = 0;
            var runtimeMarkerId = -1;
            foreach (ref readonly var runtimeMarker in runtimeBindings.SyncMarkers)
            {
                if (runtimeMarker.AnimationId == mapped.AnimationId &&
                    runtimeMarker.MarkerNameId == markerNameId &&
                    runtimeMarker.SourceIndex == sourceIndex &&
                    runtimeMarker.TrackIndex == trackIndex && SameFloat(runtimeMarker.TimeSeconds, time))
                {
                    matchCount++;
                    runtimeMarkerId = runtimeMarker.MarkerId;
                }
            }
            if (matchCount != 1)
            {
                throw new InvalidDataException("P5A marker binding is unresolved or ambiguous.");
            }
            markers.Add(new MarkerProjection(
                runtimeMarkerId, evidence["stableMarkerId"]!.GetValue<string>(), name == "Left", time));
        }
        if (markers.Count != 2 || markers[0].RuntimeMarkerId == markers[1].RuntimeMarkerId)
        {
            throw new InvalidDataException("P5A comparable sync marker pair is incomplete.");
        }
        markers.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
        return markers.ToArray();
    }

    private static MarkerPair ResolveMarkerPair(
        IReadOnlyList<MarkerProjection> markers,
        float evaluationTime)
    {
        var nextIndex = 0;
        while (nextIndex < markers.Count && markers[nextIndex].TimeSeconds <= evaluationTime)
        {
            nextIndex++;
        }
        if (nextIndex == markers.Count) nextIndex = 0;
        var previousIndex = nextIndex == 0 ? markers.Count - 1 : nextIndex - 1;
        var previous = markers[previousIndex];
        var next = markers[nextIndex];
        return new MarkerPair(previous.StableId, next.StableId);
    }

    private static MarkerDescriptor DescribeMarkers(
        IReadOnlyList<MarkerProjection> markers,
        double unwrappedTime,
        long cycle,
        float duration)
    {
        var localTime = unwrappedTime - cycle * (double)duration;
        if (markers.Count != 2 || !double.IsFinite(unwrappedTime) || !double.IsFinite(localTime) ||
            !float.IsFinite(duration) || duration <= 0f || localTime < 0d || localTime >= duration)
        {
            throw new InvalidDataException("P5A comparable sync mapped marker time is invalid.");
        }
        var early = markers[0];
        var late = markers[1];
        MarkerProjection previous;
        MarkerProjection next;
        double previousTime;
        double nextTime;
        if (localTime < early.TimeSeconds)
        {
            previous = late;
            next = early;
            previousTime = (double)late.TimeSeconds - duration;
            nextTime = early.TimeSeconds;
        }
        else if (localTime < late.TimeSeconds)
        {
            previous = early;
            next = late;
            previousTime = early.TimeSeconds;
            nextTime = late.TimeSeconds;
        }
        else
        {
            previous = late;
            next = early;
            previousTime = late.TimeSeconds;
            nextTime = (double)early.TimeSeconds + duration;
        }
        var phaseDouble = (localTime - previousTime) / (nextTime - previousTime);
        if (!double.IsFinite(phaseDouble) || phaseDouble < 0d || phaseDouble >= 1d)
        {
            throw new InvalidDataException("P5A comparable sync marker phase is invalid.");
        }
        var phase = (float)phaseDouble;
        if (phase >= 1f) phase = MathF.BitDecrement(1f);
        phase = phase == 0f ? 0f : phase;
        var left = previous.IsLeft ? phase : 1f - phase;
        var right = previous.IsLeft ? 1f - phase : phase;
        left = left == 0f ? 0f : left;
        right = right == 0f ? 0f : right;
        return new MarkerDescriptor(previous, next, cycle, phase, left, right);
    }

    private static JsonObject ComparableTransition(
        int caseIndex,
        int frameIndex,
        AlsTimelineStance stance,
        AlsDynamicTransitionPlaybackSummary result,
        AlsDynamicTransitionState state,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        if (result.Active == 0)
        {
            if (!IsDefaultTransitionSummary(result) || state.Active != 0)
            {
                throw new InvalidDataException("P5A inactive dynamic transition result is invalid.");
            }
            var expectedQueueFrame = caseIndex is 2 or 3 or 4 && frameIndex == 0;
            if ((state.Queued != 0) != expectedQueueFrame)
            {
                throw new InvalidDataException("P5A dynamic transition queue is present on an invalid frame.");
            }
            if (state.Queued != 0)
            {
                var expectedClip = (stance, state.QueuedFoot) switch
                {
                    (AlsTimelineStance.Standing, AlsTransitionFoot.Left) => runtimeBindings.DynamicTransition.StandingLeft,
                    (AlsTimelineStance.Standing, AlsTransitionFoot.Right) => runtimeBindings.DynamicTransition.StandingRight,
                    (AlsTimelineStance.Crouching, AlsTransitionFoot.Left) => runtimeBindings.DynamicTransition.CrouchingLeft,
                    (AlsTimelineStance.Crouching, AlsTransitionFoot.Right) => runtimeBindings.DynamicTransition.CrouchingRight,
                    _ => throw new InvalidDataException("P5A dynamic transition queue stance is invalid."),
                };
                if (state.AnimationId != -1 || state.PlaybackEpoch != 0 ||
                    BitConverter.SingleToInt32Bits(state.PreviousPlaybackTime) != 0 ||
                    BitConverter.SingleToInt32Bits(state.PlaybackTime) != 0 ||
                    state.CooldownFrames != runtimeBindings.DynamicTransition.CooldownFrames ||
                    state.QueuedAnimationId != expectedClip.AnimationId)
                {
                    throw new InvalidDataException("P5A dynamic transition queued source evidence is invalid.");
                }
                _ = ResolveSource(state.QueuedAnimationId, state.QueuedFoot, sourceMap);
            }
            else if (state.QueuedAnimationId != -1)
            {
                throw new InvalidDataException("P5A inactive dynamic transition retained a queued source.");
            }
            return InactiveTransition();
        }
        if (state.Active == 0 || state.AnimationId != result.AnimationId || state.Foot != result.Foot)
        {
            throw new InvalidDataException("P5A comparable dynamic transition state disagrees with the result.");
        }
        var source = ResolveSource(result.AnimationId, result.Foot, sourceMap);
        return new JsonObject
        {
            ["active"] = true,
            ["traceSourceId"] = source.TraceSourceId,
            ["activationOrdinal"] = state.PlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["foot"] = result.Foot.ToString(),
            ["previousTimeSeconds"] = state.PreviousPlaybackTime,
            ["currentTimeSeconds"] = state.PlaybackTime,
            ["playRate"] = result.PlayRate,
        };
    }

    private static bool IsDefaultTransitionSummary(AlsDynamicTransitionPlaybackSummary value) =>
        value.AnimationId == -1 &&
        value.Foot == AlsTransitionFoot.Left &&
        BitConverter.SingleToInt32Bits(value.BlendSeconds) == 0 &&
        BitConverter.SingleToInt32Bits(value.PlayRate) == 0 &&
        BitConverter.SingleToInt32Bits(value.EffectiveWeight) == 0 &&
        value.Active == 0;

    private static JsonObject InactiveTransition() => new()
    {
        ["active"] = false,
        ["traceSourceId"] = string.Empty,
        ["activationOrdinal"] = "0",
        ["foot"] = "Left",
        ["previousTimeSeconds"] = 0f,
        ["currentTimeSeconds"] = 0f,
        ["playRate"] = 0f,
    };

    private enum ActionNormalization : byte
    {
        None = 0,
        StartBoundary = 1,
        NaturalNotifyEnd = 2,
    }

    private static ActionNormalization ValidateActionNormalization(
        int caseIndex,
        int frameIndex,
        scoped in AlsP5FrameInput input,
        AlsFrameResult result,
        AlsRuntimeState previousState,
        AlsNotifyStateOwnership[] previousOwnership,
        AlsRuntimeState state,
        AlsNotifyStateOwnership[] ownership,
        AlsLaneGraphInstruction actionGraph,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var scheduledStart = caseIndex is 5 or 6 or 7 && frameIndex == 0;
        if ((input.ActionRequest.Command == AlsActionCommand.Start) != scheduledStart)
        {
            throw new InvalidDataException("P5A action start command appeared outside the frozen schedule.");
        }
        if (scheduledStart)
        {
            ValidateActionStartBoundary(
                input, result, previousState, previousOwnership, state, ownership,
                actionGraph, plan, sourceMap, runtimeBindings);
            return ActionNormalization.StartBoundary;
        }

        var naturalNotifyEnd = caseIndex is 5 or 7 && frameIndex == 56;
        if (naturalNotifyEnd)
        {
            ValidateNaturalActionNotifyEnd(
                input, result, previousState, previousOwnership, state, ownership,
                plan, sourceMap, runtimeBindings);
            return ActionNormalization.NaturalNotifyEnd;
        }
        return ActionNormalization.None;
    }

    private static void ValidateActionStartBoundary(
        scoped in AlsP5FrameInput input,
        AlsFrameResult result,
        AlsRuntimeState previousState,
        AlsNotifyStateOwnership[] previousOwnership,
        AlsRuntimeState state,
        AlsNotifyStateOwnership[] ownership,
        AlsLaneGraphInstruction actionGraph,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        if (!IsDefaultActionPlayer(previousState.ActionPlayer) || CountActiveOwners(previousOwnership) != 0)
        {
            throw new InvalidDataException("P5A action start boundary did not begin from an idle action state.");
        }

        var request = input.ActionRequest;
        var definition = ResolveActionDefinition(request.ActionDefinitionId, runtimeBindings);
        if (request.RequestId <= 0 || request.StartSectionId != definition.StartSectionId ||
            request.Priority != definition.Priority || request.SlotGeneration != input.CurrentSlotGeneration ||
            state.ActionPlayer.Playing != 1 || state.ActionPlayer.ActionDefinitionId != definition.DefinitionId ||
            state.ActionPlayer.SectionId != request.StartSectionId ||
            state.ActionPlayer.RequestId != request.RequestId ||
            state.ActionPlayer.LastProcessedRequestId != request.RequestId ||
            state.ActionPlayer.LastProcessedCommandRequestId != request.RequestId ||
            state.ActionPlayer.LastProcessedCommand != AlsActionCommand.Start ||
            state.ActionPlayer.PlaybackEpoch != 1 || state.ActionPlayer.Priority != request.Priority ||
            state.ActionPlayer.Interruptible != definition.Interruptible ||
            BitConverter.SingleToInt32Bits(state.ActionPlayer.PlaybackTime) != 0)
        {
            throw new InvalidDataException("P5A action start player state is invalid.");
        }
        if ((uint)state.ActionPlayer.SegmentBindingIndex >= (uint)runtimeBindings.ActionSegments.Length)
        {
            throw new InvalidDataException("P5A action start segment binding index is invalid.");
        }
        ref readonly var segment = ref runtimeBindings.ActionSegments[state.ActionPlayer.SegmentBindingIndex];
        if (segment.ActionDefinitionId != definition.DefinitionId)
        {
            throw new InvalidDataException("P5A action start segment does not belong to the requested action.");
        }

        ValidateActiveActionPlayback(result.ActionPlayback, state.ActionPlayer, runtimeBindings);
        var playback = result.ActionPlayback;
        if (BitConverter.SingleToInt32Bits(playback.PreviousTime) != 0 ||
            BitConverter.SingleToInt32Bits(playback.CurrentTime) != 0 ||
            BitConverter.SingleToInt32Bits(playback.PreviousClipTime) != 0 ||
            BitConverter.SingleToInt32Bits(playback.CurrentClipTime) != 0 ||
            BitConverter.SingleToInt32Bits(playback.FinalSegmentDeltaSeconds) != 0 ||
            !SameFloat(playback.EffectiveWeight, actionGraph.IncomingEffectiveWeight) ||
            actionGraph.Incoming.Active != 1 ||
            actionGraph.Incoming.OccurrenceHandleId != segment.OccurrenceHandleId ||
            actionGraph.Incoming.AnimationId != segment.AnimationId ||
            actionGraph.Incoming.BindingIndex != state.ActionPlayer.SegmentBindingIndex ||
            actionGraph.Incoming.PlaybackEpoch != playback.PlaybackEpoch ||
            BitConverter.SingleToInt32Bits(actionGraph.Incoming.PreviousClipTime) != 0 ||
            BitConverter.SingleToInt32Bits(actionGraph.Incoming.CurrentClipTime) != 0 ||
            BitConverter.SingleToInt32Bits(actionGraph.Incoming.ContributingDeltaSeconds) != 0 ||
            !SameFloat(actionGraph.Incoming.PlayRate, playback.PlayRate))
        {
            throw new InvalidDataException("P5A action start playback evidence is invalid.");
        }

        if (result.ActionOutcomes.Count != 1)
        {
            throw new InvalidDataException("P5A action start must emit exactly one outcome.");
        }
        var outcome = result.ActionOutcomes[0];
        if (outcome.RequestId != request.RequestId ||
            outcome.ActionDefinitionId != definition.DefinitionId ||
            outcome.PlaybackEpoch != playback.PlaybackEpoch ||
            outcome.ResultCode != AlsActionResultCode.Accepted)
        {
            throw new InvalidDataException("P5A action start outcome is invalid.");
        }

        if (result.TypedEvents.Count != 2)
        {
            throw new InvalidDataException("P5A action start must emit the exact Begin/Tick batch.");
        }
        var begin = result.TypedEvents[0];
        var tick = result.TypedEvents[1];
        var beginMapping = ResolveEvent(
            begin.EventId, begin.OccurrenceHandleId, begin.SourceAnimationId,
            begin.SourceActionId, begin.BoundaryOrdinal, false,
            plan, sourceMap, runtimeBindings);
        var tickMapping = ResolveEvent(
            tick.EventId, tick.OccurrenceHandleId, tick.SourceAnimationId,
            tick.SourceActionId, tick.BoundaryOrdinal, false,
            plan, sourceMap, runtimeBindings);
        ValidateEmittedEvent(begin, beginMapping.Definition);
        ValidateEmittedEvent(tick, tickMapping.Definition);
        if (beginMapping.Source.Entry.SourceKind != AlsP5OccurrenceSourceKind.ActionMontage ||
            tickMapping.Source.Entry.SourceKind != AlsP5OccurrenceSourceKind.ActionMontage ||
            begin.EventId != tick.EventId || begin.Phase != AlsAnimationEventPhase.Begin ||
            tick.Phase != AlsAnimationEventPhase.Tick || begin.EventSequence != 0 ||
            tick.EventSequence != 1 || begin.PlaybackEpoch != playback.PlaybackEpoch ||
            tick.PlaybackEpoch != playback.PlaybackEpoch || begin.PlaybackCycle != 0 ||
            tick.PlaybackCycle != 0 || begin.OwnerToken == 0 || begin.OwnerToken != tick.OwnerToken ||
            BitConverter.SingleToInt32Bits(begin.AnimationTime) != 0 ||
            BitConverter.SingleToInt32Bits(tick.AnimationTime) != 0 ||
            !SameFloat(begin.Weight, playback.EffectiveWeight) ||
            !SameFloat(tick.Weight, playback.EffectiveWeight) ||
            !SameEventPayload(beginMapping.Definition.Payload, begin.Payload) ||
            !SameEventPayload(tickMapping.Definition.Payload, tick.Payload))
        {
            throw new InvalidDataException(
                $"P5A action start event batch is invalid: " +
                $"begin(id={begin.EventId},phase={begin.Phase},sequence={begin.EventSequence}," +
                $"time=0x{BitConverter.SingleToInt32Bits(begin.AnimationTime):x8}," +
                $"weight=0x{BitConverter.SingleToInt32Bits(begin.Weight):x8},token={begin.OwnerToken}); " +
                $"tick(id={tick.EventId},phase={tick.Phase},sequence={tick.EventSequence}," +
                $"time=0x{BitConverter.SingleToInt32Bits(tick.AnimationTime):x8}," +
                $"weight=0x{BitConverter.SingleToInt32Bits(tick.Weight):x8},token={tick.OwnerToken}); " +
                $"playbackWeight=0x{BitConverter.SingleToInt32Bits(playback.EffectiveWeight):x8}.");
        }

        var owner = RequireSingleActiveOwner(ownership);
        ValidateOwnerMatchesEvent(owner, begin, plan, sourceMap, runtimeBindings);
    }

    private static void ValidateNaturalActionNotifyEnd(
        scoped in AlsP5FrameInput input,
        AlsFrameResult result,
        AlsRuntimeState previousState,
        AlsNotifyStateOwnership[] previousOwnership,
        AlsRuntimeState state,
        AlsNotifyStateOwnership[] ownership,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        if (input.ActionRequest.Command != AlsActionCommand.None ||
            previousState.ActionPlayer.Playing != 1 || state.ActionPlayer.Playing != 1 ||
            previousState.ActionPlayer.ActionDefinitionId != state.ActionPlayer.ActionDefinitionId ||
            previousState.ActionPlayer.SectionId != state.ActionPlayer.SectionId ||
            previousState.ActionPlayer.SegmentBindingIndex != state.ActionPlayer.SegmentBindingIndex ||
            previousState.ActionPlayer.RequestId != state.ActionPlayer.RequestId ||
            previousState.ActionPlayer.PlaybackEpoch != state.ActionPlayer.PlaybackEpoch ||
            previousState.ActionPlayer.Priority != state.ActionPlayer.Priority ||
            previousState.ActionPlayer.Interruptible != state.ActionPlayer.Interruptible ||
            !SameFloat(previousState.ActionPlayer.PlaybackTime, result.ActionPlayback.PreviousTime) ||
            !SameFloat(state.ActionPlayer.PlaybackTime, result.ActionPlayback.CurrentTime))
        {
            throw new InvalidDataException("P5A natural notify end action state is invalid.");
        }
        ValidateActiveActionPlayback(result.ActionPlayback, state.ActionPlayer, runtimeBindings);
        if (result.ActionOutcomes.Count != 0 || result.TypedEvents.Count != 2 ||
            CountActiveOwners(ownership) != 0)
        {
            throw new InvalidDataException("P5A natural notify end batch shape is invalid.");
        }

        var priorOwner = RequireSingleActiveOwner(previousOwnership);
        var end = result.TypedEvents[0];
        var trigger = result.TypedEvents[1];
        var endMapping = ResolveEvent(
            end.EventId, end.OccurrenceHandleId, end.SourceAnimationId,
            end.SourceActionId, end.BoundaryOrdinal, false,
            plan, sourceMap, runtimeBindings);
        var triggerMapping = ResolveEvent(
            trigger.EventId, trigger.OccurrenceHandleId, trigger.SourceAnimationId,
            trigger.SourceActionId, trigger.BoundaryOrdinal, false,
            plan, sourceMap, runtimeBindings);
        ValidateEmittedEvent(end, endMapping.Definition);
        ValidateEmittedEvent(trigger, triggerMapping.Definition);

        ref readonly var segment = ref runtimeBindings.ActionSegments[state.ActionPlayer.SegmentBindingIndex];
        var previousClip = (double)segment.AnimationStartTime +
            ((double)result.ActionPlayback.PreviousTime - segment.MontageStartTime) * segment.PlayRate;
        var currentClip = (double)segment.AnimationStartTime +
            ((double)result.ActionPlayback.CurrentTime - segment.MontageStartTime) * segment.PlayRate;
        var expectedEndOffset = MapActionBoundaryOffset(
            (double)endMapping.Definition.TimeSeconds + endMapping.Definition.DurationSeconds,
            result.ActionPlayback.PreviousTime, result.ActionPlayback.CurrentTime,
            input.DeltaTimeSeconds);
        var expectedTriggerOffset = MapActionBoundaryOffset(
            triggerMapping.Definition.TimeSeconds, previousClip, currentClip,
            input.DeltaTimeSeconds);
        if (endMapping.Source.Entry.SourceKind != AlsP5OccurrenceSourceKind.ActionMontage ||
            triggerMapping.Source.Entry.SourceKind != AlsP5OccurrenceSourceKind.ActionSequence ||
            end.Phase != AlsAnimationEventPhase.End || trigger.Phase != AlsAnimationEventPhase.Trigger ||
            end.EventSequence != 0 || trigger.EventSequence != 1 ||
            end.PlaybackEpoch != result.ActionPlayback.PlaybackEpoch ||
            trigger.PlaybackEpoch != result.ActionPlayback.PlaybackEpoch ||
            end.PlaybackCycle != 0 || trigger.PlaybackCycle != 0 ||
            end.OwnerToken == 0 || end.OwnerToken != priorOwner.OwnerToken ||
            trigger.OwnerToken != 0 || !SameFloat(end.AnimationTime, expectedEndOffset) ||
            !SameFloat(trigger.AnimationTime, expectedTriggerOffset) ||
            !SameFloat(end.Weight, result.ActionPlayback.EffectiveWeight) ||
            !SameFloat(trigger.Weight, result.ActionPlayback.EffectiveWeight) ||
            !SameEventPayload(endMapping.Definition.Payload, end.Payload) ||
            !SameEventPayload(triggerMapping.Definition.Payload, trigger.Payload))
        {
            throw new InvalidDataException(
                $"P5A natural notify end event batch is invalid: " +
                $"end(id={end.EventId},kind={endMapping.Source.Entry.SourceKind},phase={end.Phase}," +
                $"sequence={end.EventSequence},time=0x{BitConverter.SingleToInt32Bits(end.AnimationTime):x8}," +
                $"expectedTime=0x{BitConverter.SingleToInt32Bits(expectedEndOffset):x8}," +
                $"weight=0x{BitConverter.SingleToInt32Bits(end.Weight):x8},token={end.OwnerToken}); " +
                $"trigger(id={trigger.EventId},kind={triggerMapping.Source.Entry.SourceKind}," +
                $"phase={trigger.Phase},sequence={trigger.EventSequence}," +
                $"time=0x{BitConverter.SingleToInt32Bits(trigger.AnimationTime):x8}," +
                $"expectedTime=0x{BitConverter.SingleToInt32Bits(expectedTriggerOffset):x8}," +
                $"weight=0x{BitConverter.SingleToInt32Bits(trigger.Weight):x8},token={trigger.OwnerToken}); " +
                $"playbackWeight=0x{BitConverter.SingleToInt32Bits(result.ActionPlayback.EffectiveWeight):x8}.");
        }
        ValidateOwnerMatchesEvent(priorOwner, end, plan, sourceMap, runtimeBindings);
    }

    private static float MapActionBoundaryOffset(
        double boundary,
        double previous,
        double current,
        float frameDeltaSeconds)
    {
        if (!double.IsFinite(boundary) || !double.IsFinite(previous) || !double.IsFinite(current) ||
            current <= previous || boundary < previous || boundary > current ||
            !float.IsFinite(frameDeltaSeconds) || frameDeltaSeconds <= 0f)
        {
            throw new InvalidDataException("P5A action event boundary cannot be mapped into the current frame.");
        }
        var offset = (boundary - previous) / (current - previous) * frameDeltaSeconds;
        var result = offset == 0d ? 0f : (float)offset;
        if (!float.IsFinite(result) || result < 0f || result > frameDeltaSeconds)
        {
            throw new InvalidDataException("P5A action event boundary offset is invalid.");
        }
        return result;
    }

    private static void ValidateActiveActionPlayback(
        AlsActionPlayback playback,
        AlsActionPlayerState state,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        if (playback.Active != 1 || playback.PlaybackEpoch <= 0 ||
            !float.IsFinite(playback.PreviousTime) || !float.IsFinite(playback.CurrentTime) ||
            !float.IsFinite(playback.PreviousClipTime) || !float.IsFinite(playback.CurrentClipTime) ||
            !float.IsFinite(playback.FinalSegmentDeltaSeconds) ||
            !float.IsFinite(playback.EffectiveWeight) || playback.EffectiveWeight < 0f ||
            playback.EffectiveWeight > 1f || playback.PreviousTime < 0f ||
            playback.CurrentTime < playback.PreviousTime || playback.PreviousClipTime < 0f ||
            playback.CurrentClipTime < playback.PreviousClipTime)
        {
            throw new InvalidDataException("P5A active action playback has invalid scalar evidence.");
        }

        var definition = ResolveActionDefinition(playback.ActionDefinitionId, runtimeBindings);
        var segmentMatches = 0;
        var segmentIndex = -1;
        var segment = default(AlsActionSegmentBinding);
        for (var index = 0; index < runtimeBindings.ActionSegments.Length; index++)
        {
            ref readonly var candidate = ref runtimeBindings.ActionSegments[index];
            if (candidate.OccurrenceHandleId != playback.OccurrenceHandleId ||
                candidate.ActionDefinitionId != playback.ActionDefinitionId ||
                candidate.AnimationId != playback.AnimationId || candidate.SegmentId != playback.SegmentId)
            {
                continue;
            }
            segment = candidate;
            segmentIndex = index;
            segmentMatches++;
        }
        var sectionMatches = 0;
        foreach (ref readonly var section in runtimeBindings.ActionSections)
        {
            if (section.ActionDefinitionId == playback.ActionDefinitionId && section.SectionId == playback.SectionId)
            {
                sectionMatches++;
            }
        }
        var expectedPreviousClip = (float)(
            (double)segment.AnimationStartTime +
            ((double)playback.PreviousTime - segment.MontageStartTime) * segment.PlayRate);
        var expectedCurrentClip = (float)(
            (double)segment.AnimationStartTime +
            ((double)playback.CurrentTime - segment.MontageStartTime) * segment.PlayRate);
        if (segmentMatches != 1 || sectionMatches != 1 ||
            !SameFloat(playback.PreviousClipTime, expectedPreviousClip) ||
            !SameFloat(playback.CurrentClipTime, expectedCurrentClip) ||
            !SameFloat(playback.PlayRate, definition.PlayRate * segment.PlayRate) ||
            !SameFloat(playback.BlendSeconds, definition.BlendSeconds))
        {
            throw new InvalidDataException("P5A active action playback binding evidence is invalid.");
        }
        if (state.Playing != 0 &&
            (state.ActionDefinitionId != playback.ActionDefinitionId ||
             state.SectionId != playback.SectionId || state.SegmentBindingIndex != segmentIndex ||
             state.PlaybackEpoch != playback.PlaybackEpoch ||
             !SameFloat(state.PlaybackTime, playback.CurrentTime)))
        {
            throw new InvalidDataException("P5A active action playback disagrees with committed state.");
        }
    }

    private static bool IsDefaultActionPlayback(AlsActionPlayback value) =>
        value.OccurrenceHandleId == -1 && value.ActionDefinitionId == -1 && value.AnimationId == -1 &&
        value.SectionId == -1 && value.SegmentId == -1 && value.PlaybackEpoch == 0 &&
        BitConverter.SingleToInt32Bits(value.PreviousTime) == 0 &&
        BitConverter.SingleToInt32Bits(value.CurrentTime) == 0 &&
        BitConverter.SingleToInt32Bits(value.PreviousClipTime) == 0 &&
        BitConverter.SingleToInt32Bits(value.CurrentClipTime) == 0 &&
        BitConverter.SingleToInt32Bits(value.FinalSegmentDeltaSeconds) == 0 &&
        BitConverter.SingleToInt32Bits(value.PlayRate) == 0 &&
        BitConverter.SingleToInt32Bits(value.BlendSeconds) == 0 &&
        BitConverter.SingleToInt32Bits(value.EffectiveWeight) == 0 && value.Active == 0;

    private static bool IsDefaultActionPlayer(AlsActionPlayerState value) =>
        value.ActionDefinitionId == -1 && value.SectionId == -1 && value.SegmentBindingIndex == -1 &&
        value.RequestId == -1 && value.LastProcessedRequestId == 0 &&
        value.LastProcessedCommandRequestId == -1 && value.LastProcessedCommand == AlsActionCommand.None &&
        value.PlaybackEpoch == 0 && BitConverter.SingleToInt32Bits(value.PlaybackTime) == 0 &&
        value.Priority == 0 && value.Playing == 0 && value.Interruptible == 0;

    private static int CountActiveOwners(AlsNotifyStateOwnership[] ownership)
    {
        var count = 0;
        foreach (var owner in ownership)
        {
            if (owner.Active != 0) count++;
        }
        return count;
    }

    private static AlsNotifyStateOwnership RequireSingleActiveOwner(AlsNotifyStateOwnership[] ownership)
    {
        var found = false;
        var result = default(AlsNotifyStateOwnership);
        foreach (var owner in ownership)
        {
            if (owner.Active == 0) continue;
            if (owner.Active != 1 || found)
            {
                throw new InvalidDataException("P5A notify ownership is not a single canonical owner.");
            }
            found = true;
            result = owner;
        }
        return found
            ? result
            : throw new InvalidDataException("P5A expected one active notify owner.");
    }

    private static void ValidateOwnerMatchesEvent(
        AlsNotifyStateOwnership owner,
        AlsAnimationEvent animationEvent,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        _ = ResolveEvent(
            owner.EventId, owner.OccurrenceHandleId, owner.AnimationId, owner.ActionId,
            owner.BoundaryOrdinal, true, plan, sourceMap, runtimeBindings);
        if (owner.Active != 1 || owner.EventId != animationEvent.EventId ||
            owner.BoundaryOrdinal != animationEvent.BoundaryOrdinal ||
            owner.OccurrenceHandleId != animationEvent.OccurrenceHandleId ||
            owner.AnimationId != animationEvent.SourceAnimationId ||
            owner.ActionId != animationEvent.SourceActionId ||
            owner.PlaybackEpoch != animationEvent.PlaybackEpoch ||
            owner.PlaybackCycle != animationEvent.PlaybackCycle || owner.OwnerToken == 0 ||
            owner.OwnerToken != animationEvent.OwnerToken)
        {
            throw new InvalidDataException(
                $"P5A notify ownership does not match its boundary event: " +
                $"owner(event={owner.EventId},boundary={owner.BoundaryOrdinal}," +
                $"occurrence={owner.OccurrenceHandleId},animation={owner.AnimationId},action={owner.ActionId}," +
                $"epoch={owner.PlaybackEpoch},cycle={owner.PlaybackCycle},token={owner.OwnerToken},active={owner.Active}); " +
                $"event(event={animationEvent.EventId},boundary={animationEvent.BoundaryOrdinal}," +
                $"occurrence={animationEvent.OccurrenceHandleId},animation={animationEvent.SourceAnimationId}," +
                $"action={animationEvent.SourceActionId},epoch={animationEvent.PlaybackEpoch}," +
                $"cycle={animationEvent.PlaybackCycle},token={animationEvent.OwnerToken}).");
        }
    }

    private static void ValidateEmittedEvent(
        AlsAnimationEvent value,
        AlsTimelineEventDefinition definition)
    {
        var stateEvent = definition.DurationSeconds > 0f;
        if (value.Kind != definition.Kind || value.EventSequence < 0 || value.PlaybackEpoch <= 0 ||
            !float.IsFinite(value.AnimationTime) || value.AnimationTime < 0f ||
            !float.IsFinite(value.Weight) || value.Weight < definition.TriggerWeightThreshold ||
            value.Weight > 1f || !SameEventPayloadIgnoringTermination(definition.Payload, value.Payload) ||
            (!stateEvent && (value.Phase != AlsAnimationEventPhase.Trigger || value.OwnerToken != 0)) ||
            (stateEvent && (value.Phase == AlsAnimationEventPhase.Trigger || value.OwnerToken == 0)) ||
            (value.Phase != AlsAnimationEventPhase.End &&
             value.Payload.TerminationReason != definition.Payload.TerminationReason))
        {
            throw new InvalidDataException("P5A emitted event differs from its runtime definition.");
        }
    }

    private static bool SameEventPayload(
        AlsCompactEventPayload expected,
        AlsCompactEventPayload actual) =>
        SameEventPayloadIgnoringTermination(expected, actual) &&
        expected.TerminationReason == actual.TerminationReason;

    private static bool SameEventPayloadIgnoringTermination(
        AlsCompactEventPayload expected,
        AlsCompactEventPayload actual) =>
        expected.SemanticId == actual.SemanticId && expected.EnumValue0 == actual.EnumValue0 &&
        expected.EnumValue1 == actual.EnumValue1 && expected.EnumValue2 == actual.EnumValue2 &&
        SameFloat(expected.ScalarValue0, actual.ScalarValue0) && expected.Flags == actual.Flags;

    private static JsonObject ComparableAction(
        int caseIndex,
        int frameIndex,
        scoped in AlsP5FrameInput input,
        AlsActionPlayback result,
        AlsActionPlayerState state,
        ActionNormalization normalization,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        if (result.Active == 0)
        {
            if (!IsDefaultActionPlayback(result))
            {
                throw new InvalidDataException("P5A inactive action playback is not the exact default sentinel.");
            }
            return InactiveAction();
        }
        if (normalization == ActionNormalization.StartBoundary)
        {
            return InactiveAction();
        }

        ValidateActiveActionPlayback(result, state, runtimeBindings);
        var montage = ResolveActionSource(result.ActionDefinitionId, true, sourceMap);
        var segment = ResolveActionSegmentSource(
            result.ActionDefinitionId, result.SegmentId, result.AnimationId, sourceMap, runtimeBindings);
        var sectionName = ResolveSectionName(
            result.ActionDefinitionId, result.SectionId, montage, plan, runtimeBindings);
        float finalSegmentDelta;
        if (state.Playing != 0)
        {
            if (!SameFloat(result.FinalSegmentDeltaSeconds, input.DeltaTimeSeconds))
            {
                throw new InvalidDataException("P5A active action frame delta differs from the current input.");
            }
            finalSegmentDelta = 0f;
        }
        else if (caseIndex == 5 && frameIndex == 91)
        {
            if (BitConverter.SingleToInt32Bits(result.FinalSegmentDeltaSeconds) != 0x35400000)
            {
                throw new InvalidDataException("P5A natural action closing delta is invalid.");
            }
            finalSegmentDelta = result.FinalSegmentDeltaSeconds;
        }
        else if (caseIndex == 6 && frameIndex == 56)
        {
            if (BitConverter.SingleToInt32Bits(result.FinalSegmentDeltaSeconds) != 0)
            {
                throw new InvalidDataException("P5A cancelled action closing delta is invalid.");
            }
            finalSegmentDelta = 0f;
        }
        else
        {
            throw new InvalidDataException("P5A action closing playback appeared outside the frozen schedule.");
        }

        return new JsonObject
        {
            ["active"] = true,
            ["montageTraceSourceId"] = montage.TraceSourceId,
            ["segmentTraceSourceId"] = segment.TraceSourceId,
            ["activationOrdinal"] = result.PlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["currentSectionName"] = sectionName,
            ["segmentIndex"] = segment.PlanSource["canonicalEvidence"]!["segmentIndex"]!.GetValue<int>(),
            ["previousMontageTimeSeconds"] = result.PreviousTime,
            ["currentMontageTimeSeconds"] = result.CurrentTime,
            ["previousClipTimeSeconds"] = result.PreviousClipTime,
            ["currentClipTimeSeconds"] = result.CurrentClipTime,
            ["finalSegmentDeltaSeconds"] = finalSegmentDelta,
            ["playRate"] = result.PlayRate,
        };
    }

    private static JsonObject InactiveAction() => new()
    {
        ["active"] = false,
        ["montageTraceSourceId"] = string.Empty,
        ["segmentTraceSourceId"] = string.Empty,
        ["activationOrdinal"] = "0",
        ["currentSectionName"] = string.Empty,
        ["segmentIndex"] = -1,
        ["previousMontageTimeSeconds"] = 0f,
        ["currentMontageTimeSeconds"] = 0f,
        ["previousClipTimeSeconds"] = 0f,
        ["currentClipTimeSeconds"] = 0f,
        ["finalSegmentDeltaSeconds"] = 0f,
        ["playRate"] = 0f,
    };

    private static JsonArray ComparableEvents(
        AlsFrameResult result,
        ActionNormalization normalization,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        if (normalization == ActionNormalization.StartBoundary)
        {
            return new JsonArray();
        }
        var projected = new List<ProjectedEvent>();
        for (var index = 0; index < result.TypedEvents.Count; index++)
        {
            var value = result.TypedEvents[index];
            var mapping = ResolveEvent(
                value.EventId, value.OccurrenceHandleId, value.SourceAnimationId,
                value.SourceActionId, value.BoundaryOrdinal, false,
                plan, sourceMap, runtimeBindings);
            ValidateEmittedEvent(value, mapping.Definition);
            if (normalization == ActionNormalization.NaturalNotifyEnd && index == 0)
            {
                continue;
            }
            var node = new JsonObject
            {
                ["traceEventId"] = mapping.TraceEventId,
                ["traceSourceId"] = mapping.Source.TraceSourceId,
                ["activationOrdinal"] = value.PlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["playbackCycle"] = value.PlaybackCycle.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["frameEventOrdinal"] = 0,
                ["boundaryOrdinal"] = value.BoundaryOrdinal,
                ["frameOffsetSeconds"] = value.AnimationTime,
                ["kind"] = value.Kind.ToString(),
                ["phase"] = value.Phase.ToString(),
                ["payload"] = ComparablePayload(value.Payload),
            };
            projected.Add(new ProjectedEvent(
                node, value.AnimationTime, PhaseRank(value.Phase), mapping.TraceEventId));
        }
        projected.Sort(static (left, right) =>
        {
            var comparison = left.FrameOffsetSeconds.CompareTo(right.FrameOffsetSeconds);
            if (comparison != 0) return comparison;
            comparison = left.PhaseRank.CompareTo(right.PhaseRank);
            return comparison != 0
                ? comparison
                : string.CompareOrdinal(left.TraceEventId, right.TraceEventId);
        });
        var resultArray = new JsonArray();
        for (var index = 0; index < projected.Count; index++)
        {
            projected[index].Value["frameEventOrdinal"] = index;
            resultArray.Add(projected[index].Value);
        }
        return resultArray;
    }

    private static int PhaseRank(AlsAnimationEventPhase phase) => phase switch
    {
        AlsAnimationEventPhase.End => 0,
        AlsAnimationEventPhase.Trigger => 1,
        AlsAnimationEventPhase.Begin => 2,
        AlsAnimationEventPhase.Tick => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    private static JsonObject ComparablePayload(AlsCompactEventPayload value) => new()
    {
        ["semanticId"] = value.SemanticId,
        ["enumValue0"] = value.EnumValue0,
        ["enumValue1"] = value.EnumValue1,
        ["enumValue2"] = value.EnumValue2,
        ["scalarValue0"] = value.ScalarValue0,
        ["flags"] = value.Flags,
        ["terminationReason"] = value.TerminationReason.ToString(),
    };

    private static JsonArray ComparableOutcomes(
        AlsFrameResult result,
        IReadOnlyDictionary<string, SourceBinding> sourceMap)
    {
        var array = new JsonArray();
        for (var index = 0; index < result.ActionOutcomes.Count; index++)
        {
            var value = result.ActionOutcomes[index];
            var source = ResolveActionSource(value.ActionDefinitionId, true, sourceMap);
            array.Add(new JsonObject
            {
                ["actionTraceSourceId"] = source.TraceSourceId,
                ["activationOrdinal"] = value.PlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["resultCode"] = value.ResultCode.ToString(),
            });
        }
        return array;
    }

    private static JsonObject ComparableState(
        AlsRuntimeState state,
        AlsNotifyStateOwnership[] ownership,
        ActionNormalization normalization,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var actionSource = state.ActionPlayer.Playing != 0
            ? ResolveActionSource(state.ActionPlayer.ActionDefinitionId, true, sourceMap)
            : null;
        var transitionSource = state.DynamicTransition.Active != 0
            ? ResolveSource(state.DynamicTransition.AnimationId, state.DynamicTransition.Foot, sourceMap)
            : null;
        if (state.DynamicTransition.Queued != 0)
        {
            _ = ResolveSource(
                state.DynamicTransition.QueuedAnimationId,
                state.DynamicTransition.QueuedFoot,
                sourceMap);
        }
        else if (state.DynamicTransition.Active == 0 && state.DynamicTransition.QueuedAnimationId != -1)
        {
            throw new InvalidDataException("P5A idle dynamic transition retained a queued source.");
        }
        var activeOwners = new JsonArray();
        if (normalization != ActionNormalization.StartBoundary)
        {
            foreach (var value in ownership)
            {
                if (value.Active == 0) continue;
                var mapping = ResolveEvent(
                    value.EventId, value.OccurrenceHandleId, value.AnimationId, value.ActionId,
                    value.BoundaryOrdinal, true, plan, sourceMap, runtimeBindings);
                activeOwners.Add(new JsonObject
                {
                    ["traceEventId"] = mapping.TraceEventId,
                    ["traceSourceId"] = mapping.Source.TraceSourceId,
                    ["activationOrdinal"] = value.PlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["playbackCycle"] = value.PlaybackCycle.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
            }
        }
        var transitionFoot = state.DynamicTransition.Active != 0
            ? state.DynamicTransition.Foot
            : state.DynamicTransition.Queued != 0
                ? state.DynamicTransition.QueuedFoot
                : AlsTransitionFoot.Left;
        return new JsonObject
        {
            ["actionPlaying"] = state.ActionPlayer.Playing != 0,
            ["actionTraceSourceId"] = actionSource?.TraceSourceId ?? string.Empty,
            ["actionActivationOrdinal"] = state.ActionPlayer.Playing != 0
                ? state.ActionPlayer.PlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "0",
            ["actionTimeSeconds"] = state.ActionPlayer.Playing != 0 ? state.ActionPlayer.PlaybackTime : 0f,
            ["transitionPlaying"] = state.DynamicTransition.Active != 0,
            ["transitionTraceSourceId"] = transitionSource?.TraceSourceId ?? string.Empty,
            ["transitionActivationOrdinal"] = state.DynamicTransition.Active != 0
                ? state.DynamicTransition.PlaybackEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "0",
            ["transitionTimeSeconds"] = state.DynamicTransition.Active != 0
                ? state.DynamicTransition.PlaybackTime
                : 0f,
            ["transitionCooldownFrames"] = state.DynamicTransition.CooldownFrames,
            ["transitionFoot"] = transitionFoot.ToString(),
            ["activeNotifyStates"] = activeOwners,
        };
    }

    private static SourceBinding ResolveSource(
        int occurrenceHandleId,
        int animationId,
        int actionDefinitionId,
        AlsTimelineSourceKind runtimeKind,
        IReadOnlyDictionary<string, SourceBinding> sourceMap)
    {
        SourceBinding? result = null;
        foreach (var source in sourceMap.Values)
        {
            if (source.Entry.OccurrenceHandleId != occurrenceHandleId) continue;
            var expectedKind = source.Entry.SourceKind switch
            {
                AlsP5OccurrenceSourceKind.ActionMontage => AlsTimelineSourceKind.Montage,
                AlsP5OccurrenceSourceKind.ActionSequence => AlsTimelineSourceKind.MontageSegmentAnimation,
                _ => AlsTimelineSourceKind.Animation,
            };
            if (expectedKind != runtimeKind) continue;
            if (source.ActionDefinitionId >= 0 && source.ActionDefinitionId != actionDefinitionId) continue;
            if (expectedKind != AlsTimelineSourceKind.Montage && source.AnimationId != animationId) continue;
            if (result is not null) throw new InvalidDataException("P5A trace source is ambiguous.");
            result = source;
        }
        return result ?? throw new InvalidDataException("P5A trace source is unresolved.");
    }

    private static SourceBinding ResolveSource(
        int animationId,
        AlsTransitionFoot foot,
        IReadOnlyDictionary<string, SourceBinding> sourceMap)
    {
        SourceBinding? result = null;
        var role = foot == AlsTransitionFoot.Left ? "transition_left_sequence" : "transition_right_sequence";
        foreach (var source in sourceMap.Values)
        {
            if (source.Entry.SourceKind != AlsP5OccurrenceSourceKind.Transition ||
                source.AnimationId != animationId || source.CanonicalRole != role) continue;
            if (result is not null) throw new InvalidDataException("P5A transition source is ambiguous.");
            result = source;
        }
        return result ?? throw new InvalidDataException("P5A transition source is unresolved.");
    }

    private static SourceBinding ResolveActionSource(
        int definitionId,
        bool montage,
        IReadOnlyDictionary<string, SourceBinding> sourceMap)
    {
        var kind = montage ? AlsP5OccurrenceSourceKind.ActionMontage : AlsP5OccurrenceSourceKind.ActionSequence;
        SourceBinding? result = null;
        foreach (var source in sourceMap.Values)
        {
            if (source.Entry.SourceKind != kind || source.ActionDefinitionId != definitionId) continue;
            if (result is not null) throw new InvalidDataException("P5A action source is ambiguous.");
            result = source;
        }
        return result ?? throw new InvalidDataException("P5A action source is unresolved.");
    }

    private static SourceBinding ResolveActionSegmentSource(
        int definitionId,
        int segmentId,
        int segmentAnimationId,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        SourceBinding? result = null;
        foreach (var source in sourceMap.Values)
        {
            if (source.Entry.SourceKind != AlsP5OccurrenceSourceKind.ActionSequence ||
                source.ActionDefinitionId != definitionId || source.SegmentId != segmentId ||
                source.AnimationId != segmentAnimationId) continue;
            if (result is not null) throw new InvalidDataException("P5A action segment source is ambiguous.");
            result = source;
        }
        return result ?? throw new InvalidDataException("P5A action segment source is unresolved.");
    }

    private static string ResolveSectionName(
        int definitionId,
        int sectionId,
        SourceBinding montage,
        JsonObject plan,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var runtimeMatches = 0;
        foreach (ref readonly var section in runtimeBindings.ActionSections)
        {
            if (section.ActionDefinitionId == definitionId && section.SectionId == sectionId) runtimeMatches++;
        }
        if (runtimeMatches != 1)
        {
            throw new InvalidDataException("P5A action section binding is unresolved or ambiguous.");
        }
        string? result = null;
        foreach (var node in plan["sectionMap"]!.AsArray())
        {
            var row = node!.AsObject();
            if (row["actionTraceSourceId"]!.GetValue<string>() != montage.TraceSourceId ||
                row["hostResolution"]!["sectionId"]!.GetValue<int>() != sectionId) continue;
            if (result is not null) throw new InvalidDataException("P5A action section map is ambiguous.");
            result = row["canonicalSectionName"]!.GetValue<string>();
        }
        return result ?? throw new InvalidDataException("P5A action section map is unresolved.");
    }

    private static EventProjection ResolveEvent(
        int eventId,
        int occurrenceHandleId,
        int animationId,
        int actionDefinitionId,
        int boundaryOrdinal,
        bool ownerIdentity,
        JsonObject plan,
        IReadOnlyDictionary<string, SourceBinding> sourceMap,
        scoped in AlsP5RuntimeBindings runtimeBindings)
    {
        var found = false;
        var definition = default(AlsTimelineEventDefinition);
        foreach (ref readonly var value in runtimeBindings.TimelineDefinitions)
        {
            if (value.EventId != eventId) continue;
            if (found) throw new InvalidDataException("P5A event definition is ambiguous.");
            definition = value;
            found = true;
        }
        if (!found || definition.RequiredOccurrenceHandleId != occurrenceHandleId ||
            definition.BoundaryOrdinal != boundaryOrdinal)
        {
            throw new InvalidDataException("P5A event definition is unresolved or inconsistent.");
        }
        if (definition.SourceAnimationId != animationId ||
            definition.SourceActionId != actionDefinitionId)
        {
            throw new InvalidDataException(
                $"P5A emitted event source identity is invalid: event={eventId}, owner={ownerIdentity}, " +
                $"kind={definition.SourceKind}, expectedAnimation={definition.SourceAnimationId}, " +
                $"actualAnimation={animationId}, expectedAction={definition.SourceActionId}, " +
                $"actualAction={actionDefinitionId}.");
        }
        var source = ResolveSource(
            occurrenceHandleId, definition.SourceAnimationId, definition.SourceActionId,
            definition.SourceKind, sourceMap);
        string? traceEventId = null;
        foreach (var node in plan["eventMap"]!.AsArray())
        {
            var row = node!.AsObject();
            if (row["traceSourceId"]!.GetValue<string>() != source.TraceSourceId) continue;
            var evidence = row["canonicalEvidence"]!.AsObject();
            if (evidence["sourceIndex"]!.GetValue<int>() != definition.SourceIndex ||
                evidence["trackIndex"]!.GetValue<int>() != definition.TrackIndex ||
                evidence["boundaryOrdinal"]!.GetValue<int>() != definition.BoundaryOrdinal ||
                !SameFloat(evidence["timeSeconds"]!.GetValue<float>(), definition.TimeSeconds) ||
                !SameFloat(evidence["durationSeconds"]!.GetValue<float>(), definition.DurationSeconds) ||
                !SameFloat(evidence["triggerWeightThreshold"]!.GetValue<float>(), definition.TriggerWeightThreshold) ||
                evidence["kind"]!.GetValue<string>() != definition.Kind.ToString() ||
                evidence["tickMode"]!.GetValue<string>() != definition.TickMode.ToString() ||
                !SamePayload(evidence["payload"]!.AsObject(), definition.Payload))
            {
                continue;
            }
            if (traceEventId is not null) throw new InvalidDataException("P5A event map is ambiguous.");
            traceEventId = row["traceEventId"]!.GetValue<string>();
        }
        return traceEventId is not null
            ? new EventProjection(traceEventId, source, definition)
            : throw new InvalidDataException("P5A event map is unresolved.");
    }

    private static bool SamePayload(JsonObject expected, AlsCompactEventPayload actual) =>
        expected["semanticId"]!.GetValue<int>() == actual.SemanticId &&
        expected["enumValue0"]!.GetValue<int>() == actual.EnumValue0 &&
        expected["enumValue1"]!.GetValue<int>() == actual.EnumValue1 &&
        expected["enumValue2"]!.GetValue<int>() == actual.EnumValue2 &&
        SameFloat(expected["scalarValue0"]!.GetValue<float>(), actual.ScalarValue0) &&
        expected["flags"]!.GetValue<int>() == actual.Flags &&
        expected["terminationReason"]!.GetValue<string>() == actual.TerminationReason.ToString();

    private static bool SameFloat(float left, float right) =>
        BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private static JsonObject LaneGraph(AlsLaneGraphInstruction value) => new()
    {
        ["outgoing"] = LaneSource(value.Outgoing), ["incoming"] = LaneSource(value.Incoming),
        ["laneWeight"] = value.LaneWeight, ["incomingMix"] = value.IncomingMix,
        ["outgoingEffectiveWeight"] = value.OutgoingEffectiveWeight,
        ["incomingEffectiveWeight"] = value.IncomingEffectiveWeight,
    };

    private static JsonObject LaneSource(AlsLaneGraphSource value) => new()
    {
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["animationId"] = value.AnimationId,
        ["bindingIndex"] = value.BindingIndex, ["playbackEpoch"] = value.PlaybackEpoch.ToString(),
        ["previousClipTime"] = value.PreviousClipTime, ["currentClipTime"] = value.CurrentClipTime,
        ["contributingDeltaSeconds"] = value.ContributingDeltaSeconds, ["playRate"] = value.PlayRate,
        ["active"] = value.Active != 0,
    };

    private static JsonObject SyncMapping(AlsSyncMappedPlayback value) => new()
    {
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["animationId"] = value.AnimationId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["durationSeconds"] = value.DurationSeconds,
        ["previousCycle"] = value.PreviousCycle.ToString(), ["currentCycle"] = value.CurrentCycle.ToString(),
        ["previousTimeSeconds"] = value.PreviousTimeSeconds, ["currentTimeSeconds"] = value.CurrentTimeSeconds,
        ["mappedPlayRate"] = value.MappedPlayRate,
    };

    private static JsonObject Sync(AlsSyncResult value) => new()
    {
        ["groupId"] = value.GroupId, ["leaderOccurrenceHandleId"] = value.LeaderOccurrenceHandleId,
        ["leaderAnimationId"] = value.LeaderAnimationId, ["leaderPlaybackEpoch"] = value.LeaderPlaybackEpoch.ToString(),
        ["previousMarkerId"] = value.PreviousMarkerId, ["nextMarkerId"] = value.NextMarkerId,
        ["cycle"] = value.Cycle.ToString(), ["phase"] = value.Phase,
        ["leftFootPhase"] = value.LeftFootPhase, ["rightFootPhase"] = value.RightFootPhase,
    };

    private static JsonObject Transition(AlsDynamicTransitionPlaybackSummary value) => new()
    {
        ["animationId"] = value.AnimationId, ["foot"] = value.Foot.ToString(),
        ["blendSeconds"] = value.BlendSeconds, ["playRate"] = value.PlayRate,
        ["effectiveWeight"] = value.EffectiveWeight, ["active"] = value.Active != 0,
    };

    private static JsonObject Action(AlsActionPlayback value) => new()
    {
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["actionDefinitionId"] = value.ActionDefinitionId,
        ["animationId"] = value.AnimationId, ["sectionId"] = value.SectionId, ["segmentId"] = value.SegmentId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["previousTime"] = value.PreviousTime,
        ["currentTime"] = value.CurrentTime, ["previousClipTime"] = value.PreviousClipTime,
        ["currentClipTime"] = value.CurrentClipTime, ["finalSegmentDeltaSeconds"] = value.FinalSegmentDeltaSeconds,
        ["playRate"] = value.PlayRate, ["blendSeconds"] = value.BlendSeconds,
        ["effectiveWeight"] = value.EffectiveWeight, ["active"] = value.Active != 0,
    };

    private static JsonObject Event(AlsAnimationEvent value) => new()
    {
        ["eventId"] = value.EventId, ["sourceAnimationId"] = value.SourceAnimationId,
        ["sourceActionId"] = value.SourceActionId, ["occurrenceHandleId"] = value.OccurrenceHandleId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["playbackCycle"] = value.PlaybackCycle.ToString(),
        ["ownerToken"] = value.OwnerToken.ToString("x16"), ["eventSequence"] = value.EventSequence.ToString(),
        ["boundaryOrdinal"] = value.BoundaryOrdinal, ["animationTime"] = value.AnimationTime,
        ["weight"] = value.Weight, ["kind"] = value.Kind.ToString(), ["phase"] = value.Phase.ToString(),
        ["payload"] = new JsonObject
        {
            ["semanticId"] = value.Payload.SemanticId, ["enumValue0"] = value.Payload.EnumValue0,
            ["enumValue1"] = value.Payload.EnumValue1, ["enumValue2"] = value.Payload.EnumValue2,
            ["scalarValue0"] = value.Payload.ScalarValue0, ["flags"] = value.Payload.Flags,
            ["terminationReason"] = value.Payload.TerminationReason.ToString(),
        },
    };

    private static JsonObject Outcome(AlsActionOutcome value) => new()
    {
        ["requestId"] = value.RequestId.ToString(), ["actionDefinitionId"] = value.ActionDefinitionId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["resultCode"] = value.ResultCode.ToString(),
    };

    private static JsonObject ActionPlayer(AlsActionPlayerState value) => new()
    {
        ["actionDefinitionId"] = value.ActionDefinitionId, ["sectionId"] = value.SectionId,
        ["segmentBindingIndex"] = value.SegmentBindingIndex, ["requestId"] = value.RequestId.ToString(),
        ["lastProcessedRequestId"] = value.LastProcessedRequestId.ToString(),
        ["lastProcessedCommandRequestId"] = value.LastProcessedCommandRequestId.ToString(),
        ["lastProcessedCommand"] = value.LastProcessedCommand.ToString(), ["playbackEpoch"] = value.PlaybackEpoch.ToString(),
        ["playbackTime"] = value.PlaybackTime, ["priority"] = value.Priority,
        ["playing"] = value.Playing != 0, ["interruptible"] = value.Interruptible != 0,
    };

    private static JsonObject TransitionState(AlsDynamicTransitionState value) => new()
    {
        ["animationId"] = value.AnimationId, ["queuedAnimationId"] = value.QueuedAnimationId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["previousPlaybackTime"] = value.PreviousPlaybackTime,
        ["playbackTime"] = value.PlaybackTime, ["cooldownFrames"] = value.CooldownFrames,
        ["foot"] = value.Foot.ToString(), ["queuedFoot"] = value.QueuedFoot.ToString(),
        ["active"] = value.Active != 0, ["queued"] = value.Queued != 0,
    };

    private static JsonObject LaneState(AlsLaneBlendState value) => new()
    {
        ["outgoingOccurrenceHandleId"] = value.OutgoingOccurrenceHandleId,
        ["outgoingAnimationId"] = value.OutgoingAnimationId, ["outgoingBindingIndex"] = value.OutgoingBindingIndex,
        ["outgoingPlaybackEpoch"] = value.OutgoingPlaybackEpoch.ToString(), ["outgoingClipTime"] = value.OutgoingClipTime,
        ["laneWeight"] = value.LaneWeight, ["incomingMix"] = value.IncomingMix, ["blendSeconds"] = value.BlendSeconds,
        ["visualActive"] = value.VisualActive != 0, ["outgoingActive"] = value.OutgoingActive != 0,
    };

    private static JsonObject Cursor(AlsTimelineCursor value) => new()
    {
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["animationId"] = value.AnimationId,
        ["actionId"] = value.ActionId, ["playbackEpoch"] = value.PlaybackEpoch.ToString(),
        ["consumedUnwrappedTimeSeconds"] = value.ConsumedUnwrappedTimeSeconds,
    };

    private static JsonObject Authority(AlsTimelineAuthorityState value) => new()
    {
        ["groupId"] = value.GroupId, ["occurrenceHandleId"] = value.OccurrenceHandleId,
        ["animationId"] = value.AnimationId, ["actionId"] = value.ActionId,
        ["playbackEpoch"] = value.PlaybackEpoch.ToString(), ["active"] = value.Active != 0,
    };

    private static JsonObject Owner(AlsNotifyStateOwnership value) => new()
    {
        ["eventId"] = value.EventId, ["boundaryOrdinal"] = value.BoundaryOrdinal,
        ["occurrenceHandleId"] = value.OccurrenceHandleId, ["animationId"] = value.AnimationId,
        ["actionId"] = value.ActionId, ["playbackEpoch"] = value.PlaybackEpoch.ToString(),
        ["playbackCycle"] = value.PlaybackCycle.ToString(), ["ownerToken"] = value.OwnerToken.ToString("x16"),
        ["active"] = value.Active != 0,
    };

    private sealed record SourceBinding(
        string TraceSourceId,
        AlsP5OccurrenceLayoutEntry Entry,
        int AnimationId,
        int ActionDefinitionId,
        int SegmentId,
        string CanonicalRole,
        JsonObject PlanSource);

    private sealed record ReplayStorage(
        AlsTimelineCursor[] Cursors,
        AlsTimelineAuthorityState[] Authorities,
        AlsTimelineCursor[] CandidateCursors,
        AlsTimelineAuthorityState[] CandidateAuthorities);

    internal readonly record struct ShadowFinalizeOutcome(
        bool Succeeded,
        AlsP5FailureCode Failure,
        ulong NextOwnerToken,
        AlsRuntimeState NextState,
        AlsFrameResult Result);

    private sealed record MarkerProjection(int RuntimeMarkerId, string StableId, bool IsLeft, float TimeSeconds);
    private sealed record MarkerPair(string PreviousStableId, string NextStableId);
    private sealed record MarkerDescriptor(
        MarkerProjection Previous,
        MarkerProjection Next,
        long Cycle,
        float Phase,
        float LeftFootPhase,
        float RightFootPhase);
    private sealed record EventProjection(
        string TraceEventId,
        SourceBinding Source,
        AlsTimelineEventDefinition Definition);
    private sealed record ProjectedEvent(
        JsonObject Value,
        float FrameOffsetSeconds,
        int PhaseRank,
        string TraceEventId);
}
