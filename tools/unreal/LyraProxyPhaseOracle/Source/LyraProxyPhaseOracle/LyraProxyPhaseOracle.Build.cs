using UnrealBuildTool;
public class LyraProxyPhaseOracle:ModuleRules
{
    public LyraProxyPhaseOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.Add("Json");}
}
