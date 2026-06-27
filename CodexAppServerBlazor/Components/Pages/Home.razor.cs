using CodexAppServerBlazor.Services;
using Microsoft.AspNetCore.Components;

namespace CodexAppServerBlazor.Components.Pages;

public partial class Home : IDisposable
{
    private CodexConnectionSnapshot snapshot = new(
        false,
        null,
        string.Empty,
        [],
        [],
        [],
        []);

    private DirectoryBrowserSnapshot directorySnapshot = DirectoryBrowserSnapshot.Empty;
    private string codexExe = "codex";
    private string repoRoot = string.Empty;
    private string model = "gpt-5.4";
    private string approvalPolicy = "never";
    private string sandbox = "danger-full-access";
    private string prompt = "Inspect the current workspace. Start with discovery, propose the next safe step, and do not edit files unless explicitly asked.";
    private string? errorMessage;
    private bool busy;
    private bool isConnectionPanelVisible = true;

    [Inject]
    public CodexConnectionService ConnectionService { get; set; } = default!;

    [Inject]
    public DirectoryBrowserService DirectoryBrowser { get; set; } = default!;

    [Inject]
    public NativeFolderPickerService FolderPicker { get; set; } = default!;

    [Inject]
    public IConfiguration Configuration { get; set; } = default!;

    protected override void OnInitialized()
    {
        ConnectionService.Changed += OnConnectionChanged;
        snapshot = ConnectionService.GetSnapshot();
        string configuredCwd = Configuration["Workspace:DefaultCwd"] ?? Directory.GetCurrentDirectory();
        SetWorkspace(configuredCwd);
    }

    private async Task StartServer()
    {
        await RunCommandAsync(() => ConnectionService.StartServerAsync(codexExe, CancellationToken.None));
    }

    private async Task StartThread()
    {
        await RunCommandAsync(() => ConnectionService.StartThreadAsync(
            repoRoot,
            model,
            approvalPolicy,
            sandbox,
            CancellationToken.None));
    }

    private async Task SendTurn()
    {
        await RunCommandAsync(() => ConnectionService.SendTurnAsync(
            repoRoot,
            prompt,
            model,
            approvalPolicy,
            sandbox,
            CancellationToken.None));
    }

    private async Task BrowseForDirectory()
    {
        string? selectedPath = await FolderPicker.PickFolderAsync(repoRoot, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SetWorkspace(selectedPath);
        }
    }

    private void SetWorkspace(string? path)
    {
        directorySnapshot = DirectoryBrowser.GetSnapshot(path);
        repoRoot = directorySnapshot.CurrentPath;
    }

    private void ToggleConnectionPanel()
    {
        isConnectionPanelVisible = !isConnectionPanelVisible;
    }

    private async Task RunCommandAsync(Func<Task> command)
    {
        try
        {
            busy = true;
            errorMessage = null;
            await command();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            busy = false;
            snapshot = ConnectionService.GetSnapshot();
        }
    }

    private void OnConnectionChanged()
    {
        snapshot = ConnectionService.GetSnapshot();
        _ = InvokeAsync(StateHasChanged);
    }

    private static string JoinLines(IEnumerable<string> lines)
    {
        return string.Join(Environment.NewLine, lines);
    }

    public void Dispose()
    {
        ConnectionService.Changed -= OnConnectionChanged;
    }
}
