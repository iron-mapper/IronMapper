using Comparison.Mappers.Shared;
using Comparison.Models.Custom;
using IronMapper.Configuration;

namespace Comparison.Mappers.IronMapperStaticFormat;

/// <summary>
/// Same mapping as the EmployeeEntity → EmployeeDto map in ComparisonProfile, except the converter:
/// a static method call in MapFrom (workaround) instead of UseConverter&lt;SalaryConverter&gt;().
/// </summary>
public sealed class StaticFormatProfile : MappingProfile
{
    public StaticFormatProfile()
    {
        CreateMap<EmployeeEntity, EmployeeDto>()
            .ForMember(d => d.EmailAddress, o => o.MapFrom(s => s.Email))
            .ForMember(d => d.Notes, o => o.Ignore())
            .ForMember(d => d.Salary, o => o.MapFrom(s => SalaryFormat.Format(s.Salary)))
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName + " " + s.LastName));
    }
}
