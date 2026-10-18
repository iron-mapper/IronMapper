using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;

namespace Comparison.Mappers;

/// <summary>
/// Common facade over every library, used by the equivalence tests.
/// Benchmarks call the libraries directly, not through this interface.
/// </summary>
public interface IComparisonMapper
{
    string Name { get; }
    UserDto MapFlat(UserEntity src);
    OrderDto MapNested(OrderEntity src);
    List<OrderDto> MapCollection(List<OrderEntity> src);
    EmployeeDto MapCustom(EmployeeEntity src);

    /// <summary>Updates <paramref name="dest"/> in place. May throw <see cref="NotSupportedException"/>.</summary>
    void MapInPlace(UserEntity src, UserDto dest);
}
