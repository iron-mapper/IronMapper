using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;
using Xunit;

namespace IronMapper.Generator.Tests;

/// <summary>
/// Snapshots of the generated code for typical scenarios (Verify.SourceGenerators). A change of the generated code
/// shows up as a diff of <c>Snapshots/*.verified.cs</c> in the PR; such a change is a reason to re-run the benchmarks.
/// Update the snapshots after an intended change: run the tests, review <c>*.received.*</c>, then accept them
/// (see CLAUDE.md). Every scenario must also compile, so a snapshot never records broken code.
/// </summary>
public class GeneratedCodeSnapshotTests
{
    [Fact]
    public Task Attribute() => Verify("""
        using IronMapper.Attributes;

        namespace App;

        [MapTo(typeof(UserDto))]
        public class User
        {
            public int Id { get; set; }
            [MapProperty("FullName")] public string Name { get; set; } = "";
            [Ignore] public string Password { get; set; } = "";
            public string Email { get; set; } = "";
        }

        public class UserDto
        {
            public int Id { get; set; }
            public string FullName { get; set; } = "";
            public string Password { get; set; } = "";
            public string Email { get; set; } = "";
        }
        """);

    [Fact]
    public Task ProfileWithForMember() => Verify("""
        using IronMapper.Configuration;

        namespace App;

        public class Order { public int Id { get; set; } public string Street { get; set; } = ""; public string City { get; set; } = ""; public string Note { get; set; } = ""; }
        public class OrderDto { public int Id { get; set; } public string Address { get; set; } = ""; public string Note { get; set; } = ""; }

        public class OrderProfile : MappingProfile
        {
            public OrderProfile()
            {
                CreateMap<Order, OrderDto>()
                    .ForMember(d => d.Address, o => o.MapFrom(s => Join(s.Street, s.City)))
                    .ForMember(d => d.Note, o => o.Ignore());
            }

            internal static string Join(string a, string b) => a + ", " + b;
        }
        """);

    [Fact]
    public Task Collections() => Verify("""
        using System.Collections.Generic;
        using IronMapper.Attributes;

        namespace App;

        [MapTo(typeof(CommentDto))]
        public class Comment { public string Text { get; set; } = ""; }
        public class CommentDto { public string Text { get; set; } = ""; }

        [MapTo(typeof(PostDto))]
        public class Post
        {
            public List<Comment> Comments { get; set; } = new();
            public Comment[] Pinned { get; set; } = System.Array.Empty<Comment>();
            public IEnumerable<Comment> Archived { get; set; } = System.Array.Empty<Comment>();
            public List<string> Tags { get; set; } = new();
        }

        public class PostDto
        {
            public List<CommentDto> Comments { get; set; } = new();
            public CommentDto[] Pinned { get; set; } = System.Array.Empty<CommentDto>();
            public IReadOnlyList<CommentDto> Archived { get; set; } = System.Array.Empty<CommentDto>();
            public List<string> Tags { get; set; } = new();
        }
        """);

    // How each declared kind of source collection is read (indexer, Count + foreach over the declared type, foreach),
    // an array destination from a counted and from an unknown-size source, and a lazy IEnumerable<T> destination.
    [Fact]
    public Task CollectionShapes() => Verify("""
        using System.Collections.Generic;
        using IronMapper.Attributes;

        namespace App;

        [MapTo(typeof(LineDto))]
        public class Line { public int Id { get; set; } }
        public class LineDto { public int Id { get; set; } }

        [MapTo(typeof(OrderDto))]
        public class Order
        {
            public IList<Line>? Indexed { get; set; }
            public ICollection<Line>? Counted { get; set; }
            public HashSet<Line>? Set { get; set; }
            public IEnumerable<Line>? Unknown { get; set; }
            public IReadOnlyCollection<Line>? Lazy { get; set; }
        }

        public class OrderDto
        {
            public IReadOnlyList<LineDto>? Indexed { get; set; }
            public LineDto[]? Counted { get; set; }
            public List<LineDto?>? Set { get; set; }
            public LineDto[]? Unknown { get; set; }
            public IEnumerable<LineDto>? Lazy { get; set; }
        }
        """);

    [Fact]
    public Task Converters() => Verify("""
        using IronMapper.Attributes;
        using IronMapper.Configuration;
        using IronMapper.Interfaces;

        namespace App;

        public sealed class Upper : ITypeConverter<string, string>
        {
            public string Convert(string source) => source.ToUpperInvariant();
        }

        public sealed class MoneyConverter : ITypeConverter<Price, PriceDto>
        {
            public PriceDto Convert(Price source) => new PriceDto { Text = source.Amount + " " + source.Currency };
        }

        // Has state: created with new on every call (stateless converters are cached).
        public sealed class Sequence : ITypeConverter<int, int>
        {
            private int _next;
            public int Convert(int source) => source + _next++;
        }

        [MapTo(typeof(TagDto))]
        public class Tag
        {
            [MapConverter(typeof(Upper))] public string Label { get; set; } = "";
            [MapConverter(typeof(Sequence))] public int Order { get; set; }
        }
        public class TagDto { public string Label { get; set; } = ""; public int Order { get; set; } }

        public class Item { public string Code { get; set; } = ""; }
        public class ItemDto { public string Code { get; set; } = ""; }

        public class Price { public decimal Amount { get; set; } public string Currency { get; set; } = ""; }
        public class PriceDto { public string Text { get; set; } = ""; }

        public class ConverterProfile : MappingProfile
        {
            public ConverterProfile()
            {
                CreateMap<Item, ItemDto>().ForMember(d => d.Code, o => o.UseConverter<Upper>());
                CreateMap<Price, PriceDto>().ConvertUsing<MoneyConverter>();
            }
        }
        """);

    [Fact]
    public Task HooksAndTransformers() => Verify("""
        using IronMapper.Configuration;

        namespace App;

        public class Account { public string Name { get; set; } = ""; public bool Active { get; set; } }
        public class AccountDto { public string Name { get; set; } = ""; public string Status { get; set; } = ""; }

        public class AccountProfile : MappingProfile
        {
            public AccountProfile()
            {
                CreateMap<Account, AccountDto>()
                    .When(s => s.Active)
                    .BeforeMap((s, d) => d.Status = "new")
                    .AfterMap((s, d) => d.Status = d.Status + "!");
                AddTransformer<string>(v => v.Trim());
            }
        }
        """);

    [Fact]
    public Task InternalTypes() => Verify("""
        using IronMapper.Attributes;
        using IronMapper.Configuration;

        namespace App;

        [MapTo(typeof(LedgerDto))]
        internal class Ledger { public string Code { get; set; } = ""; }
        internal class LedgerDto { public string Code { get; set; } = ""; }

        public class Outer
        {
            internal class Entry { public decimal Amount { get; set; } }
        }
        public class EntryDto { public decimal Amount { get; set; } }

        internal class EntryProfile : MappingProfile
        {
            public EntryProfile() { CreateMap<Outer.Entry, EntryDto>(); }
        }
        """);

    [Fact]
    public Task NameCollision() => Verify("""
        using IronMapper.Attributes;

        namespace App
        {
            [MapTo(typeof(Admin.UserDto))]
            [MapTo(typeof(Public.UserDto))]
            public class User { public string Name { get; set; } = ""; }
        }

        namespace App.Admin
        {
            public class UserDto { public string Name { get; set; } = ""; }
        }

        namespace App.Public
        {
            public class UserDto { public string Name { get; set; } = ""; }
        }
        """);

    [Fact]
    public Task TurnedOffOverloads() => Verify("""
        using IronMapper.Attributes;
        using IronMapper.Configuration;

        namespace App;

        // Only MapToUserDto is public; the in-place body is a private method for IMapper, List/Array are not generated.
        [MapTo(typeof(UserDto), GenerateInPlace = false, GenerateList = false, GenerateArray = false)]
        public class User { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class UserDto { public int Id { get; set; } public string Name { get; set; } = ""; }

        public class Order { public int Id { get; set; } }
        public class OrderDto { public int Id { get; set; } }
        public class OrderProfile : MappingProfile
        {
            public OrderProfile() { CreateMap<Order, OrderDto>().GenerateMethods(list: false); }
        }
        """);

    [Fact]
    public Task IncompatibleProperties() => Verify("""
        using System.Collections.Generic;
        using IronMapper.Attributes;

        namespace App;

        // IM0002: Count (int? -> int), Address (no mapping for the nested object) and Lines (no mapping for the elements)
        // are not generated; Id and Name are.
        [MapTo(typeof(OrderDto))]
        public class Order
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int? Count { get; set; }
            public Address Address { get; set; } = new();
            public List<Line> Lines { get; set; } = new();
        }

        public class OrderDto
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public AddressDto Address { get; set; } = new();
            public List<LineDto> Lines { get; set; } = new();
        }

        public class Address { public string City { get; set; } = ""; }
        public class AddressDto { public string City { get; set; } = ""; }
        public class Line { public string Sku { get; set; } = ""; }
        public class LineDto { public string Sku { get; set; } = ""; }
        """);

    [Fact]
    public Task Nested() => Verify("""
        using System.Collections.Generic;
        using IronMapper.Configuration;

        namespace App;

        // Nested objects mapped by the mappings of their type pairs, as in the Nested benchmark: MapToXCore without a null
        // check for non-nullable properties and elements, a check for the nullable Referrer.
        public class Address { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
        public class AddressDto { public string Street { get; set; } = ""; public string City { get; set; } = ""; }
        public class Customer { public int Id { get; set; } public string Name { get; set; } = ""; public Address Address { get; set; } = new(); }
        public class CustomerDto { public int Id { get; set; } public string Name { get; set; } = ""; public AddressDto Address { get; set; } = new(); }
        public class Product { public int Id { get; set; } public string Sku { get; set; } = ""; }
        public class ProductDto { public int Id { get; set; } public string Sku { get; set; } = ""; }
        public class OrderLine { public Product Product { get; set; } = new(); public int Quantity { get; set; } }
        public class OrderLineDto { public ProductDto Product { get; set; } = new(); public int Quantity { get; set; } }

        public class Order
        {
            public int Id { get; set; }
            public Customer Customer { get; set; } = new();
            public Customer? Referrer { get; set; }
            public List<OrderLine> Lines { get; set; } = new();
        }

        public class OrderDto
        {
            public int Id { get; set; }
            public CustomerDto Customer { get; set; } = new();
            public CustomerDto? Referrer { get; set; }
            public List<OrderLineDto> Lines { get; set; } = new();
        }

        public class ShopProfile : MappingProfile
        {
            public ShopProfile()
            {
                CreateMap<Address, AddressDto>();
                CreateMap<Customer, CustomerDto>();
                CreateMap<Product, ProductDto>();
                CreateMap<OrderLine, OrderLineDto>();
                CreateMap<Order, OrderDto>();
            }
        }
        """);

    private static Task Verify(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { source });
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new IronMapperGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "Generated code does not compile:\n" + string.Join("\n", errors));

        // No compiler warnings inside the generated files (an error with TreatWarningsAsErrors the user cannot fix).
        var generatedWarnings = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Warning && d.Location.SourceTree?.FilePath.EndsWith(".g.cs") == true)
            .ToList();
        Assert.True(generatedWarnings.Count == 0, "Warnings in generated code:\n" + string.Join("\n", generatedWarnings));

        // The version in [GeneratedCode("IronMapper", "<version>")] is the package version: not part of the snapshot.
        return Verifier.Verify(driver).UseDirectory("Snapshots")
            .ScrubLinesWithReplace(line => GeneratedCodeVersion.Replace(line, "GeneratedCode(\"IronMapper\", \"{version}\")"));
    }

    private static readonly System.Text.RegularExpressions.Regex GeneratedCodeVersion =
        new("""GeneratedCode\("IronMapper", "[^"]*"\)""");
}
