using System.Collections.Generic;
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

    [Fact]
    public Task Constructors() => Verify("""
        using System.Collections.Generic;
        using IronMapper.Attributes;
        using IronMapper.Configuration;

        namespace App;

        [MapTo(typeof(ChangeDto))]
        public class Change
        {
            public System.Guid Id { get; set; }
            public string NewAddress { get; set; } = "";
            public System.DateTimeOffset RequestedAt { get; set; }
            public System.Guid[] MovedIds { get; set; } = System.Array.Empty<System.Guid>();
            public Owner Owner { get; set; } = new();
            public string Note { get; set; } = "";
        }

        public record ChangeDto(System.Guid Id, string? NewAddress, System.DateTimeOffset? RequestedAt, IReadOnlyList<System.Guid> MovedIds,
            OwnerDto Owner, string Source = "import")
        {
            public string Note { get; set; } = "";
        }

        [MapTo(typeof(OwnerDto))]
        public class Owner { public string Name { get; set; } = ""; }
        public record OwnerDto(string Name);

        public class UserRow { public System.Guid Id { get; set; } public System.DateTimeOffset? BlockedAt { get; set; } }
        public record Account(System.Guid Id, bool Blocked, string Theme = "system");

        public class AccountProfile : MappingProfile
        {
            public AccountProfile()
            {
                CreateMap<UserRow, Account>().ForMember(d => d.Blocked, o => o.MapFrom(s => s.BlockedAt != null));
            }
        }
        """);

    [Fact]
    public Task Factories() => Verify("""
        using System.Collections.Generic;
        using IronMapper.Configuration;

        namespace App;

        public readonly record struct MemberId
        {
            private MemberId(System.Guid value) => Value = value;
            public System.Guid Value { get; }
            public static MemberId From(System.Guid value) => new(value);
        }

        public class TeamRow
        {
            public System.Guid OwnerId { get; set; }
            public string Name { get; set; } = "";
            public System.Guid[] MemberIds { get; set; } = System.Array.Empty<System.Guid>();
            public string? Motto { get; set; }
            public SettingsRow Settings { get; set; } = new();
        }

        public class SettingsRow { public int PageSize { get; set; } public string Locale { get; set; } = ""; }

        public sealed class Settings
        {
            private Settings(int pageSize, string locale) { PageSize = pageSize; Locale = locale; }
            public int PageSize { get; }
            public string Locale { get; }
            public static Settings Create(int pageSize, string locale) => new(pageSize, locale);
        }

        public sealed class Team
        {
            private Team(MemberId owner, string name, IReadOnlyList<System.Guid> memberIds, Settings settings)
            {
                Owner = owner; Name = name; MemberIds = memberIds; Settings = settings;
            }

            public MemberId Owner { get; }
            public string Name { get; }
            public IReadOnlyList<System.Guid> MemberIds { get; }
            public Settings Settings { get; }
            public string? Motto { get; set; }

            public static Team Restore(MemberId owner, string name, IReadOnlyList<System.Guid> memberIds, Settings settings)
                => new(owner, name, memberIds, settings);
        }

        public class PersonRow { public string First { get; set; } = ""; public string Last { get; set; } = ""; public int Age { get; set; } }

        public class Person
        {
            public Person(string fullName) { FullName = fullName; }
            public Person(string fullName, int age) { FullName = fullName; Age = age; }
            public string FullName { get; }
            public int Age { get; }
        }

        public class TeamProfile : MappingProfile
        {
            public TeamProfile()
            {
                CreateMap<SettingsRow, Settings>().ConstructWith(Settings.Create);
                CreateMap<TeamRow, Team>()
                    .ConstructWith(Team.Restore)
                    .ForCtorParam("owner", o => o.MapFrom(s => MemberId.From(s.OwnerId)));
                CreateMap<PersonRow, Person>()
                    .ConstructWith((string fullName, int age) => new Person(fullName, age))
                    .ForCtorParam("fullName", o => o.MapFrom(s => s.First + " " + s.Last));
            }
        }
        """);

    [Fact]
    public Task PairConverters() => Verify("""
        using System.Collections.Generic;
        using IronMapper.Configuration;

        namespace App;

        public readonly record struct MemberId
        {
            private MemberId(System.Guid value) => Value = value;
            public System.Guid Value { get; }
            public static MemberId From(System.Guid value) => new(value);
        }

        public class TeamRow
        {
            public System.Guid Owner { get; set; }
            public System.Guid? Deputy { get; set; }
            public System.Guid[] Members { get; set; } = System.Array.Empty<System.Guid>();
        }

        public record Team(MemberId Owner, MemberId? Deputy, IReadOnlyList<MemberId> Members);

        public class TeamProfile : MappingProfile
        {
            public TeamProfile()
            {
                CreateMap<System.Guid, MemberId>().ConvertUsing(MemberId.From);
                CreateMap<MemberId, System.Guid>().ConvertUsing(v => v.Value);
                CreateMap<TeamRow, Team>();
            }
        }
        """);

    [Fact]
    public Task NullableNarrowing() => Verify("""
        using System.Collections.Generic;
        using IronMapper.Configuration;

        namespace App;

        // IM0023: Email (string? → string), Tags (List<string?> → List<string>, assigned as is), Lines (nullable elements for
        // the element mapping), the parameter of the record; the values are marked with ! in the generated code.
        public class Line { public string Sku { get; set; } = ""; }
        public class LineDto { public string Sku { get; set; } = ""; }
        public class ContactRow
        {
            public string? Email { get; set; }
            public string? Phone { get; set; }
            public List<string?> Tags { get; set; } = new();
            public List<Line?> Lines { get; set; } = new();
        }
        public class Contact
        {
            public string Email { get; set; } = "";
            public string? Phone { get; set; }
            public List<string> Tags { get; set; } = new();
            public List<LineDto> Lines { get; set; } = new();
        }
        public record ContactCard(string Email, string? Phone);

        public class ContactProfile : MappingProfile
        {
            public ContactProfile()
            {
                CreateMap<Line, LineDto>();
                CreateMap<ContactRow, Contact>();
                CreateMap<ContactRow, ContactCard>();
            }
        }
        """);

    // The shape of a mapping of a real project (closed: only the shape, neutral names), measured when 2.0.0 was replaced by
    // 2.1.0-alpha.1: eight properties, one of them the .Value of a value object, the in-place overload, List and Array
    // turned off, used as a nested object nowhere. The generated file must be the one of 2.0.0 byte for byte (checked
    // against the IronMapper 2.0.0 package when this snapshot was accepted): item 7 of the 2.1.0 PR order.
    [Fact]
    public Task NotNestedMapping_SameCodeAs200() => Verify("""
        using System;
        using IronMapper.Configuration;

        namespace App.Requests
        {
            public readonly record struct OwnerId(Guid Value);

            internal sealed class ClosureRequest
            {
                public Guid Id { get; init; }
                public OwnerId OwnerId { get; init; }
                public string EmailHash { get; init; } = "";
                public string? NotifyEmail { get; init; }
                public string Locale { get; init; } = "";
                public DateTimeOffset RequestedAt { get; init; }
                public string? Ip { get; init; }
                public string? UserAgent { get; init; }
            }
        }

        namespace App.Storage
        {
            internal sealed class ClosureRequestRecord
            {
                public Guid Id { get; set; }
                public Guid OwnerId { get; set; }
                public string EmailHash { get; set; } = "";
                public string? NotifyEmail { get; set; }
                public string Locale { get; set; } = "";
                public DateTimeOffset RequestedAt { get; set; }
                public string? Ip { get; set; }
                public string? UserAgent { get; set; }
            }

            internal sealed class StorageProfile : MappingProfile
            {
                public StorageProfile()
                {
                    CreateMap<App.Requests.ClosureRequest, ClosureRequestRecord>()
                        .ForMember(d => d.OwnerId, o => o.MapFrom(s => s.OwnerId.Value))
                        .GenerateMethods(inPlace: false, list: false, array: false);
                }
            }
        }
        """);

    // IronMapperRegistration=false (item 2.16 of the plan): no GeneratedMappers.Registration.g.cs, no private MapInto (the
    // in-place overload of Order is turned off, so it has no in-place code at all, and CustomerDto, nested in it, no in-place
    // …Core), no throwing stub for Badge (created by its constructor) and Point (a value type, [Obsolete] in-place overload).
    [Fact]
    public Task RegistrationTurnedOff() => Verify("""
        using IronMapper.Attributes;

        namespace App;

        [MapTo(typeof(OrderDto), GenerateInPlace = false, GenerateList = false, GenerateArray = false)]
        public class Order { public int Id { get; set; } public Customer Customer { get; set; } = new(); }
        public class OrderDto { public int Id { get; set; } public CustomerDto Customer { get; set; } = new(); }

        [MapTo(typeof(CustomerDto), GenerateList = false, GenerateArray = false)]
        public class Customer { public string Name { get; set; } = ""; }
        public class CustomerDto { public string Name { get; set; } = ""; }

        [MapTo(typeof(Badge), GenerateList = false, GenerateArray = false)]
        public class BadgeRow { public string Name { get; set; } = ""; }
        public record Badge(string Name);

        [MapTo(typeof(PointDto), GenerateList = false, GenerateArray = false)]
        public class Point { public int X { get; set; } }
        public struct PointDto { public int X { get; set; } }
        """, new Dictionary<string, string> { ["IronMapperRegistration"] = "false" });

    // Profile configuration that 2.0.0 dropped (PR 8 of 2.1.0): MapFrom with a static method group is called with the
    // source (#59; 2.0.0 generated Title = source.Title and Slug = source.Slug, CS1061), and so are When and AfterMap (2.0.0:
    // no condition, no hook); [Ignore] on the destination Secret and the source Draft is applied in the new object, in place
    // and in the registration (#61; 2.0.0 copied both).
    [Fact]
    public Task SilentlyIgnoredConfig() => Verify("""
        using IronMapper.Attributes;
        using IronMapper.Configuration;

        namespace App;

        public class ArticleRow
        {
            public bool Published { get; set; }
            public string Headline { get; set; } = "";
            public string Title { get; set; } = "";
            public string Secret { get; set; } = "";
            [Ignore] public string Draft { get; set; } = "";
        }
        public class ArticleCard
        {
            public string Title { get; set; } = "";
            public string Slug { get; set; } = "";
            [Ignore] public string Secret { get; set; } = "";
            public string Draft { get; set; } = "";
            public string Stamp { get; set; } = "";
        }

        public static class ArticleFormat
        {
            public static string Title(ArticleRow row) => row.Headline.ToUpperInvariant();
            public static string Slug(ArticleRow row) => row.Headline.Replace(' ', '-');
            public static bool IsPublished(ArticleRow row) => row.Published;
            public static void Stamp(ArticleRow row, ArticleCard card) => card.Stamp = card.Slug;
        }

        public class ArticleProfile : MappingProfile
        {
            public ArticleProfile()
            {
                CreateMap<ArticleRow, ArticleCard>()
                    .ForMember(d => d.Title, o => o.MapFrom(ArticleFormat.Title))
                    .ForMember(d => d.Slug, o => o.MapFrom(ArticleFormat.Slug))
                    .When(ArticleFormat.IsPublished)
                    .AfterMap(ArticleFormat.Stamp)
                    .GenerateMethods(list: false, array: false);
            }
        }
        """);

    private static Task Verify(string source, IReadOnlyDictionary<string, string>? buildProperties = null)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(new[] { source });
        GeneratorDriver driver = buildProperties is null
            ? CSharpGeneratorDriver.Create(new IronMapperGenerator())
            : CSharpGeneratorDriver.Create(new[] { new IronMapperGenerator().AsSourceGenerator() },
                optionsProvider: TestOptionsProvider.FromBuildProperties(buildProperties));
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
