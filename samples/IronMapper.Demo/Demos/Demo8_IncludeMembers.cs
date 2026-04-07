using IronMapper.Configuration;
using IronMapper.Generated;

namespace IronMapper.Demo.Demos;

// ── Types for IncludeMembers flattening demo ─────────────────────────────────

public class ContactDetails
{
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

public class ShippingAddress
{
    public string Street  { get; set; } = string.Empty;
    public string City    { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}

public class CustomerOrder
{
    public int             OrderId  { get; set; }
    public ContactDetails  Contact  { get; set; } = new();
    public ShippingAddress Shipping { get; set; } = new();
    public decimal         Total    { get; set; }
}

/// <summary>
/// Flat DTO — properties from nested <see cref="ContactDetails"/> and
/// <see cref="ShippingAddress"/> are flattened here by name.
/// </summary>
public class CustomerOrderDto
{
    public int     OrderId { get; set; }
    public string  Email   { get; set; } = string.Empty;
    public string  Phone   { get; set; } = string.Empty;
    public string  Street  { get; set; } = string.Empty;
    public string  City    { get; set; } = string.Empty;
    public string  Country { get; set; } = string.Empty;
    public decimal Total   { get; set; }
}

public class CustomerOrderProfile : MappingProfile
{
    public CustomerOrderProfile()
    {
        // IncludeMembers flattens Contact and Shipping into CustomerOrderDto.
        // Priority: direct properties (OrderId, Total) > ForMember > IncludeMembers.
        // First-member-wins when both Contact and Shipping share a property name.
        CreateMap<CustomerOrder, CustomerOrderDto>()
            .IncludeMembers(s => s.Contact, s => s.Shipping);
    }
}

// ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Demonstrates <c>IncludeMembers</c>: nested objects flattened into a single flat DTO.
/// </summary>
public static class Demo8_IncludeMembers
{
    public static Task RunAsync()
    {
        PrintHeader("DEMO 8 — IncludeMembers: flatten nested objects");

        var order = new CustomerOrder
        {
            OrderId  = 42,
            Contact  = new ContactDetails  { Email = "alice@example.com", Phone = "+1-555-0100" },
            Shipping = new ShippingAddress { Street = "1 Main St", City = "Springfield", Country = "US" },
            Total    = 199.99m
        };

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("SOURCE  CustomerOrder (nested structure):");
        Console.WriteLine($"  OrderId:          {order.OrderId}");
        Console.WriteLine($"  Contact.Email:    {order.Contact.Email}");
        Console.WriteLine($"  Contact.Phone:    {order.Contact.Phone}");
        Console.WriteLine($"  Shipping.Street:  {order.Shipping.Street}");
        Console.WriteLine($"  Shipping.City:    {order.Shipping.City}");
        Console.WriteLine($"  Shipping.Country: {order.Shipping.Country}");
        Console.WriteLine($"  Total:            {order.Total:C}");

        var dto = order.MapToCustomerOrderDto();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("\nRESULT  CustomerOrderDto (flat):");
        Console.WriteLine($"  OrderId: {dto.OrderId}");
        Console.WriteLine($"  Email:   {dto.Email}");
        Console.WriteLine($"  Phone:   {dto.Phone}");
        Console.WriteLine($"  Street:  {dto.Street}");
        Console.WriteLine($"  City:    {dto.City}");
        Console.WriteLine($"  Country: {dto.Country}");
        Console.WriteLine($"  Total:   {dto.Total:C}");

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine();
        Console.WriteLine("  CreateMap<CustomerOrder, CustomerOrderDto>()");
        Console.WriteLine("      .IncludeMembers(s => s.Contact, s => s.Shipping)");
        Console.WriteLine();
        Console.WriteLine("  Properties matched by name at compile time — no reflection.");
        Console.WriteLine("  Direct props (OrderId, Total) always take priority over nested.");
        Console.ResetColor();

        return Task.CompletedTask;
    }

    private static void PrintHeader(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n" + new string('─', 60));
        Console.WriteLine($" {title}");
        Console.WriteLine(new string('─', 60));
        Console.ResetColor();
    }
}
