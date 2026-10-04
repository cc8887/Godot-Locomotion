using System.Text.Json;
using GodotAls.Core.Locomotion;

namespace GodotAls.Core.Tests;

public sealed class AlsCharacterVelocityTests
{
    [Fact]
    public void OriginalShooterFallingAccelerationVelocityAndDisplacement()
    {
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Fixtures","Physics","lyra_character_falling_native.json")));
        var p=doc.RootElement.GetProperty("profile");
        float F(string name)=>p.GetProperty(name).GetSingle();
        var v=new AlsCharacterVelocitySettings(F("MaxWalkSpeed"),F("MinAnalogWalkSpeed"),F("FallingLateralFriction"),
            p.GetProperty("bUseSeparateBrakingFriction").GetBoolean(),F("BrakingFriction"),F("BrakingFrictionFactor"),
            F("BrakingDecelerationFalling"),F("BrakingSubStepTime"));
        var settings=new AlsCharacterFallingSettings(v,F("MaxAcceleration"),F("AirControl"),F("AirControlBoostMultiplier"),
            F("AirControlBoostVelocityThreshold"),F("gravityZ"),F("terminalVelocity"));
        static AlsDoubleVector V(JsonElement e)=>new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
        int count=0;
        foreach(var row in doc.RootElement.GetProperty("fallingKernel").EnumerateArray())
        {
            var step=AlsCharacterFalling.Advance(V(row.GetProperty("velocity")),V(row.GetProperty("acceleration")),
                row.GetProperty("analog").GetSingle(),row.GetProperty("delta").GetSingle(),settings);
            void Equal(string name,AlsDoubleVector actual)
            {
                var expected=V(row.GetProperty(name));double error=System.Math.Sqrt((actual-expected).LengthSquared);
                Assert.True(error<=1e-10,$"Native falling row {count}/{name}: error={error:R} actual={actual} expected={expected}");
            }
            Equal("fallAcceleration",step.Acceleration);Equal("output",step.Velocity);Equal("displacement",step.Displacement);count++;
        }
        Assert.Equal(175,count);
    }
    [Fact]
    public void OriginalShooterCharacterMovementVelocityKernel()
    {
        using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "Fixtures","Physics","lyra_character_velocity_native.json")));
        var p=doc.RootElement.GetProperty("profile");
        Assert.Equal("/Script/LyraGame.LyraCharacterMovementComponent",p.GetProperty("movementClass").GetString());
        static AlsDoubleVector V(JsonElement e)=>new(e[0].GetDouble(),e[1].GetDouble(),e[2].GetDouble());
        int count=0;
        foreach(var row in doc.RootElement.GetProperty("kernel").EnumerateArray())
        {
            var s=new AlsCharacterVelocitySettings(p.GetProperty(row.GetProperty("crouching").GetBoolean()?"MaxWalkSpeedCrouched":"MaxWalkSpeed").GetSingle(),
                p.GetProperty("MinAnalogWalkSpeed").GetSingle(),p.GetProperty("GroundFriction").GetSingle(),p.GetProperty("bUseSeparateBrakingFriction").GetBoolean(),
                p.GetProperty("BrakingFriction").GetSingle(),p.GetProperty("BrakingFrictionFactor").GetSingle(),
                p.GetProperty("BrakingDecelerationWalking").GetSingle(),p.GetProperty("BrakingSubStepTime").GetSingle());
            var actual=AlsCharacterVelocity.Advance(V(row.GetProperty("velocity")),V(row.GetProperty("acceleration")),
                row.GetProperty("analog").GetSingle(),row.GetProperty("delta").GetSingle(),s);
            var expected=V(row.GetProperty("output"));
            double error=(actual-expected).LengthSquared;
            Assert.True(error<=1e-20,$"Native row {count} error={System.Math.Sqrt(error):R} actual={actual} expected={expected}");
            count++;
        }
        Assert.Equal(250,count);
    }
}
