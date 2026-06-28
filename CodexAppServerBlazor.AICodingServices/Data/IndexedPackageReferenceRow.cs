namespace CodexAppServerBlazor.AICodingServices.Data;

public sealed record IndexedPackageReferenceRow(
    string ProjectPath,
    string Include,
    string Version);
