using System.Text.Json;
using CodexAppServerBlazor.AICodingServices.Core;
using CodexAppServerBlazor.AICodingServices.Workflow;
using CodexAppServerBlazor.Services.Workflow;

namespace CodexAppServerBlazor.Services;

public sealed class TurnUsageHistoryService
{
    private const int MaxHistoryFiles = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly CodingServicesSettingsProvider settingsProvider;

    public TurnUsageHistoryService(CodingServicesSettingsProvider settingsProvider)
    {
        this.settingsProvider = settingsProvider;
    }

    public void RecordTurn(
        string workspaceRoot,
        string? threadId,
        string? turnId,
        WorkflowTurnMode? mode,
        string? activeTaskId,
        string? currentEditSessionId,
        CodexTelemetrySummary telemetrySummary,
        string terminalEventType,
        string terminalSummary,
        bool operatorDecisionRequested,
        bool notesUpdateQuestionRequested)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return;
        }

        try
        {
            CodingServicesSettings settings = settingsProvider.GetSettings(workspaceRoot);
            WorkflowEditPaths paths = new(settings);
            string usageRoot = Path.Combine(paths.HistoryRoot, "turn-usage");
            Directory.CreateDirectory(usageRoot);

            TurnUsageRecord record = new()
            {
                RecordedAtUtc = DateTimeOffset.UtcNow,
                WorkspaceRoot = workspaceRoot,
                WatchedSolutionPath = settings.WatchedSolutionPath,
                ThreadId = threadId,
                TurnId = turnId,
                Mode = mode?.ToString(),
                ActiveTaskId = activeTaskId,
                CurrentEditSessionId = currentEditSessionId,
                DeclaredRelativePaths = LoadDeclaredRelativePaths(paths, currentEditSessionId),
                TerminalEventType = terminalEventType,
                TerminalSummary = terminalSummary,
                OperatorDecisionRequested = operatorDecisionRequested,
                NotesUpdateQuestionRequested = notesUpdateQuestionRequested,
                InputTokens = telemetrySummary.InputTokens,
                CachedInputTokens = telemetrySummary.CachedInputTokens,
                OutputTokens = telemetrySummary.OutputTokens,
                ReasoningOutputTokens = telemetrySummary.ReasoningOutputTokens,
                ModelContextWindow = telemetrySummary.ModelContextWindow,
                PrimaryUsedPercent = telemetrySummary.PrimaryUsedPercent,
                SecondaryUsedPercent = telemetrySummary.SecondaryUsedPercent,
                PlanType = telemetrySummary.PlanType
            };

            string safeThread = SanitizeFileSegment(threadId, "thread");
            string safeTurn = SanitizeFileSegment(turnId, "turn");
            string filePath = Path.Combine(
                usageRoot,
                $"{record.RecordedAtUtc:yyyyMMddTHHmmssfff}-{safeThread}-{safeTurn}.json");
            File.WriteAllText(filePath, JsonSerializer.Serialize(record, JsonOptions));

            TrimHistory(usageRoot);
        }
        catch
        {
            // Best-effort telemetry persistence must never break the interactive workflow.
        }
    }

    private static List<string> LoadDeclaredRelativePaths(WorkflowEditPaths paths, string? currentEditSessionId)
    {
        if (string.IsNullOrWhiteSpace(currentEditSessionId))
        {
            return [];
        }

        string sessionPlanPath = paths.GetSessionPlanPath(currentEditSessionId);
        if (!File.Exists(sessionPlanPath))
        {
            return [];
        }

        EditSessionPlan? plan = JsonSerializer.Deserialize<EditSessionPlan>(File.ReadAllText(sessionPlanPath), JsonOptions);
        return plan?.DeclaredRelativePaths ?? [];
    }

    private static string SanitizeFileSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        char[] buffer = value
            .Select(ch => invalidCharacters.Contains(ch) ? '-' : ch)
            .ToArray();
        string sanitized = new string(buffer).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static void TrimHistory(string usageRoot)
    {
        FileInfo[] files = new DirectoryInfo(usageRoot)
            .GetFiles("*.json")
            .OrderByDescending(file => file.CreationTimeUtc)
            .ToArray();
        if (files.Length <= MaxHistoryFiles)
        {
            return;
        }

        foreach (FileInfo file in files.Skip(MaxHistoryFiles))
        {
            file.Delete();
        }
    }
}

public sealed class TurnUsageRecord
{
    public DateTimeOffset RecordedAtUtc { get; set; }

    public string WorkspaceRoot { get; set; } = string.Empty;

    public string WatchedSolutionPath { get; set; } = string.Empty;

    public string? ThreadId { get; set; }

    public string? TurnId { get; set; }

    public string? Mode { get; set; }

    public string? ActiveTaskId { get; set; }

    public string? CurrentEditSessionId { get; set; }

    public List<string> DeclaredRelativePaths { get; set; } = [];

    public string TerminalEventType { get; set; } = string.Empty;

    public string TerminalSummary { get; set; } = string.Empty;

    public bool OperatorDecisionRequested { get; set; }

    public bool NotesUpdateQuestionRequested { get; set; }

    public int? InputTokens { get; set; }

    public int? CachedInputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public int? ReasoningOutputTokens { get; set; }

    public int? ModelContextWindow { get; set; }

    public int? PrimaryUsedPercent { get; set; }

    public int? SecondaryUsedPercent { get; set; }

    public string? PlanType { get; set; }
}
