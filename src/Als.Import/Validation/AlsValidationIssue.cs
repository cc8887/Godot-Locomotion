namespace GodotAls.Import.Validation;

public sealed record AlsValidationIssue(
    string Code,
    string? AssetId,
    string FieldPath,
    string Message,
    string? Expected = null,
    string? Actual = null);
