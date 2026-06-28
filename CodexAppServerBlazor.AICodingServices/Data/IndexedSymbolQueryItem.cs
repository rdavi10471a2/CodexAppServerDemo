namespace CodexAppServerBlazor.AICodingServices.Data;

public sealed record IndexedSymbolQueryItem(
    IndexedSymbolRow Symbol,
    string RelativePath,
    string SelectorHintJson);
