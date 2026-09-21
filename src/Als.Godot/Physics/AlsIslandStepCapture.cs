using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Physics;

// Opt-in Main diagnostic. Input contains gathered contact rows, not a claimed
// native narrow-phase result. Native replay must recompute every solver output.
internal sealed class AlsIslandStepCapture(AlsRagdollPhysicsDefinition definition, AlsPhysicsJointSettings[] settings,
    JsonElement solverSettings, AlsWorldContacts contacts, Func<int> frame, HashSet<int> frames, string directory) : IAlsIslandStepObserver
{
    private readonly List<object> _samples = [];
    private object? _input;
    private int _capturedFrame;
    public bool Enabled => frames.Contains(frame());
    public void Begin(double dt, int positionIterations, int velocityIterations, ReadOnlySpan<AlsIslandBody> bodies, ReadOnlySpan<AlsIslandJoint> joints,
        ReadOnlySpan<AlsPrecisePose> initial, ReadOnlySpan<AlsPrecisePose> predicted,
        ReadOnlySpan<AlsProjectionVelocity> velocity, ReadOnlySpan<int> order)
    {
        if (!Godot.GodotThread.IsMainThread()) throw new InvalidOperationException("Step export requires Main.");
        _samples.Clear(); _capturedFrame = frame(); var bodyInputs = new object[bodies.Length];
        for (var i = 0; i < bodies.Length; i++) bodyInputs[i] = new
        {
            name = i < definition.Bodies.Length ? definition.Bodies[i].Bone : $"environment_{i}",
            inverseMass = bodies[i].InverseMass.Mass, inverseInertia = V(bodies[i].InverseMass.Inertia),
            level = contacts.PreparedBodyLevelAt(i),
            initial = Pose(initial[i]), predicted = Pose(predicted[i]), v = V(velocity[i].Linear), w = V(velocity[i].Angular)
        };
        var jointInputs = new object[order.Length];
        for (var i = 0; i < order.Length; i++)
        {
            var j = joints[order[i]]; var assetJoint = definition.Joints.Single(a => a.ParentBody == j.Parent && a.ChildBody == j.Child);
            jointInputs[i] = new { id = order[i], parent = j.Parent, child = j.Child, parentFrame = Pose(j.ParentFrame),
                childFrame = Pose(j.ChildFrame), jointSettings = settings[assetJoint.Index].NativeSettings, projection = j.Projection };
        }
        var contactInputs = new object[contacts.PreparedPairCount];
        for (var i = 0; i < contactInputs.Length; i++)
        {
            var pair = contacts.PreparedPairAt(i); var points = new object[pair.PointCount];
            for (var p = 0; p < points.Length; p++)
            {
                var c = contacts.PreparedPointAt(i, p);
                points[p] = new { arm0 = V(c.Arm0), arm1 = V(c.Arm1), normal = V(c.Normal), u = V(c.TangentU), v = V(c.TangentV),
                    error = V(c.Error), targetVelocity = c.TargetVelocity, disablePosition = c.DisablePosition,
                    disableVelocity = c.DisableVelocity, disableFriction = c.DisableFriction };
            }
            contactInputs[i] = new { body0 = pair.Body0, body1 = pair.Body1, material = pair.Material, points };
        }
        _input = new { mesh = definition.Mesh, frame = _capturedFrame, dt, positionIterations, velocityIterations,
            solverSettings, contactShock = contacts.UsesGraphLevels ? contacts.ShockSettings : new AlsContactShockSettings(0, 0, 1, 1),
            bodies = bodyInputs, joints = jointInputs, contacts = contactInputs };
    }
    public void Capture(string stage, int iteration, ReadOnlySpan<AlsPrecisePose> predicted,
        ReadOnlySpan<AlsProjectionDelta> delta, ReadOnlySpan<AlsProjectionVelocity> velocity)
    {
        var bodies = new object[predicted.Length];
        for (var i = 0; i < bodies.Length; i++) bodies[i] = new { predicted = Pose(predicted[i]),
            dp = V(delta[i].Position), dq = V(delta[i].Rotation), v = V(velocity[i].Linear), w = V(velocity[i].Angular) };
        _samples.Add(new { stage, iteration, bodies });
    }
    public void Complete()
    {
        var path = Path.Combine(directory, $"{definition.Mesh.Split('.').Last()}-{_capturedFrame}.json");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(file, new { schemaVersion = 1, input = _input, coreSamples = _samples },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
        Godot.GD.Print($"CORE_STEP_CAPTURE mesh={definition.Mesh} frame={_capturedFrame} stages={_samples.Count} output={path}");
    }
    private static object Pose(AlsPrecisePose p) => new { position = V(p.Position), rotation = new[] { p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W } };
    private static double[] V(AlsDoubleVector v) => [v.X, v.Y, v.Z];
    private static float[] V(NVector v) => [v.X, v.Y, v.Z];
}
