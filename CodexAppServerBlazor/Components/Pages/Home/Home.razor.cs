using CodexAppServerBlazor.Mcp;
using CodexAppServerBlazor.Services;
using Markdig;
using Markdig.Extensions.MediaLinks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Radzen;
using System.Net;
using System.Text;

namespace CodexAppServerBlazor.Components.Pages.Home;

public partial class Home : IDisposable, IAsyncDisposable
{
    private const string CurrentUserId = "operator";
    private const string AssistantUserId = "codex";

    private CodexConnectionSnapshot snapshot = new(
        false,
        null,
        false,
        string.Empty,
        CodexTelemetrySummary.Empty,
        [],
        [],
        [],
        []);

    private DirectoryBrowserSnapshot directorySnapshot = DirectoryBrowserSnapshot.Empty;
    private SourceWorkspaceSnapshot sourceSnapshot = SourceWorkspaceSnapshot.Empty("No workspace is selected.");
    private SourceWorkspaceSnapshot testSourceSnapshot = SourceWorkspaceSnapshot.Empty("No test project context is loaded.");
    private string codexExe = "codex";
    private string repoRoot = string.Empty;
    private string sourceFilter = string.Empty;
    private string? selectedSourcePath;
    private int? selectedSourceLine;
    private string testSourceFilter = string.Empty;
    private string? selectedTestSourcePath;
    private int? selectedTestSourceLine;
    private string model = "gpt-5.4";
    private string approvalPolicy = "never";
    private string sandbox = "danger-full-access";
    private string mcpUrl = McpHostFactory.DefaultLocalMcpUrl;
    private readonly List<TranscriptMessage> chatMessages = [];
    private string chatDraft = "Inspect the current workspace. Start with discovery, propose the next safe step, and do not edit files unless explicitly asked.";
    private string? activeAssistantMessageId;
    private string renderedAssistantText = string.Empty;
    private string? errorMessage;
    private bool busy;
    private bool isRebuildingSourceIndex;
    private bool isConnectionPanelVisible = true;
    private ElementReference controlGrid;
    private ElementReference connectionPane;
    private ElementReference workPanel;
    private ElementReference mainSplitter;
    private IJSObjectReference? mainResizeModule;
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private string TranscriptHtml => BuildTranscriptHtml();
    private string TranscriptText => BuildTranscriptText();
    private string CurrentTurnHtml => RenderMarkdown(GetCurrentTurnText());

    [Inject]
    public CodexConnectionService ConnectionService { get; set; } = default!;

    [Inject]
    public DirectoryBrowserService DirectoryBrowser { get; set; } = default!;

    [Inject]
    public SourceWorkspaceService SourceWorkspace { get; set; } = default!;

    [Inject]
    public NativeFolderPickerService FolderPicker { get; set; } = default!;

    [Inject]
    public IConfiguration Configuration { get; set; } = default!;

    [Inject]
    public NotificationService NotificationService { get; set; } = default!;

    protected override void OnInitialized()
    {
        ConnectionService.Changed += OnConnectionChanged;
        snapshot = ConnectionService.GetSnapshot();
        mcpUrl = Configuration["Mcp:Url"] ?? McpHostFactory.DefaultLocalMcpUrl;
        string configuredCwd = Configuration["Workspace:DefaultCwd"] ?? Directory.GetCurrentDirectory();
        SetWorkspace(configuredCwd);
    }

    private async Task ToggleServer()
    {
        if (snapshot.IsServerStarted)
        {
            await RunCommandAsync(() => ConnectionService.StopServerAsync(CancellationToken.None));
        }
        else
        {
            if (!ValidateWorkspaceForOperation("start Codex server"))
            {
                return;
            }

            await RunCommandAsync(() => ConnectionService.StartServerAsync(codexExe, CancellationToken.None));
        }
    }

    private async Task StartThread()
    {
        if (!ValidateWorkspaceForOperation("start a Codex thread"))
        {
            return;
        }

        await RunCommandAsync(() => ConnectionService.StartThreadAsync(
            repoRoot,
            model,
            approvalPolicy,
            sandbox,
            CancellationToken.None));
    }

    private async Task SendChatDraft()
    {
        if (busy)
        {
            errorMessage = "A Codex operation is already running.";
            return;
        }

        string content = chatDraft;
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        if (!ValidateWorkspaceForOperation("send a turn"))
        {
            return;
        }

        chatDraft = string.Empty;
        AddUserMessage(content);
        StartAssistantMessage();
        await RunCommandAsync(() => ConnectionService.SendTurnAsync(
            repoRoot,
            content,
            model,
            approvalPolicy,
            sandbox,
            CancellationToken.None));
        RenderAssistantSnapshot(isStreaming: snapshot.IsTurnRunning);
    }

    private async Task BrowseForDirectory()
    {
        string? selectedPath = await FolderPicker.PickFolderAsync(repoRoot, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SetWorkspace(selectedPath);
        }
    }

    private bool ValidateWorkspaceForOperation(string operationName)
    {
        if (!string.IsNullOrWhiteSpace(repoRoot) && Directory.Exists(repoRoot))
        {
            return true;
        }

        string detail = string.IsNullOrWhiteSpace(repoRoot)
            ? "No CWD is selected."
            : $"CWD does not exist: {repoRoot}";
        string message = $"Cannot {operationName}. {detail}";
        errorMessage = message;
        NotificationService.Notify(new NotificationMessage
        {
            Severity = NotificationSeverity.Error,
            Summary = "Invalid CWD",
            Detail = message,
            Duration = 7000
        });
        return false;
    }

    private void SetWorkspace(string? path)
    {
        directorySnapshot = DirectoryBrowser.GetSnapshot(path);
        repoRoot = directorySnapshot.CurrentPath;
        selectedSourcePath = null;
        selectedSourceLine = null;
        selectedTestSourcePath = null;
        selectedTestSourceLine = null;
        RefreshSourceSnapshot();
        RefreshTestSourceSnapshot();
    }

    private void RefreshSourceSnapshot()
    {
        try
        {
            sourceSnapshot = SourceWorkspace.BuildSnapshot(repoRoot, selectedSourcePath, selectedSourceLine, sourceFilter);
            selectedSourcePath = sourceSnapshot.SelectedFile?.RelativePath;
            selectedSourceLine = sourceSnapshot.SelectedFile?.SelectedLine;
        }
        catch (Exception ex)
        {
            sourceSnapshot = SourceWorkspaceSnapshot.Empty(ex.Message);
            selectedSourcePath = null;
            selectedSourceLine = null;
        }
    }

    private async Task RebuildSourceIndex()
    {
        if (isRebuildingSourceIndex)
        {
            return;
        }

        try
        {
            isRebuildingSourceIndex = true;
            errorMessage = null;
            await SourceWorkspace.RebuildIndexAsync(repoRoot, CancellationToken.None);
            RefreshSourceSnapshot();
            RefreshTestSourceSnapshot();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            isRebuildingSourceIndex = false;
        }
    }

    private void SelectSourceFile(SourceSelection selection)
    {
        selectedSourcePath = selection.RelativePath;
        selectedSourceLine = Math.Max(selection.Line, 1);
        RefreshSourceSnapshot();
    }

    private void ApplySourceFilter()
    {
        selectedSourcePath = null;
        selectedSourceLine = null;
        RefreshSourceSnapshot();
    }

    private void RefreshTestSourceSnapshot()
    {
        try
        {
            testSourceSnapshot = SourceWorkspace.BuildTestSnapshot(repoRoot, selectedTestSourcePath, selectedTestSourceLine, testSourceFilter);
            selectedTestSourcePath = testSourceSnapshot.SelectedFile?.RelativePath;
            selectedTestSourceLine = testSourceSnapshot.SelectedFile?.SelectedLine;
        }
        catch (Exception ex)
        {
            testSourceSnapshot = SourceWorkspaceSnapshot.Empty(ex.Message);
            selectedTestSourcePath = null;
            selectedTestSourceLine = null;
        }
    }

    private void SelectTestSourceFile(SourceSelection selection)
    {
        selectedTestSourcePath = selection.RelativePath;
        selectedTestSourceLine = Math.Max(selection.Line, 1);
        RefreshTestSourceSnapshot();
    }

    private void ApplyTestSourceFilter()
    {
        selectedTestSourcePath = null;
        selectedTestSourceLine = null;
        RefreshTestSourceSnapshot();
    }

    private void ToggleConnectionPanel()
    {
        isConnectionPanelVisible = !isConnectionPanelVisible;
    }

    private void ClearChatHistory()
    {
        if (snapshot.IsTurnRunning)
        {
            return;
        }

        chatMessages.Clear();
        activeAssistantMessageId = null;
        renderedAssistantText = string.Empty;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        mainResizeModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "/js/sourceResize.js");
        await mainResizeModule.InvokeVoidAsync(
            "setBeforeUnloadGuard",
            ShouldWarnBeforeUnload(),
            "Refreshing or closing this page will reset the current Coding Services session.");

        if (!isConnectionPanelVisible)
        {
            return;
        }

        await mainResizeModule.InvokeVoidAsync(
            "attachMainSplitter",
            controlGrid,
            connectionPane,
            workPanel,
            mainSplitter);
    }

    private bool ShouldWarnBeforeUnload()
    {
        return snapshot.IsServerStarted
            || snapshot.IsTurnRunning
            || !string.IsNullOrWhiteSpace(snapshot.ThreadId);
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
            RenderAssistantSnapshot(isStreaming: false);
        }
    }

    private void OnConnectionChanged()
    {
        snapshot = ConnectionService.GetSnapshot();
        RenderAssistantSnapshot(isStreaming: snapshot.IsTurnRunning);
        _ = InvokeAsync(StateHasChanged);
    }

    private void AddUserMessage(string content)
    {
        chatMessages.Add(new TranscriptMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = CurrentUserId,
            Role = "user",
            IsUser = true,
            Content = content,
            Timestamp = DateTime.Now
        });
    }

    private void StartAssistantMessage()
    {
        activeAssistantMessageId = Guid.NewGuid().ToString("N");
        renderedAssistantText = string.Empty;
        chatMessages.Add(new TranscriptMessage
        {
            Id = activeAssistantMessageId,
            UserId = AssistantUserId,
            Role = "assistant",
            IsUser = false,
            IsStreaming = true,
            Content = string.Empty,
            Timestamp = DateTime.Now
        });
    }

    private void RenderAssistantSnapshot(bool isStreaming)
    {
        if (string.IsNullOrEmpty(activeAssistantMessageId) || string.IsNullOrEmpty(snapshot.AssistantText))
        {
            return;
        }

        TranscriptMessage? message = chatMessages.FirstOrDefault(candidate => candidate.Id == activeAssistantMessageId);
        if (message is null)
        {
            return;
        }

        renderedAssistantText = snapshot.AssistantText;
        message.Content = renderedAssistantText;
        message.IsStreaming = isStreaming;
        if (!isStreaming)
        {
            activeAssistantMessageId = null;
        }
    }

    private static string JoinLines(IEnumerable<string> lines)
    {
        return string.Join(Environment.NewLine, lines);
    }

    private string BuildTranscriptHtml()
    {
        var html = new StringBuilder();
        html.Append("""
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
        :root {
            color-scheme: light;
            font-family: "Segoe UI", Arial, sans-serif;
            color: #16202c;
            background: #ffffff;
        }
        * {
            box-sizing: border-box;
        }
        body {
            margin: 0;
            padding: 14px;
            font-size: 14px;
            line-height: 1.45;
        }
        .empty {
            display: grid;
            min-height: calc(100vh - 28px);
            place-content: center;
            gap: 8px;
            color: #6c7d8f;
            text-align: center;
        }
        .empty-icon {
            font-size: 34px;
        }
        .message {
            max-width: 1180px;
            margin: 0 0 12px;
            border: 1px solid #d7e1ea;
            border-radius: 7px;
            background: #ffffff;
            overflow: hidden;
        }
        .message.user {
            background: #f7fafc;
        }
        .message.assistant {
            background: #ffffff;
        }
        .message-header {
            display: flex;
            justify-content: space-between;
            gap: 10px;
            border-bottom: 1px solid #e4ebf1;
            padding: 7px 10px;
            color: #526a82;
            font-size: 12px;
            font-weight: 800;
            text-transform: uppercase;
        }
        .message-body {
            padding: 10px 12px;
            overflow-wrap: anywhere;
        }
        .message-body p:first-child {
            margin-top: 0;
        }
        .message-body p:last-child {
            margin-bottom: 0;
        }
        .message-body ul,
        .message-body ol {
            padding-left: 22px;
        }
        .message-body a {
            color: #4169e1;
            text-decoration: none;
        }
        .message-body a:hover {
            text-decoration: underline;
        }
        code, pre {
            font-family: Consolas, "Courier New", monospace;
        }
        code {
            border-radius: 4px;
            padding: 1px 4px;
            background: #eef3f7;
        }
        pre {
            overflow: auto;
            border-radius: 6px;
            padding: 10px;
            background: #0f1722;
            color: #d9e6f2;
            white-space: pre;
        }
        pre code {
            padding: 0;
            background: transparent;
            color: inherit;
        }
        </style>
        </head>
        <body>
        """);

        if (chatMessages.Count == 0)
        {
            html.Append("""
            <div class="empty">
                <div class="empty-icon">□</div>
                <div>Start a workspace conversation.</div>
            </div>
            """);
        }
        else
        {
            foreach (TranscriptMessage message in chatMessages)
            {
                if (message.IsStreaming)
                {
                    continue;
                }

                string role = message.IsUser ? "user" : "assistant";
                string label = message.IsUser ? "You" : "Codex";
                string timestamp = WebUtility.HtmlEncode(message.Timestamp.ToString("HH:mm:ss"));
                html.Append("<article class=\"message ");
                html.Append(role);
                html.Append("\"><header class=\"message-header\"><span>");
                html.Append(label);
                html.Append("</span><span>");
                html.Append(timestamp);
                html.Append("</span></header><div class=\"message-body\">");
                html.Append(RenderMarkdown(message.Content));
                html.Append("</div></article>");
            }
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private string BuildTranscriptText()
    {
        var text = new StringBuilder();
        foreach (TranscriptMessage message in chatMessages)
        {
            if (message.IsStreaming)
            {
                continue;
            }

            string label = message.IsUser ? "You" : "Codex";
            if (text.Length > 0)
            {
                text.AppendLine();
                text.AppendLine();
            }

            text.Append(label);
            text.Append(" ");
            text.AppendLine(message.Timestamp.ToString("HH:mm:ss"));
            text.AppendLine(message.Content);
        }

        return text.ToString();
    }

    private string GetCurrentTurnText()
    {
        if (!string.IsNullOrWhiteSpace(activeAssistantMessageId))
        {
            TranscriptMessage? message = chatMessages.FirstOrDefault(candidate => candidate.Id == activeAssistantMessageId);
            if (message?.IsStreaming == true)
            {
                return message.Content;
            }
        }

        return string.Empty;
    }

    private static string RenderMarkdown(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        return Markdown.ToHtml(markdown, MarkdownPipeline);
    }

    public void Dispose()
    {
        ConnectionService.Changed -= OnConnectionChanged;
    }

    public async ValueTask DisposeAsync()
    {
        if (mainResizeModule is not null)
        {
            try
            {
                await mainResizeModule.InvokeVoidAsync("setBeforeUnloadGuard", false, string.Empty);
                await mainResizeModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}

public sealed class TranscriptMessage
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public bool IsStreaming { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
