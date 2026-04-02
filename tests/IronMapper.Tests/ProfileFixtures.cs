using IronMapper.Configuration;

// These types are NOT file-scoped so the source generator can discover them.
namespace IronMapper.Tests.ProfileFixtures;

// -----------------------------------------------------------------------
// PersonEntity / PersonDto — used to test ForMember, Ignore, and When.
// -----------------------------------------------------------------------

public class PersonEntity
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class PersonDto
{
    public int Id { get; set; }
    /// <summary>Populated via ForMember MapFrom: FirstName + " " + LastName.</summary>
    public string FullName { get; set; } = string.Empty;
    /// <summary>Explicitly ignored in PersonProfile.</summary>
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>
/// Profile that exercises ForMember.MapFrom (lambda), ForMember.Ignore, and When.
/// The source generator analyses this constructor at compile time and emits
/// <c>PersonEntity.MapToPersonDto()</c>.
/// </summary>
public class PersonProfile : MappingProfile
{
    public PersonProfile()
    {
        CreateMap<PersonEntity, PersonDto>()
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => src.FirstName + " " + src.LastName))
            .ForMember(dest => dest.Email, opt => opt.Ignore())
            .When(src => src.IsActive);
    }
}

// -----------------------------------------------------------------------
// AddressEntity / AddressDto — used to test ReverseMap.
// -----------------------------------------------------------------------

public class AddressEntity
{
    public int Id { get; set; }
    public string Street { get; set; } = string.Empty;
}

public class AddressDto
{
    public int Id { get; set; }
    public string Street { get; set; } = string.Empty;
}

/// <summary>Profile that registers a bidirectional mapping via ReverseMap().</summary>
public class AddressProfile : MappingProfile
{
    public AddressProfile()
    {
        CreateMap<AddressEntity, AddressDto>().ReverseMap();
    }
}

// -----------------------------------------------------------------------
// SimpleSource / SimpleDest — used to test a plain CreateMap with no config.
// -----------------------------------------------------------------------

public class SimpleSource
{
    public int Value { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class SimpleDest
{
    public int Value { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>Profile that registers a simple mapping with no ForMember customisations.</summary>
public class SimpleProfile : MappingProfile
{
    public SimpleProfile()
    {
        CreateMap<SimpleSource, SimpleDest>();
    }
}
