using UnrealBuildTool;
public class LyraContextEffectsOracle:ModuleRules
{
    public LyraContextEffectsOracle(ReadOnlyTargetRules Target):base(Target)
    {
        PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine","LyraGame","GameplayTags"});
        PrivateDependencyModuleNames.AddRange(new[]{"Json","PhysicsCore","Niagara","Chaos"});
    }
}
