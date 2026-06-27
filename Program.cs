using CodexAppServerWinForms.Mcp;

namespace CodexAppServerWinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var selectedFileState = new SelectedFileState();
        using var mcpHost = McpHostFactory.Create(selectedFileState);

        mcpHost.Start();

        try
        {
            Application.Run(new MainForm(selectedFileState));
        }
        finally
        {
            mcpHost.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
    }
}
