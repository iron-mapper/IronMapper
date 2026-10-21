using Comparison.Models.Nested;
using Microsoft.Extensions.DependencyInjection;

namespace Comparison.Mappers.Manual;

/// <summary>
/// Hand-written mapping resolved from the same Microsoft.Extensions.DependencyInjection container as
/// <c>IronMapperDiSetup</c>: one singleton registered by type, the provider built, the service resolved once. The cold
/// start of <c>IronMapper_IMapper</c> minus this one is what IronMapper adds on top of the container.
/// </summary>
public static class ManualDiSetup
{
    public static ManualOrderMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ManualOrderMapper>();
        return services.BuildServiceProvider().GetRequiredService<ManualOrderMapper>();
    }
}

/// <summary>The hand-written Nested mapping as a service.</summary>
public sealed class ManualOrderMapper
{
    public OrderDto Map(OrderEntity source) => ManualMappings.MapNested(source);
}
