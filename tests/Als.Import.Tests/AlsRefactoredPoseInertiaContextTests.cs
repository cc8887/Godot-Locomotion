using GodotAls.Core.Contracts;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredPoseInertiaContextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void LinkedStagePreservesCoreComponentParentAndTeleportSemantics(float threshold)
    {
        var stage = new AlsRefactoredPoseInertia(1, ["value"]);
        var core = new AlsInertialization(1, 1);
        var expected = new AlsPrecisePose[1]; var values = new AlsInertialCurve[1];
        var counter = new AlsGraphTraversalCounter(0, 0);
        var activeFrames = 0;
        for (var f = 0; f < 90; f++)
        {
            var id = new AlsFrameIdentity(f, 3, 1);
            var context = new AlsPoseUpdateContext(id, 1, 1f / 60).WithUpdateCounter(counter);
            var pose = AlsPrecisePose.Identity with { Position = new(f < 10 ? 0 : 15, 0, 0) };
            var component = AlsPrecisePose.Identity with
            {
                Position = new(f * 2 + (f >= 30 ? 500 : 0), 0, 0),
                Rotation = AlsQuaternion.FromAxisAngle(System.Numerics.Vector3.UnitZ, f * .01f)
            };
            var parent = f < 50 ? 0 : 77;
            core.Update(context.Delta);
            if (f % 10 == 0) core.Request(.5f);
            core.EvaluatePrecisePose([pose], [new(f * .01f)], component, parent, threshold, expected, values);
            if (f == 30) Assert.Equal(threshold == 0, core.IsActive);
            if (core.IsActive) activeFrames++;
            for (var retry = 0; retry < 2; retry++)
            {
                stage.Prepare(context, f == 0);
                if (f % 10 == 0) stage.Request(.5f);
                // A second evaluation must replace the candidate, not advance history.
                for (var evaluation = 0; evaluation < 2; evaluation++)
                {
                    stage.Evaluate([pose], [new(f * .01f)], component, parent, threshold);
                    Assert.Equal(expected, stage.Pose.ToArray());
                    Assert.Equal(values, stage.Curves.ToArray());
                }
                if (retry == 0) stage.Cancel(); else stage.Commit(id);
            }
            counter = counter.Next((ulong)f + 1);
        }
        Assert.True(activeFrames > 30);
    }
}
