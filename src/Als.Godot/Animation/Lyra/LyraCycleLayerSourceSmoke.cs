using System.Globalization;
using System.Text.Json;
using Godot;
using GodotAls.Core.Sync;
using GodotAls.Core.Locomotion;
using FileAccess = Godot.FileAccess;

namespace GodotAls.Animation.Lyra;

public partial class LyraCycleLayerSourceSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Cycle layer sources failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static float Float(JsonElement row, string name) =>
        BitConverter.Int32BitsToSingle(unchecked((int)row.GetProperty(name + "Bits").GetUInt32()));
    private static double Double(JsonElement row, string name) => BitConverter.Int64BitsToDouble(
        unchecked((long)ulong.Parse(row.GetProperty(name + "Bits").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
    private static void Equal(float actual, JsonElement native, string field, string label) =>
        Require(BitConverter.SingleToInt32Bits(actual) == BitConverter.SingleToInt32Bits(Float(native, field)),
            $"{label}/{field}: actual={actual:R} native={Float(native, field):R}");
    internal static void Run(bool includePose = false, bool includeRoot = false, bool includeOrientation = false, bool includeStride = false,
        bool runtime = false, bool observedMain = false, bool completeMain = false, bool mainCycleLean = false)
    {
        Require(!includeStride || includePose && includeRoot && includeOrientation, "Stride requires the complete Cycle Warp chain.");
        Require(!runtime || includeStride, "Original Cycle runtime requires both Warps.");
        const string root = "res://assets/generated/lyra_als/";
        var hiddenReset = OS.GetCmdlineUserArgs().Contains("--lyra-cycle-hidden-reset-smoke");
        Require(!runtime || !hiddenReset, "The bound Cycle oracle has its own hidden-frame coverage.");
        Require(!observedMain || runtime, "Observed Main requires the original Cycle runtime.");
        Require(!completeMain || observedMain, "Complete Main requires the observed Cycle path.");
        var mainComparison = completeMain ? new LyraMainNativeComparison() : null;
        Require(!mainCycleLean || completeMain, "Main Cycle Lean requires full Main update.");
        var variant = mainCycleLean ? "main_cycle_lean" : completeMain ? "main_update_cycle" : observedMain ? "main_observation_cycle" : runtime ? "cycle_runtime" : hiddenReset ? "cycle_layer_hidden_reset" : "cycle_layer";
        using var native = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + variant + (runtime ? "_native.json" : "_native_bits.json")));
        using var requests = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + variant + "_requests.json"));
        using var definitions = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "cycle_source_definitions.json"));
        using var logical = JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "logical_controls/catalog.json"));
        var data = native.RootElement; var staticData = definitions.RootElement;
        using var poseNative = includePose ? JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "cycle_layer_pose_native_v2.json")) : null;
        using var rootNative = includeRoot && !runtime ? JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "root_motion_native.json")) : null;
        using var orientationNative = includeOrientation && !runtime ? JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "orientation_native_v2.json")) : null;
        using var orientationRequests = includeOrientation && !runtime ? JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "orientation_requests.json")) : null;
        using var strideNative = includeStride && !runtime ? JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "stride_native.json")) : null;
        using var strideRequests = includeStride && !runtime ? JsonDocument.Parse(FileAccess.GetFileAsBytes(root + "stride_requests.json")) : null;
        var bank = includePose ? LyraLogicalSourceBank.Load(includeMainLean: mainCycleLean) : null;
        var comparison = bank is null ? null : new LyraCycleLayerPoseComparison(bank, poseNative!.RootElement, includeRoot, includeOrientation, includeStride, runtime, mainCycleLean);
        var rootComparison = includeRoot && !includeOrientation ? new LyraRootMotionComparison(bank!, rootNative!.RootElement) : null;
        var orientationComparison = includeOrientation ? new LyraOrientationComparison(runtime ? data : (strideNative ?? orientationNative)!.RootElement,
            includeStride, runtime, variant + "_requests.json", observedMain ? 3406 : 3255) : null;
        Require(data.GetProperty("schemaVersion").GetInt32() == 1 &&
            data.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + variant + "_requests.json")) &&
            data.GetProperty("contractSha256").GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + "cycle_layer_graph.json")), "Stale Cycle layer fixture.");
        foreach (var dependency in data.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(FileAccess.GetFileAsBytes(root + dependency.Name)), "Changed Cycle layer dependency.");
        var paths = data.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("path").GetString()!).ToArray();
        var targets = logical.RootElement.GetProperty("entries").EnumerateArray().GroupBy(e => e.GetProperty("source").GetString()!)
            .ToDictionary(g => g.Key, g => g.ToArray());
        string Target(string source, string profile)
        {
            var choices = targets[source];
            var slot = profile == "unarmed" ? "hipfire_crouch" : "pistol_crouch_idle";
            return (choices.Length == 1 ? choices[0] : choices.Single(c => c.GetProperty("slot").GetString() == slot)).GetProperty("target").GetString()!;
        }
        var symbols = data.GetProperty("assets").EnumerateArray().SelectMany(a => a.GetProperty("markers").EnumerateArray())
            .Select(m => m.GetProperty("name").GetString()!).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var sequenceList = new List<AlsAssetSyncSequence>(); var markerList = new List<AlsAssetSyncMarker>(); var masks = new ulong[paths.Length];
        foreach (var row in data.GetProperty("assets").EnumerateArray())
        {
            var id = sequenceList.Count; var markers = row.GetProperty("markers").EnumerateArray().ToArray();
            sequenceList.Add(new(id, row.GetProperty("length").GetSingle(), row.GetProperty("rateScale").GetSingle(), markerList.Count, markers.Length));
            foreach (var marker in markers)
            {
                var symbol = Array.IndexOf(symbols, marker.GetProperty("name").GetString()!) + 1;
                masks[id] |= 1UL << symbol; markerList.Add(new(symbol, marker.GetProperty("time").GetSingle()));
            }
        }
        var leanSequenceBase = sequenceList.Count;
        if (mainCycleLean) foreach (var slot in new[] { "main_lean_center", "main_lean_left", "main_lean_right" })
        {
            var d = bank!.Get(slot).Data;
            sequenceList.Add(new(d.Identity.AnimationId, (float)d.PlayLength, 1, 0, 0));
        }
        var sequences = sequenceList.ToArray(); var allMarkers = markerList.ToArray();
        if (mainCycleLean) Array.Resize(ref masks, sequences.Length);
        var sourceCatalog = LyraSourceNodeCatalog.Load(); var inventory = LyraLinkedLayerInventory.Load();
        var graphs = new Dictionary<string, LyraCycleLayerGraph>();
        foreach (var profile in new[] { "base", "unarmed", "unarmed_feminine", "pistol", "pistol_feminine", "rifle", "rifle_feminine", "shotgun", "shotgun_feminine" })
            graphs.Add(profile, LyraCycleLayerGraph.Load(profile, sourceCatalog));
        var frames = 0; var cycleTicks = 0; var hipFireTicks = 0; var hidden = 0; var rejected = 0; var tinyWeighted = 0; var inertia = 0; var boundFrames = 0;
        foreach (var (trace, traceIndex) in data.GetProperty("traces").EnumerateArray().Select((t, i) => (t, i)))
        {
            var observationHost = observedMain && !completeMain ? new LyraMainObservationHost() : null;
            var mainHost = completeMain ? new LyraMainUpdateHost() : null;
            var profileName = trace.GetProperty("profile").GetString()!; var profile = inventory.Get(profileName);
            var graph = graphs[profileName]; var authored = requests.RootElement.GetProperty("traces")[traceIndex];
            var clamp = staticData.GetProperty("clamps").GetProperty(profileName);
            LyraCycleAsset CycleAsset(string group, LyraCardinalDirection direction)
            {
                var path = Target(profile.Cardinal(group, direction)!, profileName); var stats = staticData.GetProperty("assets").GetProperty(path);
                return new(Array.IndexOf(paths, path), Float(stats, "length"), Float(stats, "rootDistance"));
            }
            int HipFireAsset(bool crouching) => Array.IndexOf(paths, Target(profile.Asset(crouching ? "Aim_HipFirePose_Crouch" : "Aim_HipFirePose")!, profileName));
            var host = new LyraCycleLayerSourceHost(graph, 0, 1, CycleAsset, HipFireAsset, sequences, masks, Double(clamp, "clampMin"), Double(clamp, "clampMax"));
            var poseHost = bank is null ? null : new LyraCycleLayerPoseHost(host, bank,
                LyraCycleLayerPosePolicy.Load(profileName, bank), paths.Select(path => logical.RootElement.GetProperty("entries").EnumerateArray()
                    .Single(e => e.GetProperty("target").GetString() == path).GetProperty("slot").GetString()!).ToArray(), includeRoot,
                includeOrientation ? LyraOrientationWarpingPolicy.Load(profileName, bank) : null,
                includeStride ? LyraStrideWarpingPolicy.Load(profileName, bank) : null,
                runtime ? LyraCycleRuntimeBindings.Load(profileName) : null);
            var mainCycleHost = mainCycleLean ? new LyraMainCycleLeanHost(poseHost!, bank!, sequences, leanSequenceBase,
                LyraLinkedLayerContracts.Load().Get(profile.ClassPath).Functions[LyraLayerHook.FullBody_CycleState]) : null;
            var jointHost = mainCycleHost?.Joint ?? (observedMain ? new LyraObservedCycleHost(poseHost!, completeMain) : null);
            var leanComparison = mainCycleLean ? new LyraMainCycleLeanComparison(bank!) : null;
            var poseRows = runtime ? trace.GetProperty("frames").EnumerateArray().Select((r, i) => (Row: r, Index: i))
                .Where(r => r.Row.TryGetProperty("output", out _)).ToDictionary(r => r.Index, r => r.Row)
                : (strideNative ?? orientationNative ?? rootNative ?? poseNative)?.RootElement.GetProperty("traces")[traceIndex].GetProperty("rows").EnumerateArray()
                .ToDictionary(r => r.GetProperty("frame").GetInt32());
            var previousGroups = Array.Empty<AlsAssetSyncBatchGroupHistory>();
            var previousPlayers = Array.Empty<AlsAssetPlayerHistory>(); var previousSamples = Array.Empty<AlsAssetSampleHistory>();
            for (var i = 0; i < trace.GetProperty("frames").GetArrayLength(); i++)
            {
                var row = trace.GetProperty("frames")[i]; var frame = authored.GetProperty("frames")[i];
                var main = observedMain ? default : frame.GetProperty("main");
                var delta = frame.GetProperty("delta").GetSingle();
                var observationInput = default(LyraMainObservationInput);
                LyraMainObservationCandidate? observationCandidate = null;
                var previousObservation = mainHost?.State ?? observationHost?.State;
                var previousTail = mainHost?.Tail;
                var fullInput = default(LyraMainUpdateInput);
                LyraMainUpdateCandidate? fullCandidate = null;
                if (completeMain)
                {
                    var observation = frame.GetProperty("observation");
                    fullInput = LyraMainUpdateSmoke.ReadInput(observation);
                    fullCandidate = mainHost!.Prepare(fullInput, delta);
                    var expected = row.GetProperty("observation"); mainComparison!.Identity = $"{profileName}/{trace.GetProperty("hz")}/{i}";
                    mainComparison.Compare("Main", fullCandidate.State, LyraMainObservationState.Read(expected.GetProperty("after")));
                    mainComparison.Compare("MainTail", fullCandidate.Tail, LyraMainTailState.Read(expected.GetProperty("tailAfter")));
                    mainHost.Cancel(); Require(mainHost.State == previousObservation && mainHost.Tail == previousTail, "Complete Main/Cycle cancellation published history.");
                    var retryMain = mainHost.Prepare(fullInput, delta);
                    Require(retryMain.State == fullCandidate.State && retryMain.Tail == fullCandidate.Tail && retryMain.Observation.Stages.SequenceEqual(fullCandidate.Observation.Stages), "Complete Main/Cycle retry diverged.");
                    fullCandidate = retryMain; observationCandidate = retryMain.Observation;
                }
                else if (observedMain)
                {
                    var observation = frame.GetProperty("observation"); var snapshot = observation.GetProperty("snapshot"); var rotation = snapshot.GetProperty("rotation");
                    observationInput = new(LyraMainObservationState.Vector(snapshot.GetProperty("location")),
                        new(rotation.GetProperty("pitch").GetDouble(), rotation.GetProperty("yaw").GetDouble(), rotation.GetProperty("roll").GetDouble(), observation.GetProperty("first").GetBoolean(), false, false),
                        LyraMainObservationState.Vector(snapshot.GetProperty("velocity")), LyraMainObservationState.Vector(snapshot.GetProperty("acceleration")),
                        snapshot.GetProperty("ground").GetBoolean(), snapshot.GetProperty("crouching").GetBoolean(), snapshot.GetProperty("movementMode").GetInt32(),
                        observation.GetProperty("ads").GetBoolean(), observation.GetProperty("firing").GetBoolean(), observation.GetProperty("rootYaw").GetDouble());
                    observationCandidate = observationHost!.Prepare(observationInput, delta);
                    for (var stage = 0; stage < 6; stage++)
                    {
                        var actual = observationCandidate.Stages[stage]; var expected = LyraMainObservationState.Read(row.GetProperty("observation").GetProperty("stages")[stage]);
                        Require(new[] { actual.Location - expected.Location, actual.Velocity - expected.Velocity, actual.LocalVelocity - expected.LocalVelocity,
                            actual.LocalAcceleration - expected.LocalAcceleration, actual.Pivot - expected.Pivot }.All(v => v.LengthSquared <= 1e-20) &&
                            actual with { Location=expected.Location, Velocity=expected.Velocity, LocalVelocity=expected.LocalVelocity,
                                LocalAcceleration=expected.LocalAcceleration, Pivot=expected.Pivot } == expected, $"Main/Cycle observation {traceIndex}/{i}/{stage}");
                    }
                    observationHost.Cancel(); Require(observationHost.State == previousObservation, "Main/Cycle cancel published observations.");
                    var retryObservation = observationHost.Prepare(observationInput, delta);
                    Require(retryObservation.Stages.SequenceEqual(observationCandidate.Stages), "Main/Cycle observation retry diverged.");
                    observationCandidate = retryObservation;
                }
                var label = $"{profileName}/{trace.GetProperty("hz")}/{i}";
                var direction = observedMain ? observationCandidate!.CycleInput.Direction : main.GetProperty("LocalVelocityDirectionNoOffset").GetInt32() switch
                { 0 => LyraCardinalDirection.Forward, 1 => LyraCardinalDirection.Backward, 2 => LyraCardinalDirection.Left,
                    3 => LyraCardinalDirection.Right, _ => throw new InvalidOperationException("Direction") };
                var input = observedMain ? observationCandidate!.CycleInput : new LyraCycleInput(main.GetProperty("IsCrouching").GetBoolean(), main.GetProperty("GameplayTag_IsADS").GetBoolean(),
                    direction, main.GetProperty("DisplacementSpeed").GetSingle(), main.GetProperty("IsRunningIntoWall").GetBoolean());
                var weight = frame.GetProperty("weight").GetSingle();
                var hipFireWeight = frame.GetProperty("layer").GetProperty("HipFireUpperBodyOverrideWeight").GetDouble();
                var active = frame.GetProperty("active").GetBoolean(); var reset = frame.GetProperty("reinitialize").GetBoolean();
                var warpInput = includeOrientation && active && !runtime ? LyraOrientationComparison.Input(orientationRequests!.RootElement.GetProperty("traces")[traceIndex].GetProperty("frames")[i]) : (AlsOrientationWarpingInput?)null;
                var strideRow = includeStride && active && !runtime ? strideRequests!.RootElement.GetProperty("traces")[traceIndex].GetProperty("frames")[i] : (JsonElement?)null;
                var strideInput = strideRow is null ? (AlsStrideWarpingInput?)null : new AlsStrideWarpingInput(delta,
                    strideRow.Value.GetProperty("speed").GetSingle(), strideRow.Value.GetProperty("alpha").GetSingle(), warpInput!.Value.Component, reset);
                var boundContext = default(LyraCycleWarpContext);
                if (runtime)
                {
                    var rotation = frame.GetProperty("relativeRotation");
                    var relative = new AlsQuaternion(rotation[0].GetDouble(), rotation[1].GetDouble(), rotation[2].GetDouble(), rotation[3].GetDouble());
                    var component = new AlsPrecisePose(default, relative, AlsDoubleVector.One);
                    if (observedMain)
                    {
                        var snapshot = frame.GetProperty("componentInput"); var q = snapshot.GetProperty("rotation"); var p = snapshot.GetProperty("position");
                        component = new(new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()), new(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble()), AlsDoubleVector.One);
                    }
                    boundContext = new(observedMain ? observationCandidate!.State.DirectionAngle : main.GetProperty("LocalVelocityDirectionAngle").GetDouble(),
                        component, relative, i);
                }
                LyraObservedCycleCandidate? jointCandidate = null;
                LyraMainCycleLeanCandidate? mainCycleCandidate = null;
                LyraCycleLayerPoseCandidate? PreparePose()
                {
                    if (observedMain)
                    {
                        if (mainCycleLean)
                        {
                            mainCycleCandidate = mainCycleHost!.Prepare(fullInput, delta, weight, hipFireWeight, active, reset,
                                boundContext.Component, boundContext.RelativeRotation);
                            jointCandidate = mainCycleCandidate.Joint;
                        }
                        else jointCandidate = completeMain ? jointHost!.PrepareFull(fullInput, delta, weight, hipFireWeight, active, reset, boundContext.Component, boundContext.RelativeRotation)
                            : jointHost!.Prepare(observationInput, delta, weight, hipFireWeight, active, reset, boundContext.Component, boundContext.RelativeRotation);
                        if (completeMain) Require(jointCandidate.Main!.State == fullCandidate!.State && jointCandidate.Main.Tail == fullCandidate.Tail, "Owning complete Main/Cycle differed from independent Main.");
                        Require(jointCandidate.Observation.Stages.SequenceEqual(observationCandidate!.Stages), "Owning Main/Cycle host differed from independent Main calculation.");
                        return jointCandidate.Cycle;
                    }
                    return runtime ? poseHost!.PrepareBound(input, delta, weight, hipFireWeight, active, reset, boundContext)
                        : poseHost?.Prepare(input, delta, weight, hipFireWeight, active, reset, orientation: warpInput, stride: strideInput);
                }
                void CancelPose() { if (mainCycleLean) mainCycleHost!.Cancel(); else if (observedMain) jointHost!.Cancel(); else poseHost!.Cancel(); }
                var oldCycle = host.Cycle; var oldHipFire = host.HipFire; var oldBlend = host.BlendWeight;
                var oldOrientation = poseHost?.OrientationState;
                var oldStride = poseHost?.StrideState;
                if (runtime)
                {
                    var manualRejected = false;
                    try { poseHost!.Prepare(input, delta, weight, hipFireWeight, active, reset); }
                    catch (InvalidOperationException) { manualRejected = true; }
                    var contextRejected = false;
                    try { poseHost!.PrepareBound(input, delta, weight, hipFireWeight, active, reset, boundContext with { DirectionAngle = double.NaN }); }
                    catch (ArgumentException) { contextRejected = true; }
                    Require(manualRejected && contextRejected && host.Cycle == oldCycle && host.HipFire == oldHipFire &&
                        poseHost!.OrientationState == oldOrientation && poseHost.StrideState == oldStride, "Invalid bound inputs published source/Warp history.");
                    rejected += 2;
                }
                var cancelledPose = PreparePose();
                var cancelledJoint = jointCandidate;
                var cancelledMainCycle = mainCycleCandidate;
                var cancelled = cancelledPose?.Sources ?? host.Prepare(input, delta, weight, hipFireWeight, active, reset);
                if (poseHost is null) host.Cancel(); else CancelPose();
                Require(host.Cycle == oldCycle && host.HipFire == oldHipFire && host.BlendWeight == oldBlend, "Cancel published layer state.");
                var poseCandidate = PreparePose();
                var candidate = poseCandidate?.Sources ?? host.Prepare(input, delta, weight, hipFireWeight, active, reset);
                Require(candidate.Cycle.State == cancelled.Cycle.State && candidate.HipFire == cancelled.HipFire &&
                    candidate.Players.SequenceEqual(cancelled.Players) && candidate.Groups.SequenceEqual(cancelled.Groups), "Retry changed source traversal.");
                if (runtime && active)
                {
                    Equal(poseCandidate!.Orientation!.Value.LocomotionAngle, row, "orientationAngle", label);
                    Equal(poseCandidate.Orientation.Value.Alpha, row, "orientationAlpha", label);
                    Equal(poseCandidate.Stride!.Value.Speed, row, "strideSpeed", label);
                    Equal(poseCandidate.Stride.Value.Alpha, row, "strideNodeAlpha", label);
                    Require(poseCandidate.Orientation.Value.LocomotionDirection == default, label + "/bound direction"); boundFrames++;
                }
                Equal(candidate.Cycle.Before, row, "before", label); Equal(candidate.Cycle.State.Time, row, "prepared", label);
                Require((candidate.Cycle.BeforeAsset < 0 ? "" : paths[candidate.Cycle.BeforeAsset]) == row.GetProperty("beforeAsset").GetString(), label + "/before asset");
                Require(paths[candidate.Cycle.State.AssetId] == row.GetProperty("asset").GetString(), label + "/Cycle asset");
                Equal(candidate.Cycle.State.PlayRate, row, "playRate", label);
                var inertiaDurations = mainCycleCandidate?.InertiaDurations ?? (candidate.Cycle.InertiaDuration > 0 ? [candidate.Cycle.InertiaDuration] : Array.Empty<float>());
                Require(row.GetProperty("inertia").GetArrayLength() == inertiaDurations.Length, label + "/inertia count");
                for (var request = 0; request < inertiaDurations.Length; request++)
                { Equal(inertiaDurations[request], row.GetProperty("inertia")[request], "duration", label); inertia++; }
                Require(BitConverter.DoubleToInt64Bits(candidate.Cycle.State.StrideAlpha) == BitConverter.DoubleToInt64Bits(Double(row, "strideAlpha")), label + "/stride");
                Equal(candidate.BlendWeight, row, "blendWeight", label); Equal(candidate.CycleWeight, row, "cycleWeight", label);
                Equal(candidate.HipFireWeight, row, "hipFireWeight", label);
                Equal(oldHipFire.Time, row, "hipFireBefore", label); Equal(candidate.HipFire.Time, row, "hipFirePrepared", label);
                Require(candidate.HipFireTicked == row.GetProperty("hipFireActive").GetBoolean(), label + "/HipFire relevance");
                Require((candidate.HipFire.AssetId < 0 ? "" : paths[candidate.HipFire.AssetId]) == row.GetProperty("hipFireAsset").GetString(), label + "/HipFire asset");
                var syncPlayers = mainCycleCandidate?.Players ?? candidate.Players;
                var syncSamples = mainCycleCandidate?.Samples ?? candidate.Samples;
                var syncGroups = mainCycleCandidate?.Groups ?? candidate.Groups;
                var outputs = new AlsAssetPlayerHistory[syncPlayers.Length]; var sampleOutput = new AlsAssetSampleHistory[syncSamples.Length];
                var groups = new AlsAssetSyncBatchGroupHistory[1];
                Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([0], syncGroups, syncPlayers, syncSamples, sequences, allMarkers,
                    previousGroups, previousPlayers, previousSamples, delta, groups, outputs, sampleOutput, out var failure), label + "/Sync: " + failure);
                if (mainCycleLean) mainCycleHost!.Resolve(mainCycleCandidate!, outputs, sampleOutput);
                void CommitJoint()
                { if (mainCycleLean) mainCycleHost!.Commit(mainCycleCandidate!, outputs, sampleOutput); else jointHost!.Commit(jointCandidate!, outputs); }
                void EvaluateJoint()
                { if (mainCycleLean) mainCycleHost!.Evaluate(mainCycleCandidate!, outputs, sampleOutput); else jointHost!.Evaluate(jointCandidate!, outputs); }
                if (active)
                {
                    var output = outputs.Single(o => o.PlayerId == graph.Cycle.Index);
                    Equal(output.Time, row, "time", label); Equal(output.DeltaPrevious, row, "previous", label); Equal(output.Delta, row, "delta", label);
                    Require(output.Marker.PreviousIndex == row.GetProperty("markerPrevious").GetInt32() && output.Marker.NextIndex == row.GetProperty("markerNext").GetInt32(), label + "/Cycle marker");
                    Equal(output.Marker.PreviousDistance, row, "markerPreviousDistance", label); Equal(output.Marker.NextDistance, row, "markerNextDistance", label); cycleTicks++;
                }
                else hidden++;
                if (candidate.HipFireTicked)
                {
                    var output = outputs.Single(o => o.PlayerId == graph.HipFire.Index);
                    Equal(output.Time, row, "hipFireTime", label); Equal(output.DeltaPrevious, row, "hipFirePrevious", label); Equal(output.Delta, row, "hipFireDelta", label);
                    Require(output.Marker.PreviousIndex == row.GetProperty("hipFireMarkerPrevious").GetInt32() && output.Marker.NextIndex == row.GetProperty("hipFireMarkerNext").GetInt32(), label + "/HipFire marker");
                    Equal(output.Marker.PreviousDistance, row, "hipFireMarkerPreviousDistance", label); Equal(output.Marker.NextDistance, row, "hipFireMarkerNextDistance", label);
                    if (candidate.HipFireWeight <= 1e-5f) tinyWeighted++; hipFireTicks++;
                    var bad = outputs.ToArray(); var hipIndex = Array.FindIndex(bad, o => o.PlayerId == graph.HipFire.Index);
                    bad[hipIndex] = bad[hipIndex] with { Epoch = 2 };
                    var rejectedBad = false; try { host.Commit(candidate, bad); } catch (InvalidOperationException) { rejectedBad = true; }
                    Require(rejectedBad && host.Cycle == oldCycle && host.HipFire == oldHipFire, "Late HipFire fault published Cycle."); rejected++;
                }
                var rejectedOld = false; try { host.Commit(cancelled, outputs); } catch (InvalidOperationException) { rejectedOld = true; }
                Require(rejectedOld && host.Cycle == oldCycle && host.HipFire == oldHipFire, "Stale layer candidate accepted."); rejected++;
                if (observedMain)
                {
                    var staleJointRejected = false; try { if (mainCycleLean) mainCycleHost!.Commit(cancelledMainCycle!, outputs, sampleOutput); else jointHost!.Commit(cancelledJoint!, outputs); } catch (InvalidOperationException) { staleJointRejected = true; }
                    Require(staleJointRejected && jointHost!.Observation == previousObservation, "Stale composite candidate published Main history."); rejected++;
                }
                if (poseHost is not null && poseCandidate is not null)
                {
                    if (active)
                    {
                        var rejectedEarly = false; try { if (observedMain) CommitJoint(); else poseHost.Commit(poseCandidate, outputs); } catch (InvalidOperationException) { rejectedEarly = true; }
                        Require(rejectedEarly && host.Cycle == oldCycle, "Pose commit skipped evaluation."); rejected++;
                        if (observedMain) EvaluateJoint(); else poseHost.Evaluate(poseCandidate, outputs);
                        comparison!.Compare(poseRows![i].GetProperty("output"), poseHost, label, mainCycleLean ? mainCycleHost!.Pose : default);
                        if (mainCycleLean) leanComparison!.Compare(row, mainCycleHost!, mainCycleCandidate!, poseHost, label);
                        rootComparison?.Compare(poseRows[i].GetProperty("output"), poseHost, label);
                        orientationComparison?.Compare(poseRows[i].GetProperty("output"), poseHost, label);
                        var pose = poseHost.Pose.ToArray(); var curves = poseHost.Curves.ToArray(); var attributes = poseHost.Attributes.ToArray();
                        var mainPose = mainCycleLean ? mainCycleHost!.Pose.ToArray() : null;
                        var oldLean = mainCycleHost?.LeanState;
                        var rootAttribute = poseHost.RootMotion;
                        var evaluatedCandidate = poseCandidate; CancelPose();
                        Require(!poseHost.HasPose && host.Cycle == oldCycle && host.HipFire == oldHipFire && poseHost.OrientationState == oldOrientation && poseHost.StrideState == oldStride, "Pose cancel published state.");
                        Require(!mainCycleLean || !mainCycleHost!.HasPose && mainCycleHost.LeanState == oldLean, "Main Cycle cancel published Lean history.");
                        poseCandidate = PreparePose()!; candidate = poseCandidate.Sources;
                        if (mainCycleLean) mainCycleHost!.Resolve(mainCycleCandidate!, outputs, sampleOutput);
                        Require(candidate.Players.SequenceEqual(evaluatedCandidate.Sources.Players), "Pose retry changed source tick.");
                        var rejectedEvaluation = false; try { poseHost.Evaluate(evaluatedCandidate, outputs); } catch (InvalidOperationException) { rejectedEvaluation = true; }
                        Require(rejectedEvaluation && !poseHost.HasPose, "Old pose candidate evaluated."); rejected++;
                        if (observedMain) EvaluateJoint(); else poseHost.Evaluate(poseCandidate, outputs);
                        Require(poseHost.Pose.SequenceEqual(pose) && poseHost.Curves.SequenceEqual(curves) && poseHost.Attributes.SequenceEqual(attributes) &&
                            poseHost.RootMotion == rootAttribute, "Evaluated pose retry changed output.");
                        if (mainCycleLean)
                        {
                            Require(mainCycleHost!.Pose.SequenceEqual(mainPose), "Main Cycle pose retry diverged.");
                            var badSamples = sampleOutput.ToArray(); badSamples[^1] = badSamples[^1] with { Time = badSamples[^1].Time + .001f };
                            var rejectedLean = false;
                            try { mainCycleHost.Commit(mainCycleCandidate!, outputs, badSamples); } catch (InvalidOperationException) { rejectedLean = true; }
                            Require(rejectedLean && mainCycleHost.LeanState == oldLean && jointHost!.Observation == previousObservation,
                                "Late Lean sample fault published Main/Cycle history."); rejected++;
                        }
                        var changed = outputs.ToArray(); var cycleIndex = Array.FindIndex(changed, o => o.PlayerId == graph.Cycle.Index);
                        changed[cycleIndex] = changed[cycleIndex] with { Time = changed[cycleIndex].Time > .001f ? changed[cycleIndex].Time - .001f : .001f };
                        var rejectedChanged = false; try { poseHost.Commit(poseCandidate, changed); } catch (InvalidOperationException) { rejectedChanged = true; }
                        Require(rejectedChanged && host.Cycle == oldCycle && host.HipFire == oldHipFire, "Different Sync clock committed an evaluated pose."); rejected++;
                        if (includeRoot)
                        {
                            changed = outputs.ToArray(); changed[cycleIndex] = changed[cycleIndex] with { Delta = changed[cycleIndex].Delta + .001f };
                            var rejectedDelta = false; try { poseHost.Commit(poseCandidate, changed); } catch (InvalidOperationException) { rejectedDelta = true; }
                            Require(rejectedDelta && host.Cycle == oldCycle && host.HipFire == oldHipFire &&
                                poseHost.RootMotion == rootAttribute, "Different root interval committed an evaluated pose."); rejected++;
                        }
                    }
                    else
                    {
                        var rejectedHidden = false; try { poseHost.Evaluate(poseCandidate, outputs); } catch (InvalidOperationException) { rejectedHidden = true; }
                        Require(rejectedHidden && !poseHost.HasPose && !poseRows!.ContainsKey(i), "Hidden Cycle produced a pose."); rejected++;
                    }
                    if (observedMain)
                    {
                        Require((mainHost?.State ?? observationHost!.State) == previousObservation && jointHost!.Observation == previousObservation, "Failed/partial Cycle frame published Main observations.");
                        if (completeMain) { Require(mainHost!.Tail == previousTail && jointHost!.Tail == previousTail, "Partial Cycle published full Main tail."); mainHost.ValidateCommit(fullCandidate!); }
                        else observationHost!.ValidateCommit(observationCandidate!);
                        if (mainCycleLean) mainCycleHost!.ValidateCommit(mainCycleCandidate!, outputs, sampleOutput);
                        else jointHost!.ValidateCommit(jointCandidate!, outputs);
                    }
                    if (observedMain) CommitJoint(); else poseHost.Commit(poseCandidate, outputs);
                    if (completeMain) mainHost!.Commit(fullCandidate!); else if (observedMain) observationHost!.Commit(observationCandidate!);
                    var rejectedDuplicate = false; try { if (observedMain) CommitJoint(); else poseHost.Commit(poseCandidate, outputs); } catch (InvalidOperationException) { rejectedDuplicate = true; }
                    Require(rejectedDuplicate, "Pose committed twice."); rejected++;
                }
                else host.Commit(candidate, outputs);
                Equal(host.Cycle.Time, row, "time", label); Equal(host.HipFire.Time, row, "hipFireTime", label);
                Equal(host.Cycle.DeltaPrevious, row, "previous", label); Equal(host.Cycle.Delta, row, "delta", label);
                Equal(host.HipFire.DeltaPrevious, row, "hipFirePrevious", label); Equal(host.HipFire.Delta, row, "hipFireDelta", label);
                Require(host.Cycle.Marker.PreviousIndex == row.GetProperty("markerPrevious").GetInt32() && host.Cycle.Marker.NextIndex == row.GetProperty("markerNext").GetInt32(), label + "/persistent marker");
                Equal(host.Cycle.Marker.PreviousIndex == -2 ? 0 : host.Cycle.Marker.PreviousDistance, row, "markerPreviousDistance", label);
                Equal(host.Cycle.Marker.NextIndex == -2 ? 0 : host.Cycle.Marker.NextDistance, row, "markerNextDistance", label);
                if (mainCycleLean) leanComparison!.Clock(row.GetProperty("lean"), mainCycleHost!.LeanState, label);
                previousGroups = groups; previousPlayers = outputs; previousSamples = sampleOutput; frames++;
            }
            leanComparison?.Finish();
        }
        Require(frames == 3780 && hipFireTicks == 2205 && hidden == 252 && tinyWeighted == 225 && (observedMain ? inertia > 0 : inertia == 108), "Missing Cycle source closure coverage.");
        Require(!runtime || boundFrames == 3528, "Missing original Cycle exposed-input coverage.");
        GD.Print($"LYRA_CYCLE_LAYER_SOURCES_GODOT_OK hidden_reset={hiddenReset} graphs={graphs.Count} frames={frames} cycle_ticks={cycleTicks} hipfire_ticks={hipFireTicks} hidden={hidden} tiny_weighted={tinyWeighted} inertia={inertia} rejected={rejected} exact_bits=true");
        if (runtime) GD.Print($"LYRA_CYCLE_BINDINGS_GODOT_OK frames={boundFrames} angle=Main.LocalVelocityDirectionAngle speed=Main.DisplacementSpeed alpha=freshCallback doubleToFloat=nativePins retry=true");
        comparison?.Finish();
        rootComparison?.Finish();
        orientationComparison?.Finish();
        if (mainCycleLean) GD.Print($"LYRA_MAIN_CYCLE_LEAN_GODOT_OK frames={frames} poseFrames={boundFrames} mainScalarsAndSpring=exactBits vectorCm={mainComparison!.MaxVector:R} retry=true sharedCommit=true production=false mainGraph=false");
        else if (completeMain) GD.Print($"LYRA_MAIN_UPDATE_CYCLE_GODOT_OK frames={frames} poseFrames={boundFrames} mainScalarsAndSpring=exactBits vectorCm={mainComparison!.MaxVector:R} retry=true sharedCommit=true production=false mainGraph=false");
        else if (observedMain) GD.Print($"LYRA_MAIN_OBSERVATION_CYCLE_GODOT_OK frames={frames} stages={frames * 6} poseFrames={boundFrames} observations=calculated callback=observedMain retry=true sharedCommit=true production=false wholeMain=false");
    }
}
