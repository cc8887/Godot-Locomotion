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
            "ALS",
            "ControlRig",
            "RigVM",
            "GameplayTags",
            "AnimGraphRuntime",
            "AssetRegistry",
            "Json",
            "JsonUtilities",
            "RenderCore",
            "Renderer",
            "SSL",
            "UnrealEd",
            "AnimationDataController",
        });

        AddEngineThirdPartyPrivateStaticDependencies(target, "OpenSSL");
    }
}
