using IronMapper.Extensions.DI;
using Microsoft.Extensions.DependencyInjection;
using IIronMapper = IronMapper.Interfaces.IMapper;

namespace Comparison.Mappers.IronMapper;

/// <summary>
/// The DI path: <c>AddIronMapper</c> + <see cref="IIronMapper"/> resolved once. Up to 1.1.x the mapper finds and calls the
/// generated methods through reflection (<c>MethodInfo.Invoke</c>); from 1.2.0 through the generated registry (delegates).
/// </summary>
public static class IronMapperDiSetup
{
    public static IIronMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(o => o.AddProfile<ComparisonProfile>());
        return services.BuildServiceProvider().GetRequiredService<IIronMapper>();
    }
}
