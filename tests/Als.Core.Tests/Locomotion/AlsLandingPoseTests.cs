using System.Numerics;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests.Locomotion;

public sealed class AlsLandingPoseTests
{
    [Theory]
    [InlineData(3, -5, 0)] [InlineData(3, 7.5f, .5f)] [InlineData(3, -10, 1)]
    [InlineData(6, -7.5f, 0)] [InlineData(6, -15, .75f)] [InlineData(6, -17.5f, 1)]
    public void LandingMapsAbsoluteSpeedThenClampsAndPreservesTheOtherNode(int state, float speed, float alpha)
    {
        var previous = new AlsLandingBlendInputs(.2f, .3f); var next = previous.Update(state, speed);
        Assert.Equal(alpha, next.PoseAlpha(state));
        Assert.Equal(state == 3 ? previous.Moving : previous.Land, state == 3 ? next.Moving : next.Land);
        Assert.Equal(state == 6, next.MovingAdditiveActive);
    }

    [Fact]
    public void MeshDifferenceAndApplyReconstructTargetWithNoncommutingParentRotations()
    {
        AlsLocalPose[] basis = [Pose(Vector3.UnitX, .6f), Pose(Vector3.UnitY, -.3f), Pose(Vector3.UnitZ, .4f)];
        AlsLocalPose[] target = [Pose(Vector3.UnitY, -.5f), Pose(Vector3.UnitZ, .9f), Pose(Vector3.UnitX, .2f)];
        int[] parents = [-1, 0, 1]; var scratch = new Quaternion[6]; var difference = new AlsLocalPose[3]; var result = new AlsLocalPose[3];
        AlsMeshSpaceAdditivePose.Difference(target, basis, parents, scratch, difference);
        AlsMeshSpaceAdditivePose.Apply(basis, difference, parents, scratch, result);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(MathF.Abs(Quaternion.Dot(target[i].Rotation, result[i].Rotation)) > .99999f);
            Assert.True(Vector3.Distance(target[i].Position, result[i].Position) < 1e-6f);
            Assert.True(Vector3.Distance(target[i].Scale, result[i].Scale) < 1e-6f);
        }
        var local = AlsLocalAdditivePose.Difference(target[1], basis[1]);
        Assert.True(MathF.Abs(Quaternion.Dot(local.Rotation, difference[1].Rotation)) < .99f);
        var alias = target.ToArray(); AlsMeshSpaceAdditivePose.Difference(alias, basis, parents, scratch, alias);
        Assert.Equal(difference, alias);
        alias = basis.ToArray(); AlsMeshSpaceAdditivePose.Apply(alias, difference, parents, scratch, alias);
        Assert.Equal(result, alias);
        AlsMeshSpaceAdditivePose.Apply(basis, difference, parents, scratch, result, 0);
        Assert.Equal(basis, result);
    }

    [Fact]
    public void MeshAdditiveChildCompensatesChangedParentAndKeepsTranslationLocal()
    {
        AlsLocalPose[] basis = [new(Vector3.Zero, Quaternion.Identity, Vector3.One), new(Vector3.UnitX, Quaternion.Identity, Vector3.One)];
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        AlsLocalPose[] delta = [new(Vector3.Zero, rotation, Vector3.Zero), new(Vector3.UnitY, Quaternion.Identity, Vector3.Zero)];
        var output = new AlsLocalPose[2]; var scratch = new Quaternion[4];
        AlsMeshSpaceAdditivePose.Apply(basis, delta, [-1, 0], scratch, output);
        Assert.True(MathF.Abs(Quaternion.Dot(output[0].Rotation * output[1].Rotation, Quaternion.Identity)) > .99999f);
        Assert.Equal(new Vector3(1, 1, 0), output[1].Position);
        AlsMeshSpaceAdditivePose.Apply(basis, delta, [-1, 0], scratch, output, 0);
        Assert.Equal(basis, output);
        Assert.Throws<ArgumentException>(() => AlsMeshSpaceAdditivePose.Apply(basis, delta, [-1, 1], scratch, output));
        Assert.Throws<ArgumentException>(() => default(AlsLandingBlendInputs).Update(3, float.NaN));
    }
    private static AlsLocalPose Pose(Vector3 axis, float angle) => new(axis * angle, Quaternion.CreateFromAxisAngle(axis, angle), Vector3.One * (1 + angle * .2f));
}
