using UnrealBuildTool;
public class LyraGameplayNotifyOracle:ModuleRules
{
    public LyraGameplayNotifyOracle(ReadOnlyTargetRules Target):base(Target)
    {
        PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});
        PrivateDependencyModuleNames.AddRange(new[]{"Json","GameplayAbilities","GameplayTags"});
    }
}
