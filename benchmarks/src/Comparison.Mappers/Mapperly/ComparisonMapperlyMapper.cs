using Comparison.Mappers.Shared;
using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using Riok.Mapperly.Abstractions;

namespace Comparison.Mappers.Mapperly;

[Mapper]
public static partial class ComparisonMapperlyMapper
{
    public static partial UserDto MapFlat(UserEntity src);

    public static partial void MapInPlace(UserEntity src, [MappingTarget] UserDto dest);

    public static partial OrderDto MapNested(OrderEntity src);

    public static partial List<OrderDto> MapCollection(List<OrderEntity> src);

    [MapProperty(nameof(EmployeeEntity.Email), nameof(EmployeeDto.EmailAddress))]
    [MapperIgnoreSource(nameof(EmployeeEntity.Notes))]
    [MapperIgnoreTarget(nameof(EmployeeDto.Notes))]
    [MapProperty(nameof(EmployeeEntity.Salary), nameof(EmployeeDto.Salary), Use = nameof(FormatSalary))]
    [MapPropertyFromSource(nameof(EmployeeDto.FullName), Use = nameof(GetFullName))]
    public static partial EmployeeDto MapCustom(EmployeeEntity src);

    [UserMapping(Default = false)]
    private static string FormatSalary(decimal salary) => SalaryFormat.Format(salary);

    [UserMapping(Default = false)]
    private static string GetFullName(EmployeeEntity src) => src.FirstName + " " + src.LastName;
}
