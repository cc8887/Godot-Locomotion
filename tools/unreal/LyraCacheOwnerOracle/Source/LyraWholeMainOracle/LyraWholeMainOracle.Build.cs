using UnrealBuildTool;
public class LyraWholeMainOracle:ModuleRules
{
    public LyraWholeMainOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.AddRange(new[]{"AnimGraphRuntime","AnimationWarpingRuntime","Json"});}
}
