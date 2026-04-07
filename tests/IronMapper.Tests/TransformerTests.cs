using IronMapper.Generated;
using IronMapper.Tests.ProfileFixtures;
using Xunit;

namespace IronMapper.Tests;

/// <summary>Tests for profile-level AddTransformer functionality.</summary>
public class TransformerTests
{
    [Fact]
    public void AddTransformer_StringProperty_ValueIsTrimmed()
    {
        var entity = new TransformerEntity { Name = "  hello  ", Price = 1.0m };
        var dto = entity.MapToTransformerDto();
        Assert.Equal("hello", dto.Name);
    }

    [Fact]
    public void AddTransformer_DecimalProperty_ValueIsRounded()
    {
        var entity = new TransformerEntity { Name = "x", Price = 1.2345m };
        var dto = entity.MapToTransformerDto();
        Assert.Equal(1.23m, dto.Price);
    }

    [Fact]
    public void AddTransformer_DoesNotAffectForMemberProperties()
    {
        var entity = new TransformerForMemberEntity { Name = "  trimme  ", Tag = "  raw  " };
        var dto = entity.MapToTransformerForMemberDto();
        // Name is name-matched → transformer applies → trimmed
        Assert.Equal("trimme", dto.Name);
        // Tag is ForMember-configured → transformer does NOT apply → raw lambda result
        Assert.Equal("  raw  _raw", dto.Tag);
    }
}
