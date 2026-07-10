namespace CodexAppServerBlazor.Services.Workflow;

public sealed record WorkflowPromptSection(
    string? PromptMarkdown,
    string Status)
{
    public bool HasPrompt => !string.IsNullOrWhiteSpace(PromptMarkdown);
}
