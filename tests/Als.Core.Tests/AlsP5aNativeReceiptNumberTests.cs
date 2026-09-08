using System.Reflection;
using System.Text.Json.Nodes;
using GodotAls.Core.Animation;

namespace GodotAls.Core.Tests;

public sealed class AlsP5aNativeReceiptNumberTests
{
    private const string ReceiptPath = "$raw.cases[2].frames[0].nativeActual.transitionStimulusReceipts[0].left";

    [Fact]
    public void EquivalentDecimalAndExponentReceiptCoordinatesAreAccepted()
    {
        var failure = ValidateReceipt("9e-2");

        Assert.Null(failure);
    }

    [Theory]
    [InlineData("0.089999999999999997")]
    [InlineData("1e-9999")]
    public void NumericallyDifferentReceiptCoordinatesAreRejectedAtTheExactAxis(string observedX)
    {
        var failure = ValidateReceipt(observedX);

        var invalid = Assert.IsType<InvalidDataException>(failure);
        Assert.Contains(ReceiptPath + ".observedTargetMeters.x", invalid.Message, StringComparison.Ordinal);
    }

    private static Exception? ValidateReceipt(string observedX)
    {
        var probe = JsonNode.Parse("""
            {
              "targetMeters": { "x": 0.09, "y": 0, "z": 0 },
              "lockMeters": { "x": 0, "y": 0, "z": 0 },
              "relevant": true
            }
            """)!.AsObject();
        var receipt = JsonNode.Parse($$"""
            {
              "observedTargetMeters": { "x": {{observedX}}, "y": 0, "z": 0 },
              "observedLockMeters": { "x": 0, "y": 0, "z": 0 },
              "observedLockAmount": 1
            }
            """)!.AsObject();
        var method = typeof(AlsP5aNativeEvidence).GetMethod(
            "ValidateReceiptFoot",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        try
        {
            method!.Invoke(null, [probe, receipt, ReceiptPath]);
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException;
        }
    }
}
