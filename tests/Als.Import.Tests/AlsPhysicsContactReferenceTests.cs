using System.Numerics;
using System.Text.Json;
using GodotAls.Core.Physics;
using static GodotAls.Import.Tests.AlsPhysicsJointStepReferenceTests;

namespace GodotAls.Import.Tests;

public sealed class AlsPhysicsContactReferenceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("v4_physics_contact_reference.json", false)]
    [InlineData("v4_physics_contact_gather_reference.json", true)]
    public void NativeManifoldMassesPositionFrictionImplicitVelocityAndVelocityRowsMatch(string file, bool gatherGeometry)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/" + file)));
        Assert.Equal(gatherGeometry ? 2 : 1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("UE world COM contact offsets; cm, kg, radians; normal points from body1 to body0", doc.RootElement.GetProperty("coordinates").GetString());
        var cvars = doc.RootElement.GetProperty("cvars");
        Assert.Equal(.5, D(cvars, "p.Chaos.PBDCollisionSolver.Position.StaticFriction.Stiffness"));
        Assert.Equal(1, D(cvars, "p.Chaos.PBDCollisionSolver.Velocity.StaticFriction.Stiffness"));
        Assert.Equal(0, D(cvars, "p.Chaos.PBDCollisionSolver.Velocity.AveragePointEnabled"));
        Assert.Equal(0, D(cvars, "p.Chaos.Solver.Use1DFriction"));
        if (gatherGeometry)
        {
            Assert.Equal(1, D(cvars, "p.Chaos.PBDCollisionSolver.EnableInitialDepenetration"));
            Assert.Equal(0, D(cvars, "p.Chaos.PBDCollisionSolver.RestitutionUsePreIntegrateVelocity"));
        }
        var cases = doc.RootElement.GetProperty("cases"); Assert.Equal(gatherGeometry ? 432 : 288, cases.GetArrayLength());
        var ids = new HashSet<string>(); float maxMass = 0, maxDP = 0, maxDQ = 0, maxV = 0, maxW = 0, maxPush = 0, maxImpulse = 0, maxRatio = 0;
        float maxGather = 0;
        var negativeImpulse = false; var sliding = false; var sticking = false;
        foreach (var row in cases.EnumerateArray())
        {
            var points = row.GetProperty("points"); var count = points.GetArrayLength();
            var id = $"{row.GetProperty("hz")}/{row.GetProperty("bodyMode")}/{row.GetProperty("scenario")}/{row.GetProperty("seeded")}/{count}";
            Assert.True(ids.Add(id)); var dt = (float)D(row, "dt");
            var p = row.GetProperty("bodies")[0]; var c = row.GetProperty("bodies")[1];
            var mass0 = new AlsJointInverseMass(D(p, "inverseMass"), V(p, "inverseInertia"));
            var mass1 = new AlsJointInverseMass(D(c, "inverseMass"), V(c, "inverseInertia"));
            var input = points.EnumerateArray().Select(e => new AlsContactPointInput(Vec(e, "arm0"), Vec(e, "arm1"), Vec(e, "normal"),
                Vec(e, "u"), Vec(e, "v"), Vec(e, "error"), (float)D(e, "targetVelocity"), B(e, "disablePosition"), B(e, "disableVelocity"), B(e, "disableFriction"))).ToArray();
            var material = new AlsContactMaterial((float)D(row, "staticFriction"), (float)D(row, "dynamicFriction"), (float)D(row, "velocityFriction"),
                (float)D(row, "minFrictionPushOut"), (float)D(row, "stiffness"));
            var manifold = new AlsCachedContactManifold(count);
            var seed = row.GetProperty("seed").GetProperty("bodies");
            if (gatherGeometry)
            {
                var g = row.GetProperty("geometry");
                var body0 = new AlsContactGatherBody(Pose(g.GetProperty("shape0")), Pose(p.GetProperty("pose")).Position,
                    (float)mass0.Mass, new(Vec(seed[0], "v"), Vec(seed[0], "w")));
                var body1 = new AlsContactGatherBody(Pose(g.GetProperty("shape1")), Pose(c.GetProperty("pose")).Position,
                    (float)mass1.Mass, new(Vec(seed[1], "v"), Vec(seed[1], "w")));
                var settings = new AlsContactGatherSettings(dt, (float)D(g, "restitution"), (float)D(g, "threshold"),
                    (float)D(g, "maxPushOutVelocity"), (float)D(g, "maxDepenetrationVelocity"), B(g, "perContactInitialPhi"),
                    B(g, "initialManifold"), (float)D(g, "minInitialPhi"));
                var raw = g.GetProperty("points").EnumerateArray().Select(e => new AlsContactGeometry(Vec(e, "point0"), Vec(e, "point1"),
                    Vec(e, "normal1"), Vec(e, "anchor0"), Vec(e, "anchor1"), B(e, "hasAnchor"), B(e, "initialContact"),
                    (float)D(e, "initialPhi"), (float)D(e, "targetPhi"), B(e, "disablePosition"), B(e, "disableVelocity"), B(e, "disableFriction"))).ToArray();
                for (var i = 0; i < count; i++)
                {
                    var actual = AlsContactGather.Gather(raw[i], body0, body1, settings);
                    CheckGather(actual.Point.Arm0, input[i].Arm0); CheckGather(actual.Point.Arm1, input[i].Arm1);
                    CheckGather(actual.Point.Normal, input[i].Normal); CheckGather(actual.Point.TangentU, input[i].TangentU);
                    CheckGather(actual.Point.TangentV, input[i].TangentV); CheckGather(actual.Point.Error, input[i].Error);
                    CheckGather(new(actual.Point.TargetVelocity), new(input[i].TargetVelocity));
                    CheckGather(new(actual.InitialPhi), new((float)D(points[i], "initialPhi")));
                    Assert.Equal(input[i].DisablePosition, actual.Point.DisablePosition);
                    Assert.Equal(input[i].DisableVelocity, actual.Point.DisableVelocity); Assert.Equal(input[i].DisableFriction, actual.Point.DisableFriction);
                    input[i] = actual.Point; // downstream replay consumes Core output, not native gathered inputs
                    void CheckGather(Vector3 a, Vector3 b) => maxGather = MathF.Max(maxGather, Near(a, b, 2e-5f, id + " gather point " + i));
                }
                manifold.GatherGeometry(raw, material, body0, Pose(p.GetProperty("pose")).Rotation, mass0.Inertia,
                    body1, Pose(c.GetProperty("pose")).Rotation, mass1.Inertia, settings);
                for (var i = 0; i < count; i++) Assert.InRange(MathF.Abs(manifold.InitialPhiAt(i) - (float)D(points[i], "initialPhi")), 0, 2e-5f);
            }
            else manifold.Gather(input, material, Pose(p.GetProperty("pose")).Rotation, mass0, Pose(c.GetProperty("pose")).Rotation, mass1);
            for (var i = 0; i < count; i++) maxMass = MathF.Max(maxMass, Near(manifold.PointAt(i).ContactMass, Vec(points[i], "mass"), 2e-5f, id + " mass"));
            var d0 = new AlsProjectionDelta(Vec(seed[0], "dp"), Vec(seed[0], "dq")); var d1 = new AlsProjectionDelta(Vec(seed[1], "dp"), Vec(seed[1], "dq"));
            var v0 = new AlsProjectionVelocity(Vec(seed[0], "v"), Vec(seed[0], "w")); var v1 = new AlsProjectionVelocity(Vec(seed[1], "v"), Vec(seed[1], "w"));
            var positions = row.GetProperty("positionSamples"); Assert.Equal(8, positions.GetArrayLength());
            for (var it = 0; it < 8; it++) { manifold.SolvePosition(ref d0, ref d1, it >= 4); Check(positions[it], $"position {it}"); }
            v0 = AlsCachedJoint.AddImplicitVelocity(v0, d0, D(row, "dt"), mass0.Mass > 0);
            v1 = AlsCachedJoint.AddImplicitVelocity(v1, d1, D(row, "dt"), mass1.Mass > 0);
            Check(row.GetProperty("implicit"), "implicit");
            var velocities = row.GetProperty("velocitySamples"); Assert.Equal(2, velocities.GetArrayLength());
            for (var it = 0; it < 2; it++) { manifold.SolveVelocity(ref v0, ref v1, dt, it == 1); Check(velocities[it], $"velocity {it}"); }
            // A fresh Gather clears per-step lambdas, even after a full solve.
            manifold.Gather(input, material, Pose(p.GetProperty("pose")).Rotation, mass0, Pose(c.GetProperty("pose")).Rotation, mass1);
            for (var i = 0; i < count; i++) { Assert.Equal(Vector3.Zero, manifold.PointAt(i).PushOut); Assert.Equal(Vector3.Zero, manifold.PointAt(i).Impulse); }

            void Check(JsonElement expected, string phase)
            {
                var context = id + " " + phase; var bodies = expected.GetProperty("bodies");
                Body(bodies[0], d0, v0); Body(bodies[1], d1, v1);
                for (var i = 0; i < count; i++)
                {
                    var e = expected.GetProperty("points")[i]; var a = manifold.PointAt(i);
                    maxPush = MathF.Max(maxPush, Near(a.PushOut, Vec(e, "pushOut"), 2e-5f, context + " pushout"));
                    maxImpulse = MathF.Max(maxImpulse, Near(a.Impulse, Vec(e, "impulse"), .001f, context + " impulse"));
                    var ratio = MathF.Abs(a.StaticFrictionRatio - (float)D(e, "frictionRatio")); maxRatio = MathF.Max(maxRatio, ratio);
                    Assert.True(ratio < 5e-5f, context + " friction ratio " + ratio); Assert.True(a.PushOut.X >= 0);
                    negativeImpulse |= a.Impulse.X < -.01f; sliding |= a.StaticFrictionRatio is > 0 and < .9f; sticking |= a.StaticFrictionRatio == 1;
                }
                void Body(JsonElement e, AlsProjectionDelta delta, AlsProjectionVelocity velocity)
                {
                    maxDP = MathF.Max(maxDP, Near(delta.Position, Vec(e, "dp"), 2e-5f, context + " DP"));
                    maxDQ = MathF.Max(maxDQ, Near(delta.Rotation, Vec(e, "dq"), 5e-6f, context + " DQ"));
                    maxV = MathF.Max(maxV, Near(velocity.Linear, Vec(e, "v"), .001f, context + " V"));
                    maxW = MathF.Max(maxW, Near(velocity.Angular, Vec(e, "w"), .0002f, context + " W"));
                }
            }
        }
        Assert.True(negativeImpulse && sliding && sticking, "Fixture must exercise pushout cancellation and both friction cones.");
        output.WriteLine($"NATIVE_CONTACT_ROWS_OK geometry={gatherGeometry} cases={ids.Count} max_gather={maxGather:R} max_mass={maxMass:R} max_dp={maxDP:R} max_dq={maxDQ:R} max_v={maxV:R} max_w={maxW:R} max_pushout={maxPush:R} max_impulse={maxImpulse:R} max_ratio={maxRatio:R}");
    }
    private static Vector3 Vec(JsonElement e, string key) => V(e, key).ToSingle();
    private static float Near(Vector3 a, Vector3 b, float tolerance, string context)
    { var error = Vector3.Distance(a, b); Assert.True(error <= tolerance, $"{context}: error={error:R}, actual={a}, expected={b}"); return error; }
}
