using CodexAppServerBlazor.Mcp;

namespace CodexAppServerBlazor.Tests;

public sealed class McpHostFactoryTests
{
    [Fact]
    public void GetHealthToolInventory_uses_live_tool_descriptions_for_declare_session_files()
    {
        McpHealthToolInventoryItem tool = Assert.Single(
            McpHostFactory.GetHealthToolInventory(),
            item => item.Name == "declare_session_files");

        Assert.Contains("Required arguments: `sessionId` and non-empty `watchedFilePaths`", tool.Description, StringComparison.Ordinal);
        Assert.Contains("refresh_file(..., sessionId)", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void GetHealthToolInventory_includes_submit_symbol()
    {
        McpHealthToolInventoryItem tool = Assert.Single(
            McpHostFactory.GetHealthToolInventory(),
            item => item.Name == "submit_symbol");

        Assert.Contains("Replaces a single symbol", tool.Description, StringComparison.Ordinal);
        Assert.Contains("safer than stacking span replacements", tool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void GetHealthToolInventory_marks_replace_span_in_file_as_fallback_only()
    {
        McpHealthToolInventoryItem tool = Assert.Single(
            McpHostFactory.GetHealthToolInventory(),
            item => item.Name == "replace_span_in_file");

        Assert.Contains("Coordinate-based fallback only", tool.Description, StringComparison.Ordinal);
        Assert.Contains("destructive as a planning surface", tool.Description, StringComparison.Ordinal);
    }
}
