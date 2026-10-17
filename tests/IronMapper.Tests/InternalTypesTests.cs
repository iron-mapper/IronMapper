using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;
using Xunit;

namespace IronMapper.Tests;

// -----------------------------------------------------------------------
// Fixtures: internal and nested types. Before the fix the generated public
// methods referencing these types failed to compile (CS0050/CS0051).
// -----------------------------------------------------------------------

[MapTo(typeof(InternalCustomerDto))]
internal class InternalCustomer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal class InternalCustomerDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

internal static class NestingHost
{
    [MapTo(typeof(NestedLineDto))]
    public class NestedLine
    {
        public int Quantity { get; set; }
    }

    public class NestedLineDto
    {
        public int Quantity { get; set; }
    }

    public class ProfiledLine
    {
        public string Sku { get; set; } = string.Empty;
    }

    public class ProfiledLineDto
    {
        public string Sku { get; set; } = string.Empty;
    }
}

internal class NestingHostProfile : MappingProfile
{
    public NestingHostProfile()
    {
        CreateMap<NestingHost.ProfiledLine, NestingHost.ProfiledLineDto>();
    }
}

public class InternalTypesTests
{
    [Fact]
    public void InternalTypes_ViaAttribute_MapAndCompanionMethodsWork()
    {
        var customer = new InternalCustomer { Id = 5, Name = "Eve" };

        var dto = customer.MapToInternalCustomerDto();
        var list = new[] { customer }.MapToInternalCustomerDtoList();
        var array = new[] { customer }.MapToInternalCustomerDtoArray();
        var existing = new InternalCustomerDto();
        customer.MapToInternalCustomerDto(existing);

        Assert.Equal(5, dto.Id);
        Assert.Equal("Eve", dto.Name);
        Assert.Equal("Eve", Assert.Single(list).Name);
        Assert.Equal(5, Assert.Single(array).Id);
        Assert.Equal("Eve", existing.Name);
    }

    [Fact]
    public void PublicTypesNestedInInternalClass_ViaAttribute_Map()
    {
        var dto = new NestingHost.NestedLine { Quantity = 4 }.MapToNestedLineDto();

        Assert.Equal(4, dto.Quantity);
    }

    [Fact]
    public void PublicTypesNestedInInternalClass_ViaProfile_Map()
    {
        var dto = new NestingHost.ProfiledLine { Sku = "A-1" }.MapToProfiledLineDto();

        Assert.Equal("A-1", dto.Sku);
    }
}
