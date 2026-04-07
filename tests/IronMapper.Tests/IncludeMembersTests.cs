using IronMapper.Generated;
using IronMapper.Tests.ProfileFixtures;
using Xunit;

namespace IronMapper.Tests;

/// <summary>
/// End-to-end tests for the IncludeMembers flattening feature.
/// Fixtures and profiles are defined in ProfileFixtures.cs.
/// </summary>
public class IncludeMembersTests
{
    // ------------------------------------------------------------------
    // Basic flattening
    // ------------------------------------------------------------------

    [Fact]
    public void IncludeMembers_SingleNestedMember_FlattenedPropertiesMapped()
    {
        var entity = new OrderEntity
        {
            Customer = new ContactInfo { Name = "Alice", Email = "alice@example.com" }
        };

        var dto = entity.MapToOrderNullDto(); // uses OrderNullProfile (Customer only)

        Assert.Equal("Alice", dto.Name);
    }

    [Fact]
    public void IncludeMembers_MultipleNestedMembers_AllPropertiesMapped()
    {
        var entity = new OrderEntity
        {
            Id       = 7,
            Customer = new ContactInfo { Name = "Bob", Email = "bob@example.com", Phone = "111" },
            Shipping = new ShippingInfo { City = "Berlin", Country = "DE", Phone = "999" },
            Total    = 42.50m
        };

        var dto = entity.MapToOrderDto();

        Assert.Equal(7,                  dto.Id);
        Assert.Equal("Bob",              dto.Name);
        Assert.Equal("bob@example.com",  dto.Email);
        Assert.Equal("Berlin",           dto.City);
        Assert.Equal("DE",               dto.Country);
        Assert.Equal(42.50m,             dto.Total);
    }

    // ------------------------------------------------------------------
    // Priority rules
    // ------------------------------------------------------------------

    [Fact]
    public void IncludeMembers_FirstMemberWins_OnDuplicatePropertyName()
    {
        var entity = new OrderEntity
        {
            Customer = new ContactInfo { Phone = "from-customer" },
            Shipping = new ShippingInfo { Phone = "from-shipping" }
        };

        var dto = entity.MapToOrderDto();

        // Customer is listed first → Customer.Phone wins
        Assert.Equal("from-customer", dto.Phone);
    }

    [Fact]
    public void IncludeMembers_DirectSourcePropertyWins_OverIncludedMember()
    {
        var entity = new OrderEntity
        {
            Id       = 99,
            Customer = new ContactInfo { Name = "Carol" },
            Total    = 100m
        };

        var dto = entity.MapToOrderDto();

        // Id and Total are direct props on OrderEntity → resolved before IncludeMembers
        Assert.Equal(99,    dto.Id);
        Assert.Equal(100m,  dto.Total);
    }

    [Fact]
    public void IncludeMembers_ForMemberWins_OverIncludedMember()
    {
        var entity = new OrderEntity
        {
            Customer = new ContactInfo { Email = "real@email.com", Name = "Dave" }
        };

        // OrderForMemberProfile: ForMember(d => d.Email, ...) overrides IncludeMembers(s => s.Customer)
        var dto = entity.MapToOrderForMemberDto();

        Assert.Equal("override@example.com", dto.Email);
        Assert.Equal("Dave",                 dto.Name); // Name still comes from IncludeMembers
    }

    // ------------------------------------------------------------------
    // Null safety
    // ------------------------------------------------------------------

    [Fact]
    public void IncludeMembers_NullNestedMember_DoesNotThrow()
    {
        var entity = new OrderEntity
        {
            Customer = null! // deliberately null
        };

        // Must not throw; Name should be null (default! for string).
        var dto = entity.MapToOrderNullDto();
        Assert.Null(dto.Name);
    }

    // ------------------------------------------------------------------
    // Transformers
    // ------------------------------------------------------------------

    [Fact]
    public void IncludeMembers_WithTransformer_TransformerAppliedToFlattenedProperties()
    {
        var entity = new OrderEntity
        {
            Customer = new ContactInfo { Name = "  Eve  ", Email = "  eve@example.com  " }
        };

        // OrderTrimProfile: AddTransformer<string>(v => v.Trim()) + IncludeMembers(s => s.Customer)
        var dto = entity.MapToOrderTrimDto();

        Assert.Equal("Eve",             dto.Name);
        Assert.Equal("eve@example.com", dto.Email);
    }

    // ------------------------------------------------------------------
    // Hooks
    // ------------------------------------------------------------------

    [Fact]
    public void IncludeMembers_WithBeforeMap_HookExecutesAndPropertiesMapped()
    {
        var entity = new OrderEntity
        {
            Customer = new ContactInfo { Name = "Frank" }
        };

        // OrderHookProfile: BeforeMap sets WasHooked = true + IncludeMembers(s => s.Customer)
        var dto = entity.MapToOrderHookDto();

        Assert.True(dto.WasHooked);
        Assert.Equal("Frank", dto.Name);
    }

    // ------------------------------------------------------------------
    // In-place overload
    // ------------------------------------------------------------------

    [Fact]
    public void IncludeMembers_InPlaceOverload_FlattenedPropertiesAssigned()
    {
        var entity = new OrderEntity
        {
            Customer = new ContactInfo { Name = "Grace", Email = "grace@example.com" }
        };
        var existing = new OrderNullDto();

        entity.MapToOrderNullDto(existing);

        Assert.Equal("Grace", existing.Name);
    }
}
