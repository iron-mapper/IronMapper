using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Interfaces;

// Every kind of generated method, called the way an application calls it. A trimmed-away or broken method shows up
// as a wrong value here (or as a crash), so the probe checks the results, not only that the executable starts.
var user = new User { Id = 7, Name = "Alice", Secret = "hidden", Role = "admin" };
var userDto = user.MapToUserDto();

var order = new Order
{
    Street = "Main St",
    City = "Springfield",
    Lines = { new OrderLine { Sku = "A-1", Quantity = 2 }, new OrderLine { Sku = "B-2", Quantity = 3 } },
};
var orderDto = order.MapToOrderDto();

var users = new[] { new User { Id = 1, Name = "a" }, new User { Id = 2, Name = "b" } };
var userList = users.MapToUserDtoList();
var userArray = users.MapToUserDtoArray();

var target = new UserDto { Secret = "kept" };
new User { Id = 9, Name = "In place", Role = "x" }.MapToUserDto(target);

var failures = new List<string>();
void Check(bool ok, string what)
{
    if (!ok)
    {
        failures.Add(what);
    }
}

Check(userDto is { Id: 7, FullName: "Alice", Secret: "", Role: "ADMIN" }, $"[MapTo] + [MapProperty] + [Ignore] + [MapConverter]: {userDto.Id} '{userDto.FullName}' '{userDto.Secret}' '{userDto.Role}'");
Check(orderDto.Address == "Main St, Springfield", $"profile MapFrom: '{orderDto.Address}'");
Check(orderDto.Lines.Count == 2 && orderDto.Lines[0].Sku == "A-1" && orderDto.Lines[1].Quantity == 3, "profile: nested collection property");
Check(userList.Count == 2 && userList[1].Id == 2, "MapToUserDtoList");
Check(userArray.Length == 2 && userArray[0].FullName == "a", "MapToUserDtoArray");
Check(target is { Id: 9, FullName: "In place", Secret: "kept", Role: "X" }, $"in-place: {target.Id} '{target.FullName}' '{target.Secret}'");

if (failures.Count > 0)
{
    foreach (string failure in failures)
    {
        Console.Error.WriteLine($"FAIL {failure}");
    }

    return 1;
}

Console.WriteLine("IronMapper NativeAOT probe passed.");
return 0;

[MapTo(typeof(UserDto))]
public class User
{
    public int Id { get; set; }

    [MapProperty("FullName")]
    public string Name { get; set; } = string.Empty;

    [Ignore]
    public string Secret { get; set; } = string.Empty;

    [MapConverter(typeof(UpperConverter))]
    public string Role { get; set; } = string.Empty;
}

public class UserDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class UpperConverter : ITypeConverter<string, string>
{
    public string Convert(string source) => source.ToUpperInvariant();
}

public class Order
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public List<OrderLine> Lines { get; set; } = new();
}

public class OrderLine
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class OrderDto
{
    public string Address { get; set; } = string.Empty;
    public List<OrderLineDto> Lines { get; set; } = new();
}

public class OrderLineDto
{
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<Order, OrderDto>()
            .ForMember(d => d.Address, o => o.MapFrom(s => s.Street + ", " + s.City));
        CreateMap<OrderLine, OrderLineDto>();
    }
}
