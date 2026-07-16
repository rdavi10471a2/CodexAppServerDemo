using CodexAppServerBlazor.Services;

namespace CodexAppServerBlazor.Tests;

public sealed class CodexTelemetrySummaryTests
{
    [Fact]
    public void Apply_merges_token_and_rate_events()
    {
        CodexTelemetrySummary summary = CodexTelemetrySummary.Empty
            .Apply(new TelemetryEvent(
                TurnUsage: new TelemetryUsage(25_000, 20_000, 500, 50, 25_550),
                SessionUsage: new TelemetryUsage(25_000, 20_000, 500, 50, 25_550),
                ModelContextWindow: 100_000,
                PrimaryUsedPercent: null,
                SecondaryUsedPercent: null,
                PlanType: null,
                TurnId: "turn-1",
                Summary: "tokens"))
            .Apply(new TelemetryEvent(
                TurnUsage: null,
                SessionUsage: null,
                ModelContextWindow: null,
                PrimaryUsedPercent: 34,
                SecondaryUsedPercent: 37,
                PlanType: "prolite",
                TurnId: null,
                Summary: "rate"));

        Assert.Equal(25_000, summary.CurrentTurn.InputTokens);
        Assert.Equal(20_000, summary.CurrentTurn.CachedInputTokens);
        Assert.Equal(500, summary.CurrentTurn.OutputTokens);
        Assert.Equal(50, summary.CurrentTurn.ReasoningOutputTokens);
        Assert.Equal(25_550, summary.CurrentTurn.TotalTokens);
        Assert.Equal(100_000, summary.CurrentTurn.ModelContextWindow);
        Assert.Equal(25_000, summary.SessionTotal.InputTokens);
        Assert.Equal(34, summary.RateLimits.PrimaryUsedPercent);
        Assert.Equal(37, summary.RateLimits.SecondaryUsedPercent);
        Assert.Equal("prolite", summary.RateLimits.PlanType);
        Assert.Equal("turn-1", summary.LastTurnId);
    }
}
