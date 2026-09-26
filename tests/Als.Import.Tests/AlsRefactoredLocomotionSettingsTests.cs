using System.Text.Json.Nodes;
using GodotAls.Import.Compilation;

namespace GodotAls.Import.Tests;

public sealed class AlsRefactoredLocomotionSettingsTests
{
    [Fact]
    public void ActualCharacterAndAnimationDefaultsRemainSeparate()
    {
        var c = MantlingHostFixture.Read("refactored_character_settings");
        var a = MantlingHostFixture.Read("refactored_movement_settings");
        var settings = new AlsRefactoredLocomotionSettings(c, a);
        Assert.Equal(50, settings.MovingThreshold); Assert.Equal(150, settings.MovingSmoothThreshold);
        Assert.Equal(300, settings.TeleportDistance);
        var altered = JsonNode.Parse(c)!; altered["movingSpeedThreshold"] = 80;
        Assert.Equal(80, new AlsRefactoredLocomotionSettings(altered.ToJsonString(), a).MovingThreshold);
        altered["movingSpeedThreshold"] = -1;
        Assert.Throws<ArgumentException>(() => new AlsRefactoredLocomotionSettings(altered.ToJsonString(), a));
        Assert.Throws<ArgumentException>(() => new AlsRefactoredLocomotionSettings(c, a + " "));
    }
}
