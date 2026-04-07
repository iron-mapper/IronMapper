using System;
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

// -----------------------------------------------------------------------
// TransformerEntity / TransformerDto — used to test AddTransformer.
// -----------------------------------------------------------------------

public class TransformerEntity { public string Name { get; set; } = ""; public decimal Price { get; set; } }
public class TransformerDto    { public string Name { get; set; } = ""; public decimal Price { get; set; } }

/// <summary>Profile that exercises AddTransformer for string trimming and decimal rounding.</summary>
public class TransformerProfile : MappingProfile
{
    public TransformerProfile()
    {
        AddTransformer<string>(v => v.Trim());
        AddTransformer<decimal>(v => Math.Round(v, 2));
        CreateMap<TransformerEntity, TransformerDto>();
    }
}

// -----------------------------------------------------------------------
// TransformerForMemberEntity / TransformerForMemberDto — used to verify
// that transformers do NOT apply to ForMember-configured properties.
// -----------------------------------------------------------------------

public class TransformerForMemberEntity { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }
public class TransformerForMemberDto    { public string Name { get; set; } = ""; public string Tag { get; set; } = ""; }

/// <summary>
/// Profile that registers a string transformer but explicitly configures Tag via ForMember.
/// The transformer should trim Name but leave Tag's custom lambda untouched.
/// </summary>
public class TransformerForMemberProfile : MappingProfile
{
    public TransformerForMemberProfile()
    {
        AddTransformer<string>(v => v.Trim());
        CreateMap<TransformerForMemberEntity, TransformerForMemberDto>()
            .ForMember(dest => dest.Tag, opt => opt.MapFrom(src => src.Tag + "_raw"));
    }
}

// -----------------------------------------------------------------------
// IncludeMembers fixtures
// -----------------------------------------------------------------------

/// <summary>Nested contact info for IncludeMembers tests.</summary>
public class ContactInfo
{
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

/// <summary>Nested shipping address for IncludeMembers tests. Has Phone to test first-member-wins.</summary>
public class ShippingInfo
{
    public string City    { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Phone   { get; set; } = string.Empty;
}

public class OrderEntity
{
    public int          Id       { get; set; }
    public ContactInfo  Customer { get; set; } = new();
    public ShippingInfo Shipping { get; set; } = new();
    public decimal      Total    { get; set; }
}

/// <summary>
/// Flat DTO that receives properties from both nested members.
/// Phone comes from Customer (first member) not Shipping (first-member-wins).
/// Id and Total are direct properties from OrderEntity.
/// </summary>
public class OrderDto
{
    public int     Id      { get; set; }
    public string  Name    { get; set; } = string.Empty;
    public string  Email   { get; set; } = string.Empty;
    public string  Phone   { get; set; } = string.Empty;
    public string  City    { get; set; } = string.Empty;
    public string  Country { get; set; } = string.Empty;
    public decimal Total   { get; set; }
}

/// <summary>
/// Profile that flattens Customer and Shipping into OrderDto.
/// Customer.Phone takes priority over Shipping.Phone (first-member-wins).
/// Id and Total are direct properties on OrderEntity (priority over IncludeMembers).
/// </summary>
public class OrderProfile : MappingProfile
{
    public OrderProfile()
    {
        CreateMap<OrderEntity, OrderDto>()
            .IncludeMembers(s => s.Customer, s => s.Shipping);
    }
}

// -----------------------------------------------------------------------
// IncludeMembers: ForMember override fixture
// -----------------------------------------------------------------------

public class OrderForMemberDto
{
    public string Email { get; set; } = string.Empty;
    public string Name  { get; set; } = string.Empty;
}

/// <summary>
/// Profile where ForMember for Email overrides the IncludeMembers resolution.
/// Name still comes from Customer via IncludeMembers.
/// </summary>
public class OrderForMemberProfile : MappingProfile
{
    public OrderForMemberProfile()
    {
        CreateMap<OrderEntity, OrderForMemberDto>()
            .ForMember(dest => dest.Email, opt => opt.MapFrom(src => "override@example.com"))
            .IncludeMembers(s => s.Customer);
    }
}

// -----------------------------------------------------------------------
// IncludeMembers: null nested member fixture
// -----------------------------------------------------------------------

public class OrderNullDto
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Profile that includes a nullable nested member — must not throw when it is null.</summary>
public class OrderNullProfile : MappingProfile
{
    public OrderNullProfile()
    {
        CreateMap<OrderEntity, OrderNullDto>()
            .IncludeMembers(s => s.Customer);
    }
}

// -----------------------------------------------------------------------
// IncludeMembers: transformer + IncludeMembers fixture
// -----------------------------------------------------------------------

public class OrderTrimDto
{
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// Profile that combines AddTransformer with IncludeMembers.
/// The string transformer must be applied to Name and Email coming from Customer.
/// </summary>
public class OrderTrimProfile : MappingProfile
{
    public OrderTrimProfile()
    {
        AddTransformer<string>(v => v.Trim());
        CreateMap<OrderEntity, OrderTrimDto>()
            .IncludeMembers(s => s.Customer);
    }
}

// -----------------------------------------------------------------------
// IncludeMembers: BeforeMap / AfterMap + IncludeMembers fixture
// -----------------------------------------------------------------------

public class OrderHookDto
{
    public string Name      { get; set; } = string.Empty;
    public bool   WasHooked { get; set; }
}

/// <summary>
/// Profile that uses BeforeMap/AfterMap hooks together with IncludeMembers.
/// The hook sets WasHooked; the Name comes from Customer via IncludeMembers.
/// </summary>
public class OrderHookProfile : MappingProfile
{
    public OrderHookProfile()
    {
        CreateMap<OrderEntity, OrderHookDto>()
            .BeforeMap((src, dest) => dest.WasHooked = true)
            .IncludeMembers(s => s.Customer);
    }
}
