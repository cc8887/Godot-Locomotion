using UnrealBuildTool;
public class LyraNotifyDispatchOracle:ModuleRules
{
    public LyraNotifyDispatchOracle(ReadOnlyTargetRules Target):base(Target)
    {PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs;PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});PrivateDependencyModuleNames.AddRange(new[]{"Json","UnrealEd","BlueprintGraph","AnimGraph"});}
}
