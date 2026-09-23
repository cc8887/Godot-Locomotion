using System.Text.Json;
using System.Text.Json.Nodes;
using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;
using NVector = System.Numerics.Vector3;

namespace GodotAls.Physics;

// Opt-in Main diagnostic. Raw inputs are after history preparation, before Gather;
// this does not claim native narrow-phase or history matching equivalence.
internal sealed class AlsIslandStepCapture(AlsRagdollPhysicsDefinition definition, AlsPhysicsJointSettings[] settings,
    JsonElement solverSettings, AlsWorldContacts contacts, AlsGodotContactQuery query, Func<int> frame, HashSet<int> frames, string directory) : IAlsIslandStepObserver
{
    private readonly List<object> _samples = [];
    private object? _input;
    private int _capturedFrame;
    private readonly List<object> _historyInputs = [];
    private readonly List<int> _historyPairs = [];
    public bool Enabled => frames.Contains(frame());
    public void Begin(double dt, int positionIterations, int velocityIterations, ReadOnlySpan<AlsIslandBody> bodies, ReadOnlySpan<AlsIslandJoint> joints,
        ReadOnlySpan<AlsPrecisePose> initial, ReadOnlySpan<AlsPrecisePose> predicted,
        ReadOnlySpan<AlsProjectionVelocity> velocity, ReadOnlySpan<int> order)
    {
        if (!Godot.GodotThread.IsMainThread()) throw new InvalidOperationException("Step export requires Main.");
        _samples.Clear(); _capturedFrame = frame(); var bodyInputs = new object[bodies.Length];
        _historyInputs.Clear(); _historyPairs.Clear();
        for (var i = 0; i < contacts.HistoryPairCount; i++)
        {
            var count = contacts.HistoryPointCountAt(i); var savedCount = contacts.HistorySavedCountAt(i);
            if (count == 0 && savedCount == 0) continue;
            var saved = new object[savedCount]; var detected = new object[count]; var assigned = new object[count];
            for (var p = 0; p < savedCount; p++)
            {
                var s = contacts.HistorySavedAt(i, p);
                saved[p] = new { anchor0 = V(s.Anchor0), anchor1 = V(s.Anchor1), initialPhi = s.InitialPhi };
            }
            for (var p = 0; p < count; p++)
            {
                var a = contacts.HistoryPreparedAt(i, p); var g = a.Geometry;
                detected[p] = new { point0 = V(g.Point0), point1 = V(g.Point1), normal1 = V(g.Normal1), disabled = a.Disabled };
                assigned[p] = new { anchor0 = V(g.Anchor0), anchor1 = V(g.Anchor1), initialPhi = g.InitialPhi,
                    hasAnchor = g.HasAnchor, initialContact = g.InitialContact, savedIndex = a.SavedIndex };
            }
            var settings = contacts.HistoryGatherAt(i).Settings;
            var state=contacts.HistoryRetainedStateAt(i);var retainedPoints=new object[state.Count];
            for(var p=0;p<retainedPoints.Length;p++)
            {
                var s=contacts.HistoryRetainedPointAt(i,p);var c=s.Contact;
                retainedPoints[p]=new {point0=V(c.Point0),point1=V(c.Point1),normal1=V(c.Normal1),initial0=V(s.Initial0),initial1=V(s.Initial1),disabled=c.Disabled,phi=c.NativePhi};
            }
            var key=contacts.HistoryKeyAt(i);
            _historyPairs.Add(i);
            _historyInputs.Add(new { key = contacts.HistoryKeyAt(i), epoch = contacts.CompletedSteps,
                restored = contacts.HistoryRestoredAt(i), manifoldTolerance = contacts.HistoryToleranceAt(i),
                cullDistance=query.CullDistance((int)key.Shape0.Shape,(int)key.Shape1.Shape),
                retained=new {key=state.Key,epoch=state.Epoch,tolerance=state.Tolerance,positionDelta=V(state.PositionDelta),
                    rotationDelta=new[]{state.RotationDelta.X,state.RotationDelta.Y,state.RotationDelta.Z,state.RotationDelta.W},points=retainedPoints},
                matching = contacts.HistoryMatchingAt(i), initialManifold = settings.InitialManifold,
                priorMinInitialPhi = settings.MinInitialPhi, saved, detected, assigned });
        }
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
                childFrame = Pose(j.ChildFrame), jointSettings = StepSettings(settings[assetJoint.Index].NativeSettings, j), projection = j.Projection };
        }
        var contactInputs = new object[contacts.PreparedPairCount];
        for (var i = 0; i < contactInputs.Length; i++)
        {
            var pair = contacts.PreparedPairAt(i); var points = new object[pair.PointCount];
            var raw = contacts.PreparedGatherAt(i); var geometry = new object[pair.PointCount];
            for (var p = 0; p < points.Length; p++)
            {
                var c = contacts.PreparedPointAt(i, p);
                points[p] = new { arm0 = V(c.Arm0), arm1 = V(c.Arm1), normal = V(c.Normal), u = V(c.TangentU), v = V(c.TangentV),
                    error = V(c.Error), targetVelocity = c.TargetVelocity, disablePosition = c.DisablePosition,
                    disableVelocity = c.DisableVelocity, disableFriction = c.DisableFriction,
                    initialPhi = contacts.PreparedInitialPhiAt(i, p) };
                var g = contacts.PreparedGeometryAt(i, p);
                geometry[p] = new { point0 = V(g.Point0), point1 = V(g.Point1), normal1 = V(g.Normal1),
                    anchor0 = V(g.Anchor0), anchor1 = V(g.Anchor1), hasAnchor = g.HasAnchor, initialContact = g.InitialContact,
                    initialPhi = g.InitialPhi, targetPhi = g.TargetPhi, disablePosition = g.DisablePosition,
                    disableVelocity = g.DisableVelocity, disableFriction = g.DisableFriction };
            }
            var before=new AlsGjkCache();var after=new AlsGjkCache();
            var queried=query.CopyPendingPolygonQuery(contacts.PreparedKeyAt(i),before,after);
            contactInputs[i] = new { key = contacts.PreparedKeyAt(i), body0 = pair.Body0, body1 = pair.Body1, material = pair.Material, points,
                polygonQuery = queried ? new { before=Cache(before),after=Cache(after) } : null,
                gather = new { body0 = GatherBody(raw.Body0), body1 = GatherBody(raw.Body1), settings = raw.Settings, points = geometry } };
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
        // Complete is before StageCommit; these are actual solver results to be
        // committed, not a claim that the surrounding island has published.
        var historyResults = new object[_historyPairs.Count];
        for (var i = 0; i < _historyPairs.Count; i++)
        {
            var pair = _historyPairs[i]; var results = new object[contacts.HistoryPointCountAt(pair)];
            for (var p = 0; p < results.Length; p++)
            {
                var r = contacts.HistoryResultAt(pair, p);
                results[p] = new { ratio = r.FrictionRatio, initialPhi = r.InitialPhi };
            }
            historyResults[i] = results;
        }
        var path = Path.Combine(directory, $"{definition.Mesh.Split('.').Last()}-{_capturedFrame}.json");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(file, new { schemaVersion = 1, input = _input, coreSamples = _samples,
            historyInputs = _historyInputs, historyResults },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
        Godot.GD.Print($"CORE_STEP_CAPTURE mesh={definition.Mesh} frame={_capturedFrame} stages={_samples.Count} output={path}");
    }
    private static object Pose(AlsPrecisePose p) => new { position = V(p.Position), rotation = new[] { p.Rotation.X, p.Rotation.Y, p.Rotation.Z, p.Rotation.W } };
    private static JsonObject StepSettings(JsonElement authored, AlsIslandJoint joint)
    {
        var result = JsonNode.Parse(authored.GetRawText())!.AsObject();
        if (joint.ConnectivityOnly) return result;
        // Begin receives the candidate joints actually used by this step. The
        // immutable asset JSON otherwise replays a static target and old K/C.
        var angular = joint.Angular;
        var target = angular.DriveTarget;
        result["AngularDrivePositionTarget"] = JsonSerializer.SerializeToNode(new[] { target.X, target.Y, target.Z, target.W });
        ReadOnlySpan<AlsAngularAxisSettings> axes = [angular.X, angular.Y, angular.Z];
        for (var i = 0; i < axes.Length; i++)
        {
            var name = i == 0 ? "Twist" : "Swing";
            // Disabled coefficients are not retained by Core; keep the asset
            // metadata on those axes, whose flags exclude them from replay.
            if (authored.GetProperty($"bAngular{name}PositionDriveEnabled").GetBoolean())
                result["AngularDriveStiffness"]![i] = axes[i].DriveStiffness;
            if (authored.GetProperty($"bAngular{name}VelocityDriveEnabled").GetBoolean())
                result["AngularDriveDamping"]![i] = axes[i].DriveDamping;
        }
        return result;
    }
    private static object[] Cache(AlsGjkCache cache)
    {
        var rows=new object[cache.Count];
        for(var i=0;i<rows.Length;i++)rows[i]=new {a=V(cache.WitnessA[i]),b=V(cache.WitnessB[i]),weight=cache.Weights[i]};
        return rows;
    }
    private static object GatherBody(AlsContactGatherBody b) => new { shapeWorld = Pose(b.ShapeWorld),
        centerOfMass = V(b.CenterOfMass), inverseMass = b.InverseMass, v = V(b.Velocity.Linear), w = V(b.Velocity.Angular) };
    private static double[] V(AlsDoubleVector v) => [v.X, v.Y, v.Z];
    private static float[] V(NVector v) => [v.X, v.Y, v.Z];
}
