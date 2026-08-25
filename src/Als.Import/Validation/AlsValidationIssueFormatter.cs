namespace GodotAls.Import.Validation;

public static class AlsValidationIssueFormatter
{
    public static string Format(AlsValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        return $"{issue.Code} asset={issue.AssetId ?? "<none>"} field={issue.FieldPath} " +
            $"expected={issue.Expected ?? "<unspecified>"} actual={issue.Actual ?? "<unspecified>"}: " +
            issue.Message;
    }
}
