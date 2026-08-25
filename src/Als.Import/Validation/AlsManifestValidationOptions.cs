namespace GodotAls.Import.Validation;

public sealed record AlsManifestValidationOptions(
    int SupportedSchemaVersion = 1,
    bool RequireCompleteAudit = true);
