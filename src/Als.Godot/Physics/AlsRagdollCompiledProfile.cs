using System.Text.Json;
using Godot;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Physics;

// Asset data is shared by characters; only the island, contact registry and
// animation state belong to an individual ragdoll activation.
internal sealed class AlsRagdollCompiledProfile
{
    private static readonly Dictionary<string, AlsRagdollCompiledProfile> Profiles = new(StringComparer.Ordinal);

    internal AlsRagdollPhysicsDefinition AuthoredDefinition { get; }
    internal AlsRagdollPhysicsDefinition Definition { get; }
    internal AlsPhysicsJointSettings[] Settings { get; }
    internal AlsConditionedBodyInertia[] Conditioning { get; }
    internal AlsRigSleepSettings Sleep { get; }
    internal AlsIslandJoint[] Joints { get; }
    internal AlsContactDetectorSettings Detector { get; }
    internal AlsContactRuntimeSettings Contact { get; }
    internal AlsRagdollShapeProfile Shapes { get; }

    private AlsRagdollCompiledProfile(string mesh)
    {
        AuthoredDefinition = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs.json"), mesh);
        Definition = AlsPhysicsJointFrameCompiler.Compile(Read("v4_physics_joint_frame_inputs.json"), AuthoredDefinition);
        Settings = AlsPhysicsJointCompiler.Compile(Read("v4_physics_joint_reference.json"), Definition);
        Conditioning = AlsBodyInertiaCompiler.Compile(Read("v4_physics_inertia_reference.json"), Definition, Settings);
        Sleep = AlsSleepSettingsCompiler.Compile(Read("v4_physics_sleep_settings.json"), Definition);
        using var reference = JsonDocument.Parse(Read("v4_physics_awake_solver_reference.json"));
        var solver = reference.RootElement.GetProperty("cases")[0].GetProperty("solverSettings");
        Joints = Definition.Joints.Select(j => AlsCachedJointSettingsCompiler.IslandJoint(j.ParentBody, j.ChildBody,
            j.ParentFrame, j.ChildFrame, Settings[j.Index].NativeSettings, solver)).ToArray();
        Detector = AlsContactDetectorCompiler.Compile(Read("v4_physics_cull_reference.json"));
        Contact = AlsContactRuntimeSettingsCompiler.Compile(Read("v4_physics_contact_settings.json"), Definition);
        Shapes = AlsRagdollShapeProfile.Compile(Definition);
    }

    internal static AlsRagdollCompiledProfile Get(string mesh)
    {
        if (!GodotThread.IsMainThread()) throw new InvalidOperationException("Ragdoll asset preparation requires Main.");
        if (!Profiles.TryGetValue(mesh, out var profile))
        {
            profile = new(mesh);
            Profiles.Add(mesh, profile);
        }
        return profile;
    }

    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
}

internal sealed class AlsRagdollShapeProfile
{
    internal IReadOnlyDictionary<(int Body, int Shape), AlsConvexTopology> Cooked { get; }
    internal IReadOnlyDictionary<(int Body, int Shape), AlsConvexProperties> Properties { get; }
    internal IReadOnlyDictionary<(int Body, int Shape), AlsRuntimeShape> Runtime { get; }
    internal IReadOnlyDictionary<(int Body, int Shape), AlsPrimitiveGeometry> Primitives { get; }
    internal IReadOnlyDictionary<(int Body, int Shape), AlsObservedSimulationFilter> Filters { get; }

    private AlsRagdollShapeProfile(
        IReadOnlyDictionary<(int Body, int Shape), AlsConvexTopology> cooked,
        IReadOnlyDictionary<(int Body, int Shape), AlsConvexProperties> properties,
        IReadOnlyDictionary<(int Body, int Shape), AlsRuntimeShape> runtime,
        IReadOnlyDictionary<(int Body, int Shape), AlsPrimitiveGeometry> primitives,
        IReadOnlyDictionary<(int Body, int Shape), AlsObservedSimulationFilter> filters)
    { Cooked = cooked; Properties = properties; Runtime = runtime; Primitives = primitives; Filters = filters; }

    internal static AlsRagdollShapeProfile Compile(AlsRagdollPhysicsDefinition definition)
    {
        static string Read(string name) => Godot.FileAccess.GetFileAsString("res://assets/config/" + name);
        var authored = Read("v4_physics_asset_inputs.json");
        var runtimeJson = Read("v4_physics_runtime_shapes.json");
        var cooked = AlsConvexTopologyCompiler.Compile(Read("v4_physics_convex_topology.json"), definition)
            .ToDictionary(s => (s.Body, s.Shape), s => s.Topology);
        var properties = AlsConvexPropertiesCompiler.Compile(Read("v4_physics_convex_properties.json"), definition, cooked);
        var runtime = AlsRuntimeShapeCompiler.Compile(runtimeJson, authored, definition.Mesh)
            .ToDictionary(s => (s.Body, s.Shape));
        var primitives = AlsPrimitiveGeometryCompiler.Compile(Read("v4_physics_primitive_geometry.json"),
            runtimeJson, definition.Mesh).ToDictionary(s => (s.Body, s.Shape));
        var filters = AlsSimulationFilterCompiler.Compile(Read("v4_physics_simulation_filters.json"),
            runtimeJson, authored, definition.Mesh);
        return new(cooked, properties, runtime, primitives, filters);
    }
}
