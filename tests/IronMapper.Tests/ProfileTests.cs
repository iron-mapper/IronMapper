using IronMapper.Configuration;
using IronMapper.Generated;
using IronMapper.Tests.ProfileFixtures;
using Xunit;

namespace IronMapper.Tests;

/// <summary>
/// Verifies that the source generator correctly processes <see cref="MappingProfile"/>
/// subclasses and produces working extension methods.
/// </summary>
public class ProfileTests
{
    // -----------------------------------------------------------------------
    // ForMember + MapFrom (lambda expression)
    // -----------------------------------------------------------------------

    [Fact]
    public void FluentMapping_ForMemberMapFrom_ConcatenatesSourceProperties()
    {
        var entity = new PersonEntity
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            IsActive = true
        };

        var dto = entity.MapToPersonDto();

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Id);
        Assert.Equal("John Doe", dto.FullName);
    }

    [Fact]
    public void LambdaExpression_MapFromLambda_IsEvaluatedCorrectly()
    {
        var entity = new PersonEntity
        {
            FirstName = "Jane",
            LastName = "Smith",
            IsActive = true
        };

        var dto = entity.MapToPersonDto();

        Assert.Equal("Jane Smith", dto.FullName);
    }

    // -----------------------------------------------------------------------
    // ForMember + Ignore
    // -----------------------------------------------------------------------

    [Fact]
    public void ForMemberIgnore_IgnoredProperty_RemainsDefault()
    {
        var entity = new PersonEntity
        {
            Id = 5,
            FirstName = "Alice",
            LastName = "Wonder",
            IsActive = true
        };

        var dto = entity.MapToPersonDto();

        // Email is configured via .Ignore() in PersonProfile — must remain default.
        Assert.Equal(string.Empty, dto.Email);
    }

    // -----------------------------------------------------------------------
    // When condition
    // -----------------------------------------------------------------------

    [Fact]
    public void WhenCondition_ConditionTrue_ReturnsMappedObject()
    {
        var entity = new PersonEntity { Id = 3, FirstName = "Bob", LastName = "Builder", IsActive = true };

        var dto = entity.MapToPersonDto();

        Assert.NotNull(dto);
    }

    [Fact]
    public void WhenCondition_ConditionFalse_ReturnsDefault()
    {
        var entity = new PersonEntity { Id = 4, FirstName = "Inactive", LastName = "User", IsActive = false };

        var dto = entity.MapToPersonDto();

        // When(src => src.IsActive) — IsActive is false, so mapper returns default(PersonDto) = null.
        Assert.Null(dto);
    }

    // -----------------------------------------------------------------------
    // ReverseMap
    // -----------------------------------------------------------------------

    [Fact]
    public void ReverseMap_ReverseExtensionMethod_MapsDestToSource()
    {
        var dto = new AddressDto { Id = 7, Street = "Main St" };

        var entity = dto.MapToAddressEntity();

        Assert.NotNull(entity);
        Assert.Equal(7, entity.Id);
        Assert.Equal("Main St", entity.Street);
    }

    [Fact]
    public void ReverseMap_ForwardMapping_StillWorks()
    {
        var entity = new AddressEntity { Id = 8, Street = "Oak Ave" };

        var dto = entity.MapToAddressDto();

        Assert.Equal(8, dto.Id);
        Assert.Equal("Oak Ave", dto.Street);
    }

    // -----------------------------------------------------------------------
    // Simple CreateMap (no fluent config) via profile
    // -----------------------------------------------------------------------

    [Fact]
    public void SimpleProfileMapping_MatchingProperties_AreCopiedByName()
    {
        var src = new SimpleSource { Value = 42, Label = "hello" };

        var dest = src.MapToSimpleDest();

        Assert.Equal(42, dest.Value);
        Assert.Equal("hello", dest.Label);
    }
}
