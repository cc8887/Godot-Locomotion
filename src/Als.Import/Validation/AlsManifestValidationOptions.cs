namespace GodotAls.Import.Validation;

public sealed record AlsManifestValidationOptions(
    int SupportedSchemaVersion = Manifest.AlsManifest.CurrentSchemaVersion,
    string SupportedExporterVersion = Manifest.AlsManifest.CurrentExporterVersion,
    bool RequireCompleteAudit = true);
