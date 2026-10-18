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

namespace ConsumerText
{
    internal static class TextExtensions
    {
        public static string Shout(this string value) => value.ToUpperInvariant() + "!";
    }
}
