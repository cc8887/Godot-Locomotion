using System.Globalization;
using System.Text.Json;
using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Sync;

namespace GodotAls.Animation.Lyra;

public partial class LyraMainLeanCompositionSmoke : Node
{
    public override void _Ready()
    {
        try { Run(); GetTree().Quit(); }
        catch (Exception error) { GD.PushError("Main Lean composition failed: " + error); GetTree().Quit(1); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Double(JsonElement row, string field, double value)
    {
        var expected = ulong.Parse(row.GetProperty(field + "Bits").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        Require(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)) == expected, $"Rotation {field} actual={value:R} expected={row.GetProperty(field)}");
    }
    private static void Rotation(JsonElement row, LyraMainRotationState value)
    {
        Double(row, "pitch", value.Pitch); Double(row, "yaw", value.Yaw); Double(row, "roll", value.Roll);
        Double(row, "yawDelta", value.YawDelta); Double(row, "yawSpeed", value.YawSpeed); Double(row, "angle", value.LeanAngle);
    }
    private static void Single(JsonElement row, string field, float value)
        => Require(unchecked((uint)BitConverter.SingleToInt32Bits(value)) == row.GetProperty(field + "Bits").GetUInt32(), "Lean " + field);
    private static void Same(LyraMainLeanCompositionResolved first, LyraMainLeanCompositionResolved second)
    {
        Require(first.Candidate.Rotation == second.Candidate.Rotation, "Rotation retry/update-only changed.");
        for (var i = 0; i < 3; i++)
        {
            var a = first.Lean.States[i]; var b = second.Lean.States[i];
            Require(a with { Samples = [] } == b with { Samples = [] } && a.Samples.SequenceEqual(b.Samples), "Lean retry/update-only changed.");
        }
    }
    private static void Run()
    {
        var bank = LyraLogicalSourceBank.Load(includeMainLean: true);
        var requestBytes = Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "composition_v2_requests.json");
        using var requests = JsonDocument.Parse(requestBytes);
        using var native = JsonDocument.Parse(Godot.FileAccess.GetFileAsBytes(LyraLogicalSourceBank.MainLeanRoot + "composition_v2_native.json"));
        var data = native.RootElement;
        Require(data.GetProperty("schemaVersion").GetInt32() == 2 && data.GetProperty("requestSha256").GetString() == LyraLogicalSourceBank.Sha(requestBytes), "Stale composition requests.");
        foreach (var dependency in data.GetProperty("dependencies").EnumerateObject())
            Require(dependency.Value.GetString() == LyraLogicalSourceBank.Sha(Godot.FileAccess.GetFileAsBytes("res://assets/generated/lyra_als/" + dependency.Name)), "Stale composition dependency.");
        var sequences = new[] { "main_lean_center", "main_lean_left", "main_lean_right" }.Select(bank.Get)
            .Select(s => new AlsAssetSyncSequence(s.Data.Identity.AnimationId, (float)s.Data.PlayLength, 1, 0, 0)).ToArray();
        var slots = requests.RootElement.GetProperty("baseSlots").EnumerateArray().Select(v => v.GetString()!).ToArray();
        Require(slots.SequenceEqual(new[] { "jog_fwd_pivot", "jog_fwd_cycle", "jog_fwd_start" }), "Wrong actual phase/base order.");
        var samplers = slots.Select(bank.CreateSampler).ToArray();
        var rootSamplers = slots.Select(bank.Get).Select(d => new AlsRawRootMotionIntervalSampler(d.Data, bank.Reference[0], d.NormalizedRootMotionScale)).ToArray();
        var basis = Enumerable.Range(0, 3).Select(_ => new AlsPrecisePose[81]).ToArray();
        var baseCurves = Enumerable.Range(0, 3).Select(_ => new LyraCurveSample[bank.Curves.Names.Length]).ToArray();
        var baseAttributes = Enumerable.Range(0, 3).Select(_ => new LyraAttributeSample[bank.Curves.Attributes.Layout.Length]).ToArray();
        var output = Enumerable.Range(0, 3).Select(_ => new AlsPrecisePose[81]).ToArray();
        var curves = Enumerable.Range(0, 3).Select(_ => new LyraCurveSample[bank.Curves.Names.Length]).ToArray();
        var attributes = Enumerable.Range(0, 3).Select(_ => new LyraAttributeSample[bank.Curves.Attributes.Layout.Length]).ToArray();
        var rootValues = new LyraRootMotionAttribute[3]; var composedRoots = new LyraRootMotionAttribute[3];
        double maxP = 0, maxQ = 0, maxS = 0, rootP = 0, rootQ = 0, rootS = 0;
        var frames = 0; var ticks = 0; var rejected = 0; var rootPresent = 0; var rootIdentity = 0; var curvesCompared = 0; var attributesCompared = 0;
        void Reject(Action action)
        {
            try { action(); } catch (InvalidOperationException) { rejected++; return; } catch (ArgumentException) { rejected++; return; }
            throw new InvalidOperationException("Invalid composition operation accepted.");
        }
        var nativeTraces = data.GetProperty("traces"); var inputs = requests.RootElement.GetProperty("traces");
        for (var traceIndex = 0; traceIndex < 3; traceIndex++)
        {
            var host = new LyraMainLeanCompositionHost(bank, sequences, 1000, 9000, 0, 1);
            var sparse = new LyraMainLeanCompositionHost(bank, sequences, 1000, 9000, 0, 1);
            var rows = nativeTraces[traceIndex].GetProperty("frames"); var inputRows = inputs[traceIndex].GetProperty("frames");
            LyraMainLeanCompositionResolved? old = null;
            for (var index = 0; index < rows.GetArrayLength(); index++)
            {
                var frame = inputRows[index]; var row = rows[index]; var snapshot = frame.GetProperty("actorSnapshot");
                var delta = frame.GetProperty("delta").GetSingle();
                var input = new LyraMainRotationInput(snapshot[0].GetDouble(), snapshot[1].GetDouble(), snapshot[2].GetDouble(),
                    frame.GetProperty("first").GetBoolean(), frame.GetProperty("crouchingAtRotation").GetBoolean(), frame.GetProperty("adsAtRotation").GetBoolean());
                var active = frame.GetProperty("active").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
                var weights = frame.GetProperty("weights").EnumerateArray().Select(v => v.GetSingle()).ToArray();
                var initialize = frame.GetProperty("initialize").EnumerateArray().Select(v => v.GetBoolean()).ToArray();
                var order = frame.GetProperty("order").EnumerateArray().Select(v => v.GetInt32()).ToArray();
                var before = host.Rotation; var leanBefore = host.LeanStates;
                Rotation(row.GetProperty("rotationBefore"), before);
                LyraMainLeanCompositionResolved Execute(LyraMainLeanCompositionHost owner)
                {
                    var prepared = owner.Prepare(input, delta, weights, active, initialize, order);
                    var collected = owner.CollectAtCommonSync(prepared);
                    var playerOutput = new AlsAssetPlayerHistory[collected.Players.Length]; var sampleOutput = new AlsAssetSampleHistory[collected.Samples.Length];
                    Require(AlsSyncRuntime.TryEvaluateAssetSyncBatch([], Enumerable.Repeat(-1, collected.Players.Length).ToArray(),
                        collected.Players, collected.Samples, sequences, [], [], [], [], delta, [], playerOutput, sampleOutput, out var failure), "Composition Sync: " + failure);
                    return owner.Resolve(prepared, collected, playerOutput, sampleOutput);
                }
                var result = Execute(host); Rotation(row.GetProperty("rotation"), result.Candidate.Rotation);
                var expectedNodes = row.GetProperty("nodes");
                for (var node = 0; node < 3; node++)
                {
                    var expected = expectedNodes[node]; var state = result.Lean.States[node];
                    Single(expected, "time", state.Time); Single(expected, "pin", state.Pin); Single(expected, "cachedWeight", state.CachedWeight);
                    Single(expected, "previous", state.DeltaPrevious); Single(expected, "delta", state.Delta);
                    Require(state.Samples.Length == expected.GetProperty("samples").GetArrayLength(), "Composition sample inventory.");
                    for (var sample = 0; sample < state.Samples.Length; sample++)
                    {
                        var value = state.Samples[sample]; var e = expected.GetProperty("samples")[sample];
                        Require(value.Weight.SampleId == e.GetProperty("index").GetInt32(), "Composition sample order.");
                        Single(e, "weight", value.Weight.Weight); Single(e, "weightRate", value.Weight.WeightRate);
                        Single(e, "time", value.Clock.Time); Single(e, "previous", value.Clock.PreviousTime);
                        Single(e, "deltaPrevious", value.Clock.DeltaPrevious); Single(e, "delta", value.Clock.Delta);
                    }
                    if (!active[node]) continue;
                    Single(expected, "alpha", 1); Single(expected, "baseWeight", weights[node]);
                    samplers[node].Sample(frame.GetProperty("baseTimes")[node].GetDouble(), basis[node], baseCurves[node], baseAttributes[node]);
                    // Original GetAnimationPose defaults bExtractWithRootMotionProvider=true:
                    // disabled interval override still produces a present identity attribute.
                    rootValues[node] = bank.Get(slots[node]).EnableRootMotion
                        ? new(frame.GetProperty("generateRootMotion")[node].GetBoolean()
                            ? rootSamplers[node].Extract(frame.GetProperty("baseRootPrevious")[node].GetSingle(), frame.GetProperty("baseRootDelta")[node].GetSingle(), true)
                            : AlsPrecisePose.Identity, true) : default;
                    LyraLogicalSourceSmoke.Compare(expected.GetProperty("base").GetProperty("pose"), basis[node], ref maxP, ref maxQ, ref maxS, "compositionBase", index, "rawSource");
                }
                Parallel.For(0, 3, node =>
                {
                    if (active[node]) composedRoots[node] = host.Evaluate(result, node, basis[node], baseCurves[node], baseAttributes[node], rootValues[node], output[node], curves[node], attributes[node]);
                });
                for (var node = 0; node < 3; node++)
                {
                    var captured = node;
                    if (!active[node]) { Reject(() => host.Evaluate(result, captured, basis[captured], baseCurves[captured], baseAttributes[captured], default, output[captured], curves[captured], attributes[captured])); continue; }
                    var expected = expectedNodes[node].GetProperty("output");
                    LyraLogicalSourceSmoke.Compare(expected.GetProperty("pose"), output[node], ref maxP, ref maxQ, ref maxS, "MainApplyAdditive", index, "compiledRoot");
                    Require(curves[node].SequenceEqual(baseCurves[node]) && attributes[node].SequenceEqual(baseAttributes[node]) && composedRoots[node] == rootValues[node], "Main metadata forwarding changed.");
                    Metadata(expected, bank, curves[node], attributes[node], ref curvesCompared, ref attributesCompared);
                    var present = expected.TryGetProperty("rootMotion", out var nativeRoot);
                    Require(present == composedRoots[node].Present, "Main root attribute presence.");
                    if (present)
                    {
                        LyraLogicalSourceSmoke.Compare(JsonSerializer.SerializeToElement(new[] { nativeRoot }), new[] { composedRoots[node].Value }, ref rootP, ref rootQ, ref rootS, "MainRoot", index, "passthrough");
                        rootPresent++; if (composedRoots[node].Value == AlsPrecisePose.Identity) rootIdentity++;
                    }
                    var saved = output[node].ToArray();
                    Reject(() => host.Evaluate(result, captured, basis[captured], baseCurves[captured], baseAttributes[captured], default, basis[captured], curves[captured], attributes[captured]));
                    var poison = baseCurves[node].ToArray(); poison[0] = new(float.NaN, true);
                    Reject(() => host.Evaluate(result, captured, basis[captured], poison, baseAttributes[captured], default, output[captured], curves[captured], attributes[captured]));
                    Require(saved.SequenceEqual(output[node]), "Failed Main evaluation published a partial pose."); ticks++;
                    Require(!host.Evaluate(result, node, basis[node], baseCurves[node], baseAttributes[node], default,
                        output[node], curves[node], attributes[node]).Present, "Main invented an absent input root attribute.");
                }
                host.Cancel(); Require(before == host.Rotation && leanBefore.Equals(host.LeanStates), "Cancelled Main transaction published history.");
                Reject(() => host.Commit(result));
                var retry = Execute(host); Same(result, retry);
                for (var node = 0; node < 3; node++) if (active[node])
                {
                    host.Evaluate(retry, node, basis[node], baseCurves[node], baseAttributes[node], rootValues[node], output[node], curves[node], attributes[node]);
                    LyraLogicalSourceSmoke.Compare(expectedNodes[node].GetProperty("output").GetProperty("pose"), output[node],
                        ref maxP, ref maxQ, ref maxS, "MainApplyAdditiveRetry", index, "compiledRoot");
                }
                host.Commit(retry); Reject(() => host.Commit(retry)); if (old is not null) { var retired = old; Reject(() => host.Commit(retired)); } old = retry;
                var sparseResult = Execute(sparse); Same(retry, sparseResult);
                if (index % 17 == 0) for (var node = 0; node < 3; node++) if (active[node])
                {
                    sparse.Evaluate(sparseResult, node, basis[node], baseCurves[node], baseAttributes[node], rootValues[node], output[node], curves[node], attributes[node]);
                    LyraLogicalSourceSmoke.Compare(expectedNodes[node].GetProperty("output").GetProperty("pose"), output[node],
                        ref maxP, ref maxQ, ref maxS, "MainApplyAdditiveSparse", index, "compiledRoot");
                }
                sparse.Commit(sparseResult); frames++;
            }
        }
        Require(frames == 2100 && ticks == 2121 && rootPresent > 0 && rootIdentity > 0, "Incomplete Main composition coverage.");
        GD.Print($"LYRA_MAIN_LEAN_COMPOSITION_GODOT_OK frames={frames} ticks={ticks} bones={ticks * 81} curves={curvesCompared} attributes={attributesCompared} rootPresent={rootPresent} rootIdentity={rootIdentity} rejected={rejected} " +
            $"positionCm={maxP} quaternion={maxQ} scale={maxS} rootPositionCm={rootP} rootQuaternion={rootQ} rootScale={rootS} rotationAndClocks=exactBits retry=true updateOnly=true parallel=true production=false wholeMain=false");
    }
    private static void Metadata(JsonElement expected, LyraLogicalSourceBank bank, LyraCurveSample[] curves, LyraAttributeSample[] attributes, ref int curveCount, ref int attributeCount)
    {
        var rows = expected.GetProperty("curves"); var seen = 0;
        for (var i = 0; i < curves.Length; i++)
        {
            var present = rows.TryGetProperty(bank.Curves.Names[i], out var row); var value = curves[i];
            Require(value.Present == present && (!present || BitConverter.SingleToInt32Bits(value.Value) == BitConverter.SingleToInt32Bits(row.GetProperty("value").GetSingle()) && value.Flags == row.GetProperty("flags").GetUInt32()), "Main curve presence/value/flags.");
            if (present) { seen++; curveCount++; }
        }
        Require(seen == rows.EnumerateObject().Count(), "Unrepresented Main curve.");
        var attributeRows = expected.GetProperty("attributes").EnumerateArray().ToArray(); seen = 0;
        for (var i = 0; i < attributes.Length; i++)
        {
            var id = bank.Curves.Attributes.Layout[i];
            var matches = attributeRows.Where(a => a.GetProperty("name").GetString() == id.Name && a.GetProperty("type").GetString() == id.Type && a.GetProperty("namespace").GetString() == id.Namespace && a.GetProperty("bone").GetString()!.Equals(id.Bone, StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(matches.Length <= 1 && attributes[i].Present == (matches.Length == 1) && (!attributes[i].Present || attributes[i].Value == matches[0].GetProperty("value").GetInt32()), "Main attribute identity/value/presence.");
            if (attributes[i].Present) { seen++; attributeCount++; }
        }
        Require(seen == attributeRows.Length, "Unrepresented Main attribute.");
    }
}
