using Comparison.Mappers.Shared;
using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using Mapster;

namespace Comparison.Mappers.Mapster;

public static partial class MapsterSetup
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
}
