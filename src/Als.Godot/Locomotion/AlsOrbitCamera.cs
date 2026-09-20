using Godot;

namespace GodotAls.Locomotion;

public partial class AlsOrbitCamera : Node3D
{
    private Node3D? _target;
    private SpringArm3D? _springArm;
    private float _yaw;
    private float _pitch = -0.2f;
    private bool _captureRequested = true;

    [Export(PropertyHint.Range, "0.0005,0.02,0.0005")]
    public float MouseSensitivity { get; set; } = 0.0025f;

    public float MinimumPitch { get; } = Mathf.DegToRad(-70f);

    public float MaximumPitch { get; } = Mathf.DegToRad(25f);

    public float Yaw => _yaw;

    public float Pitch => _pitch;

    public Vector3 FollowOffset { get; } = new(0f, 0.53f, 0f);

    public Node3D? Target =>
        _target is not null && GodotObject.IsInstanceValid(_target)
            ? _target
            : null;

    public bool IsMouseCaptured { get; private set; }

    public override void _Ready()
    {
        _springArm = GetNodeOrNull<SpringArm3D>("SpringArm3D")
            ?? throw new InvalidOperationException(
                "ALS orbit camera requires a SpringArm3D child named SpringArm3D.");
        ApplyOrbit();
        SetMouseCaptured(DisplayServer.GetName() != "headless");
    }

    public void Configure(Node3D target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!GodotObject.IsInstanceValid(target) || !target.IsInsideTree())
        {
            throw new InvalidOperationException("Orbit camera target must be a live scene node.");
        }
        _target = target;
    }

    public override void _Process(double delta)
    {
        if (_target is not null && GodotObject.IsInstanceValid(_target))
        {
            GlobalPosition = _target.GlobalPosition + FollowOffset;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("mouse_capture_toggle"))
        {
            ToggleMouseCapture();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (IsMouseCaptured && @event is InputEventMouseMotion mouseMotion)
        {
            ApplyMouseMotion(mouseMotion.Relative);
            GetViewport().SetInputAsHandled();
        }
    }

    public void ApplyMouseMotion(in Vector2 relative)
    {
        if (!float.IsFinite(relative.X) || !float.IsFinite(relative.Y) ||
            !float.IsFinite(MouseSensitivity) || MouseSensitivity <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(relative),
                "Mouse motion and sensitivity must be finite and sensitivity must be positive.");
        }
        _yaw = Mathf.Wrap(_yaw - (relative.X * MouseSensitivity), -Mathf.Pi, Mathf.Pi);
        _pitch = Mathf.Clamp(
            _pitch - (relative.Y * MouseSensitivity),
            MinimumPitch,
            MaximumPitch);
        ApplyOrbit();
    }

    public void ToggleMouseCapture() => SetMouseCaptured(!IsMouseCaptured);

    public void SetMouseCaptured(bool captured)
    {
        _captureRequested = captured;
        IsMouseCaptured = captured;
        Input.MouseMode = captured
            ? Input.MouseModeEnum.Captured
            : Input.MouseModeEnum.Visible;
    }

    public override void _Notification(int what)
    {
        switch (what)
        {
            case (int)NotificationWMWindowFocusOut:
                IsMouseCaptured = false;
                break;
            case (int)NotificationWMWindowFocusIn:
                IsMouseCaptured = _captureRequested;
                Input.MouseMode = _captureRequested
                    ? Input.MouseModeEnum.Captured
                    : Input.MouseModeEnum.Visible;
                break;
        }
    }

    private void ApplyOrbit()
    {
        Rotation = new Vector3(0f, _yaw, 0f);
        if (_springArm is not null)
        {
            _springArm.Rotation = new Vector3(_pitch, 0f, 0f);
        }
    }
}
