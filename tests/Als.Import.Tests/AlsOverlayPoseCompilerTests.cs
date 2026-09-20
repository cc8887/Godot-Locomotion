using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsOverlayPoseCompilerTests
{
    internal static readonly Lazy<AlsOverlayPoseDefinition> Definition = new(() =>
    {
        var set = P3RepositoryFixtures.LoadAnimationSet();
        var layer = AlsAimPoseCompilerTests.Read("v4_layering_inputs.json"); var overlay = AlsAimPoseCompilerTests.Read("v4_overlay_inputs.json");
        var sources = AlsOverlaySourceCompiler.Compile(layer, overlay, set);
        return AlsOverlayPoseCompiler.Compile(layer, overlay, set, sources, AlsOverlayStateCompiler.Compile(layer, overlay, set, sources));
    });

    [Fact]
    public void CompilesEveryReachableOverlayOperationAndPreservesActualPinValues()
    {
        var graph = Definition.Value; var nodes = graph.Nodes.ToArray();
        Assert.Equal(295, nodes.Length); Assert.Equal(148, nodes.Count(n => n.Kind == AlsOverlayPoseKind.Source));
        Assert.Equal(Enumerable.Range(0, 148), nodes.Where(n => n.Kind == AlsOverlayPoseKind.Source).Select(n => n.Source).Order());
        Assert.Equal(25, nodes.Count(n => n.Kind == AlsOverlayPoseKind.StateRoot));
        Assert.Equal(5, nodes.Count(n => n.Kind == AlsOverlayPoseKind.Machine));
        Assert.Equal(43, nodes.Count(n => n.Kind == AlsOverlayPoseKind.TwoWay));
        Assert.Equal(24, nodes.Count(n => n.Kind == AlsOverlayPoseKind.MultiWay));
        Assert.Equal(23, nodes.Count(n => n.Kind == AlsOverlayPoseKind.LocalAdditive));
        Assert.Equal(12, nodes.Count(n => n.Kind == AlsOverlayPoseKind.MeshAdditive));
        Assert.Equal(4, nodes.Count(n => n.Kind == AlsOverlayPoseKind.ModifyCurve));
        Assert.Equal(9, nodes.Count(n => n.Kind == AlsOverlayPoseKind.BlendList));
        Assert.Equal(3, nodes.Count(n => n.InertialTransition));
        Assert.Equal(6, nodes.Count(n => n.ResetChild));
        // Exported editable struct defaults are not the generated exposed pin values.
        Assert.Equal(new[] { .25f, .5f, .75f, 1f }, nodes.Where(n => n.Kind == AlsOverlayPoseKind.LocalAdditive).Select(n => n.Values[0].Constant).Distinct().Order());
        Assert.Contains(nodes, n => n.Kind == AlsOverlayPoseKind.BlendList && n.BlendTimes[0] == .75f && n.BlendTimes[1] == .2f);
        Assert.Contains(nodes, n => n.Kind == AlsOverlayPoseKind.ModifyCurve && n.Values[0].Constant == .5f);
        Assert.Contains(nodes, n => n.Kind == AlsOverlayPoseKind.TwoWay && n.Alpha.Interpolate && n.Alpha.Increasing == 0 && n.Alpha.Decreasing == 5);
        Assert.All(nodes.Where(n => n.Kind == AlsOverlayPoseKind.Machine), node =>
            Assert.Equal(graph.States.Machines[node.Machine].States.ToArray().Where(s => !s.Conduit).Select(s => s.RootIndex), node.Inputs.ToArray()));
    }
}
