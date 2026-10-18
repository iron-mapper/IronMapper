using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Generated;

namespace InternalLib;

/// <summary>Public entry point; every mapping of this library is internal.</summary>
public static class InternalLibApi
{
    public static string Describe(string code, int amount)
        => new Secret { Code = code }.MapToSecretDto().Code + ":" + new Entry { Amount = amount }.MapToEntryDto().Amount;
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

internal class Entry
{
    public int Amount { get; set; }
}

internal class EntryDto
{
    public int Amount { get; set; }
}

internal class EntryProfile : MappingProfile
{
    public EntryProfile()
    {
        CreateMap<Entry, EntryDto>();
    }
}
