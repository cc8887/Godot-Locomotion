using System.Text;

namespace GodotAls.Core.Tests;

public sealed class AlsP5aNumericLexemeTests
{
    [Fact]
    public void NumericRewritingPreservesLongerTokensOtherPropertiesAndStrings()
    {
        const string document = """{"x":0.09,"nested":{"x":0.09001},"not_x":0.09,"text":"x: 0.09","values":[0.09]}""";
        var result = P5aOracleApphost.RewriteNumberLexeme(Encoding.UTF8.GetBytes(document), "x", "0.09", "9e-2");
        Assert.Equal("""{"x":9e-2,"nested":{"x":0.09001},"not_x":0.09,"text":"x: 0.09","values":[0.09]}""",
            Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void NumericRewritingDoesNotTreatArrayItemsAsTheNamedPropertyValue()
    {
        const string document = """{"x":[0.09],"child":{"x":0.09}}""";
        var result = P5aOracleApphost.RewriteNumberLexeme(Encoding.UTF8.GetBytes(document), "x", "0.09", "9e-2");
        Assert.Equal("""{"x":[0.09],"child":{"x":9e-2}}""", Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void NumericRewritingRejectsAnEmptyMutation()
    {
        Assert.ThrowsAny<Exception>(() => P5aOracleApphost.RewriteNumberLexeme(
            Encoding.UTF8.GetBytes("""{"x":0.09001}"""), "x", "0.09", "9e-2"));
    }
}
