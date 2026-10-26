using Comparison.Mappers.Shared;
using Comparison.Models.Custom;
using Comparison.Models.Flat;
using Comparison.Models.Nested;

namespace Comparison.Mappers.Manual;

/// <summary>Hand-written baseline: plain assignments, collections via pre-sized List + loop.</summary>
public static class ManualMappings
{
    public static UserDto MapFlat(UserEntity src) => new()
    {
        Id = src.Id,
        ExternalId = src.ExternalId,
        UserName = src.UserName,
        Email = src.Email,
        FirstName = src.FirstName,
        LastName = src.LastName,
        Balance = src.Balance,
        Rating = src.Rating,
        IsActive = src.IsActive,
        CreatedAt = src.CreatedAt,
        LastLoginAt = src.LastLoginAt,
        CorrelationId = src.CorrelationId,
        Status = src.Status,
        Age = src.Age,
        CreditLimit = src.CreditLimit,
    };

    public static void MapInPlace(UserEntity src, UserDto dest)
    {
        dest.Id = src.Id;
        dest.ExternalId = src.ExternalId;
        dest.UserName = src.UserName;
        dest.Email = src.Email;
        dest.FirstName = src.FirstName;
        dest.LastName = src.LastName;
        dest.Balance = src.Balance;
        dest.Rating = src.Rating;
        dest.IsActive = src.IsActive;
        dest.CreatedAt = src.CreatedAt;
        dest.LastLoginAt = src.LastLoginAt;
        dest.CorrelationId = src.CorrelationId;
        dest.Status = src.Status;
        dest.Age = src.Age;
        dest.CreditLimit = src.CreditLimit;
    }

    public static OrderDto MapNested(OrderEntity src)
    {
        var lines = new List<OrderLineDto>(src.Lines.Count);
        foreach (var line in src.Lines)
        {
            lines.Add(MapLine(line));
        }

        return new OrderDto
        {
            Id = src.Id,
            Number = src.Number,
            CreatedAt = src.CreatedAt,
            Total = src.Total,
            Customer = MapCustomer(src.Customer),
            Lines = lines,
        };
    }

    public static List<OrderDto> MapCollection(List<OrderEntity> src)
    {
        var result = new List<OrderDto>(src.Count);
        foreach (var order in src)
        {
            result.Add(MapNested(order));
        }

        return result;
    }

    public static EmployeeDto MapCustom(EmployeeEntity src) => new()
    {
        Id = src.Id,
        FirstName = src.FirstName,
        LastName = src.LastName,
        EmailAddress = src.Email,
        Department = src.Department,
        Salary = SalaryFormat.Format(src.Salary),
        HiredAt = src.HiredAt,
        FullName = src.FirstName + " " + src.LastName,
    };

    // One small method per type: nested initializers (src.Address.X, src.Product.X) made MapLine too large
    // for the JIT inliner, so it stayed a call per line (see README, "Baseline Manual").
    private static CustomerDto MapCustomer(CustomerEntity src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        Email = src.Email,
        Address = MapAddress(src.Address),
    };

    private static AddressDto MapAddress(AddressEntity src) => new()
    {
        Street = src.Street,
        City = src.City,
        PostalCode = src.PostalCode,
        Country = src.Country,
    };

    private static OrderLineDto MapLine(OrderLineEntity src) => new()
    {
        Id = src.Id,
        Product = MapProduct(src.Product),
        Quantity = src.Quantity,
        UnitPrice = src.UnitPrice,
    };

    private static ProductDto MapProduct(ProductEntity src) => new()
    {
        Id = src.Id,
        Sku = src.Sku,
        Name = src.Name,
        Price = src.Price,
    };
}
