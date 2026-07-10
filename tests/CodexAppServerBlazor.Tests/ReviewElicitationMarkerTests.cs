using CodexAppServerBlazor.Mcp;

namespace CodexAppServerBlazor.Tests;

public sealed class ReviewElicitationMarkerTests
{
    [Fact]
    public void Build_then_TryParse_round_trips_session_id()
    {
        string marker = ReviewElicitationMarker.Build("session-123");

        bool parsed = ReviewElicitationMarker.TryParse(marker, out string sessionId);

        Assert.True(parsed);
        Assert.Equal("session-123", sessionId);
    }

    [Fact]
    public void TryParse_finds_marker_embedded_in_a_larger_message()
    {
        string message = "Governed review ready for edit session 'edit-777'. Review detail: "
            + "/review/session/edit-777 " + ReviewElicitationMarker.Build("edit-777");

        bool parsed = ReviewElicitationMarker.TryParse(message, out string sessionId);

        Assert.True(parsed);
        Assert.Equal("edit-777", sessionId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no marker in this message")]
    [InlineData("[[aim-review:]] empty session id is not valid")]
    public void TryParse_returns_false_without_a_valid_marker(string? text)
    {
        bool parsed = ReviewElicitationMarker.TryParse(text, out string sessionId);

        Assert.False(parsed);
        Assert.Equal(string.Empty, sessionId);
    }
}
