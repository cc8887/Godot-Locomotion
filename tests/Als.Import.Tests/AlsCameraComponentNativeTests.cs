using System.Text.Json;
using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Inspection;
using GodotAls.Import.Runtime;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsCameraComponentNativeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ContinuousSpatialHistoryMatchesActualNativeComponentTicks(bool based)
    {
        var directory = Path.Combine(RepositoryRoot.Find(), "assets", "config");
        var rig = AlsCameraRigDefinition.Compile(File.ReadAllText(Path.Combine(directory, "refactored_camera_inputs.json")));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, based ? "refactored_camera_base_reference.json" : "refactored_camera_component_reference.json")));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("/ALS/ALSCamera/B_Als_CameraComponent.B_Als_CameraComponent_C",doc.RootElement.GetProperty("cameraClass").GetString());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"refactored_camera_inputs.json")))).ToLowerInvariant(),
            doc.RootElement.GetProperty("settingsDigest").GetString());
        int count = 0, firstPerson = 0, thirdPerson = 0, partial = 0, teleports = 0, overrides = 0, baseChanges = 0, basedFrames = 0;
        int translatingFrames = 0, tiltedFrames = 0;
        double maxPosition = 0, maxRotation = 0;
        foreach (var trace in doc.RootElement.GetProperty("traces").EnumerateArray())
        {
            AlsDoubleVector previousBaseLocation = default;
            var initial = trace.GetProperty("initial");
            var state = new AlsCameraFollowState(true, 0, "", V(initial,"PivotTargetLocation"), V(initial,"PivotLagLocation"),
                V(initial,"PivotLocation"), V(initial,"CameraLocation"), R(initial,"rotation"), F(initial,"fov"), F(initial,"ratio"),
                default, AlsQuaternion.Identity);
            Assert.Equal(trace.GetProperty("hz").GetInt32() * 4, trace.GetProperty("frames").GetArrayLength());
            foreach (var row in trace.GetProperty("frames").EnumerateArray())
            {
                var i = row.GetProperty("input"); var c = row.GetProperty("curves"); var expected = row.GetProperty("output");
                var q = i.GetProperty("meshRotation");
                var input = new AlsCameraFollowInput(F(i,"delta"), true, R(i,"view"), V(i,"firstPivot"), V(i,"secondPivot"),
                    V(i,"firstPerson"), V(i,"shoulder"), false, default,
                    new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),F(i,"meshScale"),
                    0,"",false,default,AlsQuaternion.Identity,i.GetProperty("overrideFov").GetBoolean(),F(i,"fovOverride"),F(c,"FovOffset"));
                if (based)
                {
                    input = input with { BaseId = i.GetProperty("baseId").GetInt64(),
                        RelativeBaseRotation = i.GetProperty("relativeBaseRotation").GetBoolean(),
                        BaseLocation = V(i,"baseLocation"), BaseRotation = Q(i,"baseRotation") };
                    if (input.BaseId != state.BaseId) baseChanges++;
                    if (input.RelativeBaseRotation) basedFrames++;
                    if (input.BaseId > 0 && input.BaseId == state.BaseId &&
                        (input.BaseLocation-previousBaseLocation).LengthSquared > .0001) translatingFrames++;
                    if (input.RelativeBaseRotation && Math.Abs(input.BaseRotation.X)+Math.Abs(input.BaseRotation.Y) > .01) tiltedFrames++;
                    previousBaseLocation = input.BaseLocation;
                }
                AlsDoubleVector CurveVector(string prefix) => new(F(c,prefix+"X"),F(c,prefix+"Y"),F(c,prefix+"Z"));
                var curves = new AlsCameraFollowCurves(CurveVector("PivotOffset"),CurveVector("CameraOffset"),
                    F(c,"LocationLagX"),F(c,"LocationLagY"),F(c,"LocationLagZ"),F(c,"RotationLag"),F(c,"TraceOverride"),F(c,"FirstPersonOverride"));
                if (curves.FirstPersonOverride >= .99999) firstPerson++;
                if (curves.FirstPersonOverride > .00001 && curves.FirstPersonOverride < .99999) partial++;
                if (curves.CameraOffset.X < -150) thirdPerson++;
                if (input.OverrideFov) overrides++;
                if (((input.FirstPivotSocket+input.SecondPivotSocket)*.5-state.PivotTarget).LengthSquared > 200*200) teleports++;
                // A continuous Godot history; only the trace's initial state is
                // seeded from UE. No per-frame replacement with expected output.
                state = AlsCameraFollow.Step(state,input,rig.Follow,curves,r => new(r.Start,r.End));
                foreach (var pair in new[] { (state.PivotTarget,"PivotTargetLocation"),(state.PivotLag,"PivotLagLocation"),
                    (state.Pivot,"PivotLocation"),(state.Location,"CameraLocation") })
                {
                    var error = Math.Sqrt((pair.Item1 - V(expected,pair.Item2)).LengthSquared);
                    maxPosition = Math.Max(maxPosition,error);
                    Assert.True(error <= .001, $"hz={trace.GetProperty("hz")} frame={count} {pair.Item2} error={error:R} cm");
                }
                var rotation = R(expected,"rotation");
                var re = Math.Max(Math.Abs(AlsCharacterRotationMath.Normalize(state.Rotation.Yaw-rotation.Yaw)),
                    Math.Max(Math.Abs(AlsCharacterRotationMath.Normalize(state.Rotation.Pitch-rotation.Pitch)),Math.Abs(AlsCharacterRotationMath.Normalize(state.Rotation.Roll-rotation.Roll))));
                maxRotation = Math.Max(maxRotation,re); Assert.InRange(re,0,1e-7);
                if (based)
                {
                    var localError = Math.Sqrt((state.BaseLocalPivotLag - V(expected,"baseLocalPivotLag")).LengthSquared);
                    maxPosition = Math.Max(maxPosition,localError); Assert.InRange(localError,0,.001);
                    var nativeQ = Q(expected,"baseLocalRotation"); var actualQ = state.BaseLocalRotation;
                    var sign = nativeQ.X*actualQ.X+nativeQ.Y*actualQ.Y+nativeQ.Z*actualQ.Z+nativeQ.W*actualQ.W < 0 ? -1 : 1;
                    Assert.InRange(Math.Abs(nativeQ.X-sign*actualQ.X),0,1e-8);
                    Assert.InRange(Math.Abs(nativeQ.Y-sign*actualQ.Y),0,1e-8);
                    Assert.InRange(Math.Abs(nativeQ.Z-sign*actualQ.Z),0,1e-8);
                    Assert.InRange(Math.Abs(nativeQ.W-sign*actualQ.W),0,1e-8);
                }
                Assert.InRange(Math.Abs(state.Fov-F(expected,"fov")),0,1e-5);
                Assert.Equal(1,F(expected,"ratio")); Assert.Equal(1,state.TraceRatio); count++;
            }
        }
        Assert.Equal(840,count); Assert.True(firstPerson > 100 && thirdPerson > 100, "Native camera graph did not evaluate both view modes.");
        Assert.True(partial > 0 && overrides > 100); Assert.Equal(based ? 6 : 3,teleports);
        Assert.Equal(based ? 9 : 0,baseChanges); Assert.Equal(based ? 693 : 0,basedFrames);
        if (based) Assert.True(translatingFrames > 600 && tiltedFrames > 500, "Native bases did not actually translate and tilt.");
        output.WriteLine($"translating_frames={translatingFrames} tilted_frames={tiltedFrames}");
        output.WriteLine($"based={based} frames={count} fullFP={firstPerson} partial={partial} third={thirdPerson} teleports={teleports} overrides={overrides} base_changes={baseChanges} based_frames={basedFrames} max_position_cm={maxPosition:R} max_rotation_deg={maxRotation:R}");
    }
    private static float F(JsonElement e,string name) => e.GetProperty(name).GetSingle();
    private static AlsDoubleVector V(JsonElement e,string name) { var a=e.GetProperty(name); return new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble()); }
    private static AlsAimingRotation R(JsonElement e,string name) { var a=e.GetProperty(name); return new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble()); }
    private static AlsQuaternion Q(JsonElement e,string name) { var a=e.GetProperty(name); return new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble(),a[3].GetDouble()); }
}
