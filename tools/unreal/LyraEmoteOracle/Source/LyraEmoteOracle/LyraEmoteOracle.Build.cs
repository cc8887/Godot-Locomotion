using UnrealBuildTool;
public class LyraEmoteOracle:ModuleRules
{
    public LyraEmoteOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.AddRange(new[]{"Json","LyraGame","GameplayAbilities","GameplayTasks","GameplayTags","UnrealEd","BlueprintGraph"});}
}
