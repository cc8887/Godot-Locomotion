using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

// Full authored topology on the independent UE probe's 79-bone hierarchy.
// Poses and Slot weights here are controlled component inputs, not Overlay gameplay.
public sealed class AlsLayerBlendingRuntimeTests
{
    [NormalEditorLayeringRepeat]
    public void NormalEditorRepeatsTheConsumedGraphAndInputSemantics()
    {
        var a = new Fixture();
        var text = File.ReadAllText(Environment.GetEnvironmentVariable("ALS_LAYERING_REPEAT_FILE")!);
        var repeated = AlsLayerBlendingCompiler.Compile(text);
        var input = AlsLayeringInputCompiler.Compile(text);
        Assert.Equal(a.Definition.RootIndex, repeated.RootIndex);
        Assert.Equal(a.Definition.Caches.UpdateOrder.ToArray(), repeated.Caches.UpdateOrder.ToArray());
        foreach (var node in a.Definition.Nodes)
        {
            var other = repeated.Node(node.Index);
            Assert.Equal(node.Name, other.Name); Assert.Equal(node.Kind, other.Kind);
            Assert.Equal(node.Inputs, other.Inputs); Assert.Equal(node.Alphas, other.Alphas);
            Assert.Equal(node.Label, other.Label); Assert.Equal(node.MeshSpaceRotation, other.MeshSpaceRotation);
            Assert.Equal(node.CurveBlendMode, other.CurveBlendMode);
            Assert.Equal(JsonSerializer.Serialize(node.Filters), JsonSerializer.Serialize(other.Filters));
        }
        Assert.Equal(a.InputModel.CurveNames.ToArray(), input.CurveNames.ToArray());
        Assert.Equal(a.InputModel.Evaluate(new(2, 0, 1), new(1, 0, 1), a.Names, a.Feedback),
            input.Evaluate(new(2, 0, 1), new(1, 0, 1), a.Names, a.Feedback));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void UnvisitedGraphInitializesCachesAndSlotsWithoutUpdatingOrEvaluating(int hz)
    {
        var f=new Fixture(); var runtime=f.Runtime(); var sink=f.Sink();
        var graph=default(AlsAnimationGraphFrame); var committed=default(AlsFrameIdentity);
        for(var frame=1;frame<=6;frame++)
        {
            var id=new AlsFrameIdentity(frame,0,1); graph=graph.Next(id,(ulong)frame);
            if(frame==3)graph=graph with {Bones=graph.Bones.Next(3)};
            if(frame==4)graph=graph with {Initialization=graph.Initialization.Next(4)};
            var input=f.InputModel.Evaluate(id,committed,committed==default ? [] : f.Names,committed==default ? [] : f.Feedback);
            sink.ResetFrame();
            var initialized=sink.InitializedInputs; var slots=sink.InitializedSlots; var bones=sink.BoneCaches;
            Prepare();
            Assert.Equal(frame is 1 or 4 ? 3 : 0,sink.InitializedInputs-initialized);
            Assert.Equal(frame is 1 or 4 ? 7 : 0,sink.InitializedSlots-slots);
            Assert.Equal(frame is 1 or 3 ? 3 : 0,sink.BoneCaches-bones);
            Assert.Equal(0,runtime.SourceUpdateCount); Assert.Equal(0,runtime.SourceEvaluationCount);
            Assert.Equal(0,sink.Updates); Assert.Equal(0,sink.SlotUpdates);
            Assert.Throws<InvalidOperationException>(()=>runtime.Evaluate(f.Output,f.Curves));
            runtime.Cancel(); Assert.Equal(committed,runtime.Identity);
            Prepare(); runtime.ValidateCommit(); runtime.Commit(); committed=id;
            void Prepare()=>runtime.Prepare(new(id,1,1f/hz),input,committed==default ? [] : f.Feedback,
                graph.Initialization,graph.Bones,graph.Evaluation,sink,updateSource:false);
        }
        f.Prepare(runtime,sink,7,hz); runtime.Evaluate(f.Output,f.Curves); runtime.Commit();
        Assert.Equal(3,sink.Updates); Assert.Equal(7,sink.SlotUpdates);
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void DynamicDifferenceReconstructsMovementAndFinalCurvesRestoreTheVirtualBoneBranch(int rate)
    {
        var f = new Fixture(); var runtime = f.Runtime(); var sink = f.Sink();
        for (var frame = 1; frame <= rate * 2; frame++)
        {
            f.Prepare(runtime, sink, frame, rate);
            runtime.Evaluate(f.Output, f.Curves);
            AssertPose(f.Movement, f.Output);
            Assert.Equal(new AlsInertialCurve(10), f.Curves[Array.IndexOf(f.Names, "Shared")]);
            Assert.Equal(new AlsInertialCurve(2), f.Curves[Array.IndexOf(f.Names, "BaseOnly")]);
            Assert.Equal(new AlsInertialCurve(5), f.Curves[Array.IndexOf(f.Names, "OverlayOnly")]);
            Assert.Equal(new[] { "Base Poses Input", "Base Layer Input", "Overlay Layer Input" }, sink.UpdatedInputs[..sink.Updates]);
            Assert.Equal(11, runtime.SourceUpdateCount);
            Assert.Equal(11, runtime.SourceEvaluationCount);
            Assert.Equal(3, sink.InputEvaluations);
            Assert.Equal(7, sink.SlotUpdates);
            Assert.Equal(0, sink.SlotEvaluations);
            runtime.Commit();
            Assert.Equal(frame, runtime.Identity.FrameId);
        }
        Assert.Equal(3, sink.InitializedInputs);
        Assert.Equal(7, sink.InitializedSlots);
        Assert.Equal(3, sink.BoneCaches);
    }

    [Fact]
    public void MissingTwoWayCurvesSkipOverlayBodyAndBothDynamicAdditiveSources()
    {
        var f = new Fixture(); var runtime = f.Runtime(); var sink = f.Sink();
        f.Prepare(runtime, sink, 1, 60, cold: true);
        runtime.Evaluate(f.Output, f.Curves);
        AssertPose(f.Movement, f.Output);
        Assert.Equal(new[] { "Base Layer Input", "Overlay Layer Input" }, sink.UpdatedInputs[..sink.Updates]);
        Assert.Equal(2, sink.InputEvaluations);
        Assert.Equal(1, sink.SlotUpdates); // Curves still matters even when all body branches choose A.
        Assert.Equal(new AlsInertialCurve(10), f.Curves[Array.IndexOf(f.Names, "Shared")]);
        runtime.Commit();
        f.Prepare(runtime, sink, 2, 60);
        runtime.Evaluate(f.Output, f.Curves);
        Assert.Equal(3, sink.InputEvaluations);
        Assert.Equal(3, sink.InitializedInputs); // Becoming relevant did not invent reinitialization.
        runtime.Commit();
    }

    [Fact]
    public void FullyOverridingCurvesSlotRetainsItsOutputWhileItsSourceIsNotEvaluated()
    {
        var f = new Fixture(); var runtime = f.Runtime(); var sink = f.Sink(); sink.OverrideCurvesSlot = true;
        f.Prepare(runtime, sink, 1, 60, cold: true);
        runtime.Evaluate(f.Output, f.Curves);
        AssertPose(f.Movement, f.Output);
        Assert.Equal(new[] { "Base Layer Input" }, sink.UpdatedInputs[..sink.Updates]);
        Assert.Equal(1, sink.InputEvaluations); Assert.Equal(1, sink.SlotEvaluations);
        Assert.False(sink.SlotSourceEvaluated);
        Assert.Equal(new AlsInertialCurve(14), f.Curves[Array.IndexOf(f.Names, "Shared")]);
        runtime.Commit();
    }

    [Theory]
    [InlineData("initialize")] [InlineData("bones")] [InlineData("update")] [InlineData("evaluate")]
    public void SourceFailureDiscardsCandidateAndSameFrameRetryMatchesFreshRuntime(string stage)
    {
        var f = new Fixture(); var runtime = f.Runtime(); var sink = f.Sink(); sink.FailureStage = stage;
        Assert.Throws<InvalidOperationException>(() => { f.Prepare(runtime, sink, 1, 60); runtime.Evaluate(f.Output, f.Curves); });
        Assert.False(runtime.HasCandidate); Assert.Equal(default, runtime.Identity);
        sink.FailureStage = "";
        f.Prepare(runtime, sink, 1, 60); runtime.Evaluate(f.Output, f.Curves); runtime.Commit();
        var expected = new Fixture(); var reference = expected.Runtime(); var referenceSink = expected.Sink();
        expected.Prepare(reference, referenceSink, 1, 60); reference.Evaluate(expected.Output, expected.Curves); reference.Commit();
        AssertPose(expected.Output, f.Output); Assert.Equal(expected.Curves, f.Curves);
        Assert.Equal(reference.SourceUpdateCount, runtime.SourceUpdateCount);
        Assert.Equal(reference.SourceEvaluationCount, runtime.SourceEvaluationCount);
    }

    [Fact]
    public void CancelDoesNotPublishAndFullRuntimeSteadyStateDoesNotAllocate()
    {
        var f = new Fixture(); var runtime = f.Runtime(); var sink = f.Sink();
        for (var frame = 1; frame <= 48; frame++)
        { f.Prepare(runtime, sink, frame, 60); runtime.Evaluate(f.Output, f.Curves); runtime.Commit(); }
        f.Prepare(runtime, sink, 49, 60); runtime.Evaluate(f.Output, f.Curves); runtime.Cancel();
        Assert.Equal(48, runtime.Identity.FrameId);
        // Match the other Import runtime allocation tests: isolate the measured worker from
        // the xUnit scheduler and warm this thread before measuring all successful frame stages.
        long allocated = -1; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                for (var frame = 49; frame <= 98; frame++)
                { f.Prepare(runtime, sink, frame, 60); runtime.Evaluate(f.Output, f.Curves); runtime.Commit(); }
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var frame = 99; frame <= 198; frame++)
                { f.Prepare(runtime, sink, frame, 60); runtime.Evaluate(f.Output, f.Curves); runtime.Commit(); }
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(failure); Assert.Equal(0, allocated);
    }

    [Fact]
    public void SeparateEvaluationScopesResampleCachedSourcesWithoutRepeatingUpdates()
    {
        var f = new Fixture(); var runtime = f.Runtime(); var sink = f.Sink();
        f.Prepare(runtime, sink, 1, 60);
        runtime.Evaluate(f.Output, f.Curves);
        var firstPose = (AlsLocalPose[])f.Output.Clone(); var firstCurves = (AlsInertialCurve[])f.Curves.Clone();
        runtime.Evaluate(f.Output, f.Curves);
        AssertPose(firstPose, f.Output); Assert.Equal(firstCurves, f.Curves);
        Assert.Equal(22, runtime.SourceEvaluationCount); Assert.Equal(6, sink.InputEvaluations);
        Assert.Equal(11, runtime.SourceUpdateCount); Assert.Equal(3, sink.Updates); Assert.Equal(7, sink.SlotUpdates);
        runtime.Commit();
    }

    [Fact]
    public void MissingVirtualCurveBoneIsRejectedRatherThanSilentlyDroppingCurveRestoration()
    {
        var f = new Fixture(); var names = (string[])f.BoneNames.Clone();
        names[Array.IndexOf(names, "VB Curves")] = "Missing virtual curve source";
        Assert.Throws<ArgumentException>(() => new AlsLayerBlendingRuntime(f.Definition, names, f.Parents, f.Names, f.Reference));
    }

    private sealed class Fixture
    {
        public readonly AlsLayerBlendingDefinition Definition;
        public readonly AlsLayeringInputModel InputModel;
        public readonly string[] BoneNames, Names;
        public readonly int[] Parents;
        public readonly AlsLocalPose[] Reference, Movement, Output;
        public readonly AlsInertialCurve[] Feedback, Curves;
        public Fixture()
        {
            var text = Read("assets/config/v4_layering_inputs.json");
            Definition = AlsLayerBlendingCompiler.Compile(text); InputModel = AlsLayeringInputCompiler.Compile(text);
            using var golden = JsonDocument.Parse(Read("tests/Als.Core.Tests/Fixtures/P3/v4_mesh_space_blend_native.json"));
            var root = golden.RootElement;
            BoneNames = root.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray();
            Parents = root.GetProperty("parents").EnumerateArray().Select(n => n.GetInt32()).ToArray();
            Reference = root.GetProperty("base").EnumerateArray().Select(Pose).ToArray();
            Movement = root.GetProperty("layer").EnumerateArray().Select(Pose).ToArray(); Output = new AlsLocalPose[Reference.Length];
            Names = InputModel.CurveNames.ToArray().Concat(Definition.Nodes.ToArray().SelectMany(n => n.Alphas)
                .Where(a => a.Kind == AlsLayerAlphaKind.Curve).Select(a => a.Name))
                .Concat(new[] { "Shared", "BaseOnly", "OverlayOnly" }).Distinct().Order().ToArray();
            Feedback = new AlsInertialCurve[Names.Length]; Curves = new AlsInertialCurve[Names.Length];
            foreach (var name in new[] { "Layering_Legs", "Layering_Pelvis", "Layering_Spine", "Layering_Head", "Layering_Arm_L", "Layering_Arm_R",
                "Layering_Spine_Add", "Layering_Head_Add", "Layering_Arm_L_Add", "Layering_Arm_R_Add", "BasePose_N" })
                Feedback[Array.IndexOf(Names, name)] = new(1);
        }
        public AlsLayerBlendingRuntime Runtime() => new(Definition, BoneNames, Parents, Names, Reference);
        public SourceSink Sink() => new(this);
        public void Prepare(AlsLayerBlendingRuntime runtime, SourceSink sink, int frame, int rate, bool cold = false)
        {
            sink.ResetFrame(); var identity = new AlsFrameIdentity(frame, 0, 1);
            var input = InputModel.Evaluate(identity, cold ? default : new(frame - 1, 0, 1),
                cold ? [] : Names, cold ? [] : Feedback);
            runtime.Prepare(new(identity, 1, 1f / rate), input, cold ? [] : Feedback,
                new(0, 0), new(0, 0), new((short)(frame % 30000), (ulong)frame), sink);
        }
    }

    private sealed class SourceSink(Fixture f) : IAlsLayerBlendingSink
    {
        public int InitializedInputs, InitializedSlots, BoneCaches, Updates, InputEvaluations, SlotUpdates, SlotEvaluations;
        public readonly string[] UpdatedInputs = new string[32];
        public string FailureStage = "";
        public bool OverrideCurvesSlot, SlotSourceEvaluated;
        public void ResetFrame() { Updates = InputEvaluations = SlotUpdates = SlotEvaluations = 0; }
        private void Check(string stage) { if (FailureStage == stage) throw new InvalidOperationException("Injected layer source failure."); }
        public void InitializeInput(int id, string name) { Check("initialize"); InitializedInputs++; }
        public void CacheInputBones(int id, string name) { Check("bones"); BoneCaches++; }
        public void UpdateInput(int id, string name, in AlsPoseUpdateContext context) { Check("update"); UpdatedInputs[Updates++] = name; }
        public void EvaluateInput(int id, string name, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
        {
            Check("evaluate"); InputEvaluations++; curves.Clear();
            (name == "Base Layer Input" ? f.Movement : f.Reference).CopyTo(pose);
            if (name == "Base Layer Input") { curves[Array.IndexOf(f.Names, "Shared")] = new(3); curves[Array.IndexOf(f.Names, "BaseOnly")] = new(2); }
            if (name == "Overlay Layer Input") { curves[Array.IndexOf(f.Names, "Shared")] = new(7); curves[Array.IndexOf(f.Names, "OverlayOnly")] = new(5); }
        }
        public void InitializeSlot(int id, string name) => InitializedSlots++;
        public AlsSlotWeights GetSlotWeights(int id, string name, in AlsPoseUpdateContext context) =>
            OverrideCurvesSlot && name == "Curves" ? new(0, 1, 1) : AlsSlotWeights.Passthrough;
        public void UpdateSlot(int id, string name, in AlsSlotWeights weights, in AlsSlotSourceUpdate source, in AlsPoseUpdateContext context) => SlotUpdates++;
        public void EvaluateSlot(int id, string name, in AlsSlotWeights weights, bool sourceEvaluated,
            ReadOnlySpan<AlsLocalPose> sourcePose, ReadOnlySpan<AlsInertialCurve> sourceCurves, Span<AlsLocalPose> pose, Span<AlsInertialCurve> curves)
        {
            SlotEvaluations++; SlotSourceEvaluated = sourceEvaluated;
            // UE creates an unevaluated SourceContext here. The fixed-layout adapter supplies
            // deterministic placeholders; their atoms must not contribute when sourceEvaluated is false.
            if (name != "Curves" || !OverrideCurvesSlot || sourceEvaluated ||
                sourcePose.Length != f.Reference.Length || sourceCurves.Length != f.Names.Length)
                throw new InvalidOperationException("Unexpected Slot evaluation contract.");
            f.Reference.CopyTo(pose); curves.Clear(); curves[Array.IndexOf(f.Names, "Shared")] = new(11);
        }
        public void OnCachedUpdatesSkipped(int handler, ReadOnlySpan<AlsPoseUpdateContext> skipped) { }
    }

    private static void AssertPose(AlsLocalPose[] expected, AlsLocalPose[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(Vector3.Distance(expected[i].Position, actual[i].Position), 0, .00001f);
            Assert.InRange(Vector3.Distance(expected[i].Scale, actual[i].Scale), 0, .00001f);
            Assert.InRange(MathF.Min((expected[i].Rotation - actual[i].Rotation).Length(),
                (expected[i].Rotation + actual[i].Rotation).Length()), 0, .00002f);
        }
    }
    private static AlsLocalPose Pose(JsonElement row)
    {
        var p = row.GetProperty("position"); var q = row.GetProperty("rotation"); var s = row.GetProperty("scale");
        return new(new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()),
            new(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle()), new(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle()));
    }
    private static string Read(string path) => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), path));
}

// Ordinary Editor exports are local integration evidence, not checked-in test dependencies.
// Set this to the actual repeated export; the default unit suite reports this check as skipped.
public sealed class NormalEditorLayeringRepeatAttribute : FactAttribute
{
    public NormalEditorLayeringRepeatAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALS_LAYERING_REPEAT_FILE")))
            Skip = "Set ALS_LAYERING_REPEAT_FILE to an ordinary Editor LayerBlending export.";
    }
}
