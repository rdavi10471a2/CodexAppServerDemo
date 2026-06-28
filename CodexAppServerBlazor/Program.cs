using CodexAppServerBlazor.Components;
using CodexAppServerBlazor.Services;
using CodexAppServerBlazor.Mcp;
using Radzen;

namespace CodexAppServerBlazor;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseStaticWebAssets();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();
        builder.Services.AddRadzenComponents();
        builder.Services.AddSingleton<WorkspaceState>();
        builder.Services.AddSingleton<CodexConnectionService>();
        builder.Services.AddSingleton<DirectoryBrowserService>();
        builder.Services.AddSingleton<CodingServicesSettingsProvider>();
        builder.Services.AddSingleton<SourceWorkspaceService>();
        builder.Services.AddSingleton<NativeFolderPickerService>();
        builder.Services.AddSingleton<HarnessMcpHostedService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<HarnessMcpHostedService>());
        builder.Services.AddHostedService<WorkspaceStartupHostedService>();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
