using System.Text;
using CodexAppServerBlazor.Mcp;

namespace CodexAppServerBlazor.Services.Workflow;

public sealed class WorkspaceWorkflowContextService : IWorkspaceWorkflowContextService
{
    private readonly SourceWorkspaceService sourceWorkspaceService;

    public WorkspaceWorkflowContextService(SourceWorkspaceService sourceWorkspaceService)
    {
        this.sourceWorkspaceService = sourceWorkspaceService;
    }

    public WorkflowPromptSection BuildTurnContext(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return new WorkflowPromptSection(null, $"CWD does not exist: {workspaceRoot}");
        }

        SourceWorkspaceStructureSnapshot snapshot;
        try
        {
            snapshot = sourceWorkspaceService.BuildProductStructureSnapshot(workspaceRoot, filter: null);
        }
        catch (Exception ex)
        {
            return new WorkflowPromptSection(null, $"Could not build workspace context: {ex.Message}");
        }

        if (!File.Exists(snapshot.WatchedSolutionPath))
        {
            return new WorkflowPromptSection(null, $"No valid watched solution found for CWD: {workspaceRoot}");
        }

        if (!File.Exists(snapshot.IndexDatabasePath) || snapshot.FileCount == 0 || snapshot.Tree.Count == 0)
        {
            return new WorkflowPromptSection(
                null,
                string.IsNullOrWhiteSpace(snapshot.Message)
                    ? "Watched solution index is missing, empty, or stale."
                    : snapshot.Message);
        }

        StringBuilder builder = new();
        builder.AppendLine("Indexed workspace context supplied by Coding Services:");
        builder.AppendLine($"- CWD: {Path.GetFullPath(workspaceRoot)}");
        builder.AppendLine($"- Watched solution: {snapshot.WatchedSolutionPath}");
        builder.AppendLine($"- Index database: {snapshot.IndexDatabasePath}");
        builder.AppendLine($"- Indexed product files: {snapshot.FileCount}");
        builder.AppendLine($"- Product projects: {snapshot.Tree.Count}");
        builder.AppendLine("- MCP discovery tools: get_workspace, get_watched_solution_digest, get_watched_solution_summary, get_test_project_summary");
        builder.AppendLine("- Test projects are omitted from this startup context; call get_test_project_summary when tests matter.");
        builder.AppendLine("- Product project/file map:");

        foreach (SourceTreeNode project in snapshot.Tree.Where(node => node.Kind.Equals("project", StringComparison.OrdinalIgnoreCase)))
        {
            List<string> files = [];
            CollectFilePaths(project, files);
            builder.AppendLine($"  - {project.Name} ({files.Count} files)");
            foreach (string file in files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"    - {file}");
            }
        }

        return new WorkflowPromptSection(
            builder.ToString(),
            $"Attached brief indexed workspace context for {snapshot.Tree.Count} projects and {snapshot.FileCount} product files.");
    }

    private static void CollectFilePaths(SourceTreeNode node, List<string> files)
    {
        if (node.File is not null)
        {
            files.Add(node.File.RelativePath);
            return;
        }

        foreach (SourceTreeNode child in node.Children)
        {
            CollectFilePaths(child, files);
        }
    }
}
