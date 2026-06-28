using CodexAppServerBlazor.Services;

namespace CodexAppServerBlazor.Tests;

public sealed class CodexTelemetrySummaryTests
{
    [Fact]
    public void Apply_merges_token_and_rate_events()
    {
        CodexTelemetrySummary summary = CodexTelemetrySummary.Empty
            .Apply(new TelemetryEvent(
                InputTokens: 25_000,
                CachedInputTokens: 20_000,
                OutputTokens: 500,
                ReasoningOutputTokens: 50,
                ModelContextWindow: 100_000,
                PrimaryUsedPercent: null,
                SecondaryUsedPercent: null,
                PlanType: null,
                Summary: "tokens"))
            .Apply(new TelemetryEvent(
                InputTokens: null,
                CachedInputTokens: null,
                OutputTokens: null,
                ReasoningOutputTokens: null,
                ModelContextWindow: null,
                PrimaryUsedPercent: 34,
                SecondaryUsedPercent: 37,
                PlanType: "prolite",
                Summary: "rate"));

        Assert.Equal(25_000, summary.InputTokens);
        Assert.Equal(20_000, summary.CachedInputTokens);
        Assert.Equal(500, summary.OutputTokens);
        Assert.Equal(50, summary.ReasoningOutputTokens);
        Assert.Equal(100_000, summary.ModelContextWindow);
        Assert.Equal(34, summary.PrimaryUsedPercent);
        Assert.Equal(37, summary.SecondaryUsedPercent);
        Assert.Equal("prolite", summary.PlanType);
        Assert.Equal(25.0, summary.ContextUsedPercent);
    }

    [Fact]
    public void ContextUsedPercent_is_empty_without_a_valid_window()
    {
        CodexTelemetrySummary summary = CodexTelemetrySummary.Empty.Apply(new TelemetryEvent(
            InputTokens: 25_000,
            CachedInputTokens: null,
            OutputTokens: null,
            ReasoningOutputTokens: null,
            ModelContextWindow: 0,
            PrimaryUsedPercent: null,
            SecondaryUsedPercent: null,
            PlanType: null,
            Summary: "tokens"));

        Assert.Null(summary.ContextUsedPercent);
    }
}
