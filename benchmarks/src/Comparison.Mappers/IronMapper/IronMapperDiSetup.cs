using IronMapper.Extensions.DI;
using Microsoft.Extensions.DependencyInjection;
using IIronMapper = IronMapper.Interfaces.IMapper;

namespace Comparison.Mappers.IronMapper;

/// <summary>The DI path: <c>AddIronMapper</c> + <see cref="IIronMapper"/> resolved once (reflection + MethodInfo.Invoke inside).</summary>
public static class IronMapperDiSetup
{
    public static IIronMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(o => o.AddProfile<ComparisonProfile>());
        return services.BuildServiceProvider().GetRequiredService<IIronMapper>();
    }
}
