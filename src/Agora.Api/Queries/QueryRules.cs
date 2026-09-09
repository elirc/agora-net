namespace Agora.Api.Queries;

/// <summary>Small shared input rules; these methods do not execute queries.</summary>
internal static class QueryRules
{
    public static bool ValidPage(int page, int pageSize, int maxPageSize = 100) =>
        page >= 1 && pageSize >= 1 && pageSize <= maxPageSize
        && (long)(page - 1) * pageSize <= int.MaxValue;

    public static string LiteralContains(string value) =>
        "%" + value.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    // Enum.TryParse alone also accepts numbers and comma-separated names.
    public static bool TryNamedEnum<T>(string? value, out T result) where T : struct, Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(n =>
            string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        result = default;
        return name is not null && Enum.TryParse(name, out result);
    }
}
