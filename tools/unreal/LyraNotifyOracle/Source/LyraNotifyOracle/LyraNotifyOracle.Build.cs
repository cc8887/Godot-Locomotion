using UnrealBuildTool;
public class LyraNotifyOracle : ModuleRules
{
    public LyraNotifyOracle(ReadOnlyTargetRules target) : base(target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] {"Core", "CoreUObject", "Engine"});
        PrivateDependencyModuleNames.AddRange(new[] {"Json", "UnrealEd"});
    }
}
