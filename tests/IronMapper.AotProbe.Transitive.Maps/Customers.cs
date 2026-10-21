using System.Collections.Generic;
using IronMapper.Attributes;
using IronMapper.Configuration;

namespace IronMapper.AotProbe.Transitive.Maps;

[MapTo(typeof(CustomerDto))]
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Address> Addresses { get; set; } = new();
}

public class CustomerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<AddressDto> Addresses { get; set; } = new();
}

public class Address
{
    public string City { get; set; } = string.Empty;
}

public class AddressDto
{
    public string Town { get; set; } = string.Empty;
}

public class AddressProfile : MappingProfile
{
    public AddressProfile()
    {
        CreateMap<Address, AddressDto>()
            .ForMember(d => d.Town, o => o.MapFrom(s => s.City));
    }
}
