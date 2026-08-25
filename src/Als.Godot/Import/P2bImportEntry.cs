using Godot;

namespace GodotAls.Import;

public partial class P2bImportEntry : Node
{
    public override void _Ready()
    {
        try
        {
            var report = AlsGodotImportCoordinator.Run();
            GD.Print(
                $"P2B_IMPORT_OK assets={report.AssetCount} files={report.FileCount} " +
                $"skeletal={report.SkeletalMeshCount} static={report.StaticMeshCount} " +
                $"animations={report.AnimationCount} textures={report.TextureCount} " +
                $"digest={report.DefinitionDigest}");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }
}
