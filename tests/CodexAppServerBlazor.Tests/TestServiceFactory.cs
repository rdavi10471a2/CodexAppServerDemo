using CodexAppServerBlazor.Services;
using Microsoft.Extensions.Configuration;

namespace CodexAppServerBlazor.Tests;

internal static class TestServiceFactory
{
    public static CodingServicesSettingsProvider CreateSettingsProvider(
        IConfiguration configuration,
        string contentRootPath)
    {
        return new CodingServicesSettingsProvider(configuration, new TestHostEnvironment(contentRootPath));
    }
}
