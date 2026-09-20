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
        foreach (var body in definition.Bodies)
        {
            if (body.Defaults.GetProperty("collisionEnabled").GetString() != "QueryAndPhysics")
                throw new NotSupportedException("Asset body collision mode needs an explicit simulation filter.");
            foreach (var source in body.Shapes)
            {
                if (source.CollisionEnabled is not (0 or 3) || source.RestOffsetCm != 0)
                    throw new NotSupportedException("Asset contact mode/rest offset is not supported.");
                var shape = Create(source); _resources.Add(shape); shape.Margin = 0;
                var local = AlsCachedJointSettingsCompiler.RigidConnector(source.Type == "convex"
                    ? source.Local with { Scale = AlsDoubleVector.One } : source.Local);
                var handle = registry.Register(new(body.Index, local, 1, 1, source.Type is "sphere" or "capsule", source.CollisionEnabled != 0));
                query.Bind(handle, shape);
            }
        }
        foreach (var (a, b) in definition.DisabledCollisions) registry.DisableBodyPair(a, b, true);
    }
    internal void BindFloor(int body, AlsContactRegistry registry, AlsGodotContactQuery query)
    {
        var floor = new BoxShape3D { Size = new(40, 1, 40), Margin = 0 }; _resources.Add(floor);
        query.Bind(registry.Register(new(body, AlsPrecisePose.Identity, 1, 1)), floor);
    }
    private static Shape3D Create(AlsPhysicsShape source) => source.Type switch
    {
        "sphere" => new SphereShape3D { Radius = (float)(source.RadiusCm * .01) },
        "box" => new BoxShape3D { Size = new((float)(source.SizeCm.Y * .01), (float)(source.SizeCm.Z * .01), (float)(source.SizeCm.X * .01)) },
        // Native local Z maps directly to Godot local Y; no extra 90° rotation.
        "capsule" => new CapsuleShape3D { Radius = (float)(source.RadiusCm * .01), Height = (float)((source.CylinderLengthCm + 2 * source.RadiusCm) * .01) },
        "convex" => new ConvexPolygonShape3D { Points = source.VerticesCm.Select(p =>
        {
            var mapped = AlsFootIkCoordinates.FromNative(p * source.Local.Scale);
            return new Vector3(mapped.X, mapped.Y, mapped.Z);
        }).ToArray() },
        _ => throw new NotSupportedException("Unmapped contact shape: " + source.Type)
    };
    public void Dispose()
    {
        if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Shape disposal requires Main.");
        foreach (var resource in _resources) resource.Dispose(); _resources.Clear();
    }
}
