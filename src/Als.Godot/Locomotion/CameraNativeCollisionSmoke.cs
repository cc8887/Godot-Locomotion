using System.Text.Json;
using Godot;
using GodotAls.Core.Camera;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Runtime;

namespace GodotAls.Locomotion;

// Replays observed native curve/socket inputs through Core + real Godot queries.
// Wall state is installed one physics frame before querying it.
public partial class CameraNativeCollisionSmoke : Node3D
{
    private JsonDocument _document = null!;
    private JsonElement _frames;
    private AlsCameraRigDefinition _rig = null!;
    private AlsCameraFollowState _state;
    private StaticBody3D _wall = null!;
    private BoxShape3D _shape = null!;
    private AlsCameraCollisionProbe _probe = null!;
    private int _warmup, _frame, _penetrations, _adjustments, _hz;
    private double _maxPosition, _maxRatio;
    private float _minimumNativeRatio = 1;
    public override void _Ready()
    {
        _hz = int.Parse(OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--hz="))?[5..] ?? "60");
        if (_hz is not (30 or 60 or 120)) throw new ArgumentOutOfRangeException(nameof(_hz));
        Engine.PhysicsTicksPerSecond = _hz;
        _rig = AlsCameraRigDefinition.Compile(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_inputs.json"));
        _document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/config/refactored_camera_collision_reference.json"));
        Require(_document.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
            _document.RootElement.GetProperty("cameraClass").GetString() == "/ALS/ALSCamera/B_Als_CameraComponent.B_Als_CameraComponent_C", "Unexpected reference source.");
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Godot.FileAccess.GetFileAsBytes("res://assets/config/refactored_camera_inputs.json"))).ToLowerInvariant();
        Require(digest == _document.RootElement.GetProperty("settingsDigest").GetString(),"Reference settings differ from runtime settings.");
        var trace = _document.RootElement.GetProperty("traces").EnumerateArray().Single(t => t.GetProperty("hz").GetInt32() == _hz);
        _frames = trace.GetProperty("frames"); var initial = trace.GetProperty("initial");
        _state = new(true,0,"",V(initial,"PivotTargetLocation"),V(initial,"PivotLagLocation"),V(initial,"PivotLocation"),
            V(initial,"CameraLocation"),R(initial,"rotation"),F(initial,"fov"),F(initial,"ratio"),default,AlsQuaternion.Identity);
        _wall = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        _shape = new BoxShape3D(); _wall.AddChild(new CollisionShape3D { Shape = _shape }); AddChild(_wall);
        _probe = new(this,1,[]); PrepareWall();
    }
    public override void _PhysicsProcess(double delta)
    {
        if (++_warmup < 3) return;
        try
        {
            var row = _frames[_frame]; var i = row.GetProperty("input"); var c = row.GetProperty("curves"); var expected = row.GetProperty("output");
            Require(Math.Abs(delta-F(i,"delta")) < 1e-7,"Physics frequency differs from reference.");
            var q = i.GetProperty("meshRotation");
            var input = new AlsCameraFollowInput(F(i,"delta"),true,R(i,"view"),V(i,"firstPivot"),V(i,"secondPivot"),V(i,"firstPerson"),V(i,"shoulder"),
                false,default,new(q[0].GetDouble(),q[1].GetDouble(),q[2].GetDouble(),q[3].GetDouble()),F(i,"meshScale"),
                0,"",false,default,AlsQuaternion.Identity,i.GetProperty("overrideFov").GetBoolean(),F(i,"fovOverride"),F(c,"FovOffset"));
            AlsDoubleVector CurveVector(string prefix) => new(F(c,prefix+"X"),F(c,prefix+"Y"),F(c,prefix+"Z"));
            var curves = new AlsCameraFollowCurves(CurveVector("PivotOffset"),CurveVector("CameraOffset"),F(c,"LocationLagX"),F(c,"LocationLagY"),
                F(c,"LocationLagZ"),F(c,"RotationLag"),F(c,"TraceOverride"),F(c,"FirstPersonOverride"));
            _state = AlsCameraFollow.Step(_state,input,_rig.Follow,curves,r => _probe.Query(r,V(i,"ownerLocation"),input.MeshScale));
            if (_probe.StartedPenetrating) _penetrations++;
            if (_probe.AdjustedStart) _adjustments++;
            var error = Math.Sqrt((_state.Location-V(expected,"CameraLocation")).LengthSquared);
            var ratioError = Math.Abs(_state.TraceRatio-F(expected,"ratio"));
            _maxPosition = Math.Max(_maxPosition,error); _maxRatio = Math.Max(_maxRatio,ratioError);
            _minimumNativeRatio = Math.Min(_minimumNativeRatio,F(expected,"ratio"));
            // Godot safe-fraction queries previously use a 3 mm clearance budget;
            // spatial math alone retains the tighter native .001 cm test.
            Require(error <= .3,$"frame={_frame} position_error_cm={error:R} ratio_error={ratioError:R} penetrating={_probe.StartedPenetrating} adjusted={_probe.AdjustedStart}");
            Require(Math.Abs(_state.Fov-F(expected,"fov")) < 1e-5,"FOV mismatch.");
            if (++_frame == _frames.GetArrayLength())
            {
                Require(_frame == _hz*4 && _penetrations == _hz && _adjustments == _hz/2 && _minimumNativeRatio < .7 && _state.TraceRatio > .9,
                    $"Incomplete collision branches: penetration={_penetrations} adjusted={_adjustments} ratio={_state.TraceRatio}");
                GD.Print($"ALS_CAMERA_NATIVE_COLLISION_OK hz={_hz} frames={_frame} penetration={_penetrations} adjusted={_adjustments} max_cm={_maxPosition:R} max_ratio={_maxRatio:R}");
                SetPhysicsProcess(false); GetTree().Quit(); return;
            }
            PrepareWall();
        }
        catch(Exception e) { GD.PushError(e.ToString()); SetPhysicsProcess(false); GetTree().Quit(1); }
    }
    private void PrepareWall()
    {
        var i = _frames[_frame].GetProperty("input"); var extent = V(i,"wallExtent");
        _shape.Size = new((float)extent.Y*.02f,(float)extent.Z*.02f,(float)extent.X*.02f);
        var p = V(i,"wallLocation"); _wall.GlobalPosition = new((float)(p.Y*.01),(float)(p.Z*.01),(float)(-p.X*.01));
    }
    public override void _ExitTree() { _probe?.Dispose(); _document?.Dispose(); }
    private static float F(JsonElement e,string name) => e.GetProperty(name).GetSingle();
    private static AlsDoubleVector V(JsonElement e,string name) { var a=e.GetProperty(name); return new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble()); }
    private static AlsAimingRotation R(JsonElement e,string name) { var a=e.GetProperty(name); return new(a[0].GetDouble(),a[1].GetDouble(),a[2].GetDouble()); }
    private static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
}
