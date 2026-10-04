using UnrealBuildTool;

public class AlsV4AssetExporter : ModuleRules
{
    public AlsV4AssetExporter(ReadOnlyTargetRules target) : base(target)
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
            "AnimGraphRuntime",
            "AnimationWarpingRuntime",
            "AnimationDataController",
            "AssetRegistry",
            "Chaos",
            "ControlRig",
            "Json",
            "JsonUtilities",
            "IKRig",
            "PhysicsCore",
            "RenderCore",
            "Renderer",
            "RigVM",
            "SSL",
            "UnrealEd",
        });

        AddEngineThirdPartyPrivateStaticDependencies(target, "OpenSSL");
    }
}
