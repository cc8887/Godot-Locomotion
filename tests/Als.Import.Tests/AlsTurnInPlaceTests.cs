using System.Text.Json.Nodes;
using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsTurnInPlaceTests
{
    [Fact]
    public void NativeSkeletonPlacesBothTurnSlotsInTheGroundedGroup()
    {
        var assets = AlsTurnInPlaceCompiler.CompileMontageAssets(Read("v4_turn_montage_inputs.json"), Bindings.Value.Set, Bindings.Value.Pose);
        Assert.Equal(8, assets.Length); Assert.All(assets, a => Assert.Equal(1, a.GroupId));
        Assert.Equal(4, assets.Count(a => a.Slot == AlsTurnSlot.Standing));
        Assert.Equal(4, assets.Count(a => a.Slot == AlsTurnSlot.Crouching));
        foreach (var asset in assets) Assert.Equal(Bindings.Value.Set.Animations[asset.AnimationId].PlayLength, asset.Duration);
        var owner = new GodotAls.Core.Actions.AlsMontageRuntime(assets);
        owner.Begin(new(1, 1, 1), .1f);
        owner.Play(new(assets[0].AnimationId, assets[0].Slot, 1.2f, 0, .2f, .2f, 1, 0));
        owner.Play(new(assets[4].AnimationId, assets[4].Slot, 1.2f, 0, .2f, .2f, 1, 0));
        Assert.True(owner.Candidate[0].Interrupted); Assert.False(owner.Observations[0].Active);
        Assert.True(owner.Observations[1].Active);
    }

    [Theory]
    [InlineData("rate")] [InlineData("root-motion")] [InlineData("slot")]
    public void RejectsIncompatibleNativeMontageInputs(string mutation)
    {
        var root = JsonNode.Parse(Read("v4_turn_montage_inputs.json"))!;
        if (mutation == "rate") root["assets"]![0]!["rateScale"] = 2;
        else if (mutation == "root-motion") root["assets"]![0]!["rootMotionEnabled"] = true;
        else root["skeletonText"] = root["skeletonText"]!.GetValue<string>().Replace("(N) Turn/Rotate", "Wrong Slot", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => AlsTurnInPlaceCompiler.CompileMontageAssets(root.ToJsonString(), Bindings.Value.Set, Bindings.Value.Pose));
    }
    private static readonly Lazy<(AlsAnimationSetDefinition Set, AlsPoseAnimationProfile Pose)> Bindings = new(() =>
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var locomotion = AlsLocomotionProfileCompiler.Compile(P3RepositoryFixtures.ReadProfile(), set);
        var pose = AlsPoseProfileCompiler.Compile(Read("p4_pose_profile.json"), set, locomotion);
        return (set, pose);
    });

    [Theory]
    [InlineData(-179, -180)] [InlineData(-130, -180)] [InlineData(-129.999, -90)]
    [InlineData(-1, -90)] [InlineData(0, 90)] [InlineData(129.999, 90)]
    [InlineData(130, 180)] [InlineData(180, 180)]
    public void NativeThresholdSignAndStanceSelectExactExportedAssets(double angle, int nominal)
    {
        var model = Compile(); Assert.Equal(130, model.Turn180Threshold);
        foreach (var stance in new[] { AlsStance.Standing, AlsStance.Crouching })
        {
            var selected = model.Select(angle, stance);
            Assert.Equal(nominal, selected.AnimatedAngle); Assert.Equal(1.2f, selected.PlayRate);
            Assert.Equal(stance == AlsStance.Standing, selected.ScaleTurnAngle);
            Assert.Equal((AlsTurnSlot)stance, selected.Slot);
            var expectedName = "ALS_" + (stance == AlsStance.Standing ? "N" : "CLF") + "_TurnIP_" +
                (nominal < 0 ? "L" : "R") + System.Math.Abs(nominal);
            Assert.Equal(expectedName, Bindings.Value.Set.Animations.Single(a => a.Id == selected.AnimationId).Name);
        }
    }

    [Fact]
    public void PlaybackUsesRequestedTimeAndRateButOnlyStandingScalesByAngle()
    {
        var model = Compile(); var request = Request(-100) with { PlayRateScale = 1.5f, StartTime = .25f };
        foreach (var stance in new[] { AlsStance.Standing, AlsStance.Crouching })
        {
            var result = model.Evaluate(request, 0, stance, .7f, []);
            Assert.True(result.AttemptPlayback); Assert.Equal(-100, result.TurnAngle, 4);
            Assert.Equal(stance == AlsStance.Standing ? 2 : 1.8f, result.RotationScale, 4);
            Assert.Equal(1.8f, result.Command.PlayRate, 5); Assert.Equal(.25f, result.Command.StartTime);
            Assert.Equal(.2f, result.Command.BlendInTime); Assert.Equal(.2f, result.Command.BlendOutTime);
            Assert.Equal(1, result.Command.LoopCount); Assert.Equal(0, result.Command.BlendOutTriggerTime);
            Assert.Equal(result, model.Evaluate(request, 0, stance, .7f, []));
        }
    }

    [Fact]
    public void NativeQueryReturnsAtFirstEligibleTrackEvenWhenLaterAssetMatches()
    {
        var model = Compile(); var asset = model.Select(90, AlsStance.Standing);
        var matching = Observation(asset); var other = matching with { AnimationId = asset.AnimationId + 1 };
        Assert.False(AlsTurnInPlaceModel.IsPlayingSlotAnimation(asset, [other, matching]));
        Assert.True(AlsTurnInPlaceModel.IsPlayingSlotAnimation(asset, [matching, other]));
        Assert.True(model.Evaluate(Request(90), 0, AlsStance.Standing, .7f, [other, matching]).AttemptPlayback);
    }

    [Theory]
    [InlineData("inactive")] [InlineData("paused")] [InlineData("persistent")]
    [InlineData("missing-slot")] [InlineData("other-slot")] [InlineData("two-segments")]
    public void IneligibleMontagesDoNotSuppressPlayback(string reason)
    {
        var model = Compile(); var asset = model.Select(90, AlsStance.Standing); var matching = Observation(asset);
        var ignored = reason switch
        {
            "inactive" => matching with { Active = false }, "paused" => matching with { Playing = false },
            "persistent" => matching with { Transient = false }, "missing-slot" => matching with { HasSlot = false },
            "other-slot" => matching with { Slot = AlsTurnSlot.Crouching }, _ => matching with { SegmentCount = 2 },
        };
        Assert.False(AlsTurnInPlaceModel.IsPlayingSlotAnimation(asset, [ignored]));
        Assert.True(AlsTurnInPlaceModel.IsPlayingSlotAnimation(asset, [ignored, matching]));
        Assert.True(model.Evaluate(Request(90), 0, AlsStance.Standing, .7f, [ignored]).AttemptPlayback);
    }

    [Fact]
    public void DuplicateHoldsScaleWhileOverrideAttemptsPlaybackAndRewritesIt()
    {
        var model = Compile(); var request = Request(100); var matching = Observation(model.Select(100, AlsStance.Standing));
        var held = model.Evaluate(request, 0, AlsStance.Standing, .7f, [matching]);
        Assert.False(held.AttemptPlayback); Assert.Equal(.7f, held.RotationScale); Assert.Equal(default, held.Command);
        var replaced = model.Evaluate(request with { OverrideCurrent = true }, 0, AlsStance.Standing, .7f, [matching]);
        Assert.True(replaced.AttemptPlayback); Assert.Equal(4f / 3, replaced.RotationScale, 5);
        var absent = model.Evaluate(default, 0, AlsStance.Standing, .7f, []);
        Assert.False(absent.AttemptPlayback); Assert.Equal(.7f, absent.RotationScale);
    }

    [Fact]
    public void ScaleIsWrittenAfterAnAttemptEvenWithZeroPlaybackRate()
    {
        var model = Compile(); var request = Request(90) with { PlayRateScale = 0 };
        var decision = model.Evaluate(request, 0, AlsStance.Standing, .7f, []);
        Assert.True(decision.AttemptPlayback); Assert.Equal(0, decision.Command.PlayRate); Assert.Equal(0, decision.RotationScale);
        // No playback success input exists: native Blueprint does not branch on the returned montage.
    }

    [Theory]
    [InlineData("comparison")] [InlineData("direction")] [InlineData("query")]
    [InlineData("scale")] [InlineData("local")] [InlineData("actor")] [InlineData("asset-field")]
    [InlineData("slot")] [InlineData("rate")] [InlineData("angle")] [InlineData("play-owner")]
    public void RejectsSourceChangesThatWouldInvalidateTheRuntime(string mutation)
    {
        var root = JsonNode.Parse(Read())!;
        var graph = root["graphs"]!.AsArray().Single(g => g!["name"]!.GetValue<string>() == "TurnInPlace")!;
        var native = graph["nativeText"]!.GetValue<string>(); var defaults = root["defaultsText"]!.GetValue<string>();
        (string before, string after) = mutation switch
        {
            "comparison" => ("Less_DoubleDouble", "LessEqual_DoubleDouble"),
            "direction" => ("MemberName=\"N_TurnIP_L90\"", "MemberName=\"N_TurnIP_R90\""),
            "query" => ("IsPlayingSlotAnimation", "IsSlotActive"),
            "scale" => ("Divide_DoubleDouble", "Multiply_DoubleDouble"),
            "local" => ("MemberScope=\"TurnInPlace\"", "MemberScope=\"TurnInPlaceCheck\""),
            "actor" => ("K2_GetActorRotation", "GetControlRotation"),
            "asset-field" => ("TargetTurnAsset_AnimatedAngle_11_0BE5A89E455AEBE851DAA49A1FCA81A5", "TargetTurnAsset_PlayRate_8_47A596764C9AFE145D75C49448F776A8"),
            "play-owner" => ("MemberName=\"PlaySlotAnimationAsDynamicMontage\",bSelfContext=True", "MemberName=\"PlaySlotAnimationAsDynamicMontage\",bSelfContext=False"),
            "slot" => ("(N) Turn/Rotate", "Grounded Slot"),
            "rate" => ("=1.200000", "=1.300000"),
            _ => ("=-90.000000", "=-80.000000"),
        };
        if (mutation is "slot" or "rate" or "angle")
        { Assert.Contains(before, defaults); root["defaultsText"] = defaults.Replace(before, after, StringComparison.Ordinal); }
        else { Assert.Contains(before, native); graph["nativeText"] = native.Replace(before, after, StringComparison.Ordinal); }
        Assert.ThrowsAny<ArgumentException>(() => Compile(root.ToJsonString()));
    }

    private static AlsTurnSlotObservation Observation(AlsTurnAsset asset) => new(true, true, true, true, asset.Slot, 1, asset.AnimationId);
    private static AlsTurnInPlaceRequest Request(float ueAngle) => new(true, -ueAngle * MathF.PI / 180, 1, 0, false);
    private static AlsTurnInPlaceModel Compile(string? json = null) => AlsTurnInPlaceCompiler.Compile(json ?? Read(), Bindings.Value.Set, Bindings.Value.Pose);
    private static string Read(string name = "v4_idle_control_inputs.json") => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", name));
}
