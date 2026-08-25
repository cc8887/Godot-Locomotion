using Godot;

namespace GodotAls.Import;

[Tool]
[GlobalClass]
public partial class AlsImporterPlugin : RefCounted
{
    public bool RunImport()
    {
        try
        {
            var report = AlsGodotImportCoordinator.Run();
            GD.Print($"ALS import completed: assets={report.AssetCount} digest={report.DefinitionDigest}");
            return true;
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            return false;
        }
    }
}
