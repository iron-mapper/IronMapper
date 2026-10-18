using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Comparison.Mappers.AutoMapper;

// Separate file: the startup probes link AutoMapperSetup.cs without the DI dependencies.
public static partial class AutoMapperSetup
{
    /// <summary>The DI path: <c>AddAutoMapper</c> + <see cref="IMapper"/> resolved from a scope (DiOverheadBenchmark).</summary>
    public static IMapper CreateMapperFromServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = ResolveLicenseKey();
            cfg.AddProfile<ComparisonAutoMapperProfile>();
        });
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMapper>();
    }
}
