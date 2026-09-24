using System.Text.Json;
using GodotAls.Core.Actions;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredTransitionPoseTests
{
    private sealed class Fixture
    {
        public readonly AlsRefactoredAnimationCatalog Catalog = new(MantlingHostFixture.Read("refactored_animation_sources"),
            p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        public readonly AlsRefactoredTransitionMontages Montages;
        public readonly AlsRefactoredTransitionPose Pose;
        public readonly AlsRefactoredWeaponNotifyProfile[] Notifications;
        public readonly AlsPrecisePose[] Base;
        public Fixture()
        {
            Notifications = Enum.GetValues<AlsRefactoredWeaponKind>().Select(k => new AlsRefactoredWeaponNotifyProfile(Catalog,
                new(Catalog, new(MantlingHostFixture.Read("refactored_weapon_machines"), Catalog, k)))).ToArray();
            var paths = Notifications.SelectMany(p => p.Bindings.ToArray()).Select(b => b.Sequence).Distinct().Order().ToArray();
            Montages = new(Catalog, MantlingHostFixture.Read("refactored_slot_inventory"), Notifications,
                paths.Select((p, i) => (p, i)).ToDictionary(v => v.p, v => 700 + v.i), 20);
            var source = Catalog.CompileAdditivePose(paths[0]);
            Pose = new(Catalog, Montages, source.BoneNames, source.Parents,
                source.CurveNames.ToArray().Reverse().Select(n => n.ToLowerInvariant()).Prepend("HostExtra").ToArray());
            var basis = Catalog.CompileAbsolutePoseWithCurves("/ALS/ALS/Animations/Base/A_Als_Stand_Pose.A_Als_Stand_Pose");
            Base = new AlsPrecisePose[source.BoneNames.Length];
            basis.Pose.CreateSampler(basis.Curves).Sample(0, true, false, false, Base, new AlsInertialCurve[basis.Curves.Names.Length]);
        }
    }
    private static readonly Lazy<Fixture> Data = new(() => new());

    [Fact]
    public void ActualHostIdsAndReorderedCurveLayoutPreserveSourceSamplesAndNativeStartPose()
    {
        var data = Data.Value; var sampler = data.Pose.CreateSampler(); var other = data.Pose.CreateSampler();
        var pose = new AlsPrecisePose[79]; var second = new AlsPrecisePose[79];
        var curves = new AlsInertialCurve[data.Pose.CurveNames.Length]; var secondCurves = new AlsInertialCurve[curves.Length];
        using var native = JsonDocument.Parse(MantlingHostFixture.Read("refactored_additive_reference"));
        foreach (var asset in data.Montages.Assets)
        {
            var path = data.Montages.SourcePath(asset.AnimationId); var source = data.Catalog.CompileAdditivePose(path); var direct = source.CreateSampler();
            var expected = new AlsPrecisePose[79]; var expectedCurves = new AlsInertialCurve[source.CurveNames.Length];
            for (var step = 0; step <= 60; step++)
            {
                var time = asset.Duration * (step / 60f);
                var entry = new AlsMontageEvaluation(step + 1, asset.AnimationId, AlsMontageSlot.Transition, time, .75f) { AdditiveType = 2 };
                sampler.Sample(entry, pose, curves); direct.Sample(time, expected, expectedCurves);
                Assert.Equal(expected, pose); Assert.False(curves[0].Present);
                for (var i = 0; i < expectedCurves.Length; i++)
                    Assert.Equal(expectedCurves[i], curves[Array.FindIndex(data.Pose.CurveNames.ToArray(), n => n.Equals(source.CurveNames[i], StringComparison.OrdinalIgnoreCase))]);
                Parallel.Invoke(() => sampler.Sample(entry, pose, curves), () => other.Sample(entry, second, secondCurves));
                Assert.Equal(pose, second); Assert.Equal(curves, secondCurves);
                if (step != 0) continue;
                // Existing native oracle uses rational double sample times. Only t=0
                // is identical to this float montage time; do not compare different times.
                var reference = native.RootElement.GetProperty("sequences").EnumerateArray().Single(s => s.GetProperty("source").GetString() == path).GetProperty("poses")[0];
                for (var bone = 0; bone < 79; bone++)
                {
                    var v = reference.GetProperty("pose")[bone]; var p = v.GetProperty("position"); var q = v.GetProperty("rotation"); var s = v.GetProperty("scale");
                    Assert.InRange(Math.Abs(p[0].GetDouble() - pose[bone].Position.X) + Math.Abs(p[1].GetDouble() - pose[bone].Position.Y) + Math.Abs(p[2].GetDouble() - pose[bone].Position.Z), 0, 1e-6);
                    var sign = q[0].GetDouble() * pose[bone].Rotation.X + q[1].GetDouble() * pose[bone].Rotation.Y + q[2].GetDouble() * pose[bone].Rotation.Z + q[3].GetDouble() * pose[bone].Rotation.W < 0 ? -1 : 1;
                    Assert.InRange(Math.Abs(q[0].GetDouble() * sign - pose[bone].Rotation.X) + Math.Abs(q[1].GetDouble() * sign - pose[bone].Rotation.Y) + Math.Abs(q[2].GetDouble() * sign - pose[bone].Rotation.Z) + Math.Abs(q[3].GetDouble() * sign - pose[bone].Rotation.W), 0, 4e-8);
                    Assert.InRange(Math.Abs(s[0].GetDouble() - pose[bone].Scale.X) + Math.Abs(s[1].GetDouble() - pose[bone].Scale.Y) + Math.Abs(s[2].GetDouble() - pose[bone].Scale.Z), 0, 3e-8);
                }
            }
        }
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(120)]
    public void RealTransitionSlotSamplesPhysicalClocksAndRetryDoesNotAdvanceThem(int hz)
    {
        var data = Data.Value; var bank = new AlsMontageRuntime([], sequences: data.Montages.Assets);
        var sampler = data.Pose.CreateSampler(); var mixer = new AlsMontageSlotPose(data.Base, data.Pose.Parents, data.Pose.CurveNames.Length);
        var result = new AlsPrecisePose[79]; var repeat = new AlsPrecisePose[79];
        var sourceCurves = new AlsInertialCurve[data.Pose.CurveNames.Length]; sourceCurves[0] = new(.42f);
        var curves = new AlsInertialCurve[sourceCurves.Length]; var repeatCurves = new AlsInertialCurve[curves.Length]; var changed = 0;
        for (var frame = 0; frame < hz * 4; frame++)
        {
            var id = new AlsFrameIdentity(frame, 9, 1); bank.Begin(id, 1f / hz);
            if (frame is 0 or 10 or 20) data.Montages.Play(bank, id, [new(id, 0, data.Notifications[frame == 10 ? 1 : 0].Bindings[0])]);
            var clocks = bank.Candidate.ToArray();
            mixer.Evaluate(bank.Frame, id, AlsMontageSlot.Transition, data.Base, sourceCurves, result, curves, sampler);
            mixer.Evaluate(bank.Frame, id, AlsMontageSlot.Transition, data.Base, sourceCurves, repeat, repeatCurves, sampler);
            Assert.Equal(result, repeat); Assert.Equal(curves, repeatCurves); Assert.Equal(clocks, bank.Candidate.ToArray()); Assert.Equal(sourceCurves[0], curves[0]);
            if (!result.SequenceEqual(data.Base)) changed++;
            if (frame % 17 == 16)
            {
                bank.Discard(); bank.Begin(id, 1f / hz);
                mixer.Evaluate(bank.Frame, id, AlsMontageSlot.Transition, data.Base, sourceCurves, repeat, repeatCurves, sampler);
                Assert.Equal(result, repeat); Assert.Equal(curves, repeatCurves);
            }
            bank.Commit(id);
        }
        Assert.True(changed > 20); Assert.Empty(bank.Committed.ToArray()); Assert.Equal(data.Base, result); Assert.Equal(sourceCurves, curves);
    }

    [Fact]
    public void InvalidSamplesDoNotPartiallyPublishAndForeignLayoutsAreRejected()
    {
        var data = Data.Value; var sampler = data.Pose.CreateSampler(); var asset = data.Montages.Assets[0];
        var valid = new AlsMontageEvaluation(1, asset.AnimationId, AlsMontageSlot.Transition, .3f, 1) { AdditiveType = 2 };
        var pose = new AlsPrecisePose[79]; var curves = new AlsInertialCurve[data.Pose.CurveNames.Length]; sampler.Sample(valid, pose, curves);
        var saved = pose.ToArray(); var savedCurves = curves.ToArray();
        foreach (var bad in new[] { valid with { Slot = AlsMontageSlot.Grounded }, valid with { AnimationId = -1 }, valid with { AdditiveType = 0 },
            valid with { Position = float.NaN }, valid with { Position = -1 }, valid with { Position = asset.Duration + .1f }, valid with { ActionDefinitionId = 0 } })
        {
            Assert.Throws<ArgumentException>(() => sampler.Sample(bad, pose, curves)); Assert.Equal(saved, pose); Assert.Equal(savedCurves, curves);
        }
        Assert.Throws<ArgumentException>(() => new AlsRefactoredTransitionPose(data.Catalog, data.Montages, data.Pose.BoneNames, data.Pose.Parents, []));
        var bones = data.Pose.BoneNames.ToArray(); (bones[1], bones[2]) = (bones[2], bones[1]);
        Assert.Throws<ArgumentException>(() => new AlsRefactoredTransitionPose(data.Catalog, data.Montages, bones, data.Pose.Parents, data.Pose.CurveNames));
        var duplicates = data.Pose.CurveNames.ToArray(); duplicates[0] = duplicates[1].ToUpperInvariant();
        Assert.Throws<ArgumentException>(() => new AlsRefactoredTransitionPose(data.Catalog, data.Montages, data.Pose.BoneNames, data.Pose.Parents, duplicates));
    }
}
