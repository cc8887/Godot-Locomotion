using System.Diagnostics;
using Godot;

namespace GodotAls.Locomotion;

public partial class AlsLocomotionHud : VBoxContainer
{
    private const long PerformanceRefreshInterval = 15;
    private Label _stateLabel = null!;
    private Label _performanceLabel = null!;
    private Label? _overlayLabel;
    private GodotAls.Core.Locomotion.AlsOverlayKind? _overlay;
    public void RefreshOverlay(GodotAls.Core.Locomotion.AlsOverlayKind overlay)
    {
        if (_overlay == overlay) return;
        _overlayLabel ??= CreateLabel("Overlay"); _overlay = overlay;
        _overlayLabel.Text = $"Overlay: {overlay}";
    }
    private long _lastStateFrame = long.MinValue;

    public string StateText => _stateLabel.Text;

    public string PerformanceText => _performanceLabel.Text;

    public long RefreshCallCount { get; private set; }

    public long StateFormatCount { get; private set; }

    public long PerformanceFormatCount { get; private set; }

    public long LastStateFrame => _lastStateFrame;

    public void EnableActionPreview()
    {
        var label = CreateLabel("ActionPreviewHelp");
        var action = GodotAls.Animation.AlsAnimationRuntimeOptions.Has("--rolling-gameplay") ? "地面翻滚" :
            GodotAls.Animation.AlsAnimationRuntimeOptions.Has("--montage-root-motion") ? "翻滚位移测试" : "原地翻滚动画预览";
        label.Text = $"R：{action}  |  X：取消动作  |  G：进入 / 退出 Ragdoll  |  Q / E：切换 Overlay 道具";
    }

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

        RefreshCallCount++;
        var result = diagnostics.Result;
        if (diagnostics.CommittedFrameId != _lastStateFrame)
        {
            var speed = diagnostics.ActualVelocity.Length();
            var turn = result.TurnActive == 0
                ? "off"
                : FormattableString.Invariant(
                    $"{(result.TurnDirection < 0 ? 'L' : 'R')}{Math.Abs(result.TurnNominalDegrees)}@{result.TurnPhase:0.00}");
            var rotate = result.RotateActive == 0
                ? "off"
                : FormattableString.Invariant(
                    $"{(result.RotateDirection < 0 ? 'L' : 'R')}@{result.RotatePhase:0.00}");
            _stateLabel.Text = string.Join('\n',
                FormattableString.Invariant(
                    $"Frame {diagnostics.CommittedFrameId} | {result.ResolvedLocomotionState} | Mode {result.ActualRotationMode} | Speed {speed:0.00}"),
                FormattableString.Invariant(
                    $"Aim ({result.AimRelativeYaw:+0.00;-0.00;+0.00},{result.AimRelativePitch:+0.00;-0.00;+0.00}) | Turn {turn} | Rotate {rotate} | Phase {result.AnimationPhase:0.00}"),
                FormattableString.Invariant(
                    $"leftLock {result.LeftFootPose.LockAmount:0.00} | rightLock {result.RightFootPose.LockAmount:0.00} | Pelvis {result.PelvisOffset.Y:+0.000;-0.000;+0.000}"),
                FormattableString.Invariant(
                    $"Blend ({result.BlendCoordinates.X:0.00}, {result.BlendCoordinates.Y:0.00}) | Stride {result.Stride:0.00} | Rate {result.PlayRate:0.00} | Lean ({result.Lean.X:0.00}, {result.Lean.Y:0.00})"));
            _lastStateFrame = diagnostics.CommittedFrameId;
            StateFormatCount++;
        }

        if (RefreshCallCount == 1 || RefreshCallCount % PerformanceRefreshInterval == 0)
        {
            var workerMilliseconds = result.WorkerElapsedTicks <= 0
                ? 0d
                : result.WorkerElapsedTicks * 1_000d / Stopwatch.Frequency;
            _performanceLabel.Text = FormattableString.Invariant(
                $"FPS {Math.Round(framesPerSecond):0} | Errors {errors} | workerMs {workerMilliseconds:0.000}");
            PerformanceFormatCount++;
        }
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
