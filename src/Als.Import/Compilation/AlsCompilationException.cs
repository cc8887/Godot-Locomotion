using GodotAls.Import.Validation;

namespace GodotAls.Import.Compilation;

public sealed class AlsCompilationException : Exception
{
    public AlsCompilationException(IEnumerable<AlsValidationIssue> issues)
        : base("ALS asset compilation failed.")
    {
        Issues = issues.ToArray();
    }

    public IReadOnlyList<AlsValidationIssue> Issues { get; }
}
