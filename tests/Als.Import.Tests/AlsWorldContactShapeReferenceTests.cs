using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsWorldContactShapeReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void IslandCollisionShapesMatchNativeDetectionTransformsFromIndependentIntegrationInputs()
    {
        string Read(string name) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + name));
        using var doc = JsonDocument.Parse(Read("v4_physics_world_shape_window.json"));
        var authored = Read("v4_physics_asset_inputs.json"); var shapesJson = Read("v4_physics_runtime_shapes.json");
        var checkedShapes = 0; double maxP = 0, maxQ = 0;
        foreach (var row in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var setup = row.GetProperty("setup"); var mesh = setup.GetProperty("mesh").GetString()!;
            var asset = AlsPhysicsAssetCompiler.Compile(authored, mesh);
            var shapes = AlsRuntimeShapeCompiler.Compile(shapesJson, authored, mesh);
            foreach (var sample in row.GetProperty("samples").EnumerateArray().Skip(1))
            {
                var observations = sample.GetProperty("stepObservations").EnumerateArray().ToArray();
                var before = observations.Single(o => o.GetProperty("stage").GetString() == "preIntegrate").GetProperty("bodies");
                var preSolve = observations.Single(o => o.GetProperty("stage").GetString() == "preSolve");
                var bodies = asset.Bodies.Select((b, i) => new AlsIslandBody(b.MassLocal,
                    B(setup.GetProperty("bodies")[i], "dynamic") ? new(D(before[i], "inverseMass"), V(before[i], "conditionedInverseInertia")) : default,
                    D(before[i], "linearDamping"), D(before[i], "angularDamping"), B(setup.GetProperty("bodies")[i], "gravity"))).ToArray();
                var states = before.EnumerateArray().Select(b => new AlsIslandBodyState(Pose(b.GetProperty("initialActor")),
                    new(V(b, "v").ToSingle(), V(b, "w").ToSingle()))).ToArray();
                var forces = before.EnumerateArray().Select(b => new AlsBodyStepForces(default,
                    V(b, "angularAcceleration"), V(b, "linearImpulseVelocity"), V(b, "angularImpulseVelocity"))).ToArray();
                var registry = new AlsContactRegistry(bodies.Length, shapes.Length);
                var indices = new Dictionary<(string, int), int>();
                foreach (var s in shapes)
                    indices.Add((asset.Bodies[s.Body].Bone, s.NativeIndex), registry.Register(new(s.Body, s.LeafLocal, 1, 1)).Slot);
                var source = new ObserveBounds();
                var contacts = new AlsWorldContacts(registry, source, new(0, 0, 0), new((float)D(row, "dtUsed"), 0, 0));
                var island = new AlsJointIsland(bodies, [], states);
                island.Step(D(row, "dtUsed"), V(setup, "gravity"), forces, contacts);
                foreach (var pair in preSolve.GetProperty("contacts").EnumerateArray())
                for (var side = 0; side < 2; side++)
                {
                    var name = pair.GetProperty("body" + side).GetString()!;
                    if (name.StartsWith("environment_", StringComparison.Ordinal)) continue;
                    var index = indices[(name, pair.GetProperty("shape" + side).GetInt32())];
                    Assert.Equal(shapes[index].LeafLocal, Pose(pair.GetProperty("shapeRelative" + side)));
                    var expected = Pose(pair.GetProperty("shapeWorld" + side)); var actual = source.Shapes[index];
                    maxP = Math.Max(maxP, Math.Sqrt((actual.Position - expected.Position).LengthSquared));
                    var q = actual.Rotation + -expected.Rotation;
                    maxQ = Math.Max(maxQ, Math.Max(Math.Max(Math.Abs(q.X), Math.Abs(q.Y)), Math.Max(Math.Abs(q.Z), Math.Abs(q.W))));
                    checkedShapes++;
                }
            }
        }
        output.WriteLine($"NATIVE_WORLD_SHAPES endpoints={checkedShapes} max_p={maxP:R} max_q={maxQ:R}");
        Assert.Equal(190, checkedShapes); Assert.Equal(0, maxP); Assert.Equal(0, maxQ);
    }

    // Exercise the production island -> world contact boundary while observing
    // shapes before queries or corrections. Native outputs are assertions only.
    private sealed class ObserveBounds : IAlsContactGeometrySource
    {
        public AlsPrecisePose[] Shapes { get; private set; } = [];
        public void PrepareBounds(ReadOnlySpan<AlsPrecisePose> shapes) => Shapes = shapes.ToArray();
        public bool AllowsPair(int a, int b) => false;
        public int Query(int a, in AlsPrecisePose wa, int b, in AlsPrecisePose wb, Span<AlsDetectedContact> points) => throw new InvalidOperationException();
    }
}
