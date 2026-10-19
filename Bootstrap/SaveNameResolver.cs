namespace DrakesTestLab.Bootstrap;

internal static class SaveNameResolver
{
    public static string ResolveActiveName(string? configured, string fallback)
    {
        var trimmed = configured?.Trim();
        return string.IsNullOrEmpty(trimmed) ? fallback : trimmed;
    }

    public static string ResolveTemplateName(string? configured, string fallback) =>
        ResolveActiveName(configured, fallback);
}
