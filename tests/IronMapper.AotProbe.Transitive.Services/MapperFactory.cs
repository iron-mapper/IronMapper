using System.Reflection;
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace IronMapper.AotProbe.Transitive.Services;

/// <summary>Builds the container with <c>AddIronMapper</c> for the given assemblies with mappings.</summary>
public static class MapperFactory
{
    public static IMapper Create(params Assembly[] assemblies)
    {
        var services = new ServiceCollection();
        services.AddIronMapper(assemblies);
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }
}
