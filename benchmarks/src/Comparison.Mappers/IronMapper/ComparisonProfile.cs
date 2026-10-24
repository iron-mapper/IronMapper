using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;
using IronMapper.Configuration;
using IronMapper.Generated;

namespace Comparison.Mappers.IronMapper;

/// <summary>
/// All IronMapper mappings of the comparison. The models carry no attributes, so everything is configured here.
/// For Flat the profile emits the same code as [MapTo] (checked by diffing the generated files).
/// </summary>
public sealed class ComparisonProfile : MappingProfile
{
    public ComparisonProfile()
    {
        CreateMap<UserEntity, UserDto>();

#if IRONMAPPER_NESTED_OBJECTS
        // From 2.1 nested objects are mapped by the mappings of their type pairs (docs/advanced.md, "Nested objects").
        CreateMap<AddressEntity, AddressDto>();
        CreateMap<CustomerEntity, CustomerDto>();
        CreateMap<ProductEntity, ProductDto>();
        CreateMap<OrderLineEntity, OrderLineDto>();
        CreateMap<OrderEntity, OrderDto>();
#else
        // Up to 2.0 nested single objects are not mapped automatically: the documented workaround is an explicit
        // MapFrom that calls the generated method.
        CreateMap<AddressEntity, AddressDto>();
        CreateMap<CustomerEntity, CustomerDto>()
            .ForMember(d => d.Address, o => o.MapFrom(s => s.Address.MapToAddressDto()));
        CreateMap<ProductEntity, ProductDto>();
        CreateMap<OrderLineEntity, OrderLineDto>()
            .ForMember(d => d.Product, o => o.MapFrom(s => s.Product.MapToProductDto()));
        CreateMap<OrderEntity, OrderDto>()
            .ForMember(d => d.Customer, o => o.MapFrom(s => s.Customer.MapToCustomerDto()));
#endif

        CreateMap<EmployeeEntity, EmployeeDto>()
            .ForMember(d => d.EmailAddress, o => o.MapFrom(s => s.Email))
            .ForMember(d => d.Notes, o => o.Ignore())
            .ForMember(d => d.Salary, o => o.UseConverter<SalaryConverter>())
            .ForMember(d => d.FullName, o => o.MapFrom(s => s.FirstName + " " + s.LastName));
    }
}
