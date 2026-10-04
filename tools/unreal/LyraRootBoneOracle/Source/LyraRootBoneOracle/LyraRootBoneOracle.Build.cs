using UnrealBuildTool;
public class LyraRootBoneOracle:ModuleRules
{
    public LyraRootBoneOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.AddRange(new[]{"AnimGraphRuntime","Json"});}
}
