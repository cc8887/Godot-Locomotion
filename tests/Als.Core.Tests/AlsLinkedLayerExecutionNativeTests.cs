using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Animation;

namespace GodotAls.Core.Tests;

public sealed class AlsLinkedLayerExecutionNativeTests
{
    private static string Evidence(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GodotALS.csproj")))
            directory = directory.Parent ?? throw new InvalidOperationException("Missing repository root.");
        return Path.Combine(directory.FullName, "artifacts/lyra-analysis", name);
    }

    private static JsonDocument Native()
    {
        var bytes = File.ReadAllBytes(Evidence("layer-fallback-v4-native.json"));
        using var closure = JsonDocument.Parse(File.ReadAllBytes(Evidence("layer-fallback-v4-closure.json")));
        Assert.Equal(closure.RootElement.GetProperty("nativeSha256").GetString(),
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        return JsonDocument.Parse(bytes);
    }

    public static IEnumerable<object[]> Cases()
    {
        using var native = Native();
        foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetProperty("name").GetString()!];
    }

    private sealed class Output
    {
        public double[][] Pose = [];
        public bool HasCurve, HasAttribute;
        public float Curve;
        public uint CurveFlags;
        public int Attribute;
    }
    private static double[][] Pose(JsonElement value) => value.GetProperty("pose").EnumerateArray()
        .Select(b => new[] { "position", "rotation", "scale" }.SelectMany(channel =>
            b.GetProperty(channel).EnumerateArray().Select(v => v.GetDouble())).ToArray()).ToArray();

    [Theory]
    [MemberData(nameof(Cases))]
    public void FallbackMatchesNativePoseCurveAttributeAndLazyInputTraversal(string name)
    {
        using var native = Native();
        var rows = native.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        var expected = rows.Single(c => c.GetProperty("name").GetString() == name);
        var additive = expected.GetProperty("additive").GetBoolean();
        var reference = Pose(rows.Single(c => c.GetProperty("name").GetString() ==
            (additive ? "unbound:0:1:0" : "unbound:0:0:0")));
        var output = new Output { Pose = reference.Select(b => b.Select(_ => 902d).ToArray()).ToArray() };
        if (expected.GetProperty("prefilled").GetBoolean())
        { output.HasCurve = output.HasAttribute = true; output.Curve = -7; output.CurveFlags = 1; output.Attribute = -21; }
        int firstUpdates = 0, secondUpdates = 0, firstEvaluations = 0, secondEvaluations = 0;
        var inputs = expected.GetProperty("inputs").GetInt32();
        Action<Output> resetPose = o => o.Pose = reference.Select(b => b.ToArray()).ToArray();
        void Input(Output o, int marker)
        {
            resetPose(o); o.Pose[0][0] = marker; o.Pose[0][1] = -marker; o.Pose[0][2] = marker * .5;
            o.HasCurve = o.HasAttribute = true; o.Curve = marker + .25f; o.CurveFlags = 2; o.Attribute = marker * 3;
        }
        Action[] updates = Enumerable.Range(0, inputs).Select<int, Action>(i =>
            i == 0 ? () => ++firstUpdates : () => ++secondUpdates).ToArray();
        Action<Output>[] evaluations = Enumerable.Range(0, inputs).Select<int, Action<Output>>(i =>
            i == 0 ? o => { ++firstEvaluations; Input(o, 11); } : o => { ++secondEvaluations; Input(o, 22); }).ToArray();
        var self = expected.TryGetProperty("self", out var selfValue) && selfValue.GetBoolean();
        var rootUpdates = 0;
        var rootEvaluations = 0;
        // All fourteen native default closures have an unconnected Result
        // pin. Even the three signatures with pose inputs discard those
        // inputs: self enters the real empty root, not the missing-root path.
        void UpdateRoot() { ++rootUpdates; }
        void EvaluateRoot(Output o) { ++rootEvaluations; resetPose(o); }
        AlsLinkedLayerExecution.Update(self, self, updates, UpdateRoot);
        AlsLinkedLayerExecution.Evaluate(self, self, evaluations, EvaluateRoot, resetPose, output);
        Assert.Equal(self ? 1 : 0, rootUpdates);
        Assert.Equal(self ? 1 : 0, rootEvaluations);
        Assert.Equal(expected.GetProperty("firstUpdates").GetInt32(), firstUpdates);
        Assert.Equal(expected.GetProperty("secondUpdates").GetInt32(), secondUpdates);
        Assert.Equal(expected.GetProperty("firstEvaluations").GetInt32(), firstEvaluations);
        Assert.Equal(expected.GetProperty("secondEvaluations").GetInt32(), secondEvaluations);
        Assert.Equal(expected.GetProperty("hasCurve").GetBoolean(), output.HasCurve);
        Assert.Equal(expected.GetProperty("curve").GetSingle(), output.Curve);
        Assert.Equal(expected.GetProperty("curveFlags").GetUInt32(), output.CurveFlags);
        Assert.Equal(expected.GetProperty("hasAttribute").GetBoolean(), output.HasAttribute);
        Assert.Equal(expected.GetProperty("attribute").GetInt32(), output.Attribute);
        var pose = Pose(expected); Assert.Equal(pose.Length, output.Pose.Length);
        for (var bone = 0; bone < pose.Length; ++bone) Assert.Equal(pose[bone], output.Pose[bone]);
    }

    [Fact]
    public void OriginalMainEmitsFourteenExecutableDefaultRootsDespiteUnimplementedFlags()
    {
        using var native = Native();
        var functions = native.RootElement.GetProperty("functions").EnumerateArray().ToArray();
        Assert.Equal(15, functions.Length);
        var main = functions.Single(f => f.GetProperty("name").GetString() == "AnimGraph");
        Assert.True(main.GetProperty("implemented").GetBoolean());
        Assert.True(main.GetProperty("hasRootProperty").GetBoolean());
        var layers = functions.Where(f => f.GetProperty("name").GetString() != "AnimGraph").ToArray();
        Assert.All(layers, f => Assert.False(f.GetProperty("implemented").GetBoolean()));
        Assert.All(layers, f => Assert.True(f.GetProperty("hasRootProperty").GetBoolean()));
        foreach (var layer in layers)
        {
            var nodes = layer.GetProperty("nodes").EnumerateArray().ToArray();
            var root = nodes.Single(n => n.GetProperty("propertyIndex").GetInt32() == layer.GetProperty("rootPropertyIndex").GetInt32());
            Assert.Equal("/Script/Engine.AnimNode_Root", root.GetProperty("type").GetString());
            Assert.Single(nodes);
            Assert.Empty(root.GetProperty("links").EnumerateArray());
        }
        var rows = native.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(40, rows.Length);
        foreach (var function in layers)
        {
            var initial = rows.Single(r => r.GetProperty("name").GetString() == "initial:" + function.GetProperty("name").GetString());
            var unlinked = rows.Single(r => r.GetProperty("name").GetString() == "unlinked:" + function.GetProperty("name").GetString());
            Assert.True(initial.GetProperty("self").GetBoolean()); Assert.True(unlinked.GetProperty("self").GetBoolean());
            Assert.Equal(initial.GetProperty("pose").GetRawText(), unlinked.GetProperty("pose").GetRawText());
        }
    }
}
