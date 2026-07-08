using CodexAppServerBlazor.Services.Tasks;
using CodexAppServerBlazor.Services.Workflow;

namespace CodexAppServerBlazor.Tests;

public sealed class WorkflowTurnContextComposerTests
{
    [Fact]
    public void Compose_in_discuss_mode_omits_task_context_and_includes_workspace_once()
    {
        WorkflowTurnContextComposer composer = new();
        WorkflowSessionState sessionState = new("C:\\Work", WorkflowTurnMode.Discuss);
        SessionBootstrapPolicy sessionBootstrapPolicy = new("C:\\policy\\CS-SessionBootstrap.txt", "Host bootstrap.", "loaded");
        WorkflowPromptSection workspaceContext = new("Indexed workspace context.", "ready");
        WorkflowTurnTaskContext taskContext = new("Task details.", "loaded", "task-1");

        WorkflowTurnEnvelope envelope = composer.Compose(
            "Think through the design.",
            "C:\\Work",
            WorkflowTurnMode.Discuss,
            sessionState,
            sessionBootstrapPolicy,
            workspaceContext,
            taskContext);

        Assert.True(envelope.IncludedSessionBootstrap);
        Assert.True(envelope.IncludedWorkspaceContext);
        Assert.Contains("Host bootstrap.", envelope.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Task details.", envelope.Prompt, StringComparison.Ordinal);
        Assert.Contains("Workflow mode:", envelope.Prompt, StringComparison.Ordinal);
        Assert.Contains("- Discuss", envelope.Prompt, StringComparison.Ordinal);
        Assert.Contains("Indexed workspace context.", envelope.Prompt, StringComparison.Ordinal);
        Assert.Contains("User request:", envelope.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_in_work_mode_requires_active_task_context()
    {
        WorkflowTurnContextComposer composer = new();
        WorkflowSessionState sessionState = new("C:\\Work", WorkflowTurnMode.Work);

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            composer.Compose(
                "Implement it.",
                "C:\\Work",
                WorkflowTurnMode.Work,
                sessionState,
                new SessionBootstrapPolicy("C:\\policy\\CS-SessionBootstrap.txt", "Host bootstrap.", "loaded"),
                new WorkflowPromptSection("Indexed workspace context.", "ready"),
                new WorkflowTurnTaskContext(null, "Task context skipped because no Active task is set.", null)));

        Assert.Contains("Cannot start a Work turn without active task context.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_in_work_mode_includes_task_context_and_skips_repeated_workspace_bootstrap()
    {
        WorkflowTurnContextComposer composer = new();
        WorkflowSessionState sessionState = new("C:\\Work", WorkflowTurnMode.Work)
        {
            HasAttachedSessionBootstrap = true,
            HasAttachedWorkspaceContext = true
        };

        WorkflowTurnEnvelope envelope = composer.Compose(
            "Implement it.",
            "C:\\Work",
            WorkflowTurnMode.Work,
            sessionState,
            new SessionBootstrapPolicy("C:\\policy\\CS-SessionBootstrap.txt", "Host bootstrap.", "loaded"),
            new WorkflowPromptSection("Indexed workspace context.", "ready"),
            new WorkflowTurnTaskContext("Task details.", "loaded", "task-1"));

        Assert.False(envelope.IncludedSessionBootstrap);
        Assert.False(envelope.IncludedWorkspaceContext);
        Assert.Contains("Task details.", envelope.Prompt, StringComparison.Ordinal);
        Assert.Contains("Work-mode task authority:", envelope.Prompt, StringComparison.Ordinal);
        Assert.Contains("host-selected Active task above is authoritative", envelope.Prompt, StringComparison.Ordinal);
        Assert.Contains("host governance attached above remains active even if the selected workspace has no local AGENTS.md file", envelope.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attempt that governed MCP path before claiming the discovery surface is unavailable", envelope.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not use shell search, broad task-memory scans, or fallback repo scans", envelope.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host bootstrap.", envelope.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Indexed workspace context.", envelope.Prompt, StringComparison.Ordinal);
        Assert.Contains("- Work", envelope.Prompt, StringComparison.Ordinal);
    }
}
