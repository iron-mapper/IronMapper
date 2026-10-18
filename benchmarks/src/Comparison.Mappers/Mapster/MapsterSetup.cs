using Comparison.Mappers.Shared;
using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;

namespace Comparison.Mappers.Mapster;

public static class MapsterSetup
{
    /// <summary>A dedicated config (not GlobalSettings), compiled up front.</summary>
    public static TypeAdapterConfig CreateConfig()
    {
        var config = new TypeAdapterConfig();

        config.NewConfig<UserEntity, UserDto>();
        config.NewConfig<OrderEntity, OrderDto>();

        config.NewConfig<EmployeeEntity, EmployeeDto>()
            .Map(d => d.EmailAddress, s => s.Email)
            .Ignore(d => d.Notes!)
            .Map(d => d.Salary, s => SalaryFormat.Format(s.Salary))
            .Map(d => d.FullName, s => s.FirstName + " " + s.LastName);

        config.Compile();
        return config;
    }

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
