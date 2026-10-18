using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace Comparison.Mappers.Mapster;

// Separate file: the startup probes link MapsterSetup.cs without the DI dependencies.
public static partial class MapsterSetup
{
    /// <summary>
    /// The DI path (Mapster.DependencyInjection): the config as a singleton + <see cref="ServiceMapper"/> as the scoped
    /// <see cref="IMapper"/>, resolved from a scope (DiOverheadBenchmark).
    /// </summary>
    public static IMapper CreateMapperFromServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateConfig());
        services.AddScoped<IMapper, ServiceMapper>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMapper>();
    }
}
