namespace GodotAls.Import.Validation;

public sealed record AlsManifestValidationOptions(
    int SupportedSchemaVersion = Manifest.AlsManifest.CurrentSchemaVersion,
    bool RequireCompleteAudit = true);
