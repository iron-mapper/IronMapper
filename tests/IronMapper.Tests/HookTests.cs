using IronMapper.Generated;
using IronMapper.Tests.ProfileFixtures;
using Xunit;

namespace IronMapper.Tests;

/// <summary>
/// Verifies that BeforeMap and AfterMap hooks are emitted and called at the correct
/// points during the mapping lifecycle.
/// </summary>
public class HookTests
{
    // -----------------------------------------------------------------------
    // BeforeMap
    // -----------------------------------------------------------------------

    [Fact]
    public void BeforeMap_HookIsInvoked_WasBeforeMapCalledIsTrue()
    {
        var entity = new TrackableEntity { Id = 1, Name = "Test" };

        var dto = entity.MapToTrackableDto();

        Assert.True(dto.WasBeforeMapCalled);
    }

    [Fact]
    public void BeforeMap_CalledBeforePropertyAssignment_DestinationIdIsDefaultAtCallTime()
    {
        // HookOrderProfile captures destination.Id inside BeforeMap.
        // At that point no properties have been assigned yet, so Id should be 0 (default).
        var entity = new HookOrderEntity { Id = 42 };

        var dto = entity.MapToHookOrderDto();

        Assert.Equal(0, dto.IdAtBeforeMap);
    }

    [Fact]
    public void BeforeMap_PropertiesStillMappedAfterHook()
    {
        var entity = new TrackableEntity { Id = 7, Name = "IronMapper" };

        var dto = entity.MapToTrackableDto();

        Assert.Equal(7, dto.Id);
        Assert.Equal("IronMapper", dto.Name);
    }

    // -----------------------------------------------------------------------
    // AfterMap
    // -----------------------------------------------------------------------

    [Fact]
    public void AfterMap_HookIsInvoked_MappedAtIsPopulated()
    {
        var before = System.DateTime.UtcNow;
        var entity = new TrackableEntity { Id = 2, Name = "Hook" };

        var dto = entity.MapToTrackableDto();

        Assert.True(dto.MappedAt >= before);
    }

    [Fact]
    public void AfterMap_CalledAfterPropertyAssignment_DestinationIdEqualsSourceId()
    {
        // HookOrderProfile captures destination.Id inside AfterMap.
        // At that point all properties are assigned, so Id should equal source.Id.
        var entity = new HookOrderEntity { Id = 99 };

        var dto = entity.MapToHookOrderDto();

        Assert.Equal(99, dto.IdAtAfterMap);
    }

    [Fact]
    public void AfterMap_PropertiesCorrectlyMapped()
    {
        var entity = new TrackableEntity { Id = 5, Name = "After" };

        var dto = entity.MapToTrackableDto();

        Assert.Equal(5, dto.Id);
        Assert.Equal("After", dto.Name);
    }

    // -----------------------------------------------------------------------
    // Ordering: BeforeMap → properties → AfterMap
    // -----------------------------------------------------------------------

    [Fact]
    public void HookOrder_BeforeMapSeesEmptyDest_AfterMapSeesFullDest()
    {
        var entity = new HookOrderEntity { Id = 10 };

        var dto = entity.MapToHookOrderDto();

        // Before properties assigned — Id was still 0.
        Assert.Equal(0, dto.IdAtBeforeMap);
        // After properties assigned — Id matches source.
        Assert.Equal(10, dto.IdAtAfterMap);
        // The real Id is also correctly mapped.
        Assert.Equal(10, dto.Id);
    }
}
