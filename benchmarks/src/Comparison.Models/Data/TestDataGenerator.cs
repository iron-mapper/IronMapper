using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;

namespace Comparison.Models.Data;

/// <summary>
/// Deterministic test data for every scenario. The same seed always yields the same objects,
/// so benchmark runs and test runs on any machine work on identical input.
/// No reference property or collection is ever <see langword="null"/>.
/// </summary>
public sealed class TestDataGenerator
{
    public const int DefaultSeed = 42;
    public const int MinOrderLines = 5;
    public const int MaxOrderLines = 20;

    private static readonly DateTime BaseDate = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly string[] FirstNames = ["Alice", "Bob", "Carol", "Dmitry", "Elena", "Frank", "Grace", "Hiro", "Irina", "John"];
    private static readonly string[] LastNames = ["Smith", "Ivanov", "Garcia", "Tanaka", "Muller", "Kowalski", "Rossi", "Novak", "Silva", "Brown"];
    private static readonly string[] Cities = ["Berlin", "Lisbon", "Tokyo", "Warsaw", "Prague", "Rome", "Madrid", "Oslo"];
    private static readonly string[] Countries = ["DE", "PT", "JP", "PL", "CZ", "IT", "ES", "NO"];
    private static readonly string[] Streets = ["Main St", "Oak Ave", "Pine Rd", "Elm St", "Lake Dr", "Hill Rd"];
    private static readonly string[] Departments = ["Engineering", "Sales", "Finance", "Support", "Marketing"];
    private static readonly string[] ProductNames = ["Keyboard", "Mouse", "Monitor", "Laptop", "Headset", "Webcam", "Dock", "Cable"];

    private readonly Random _random;
    private int _nextId = 1;

    public TestDataGenerator(int seed = DefaultSeed) => _random = new Random(seed);

    public UserEntity CreateUser()
    {
        int id = _nextId++;
        string firstName = Pick(FirstNames);
        string lastName = Pick(LastNames);
        return new UserEntity
        {
            Id = id,
            ExternalId = 10_000_000_000L + _random.Next(0, int.MaxValue),
            UserName = $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}{id}",
            Email = $"{firstName.ToLowerInvariant()}{id}@example.com",
            FirstName = firstName,
            LastName = lastName,
            Balance = NextMoney(0, 100_000),
            Rating = Math.Round(_random.NextDouble() * 5, 3),
            IsActive = _random.Next(2) == 1,
            CreatedAt = NextDate(),
            LastLoginAt = new DateTimeOffset(NextDate(), TimeSpan.Zero),
            CorrelationId = NextGuid(),
            Status = (UserStatus)_random.Next(4),
            // Nullable value types: roughly every third value is null, deterministically.
            Age = _random.Next(3) == 0 ? null : _random.Next(18, 80),
            CreditLimit = _random.Next(3) == 0 ? null : NextMoney(1_000, 50_000),
        };
    }

    public OrderEntity CreateOrder()
    {
        int id = _nextId++;
        int lineCount = _random.Next(MinOrderLines, MaxOrderLines + 1);
        var lines = new List<OrderLineEntity>(lineCount);
        for (int i = 0; i < lineCount; i++)
        {
            lines.Add(CreateOrderLine());
        }

        decimal total = 0m;
        foreach (var line in lines)
        {
            total += line.UnitPrice * line.Quantity;
        }

        return new OrderEntity
        {
            Id = id,
            Number = $"ORD-{id:D8}",
            CreatedAt = NextDate(),
            Total = total,
            Customer = CreateCustomer(),
            Lines = lines,
        };
    }

    public List<OrderEntity> CreateOrders(int count)
    {
        var orders = new List<OrderEntity>(count);
        for (int i = 0; i < count; i++)
        {
            orders.Add(CreateOrder());
        }

        return orders;
    }

    public EmployeeEntity CreateEmployee()
    {
        int id = _nextId++;
        string firstName = Pick(FirstNames);
        string lastName = Pick(LastNames);
        return new EmployeeEntity
        {
            Id = id,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{firstName.ToLowerInvariant()}.{lastName.ToLowerInvariant()}@corp.example.com",
            Department = Pick(Departments),
            // Fractional part with up to 4 digits, so the "F2" conversion actually rounds.
            Salary = _random.Next(30_000, 250_000) + _random.Next(0, 10_000) / 10_000m,
            HiredAt = NextDate(),
            Notes = $"Internal note #{id}",
        };
    }

    private CustomerEntity CreateCustomer()
    {
        int id = _nextId++;
        string firstName = Pick(FirstNames);
        string lastName = Pick(LastNames);
        int cityIndex = _random.Next(Cities.Length);
        return new CustomerEntity
        {
            Id = id,
            Name = $"{firstName} {lastName}",
            Email = $"customer{id}@example.com",
            Address = new AddressEntity
            {
                Street = $"{_random.Next(1, 300)} {Pick(Streets)}",
                City = Cities[cityIndex],
                PostalCode = _random.Next(10_000, 99_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Country = Countries[cityIndex],
            },
        };
    }

    private OrderLineEntity CreateOrderLine()
    {
        int productId = _random.Next(1, 1_000);
        decimal price = NextMoney(1, 2_000);
        return new OrderLineEntity
        {
            Id = _nextId++,
            Product = new ProductEntity
            {
                Id = productId,
                Sku = $"SKU-{productId:D5}",
                Name = Pick(ProductNames),
                Price = price,
            },
            Quantity = _random.Next(1, 10),
            UnitPrice = price,
        };
    }

    private T Pick<T>(T[] items) => items[_random.Next(items.Length)];

    private decimal NextMoney(int min, int max) => _random.Next(min * 100, max * 100) / 100m;

    private DateTime NextDate() => BaseDate.AddSeconds(_random.Next(0, 3 * 365 * 24 * 3600));

    private Guid NextGuid()
    {
        byte[] bytes = new byte[16];
        _random.NextBytes(bytes);
        return new Guid(bytes);
    }
}
