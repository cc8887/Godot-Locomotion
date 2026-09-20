using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsAdditiveSlotNativeTests(Xunit.Abstractions.ITestOutputHelper log)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActualTransitionLifecycleAndSlotMixMatchIndependentUnreal(bool useNativeWeights)
    {
        var path = Environment.GetEnvironmentVariable("ALS_ADDITIVE_SLOT_ORACLE") ??
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "P3", "v4_additive_slot_native.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path)); var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var assets = root.GetProperty("assets").EnumerateArray().Select((a, id) => new AlsSequenceMontageAsset(
            id, new(a.GetProperty("slot").GetInt32()), a.GetProperty("group").GetInt32(), N(a,"length"), a.GetProperty("additive").GetInt32())).ToArray();
        Assert.Equal(new[] {2,2,0,0}, assets.Select(a => a.AdditiveType));
        Assert.Equal(new[] {3,3,3,0}, assets.Select(a => a.Slot.Id));
        Assert.True(root.GetProperty("assets")[2].GetProperty("syntheticSlotAssignment").GetBoolean());
        var parents = root.GetProperty("parents").EnumerateArray().Select(p => p.GetInt32()).ToArray();
        var boneNames = root.GetProperty("bones").EnumerateArray().Select(p => p.GetString()!).ToArray();
        Assert.Equal(parents.Length, boneNames.Length); Assert.True(parents.Length > 70);
        var names = new SortedSet<string>(StringComparer.Ordinal); AddCurveNames(root.GetProperty("base"));
        foreach (var trace in root.GetProperty("cases").EnumerateArray())
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                AddCurveNames(row.GetProperty("result"));
                foreach (var e in row.GetProperty("evaluation").EnumerateArray()) if(e.TryGetProperty("sample",out var sample)) AddCurveNames(sample);
            }
        var curveNames = names.ToArray(); var source = Poses(root.GetProperty("base")); var sourceCurves = Curves(root.GetProperty("base"),curveNames);
        var cases = root.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(15, cases.Length);
        foreach(var hz in new[] {30,60,120}) foreach(var a in new[] {0,1}) Assert.Contains($"natural_{hz}_{a}",cases);
        foreach(var name in new[] {"replace","ordinary_grounded","turn_shared_group","same_frame","zero_rate","zero_blend","reverse","custom_trigger","uninterrupted_overlap"}) Assert.Contains(name,cases);
        var frames = 0; var overlap = 0; var overWeight = 0; var mixed = 0; var crossSlot = 0;
        double maxPositionSquared = 0, maxQuaternionSquared = 0, maxCurveError = 0;
        foreach(var trace in root.GetProperty("cases").EnumerateArray())
        {
            // The explicit Montage_Play(stopGroup:false) boundary registers each
            // transient montage as an authored one; the ALS dynamic API still stops its group.
            var actions = assets.Select(a => new AlsAuthoredMontageAsset(a.AnimationId,a.AnimationId,a.Slot,a.GroupId,a.Duration,0,1,
                new(AlsActionLifecycleMode.MontageAutoBlendOut,.2f,AlsActionBlendOption.HermiteCubic,.2f,AlsActionBlendOption.HermiteCubic,0)) {AdditiveType=a.AdditiveType}).ToArray();
            var owner = new AlsMontageRuntime([],actions,sequences: assets); var mixer = new AlsMontageSlotPose(source, parents, curveNames.Length);
            var output = new AlsPrecisePose[source.Length]; var curves = new AlsInertialCurve[curveNames.Length];
            var name = trace.GetProperty("name").GetString(); var delta = N(trace,"delta");
            foreach(var row in trace.GetProperty("frames").EnumerateArray())
            {
                var frame = row.GetProperty("frame").GetInt32(); var identity = new AlsFrameIdentity(frame,7,2); var label = $"{name} frame {frame}";
                var sampler = new NativeSamples(row.GetProperty("evaluation"),curveNames);
                // Isolate the blend kernel from float lifecycle rounding, then
                // independently exercise the same kernel with the port's clock.
                var nativeEntries = row.GetProperty("evaluation").EnumerateArray().Select(e =>
                {
                    var a=e.GetProperty("asset").GetInt32();
                    return new AlsMontageEvaluation(e.GetProperty("instance").GetInt64(),a,assets[a].Slot,N(e,"position"),N(e,"weight")) {AdditiveType=assets[a].AdditiveType};
                }).ToArray();
                var nativeFrame = new AlsMontageFrame {Identity=identity,Entries=nativeEntries,Count=nativeEntries.Length};
                var expected = Poses(row.GetProperty("result")); var expectedCurves = Curves(row.GetProperty("result"),curveNames);
                PrepareAndCheck(); var state = owner.Candidate.ToArray(); var pose = output.ToArray(); var curveState = curves.ToArray();
                owner.Discard(); PrepareAndCheck(); Assert.Equal(state,owner.Candidate.ToArray()); Assert.Equal(pose,output); Assert.Equal(curveState,curves);
                if(owner.Evaluation.Length > 1) overlap++;
                if(owner.SlotWeights(AlsMontageSlot.Grounded).TotalNodeWeight > 1.00001f) overWeight++;
                if(owner.Evaluation.ToArray().Any(e=>e.Slot == AlsMontageSlot.Grounded && e.AdditiveType == 0) &&
                   owner.Evaluation.ToArray().Any(e=>e.AdditiveType == 2)) mixed++;
                if(owner.Evaluation.ToArray().Any(e=>e.Slot.Id == 0) && owner.Evaluation.ToArray().Any(e=>e.Slot.Id == 3)) crossSlot++;
                owner.Commit(identity); frames++;

                void PrepareAndCheck()
                {
                    owner.Begin(identity,delta);
                    var evaluations = row.GetProperty("evaluation"); Assert.Equal(evaluations.GetArrayLength(),owner.Evaluation.Length);
                    for(var i=0;i<owner.Evaluation.Length;i++)
                    {
                        var e=evaluations[i]; var a=owner.Evaluation[i]; Assert.Equal(e.GetProperty("instance").GetInt64(),a.InstanceId);
                        Assert.Equal(e.GetProperty("asset").GetInt32(),a.AnimationId); Near(N(e,"position"),a.Position,3e-6,label+" time"); Near(N(e,"weight"),a.Weight,3e-6,label+" weight");
                    }
                    var weights=owner.SlotWeights(AlsMontageSlot.Grounded);
                    Near(N(row,"sourceWeight"),weights.SourceWeight,3e-6,label+" source weight"); Near(N(row,"slotWeight"),weights.SlotNodeWeight,3e-6,label+" slot weight");
                    Near(N(row,"totalWeight"),weights.TotalNodeWeight,3e-6,label+" total weight");
                    mixer.Evaluate(useNativeWeights ? nativeFrame : owner.Frame,identity,AlsMontageSlot.Grounded,source,sourceCurves,output,curves,sampler);
                    for(var b=0;b<output.Length;b++)
                    {
                        var p=output[b]; var e=expected[b]; var bone=label+" "+boneNames[b];
                        maxPositionSquared=System.Math.Max(maxPositionSquared,(p.Position-e.Position).LengthSquared);
                        Near(0,(p.Position-e.Position).LengthSquared,1e-12,bone+" position squared (cm)");
                        // Raw UE keys are near-unit, not necessarily unit. Compare
                        // components (including length); 1-|dot| is invalid here.
                        var q = AlsQuaternion.Dot(p.Rotation,e.Rotation)<0 ? -p.Rotation : p.Rotation;
                        var dx=q.X-e.Rotation.X; var dy=q.Y-e.Rotation.Y; var dz=q.Z-e.Rotation.Z; var dw=q.W-e.Rotation.W;
                        maxQuaternionSquared=System.Math.Max(maxQuaternionSquared,dx*dx+dy*dy+dz*dz+dw*dw);
                        Near(0,dx*dx+dy*dy+dz*dz+dw*dw,1e-20,bone+" quaternion squared difference");
                        Near(0,(p.Scale-e.Scale).LengthSquared,1e-14,bone+" scale squared");
                    }
                    for(var c=0;c<curves.Length;c++)
                    {
                        Assert.True(curves[c].Present==expectedCurves[c].Present,label+" curve presence "+curveNames[c]);
                        if(curves[c].Present)
                        {
                            maxCurveError=System.Math.Max(maxCurveError,System.Math.Abs((double)expectedCurves[c].Value-curves[c].Value));
                            Near(expectedCurves[c].Value,curves[c].Value,1e-5,label+" curve "+curveNames[c]);
                        }
                    }
                    foreach(var request in row.GetProperty("commands").EnumerateArray())
                    {
                        var a=request.GetProperty("asset").GetInt32();
                        if(request.GetProperty("stopGroup").GetBoolean())
                            Assert.True(owner.PlaySequence(new(a,assets[a].Slot,N(request,"rate"),N(request,"start"),N(request,"in"),N(request,"out"),1,N(request,"trigger"))));
                        else Assert.True(owner.PlayAction(a,N(request,"rate"),N(request,"start"),false));
                    }
                    var states=row.GetProperty("instances"); Assert.Equal(states.GetArrayLength(),owner.Candidate.Length);
                    for(var i=0;i<owner.Candidate.Length;i++)
                    {
                        var e=states[i]; var a=owner.Candidate[i]; Assert.Equal(e.GetProperty("instance").GetInt64(),a.InstanceId);
                        Assert.Equal(e.GetProperty("asset").GetInt32(),a.AnimationId); Near(N(e,"position"),a.Position,3e-6,label+" candidate time");
                        Near(N(e,"weight"),a.Blend.CurrentWeight,3e-6,label+" candidate weight"); Assert.Equal(e.GetProperty("playing").GetBoolean(),a.Playing);
                        Assert.Equal(e.GetProperty("active").GetBoolean(),owner.Observations[i].Active);
                    }
                }
            }
        }
        Assert.Equal(2080,frames); Assert.True(overlap>0 && overWeight>0 && mixed>0 && crossSlot>0,
            $"Missing native overlap coverage: overlap={overlap} overshoot={overWeight} mixed={mixed} crossSlot={crossSlot}");
        log.WriteLine($"ADDITIVE_SLOT_NATIVE_OK native_weights={useNativeWeights} frames={frames} bones={parents.Length} retries={frames} overlap={overlap} overshoot={overWeight} mixed={mixed} cross_slot={crossSlot} position_error_cm={System.Math.Sqrt(maxPositionSquared):R} quaternion_component_error={System.Math.Sqrt(maxQuaternionSquared):R} curve_error={maxCurveError:R}");
        void AddCurveNames(JsonElement value) { foreach(var c in value.GetProperty("curves").EnumerateObject()) names.Add(c.Name); }
    }
    private static float N(JsonElement e,string name)=>e.GetProperty(name).GetSingle();
    private static void Near(double expected,double actual,double tolerance,string label) =>
        Assert.True(double.IsFinite(actual) && System.Math.Abs(expected-actual)<=tolerance,$"{label}: native={expected:R}, port={actual:R}, tolerance={tolerance:R}");
    private static AlsPrecisePose[] Poses(JsonElement e)
    {
        var values=e.GetProperty("pose").EnumerateArray().Select(v=>v.GetDouble()).ToArray(); Assert.Equal(0,values.Length%10);
        var result=new AlsPrecisePose[values.Length/10];
        for(var b=0;b<result.Length;b++) { var i=b*10; result[b]=new(new(values[i],values[i+1],values[i+2]),new(values[i+3],values[i+4],values[i+5],values[i+6]),new(values[i+7],values[i+8],values[i+9])); }
        return result;
    }
    private static AlsInertialCurve[] Curves(JsonElement e,string[] names)
    {
        var data=e.GetProperty("curves"); return names.Select(n=>data.TryGetProperty(n,out var v)?new AlsInertialCurve(v.GetSingle()):default).ToArray();
    }
    private sealed class NativeSamples : IAlsMontagePoseSource
    {
        private readonly Dictionary<long,(AlsPrecisePose[] Pose,AlsInertialCurve[] Curves)> _values = [];
        public NativeSamples(JsonElement entries,string[] names)
        {
            foreach(var e in entries.EnumerateArray()) if(e.TryGetProperty("sample",out var sample))
                _values.Add(e.GetProperty("instance").GetInt64(),(Poses(sample),Curves(sample,names)));
        }
        public void Sample(in AlsMontageEvaluation entry,Span<AlsPrecisePose> pose,Span<AlsInertialCurve> curves)
        {
            var value=_values[entry.InstanceId]; value.Pose.CopyTo(pose); value.Curves.CopyTo(curves);
        }
    }
}
