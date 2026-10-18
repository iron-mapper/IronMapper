using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;

namespace PublicLib;

/// <summary>Public entry point that uses an internal mapping of this library.</summary>
public static class PublicLibApi
{
    public static string Reveal(string code) => new Secret { Code = code }.MapToSecretDto().Code;
}

[MapTo(typeof(ProductDto))]
public class Product
{
    public string Name { get; set; } = string.Empty;
}

public class ProductDto
{
    public string Name { get; set; } = string.Empty;
}

public class Price
{
    public decimal Amount { get; set; }
}

public class PriceDto
{
    public decimal Amount { get; set; }
}

public class PriceProfile : MappingProfile
{
    public PriceProfile()
    {
        CreateMap<Price, PriceDto>();
    }
}

// The consumer maps this pair again in its own profile (see Program.cs).
[MapTo(typeof(TagDto))]
public class Tag
{
    public string Label { get; set; } = string.Empty;
}

public class TagDto
{
    public string Label { get; set; } = string.Empty;
}

[MapTo(typeof(SecretDto))]
internal class Secret
{
    public string Code { get; set; } = string.Empty;
}

internal class SecretDto
{
    public string Code { get; set; } = string.Empty;
}
