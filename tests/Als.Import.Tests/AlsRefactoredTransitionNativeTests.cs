using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredTransitionNativeTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases() => AlsRefactoredWeaponNativeTests.Cases();

    [Theory]
    [MemberData(nameof(Cases))]
    public void ActualNotifyFunctionsAndContinuousSlotMatch(AlsRefactoredWeaponKind kind, int hz)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var profiles = Enum.GetValues<AlsRefactoredWeaponKind>().Select(k => new AlsRefactoredWeaponNotifyProfile(catalog,
            new(catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), catalog, k)))).ToArray();
        var paths = profiles.SelectMany(p => p.Bindings.ToArray()).Select(b => b.Sequence).Distinct().Order().ToArray();
        var montages = new AlsRefactoredTransitionMontages(catalog, MantlingHostFixture.Read("refactored_slot_inventory"), profiles,
            paths.Select((p, i) => (p, i)).ToDictionary(v => v.p, v => 700 + v.i), 20);
        var additive = catalog.CompileAdditivePose(paths[0]);
        var basis = catalog.CompileAbsolutePoseWithCurves("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose");
        var names = additive.CurveNames.ToArray().Concat(basis.Curves.Names.ToArray()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var source = new AlsRefactoredTransitionPose(catalog, montages, additive.BoneNames, additive.Parents, names);
        var sampler = source.CreateSampler(); var bank = new AlsMontageRuntime([], sequences: montages.Assets);
        var basePose = new AlsPrecisePose[79]; var baseCurves = new AlsInertialCurve[names.Length];
        var rawCurves = new AlsInertialCurve[basis.Curves.Names.Length];
        basis.Pose.CreateSampler(basis.Curves).Sample(0, true, false, false, basePose, rawCurves);
        for (var c = 0; c < rawCurves.Length; c++) baseCurves[Array.IndexOf(names, basis.Curves.Names[c])] = rawCurves[c];
        var mixer = new AlsMontageSlotPose(basePose, additive.Parents, names.Length);
        var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[names.Length];
        using var doc = JsonDocument.Parse(MantlingHostFixture.Read("refactored_transition_trace"));
        var root = doc.RootElement; Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        foreach (var hash in root.GetProperty("resourceHashes").EnumerateObject())
            Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MantlingHostFixture.Read(hash.Name)))), hash.Value.GetString()!.ToUpperInvariant());
        var trace = root.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("kind").GetString() == kind.ToString() && t.GetProperty("hz").GetInt32() == hz);
        double maxPosition = 0, maxRotation = 0, maxScale = 0, maxCurve = 0, maxClock = 0, maxWeight = 0;
        var frame = 0; var mixed = 0; var created = new HashSet<long>();
        foreach (var row in trace.GetProperty("frames").EnumerateArray())
        {
            var input = row.GetProperty("input"); var id = new AlsFrameIdentity(frame, 11, 1);
            var context = $"{kind}/{hz}/{frame}"; bank.Begin(id, input.GetProperty("delta").GetSingle());
            var weights = bank.Frame.SlotWeights(AlsMontageSlot.Transition);
            Compare(weights.SourceWeight, row.GetProperty("sourceWeight").GetDouble(), ref maxWeight, 2e-6, "source weight");
            Compare(weights.SlotNodeWeight, row.GetProperty("slotWeight").GetDouble(), ref maxWeight, 2e-6, "slot weight");
            Compare(weights.TotalNodeWeight, row.GetProperty("totalWeight").GetDouble(), ref maxWeight, 2e-6, "total weight");
            var evaluation = row.GetProperty("evaluation");
            Assert.True(bank.Evaluation.Length == evaluation.GetArrayLength(), context + " evaluation count");
            if (bank.Evaluation.Length > 1) mixed++;
            for (var i = 0; i < bank.Evaluation.Length; i++)
            {
                var actual = bank.Evaluation[i]; var expected = evaluation[i];
                Assert.Equal(expected.GetProperty("source").GetString(), montages.SourcePath(actual.AnimationId));
                Compare(actual.Position, expected.GetProperty("position").GetDouble(), ref maxClock, 2e-6, "evaluation clock");
                Compare(actual.Weight, expected.GetProperty("weight").GetDouble(), ref maxWeight, 2e-6, "evaluation weight");
            }
            mixer.Evaluate(bank.Frame, id, AlsMontageSlot.Transition, basePose, baseCurves, pose, curves, sampler);
            var native = row.GetProperty("result").GetProperty("pose"); Assert.Equal(790, native.GetArrayLength());
            for (var b = 0; b < 79; b++)
            {
                var p = pose[b]; var offset = b * 10;
                double[] actual = [p.Position.X, p.Position.Y, p.Position.Z, p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W, p.Scale.X, p.Scale.Y, p.Scale.Z];
                var dot = Enumerable.Range(3, 4).Sum(c => actual[c] * native[offset + c].GetDouble());
                for (var c = 0; c < 10; c++)
                {
                    var v = actual[c] * (c is >= 3 and <= 6 && dot < 0 ? -1 : 1);
                    if (c < 3) Compare(v, native[offset + c].GetDouble(), ref maxPosition, 2e-5, $"bone {b} position");
                    else if (c < 7) Compare(v, native[offset + c].GetDouble(), ref maxRotation, 2e-6, $"bone {b} rotation");
                    else Compare(v, native[offset + c].GetDouble(), ref maxScale, 2e-6, $"bone {b} scale");
                }
            }
            var nativeCurves = row.GetProperty("result").GetProperty("curves").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(nativeCurves.Count, curves.Count(c => c.Present));
            for (var c = 0; c < curves.Length; c++)
            {
                Assert.Equal(nativeCurves.ContainsKey(names[c]), curves[c].Present);
                if (curves[c].Present) Compare(curves[c].Value, nativeCurves[names[c]], ref maxCurve, 2e-6, "curve " + names[c]);
            }
            var requests = input.GetProperty("stance").GetString() == "Als.Stance.Standing" && !input.GetProperty("moving").GetBoolean()
                ? input.GetProperty("notifies").EnumerateArray().Select((n, i) => new AlsRefactoredWeaponTransitionRequest(id, i, profiles.Single(p => p.Machine.Resources.Kind == kind).Bindings[n.GetInt32()])).ToArray()
                : [];
            montages.Play(bank, id, requests);
            if (input.GetProperty("stop").GetBoolean()) montages.StopTransitionAndTurnInPlace(bank, id, input.GetProperty("stopDuration").GetSingle());
            var states = row.GetProperty("instances"); Assert.True(bank.Candidate.Length == states.GetArrayLength(), context + " instance count");
            for (var i = 0; i < bank.Candidate.Length; i++)
            {
                var actual = bank.Candidate[i]; var expected = states[i]; created.Add(actual.InstanceId);
                Assert.Equal(expected.GetProperty("instance").GetInt64(), actual.InstanceId);
                Assert.Equal(expected.GetProperty("source").GetString(), montages.SourcePath(actual.AnimationId));
                Assert.Equal(expected.GetProperty("playing").GetBoolean(), actual.Playing);
                Compare(actual.Position, expected.GetProperty("position").GetDouble(), ref maxClock, 2e-6, "clock");
                Compare(actual.Blend.CurrentWeight, expected.GetProperty("weight").GetDouble(), ref maxWeight, 2e-6, "weight");
                Compare(actual.Blend.DesiredWeight, expected.GetProperty("desired").GetDouble(), ref maxWeight, 0, "desired");
                Compare(actual.BlendTime, expected.GetProperty("blendTime").GetDouble(), ref maxClock, 2e-6, "blend time");
                Compare(actual.PlayRate, expected.GetProperty("rate").GetDouble(), ref maxClock, 0, "rate");
            }
            bank.Commit(id); frame++;
            void Compare(double actual, double expected, ref double maximum, double tolerance, string field)
            {
                var difference = Math.Abs(actual - expected); maximum = Math.Max(maximum, difference);
                Assert.True(difference <= tolerance, $"{context} {field}: actual={actual:R} expected={expected:R} difference={difference:R}");
            }
        }
        Assert.Equal(hz * 4, frame); Assert.True(mixed > 0); Assert.Equal(8, created.Count); Assert.Empty(bank.Committed.ToArray());
        output.WriteLine($"{kind}/{hz}: frames={frame} mixed={mixed} instances={created.Count} position={maxPosition:R} rotation={maxRotation:R} scale={maxScale:R} curve={maxCurve:R} clock={maxClock:R} weight={maxWeight:R}");
    }
}
