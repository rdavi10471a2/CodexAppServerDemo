namespace CodexAppServerBlazor.AICodingServices.MSBuild;

public interface IBuildRunner
{
    BuildResult Run(BuildRequest request, TimeSpan timeout);
}