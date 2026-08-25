using GodotAls.Import.Validation;

namespace GodotAls.Import.Tests;

public sealed class AlsValidationIssueFormatterTests
{
    [Fact]
    public void PreservesEveryStructuredDiagnosticField()
    {
        var issue = new AlsValidationIssue(
            "ALSTEST001",
            "asset-id",
            "$.field",
            "Mismatch.",
            "expected-value",
            "actual-value");

        Assert.Equal(
            "ALSTEST001 asset=asset-id field=$.field expected=expected-value actual=actual-value: Mismatch.",
            AlsValidationIssueFormatter.Format(issue));
    }
}
