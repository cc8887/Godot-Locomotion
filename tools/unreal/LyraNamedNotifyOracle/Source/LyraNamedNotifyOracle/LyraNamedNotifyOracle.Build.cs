using UnrealBuildTool;
public class LyraNamedNotifyOracle:ModuleRules
{
    public LyraNamedNotifyOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.Add("Json");}
}
