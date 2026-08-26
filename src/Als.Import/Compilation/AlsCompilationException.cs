using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public sealed class AlsCompilationException : Exception
{
    public AlsCompilationException(IEnumerable<AlsValidationIssue> issues)
        : this(issues.ToArray())
    {
    }

    private AlsCompilationException(AlsValidationIssue[] issues)
        : base(CreateMessage(issues))
    {
        Issues = issues;
    }

    public IReadOnlyList<AlsValidationIssue> Issues { get; }

    private static string CreateMessage(IReadOnlyList<AlsValidationIssue> issues) =>
        issues.Count == 0
            ? "ALS asset compilation failed."
            : "ALS asset compilation failed. " + string.Join(
                " ", issues.Select(AlsValidationIssueFormatter.Format));
}
