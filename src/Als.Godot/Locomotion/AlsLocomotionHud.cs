using System.Diagnostics;
using Godot;

namespace GodotAls.Locomotion;

public partial class AlsLocomotionHud : VBoxContainer
{
    private Label _stateLabel = null!;
    private Label _performanceLabel = null!;

    public string StateText => _stateLabel.Text;

    public string PerformanceText => _performanceLabel.Text;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 3);
        _stateLabel = CreateLabel("State");
        _performanceLabel = CreateLabel("Performance");
    }

    public void Refresh(
        in AlsP3FrameDiagnostics diagnostics,
        double framesPerSecond,
        long errors)
    {
        if (!double.IsFinite(framesPerSecond) || framesPerSecond < 0d || errors < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
        }

        var result = diagnostics.Result;
        _stateLabel.Text =
            $"Frame {diagnostics.CommittedFrameId} | " +
            $"{result.ResolvedLocomotionState}/{result.AnimationState}\n" +
            $"{result.ActualGait} | {result.ActualStance} | {result.ActualRotationMode}";
        var workerMicroseconds = result.WorkerElapsedTicks <= 0
            ? 0d
            : result.WorkerElapsedTicks * 1_000_000d / Stopwatch.Frequency;
        _performanceLabel.Text =
            $"FPS {Math.Round(framesPerSecond):0} | Worker {workerMicroseconds:0.0} us | Errors {errors}";
    }

    private Label CreateLabel(string name)
    {
        var label = new Label
        {
            Name = name,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        label.AddThemeFontSizeOverride("font_size", 16);
        AddChild(label);
        return label;
    }
}
