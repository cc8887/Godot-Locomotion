using UnrealBuildTool;
public class LyraMotionWarpingOracle:ModuleRules
{
    public LyraMotionWarpingOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.AddRange(new[]{"Json","MotionWarping","UnrealEd"});}
}
