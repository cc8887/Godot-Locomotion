using UnrealBuildTool;
public class LyraWeaponEquipmentOracle:ModuleRules
{
    public LyraWeaponEquipmentOracle(ReadOnlyTargetRules Target):base(Target)
    {
        PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});
        PrivateDependencyModuleNames.AddRange(new[]{"Json","AnimGraphRuntime","LyraGame","ModularGameplay"});
    }
}
