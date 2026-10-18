using System.Globalization;
using Comparison.Models.Nested;

namespace Comparison.Models.Data;

/// <summary>
/// Deterministic checksum (64-bit FNV-1a) of an order. The entity and the DTO overloads hash the same fields in the
/// same order, so a correct mapping prints the same value as its source. Used by the startup probes to prove that the
/// mapping really ran (JIT and NativeAOT builds must print the same value). <see cref="HashCode"/> is not used: it is
/// randomized per process.
/// </summary>
public static class OrderChecksum
{
    private const ulong OffsetBasis = 14695981039346656037;
    private const ulong Prime = 1099511628211;

    public static ulong Of(OrderEntity order)
    {
        var hash = OffsetBasis;
        hash = Add(hash, order.Id, order.Number, order.CreatedAt.Ticks, order.Total);
        hash = Add(hash, order.Customer.Id, order.Customer.Name, order.Customer.Email);
        var a = order.Customer.Address;
        hash = Add(hash, a.Street, a.City, a.PostalCode, a.Country);
        foreach (var line in order.Lines)
        {
            hash = Add(hash, line.Id, line.Quantity, line.UnitPrice);
            hash = Add(hash, line.Product.Id, line.Product.Sku, line.Product.Name, line.Product.Price);
        }
        return hash;
    }

    public static ulong Of(OrderDto order)
    {
        var hash = OffsetBasis;
        hash = Add(hash, order.Id, order.Number, order.CreatedAt.Ticks, order.Total);
        hash = Add(hash, order.Customer.Id, order.Customer.Name, order.Customer.Email);
        var a = order.Customer.Address;
        hash = Add(hash, a.Street, a.City, a.PostalCode, a.Country);
        foreach (var line in order.Lines)
        {
            hash = Add(hash, line.Id, line.Quantity, line.UnitPrice);
            hash = Add(hash, line.Product.Id, line.Product.Sku, line.Product.Name, line.Product.Price);
        }
        return hash;
    }

    private static ulong Add(ulong hash, params object[] values)
    {
        foreach (var value in values)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (var c in text)
                hash = (hash ^ c) * Prime;
            hash = (hash ^ '|') * Prime;
        }
        return hash;
    }
}
