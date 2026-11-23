using System.Text.RegularExpressions;

namespace DshTrayLauncher.Services;

public static partial class LogUrlParser
{
    [GeneratedRegex(@"https?://[^\s<>""'\)\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public static IReadOnlyList<string> ExtractUrls(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return [];

        var matches = UrlRegex().Matches(message);
        if (matches.Count == 0)
            return [];

        var results = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            string cleaned = match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '"', '\'');
            if (Uri.TryCreate(cleaned, UriKind.Absolute, out _))
                results.Add(cleaned);
        }

        return results;
    }
}