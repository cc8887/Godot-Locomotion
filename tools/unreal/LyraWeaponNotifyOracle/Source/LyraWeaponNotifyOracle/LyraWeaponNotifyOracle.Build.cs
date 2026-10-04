using UnrealBuildTool;
public class LyraWeaponNotifyOracle:ModuleRules
{
    public LyraWeaponNotifyOracle(ReadOnlyTargetRules Target):base(Target)
    {
        PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine","LyraGame"});
        PrivateDependencyModuleNames.AddRange(new[]{"Json","BlueprintGraph","UnrealEd","ModularGameplay"});
    }
}
