namespace GodotAls.Core.Resources;

/// <summary>A logical identity independent of its storage path or bundle.</summary>
public readonly record struct ResourceId
{
    public string Value { get; }

    public ResourceId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value ?? "";
}

public sealed record ResourceStatistics(long Requests, long Hits, long Loads,
    long Failures, long Invalidations, int CachedResources, int LiveVersions, long RetainedBytes);
