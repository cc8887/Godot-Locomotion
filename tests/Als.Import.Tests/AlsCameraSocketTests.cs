using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using GodotAls.Import.Inspection;

namespace GodotAls.Import.Tests;

public sealed class AlsCameraSocketTests
{
    private static string Source() => File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "assets", "config", "refactored_camera_sockets.json"));
    [Fact]
    public void CameraSocketsRetainActualHeadBindingsAndNativeLocalAxes()
    {
        var sockets = AlsCameraSocketCompiler.Compile(Source());
        Assert.Equal(3, sockets.Count); Assert.All(sockets.Values, s => Assert.Equal("head", s.Bone));
        Assert.Equal(new AlsDoubleVector(4, 14, 0), sockets["FirstPersonCamera"].Local.Position);
        Assert.Equal(new AlsDoubleVector(5, 0, -20), sockets["ThirdPersonTraceShoulderLeft"].Local.Position);
        Assert.Equal(new AlsDoubleVector(5, 0, 20), sockets["ThirdPersonTraceShoulderRight"].Local.Position);
    }
    [Theory]
    [InlineData("duplicate")] [InlineData("missing")] [InlineData("rotation")] [InlineData("scale")] [InlineData("owner")]
    public void RejectsInvalidOrForeignSockets(string mutation)
    {
        var root = JsonNode.Parse(Source())!; var sockets = root["sockets"]!.AsArray();
        if (mutation == "duplicate") sockets[1]!["name"] = "firstpersoncamera";
        if (mutation == "missing") sockets.RemoveAt(2);
        if (mutation == "rotation") sockets[0]!["rotation"]![3] = 2;
        if (mutation == "scale") sockets[0]!["scale"]![0] = 0;
        if (mutation == "owner") sockets[0]!["source"] = "/Foreign:Socket";
        Assert.Throws<ArgumentException>(() => AlsCameraSocketCompiler.Compile(root.ToJsonString()));
    }
}
