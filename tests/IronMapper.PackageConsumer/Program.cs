using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;

var user = new UserEntity { Id = 42, Name = "Alice", Email = "alice@example.com", Secret = "hidden" };
UserDto dto = user.MapToUserDto();

var order = new OrderEntity { Street = "Main St", City = "Springfield" };
OrderDto orderDto = order.MapToOrderDto();

var failures = new List<string>();
if (dto.Id != 42) failures.Add($"Id: expected 42, got {dto.Id}");
if (dto.FullName != "Alice") failures.Add($"FullName: expected 'Alice', got '{dto.FullName}'");
if (dto.Email != "alice@example.com") failures.Add($"Email: expected 'alice@example.com', got '{dto.Email}'");
if (dto.Secret != string.Empty) failures.Add($"Secret: expected to be ignored, got '{dto.Secret}'");
if (orderDto.FullAddress != "Main St, Springfield") failures.Add($"FullAddress: expected 'Main St, Springfield', got '{orderDto.FullAddress}'");

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
