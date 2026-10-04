using UnrealBuildTool;
public class LyraStartupOracle:ModuleRules
{
    public LyraStartupOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.AddRange(new[]{"AnimGraphRuntime","Json"});}
}
