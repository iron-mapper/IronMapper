using IronMapper.Attributes;
using IronMapper.Configuration;
using IronMapper.Exceptions;
using IronMapper.Extensions.DI;
using IronMapper.Integration.Tests.NoOpInPlace;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests.NoOpInPlace
{
    // In-place overloads that change nothing for the caller: a value-type destination, ConvertUsing, only init properties.
    [MapTo(typeof(SpotDto))]
    public class Spot { public int X { get; set; } }
    public struct SpotDto { public int X { get; set; } }

    public class Amount { public decimal Value { get; set; } }
    public class AmountDto { public string Text { get; set; } = string.Empty; }

    [MapTo(typeof(LabelDto))]
    public class Label { public string Name { get; set; } = string.Empty; }
    public class LabelDto { public string Name { get; init; } = string.Empty; }

    public class AmountProfile : MappingProfile
    {
        public AmountProfile() { CreateMap<Amount, AmountDto>().ConvertUsing(a => new AmountDto { Text = a.Value.ToString() }); }
    }
}

namespace IronMapper.Integration.Tests
{
    /// <summary>
    /// <see cref="IMapper.Map{TSource, TDestination}(TSource, TDestination)"/> for a pair whose in-place overload changes
    /// nothing (2.1.0: <c>[Obsolete]</c>, removed in 3.0.0): <see cref="MappingException"/> with the reason instead of
    /// silently doing nothing. A new object is mapped as before.
    /// </summary>
    public class NoOpInPlaceMapperTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(NoOpInPlaceMapperTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        [Fact]
        public void MapInPlace_ValueTypeConvertUsingInitOnly_MappingExceptionWithTheReason()
        {
            var mapper = CreateMapper();

            Assert.Equal(
                "No in-place mapping from Spot to SpotDto: SpotDto is a value type, so the destination is passed by copy and the caller's value does not change. Map to a new object instead.",
                Assert.Throws<MappingException>(() => mapper.Map(new Spot { X = 1 }, new SpotDto())).Message);
            Assert.Equal(
                "No in-place mapping from Amount to AmountDto: the mapping uses ConvertUsing, which only creates a new object. Map to a new object instead.",
                Assert.Throws<MappingException>(() => mapper.Map(new Amount { Value = 2 }, new AmountDto())).Message);
            Assert.Equal(
                "No in-place mapping from Label to LabelDto: LabelDto has no property that an existing object can be updated with (every mapped property is init-only). Map to a new object instead.",
                Assert.Throws<MappingException>(() => mapper.Map(new Label { Name = "a" }, new LabelDto())).Message);
        }

        [Fact]
        public void MapNewObject_AsBefore()
        {
            var mapper = CreateMapper();

            Assert.Equal(1, mapper.Map<Spot, SpotDto>(new Spot { X = 1 }).X);
            Assert.Equal("2", mapper.Map<Amount, AmountDto>(new Amount { Value = 2 }).Text);
            Assert.Equal("a", mapper.Map<Label, LabelDto>(new Label { Name = "a" }).Name);
        }
    }
}
