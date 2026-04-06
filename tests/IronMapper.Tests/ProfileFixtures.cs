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

// -----------------------------------------------------------------------
// TrackableEntity / TrackableDto — used to test BeforeMap and AfterMap.
// -----------------------------------------------------------------------

public class TrackableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TrackableDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Set to <see langword="true"/> by BeforeMap hook.</summary>
    public bool WasBeforeMapCalled { get; set; }
    /// <summary>Set to <see cref="System.DateTime.UtcNow"/> by AfterMap hook.</summary>
    public System.DateTime MappedAt { get; set; }
}

/// <summary>
/// Profile that exercises both BeforeMap and AfterMap hooks.
/// The source generator analyses this constructor and emits private helper methods
/// called at the appropriate points within <c>MapToTrackableDto</c>.
/// </summary>
public class TrackableProfile : MappingProfile
{
    public TrackableProfile()
    {
        CreateMap<TrackableEntity, TrackableDto>()
            .BeforeMap((src, dest) => dest.WasBeforeMapCalled = true)
            .AfterMap((src, dest) => dest.MappedAt = System.DateTime.UtcNow);
    }
}

// -----------------------------------------------------------------------
// HookOrderEntity / HookOrderDto — used to verify hook execution order.
// -----------------------------------------------------------------------

public class HookOrderEntity
{
    public int Id { get; set; }
}

public class HookOrderDto
{
    public int Id { get; set; }
    /// <summary>
    /// Captures <c>destination.Id</c> inside BeforeMap.
    /// Expected to be 0 because properties have not been assigned yet.
    /// </summary>
    public int IdAtBeforeMap { get; set; }
    /// <summary>
    /// Captures <c>destination.Id</c> inside AfterMap.
    /// Expected to equal <c>source.Id</c> because properties are fully assigned.
    /// </summary>
    public int IdAtAfterMap { get; set; }
}

/// <summary>Profile that captures <c>destination.Id</c> at both hook points to verify ordering.</summary>
public class HookOrderProfile : MappingProfile
{
    public HookOrderProfile()
    {
        CreateMap<HookOrderEntity, HookOrderDto>()
            .BeforeMap((src, dest) => dest.IdAtBeforeMap = dest.Id)
            .AfterMap((src, dest) => dest.IdAtAfterMap = dest.Id);
    }
}
