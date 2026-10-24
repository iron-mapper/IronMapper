using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Nested objects (issue #23): a name-matched property whose types differ is mapped by the mapping of its type pair,
/// through the private <c>MapToXCore</c> after a null check by the nullable annotations (<c>null</c> → <c>null</c> for a
/// nullable destination, <c>InvalidOperationException</c> naming both properties for a non-nullable one, none for a
/// struct). The in-place overload updates an existing nested object through the in-place <c>MapToXCore</c>. Without a
/// mapping for the pair: IM0002 that names the pair and what to add. Up to 2.0.0 such a property failed to compile in
/// the generated file (CS0029), so no build that compiles with 2.0.0 changes.
/// </summary>
public class NestedObjectTests
{
    private static Diagnostic[] Of(string id, IEnumerable<Diagnostic> diagnostics) => diagnostics.Where(d => d.Id == id).ToArray();

    private static string At(Diagnostic diagnostic)
        => diagnostic.Location.SourceTree!.ToString().Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);

    private static void AssertNoErrors(IReadOnlyList<Diagnostic> errors)
        => Assert.True(errors.Count == 0, "Errors in the compilation:\n" + string.Join("\n", errors));

    private static string Generated(IEnumerable<string> sources, string mapping)
        => sources.Single(s => s.Contains($"private static global::App.{mapping} MapTo{mapping}Core("));

    [Fact]
    public void Attribute_NestedObjectWithMapping_MappedThroughCore_InPlaceUpdatesTheExistingObjects()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public int Id { get; set; } public Customer Customer { get; set; } = new(); }
            public class OrderDto { public int Id { get; set; } public CustomerDto Customer { get; set; } = new(); }

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; public Address Address { get; set; } = new(); }
            public class CustomerDto { public string Name { get; set; } = ""; public AddressDto Address { get; set; } = new(); }

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public static class Program
            {
                public static string Run()
                {
                    var order = new Order { Id = 1, Customer = new Customer { Name = "Ann", Address = new Address { City = "Oslo" } } };
                    var dto = order.MapToOrderDto();
                    // Two levels: the existing customer and its existing address are updated, not replaced.
                    var target = new OrderDto();
                    var customer = target.Customer;
                    var address = target.Customer.Address;
                    order.MapToOrderDto(target);
                    // A null destination value gets a new object.
                    var empty = new OrderDto { Customer = null! };
                    order.MapToOrderDto(empty);
                    return $"{dto.Id},{dto.Customer.Name},{dto.Customer.Address.City},"
                        + $"{ReferenceEquals(customer, target.Customer)},{ReferenceEquals(address, target.Customer.Address)},"
                        + $"{target.Customer.Name},{target.Customer.Address.City},{empty.Customer.Name},{empty.Customer.Address.City}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("1,Ann,Oslo,True,True,Ann,Oslo,Ann,Oslo", run);
        var order = Generated(generated, "OrderDto");
        // Non-nullable destination: a null check with the throw helper, then the …Core without a second check.
        Assert.Contains("Customer = MapToCustomerDtoCore(source.Customer ?? NestedObjectIsNull<global::App.Customer>(\"Order.Customer is null, but OrderDto.Customer is not nullable.", order);
        Assert.Contains("""
                        if (source.Customer is null) NestedObjectIsNull<global::App.Customer>("Order.Customer is null, but OrderDto.Customer is not nullable. If it is an EF navigation, check Include; otherwise declare the destination property nullable (null is then mapped to null) or configure the property explicitly.");
                        else if (destination.Customer is null) destination.Customer = MapToCustomerDtoCore(source.Customer);
                        else MapToCustomerDtoCore(source.Customer, destination.Customer);
            """.ReplaceLineEndings(), order.ReplaceLineEndings());
        var customer = Generated(generated, "CustomerDto");
        Assert.Contains("Address = MapToAddressDtoCore(source.Address ?? NestedObjectIsNull<global::App.Address>(", customer);
        Assert.Contains("private static void MapToCustomerDtoCore(global::App.Customer source, global::App.CustomerDto destination)", customer);
        Assert.Contains("else MapToAddressDtoCore(source.Address, destination.Address);", customer);
    }

    [Fact]
    public void PublicMethod_ChecksNullAndCallsCore()
    {
        var source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            """;

        var (generated, _, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var code = Generated(generated, "AddressDto");
        Assert.Contains("""
                    public static global::App.AddressDto MapToAddressDto(this global::App.Address source)
                    {
                        global::System.ArgumentNullException.ThrowIfNull(source, nameof(source));
                        return MapToAddressDtoCore(source);
                    }
            """.ReplaceLineEndings(), code.ReplaceLineEndings());
        Assert.Contains("private static global::App.AddressDto MapToAddressDtoCore(global::App.Address source)", code);
        Assert.Contains("""
                    public static void MapToAddressDto(this global::App.Address source, global::App.AddressDto destination)
                    {
                        global::System.ArgumentNullException.ThrowIfNull(source, nameof(source));
                        global::System.ArgumentNullException.ThrowIfNull(destination, nameof(destination));
                        MapToAddressDtoCore(source, destination);
                    }

                    private static void MapToAddressDtoCore(global::App.Address source, global::App.AddressDto destination)
                    {
                        destination.City = source.City;
                    }
            """.ReplaceLineEndings(), code.ReplaceLineEndings());
        // No nested object checked for null: no throw helper.
        Assert.DoesNotContain(generated, s => s.Contains("NestedObjectIsNull"));
    }

    [Fact]
    public void Profile_NestedObjects_MappedWithoutForMember_IncludingReverseMap()
    {
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Customer { public string Name { get; set; } = ""; public Address Address { get; set; } = new(); }
            public class CustomerDto { public string Name { get; set; } = ""; public AddressDto Address { get; set; } = new(); }
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Address, AddressDto>().ReverseMap();
                    CreateMap<Customer, CustomerDto>().ReverseMap();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Customer { Name = "Ann", Address = new Address { City = "Oslo" } }.MapToCustomerDto();
                    var back = dto.MapToCustomer();
                    return $"{dto.Address.City},{back.Address.City}";
                }
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("Oslo,Oslo", run);
    }

    [Fact]
    public void NullableSourceAndDestination_NullStaysNull()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public Address? Address { get; set; } }
            public class CustomerDto { public AddressDto? Address { get; set; } = new(); }

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public static class Program
            {
                public static string Run()
                {
                    var none = new Customer().MapToCustomerDto();
                    var some = new Customer { Address = new Address { City = "Oslo" } }.MapToCustomerDto();
                    var target = new CustomerDto();
                    new Customer().MapToCustomerDto(target);
                    var existing = new AddressDto();
                    var updated = new CustomerDto { Address = existing };
                    new Customer { Address = new Address { City = "Bergen" } }.MapToCustomerDto(updated);
                    return $"{none.Address is null},{some.Address!.City},{target.Address is null},{ReferenceEquals(existing, updated.Address)},{existing.City}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True,Oslo,True,True,Bergen", run);
        var code = Generated(generated, "CustomerDto");
        Assert.Contains("Address = source.Address is null ? null : MapToAddressDtoCore(source.Address),", code);
        Assert.Contains("if (source.Address is null) destination.Address = null;", code);
        Assert.DoesNotContain(generated, s => s.Contains("NestedObjectIsNull"));
    }

    [Fact]
    public void NonNullableSourceAndNullableDestination_NullMapsToNull()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public Address Address { get; set; } = null!; }
            public class CustomerDto { public AddressDto? Address { get; set; } }

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public static class Program
            {
                // Declared non-nullable but null at run time (an EF navigation without Include): the destination accepts null.
                public static string Run() => $"{new Customer().MapToCustomerDto().Address is null}";
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("True", run);
        Assert.Contains("Address = source.Address is null ? null : MapToAddressDtoCore(source.Address),", Generated(generated, "CustomerDto"));
    }

    [Fact]
    public void NullableSourceAndNonNullableDestination_IM0002_NoNullForgiving()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; public Address? Address { get; set; } }
            public class CustomerDto { public string Name { get; set; } = ""; public AddressDto Address { get; set; } = new(); }

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public class Order { public Address? Address { get; set; } }
            public class OrderDto { public AddressDto Address { get; set; } = new(); }

            public class OrderProfile : MappingProfile
            {
                public OrderProfile() { CreateMap<Order, OrderDto>(); }
            }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var found = Of("IM0002", diagnostics);
        Assert.Equal(2, found.Length);
        var attribute = found.Single(d => At(d) == "Address");
        Assert.Contains("Property 'Address' of 'App.CustomerDto' is not mapped: 'Customer.Address' is 'App.Address?', which can be null, and the destination is 'App.AddressDto', which cannot. Declare the destination property nullable or the source property non-nullable, or mark the destination property [Ignore].", attribute.GetMessage());
        var profile = found.Single(d => At(d).StartsWith("CreateMap", StringComparison.Ordinal));
        Assert.Contains("configure it with ForMember(d => d.Address, o => o.MapFrom(...)) or with o.Ignore().", profile.GetMessage());
        // No code fix offered: adding a mapping would not help.
        Assert.All(found, d => Assert.False(d.Properties.ContainsKey("PairSourceType")));
        Assert.DoesNotContain(generated, s => s.Contains("Address = ") && s.Contains("CustomerDtoCore("));
        Assert.DoesNotContain(generated, s => s.Contains("null!") && s.Contains("Address"));
    }

    [Fact]
    public void SourceWithoutNullableAnnotations()
    {
        // #nullable disable on the source: a non-nullable destination gets the check with the throw helper
        // (InvalidOperationException naming both properties); a nullable destination gets null → null.
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            #nullable disable
            [MapTo(typeof(CustomerDto))]
            public class Customer { public Address Billing { get; set; } public Address Shipping { get; set; } }
            #nullable enable
            public class CustomerDto { public AddressDto Billing { get; set; } = new(); public AddressDto? Shipping { get; set; } }

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public static class Program
            {
                public static string Run()
                {
                    var dto = new Customer { Billing = new Address { City = "Oslo" } }.MapToCustomerDto();
                    try
                    {
                        new Customer().MapToCustomerDto();
                        return "no exception";
                    }
                    catch (System.InvalidOperationException e)
                    {
                        return $"{dto.Billing.City},{dto.Shipping is null},{e.Message}";
                    }
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("Oslo,True,Customer.Billing is null, but CustomerDto.Billing is not nullable. If it is an EF navigation, check Include; otherwise declare the destination property nullable (null is then mapped to null) or configure the property explicitly.", run);
        var code = Generated(generated, "CustomerDto");
        Assert.Contains("Billing = MapToAddressDtoCore(source.Billing ?? NestedObjectIsNull<global::App.Address>(", code);
        Assert.Contains("Shipping = source.Shipping is null ? null : MapToAddressDtoCore(source.Shipping),", code);
    }

    [Fact]
    public void StructAndRecordNestedObjects()
    {
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public struct Point { public int X { get; set; } public int Y { get; set; } }
            public record PointDto { public int X { get; init; } public int Y { get; init; } }
            public class Shape { public Point Origin { get; set; } public Point? Pivot { get; set; } }
            public class ShapeDto { public PointDto Origin { get; set; } = new(); }

            public class ShapeProfile : MappingProfile
            {
                public ShapeProfile() { CreateMap<Point, PointDto>(); CreateMap<Shape, ShapeDto>(); }
            }

            public static class Program
            {
                public static string Run() => new Shape { Origin = new Point { X = 1, Y = 2 } }.MapToShapeDto() is var d ? $"{d.Origin.X},{d.Origin.Y}" : "";
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(Of("IM0002", diagnostics));
        Assert.Equal("1,2", run);
        Assert.Contains("Origin = MapToPointDtoCore(source.Origin),", Generated(generated, "ShapeDto"));
    }

    [Fact]
    public void MappingOfBaseTypeOrInterface_MapsDerivedNestedObject()
    {
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public interface INamed { string Name { get; } }
            public class Address { public string City { get; set; } = ""; }
            public class HomeAddress : Address, INamed { public string Name { get; set; } = "home"; }
            public class AddressDto { public string City { get; set; } = ""; }
            public class NameDto { public string Name { get; set; } = ""; }

            public class Customer { public HomeAddress Home { get; set; } = new(); public HomeAddress Label { get; set; } = new(); }
            public class CustomerDto { public AddressDto Home { get; set; } = new(); public NameDto Label { get; set; } = new(); }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Address, AddressDto>();
                    CreateMap<INamed, NameDto>();
                    CreateMap<Customer, CustomerDto>();
                }
            }

            public static class Program
            {
                public static string Run()
                    => new Customer { Home = new HomeAddress { City = "Oslo" } }.MapToCustomerDto() is var d ? $"{d.Home.City},{d.Label.Name}" : "";
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(Of("IM0002", diagnostics));
        Assert.Equal("Oslo,home", run);
        var code = Generated(generated, "CustomerDto");
        Assert.Contains("Home = MapToAddressDtoCore(source.Home ?? NestedObjectIsNull<global::App.HomeAddress>(", code);
        Assert.Contains("Label = MapToNameDtoCore(source.Label ?? NestedObjectIsNull<global::App.HomeAddress>(", code);
        // The in-place …Core of the base type's mapping updates the existing object.
        Assert.Contains("else MapToAddressDtoCore(source.Home, destination.Home);", code);
    }

    [Fact]
    public void NestedObjectWithoutMapping_IM0002NamesThePair_WithCodeFixProperties()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public string Name { get; set; } = ""; public Address Address { get; set; } = new(); }
            public class CustomerDto { public string Name { get; set; } = ""; public AddressDto Address { get; set; } = new(); }

            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public static class Program
            {
                public static string Run() => new Customer { Name = "Ann" }.MapToCustomerDto() is var d ? $"{d.Name},{d.Address.City.Length}" : "";
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("IM0002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Address", At(diagnostic));
        Assert.Equal("Property 'Address' of 'App.CustomerDto' is not mapped: 'Customer.Address' is 'App.Address' and the destination is 'App.AddressDto', and there is no mapping from 'App.Address' to 'App.AddressDto'. Add [MapTo(typeof(App.AddressDto))] to 'App.Address', [MapFrom(typeof(App.Address))] to 'App.AddressDto', or CreateMap<App.Address, App.AddressDto>() in a profile.",
            diagnostic.GetMessage());
        Assert.Equal("T:App.Address", diagnostic.Properties["PairSourceId"]);
        Assert.Equal("T:App.AddressDto", diagnostic.Properties["PairDestinationId"]);
        Assert.Equal("global::App.Address", diagnostic.Properties["PairSourceType"]);
        Assert.Equal("global::App.AddressDto", diagnostic.Properties["PairDestinationType"]);
        Assert.Equal("Attribute", diagnostic.Properties["MappingOrigin"]);
        Assert.Equal("Address", diagnostic.Properties["PropertyName"]);
        // The rest of the mapping is generated; the property keeps its initial value.
        Assert.Equal("Ann,0", run);
        Assert.DoesNotContain("Address =", Generated(generated, "CustomerDto"));
    }

    [Fact]
    public void ExplicitConfigurationWins_MapFromConverterIgnore()
    {
        var source = """
            using IronMapper.Attributes;
            using IronMapper.Configuration;
            using IronMapper.Interfaces;
            using IronMapper.Generated;

            namespace App;

            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            public class Customer { public Address A { get; set; } = new(); public Address B { get; set; } = new(); public Address C { get; set; } = new(); }
            public class CustomerDto { public AddressDto A { get; set; } = new(); public AddressDto B { get; set; } = new(); public AddressDto C { get; set; } = new() { City = "kept" }; }
            public class Upper : ITypeConverter<Address, AddressDto> { public AddressDto Convert(Address s) => new() { City = s.City.ToUpperInvariant() }; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Address, AddressDto>();
                    CreateMap<Customer, CustomerDto>()
                        .ForMember(d => d.A, o => o.MapFrom(s => new AddressDto { City = "from " + s.A.City }))
                        .ForMember(d => d.B, o => o.UseConverter<Upper>())
                        .ForMember(d => d.C, o => o.Ignore());
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var a = new Address { City = "oslo" };
                    var d = new Customer { A = a, B = a, C = a }.MapToCustomerDto();
                    return $"{d.A.City},{d.B.City},{d.C.City}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("from oslo,OSLO,kept", run);
        Assert.DoesNotContain("MapToAddressDtoCore(source.", Generated(generated, "CustomerDto"));
    }

    [Fact]
    public void QualifiedMethodNames_NestedObjectCallsTheQualifiedCore()
    {
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App
            {
                public class User { public string Name { get; set; } = ""; }
                public class Account { public User Owner { get; set; } = new(); }
                public class AccountDto { public Admin.UserDto Owner { get; set; } = new(); }

                public class AccountProfile : MappingProfile
                {
                    public AccountProfile()
                    {
                        CreateMap<User, Admin.UserDto>();
                        CreateMap<User, Public.UserDto>();
                        CreateMap<Account, AccountDto>();
                    }
                }

                public static class Program
                {
                    public static string Run() => new Account { Owner = new User { Name = "root" } }.MapToAccountDto().Owner.Name;
                }
            }

            namespace App.Admin { public class UserDto { public string Name { get; set; } = ""; } }
            namespace App.Public { public class UserDto { public string Name { get; set; } = ""; } }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(Of("IM0002", diagnostics));
        Assert.Equal("root", run);
        Assert.Contains(generated, s => s.Contains("Owner = MapToAdminUserDtoCore(source.Owner ?? NestedObjectIsNull<global::App.User>("));
        Assert.Contains(generated, s => s.Contains("else MapToAdminUserDtoCore(source.Owner, destination.Owner);"));
    }

    [Fact]
    public void DestinationTypeNamedLikeACoreMethod_NoCollision()
    {
        // MapToItem(source) has MapToItemCore(source), which is also the public name of the mapping to ItemCore.
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Source { public int Id { get; set; } }
            public class Item { public int Id { get; set; } }
            public class ItemCore { public int Id { get; set; } }
            public class Holder { public Source Value { get; set; } = new(); }
            public class HolderDto { public Item Value { get; set; } = new(); }

            public class ItemProfile : MappingProfile
            {
                public ItemProfile() { CreateMap<Source, Item>(); CreateMap<Source, ItemCore>(); CreateMap<Holder, HolderDto>(); }
            }

            public static class Program
            {
                public static string Run()
                {
                    var s = new Source { Id = 7 };
                    return $"{s.MapToItem().Id},{s.MapToItemCore().Id},{new Holder { Value = s }.MapToHolderDto().Value.Id}";
                }
            }
            """;

        var (generated, _, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("7,7,7", run);
        Assert.Contains(generated, s => s.Contains("private static global::App.Item MapToItemCore_Source_Item(global::App.Source source)"));
        Assert.Contains(generated, s => s.Contains("Value = MapToItemCore_Source_Item(source.Value ?? NestedObjectIsNull<global::App.Source>("));
        Assert.Contains(generated, s => s.Contains("private static void MapToItemCore_Source_Item(global::App.Source source, global::App.Item destination)"));
    }

    [Fact]
    public void HooksAndInitOnly_NestedObjectMapped()
    {
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            public class Customer { public Address Home { get; set; } = new(); public Address Work { get; set; } = new(); }
            public class CustomerDto { public AddressDto Home { get; set; } = new(); public AddressDto Work { get; init; } = new(); public string Log { get; set; } = ""; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Address, AddressDto>();
                    CreateMap<Customer, CustomerDto>()
                        .ForMember(d => d.Log, o => o.Ignore())
                        .AfterMap((s, d) => d.Log = "after " + d.Home.City);
                }
            }

            public static class Program
            {
                public static string Run() => new Customer { Home = new Address { City = "Oslo" } }.MapToCustomerDto().Log;
            }
            """;

        var (_, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(Of("IM0002", diagnostics));
        Assert.Equal("after Oslo", run);
    }

    [Fact]
    public void IncludeMembers_NestedObjectOfIncludedMember_IM0002()
    {
        var source = """
            using IronMapper.Configuration;

            namespace App;

            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            public class Info { public Address Address { get; set; } = new(); }
            public class Customer { public Info Info { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile() { CreateMap<Address, AddressDto>(); CreateMap<Customer, CustomerDto>().IncludeMembers(s => s.Info); }
            }
            """;

        var (_, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        var diagnostic = Assert.Single(Of("IM0002", diagnostics));
        Assert.Contains("'Customer.Info.Address' is 'App.Address' and the destination is 'App.AddressDto': IncludeMembers does not map nested objects.", diagnostic.GetMessage());
    }

    [Fact]
    public void NestedObjectMappedInReferencedAssembly_CalledAsExtensionMethod_InPlaceThroughItsInPlaceOverload()
    {
        var library = """
            using IronMapper.Attributes;

            namespace Lib;

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            [MapTo(typeof(PhoneDto), GenerateInPlace = false)]
            public class Phone { public string Number { get; set; } = ""; }
            public class PhoneDto { public string Number { get; set; } = ""; }
            """;
        var app = """
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public Lib.Address Address { get; set; } = new(); public Lib.Address? Other { get; set; } public Lib.Phone Phone { get; set; } = new(); }
            public class CustomerDto { public Lib.AddressDto Address { get; set; } = new(); public Lib.AddressDto? Other { get; set; } public Lib.PhoneDto Phone { get; set; } = new(); }

            public static class Program
            {
                public static string Run()
                {
                    var source = new Customer { Address = new Lib.Address { City = "Oslo" }, Phone = new Lib.Phone { Number = "42" } };
                    var d = source.MapToCustomerDto();
                    // In place: the pair with the public in-place overload in Lib keeps the object; the pair without it gets a new one.
                    var target = new CustomerDto();
                    var address = target.Address;
                    var phone = target.Phone;
                    source.MapToCustomerDto(target);
                    var empty = new CustomerDto { Address = null! };
                    source.MapToCustomerDto(empty);
                    return $"{d.Address.City},{d.Other is null},{ReferenceEquals(address, target.Address)},{address.City},"
                        + $"{ReferenceEquals(phone, target.Phone)},{target.Phone.Number},{empty.Address.City}";
                }
            }
            """;

        var (lib, libDiagnostics) = GeneratorTestHelper.CompileProject("Lib", library);
        Assert.Empty(libDiagnostics);
        var (reference, image) = GeneratorTestHelper.EmitReference(lib);
        var (compilation, diagnostics) = GeneratorTestHelper.CompileProject("App", app, reference);

        Assert.Empty(diagnostics);
        var generated = compilation.SyntaxTrees.Select(t => t.ToString()).Single(s => s.Contains("MapToCustomerDtoCore(global::"));
        Assert.Contains("Address = (source.Address ?? NestedObjectIsNull<global::Lib.Address>(\"Customer.Address is null, but CustomerDto.Address is not nullable.", generated);
        Assert.Contains(")).MapToAddressDto(),", generated);
        Assert.Contains("Other = source.Other is null ? null : source.Other.MapToAddressDto(),", generated);
        // In place: the public in-place overload of Lib's mapping updates an existing object; without it, a new object.
        Assert.Contains("else if (destination.Address is null) destination.Address = source.Address.MapToAddressDto();", generated);
        Assert.Contains("else source.Address.MapToAddressDto(destination.Address);", generated);
        Assert.Contains("destination.Phone = (source.Phone ?? NestedObjectIsNull<global::Lib.Phone>(", generated);
        Assert.Equal("Oslo,True,True,Oslo,False,42,Oslo", RunProgram(compilation, image));
    }

    // ------------------------------------------------------------------
    // null in a non-nullable nested object; the in-place overload
    // ------------------------------------------------------------------

    [Fact]
    public void NonNullableNestedObjectHoldingNull_InvalidOperationException_NamesBothProperties()
    {
        // An EF navigation declared `= null!` and not loaded (no Include): not a NullReferenceException inside the mapping.
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class OrderEntity { public int Id { get; set; } public CustomerEntity Customer { get; set; } = null!; }
            public class CustomerEntity { public string Name { get; set; } = ""; }
            public class OrderDto { public int Id { get; set; } public CustomerDto Customer { get; set; } = new(); }
            public class CustomerDto { public string Name { get; set; } = ""; }

            public class OrderProfile : MappingProfile
            {
                public OrderProfile() { CreateMap<CustomerEntity, CustomerDto>(); CreateMap<OrderEntity, OrderDto>(); }
            }

            public static class Program
            {
                public static string Run()
                {
                    string Catch(System.Action action)
                    {
                        try { action(); return "no exception"; }
                        catch (System.Exception e) { return $"{e.GetType().Name}: {e.Message}"; }
                    }

                    var order = new OrderEntity { Id = 1 };
                    var target = new OrderDto();
                    var existing = target.Customer;
                    var created = Catch(() => order.MapToOrderDto());
                    var inPlace = Catch(() => order.MapToOrderDto(target));
                    return $"{created}|{created == inPlace}|{ReferenceEquals(existing, target.Customer)}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        Assert.Equal("InvalidOperationException: OrderEntity.Customer is null, but OrderDto.Customer is not nullable. If it is an EF "
                     + "navigation, check Include; otherwise declare the destination property nullable (null is then mapped to null) "
                     + "or configure the property explicitly.|True|True", run);
        // Declared once, out of line, so the mapping that checks stays small.
        var declaration = Assert.Single(generated, s => s.Contains("private static T NestedObjectIsNull<T>(string message)"));
        Assert.Contains("""
                    [global::System.Diagnostics.CodeAnalysis.DoesNotReturn]
                    [global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                    private static T NestedObjectIsNull<T>(string message)
                        => throw new global::System.InvalidOperationException(message);
            """.ReplaceLineEndings(), declaration.ReplaceLineEndings());
    }

    [Fact]
    public void InPlace_StructInitOnlyAndConvertUsing_AssignNewObjects()
    {
        // A struct destination (the getter returns a copy), a destination type with an init-only property (the in-place
        // overload skips it, item 2.15) and a ConvertUsing mapping (the converter makes the object) cannot be updated in
        // place: a new object is assigned, as before.
        var source = """
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Size { public int Width { get; set; } }
            public struct SizeDto { public int Width { get; set; } }
            public class Point { public int X { get; set; } }
            public class PointDto { public int X { get; init; } }
            public class Money { public decimal Amount { get; set; } }
            public class MoneyDto { public decimal Cents { get; set; } }
            public class Shape { public Size Size { get; set; } = new(); public Point Origin { get; set; } = new(); public Money Price { get; set; } = new(); }
            public class ShapeDto { public SizeDto Size { get; set; } public PointDto Origin { get; set; } = new(); public MoneyDto Price { get; set; } = new(); }

            public class ShapeProfile : MappingProfile
            {
                public ShapeProfile()
                {
                    CreateMap<Size, SizeDto>();
                    CreateMap<Point, PointDto>();
                    CreateMap<Money, MoneyDto>().ConvertUsing(s => new MoneyDto { Cents = s.Amount * 100 });
                    CreateMap<Shape, ShapeDto>();
                }
            }

            public static class Program
            {
                public static string Run()
                {
                    var target = new ShapeDto();
                    var origin = target.Origin;
                    var price = target.Price;
                    new Shape { Size = new Size { Width = 3 }, Origin = new Point { X = 5 }, Price = new Money { Amount = 2 } }.MapToShapeDto(target);
                    return $"{target.Size.Width},{target.Origin.X},{target.Price.Cents},{ReferenceEquals(origin, target.Origin)},{ReferenceEquals(price, target.Price)}";
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(Of("IM0002", diagnostics));
        Assert.Equal("3,5,200,False,False", run);
        var code = Generated(generated, "ShapeDto");
        Assert.Contains("destination.Size = MapToSizeDtoCore(source.Size ?? NestedObjectIsNull<global::App.Size>(", code);
        Assert.Contains("destination.Origin = MapToPointDtoCore(source.Origin ?? NestedObjectIsNull<global::App.Point>(", code);
        Assert.Contains("destination.Price = MapToMoneyDtoCore(source.Price ?? NestedObjectIsNull<global::App.Money>(", code);
        Assert.DoesNotContain("else MapTo", code);
    }

    [Fact]
    public void InPlaceTurnedOff_PrivateInPlaceCoreStillGenerated_NestedObjectUpdated()
    {
        // GenerateInPlace = false: no public in-place overload; the private MapInto (for IMapper) and the in-place …Core,
        // which the in-place …Core of the outer mapping calls for the existing nested object, are generated.
        var source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(OrderDto), GenerateInPlace = false)]
            public class Order { public Customer Customer { get; set; } = new(); }
            public class OrderDto { public CustomerDto Customer { get; set; } = new(); }

            [MapTo(typeof(CustomerDto), GenerateInPlace = false)]
            public class Customer { public string Name { get; set; } = ""; }
            public class CustomerDto { public string Name { get; set; } = ""; }
            """;

        var (generated, diagnostics, errors) = GeneratorTestHelper.RunGeneratorAndCompile(source);

        AssertNoErrors(errors);
        Assert.Empty(diagnostics);
        var order = Generated(generated, "OrderDto");
        var customer = Generated(generated, "CustomerDto");
        Assert.DoesNotContain("public static void MapTo", order + customer);
        Assert.Contains("private static void MapInto_Order_OrderDto(global::App.Order source, global::App.OrderDto destination)", order);
        Assert.Contains("            MapToOrderDtoCore(source, destination);", order);
        Assert.Contains("else MapToCustomerDtoCore(source.Customer, destination.Customer);", order);
        Assert.Contains("private static void MapToCustomerDtoCore(global::App.Customer source, global::App.CustomerDto destination)", customer);
    }

    // ------------------------------------------------------------------
    // Collections of objects with nested objects
    // ------------------------------------------------------------------

    [Fact]
    public void Collections_ArraysEnumerablesReadOnlyLists_OfObjectsWithNestedObjects()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;
            using IronMapper.Configuration;
            using IronMapper.Generated;

            namespace App;

            public class Product { public string Sku { get; set; } = ""; }
            public class ProductDto { public string Sku { get; set; } = ""; }
            public class Line { public Product Product { get; set; } = new(); }
            public class LineDto { public ProductDto Product { get; set; } = new(); }

            public class Order
            {
                public Line[] Array { get; set; } = System.Array.Empty<Line>();
                public IEnumerable<Line> Sequence { get; set; } = System.Array.Empty<Line>();
                public IReadOnlyList<Line> ReadOnly { get; set; } = System.Array.Empty<Line>();
            }

            public class OrderDto
            {
                public LineDto[] Array { get; set; } = System.Array.Empty<LineDto>();
                public IEnumerable<LineDto> Sequence { get; set; } = System.Array.Empty<LineDto>();
                public IReadOnlyList<LineDto> ReadOnly { get; set; } = System.Array.Empty<LineDto>();
            }

            public class OrderProfile : MappingProfile
            {
                public OrderProfile() { CreateMap<Product, ProductDto>(); CreateMap<Line, LineDto>(); CreateMap<Order, OrderDto>(); }
            }

            public static class Program
            {
                public static string Run()
                {
                    Line L(string sku) => new Line { Product = new Product { Sku = sku } };
                    var order = new Order
                    {
                        Array = new[] { L("a") },
                        Sequence = new[] { "b", "c" }.Select(L),
                        ReadOnly = new List<Line> { L("d") },
                    };
                    var d = order.MapToOrderDto();
                    return string.Join(",", d.Array.Select(x => x.Product.Sku).Concat(d.Sequence.Select(x => x.Product.Sku))
                        .Concat(d.ReadOnly.Select(x => x.Product.Sku)));
                }
            }
            """;

        var (generated, diagnostics, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Empty(Of("IM0002", diagnostics));
        Assert.Equal("a,b,c,d", run);
        // Elements call the public method (ArgumentNullException for a null element, as in 2.0.0); its nested objects
        // are mapped through MapToXCore.
        var order = Generated(generated, "OrderDto");
        Assert.Contains("result[i] = source[i].MapToLineDto();", order);
        Assert.Contains("yield return item.MapToLineDto();", order);
        Assert.Contains("Product = MapToProductDtoCore(source.Product ?? NestedObjectIsNull<global::App.Product>(", Generated(generated, "LineDto"));
    }

    [Fact]
    public void CollectionElementsWithoutAnnotations_KeepThePublicCall()
    {
        // As in 2.0.0: a null element throws ArgumentNullException (here without nullable annotations; with them see
        // CollectionShapeTests.NestedProperty_NullElement_ThrowsArgumentNullException in IronMapper.Tests).
        var source = """
            using System.Collections.Generic;
            using IronMapper.Attributes;
            using IronMapper.Generated;

            namespace App;

            #nullable disable
            [MapTo(typeof(OrderDto))]
            public class Order { public List<Line> Lines { get; set; } }
            public class OrderDto { public List<LineDto> Lines { get; set; } }
            #nullable enable

            [MapTo(typeof(LineDto))]
            public class Line { public string Sku { get; set; } = ""; }
            public class LineDto { public string Sku { get; set; } = ""; }

            public static class Program
            {
                public static string Run()
                {
                    try { new Order { Lines = new List<Line> { null! } }.MapToOrderDto(); return "no exception"; }
                    catch (System.ArgumentNullException) { return "ArgumentNullException"; }
                }
            }
            """;

        var (generated, _, errors, run) = GeneratorTestHelper.RunGeneratorCompileAndRun(source);

        AssertNoErrors(errors);
        Assert.Equal("ArgumentNullException", run);
        Assert.Contains("result.Add(source[i].MapToLineDto());", Generated(generated, "OrderDto"));
    }

    // ------------------------------------------------------------------
    // IDE: the diagnostic follows the mappings of other files
    // ------------------------------------------------------------------

    private const string CustomerWithAddress = """
        using IronMapper.Attributes;

        namespace App;

        [MapTo(typeof(CustomerDto))]
        public class Customer { public Address Address { get; set; } = new(); }
        public class CustomerDto { public AddressDto Address { get; set; } = new(); }
        """;

    private const string AddressTypes = """
        using IronMapper.Attributes;

        namespace App;

        /*MAP*/public class Address { public string City { get; set; } = ""; }
        public class AddressDto { public string City { get; set; } = ""; }
        """;

    [Theory]
    [InlineData("", "[MapTo(typeof(AddressDto))] ", false)]
    [InlineData("[MapTo(typeof(AddressDto))] ", "", true)]
    public void NestedMappingAddedOrRemovedInAnotherFile_SameAsFreshRun(string before, string after, bool reported)
    {
        var first = GeneratorTestHelper.CreateCompilation(new[] { CustomerWithAddress, AddressTypes.Replace("/*MAP*/", before) });
        var tree = first.SyntaxTrees.ElementAt(1);
        var second = first.ReplaceSyntaxTree(tree,
            CSharpSyntaxTree.ParseText(AddressTypes.Replace("/*MAP*/", after), (CSharpParseOptions)tree.Options));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new IronMapperGenerator());
        driver = driver.RunGenerators(first);
        var incremental = driver.RunGenerators(second).GetRunResult();
        var fresh = CSharpGeneratorDriver.Create(new IronMapperGenerator()).RunGenerators(second).GetRunResult();

        Assert.Equal(Describe(fresh.Diagnostics), Describe(incremental.Diagnostics));
        Assert.Equal(Sources(fresh), Sources(incremental));
        Assert.Equal(reported, incremental.Diagnostics.Any(d => d.Id == "IM0002"));
        Assert.Equal(!reported, incremental.GeneratedTrees.Any(t => t.ToString().Contains("Address = MapToAddressDtoCore(source.Address)")));
    }

    private static IEnumerable<string> Describe(IEnumerable<Diagnostic> diagnostics)
        => diagnostics.Select(d => $"{d.Id}: {d.GetMessage()}").OrderBy(s => s, StringComparer.Ordinal);

    private static IEnumerable<string> Sources(GeneratorDriverRunResult result)
        => result.Results.SelectMany(r => r.GeneratedSources)
            .Select(s => $"{s.HintName}\n{s.SourceText}")
            .OrderBy(s => s, StringComparer.Ordinal);

    /// <summary>Emits <paramref name="compilation"/> and runs <c>Program.Run()</c>, with the referenced image loaded alongside.</summary>
    private static string RunProgram(CSharpCompilation compilation, byte[] referenceImage)
    {
        using var stream = new System.IO.MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var context = new System.Runtime.Loader.AssemblyLoadContext($"run-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            context.LoadFromStream(new System.IO.MemoryStream(referenceImage));
            stream.Position = 0;
            var assembly = context.LoadFromStream(stream);
            var program = assembly.GetTypes().Single(t => t.Name == "Program");
            return (string)program.GetMethod("Run")!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
