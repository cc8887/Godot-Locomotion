using Godot;

namespace GodotAls.Animation.Lyra;

public partial class LyraRootYawSmoke : Node
{
    public override void _Ready()
    {
        try
        {
            var defaults = LyraRootYawDefaults.Load();
            if (!defaults.Enabled || defaults.StandingMinimum != -120 ||
                defaults.StandingMaximum != 100 || defaults.CrouchedMinimum != -90 ||
                defaults.CrouchedMaximum != 80)
                throw new InvalidOperationException("Lyra RootYawOffset CDO changed.");
            var skeleton = new Skeleton3D();
            skeleton.AddBone("root");
            AddChild(skeleton);
            var yaw = new LyraRootYawOffset(skeleton, defaults);
            const double step = 1.0 / 60.0;
            yaw.Update(0, 0, false);
            yaw.QueueMode(LyraMotionPhase.Idle);
            yaw.Update(60, step, false);
            if (Math.Abs(yaw.OffsetDegrees + 60) > 1e-5 ||
                Math.Abs(yaw.AimYawDegrees - 60) > 1e-5)
                throw new InvalidOperationException("Idle did not counter actor yaw in AimYaw.");
            yaw.ApplyRootPose();
            var actorRotation = new Quaternion(Vector3.Up, Mathf.DegToRad(-60));
            if ((actorRotation * skeleton.GetBonePoseRotation(0)).AngleTo(Quaternion.Identity) > 1e-5f)
                throw new InvalidOperationException("ALS root rotation did not preserve world facing.");
            yaw.RestoreBase();
            if (skeleton.GetBonePoseRotation(0).AngleTo(Quaternion.Identity) > 1e-5f)
                throw new InvalidOperationException("Lyra root pose was not restored.");

            yaw.QueueMode(LyraMotionPhase.Idle);
            yaw.Update(240, step, false);
            if (yaw.OffsetDegrees != 100)
                throw new InvalidOperationException("Standing root-yaw clamp failed at wrapped actor yaw.");
            yaw.QueueMode(LyraMotionPhase.Idle);
            yaw.Update(250, step, true);
            if (yaw.OffsetDegrees != 80)
                throw new InvalidOperationException("Crouched root-yaw clamp failed.");
            yaw.QueueMode(LyraMotionPhase.Start);
            yaw.Update(260, step, true);
            if (yaw.OffsetDegrees != 80 || yaw.PendingMode != LyraRootYawMode.BlendOut)
                throw new InvalidOperationException("Start did not hold RootYawOffset for this update.");
            yaw.QueueMode(LyraMotionPhase.Cycle);
            yaw.Update(260, step, true);
            if (yaw.OffsetDegrees <= 0 || yaw.OffsetDegrees >= 80)
                throw new InvalidOperationException("Cycle did not spring RootYawOffset toward zero.");
            var firstSpringValue = yaw.OffsetDegrees;
            for (var frame = 0; frame < 120; frame++) yaw.Update(260, step, true);
            if (Math.Abs(yaw.OffsetDegrees) >= 0.1f)
                throw new InvalidOperationException("Root-yaw spring failed to settle.");
            var turnYaw = new LyraRootYawOffset(skeleton, defaults);
            turnYaw.Update(0, 0, false);
            turnYaw.QueueMode(LyraMotionPhase.Idle);
            turnYaw.Update(70, step, false);
            turnYaw.ProcessTurnYawCurve(90, 1, false);
            if (turnYaw.OffsetDegrees != -70)
                throw new InvalidOperationException("The first turn curve sample moved RootYawOffset.");
            turnYaw.ProcessTurnYawCurve(75, 1, false);
            if (turnYaw.OffsetDegrees != -55 || turnYaw.TurnCurveFeedbackCount != 1)
                throw new InvalidOperationException("Turn curve delta was not applied to RootYawOffset.");
            turnYaw.ProcessTurnYawCurve(0, 0, false);
            turnYaw.ProcessTurnYawCurve(30, 1, false);
            if (turnYaw.OffsetDegrees != -55)
                throw new InvalidOperationException("Turn curve did not reset at zero weight.");
            GD.Print($"LYRA_ROOT_YAW_OK clamp=100/80 hold=80 " +
                $"spring={firstSpringValue:0.000}->{yaw.OffsetDegrees:0.000} curve={turnYaw.TurnCurveFeedbackCount}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }
}
