using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comparison.Mappers.AutoMapper;

public static class AutoMapperSetup
{
    public const string LicenseKeyVariable = "AUTOMAPPER_LICENSE_KEY";

    public static MapperConfiguration CreateConfiguration() =>
        new(cfg =>
        {
            cfg.LicenseKey = Environment.GetEnvironmentVariable(LicenseKeyVariable);
            cfg.AddProfile<ComparisonAutoMapperProfile>();
        }, NullLoggerFactory.Instance);

    public static IMapper CreateMapper() => CreateConfiguration().CreateMapper();
}
