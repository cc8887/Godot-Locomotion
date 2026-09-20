using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsDynamicMontageNativeTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var data = Read();
        foreach (var item in data.RootElement.GetProperty("cases").EnumerateArray()) yield return [item.GetProperty("name").GetString()!];
        using var mixed = Read(true);
        foreach (var item in mixed.RootElement.GetProperty("cases").EnumerateArray()) yield return ["mixed:" + item.GetProperty("name").GetString()!];
    }

    [Fact]
    public void OracleCoversEveryTurnThreeFrameRatesAndLifecycleBoundaries()
    {
        using var data = Read(); var root = data.RootElement;
        Assert.Equal(1,root.GetProperty("schemaVersion").GetInt32()); Assert.Equal(8,root.GetProperty("assets").GetArrayLength());
        var names = root.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray();
        Assert.Equal(40,names.Length);
        foreach (var hz in new[] {30,60,120}) foreach (var asset in Enumerable.Range(0,8)) Assert.Contains($"natural_{hz}_{asset}",names);
        Assert.Contains("rapid_cross_stance",names); Assert.Contains("same_frame_replace",names);
        foreach (var asset in new[] {0,4}) foreach (var mode in new[] {"reverse","zero_rate","zero_blend","start_end","custom_trigger","default_trigger","large_delta"})
            Assert.Contains($"{mode}_{asset}",names);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void CandidatePlaybackAndPreBlueprintEvaluationMatchNative(string name)
    {
        var mixed = name.StartsWith("mixed:", StringComparison.Ordinal);
        using var data = Read(mixed); var root = data.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray().Take(8).Select((a,i) =>
            new AlsDynamicMontageAsset(i,(AlsTurnSlot)a.GetProperty("slot").GetInt32(),1,Number(a,"length"))).ToArray();
        var trace = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == (mixed ? name[6..] : name));
        AlsAuthoredMontageAsset[] actions = [];
        if (mixed)
        {
            var a = root.GetProperty("assets")[8];
            actions = [new(0,8,AlsMontageSlot.BaseLayer,3,Number(a,"length"),0,1,
                new(a.GetProperty("auto").GetBoolean() ? AlsActionLifecycleMode.MontageAutoBlendOut : AlsActionLifecycleMode.MontageHoldAtEnd,
                    Number(a,"in"),(AlsActionBlendOption)a.GetProperty("inOption").GetInt32(),
                    Number(a,"out"),(AlsActionBlendOption)a.GetProperty("outOption").GetInt32(),Number(a,"trigger")))];
        }
        var delta = Number(trace,"delta"); var owner = new AlsMontageRuntime(assets, actions);
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var frame = row.GetProperty("frame").GetInt32(); var identity = new AlsFrameIdentity(frame,7,1);
            Prepare(); Check(); var expectedStates = owner.Candidate.ToArray(); var expectedPose = owner.Evaluation.ToArray();
            owner.Discard(); Prepare(); Check();
            Assert.Equal(expectedStates,owner.Candidate.ToArray()); Assert.Equal(expectedPose,owner.Evaluation.ToArray());
            owner.Commit(identity);
            void Prepare()
            {
                owner.Begin(identity,delta);
                foreach (var request in row.GetProperty("commands").EnumerateArray())
                {
                    var asset = request.GetProperty("asset").GetInt32();
                    if (mixed && request.GetProperty("stopInstance").GetInt64() > 0)
                    {
                        Assert.Equal(request.GetProperty("played").GetBoolean(), owner.StopInstance(request.GetProperty("stopInstance").GetInt64(),
                            Number(request,"in"), AlsActionBlendOption.HermiteCubic)); continue;
                    }
                    if (asset == 8)
                    {
                        Assert.Equal(request.GetProperty("played").GetBoolean(), owner.PlayAction(0,Number(request,"rate"),Number(request,"start"),
                            request.GetProperty("stopGroup").GetBoolean())); continue;
                    }
                    Assert.Equal(request.GetProperty("played").GetBoolean(),owner.Play(new(asset,assets[asset].Slot,
                        Number(request,"rate"),Number(request,"start"),Number(request,"in"),Number(request,"out"),1,Number(request,"trigger"))));
                }
            }
            void Check()
            {
                var states = row.GetProperty("instances"); var pose = row.GetProperty("evaluation");
                Assert.True(states.GetArrayLength() == owner.Candidate.Length,$"{name} frame {frame}: instance count native={states.GetArrayLength()} port={owner.Candidate.Length}");
                Assert.True(pose.GetArrayLength() == owner.Evaluation.Length,$"{name} frame {frame}: evaluation count native={pose.GetArrayLength()} port={owner.Evaluation.Length}");
                for (var i = 0; i < owner.Candidate.Length; i++)
                {
                    var expected = states[i]; var actual = owner.Candidate[i];
                    Assert.Equal(expected.GetProperty("instance").GetInt64(),actual.InstanceId);
                    Assert.Equal(expected.GetProperty("asset").GetInt32(),actual.AnimationId);
                    Assert.Equal(actual.AnimationId == 8 ? 0 : -1, actual.ActionDefinitionId);
                    Near(expected,"position",actual.Position); Near(expected,"rate",actual.PlayRate);
                    Near(expected,"weight",actual.Blend.CurrentWeight); Near(expected,"desired",actual.Blend.DesiredWeight);
                    Near(expected,"alpha",actual.Blend.Alpha); Near(expected,"remaining",actual.Blend.RemainingSeconds);
                    Near(expected,"blendTime",actual.BlendTime);
                    Assert.True(expected.GetProperty("playing").GetBoolean() == actual.Playing,$"{name} frame {frame}: playing differs");
                    Assert.Equal(expected.GetProperty("active").GetBoolean(),owner.Observations[i].Active);
                }
                for (var i = 0; i < owner.Evaluation.Length; i++)
                {
                    var expected = pose[i]; var actual = owner.Evaluation[i];
                    Assert.Equal(expected.GetProperty("instance").GetInt64(),actual.InstanceId);
                    Assert.Equal(expected.GetProperty("asset").GetInt32(),actual.AnimationId);
                    Near(expected,"position",actual.Position); Near(expected,"weight",actual.Weight);
                }
            }
            void Near(JsonElement expected, string field, float actual)
            {
                var value = Number(expected,field);
                Assert.True(float.IsFinite(actual) && MathF.Abs(value-actual) <= .000003f,
                    $"{name} frame {frame} {field}: native={value:R} port={actual:R}");
            }
        }
    }

    [Fact]
    public void AuthoredOracleCoversReplaysGroupsAndPhysicalCancellation()
    {
        using var data = Read(true); var root = data.RootElement;
        Assert.Equal(2,root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(9,root.GetProperty("assets").GetArrayLength());
        Assert.Equal("MovementActionGroup",root.GetProperty("assets")[8].GetProperty("group").GetString());
        var names = root.GetProperty("cases").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray();
        Assert.Equal(11,names.Length);
        foreach (var name in new[] {"roll_30","roll_60","roll_120","roll_and_turn","turn_then_roll","roll_same_frame",
            "roll_no_group_stop","roll_reverse","roll_zero_rate","roll_stop_old_instance","roll_stop_active"}) Assert.Contains(name,names);
        Assert.Equal(2540,root.GetProperty("cases").EnumerateArray().Sum(c => c.GetProperty("frames").GetArrayLength()));
    }

    private static float Number(JsonElement item,string name) => item.GetProperty(name).GetSingle();
    private static JsonDocument Read(bool mixed = false) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","P3",
        mixed ? "v4_mixed_montage_native.json" : "v4_dynamic_montage_native.json")));
}
