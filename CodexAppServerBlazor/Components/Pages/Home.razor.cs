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
    private string repoRoot = "C:\\CodexAppServerWinForms_corrected";
    private string cwdText = "C:\\CodexAppServerWinForms_corrected";
    private string model = "gpt-5.4";
    private string approvalPolicy = "never";
    private string sandbox = "danger-full-access";
    private string prompt = "Inspect the current workspace. Start with discovery, propose the next safe step, and do not edit files unless explicitly asked.";
    private string? browseChoice;
    private string? errorMessage;
    private bool busy;

    [Inject]
    public CodexConnectionService ConnectionService { get; set; } = default!;

    [Inject]
    public DirectoryBrowserService DirectoryBrowser { get; set; } = default!;

    protected override void OnInitialized()
    {
        ConnectionService.Changed += OnConnectionChanged;
        snapshot = ConnectionService.GetSnapshot();
        LoadDirectory(repoRoot);
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

    private Task ApplyTypedDirectory()
    {
        LoadDirectory(cwdText);
        return Task.CompletedTask;
    }

    private Task SelectBrowseChoice(object? value)
    {
        string? selectedPath = value?.ToString();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            LoadDirectory(selectedPath);
            browseChoice = selectedPath;
        }

        return Task.CompletedTask;
    }

    private Task RefreshDirectory()
    {
        LoadDirectory(directorySnapshot.CurrentPath);
        return Task.CompletedTask;
    }

    private Task OpenDirectory(string path)
    {
        LoadDirectory(path);
        return Task.CompletedTask;
    }

    private Task UseCurrentDirectory()
    {
        if (!string.IsNullOrWhiteSpace(directorySnapshot.CurrentPath))
        {
            repoRoot = directorySnapshot.CurrentPath;
            cwdText = repoRoot;
        }

        return Task.CompletedTask;
    }

    private void LoadDirectory(string? path)
    {
        directorySnapshot = DirectoryBrowser.GetSnapshot(path);
        cwdText = directorySnapshot.CurrentPath;
        browseChoice = directorySnapshot.CurrentPath;
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
