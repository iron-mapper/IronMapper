using System.Collections.Generic;
using System.Linq;
using IronMapper.Configuration;
using IronMapper.Extensions.DI;
using IronMapper.Integration.Tests.NestedObjects;
using IronMapper.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IronMapper.Integration.Tests.NestedObjects
{
    public class Warehouse { public string Code { get; set; } = string.Empty; public Location Location { get; set; } = new(); }
    public class WarehouseDto { public string Code { get; set; } = string.Empty; public LocationDto Location { get; set; } = new(); }
    public class Location { public string City { get; set; } = string.Empty; }
    public class LocationDto { public string City { get; set; } = string.Empty; }

    // In-place overloads turned off for both pairs: IMapper.Map(source, destination) goes through the private MapInto and
    // the in-place …Core of each pair.
    public class Depot { public string Code { get; set; } = string.Empty; public Site Site { get; set; } = new(); }
    public class DepotDto { public string Code { get; set; } = string.Empty; public SiteDto Site { get; set; } = new(); }
    public class Site { public string City { get; set; } = string.Empty; }
    public class SiteDto { public string City { get; set; } = string.Empty; }

    public class WarehouseProfile : MappingProfile
    {
        public WarehouseProfile()
        {
            CreateMap<Location, LocationDto>();
            CreateMap<Warehouse, WarehouseDto>();
            CreateMap<Site, SiteDto>().GenerateMethods(inPlace: false);
            CreateMap<Depot, DepotDto>().GenerateMethods(inPlace: false);
        }
    }
}

namespace IronMapper.Integration.Tests
{
    /// <summary>Nested objects mapped automatically, through <see cref="IMapper"/> (the generated registry).</summary>
    public class NestedObjectMapperTests
    {
        private static IMapper CreateMapper()
        {
            var services = new ServiceCollection();
            services.AddIronMapper(typeof(NestedObjectMapperTests).Assembly);
            return services.BuildServiceProvider().GetRequiredService<IMapper>();
        }

        [Fact]
        public void Map_NewObjectInPlaceRuntimeTypeAndCollection()
        {
            var mapper = CreateMapper();
            Warehouse Create(string city) => new() { Code = "w", Location = new Location { City = city } };

            var target = new WarehouseDto();
            var location = target.Location;
            mapper.Map(Create("Bergen"), target);

            Assert.Equal("Oslo", mapper.Map<Warehouse, WarehouseDto>(Create("Oslo")).Location.City);
            Assert.Same(location, target.Location);
            Assert.Equal("Bergen", target.Location.City);
            Assert.Equal("Tromsø", mapper.Map<WarehouseDto>((object)Create("Tromsø")).Location.City);
            Assert.Equal("Narvik", mapper.Map<List<WarehouseDto>>(new List<Warehouse> { Create("Narvik") }).Single().Location.City);
        }

        [Fact]
        public void MapInPlace_InPlaceOverloadTurnedOff_UpdatesTheExistingNestedObject()
        {
            var mapper = CreateMapper();
            var target = new DepotDto();
            var site = target.Site;

            mapper.Map(new Depot { Code = "d", Site = new Site { City = "Bodø" } }, target);

            Assert.Equal("d", target.Code);
            Assert.Same(site, target.Site);
            Assert.Equal("Bodø", site.City);
        }
    }
}
