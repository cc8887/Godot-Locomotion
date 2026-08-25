using UnrealBuildTool;

public class AlsGodotExporter : ModuleRules
{
    public AlsGodotExporter(ReadOnlyTargetRules target) : base(target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

        PublicDependencyModuleNames.AddRange(new[]
        {
            "Core",
            "CoreUObject",
            "Engine",
        });

        PrivateDependencyModuleNames.AddRange(new[]
        {
            "AssetRegistry",
            "Json",
            "JsonUtilities",
            "UnrealEd",
            "AnimationDataController",
        });
    }
}
