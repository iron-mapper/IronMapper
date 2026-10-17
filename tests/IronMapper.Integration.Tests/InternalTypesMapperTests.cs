using System.Linq;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests;

// -----------------------------------------------------------------------
// Internal fixtures: the generated methods for these types are internal,
// so IMapper must find non-public generated methods.
// -----------------------------------------------------------------------

[MapTo(typeof(InternalAccountDto))]
internal class InternalAccount
{
    public int Id { get; set; }
    public string Owner { get; set; } = string.Empty;
}

internal class InternalAccountDto
{
    public int Id { get; set; }
    public string Owner { get; set; } = string.Empty;
}

internal class InternalInvoice
{
    public int Number { get; set; }
    public decimal Amount { get; set; }
}

public class PublicInvoiceDto
{
    public int Number { get; set; }
    public decimal Amount { get; set; }
}

internal class InternalInvoiceProfile : MappingProfile
{
    public InternalInvoiceProfile()
    {
        CreateMap<InternalInvoice, PublicInvoiceDto>();
    }
}

public class InternalTypesMapperTests
{
    private static IMapper CreateMapper()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(InternalTypesMapperTests).Assembly);
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    [Fact]
    public void Map_TwoGenerics_InternalSourceAndDestination_Maps()
    {
        var dto = CreateMapper().Map<InternalAccount, InternalAccountDto>(new InternalAccount { Id = 3, Owner = "Ann" });

        Assert.Equal(3, dto.Id);
        Assert.Equal("Ann", dto.Owner);
    }

    [Fact]
    public void Map_ObjectSource_InternalSourceFromProfile_Maps()
    {
        object invoice = new InternalInvoice { Number = 12, Amount = 4.5m };

        var dto = CreateMapper().Map<PublicInvoiceDto>(invoice);

        Assert.Equal(12, dto.Number);
        Assert.Equal(4.5m, dto.Amount);
    }

    [Fact]
    public void Map_InPlace_InternalTypes_UpdatesDestination()
    {
        var destination = new InternalAccountDto();

        CreateMapper().Map(new InternalAccount { Id = 8, Owner = "Bob" }, destination);

        Assert.Equal(8, destination.Id);
        Assert.Equal("Bob", destination.Owner);
    }

    [Fact]
    public void MapCollection_InternalTypes_MapsAllItems()
    {
        var accounts = new[] { new InternalAccount { Id = 1 }, new InternalAccount { Id = 2 } };

        var dtos = CreateMapper().MapCollection<InternalAccount, InternalAccountDto>(accounts).ToList();

        Assert.Equal(new[] { 1, 2 }, dtos.Select(d => d.Id));
    }
}
