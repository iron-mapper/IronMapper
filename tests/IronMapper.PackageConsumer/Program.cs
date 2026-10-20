using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;
using ConsumerText;

var user = new UserEntity { Id = 42, Name = "Alice", Email = "alice@example.com", Secret = "hidden" };
UserDto dto = user.MapToUserDto();

var order = new OrderEntity { Street = "Main St", City = "Springfield" };
OrderDto orderDto = order.MapToOrderDto();

// Internal types: the generated methods must be internal (CS0050/CS0051 otherwise).
var ledger = new InternalLedger { Code = "L-7" };
InternalLedgerDto ledgerDto = ledger.MapToInternalLedgerDto();
var entry = new InternalEntry { Amount = 3.5m };
InternalEntryDto entryDto = entry.MapToInternalEntryDto();

// Profile lambdas: a static helper of the profile and an extension method imported only by this file.
var session = new Session { Title = "intro", Grace = TimeSpan.FromMinutes(2) };
SessionDto sessionDto = session.MapToSessionDto();

// Same simple names in two namespaces, with hooks and transformers (1.1.2: CS8785, no generated code at all),
// and a nested converter type (1.1.2: CS0234).
var shopItem = new Shop.Item { Name = "a" }.MapToItemDto();
var stockItem = new Stock.Item { Name = "c" }.MapToItemDto();
var tagDto = new Tag { Label = "t" }.MapToTagDto();

// Referenced libraries that also use IronMapper: InternalLib has only internal maps (its GeneratedMappers is
// internal and invisible here), PublicLib has public maps called from this assembly.
var internalLib = InternalLib.InternalLibApi.Describe("s", 3);
var productDto = new PublicLib.Product { Name = "p" }.MapToProductDto();
var priceDtos = new[] { new PublicLib.Price { Amount = 2.5m } }.MapToPriceDtoArray();
var productTarget = new PublicLib.ProductDto();
new PublicLib.Product { Name = "q" }.MapToProductDto(productTarget);
var publicLibInternal = PublicLib.PublicLibApi.Reveal("r");
var internalLibClass = typeof(InternalLib.InternalLibApi).Assembly.GetType("IronMapper.Generated.GeneratedMappers");
var publicLibClass = typeof(PublicLib.PublicLibApi).Assembly.GetType("IronMapper.Generated.GeneratedMappers");

// Name collision inside PublicLib: one source, two destinations named MemberDto (IM0013, suppressed there). The
// qualified methods are public and called from here; their List/Array call MapToX(x) by the qualified simple name.
var adminMember = new PublicLib.Member { Name = "m" }.MapToAdminMemberDto();
var publicMembers = new[] { new PublicLib.Member { Name = "l" } }.MapToPublicMemberDtoList();
var adminMembers = new[] { new PublicLib.Member { Name = "a" } }.MapToAdminMemberDtoArray();
var publicMemberTarget = new PublicLib.Public.MemberDto();
new PublicLib.Member { Name = "i" }.MapToPublicMemberDto(publicMemberTarget);
var teamDto = new PublicLib.Team { Members = { new PublicLib.Member { Name = "t" } } }.MapToTeamDto();

var failures = new List<string>();
if (dto.Id != 42) failures.Add($"Id: expected 42, got {dto.Id}");
if (dto.FullName != "Alice") failures.Add($"FullName: expected 'Alice', got '{dto.FullName}'");
if (dto.Email != "alice@example.com") failures.Add($"Email: expected 'alice@example.com', got '{dto.Email}'");
if (dto.Secret != string.Empty) failures.Add($"Secret: expected to be ignored, got '{dto.Secret}'");
if (orderDto.FullAddress != "Main St, Springfield") failures.Add($"FullAddress: expected 'Main St, Springfield', got '{orderDto.FullAddress}'");
if (ledgerDto.Code != "L-7") failures.Add($"Internal [MapTo]: expected 'L-7', got '{ledgerDto.Code}'");
if (entryDto.Amount != 3.5m) failures.Add($"Internal profile map: expected 3.5, got {entryDto.Amount}");
if (sessionDto.GraceSeconds != 120) failures.Add($"Profile static helper: expected 120, got {sessionDto.GraceSeconds}");
if (sessionDto.Title != "INTRO!") failures.Add($"Extension method from profile usings: expected 'INTRO!', got '{sessionDto.Title}'");
if (shopItem.Name != "as1") failures.Add($"Shop.Item: expected 'as1', got '{shopItem.Name}'");
if (stockItem.Name != "ct2") failures.Add($"Stock.Item: expected 'ct2', got '{stockItem.Name}'");
if (tagDto.Label != "T") failures.Add($"Nested [MapConverter]: expected 'T', got '{tagDto.Label}'");
if (internalLib != "s:3") failures.Add($"InternalLib: expected 's:3', got '{internalLib}'");
if (productDto.Name != "p") failures.Add($"PublicLib [MapTo] from consumer: expected 'p', got '{productDto.Name}'");
if (priceDtos.Length != 1 || priceDtos[0].Amount != 2.5m) failures.Add("PublicLib profile array map from consumer: expected [2.5]");
if (productTarget.Name != "q") failures.Add($"PublicLib in-place map from consumer: expected 'q', got '{productTarget.Name}'");
if (publicLibInternal != "r") failures.Add($"PublicLib internal map: expected 'r', got '{publicLibInternal}'");
if (internalLibClass is null || internalLibClass.IsPublic) failures.Add("InternalLib: GeneratedMappers must exist and be internal (only internal maps)");
if (publicLibClass is null || !publicLibClass.IsPublic) failures.Add("PublicLib: GeneratedMappers must exist and be public");
if (adminMember is not PublicLib.Admin.MemberDto { Name: "m" }) failures.Add($"PublicLib MapToAdminMemberDto: expected 'm', got '{adminMember.Name}'");
if (publicMembers.Count != 1 || publicMembers[0].Name != "l") failures.Add("PublicLib MapToPublicMemberDtoList: expected ['l']");
if (adminMembers.Length != 1 || adminMembers[0].Name != "a") failures.Add("PublicLib MapToAdminMemberDtoArray: expected ['a']");
if (publicMemberTarget.Name != "i") failures.Add($"PublicLib in-place MapToPublicMemberDto: expected 'i', got '{publicMemberTarget.Name}'");
if (GlobalUsingCalls.Describe() != "gpm") failures.Add($"Calls without an explicit using: expected 'gpm', got '{GlobalUsingCalls.Describe()}'");
if (teamDto.Members.Count != 1 || teamDto.Members[0].Name != "t") failures.Add("PublicLib nested collection of qualified element map: expected ['t']");

if (failures.Count > 0)
{
    foreach (var failure in failures)
        Console.Error.WriteLine($"FAIL {failure}");
    return 1;
}

Console.WriteLine("IronMapper package smoke test passed.");
return 0;

[MapTo(typeof(UserDto))]
public class UserEntity
{
    public int Id { get; set; }

    [MapProperty("FullName")]
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [Ignore]
    public string Secret { get; set; } = string.Empty;
}

public class UserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

public class OrderEntity
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}

public class OrderDto
{
    public string FullAddress { get; set; } = string.Empty;
}

public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<OrderEntity, OrderDto>()
            .ForMember(d => d.FullAddress, o => o.MapFrom(s => s.Street + ", " + s.City));
    }
}

[MapTo(typeof(InternalLedgerDto))]
internal class InternalLedger
{
    public string Code { get; set; } = string.Empty;
}

internal class InternalLedgerDto
{
    public string Code { get; set; } = string.Empty;
}

internal class InternalEntry
{
    public decimal Amount { get; set; }
}

internal class InternalEntryDto
{
    public decimal Amount { get; set; }
}

internal class InternalEntryProfile : MappingProfile
{
    public InternalEntryProfile()
    {
        CreateMap<InternalEntry, InternalEntryDto>();
    }
}

public class Session
{
    public string Title { get; set; } = string.Empty;
    public TimeSpan Grace { get; set; }
}

public class SessionDto
{
    public string Title { get; set; } = string.Empty;
    public int GraceSeconds { get; set; }
}

internal class SessionProfile : MappingProfile
{
    public SessionProfile()
    {
        CreateMap<Session, SessionDto>()
            .ForMember(d => d.GraceSeconds, o => o.MapFrom(s => ToSeconds(s.Grace)))
            .ForMember(d => d.Title, o => o.MapFrom(s => s.Title.Shout()));
    }

    internal static int ToSeconds(TimeSpan value) => (int)value.TotalSeconds;
}

[MapTo(typeof(TagDto))]
public class Tag
{
    [MapConverter(typeof(Converters.Upper))]
    public string Label { get; set; } = string.Empty;
}

public class TagDto
{
    public string Label { get; set; } = string.Empty;
}

public static class Converters
{
    public class Upper : IronMapper.Interfaces.ITypeConverter<string, string>
    {
        public string Convert(string source) => source.ToUpperInvariant();
    }
}

namespace Shop
{
    public class Item
    {
        public string Name { get; set; } = string.Empty;
    }

    public class ItemDto
    {
        public string Name { get; set; } = string.Empty;
    }

    public class ShopProfile : MappingProfile
    {
        public ShopProfile()
        {
            CreateMap<Item, ItemDto>().BeforeMap((s, d) => d.Name = "b").AfterMap((s, d) => d.Name = d.Name + "1");
            AddTransformer<string>(v => v + "s");
        }
    }
}

namespace Stock
{
    public class Item
    {
        public string Name { get; set; } = string.Empty;
    }

    public class ItemDto
    {
        public string Name { get; set; } = string.Empty;
    }

    public class StockProfile : MappingProfile
    {
        public StockProfile()
        {
            CreateMap<Item, ItemDto>().BeforeMap((s, d) => d.Name = "b").AfterMap((s, d) => d.Name = d.Name + "2");
            AddTransformer<string>(v => v + "t");
        }
    }
}

// The same pair is mapped in PublicLib ([MapTo]). The generated MapToTagDtoList/Array of this assembly must call
// this assembly's method; calling tag.MapToTagDto() here would be ambiguous (CS0121), so it is not called.
public class DuplicateTagProfile : MappingProfile
{
    public DuplicateTagProfile()
    {
        CreateMap<PublicLib.Tag, PublicLib.TagDto>();
    }
}

namespace ConsumerText
{
    internal static class TextExtensions
    {
        public static string Shout(this string value) => value.ToUpperInvariant() + "!";
    }
}
