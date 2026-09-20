using System.Text.Json;
using GodotAls.Core.Locomotion;
using GodotAls.Import.Compilation;
using Xunit.Abstractions;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLegRigTests(ITestOutputHelper output)
{
    [Fact]
    public void CompiledLegPipelineMatchesContinuousNativeNodesFinalPoseAndHistory()
    {
        var definition = AlsFootRigCompiler.Compile(AlsFootRigCompilerTests.Source());
        using var document = JsonDocument.Parse(File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("tests/Als.Core.Tests/Fixtures/FootIk/native_leg_rig.json")));
        int[] parents = [-1, 0, 1, 2, 3, 1];
        double positionError = 0, rotationError = 0, scaleError = 0, stateError = 0, velocityError = 0;
        var frames = 0; var skipped = 0; var zeroWeight = 0;
        foreach (var sample in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var reference = sample.GetProperty("initial").EnumerateArray().Select(P).ToArray();
            var left = sample.GetProperty("left").GetBoolean();
            var bones = new AlsRigLegBones(0, 1, 2, 3, left ? definition.LeftPrimary : definition.RightPrimary,
                left ? definition.LeftSecondary : definition.RightSecondary);
            var state = AlsRefactoredLegRigState.Initial;
            var scratch = new AlsPrecisePose[6]; var changed = new bool[6];
            // Also verify the actual ChainLength initialization result.
            var bindingReference = reference.Concat(new[] { reference[1], reference[2], reference[3], AlsPrecisePose.Identity }).ToArray();
            var binding = definition.Bind(new[] { "pelvis", "thigh_l", "calf_l", "foot_l", "ball", "side", "thigh_r", "calf_r", "foot_r", "ik_foot_root" },
                new[] { -1, 0, 1, 2, 3, 1, 0, 6, 7, -1 }, bindingReference, true);
            Assert.Equal(sample.GetProperty("legLength").GetSingle(), binding.LegLength);
            Assert.Equal(sample.GetProperty("footHeight").GetSingle(), binding.FootHeight);
            foreach (var row in sample.GetProperty("frames").EnumerateArray())
            {
                if (row.GetProperty("reset").GetBoolean()) state = AlsRefactoredLegRigState.Initial;
                var before = row.GetProperty("before").EnumerateArray().Select(P).ToArray();
                var candidate = before.ToArray(); var retry = before.ToArray();
                var valid = row.GetProperty("valid").GetBoolean();
                var input = new AlsRefactoredLegRigInput(F(row, "dt"), V(row.GetProperty("target")), Q(row.GetProperty("targetRotation")),
                    F(row, "offset"), V(row.GetProperty("normal")), -8, binding.LegLength, row.GetProperty("moving").GetDouble(), F(row, "weight"));
                if (valid)
                {
                    var next = AlsRefactoredLegRig.Evaluate(state, definition.Leg, bones, input, reference, candidate, parents, scratch, changed);
                    var repeat = AlsRefactoredLegRig.Evaluate(state, definition.Leg, bones, input, reference, retry, parents, scratch, changed);
                    Assert.Equal(next, repeat); Assert.Equal(candidate, retry);
                    state = next;
                    if (input.Weight == 0) { zeroWeight++; Assert.Equal(before, candidate); Assert.True(state.Location.Initialized); Assert.True(state.Rotation.Initialized); }
                }
                else { skipped++; Assert.Equal(before, candidate); }
                var after = row.GetProperty("after").EnumerateArray().Select(P).ToArray();
                for (var i = 0; i < after.Length; i++)
                {
                    positionError = Math.Max(positionError, Distance(after[i].Position, candidate[i].Position));
                    rotationError = Math.Max(rotationError, Math.Abs(1 - Math.Abs(AlsQuaternion.Dot(after[i].Rotation, candidate[i].Rotation))));
                    scaleError = Math.Max(scaleError, Distance(after[i].Scale, candidate[i].Scale));
                }
                stateError = Math.Max(stateError, Math.Abs(state.Location.OffsetZ - F(row, "springOffset")));
                stateError = Math.Max(stateError, Distance(state.SmoothedPole.Current, V(row.GetProperty("smooth"))));
                stateError = Math.Max(stateError, Distance(state.Rotation.OffsetNormal, V(row.GetProperty("rotationNormal"))));
                velocityError = Math.Max(velocityError, Math.Abs(state.Location.Spring.Velocity - F(row, "springVelocity")));
                Assert.Equal(row.GetProperty("springValid").GetBoolean(), state.Location.Spring.Valid);
                Assert.Equal(row.GetProperty("poleSuccess").GetBoolean(), state.Pole.Success);
                frames++;
            }
        }
        output.WriteLine($"frames={frames} skipped={skipped} zeroWeight={zeroWeight} positionCm={positionError:R} rotationDot={rotationError:R} scale={scaleError:R} state={stateError:R} velocity={velocityError:R}");
        Assert.Equal(1440, frames); Assert.Equal(60, skipped); Assert.True(zeroWeight >= 200);
        Assert.InRange(positionError, 0, .0002); Assert.InRange(rotationError, 0, 1e-8); Assert.InRange(scaleError, 0, 1e-10);
        Assert.InRange(stateError, 0, .00005); Assert.InRange(velocityError, 0, .003);
    }

    private static float F(JsonElement e, string n) => e.GetProperty(n).GetSingle();
    private static AlsDoubleVector V(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());
    private static AlsQuaternion Q(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble(), e[3].GetDouble());
    private static AlsPrecisePose P(JsonElement e) => new(V(e.GetProperty("p")), Q(e.GetProperty("q")), V(e.GetProperty("s")));
    private static double Distance(AlsDoubleVector a, AlsDoubleVector b) => Math.Sqrt((a - b).LengthSquared);
}
