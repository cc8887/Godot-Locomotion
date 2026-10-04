using System.Security.Cryptography;
using System.Text.Json;
using GodotAls.Core.Animation;

namespace GodotAls.Core.Tests;

public sealed class AlsAnimationProxyCounterNativeTests
{
    private static string Evidence(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "GodotALS.csproj")))
            directory = directory.Parent ?? throw new InvalidOperationException("Missing repository root.");
        return Path.Combine(directory.FullName, "artifacts/lyra-analysis", name);
    }

    private static JsonDocument Read(string kind)
    {
        var path = Evidence("proxy-phase-v1-" + kind + ".json");
        using var closure = JsonDocument.Parse(File.ReadAllBytes(Evidence("proxy-phase-v1-closure.json")));
        if (kind is "native" or "requests")
            Assert.Equal(closure.RootElement.GetProperty(kind == "native" ? "nativeSha256" : "requestSha256").GetString(),
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }

    [Fact]
    public void AllFourCountersMatchOriginalProxyAndLinkedNodesIncludingSignedWrap()
    {
        using var requests = Read("requests"); using var native = Read("native");
        var steps = requests.RootElement.GetProperty("request").GetProperty("steps").EnumerateArray().ToArray();
        var result = native.RootElement.GetProperty("result");
        var rows = result.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(31, steps.Length); Assert.Equal(steps.Length, rows.Length);
        var states = new AlsAnimationProxyCounters[3]; var invalidated = false;
        Check(result.GetProperty("initial"));
        for (var i = 0; i < steps.Length; i++)
        {
            var step = steps[i]; var frame = step.GetProperty("frame").GetUInt64(); var mask = step.GetProperty("mask").GetInt32();
            void Linked(AlsAnimationProxyPhase phase, bool single = false)
            {
                for (var call = 0; call < 3; call++)
                    if (single ? call == 0 : (mask & (1 << call)) != 0)
                    {
                        var owner = call < 2 ? 1 : 2;
                        states[owner] = states[owner].SynchronizeLinked(phase, states[0]);
                    }
            }
            void Main(AlsAnimationProxyPhase phase)
            { states[0] = states[0].AdvanceMainRoot(phase, frame); Linked(phase); }
            void Bones()
            { if (invalidated) { Main(AlsAnimationProxyPhase.CachedBones); invalidated = false; } }
            switch (step.GetProperty("op").GetString())
            {
                case "initialize": Main(AlsAnimationProxyPhase.Initialization); break;
                case "initialize-function": Linked(AlsAnimationProxyPhase.Initialization, true); break;
                case "invalidate": invalidated = true; break;
                case "bones": Bones(); break;
                case "bones-function": Linked(AlsAnimationProxyPhase.CachedBones, true); break;
                case "update": Bones(); Main(AlsAnimationProxyPhase.Update); break;
                case "update-function": Linked(AlsAnimationProxyPhase.Update, true); break;
                case "evaluate": Bones(); Main(AlsAnimationProxyPhase.Evaluation); break;
                case "evaluate-function": Linked(AlsAnimationProxyPhase.Evaluation, true); break;
                case "proxy-initialize": states[0] = states[0].InitializeProxy(); break;
                case "wrap":
                    for (var n = 0; n < step.GetProperty("iterations").GetInt32(); n++) Main(AlsAnimationProxyPhase.Evaluation);
                    break;
                case "null-initialize": case "null-update": case "null-evaluate": break;
                default: throw new InvalidOperationException("Unrecognized native phase.");
            }
            Check(rows[i]);
        }

        void Check(JsonElement snapshot)
        {
            var proxies = snapshot.GetProperty("proxies").EnumerateArray().ToArray(); Assert.Equal(3, proxies.Length);
            for (var owner = 0; owner < states.Length; owner++)
            foreach (var phase in Enum.GetValues<AlsAnimationProxyPhase>())
            {
                var name = phase switch { AlsAnimationProxyPhase.Initialization => "initialization", AlsAnimationProxyPhase.CachedBones => "bones", AlsAnimationProxyPhase.Update => "update", _ => "evaluation" };
                var expected = proxies[owner].GetProperty(name); var actual = states[owner].Counter(phase);
                Assert.Equal(expected.GetProperty("counter").GetInt32(), actual.Counter);
                Assert.Equal(expected.GetProperty("frame").GetInt64(), actual.HasUpdated ? checked((long)actual.GlobalFrame) : -1);
            }
        }
    }

    [Fact]
    public void OriginalWorkerGateAndRootCallsHaveDifferentLifetimes()
    {
        using var native = Read("native"); var rows = native.RootElement.GetProperty("result").GetProperty("rows").EnumerateArray().ToArray();
        JsonElement Proxy(int row, int owner) => rows[row].GetProperty("proxies")[owner];
        Assert.Equal(0, Proxy(7, 0).GetProperty("workerCalls").GetInt32()); // Frame zero matches the native initial gate.
        Assert.Equal(1, Proxy(8, 0).GetProperty("workerCalls").GetInt32());
        Assert.Equal(1, Proxy(9, 0).GetProperty("workerCalls").GetInt32());
        Assert.Equal(2, Proxy(9, 0).GetProperty("update").GetProperty("counter").GetInt32()); // Three real root calls.
        Assert.Equal(2, Proxy(10, 1).GetProperty("update").GetProperty("counter").GetInt32());
        Assert.Equal(1, Proxy(10, 1).GetProperty("workerCalls").GetInt32()); // Function call still executes.
        Assert.Equal(7, Proxy(14, 2).GetProperty("workerFrame").GetInt32()); // Hidden actual instance.
        Assert.Equal(1, Proxy(14, 2).GetProperty("workerCalls").GetInt32());
        Assert.Equal(8, Proxy(14, 1).GetProperty("workerFrame").GetInt32());
        Assert.Equal(4, Proxy(19, 0).GetProperty("workerCalls").GetInt32()); // Null root still permits worker entry.
        Assert.Equal(4, Proxy(19, 0).GetProperty("update").GetProperty("counter").GetInt32());
        Assert.Equal(12, Proxy(26, 0).GetProperty("workerFrame").GetInt32());
        Assert.Equal(5, Proxy(26, 0).GetProperty("workerCalls").GetInt32());
    }
}
