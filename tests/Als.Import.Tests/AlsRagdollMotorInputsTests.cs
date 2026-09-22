using GodotAls.Core.Locomotion;
using GodotAls.Core.Physics;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRagdollMotorInputsTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(500, 12500)] [InlineData(1000, 25000)] [InlineData(2000, 25000)]
    public void PelvisSpeedUsesNativeCentimetersAndSaturates(double speed, float expected)
    {
        Assert.Equal(expected, AlsRagdollMotorInputs.SpringFromPelvisVelocity(new(-speed, 0, 0)));
    }

    [Fact]
    public void MaskedInputsAreAtomicReusableAndDoNotAllocate()
    {
        var definition = AlsPhysicsAssetCompiler.Compile(Read("v4_physics_asset_inputs.json"),
            AlsPhysicsAssetCompiler.MeshRoot + "Mannequin.Mannequin");
        var settings = AlsPhysicsJointCompiler.Compile(Read("v4_physics_joint_reference.json"), definition);
        var active = Array.FindIndex(settings, s => s.AngularDrive.TwistPosition);
        settings[active] = settings[active] with { AngularDrive = settings[active].AngularDrive with {
            TwistPosition = false, SwingPosition = true, TwistVelocity = true, SwingVelocity = false } };
        var runtime = new AlsRagdollMotorInputs(definition, settings, 1.5f, 1.5f);
        var locals = definition.Bones.Select(b => b.Local).ToArray();
        var targets = settings.Select(s => s.AngularDrive.Target).ToArray();
        var output = new AlsIslandAngularDrive[runtime.OutputCount];
        runtime.EvaluateParameters(locals, targets, 12500, 2, output);
        var entry = output.Single(s => s.Joint == active);
        Assert.Equal(new AlsDoubleVector(0, 18750, 18750), entry.Stiffness);
        Assert.Equal(new AlsDoubleVector(3, 0, 0), entry.Damping);
        Assert.True(output.Zip(output.Skip(1)).All(p => p.First.Joint < p.Second.Joint));
        var saved = output.ToArray();
        var last = output[^1].Joint; var prior = targets[last]; targets[last] = default;
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(locals, targets, new(1000,0,0), output));
        Assert.Equal(saved, output);
        targets[last] = prior;
        for (var i=0; i<256; i++) runtime.Evaluate(locals, targets, new(500,0,0), output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i=0; i<2048; i++) runtime.Evaluate(locals, targets, new(500,0,0), output);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread()-before);
        Assert.Equal(AlsDoubleVector.Zero, output.Single(s=>s.Joint==active).Damping);
        Assert.Throws<ArgumentException>(() => runtime.Evaluate(locals, targets, new(double.NaN,0,0), output));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.EvaluateParameters(locals,targets,float.PositiveInfinity,0,output));
        const float fractionalSpring = 12345.678f;
        runtime.EvaluateParameters(locals,targets,fractionalSpring,0,output);
        Assert.Equal((double)fractionalSpring * 1.5f, output.Single(s=>s.Joint==active).Stiffness.Y);
        Assert.NotEqual((double)(fractionalSpring * 1.5f), output.Single(s=>s.Joint==active).Stiffness.Y);
    }

    private static string Read(string file) => File.ReadAllText(AlsFootRigCompilerTests.PathInRepository("assets/config/"+file));
}
