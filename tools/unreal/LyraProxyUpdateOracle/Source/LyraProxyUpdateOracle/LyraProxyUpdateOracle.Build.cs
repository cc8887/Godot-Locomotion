using UnrealBuildTool;
public class LyraProxyUpdateOracle:ModuleRules
{
    public LyraProxyUpdateOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.AddRange(new[]{"AnimGraphRuntime","AnimationWarpingRuntime","ControlRig","RigVM","Json","GameplayAbilities"});}
}
