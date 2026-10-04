using UnrealBuildTool;
public class LyraWeaponMontageOracle:ModuleRules
{
    public LyraWeaponMontageOracle(ReadOnlyTargetRules Target):base(Target)
    {
        PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});
        PrivateDependencyModuleNames.AddRange(new[]{"Json","AnimGraphRuntime"});
    }
}
