using System;
using System.Linq;
using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Exceptions;
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests;

// -----------------------------------------------------------------------
// Test fixtures — NOT file-scoped so the source generator can discover them.
// -----------------------------------------------------------------------

public class OrderEntity
{
    public int Id { get; set; }
    public string Product { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class OrderDto
{
    public int Id { get; set; }
    public string Product { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

/// <summary>Registers <see cref="OrderEntity"/> → <see cref="OrderDto"/> via a profile.</summary>
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<OrderEntity, OrderDto>();
    }
}

// Used for the converter test — maps via [MapConverter] attribute on the source.
public class PriceConverter : ITypeConverter<decimal, string>
{
    public string Convert(decimal source) =>
        source.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}

[MapTo(typeof(PricedDto))]
public class PricedEntity
{
    public int Id { get; set; }

    [MapConverter(typeof(PriceConverter))]
    public decimal Amount { get; set; }
}

public class PricedDto
{
    public int Id { get; set; }
    public string Amount { get; set; } = string.Empty;
}

// -----------------------------------------------------------------------
// Tests
// -----------------------------------------------------------------------

/// <summary>
/// End-to-end tests for <see cref="IronMapperServiceCollectionExtensions"/> and
/// <see cref="RuntimeMapper"/>.
/// </summary>
public class DiIntegrationTests
{
    // ------------------------------------------------------------------
    // Registration
    // ------------------------------------------------------------------

    [Fact]
    public void AddIronMapper_RegistersIMapper()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();

        var mapper = sp.GetService<IMapper>();

        Assert.NotNull(mapper);
        Assert.IsType<RuntimeMapper>(mapper);
    }

    [Fact]
    public void AddIronMapper_AutoScansProfiles_RegistersProfileTypesAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();

        // OrderProfile is a concrete MappingProfile in this assembly — it must be registered.
        var profile = sp.GetService<OrderProfile>();

        Assert.NotNull(profile);
    }

    [Fact]
    public void AddIronMapper_ActionOverload_RegistersIMapper()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(opt =>
            opt.AddProfilesFromAssembly(typeof(DiIntegrationTests).Assembly));
        using var sp = services.BuildServiceProvider();

        var mapper = sp.GetService<IMapper>();

        Assert.NotNull(mapper);
    }

    // ------------------------------------------------------------------
    // Mapping via IMapper
    // ------------------------------------------------------------------

    [Fact]
    public void Mapper_MapsCorrectly_ViaIMapper()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMapper>();

        var entity = new OrderEntity { Id = 7, Product = "Widget", Price = 9.99m };
        var dto = mapper.Map<OrderDto>(entity);

        Assert.Equal(7, dto.Id);
        Assert.Equal("Widget", dto.Product);
        Assert.Equal(9.99m, dto.Price);
    }

    [Fact]
    public void Mapper_WithCustomConverter_ViaServiceProvider()
    {
        // PricedEntity has [MapTo(PricedDto)] with [MapConverter(PriceConverter)] on Amount.
        // The generated code calls new PriceConverter().Convert(...).
        // AddConverter registers PriceConverter so it is also resolvable from DI.
        var services = new ServiceCollection();
        services.AddIronMapper(opt =>
        {
            opt.AddProfilesFromAssembly(typeof(DiIntegrationTests).Assembly);
            opt.AddConverter<PriceConverter>();
        });
        using var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMapper>();

        var entity = new PricedEntity { Id = 3, Amount = 12.5m };
        var dto = mapper.Map<PricedDto>(entity);

        Assert.Equal(3, dto.Id);
        Assert.Equal("12.50", dto.Amount);
        // Converter is also resolvable from DI.
        Assert.NotNull(sp.GetService<PriceConverter>());
    }

    [Fact]
    public void Mapper_ThrowsForUnregisteredMapping()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMapper>();

        // int has no registered mapping to OrderDto.
        Assert.Throws<MappingException>(() => mapper.Map<OrderDto>(42));
    }

    // ------------------------------------------------------------------
    // RuntimeMapper — strongly-typed generic overload
    // ------------------------------------------------------------------

    [Fact]
    public void RuntimeMapper_MapTwoGenerics_NullSource_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMapper>();

        Assert.Throws<ArgumentNullException>(() => mapper.Map<OrderEntity, OrderDto>(null!));
    }

    [Fact]
    public void RuntimeMapper_MapTwoGenerics_ValidSource_ReturnsMappedResult()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMapper>();

        var entity = new OrderEntity { Id = 5, Product = "Gadget", Price = 19.99m };
        var dto = mapper.Map<OrderEntity, OrderDto>(entity);

        Assert.Equal(5, dto.Id);
        Assert.Equal("Gadget", dto.Product);
        Assert.Equal(19.99m, dto.Price);
    }

    // ------------------------------------------------------------------
    // RuntimeMapper — in-place (void) overload always throws
    // ------------------------------------------------------------------

    [Fact]
    public void RuntimeMapper_MapInPlace_AlwaysThrowsNotSupportedException()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMapper>();

        var entity = new OrderEntity { Id = 1 };
        var dto    = new OrderDto();

        Assert.Throws<NotSupportedException>(() => mapper.Map<OrderEntity, OrderDto>(entity, dto));
    }

    // ------------------------------------------------------------------
    // RuntimeMapper — collection overload
    // ------------------------------------------------------------------

    [Fact]
    public void RuntimeMapper_MapCollection_MapsAllItems()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMapper>();

        var entities = new[]
        {
            new OrderEntity { Id = 1, Product = "A", Price = 1.0m },
            new OrderEntity { Id = 2, Product = "B", Price = 2.0m },
        };

        var dtos = mapper.MapCollection<OrderEntity, OrderDto>(entities).ToList();

        Assert.Equal(2, dtos.Count);
        Assert.Equal(1, dtos[0].Id);
        Assert.Equal("B", dtos[1].Product);
    }

    [Fact]
    public void RuntimeMapper_MapCollection_NullSource_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(typeof(DiIntegrationTests).Assembly);
        using var sp = services.BuildServiceProvider();
        var mapper = sp.GetRequiredService<IMapper>();

        Assert.Throws<ArgumentNullException>(() =>
            mapper.MapCollection<OrderEntity, OrderDto>(null!).ToList());
    }

    // ------------------------------------------------------------------
    // RuntimeMapper — constructor guard
    // ------------------------------------------------------------------

    [Fact]
    public void RuntimeMapper_NullProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeMapper(null!));
    }

    // ------------------------------------------------------------------
    // IronMapperOptions.AddProfile<T>
    // ------------------------------------------------------------------

    [Fact]
    public void IronMapperOptions_AddProfile_RegistersProfileAssembly()
    {
        var services = new ServiceCollection();
        services.AddIronMapper(opt => opt.AddProfile<OrderProfile>());
        using var sp = services.BuildServiceProvider();

        // OrderProfile lives in this assembly — AddProfile<T> must cause it to be registered.
        var profile = sp.GetService<OrderProfile>();
        Assert.NotNull(profile);
    }

    // ------------------------------------------------------------------
    // AddIronMapper — null configure guard
    // ------------------------------------------------------------------

    [Fact]
    public void AddIronMapper_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddIronMapper((Action<IronMapperOptions>)null!));
    }
}
