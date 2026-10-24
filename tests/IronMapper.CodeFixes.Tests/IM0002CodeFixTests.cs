using System.Threading.Tasks;
using Xunit;

namespace IronMapper.CodeFixes.Tests;

/// <summary>
/// IM0002 "no mapping for the pair" (a nested object or the elements of a collection): the fix adds the mapping where the
/// mapping with the diagnostic is declared — <c>[MapTo(typeof(D))]</c> on the source type of an attribute mapping,
/// <c>CreateMap&lt;S, D&gt;();</c> in a profile. Markup <c>{|IM0002:...|}</c> is where the generator's diagnostic must point.
/// </summary>
public class IM0002CodeFixTests
{
    [Fact]
    public async Task AttributeMapping_NestedObject_AddsMapToToTheSourceType()
    {
        await new CodeFixTest<IM0002CodeFixProvider>("""
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto {|IM0002:Address|} { get; set; } = new(); }

            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            """, """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            """).RunAsync();
    }

    [Fact]
    public async Task AttributeMapping_CollectionElement_AddsMapToToTheElementType()
    {
        await new CodeFixTest<IM0002CodeFixProvider>("""
            using System.Collections.Generic;
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public List<Line> Lines { get; set; } = new(); }
            public class OrderDto { public List<LineDto> {|IM0002:Lines|} { get; set; } = new(); }

            public class Line { public string Sku { get; set; } = ""; }
            public class LineDto { public string Sku { get; set; } = ""; }
            """, """
            using System.Collections.Generic;
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(OrderDto))]
            public class Order { public List<Line> Lines { get; set; } = new(); }
            public class OrderDto { public List<LineDto> Lines { get; set; } = new(); }

            [MapTo(typeof(LineDto))]
            public class Line { public string Sku { get; set; } = ""; }
            public class LineDto { public string Sku { get; set; } = ""; }
            """).RunAsync();
    }

    [Fact]
    public async Task SourceTypeInAnotherFileWithoutUsing_AddsAttributeAndUsingThere()
    {
        var test = new CodeFixTest<IM0002CodeFixProvider>("""
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public Models.Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto {|IM0002:Address|} { get; set; } = new(); }
            public class AddressDto { public string City { get; set; } = ""; }
            """, """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public Models.Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }
            public class AddressDto { public string City { get; set; } = ""; }
            """);
        test.TestState.Sources.Add(("/0/Address.cs", """
            namespace App.Models;

            public class Address { public string City { get; set; } = ""; }
            """.ReplaceLineEndings()));
        test.FixedState.Sources.Add(("/0/Address.cs", """
            using IronMapper.Attributes;

            namespace App.Models;

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            """.ReplaceLineEndings()));
        await test.RunAsync();
    }

    [Fact]
    public async Task ProfileMapping_AddsCreateMapAfterTheStatement()
    {
        await new CodeFixTest<IM0002CodeFixProvider>("""
            using IronMapper.Configuration;

            namespace App;

            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    {|IM0002:CreateMap<Customer, CustomerDto>()|};
                }
            }
            """, """
            using IronMapper.Configuration;

            namespace App;

            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Customer, CustomerDto>();
                    CreateMap<Address, AddressDto>();
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task ReverseMap_AddsCreateMapOfTheReversePair()
    {
        await new CodeFixTest<IM0002CodeFixProvider>("""
            using IronMapper.Configuration;

            namespace App;

            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Address, AddressDto>();
                    CreateMap<Customer, CustomerDto>().{|IM0002:ReverseMap|}();
                }
            }
            """, """
            using IronMapper.Configuration;

            namespace App;

            public class Customer { public Address Address { get; set; } = new(); }
            public class CustomerDto { public AddressDto Address { get; set; } = new(); }
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }

            public class CustomerProfile : MappingProfile
            {
                public CustomerProfile()
                {
                    CreateMap<Address, AddressDto>();
                    CreateMap<Customer, CustomerDto>().ReverseMap();
                    CreateMap<AddressDto, Address>();
                }
            }
            """).RunAsync();
    }

    [Fact]
    public async Task NullableNestedObjectForNonNullableDestination_NoFix()
    {
        // A mapping exists; the nullable source is the problem, which an added mapping would not solve.
        const string source = """
            #nullable enable
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(CustomerDto))]
            public class Customer { public Address? Address { get; set; } }
            public class CustomerDto { public AddressDto {|IM0002:Address|} { get; set; } = new(); }

            [MapTo(typeof(AddressDto))]
            public class Address { public string City { get; set; } = ""; }
            public class AddressDto { public string City { get; set; } = ""; }
            """;
        await new CodeFixTest<IM0002CodeFixProvider>(source, source).RunAsync();
    }

    [Fact]
    public async Task IncompatibleTypes_NoFix()
    {
        const string source = """
            using IronMapper.Attributes;

            namespace App;

            [MapTo(typeof(Dest))]
            public class Source { public long Size { get; set; } }
            public class Dest { public int {|IM0002:Size|} { get; set; } }
            """;
        await new CodeFixTest<IM0002CodeFixProvider>(source, source).RunAsync();
    }
}
