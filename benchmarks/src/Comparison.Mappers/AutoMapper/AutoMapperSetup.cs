using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comparison.Mappers.AutoMapper;

public static partial class AutoMapperSetup
{
    public const string LicenseKeyVariable = "AUTOMAPPER_LICENSE_KEY";

    /// <summary>Fallback when <see cref="LicenseKeyVariable"/> is not set: <c>~/.automapper/license.txt</c>.</summary>
    public static string LicenseKeyFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".automapper", "license.txt");

    /// <summary>
    /// The license key from <see cref="LicenseKeyVariable"/>, otherwise from <see cref="LicenseKeyFile"/>;
    /// <see langword="null"/> when neither is set. The key is never logged.
    /// </summary>
    public static string? ResolveLicenseKey()
    {
        string? key = Environment.GetEnvironmentVariable(LicenseKeyVariable);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key.Trim();
        }

        if (!File.Exists(LicenseKeyFile))
        {
            return null;
        }

        key = File.ReadAllText(LicenseKeyFile).Trim();
        return key.Length == 0 ? null : key;
    }

    public static MapperConfiguration CreateConfiguration() =>
        new(cfg =>
        {
            cfg.LicenseKey = ResolveLicenseKey();
            cfg.AddProfile<ComparisonAutoMapperProfile>();
        }, NullLoggerFactory.Instance);

    public static IMapper CreateMapper() => CreateConfiguration().CreateMapper();
}
