using System.Text.RegularExpressions;

namespace NetIntercom.Application.Common;

/// <summary>Renders {{placeholder}} tokens in canned reply bodies. Unknown tokens are left intact.</summary>
public static partial class PlaceholderRenderer
{
    [GeneratedRegex(@"\{\{\s*([\w.]+)\s*\}\}")]
    private static partial Regex TokenRegex();

    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        return TokenRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)
                ? value
                : match.Value;
        });
    }
}
