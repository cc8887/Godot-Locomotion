using GodotAls.Import.Compilation;
using GodotAls.Core.Locomotion;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredStanceCallbackTests
{
    [Theory]
    [InlineData(false, 15)]
    [InlineData(true, 12)]
    public void OriginalFunctionsKeepTheirPhasesAndAllSixHipDirections(bool crouching, int count)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var profile = new AlsRefactoredStanceCallbacks(catalog, crouching);
        var nodes = profile.Nodes.ToArray();
        Assert.Equal(count, nodes.Length);
        var hips = nodes.Where(n => n.Function == AlsRefactoredStanceFunction.SetHipsDirection).ToArray();
        Assert.Equal(new[] { "Backward", "Forward", "LeftBackward", "LeftForward", "RightBackward", "RightForward" }, hips.Select(n => n.HipsDirection).Order());
        Assert.All(hips, n => Assert.True(n.OnBecomeRelevant));
        Assert.Equal(crouching ? 0 : 2, nodes.Count(n => n.Function == AlsRefactoredStanceFunction.ResetPivot));
        Assert.Single(nodes, n => n.Function == AlsRefactoredStanceFunction.RefreshGroundedMovement);
        foreach (var change in new[] { "phase", "source", "function", "argument" })
        {
            var payload = JsonNode.Parse(catalog.Read(AlsRefactoredRotatePlayers.Blueprint(crouching)).GetRawText())!;
            var target = payload["compiled"]!["nodes"]!.AsArray().Single(n => n!["propertyIndex"]!.GetValue<int>() == hips[0].PropertyIndex)!;
            if (change == "phase") target["runtime"]!["callSite"] = "OnUpdatePostRecursion";
            if (change == "source") target["runtime"]!["source"]!["linkId"] = -1;
            if (change == "function") payload["nativeText"] = payload["nativeText"]!.GetValue<string>().Replace("MemberName=\"SetHipsDirection\"", "MemberName=\"Unknown\"", StringComparison.Ordinal);
            if (change == "argument") payload["nativeText"] = payload["nativeText"]!.GetValue<string>().Replace("DefaultValue=\"RightBackward\"", "DefaultValue=\"Unknown\"", StringComparison.Ordinal);
            using var changed = JsonDocument.Parse(payload.ToJsonString());
            Assert.Throws<ArgumentException>(() => AlsRefactoredStanceCallbacks.Compile(changed.RootElement, crouching));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelevanceUsesTraversalNotRenderFramesAndCancellationRestoresHistory(bool crouching)
    {
        var catalog = new AlsRefactoredAnimationCatalog(MantlingHostFixture.Read("refactored_animation_sources"), p => File.ReadAllBytes(Path.Combine(RepositoryRoot.Find(), "assets/config", p)));
        var profile = new AlsRefactoredStanceCallbacks(catalog, crouching);
        var runtime = new AlsRefactoredStanceCallbackRuntime(profile);
        var hip = profile.Nodes.ToArray().First(n => n.Function == AlsRefactoredStanceFunction.SetHipsDirection);
        var update = profile.Nodes.ToArray().Single(n => n.Function == AlsRefactoredStanceFunction.RefreshGroundedMovement);
        var counters = new short[] { 32767, -32768, -32766, -32766, -32765 };
        var expected = new[] { true, false, true, false, true };
        for (var frame = 0; frame < counters.Length; frame++)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                runtime.Prepare(frame, new(counters[frame], (ulong)(frame * 100)), initializeInstance: frame == 4);
                Assert.Equal(update, runtime.Enter(frame, update.PropertyIndex));
                Assert.Equal(expected[frame], runtime.Enter(frame, hip.PropertyIndex) != null);
                Assert.Throws<InvalidOperationException>(() => runtime.ValidateCommit(frame));
                runtime.Leave(frame, hip.PropertyIndex);
                runtime.Leave(frame, update.PropertyIndex);
                Assert.Null(runtime.Enter(frame, hip.PropertyIndex)); // same traversal after source returns
                runtime.Leave(frame, hip.PropertyIndex);
                if (attempt == 0) runtime.Cancel(); else runtime.Commit(frame);
            }
        }
        runtime.Prepare(5, new(-32764, 500));
        runtime.Enter(5, update.PropertyIndex);
        Assert.Throws<ArgumentException>(() => runtime.Leave(5, hip.PropertyIndex));
        Assert.Throws<InvalidOperationException>(() => runtime.Commit(5));
        runtime.Cancel();
        runtime.Prepare(5, new(-32764, 500));
        Assert.Null(runtime.Enter(5, hip.PropertyIndex));
        runtime.Leave(5, hip.PropertyIndex);runtime.Commit(5);
    }
}
