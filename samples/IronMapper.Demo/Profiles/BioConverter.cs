using System.Text.RegularExpressions;
using IronMapper.Interfaces;

namespace IronMapper.Demo.Profiles;

/// <summary>
/// Custom ITypeConverter that strips HTML tags and truncates biography text to 300 characters.
/// Called from compile-time generated mapping code — zero reflection at the call site.
/// </summary>
public class BioConverter : ITypeConverter<string?, string?>
{
    public string? Convert(string? source)
    {
        if (source is null) return null;

        // Strip HTML tags
        var clean = Regex.Replace(source, "<[^>]+>", string.Empty);

        // Normalize whitespace
        clean = Regex.Replace(clean.Trim(), @"\s{2,}", " ");

        // Truncate to 300 characters
        return clean.Length > 300 ? clean.Substring(0, 297) + "..." : clean;
    }
}
