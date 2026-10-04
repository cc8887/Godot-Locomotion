using UnrealBuildTool;
public class LyraRootMovementOracle:ModuleRules
{
    public LyraRootMovementOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.Add("Json");}
}
