namespace GodotAls.Core.Tests;

public static class AllocationTestCollection
{
    public const string Name = "Allocation";
}

[CollectionDefinition(AllocationTestCollection.Name, DisableParallelization = true)]
public sealed class AllocationTestCollectionDefinition
{
}
