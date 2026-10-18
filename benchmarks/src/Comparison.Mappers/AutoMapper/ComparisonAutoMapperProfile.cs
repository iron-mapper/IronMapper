using AutoMapper;
using Comparison.Mappers.Shared;
using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;

namespace Comparison.Mappers.AutoMapper;

public sealed class ComparisonAutoMapperProfile : Profile
{
    public ComparisonAutoMapperProfile()
    {
        CreateMap<UserEntity, UserDto>();

        CreateMap<AddressEntity, AddressDto>();
        CreateMap<CustomerEntity, CustomerDto>();
        CreateMap<ProductEntity, ProductDto>();
        CreateMap<OrderLineEntity, OrderLineDto>();
        CreateMap<OrderEntity, OrderDto>();

        CreateMap<EmployeeEntity, EmployeeDto>()
            .ForMember(d => d.EmailAddress, o => o.MapFrom(s => s.Email))
            .ForMember(d => d.Notes, o => o.Ignore())
            .ForMember(d => d.Salary, o => o.ConvertUsing(new SalaryValueConverter(), s => s.Salary))
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName + " " + s.LastName));
    }

    private sealed class SalaryValueConverter : IValueConverter<decimal, string>
    {
        public string Convert(decimal sourceMember, ResolutionContext context) => SalaryFormat.Format(sourceMember);
    }
}
