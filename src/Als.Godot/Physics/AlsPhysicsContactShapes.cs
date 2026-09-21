using Godot;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Owns geometry resources for the Core query adapter. This uses the Godot world
// basis (native Y,Z,-X), not the imported skeleton's FBX-local basis (X,-Y,Z).
internal sealed class AlsPhysicsContactShapes : IDisposable
{
    private readonly List<Shape3D> _resources = [];
    internal int Count => _resources.Count;
    internal void Bind(AlsRagdollPhysicsDefinition definition, AlsContactRegistry registry, AlsGodotContactQuery query)
    {
        var cooked = AlsConvexTopologyCompiler.Compile(Godot.FileAccess.GetFileAsString(
            "res://assets/config/v4_physics_convex_topology.json"), definition)
            .ToDictionary(s => (s.Body, s.Shape), s => s.Topology);
        var properties = AlsConvexPropertiesCompiler.Compile(Godot.FileAccess.GetFileAsString(
            "res://assets/config/v4_physics_convex_properties.json"), definition, cooked);
        var runtime = AlsRuntimeShapeCompiler.Compile(
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_runtime_shapes.json"),
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), definition.Mesh)
            .ToDictionary(s => (s.Body, s.Shape));
        var primitives = AlsPrimitiveGeometryCompiler.Compile(
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_primitive_geometry.json"),
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_runtime_shapes.json"), definition.Mesh)
            .ToDictionary(s => (s.Body, s.Shape));
        var filters = AlsSimulationFilterCompiler.Compile(
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_simulation_filters.json"),
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_runtime_shapes.json"),
            Godot.FileAccess.GetFileAsString("res://assets/config/v4_physics_asset_inputs.json"), definition.Mesh);
        foreach (var body in definition.Bodies)
        {
            if (body.Defaults.GetProperty("collisionEnabled").GetString() != "QueryAndPhysics")
                throw new NotSupportedException("Asset body collision mode needs an explicit simulation filter.");
            for (var shapeIndex = 0; shapeIndex < body.Shapes.Length; shapeIndex++)
            {
                var source = body.Shapes[shapeIndex];
                if (source.CollisionEnabled is not (0 or 3) || source.RestOffsetCm != 0)
                    throw new NotSupportedException("Asset contact mode/rest offset is not supported.");
                cooked.TryGetValue((body.Index, shapeIndex), out var topology);
                var observed = runtime[(body.Index, shapeIndex)];
                var primitive = primitives.TryGetValue((body.Index, shapeIndex), out var value) ? (AlsPrimitiveGeometry?)value : null;
                // Cooked convex vertices already bake FKConvexElem's local
                // transform. Only the actual runtime leaf wrapper is applied.
                var shape = Create(source, topology, observed, primitive); _resources.Add(shape); shape.Margin = 0;
                var local = AlsCachedJointSettingsCompiler.RigidConnector(observed.LeafLocal);
                var filter = filters[(body.Index, shapeIndex)];
                var handle = registry.Register(new(body.Index, local, 1, 1, source.Type is "sphere" or "capsule",
                    filter.Simulation, filter.Filter));
                query.Bind(handle, shape,topology,source.Type=="box"?primitive!.Value.BoxHalf:null,
                    source.Type=="convex"?observed.Scale:null,source.Type is "box" or "convex"?observed.MarginCm:0,
                    nativeCapsule:primitive?.Capsule, proxyLocal:primitive?.ProxyLocal,
                    nativeSphereRadius:source.Type=="sphere"?primitive!.Value.Radius:null,
                    convexProperties:source.Type=="convex"?properties[(body.Index,shapeIndex)]:null);
            }
        }
        foreach (var (a, b) in definition.DisabledCollisions) registry.DisableBodyPair(a, b, true);
    }
    internal void BindFloor(int body, AlsContactRegistry registry, AlsGodotContactQuery query)
    {
        var floor = new BoxShape3D { Size = new(40, 1, 40), Margin = 0 }; _resources.Add(floor);
        query.Bind(registry.Register(new(body, AlsPrecisePose.Identity, 1, 1, SimulationFilter: AlsSimulationFilter.WorldStatic)), floor,nativeHalf:new(2000,2000,50));
    }
    // Optional observation preserves explicitly authored synthetic diagnostic
    // shapes. Production asset Bind always supplies the native observation.
    internal static Shape3D Create(AlsPhysicsShape source, AlsConvexTopology? topology = null,
        AlsRuntimeShape? observed = null, AlsPrimitiveGeometry? primitive = null) => source.Type switch
    {
        "sphere" => new SphereShape3D { Radius = (float)((primitive?.Radius ?? source.RadiusCm) * .01) },
        "box" => Box(primitive.HasValue ? primitive.Value.BoxHalf * 2 : source.SizeCm),
        // Native local Z maps directly to Godot local Y; no extra 90° rotation.
        "capsule" => new CapsuleShape3D { Radius = (float)((primitive?.Radius ?? source.RadiusCm) * .01),
            Height = (float)(((primitive?.Capsule?.Height ?? source.CylinderLengthCm) + 2 * (primitive?.Radius ?? source.RadiusCm)) * .01) },
        "convex" => CreateConvex(observed.HasValue
            ? source with { Local = observed.Value.LeafLocal with { Scale = observed.Value.Scale } } : source,
            topology ?? throw new InvalidDataException("Cooked convex topology is required.")),
        _ => throw new NotSupportedException("Unmapped contact shape: " + source.Type)
    };
    private static BoxShape3D Box(AlsDoubleVector size) => new() { Size = new((float)(size.Y * .01), (float)(size.Z * .01), (float)(size.X * .01)) };
    private static ConvexPolygonShape3D CreateConvex(AlsPhysicsShape source, AlsConvexTopology topology)
    {
        if (topology.Margin != 0) throw new NotSupportedException("Nonzero native convex margin needs explicit transport.");
        var points = new Vector3[topology.VertexCount];
        for (var i = 0; i < points.Length; i++)
        {
            var mapped = AlsFootIkCoordinates.FromNative(new AlsDoubleVector(topology.VertexAt(i)) * source.Local.Scale);
            points[i] = new(mapped.X, mapped.Y, mapped.Z);
        }
        return new() { Points = points, Margin = 0 };
    }
    public void Dispose()
    {
        if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Shape disposal requires Main.");
        foreach (var resource in _resources) resource.Dispose(); _resources.Clear();
    }
}
