using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Extensions.DI;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;

// NativeAOT probe B: IMapper from IronMapper.Extensions.DI. eng/verify-aot.ps1 requires zero trim/AOT warnings and runs
// the executable; every check below must pass (exit code 0).
var services = new ServiceCollection();
services.AddIronMapper(typeof(User).Assembly);

using var provider = services.BuildServiceProvider();
var mapper = provider.GetRequiredService<IMapper>();
var failures = 0;

void Check(bool condition, string what)
{
    if (condition) return;
    Console.WriteLine("FAILED: " + what);
    failures++;
}

var user = new User { Id = 1, Name = "Ann", Orders = new List<Order> { new() { Number = "A-1" }, new() { Number = "A-2" } } };

var dto = mapper.Map<User, UserDto>(user);
Check(dto.Id == 1 && dto.Name == "Ann", "Map<User, UserDto>");
Check(dto.Orders.Count == 2 && dto.Orders[1].Number == "A-2", "nested collection");

Check(mapper.Map<UserDto>(user).Name == "Ann", "Map<UserDto>(object)");

var existing = new UserDto { Id = 99 };
mapper.Map(user, existing);
Check(existing.Id == 1 && existing.Name == "Ann", "in-place Map(source, destination)");

Check(mapper.MapCollection<User, UserDto>(new[] { user, user }).Count() == 2, "MapCollection");
Check(mapper.Map<List<OrderDto>>(user.Orders).Count == 2, "Map<List<OrderDto>>(list)");
Check(mapper.Map<OrderDto[]>(user.Orders.ToArray())[0].Number == "A-1", "Map<OrderDto[]>(array)");
Check(mapper.Map<IEnumerable<OrderDto>>(new HashSet<Order>(user.Orders)).Count() == 2, "Map<IEnumerable<OrderDto>>(set)");
Check(mapper.Map<Product, ProductDto>(new Product { Price = 2.5m }).Price == "2.50", "profile map with converter");

try
{
    mapper.Map<User, UserDto>(new User { Orders = new List<Order> { null! } });
    Check(false, "null element: no exception");
}
catch (ArgumentNullException ex)
{
    Check(ex.ParamName == "source", "null element: ParamName");
}

Console.WriteLine(failures == 0 ? "IMapper probe: OK" : $"IMapper probe: {failures} check(s) failed");
return failures == 0 ? 0 : 1;

[MapTo(typeof(UserDto))]
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Order> Orders { get; set; } = new();
}

public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<OrderDto> Orders { get; set; } = new();
}

[MapTo(typeof(OrderDto))]
public class Order
{
    public string Number { get; set; } = string.Empty;
}

public class OrderDto
{
    public string Number { get; set; } = string.Empty;
}

public class Product
{
    public decimal Price { get; set; }
}

public class ProductDto
{
    public string Price { get; set; } = string.Empty;
}

public class PriceConverter : ITypeConverter<decimal, string>
{
    public string Convert(decimal source) => source.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}

public class ProductProfile : MappingProfile
{
    public ProductProfile()
    {
        CreateMap<Product, ProductDto>().ForMember(d => d.Price, o => o.UseConverter<PriceConverter>());
    }
}
