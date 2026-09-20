using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsPoseCacheNativeTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void ReplaysNativeInitializationBoneCachingAndActualEngineEvaluationScopes(int hz)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "P3", "v4_pose_cache_lifecycle_native.json")));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("UAnimInstance::ParallelEvaluateAnimation", root.GetProperty("source").GetString());
        var trace = root.GetProperty("traces").EnumerateArray().Single(row => row.GetProperty("hz").GetInt32() == hz);
        var definition = new AlsPoseCacheDefinition(10, [8, 9], [new(1, 8), new(2, 8), new(3, 9)]);
        var committed = new AlsPoseCacheEvaluation(definition, 4, 2);
        var candidate = new AlsPoseCacheEvaluation(definition, 4, 2);
        var updates = new AlsPoseCacheTraversal(definition, 2);
        var identity = new AlsFrameIdentity(1, 0, 1);
        var bones = new AlsLocalPose[4]; var curves = new AlsInertialCurve[2];
        // Replay twice from the same committed state; scoped payloads and lifecycle mutations must not leak.
        for (var retry = 0; retry < 2; retry++)
        {
            var sink = new Sink();
            var scopes = new Stack<AlsPoseCacheScope>();
            candidate.BeginCandidate(identity, committed);
            var readCount = 0; var maxDepth = 0;
            foreach (var operation in trace.GetProperty("operations").EnumerateArray())
            {
                var name = operation.GetProperty("op").GetString();
                if (name == "push") { scopes.Push(candidate.PushScope()); maxDepth = System.Math.Max(maxDepth, scopes.Count); continue; }
                if (name == "pop") { candidate.PopScope(scopes.Pop()); continue; }
                var cache = operation.GetProperty("cache").GetInt32();
                var read = cache == 0 ? 1 + readCount % 2 : 3;
                var counter = operation.TryGetProperty("counter", out var count)
                    ? new AlsGraphTraversalCounter(count.GetInt16(), operation.GetProperty("globalFrame").GetUInt64()) : default;
                switch (name)
                {
                    case "initialize": candidate.Initialize(read, counter, sink); break;
                    case "bones": candidate.CacheBones(read, counter, sink); break;
                    case "update":
                        updates.Begin(identity);
                        updates.Use(read, new(identity, 1, operation.GetProperty("delta").GetSingle()));
                        updates.Drain(sink);
                        break;
                    case "read":
                        candidate.Evaluate(read, counter, scopes.Peek(), sink, bones, curves);
                        var positions = operation.GetProperty("positions");
                        Assert.Equal(4, positions.GetArrayLength());
                        for (var bone = 0; bone < bones.Length; bone++)
                        {
                            Assert.Equal(new Vector3(positions[bone][0].GetSingle(), positions[bone][1].GetSingle(),
                                positions[bone][2].GetSingle()), bones[bone].Position);
                            Assert.Equal(Quaternion.Identity, bones[bone].Rotation);
                            Assert.Equal(Vector3.One, bones[bone].Scale);
                        }
                        var nativeCurves = operation.GetProperty("curves");
                        Assert.Equal(new(nativeCurves.GetProperty("Present").GetSingle()), curves[0]);
                        var present = nativeCurves.TryGetProperty("Optional", out var optional);
                        Assert.Equal(new AlsInertialCurve(present ? optional.GetSingle() : 0, present), curves[1]);
                        if (operation.GetProperty("mutateOutput").GetBoolean())
                        { bones[0] = AlsLocalPose.Identity; Array.Fill(curves, new AlsInertialCurve(-99)); }
                        readCount++;
                        break;
                    default: throw new InvalidOperationException("Unexpected native cache lifecycle operation.");
                }
                Assert.False(operation.GetProperty("nativeUpdateCounterSet").GetBoolean());
                Assert.Equal(operation.GetProperty("initializations").GetInt32(), sink.Initializations[cache]);
                Assert.Equal(operation.GetProperty("boneCaches").GetInt32(), sink.BoneCaches[cache]);
                Assert.Equal(operation.GetProperty("updates").GetInt32(), sink.Updates[cache]);
                Assert.Equal(operation.GetProperty("evaluations").GetInt32(), sink.Evaluations[cache]);
            }
            Assert.Empty(scopes);
            Assert.Equal(2, maxDepth);
            Assert.Equal(17, readCount);
            Assert.Equal(sink.Evaluations.Sum(), candidate.SourceEvaluations);
        }
    }

    private sealed class Sink : IAlsPoseCachePoseSink, IAlsPoseCacheUpdateSink
    {
        public readonly int[] Initializations = new int[2], BoneCaches = new int[2], Updates = new int[2], Evaluations = new int[2];
        public void InitializeSource(int node) => Initializations[node - 8]++;
        public void CacheSourceBones(int node) => BoneCaches[node - 8]++;
        public void UpdateCachedSource(int node, in AlsPoseUpdateContext context) => Updates[node - 8]++;
        public void OnCachedUpdatesSkipped(int handlerNodeIndex, ReadOnlySpan<AlsPoseUpdateContext> skipped) =>
            throw new InvalidOperationException("Lifecycle probe does not register skipped-update handlers.");
        public void EvaluateSource(int node, Span<AlsLocalPose> bones, Span<AlsInertialCurve> curves)
        {
            var id = node - 8; var evaluation = ++Evaluations[id];
            for (var bone = 0; bone < bones.Length; bone++)
                bones[bone] = new(new Vector3(id, evaluation, bone), Quaternion.Identity, Vector3.One);
            curves[0] = new(evaluation + id * .25f);
            curves[1] = evaluation % 2 == 1 ? new(-.5f * evaluation) : new(0, false);
        }
    }
}
