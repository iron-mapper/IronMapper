using IronMapper.Configuration;
using IronMapper.Extensions.DI;
using IronMapper.Integration.Tests.HooksAndWhen;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests.HooksAndWhen
{
    public class Shift
    {
        public bool Active { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class ShiftDto
    {
        public string Name { get; set; } = string.Empty;
        public string Log { get; set; } = string.Empty;
    }

    public class Slot
    {
        public bool Active { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class SlotDto
    {
        public string Name { get; set; } = string.Empty;
        public string Log { get; set; } = string.Empty;
    }

    public class ShiftProfile : MappingProfile
    {
        public ShiftProfile()
        {
            CreateMap<Shift, ShiftDto>()
                .ForMember(d => d.Log, o => o.Ignore())
                .When(s => s.Active)
                .BeforeMap((s, d) => d.Log += "before;")
                .AfterMap((s, d) => d.Log += "after;");

            // The public in-place overload turned off: IMapper uses the private MapInto of the registration, same body.
            CreateMap<Slot, SlotDto>()
                .ForMember(d => d.Log, o => o.Ignore())
                .When(s => s.Active)
                .AfterMap((s, d) => d.Log += "after;")
                .GenerateMethods(inPlace: false);
        }
    }
}

namespace IronMapper.Integration.Tests
{
    /// <summary>
    /// <see cref="IMapper.Map{TSource, TDestination}(TSource, TDestination)"/> with <c>When</c> and
    /// <c>BeforeMap</c>/<c>AfterMap</c> (issue #62): the same configuration as a new object, as AutoMapper's
    /// <c>Map(source, destination)</c> (up to 2.0.0 the in-place update skipped them).
    /// </summary>
    public class HooksAndWhenMapperTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(HooksAndWhenMapperTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        [Fact]
        public void MapInPlace_HooksRun_WhenFalseLeavesDestination()
        {
            var mapper = CreateMapper();
            var updated = new ShiftDto { Name = "old" };
            var kept = new ShiftDto { Name = "old" };

            mapper.Map(new Shift { Active = true, Name = "new" }, updated);
            mapper.Map(new Shift { Active = false, Name = "new" }, kept);

            Assert.Equal("new", updated.Name);
            Assert.Equal("before;after;", updated.Log);
            Assert.Equal("old", kept.Name);
            Assert.Equal(string.Empty, kept.Log);
        }

        [Fact]
        public void MapInPlace_PublicOverloadTurnedOff_SameBehavior()
        {
            var mapper = CreateMapper();
            var updated = new SlotDto { Name = "old" };
            var kept = new SlotDto { Name = "old" };

            mapper.Map(new Slot { Active = true, Name = "new" }, updated);
            mapper.Map(new Slot { Active = false, Name = "new" }, kept);

            Assert.Equal("new", updated.Name);
            Assert.Equal("after;", updated.Log);
            Assert.Equal("old", kept.Name);
            Assert.Equal(string.Empty, kept.Log);
        }
    }
}
