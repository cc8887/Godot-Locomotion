using UnrealBuildTool;
public class LyraMontageNotifyOracle : ModuleRules
{
    public LyraMontageNotifyOracle(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[]{"Core","CoreUObject","Engine"});
        PrivateDependencyModuleNames.AddRange(new[]{"Json","UnrealEd"});
    }
}
