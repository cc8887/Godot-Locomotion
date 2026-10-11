using GodotAls.Tools.PerformanceTrace;

namespace Als.PerformanceTrace.Tests;

public sealed class TraceAccountingTests
{
    [Fact]
    public void NestedTimersSubtractOnlyDirectChildWork()
    {
        var spans = new List<TraceSpan>
        {
            Span(1, 0, 0, 110, 100), Span(2, 1, 1, 41, 35),
            Span(3, 2, 2, 20, 15), Span(4, 1, 50, 90, 32)
        };
        var errors = new List<string>();
        TraceAnalyzer.ComputeSelfTimes(spans, errors);
        Assert.Empty(errors);
        Assert.Equal(33, spans[0].SelfMs);
        Assert.Equal(20, spans[1].SelfMs);
        Assert.Equal(15, spans[2].SelfMs);
        Assert.Equal(32, spans[3].SelfMs);
        Assert.Equal(spans[0].DurationMs, spans.Sum(s => s.SelfMs));
    }

    [Theory]
    [InlineData(99, 10)]
    [InlineData(1, 11)]
    public void MissingOrCrossThreadParentsAreReported(long parentId, int childThread)
    {
        var spans = new List<TraceSpan> { Span(1, 0, 0, 110, 100), Span(2, parentId, 1, 41, 35, childThread) };
        var errors = new List<string>();
        TraceAnalyzer.ComputeSelfTimes(spans, errors);
        Assert.Contains(errors, e => e.Contains("Invalid parent", StringComparison.Ordinal));
    }

    [Fact]
    public void OverlappingSynchronousSpansCannotPassValidation()
    {
        var spans = new List<TraceSpan>
        { Span(1, 0, 0, 100, 100), Span(2, 1, 1, 61, 50), Span(3, 1, 50, 90, 40) };
        var errors = new List<string>();
        TraceAnalyzer.ComputeSelfTimes(spans, errors);
        Assert.Single(errors);
    }

    [Fact]
    public void AnOutOfParentSpanCannotPassValidation()
    {
        var spans = new List<TraceSpan> { Span(1, 0, 0, 100, 100), Span(2, 1, 80, 120, 35) };
        var errors = new List<string>();
        TraceAnalyzer.ComputeSelfTimes(spans, errors);
        Assert.Single(errors);
    }

    private static TraceSpan Span(long id, long parentId, double startMs, double endMs,
        double durationMs, int threadId = 10) => new()
    {
        Id = id, ParentId = parentId, ProcessId = 1, ThreadId = threadId,
        StartMs = startMs, EndMs = endMs, DurationMs = durationMs, HasEnd = true, Completed = true
    };
}
